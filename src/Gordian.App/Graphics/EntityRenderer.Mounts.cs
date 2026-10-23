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

        private static CharacterRace RaceOf(WorldEntity entity) => (CharacterRace)((entity.Appearance.FaceModel >> 8) & 0xFF);

        /// <summary>
        /// The model a mounted character is animated with (same meshes and GPU copy): on a chocobo its race's chocobo riding
        /// pack; on another mount the pose that mount gives its race (<see cref="MountSeating"/>, round 5: the Magic Pot's
        /// riders stand, the Spectral Chair's sit) from its race's mount rider pack. Else <paramref name="character"/> itself
        /// (on foot, a costume, a race without the packs).
        /// </summary>
        private static EntityModel RiderOrSelf(WorldEntity entity, EntityModel character, ResourceManager? resources)
        {
            if (!entity.Mount.IsMounted || resources == null || !character.IsCharacter || entity.Appearance.ModelId != 0) return character;
            var race = RaceOf(entity);
            if (!entity.Mount.RidesChocobo)
            {
                byte pose = resources.GetRideData(entity.Mount)?.PoseFor(race) ?? MountSeating.StraddlePose;
                if (resources.TryGetPoseRiderModel(character, race, pose, out var posed) && posed != null) return posed;
            }
            return resources.TryGetRiderModel(character, race, out var rider) && rider != null ? rider : character;
        }

        /// <summary>
        /// A mount prepared for this frame: its model and GPU copy, posed palette, scale, where the rider goes and how far the
        /// rider's name plate is raised.
        /// </summary>
        private readonly record struct MountRide(EntityModel Mount, GpuEntityModel Gpu, JointPaletteEntry Palette, uint Key, float Scale,
            Vector3 RiderOffset, float PlateLift);

        /// <summary>
        /// Advances and poses the mount of a mounted entity (its own state, the rider's movement category) and works out the
        /// rider's place (#87, round 5, all from the mount's data, <see cref="MountSeating"/>):
        /// <list type="bullet">
        /// <item>A chocobo is scaled by the rider's race (<see cref="MountModels.MountScale"/>) and the chocobo riding pack fits it
        /// as authored.</item>
        /// <item>Any other mount is drawn at its own size for the rider's build (Info bytes 10-12). The rider holds the pose the
        /// mount gives its race, its hips on the race's seat reference (r48 + race) in the mount's current pose, so it bobs with
        /// the mount.</item>
        /// </list>
        /// Frees the mount's palette and returns null when the entity does not ride.
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
            // The straddling pack's chocobo is scaled by the race (the pose rider's idle is the straddle's on a straddle).
            float raceScale = riding ? resources.GetMountScale(rider!) : 1f;
            var race = RaceOf(entity);
            if (entity.Mount.RidesChocobo)
            {
                // The name plate: the rider's own overhead point raised by half its seat's height (round 3 H17).
                var chocoboSeat = MountModels.ReferencePoint(mount, pose, MountModels.ChocoboSeatReference);
                return new MountRide(mount, gpuModel, palette, key, raceScale, Vector3.Zero, 0.5f * raceScale * -chocoboSeat.Y);
            }

            var data = resources.GetRideData(entity.Mount);
            float scale = data?.ScaleFor(race) ?? 1f;
            byte riderPose = data?.PoseFor(race) ?? MountSeating.StraddlePose;
            int seatReference = MountSeating.SeatReference(race);
            var seat = scale * (MountModels.HasReference(mount, seatReference)
                ? MountModels.ReferencePoint(mount, pose, seatReference)
                : MountModels.ReferencePoint(mount, pose, MountModels.ChocoboSeatReference));
            if (!riding) return new MountRide(mount, gpuModel, palette, key, scale, Vector3.Zero, 0f);
            // Whatever the pose (a straddle, standing on the Magic Pot, a chair), the rider's hips go on the race's seat: the
            // references sit just over the raptor's back at one height for every race, and on the Magic Pot by each race's
            // standing hip height over the lid. Model space is Y-down: heights are -Y.
            var offset = seat - resources.GetRiderHips(rider!);
            // The name plate rides with the rider: raised by how far its origin moved, plus half the seat's height for a
            // straddle (round 3 H17), whose origin is the ground under a chocobo.
            float lift = -offset.Y + (riderPose == MountSeating.StraddlePose ? 0.5f * raceScale * MountModels.ChocoboRestSeatHeight : 0f);
            return new MountRide(mount, gpuModel, palette, key, scale, offset, lift);
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
