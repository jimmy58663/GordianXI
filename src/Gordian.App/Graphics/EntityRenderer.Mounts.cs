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
            Matrix4x4 RiderShift, float PlateLift);

        /// <summary>
        /// How much higher a straddling rider's name plate sits over its head than on foot (#87 round 9, yalms). The plate rises
        /// with the seated head (<see cref="MountModels.SeatedHeadRise"/>) plus this. Rounds 2-8 raised it by a share of the
        /// seat's height, which can't suit every build: the Tarutaru's head rises most when seated, its standing head being
        /// lowest. Half the seat was a little low for the tall races (round 3); 0.8 of it was about 25% too high for them and
        /// still low for the Tarutaru (round 8 H44). With this margin, a Hume's plate rises 0.86, 25% under round 8's 1.15.
        /// </summary>
        private const float StraddlePlateMargin = 0.13f;

        /// <summary>
        /// Advances and poses the mount of a mounted entity (its own state, the rider's movement category) and works out the
        /// rider's place (#87, round 5, all from the mount's data, <see cref="MountSeating"/>):
        /// <list type="bullet">
        /// <item>A chocobo is scaled by the rider's race (<see cref="MountModels.MountScale"/>) and the chocobo riding pack fits it
        /// as authored.</item>
        /// <item>Any other mount is drawn at its own size (Info byte 10) times the race's chocobo factor (round 7). The rider holds the pose the
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
                // The name plate: the rider's own overhead point raised with its seated head (round 9).
                float plate = riding ? resources.GetRiderHeadRise(rider!) + StraddlePlateMargin : 0f;
                return new MountRide(mount, gpuModel, palette, key, raceScale, Matrix4x4.Identity, plate);
            }

            // Every mount takes the chocobo's race factor (round 7: retail's Fenrir grows from the Tarutaru to the Galka,
            // image-13.png) times its own size (Info byte 10).
            var data = resources.GetRideData(entity.Mount);
            float raceFactor = character is { IsCharacter: true } ? resources.GetRaceMountScale(character, race) : 1f;
            float scale = (data?.BaseScale ?? 1f) * raceFactor;
            byte riderPose = data?.PoseFor(race) ?? MountSeating.StraddlePose;
            int seatReference = MountSeating.SeatReference(race);
            var seat = scale * (MountModels.HasReference(mount, seatReference)
                ? MountModels.ReferencePoint(mount, pose, seatReference)
                : MountModels.ReferencePoint(mount, pose, MountModels.ChocoboSeatReference));
            if (!riding) return new MountRide(mount, gpuModel, palette, key, scale, Matrix4x4.Identity, 0f);
            // Whatever the pose (a straddle, standing on the Magic Pot, a chair), the rider's own seat (its reference 107, just
            // under the hips) goes on the race's seat: the references sit just over the raptor's back at one height for every
            // race, and on the Magic Pot by each race's standing hip height over the lid. Model space is Y-down: heights are -Y.
            var riderSeat = resources.GetRiderSeat(rider!);
            var offset = seat - riderSeat;
            // The rider turns about its seat by the race's moun yaw (round 8: the tiger's sidesaddle, the Morbol's slight turn).
            // Model space is EntityRotMatrix's (X flipped about), so +Y here is clockwise from above in the world, like the heading.
            float yaw = data?.YawFor(race) ?? 0f;
            var shift = yaw == 0f ? Matrix4x4.CreateTranslation(offset)
                : Matrix4x4.CreateTranslation(-riderSeat) * Matrix4x4.CreateRotationY(yaw) * Matrix4x4.CreateTranslation(seat);
            // The name plate rides with the rider: raised by how far its origin moved, plus, for a straddle (whose origin is the
            // ground under a chocobo), by how far its head rises seated (round 9).
            float lift = -offset.Y + (riderPose == MountSeating.StraddlePose ? resources.GetRiderHeadRise(rider!) + StraddlePlateMargin : 0f);
            return new MountRide(mount, gpuModel, palette, key, scale, shift, lift);
        }

        /// <summary>
        /// Draws a prepared mount at the entity's origin and heading, scaled for its rider, with event fades, its shadow and
        /// its blur aura (only Fenrir has one). Returns the draw calls made.
        /// </summary>
        private int DrawMount(CommandList cl, WorldEntity entity, in MountRide ride, in ZoneSceneUniform riderUniform,
            Matrix4x4 headingRot, Vector3 pos, bool faded, bool isLocalPlayer, float deltaSeconds)
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
                QueueShadow(uniform, ride.Palette.Set, ride.Gpu, 0, pos, isLocalPlayer, CursorHeight(ride.Mount) * ride.Scale, ride.Key, deltaSeconds);
            }
            return draws;
        }
    }
}
