// src/Gordian.App/Graphics/EntityRenderer.Mounts.cs
// Mounted players (#87): the rider plays its race's riding pack on its own meshes, and the mount is drawn as a second
// model at the same origin and heading. See Gordian.Core.Resources.MountModels for the data.
using System;
using System.Numerics;
using Gordian.Core.Animation;
using Gordian.Core.Resources;
using Gordian.Core.Resources.Models;
using Gordian.Core.Resources.Tables;
using Gordian.Core.World;
using NeoVeldrid;

namespace Gordian.App.Graphics
{
    public sealed partial class EntityRenderer
    {
        /// <summary>The mount's joint palette key: the rider's server id with the top bit set (no entity id uses it).</summary>
        private static uint MountPaletteKey(uint serverId) => serverId ^ 0x8000_0000u;

        /// <summary>
        /// The model a mounted character is animated with: its riding variant (same meshes and GPU copy, the riding clips),
        /// else <paramref name="character"/> itself (on foot, a costume, a race without a riding pack).
        /// </summary>
        private static EntityModel RiderOrSelf(WorldEntity entity, EntityModel character, ResourceManager? resources)
        {
            if (!entity.Mount.IsMounted || resources == null || !character.IsCharacter || entity.Appearance.ModelId != 0) return character;
            var race = (CharacterRace)((entity.Appearance.FaceModel >> 8) & 0xFF);
            return resources.TryGetRiderModel(character, race, out var rider) && rider != null ? rider : character;
        }

        /// <summary>A mount prepared for this frame: its model and GPU copy, posed palette, scale, where the rider sits and the seat's height.</summary>
        private readonly record struct MountRide(EntityModel Mount, GpuEntityModel Gpu, JointPaletteEntry Palette, uint Key, float Scale,
            Vector3 RiderOffset, float SeatHeight);

        /// <summary>The chocobo the riding packs are authored on (its seat is the reference other mounts replace).</summary>
        private static readonly MountAppearance RidingChocobo = MountAppearance.FromWire(MountAppearance.StatusChocobo, 0, 1);

        /// <summary>
        /// Advances and poses the mount of a mounted entity (its own state, the rider's movement category) and works out the
        /// rider's place (#87, round 2): the mount is scaled by the rider's race (<see cref="MountModels.MountScale"/>), and
        /// the rider, whose pack moves with a chocobo's seat, is moved by this mount's seat (skeleton reference 1, posed:
        /// it bobs with the mount, idle included) less the chocobo's at the rider's clip time, and up by how much higher
        /// this mount's back stands over its seat than the chocobo's (<see cref="MountModels.BackHeight"/>), scaled. On the
        /// chocobo itself the pack fits as authored. Frees the mount's palette and returns null when the entity does not ride.
        /// </summary>
        private MountRide? PrepareMount(CommandList cl, WorldEntity entity, EntityModel? character, ResourceManager? resources,
            bool engaged, bool isLocalPlayer, float deltaSeconds)
        {
            uint key = MountPaletteKey(entity.ServerId);
            if (!entity.Mount.IsMounted || resources == null || !resources.TryLoadMountModel(entity.Mount, out var mount)
                || mount?.Skeleton is not { Count: > 0 })
            {
                if (_jointPaletteByEntity.TryRemove(key, out var stale)) stale.Dispose();
                return null;
            }
            var gpuModel = GetOrUploadGpuModel(mount);
            if (gpuModel.Submeshes.Count == 0) return null;

            var category = AnimationStateClassifier.Classify(entity, engaged, isLocalPlayer);
            if (!entity.IsMotionStopped || entity.MountAnimation.CurrentClip == null)
            {
                entity.MountAnimation.Advance(entity.MountAnimation.ClaimFrameTime(deltaSeconds), category, 0, mount);
            }
            var palette = _jointPaletteByEntity.GetOrAdd(key, _ => CreateJointPalette());
            UpdateJointPalette(cl, palette.Buffer, mount, entity.MountAnimation, null, Vector2.Zero, null, out var pose);

            var rider = character != null ? RiderOrSelf(entity, character, resources) : null;
            bool riding = rider != null && !ReferenceEquals(rider, character);
            // The pack's chocobo is scaled by the race; the mount by the race and its own size, its model's Info scale (round 4).
            float raceScale = riding ? resources.GetMountScale(rider!) : 1f;
            float scale = raceScale * resources.GetMountBaseScale(entity.Mount);
            var seat = MountModels.SeatPoint(mount, pose) + new Vector3(MountModels.SeatShift(entity.Mount), 0f, 0f);
            var offset = Vector3.Zero;
            float back = 0f;
            if (riding && !entity.Mount.RidesChocobo && resources.TryLoadMountModel(RidingChocobo, out var chocobo) && chocobo != null)
            {
                var animation = entity.Animation;
                var expected = MountModels.ChocoboSeat(chocobo, animation.CurrentClip, animation.ElapsedSeconds);
                if (animation.IsBlending && animation.PreviousClip != null)
                {
                    expected = Vector3.Lerp(MountModels.ChocoboSeat(chocobo, animation.PreviousClip, animation.PreviousElapsedSeconds),
                        expected, animation.BlendWeight);
                }
                // Model space is Y-down: a higher back moves the rider to -Y.
                // Model space is Y-down: the back's height over the seat raises the rider (-Y). The pack expects the chocobo's
                // seat and back at the race scale; this mount's are at its own scale.
                back = resources.GetBackHeight(mount, MountModels.SeatShift(entity.Mount));
                var mountBack = scale * (seat - new Vector3(0f, back, 0f));
                var chocoboBack = raceScale * (expected - new Vector3(0f, resources.GetBackHeight(chocobo), 0f));
                offset = mountBack - chocoboBack;
            }
            // The name plate: the rider's own overhead point raised by half its seat's height (round 3 H17; the full height,
            // round 2, sat a little too high against retail's image-9.png). For another mount the seat is its back as placed.
            float seatHeight = riding && !entity.Mount.RidesChocobo ? raceScale * MountModels.ChocoboRestSeatHeight - offset.Y : scale * -seat.Y;
            return new MountRide(mount, gpuModel, palette, key, scale, offset, 0.5f * seatHeight);
        }

        /// <summary>
        /// Draws a prepared mount at the entity's origin and heading, scaled for its rider, with event fades, its shadow and
        /// its blur aura (only Fenrir has one). Returns the draw calls made.
        /// </summary>
        private int DrawMount(CommandList cl, WorldEntity entity, in MountRide ride, in ZoneSceneUniform riderUniform,
            Matrix4x4 headingRot, Vector3 pos, bool faded, bool isLocalPlayer)
        {
            var uniform = riderUniform;
            uniform.World = Matrix4x4.CreateScale(ride.Scale) * EntityRotMatrix * headingRot * Matrix4x4.CreateTranslation(pos);
            cl.UpdateBuffer(_entityUniformBuffer, 0, ref uniform);
            cl.SetPipeline(faded ? _skinnedDepthPipeline : _skinnedPipeline);
            cl.SetGraphicsResourceSet(0, _entityResourceSet);
            cl.SetGraphicsResourceSet(2, ride.Palette.Set);
            int draws = DrawSubmeshes(cl, ride.Gpu, 0, uniform);
            // The Fenrir mount carries an avatar's blur aura (Section 0x5E), drawn as on the avatar (#82).
            if (!faded && ride.Mount.BlurAura is { } aura) QueueBlurAura(uniform, ride.Palette.Set, ride.Gpu, 0, aura, ride.Key);
            if (faded)
            {
                cl.SetPipeline(_skinnedFadePipeline);
                cl.SetGraphicsResourceSet(0, _entityResourceSet);
                cl.SetGraphicsResourceSet(2, ride.Palette.Set);
                draws += DrawSubmeshes(cl, ride.Gpu, 0, uniform);
            }
            else if (!entity.HidesShadow)
            {
                QueueShadow(uniform, ride.Palette.Set, ride.Gpu, 0, pos, isLocalPlayer, CursorHeight(ride.Mount) * ride.Scale);
            }
            return draws;
        }
    }
}
