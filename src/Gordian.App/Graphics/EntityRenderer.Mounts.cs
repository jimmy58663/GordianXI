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
        /// How far the riding pack lifts the rider's head over its standing height (Hume male <c>chi</c>: hips 1.05 to 1.78
        /// yalms), for the name plate and target cursor of a rider that stands above its mount's own overhead point.
        /// </summary>
        internal const float RiderSeatRaise = 0.75f;

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

        /// <summary>
        /// How far the mounted character is moved up (positive) or down from the shared origin to sit on its mount's back
        /// (<see cref="MountModels.SeatOffset"/>, cached per rider and mount); 0 on foot or without the models.
        /// </summary>
        private static float RiderSeatOffset(WorldEntity entity, EntityModel character, ResourceManager? resources)
        {
            if (resources == null) return 0f;
            var rider = RiderOrSelf(entity, character, resources);
            if (ReferenceEquals(rider, character) || !resources.TryLoadMountModel(entity.Mount, out var mount) || mount == null) return 0f;
            return resources.GetSeatOffset(rider, mount);
        }

        /// <summary>
        /// Draws the mount of a mounted entity at the rider's origin and heading, unscaled (retail draws the same mount
        /// under every race, Mounts.mp4), animated by its own state with the rider's movement category. Leaves its joint
        /// palette for the next frame; frees it when the entity no longer rides. Returns the draw calls made.
        /// </summary>
        private int DrawMount(CommandList cl, WorldEntity entity, ResourceManager? resources, in ZoneSceneUniform riderUniform,
            Matrix4x4 headingRot, Vector3 pos, bool faded, bool engaged, bool isLocalPlayer, float deltaSeconds, bool draw)
        {
            uint key = MountPaletteKey(entity.ServerId);
            if (!entity.Mount.IsMounted || resources == null || !resources.TryLoadMountModel(entity.Mount, out var mount)
                || mount?.Skeleton is not { Count: > 0 })
            {
                if (_jointPaletteByEntity.TryRemove(key, out var stale)) stale.Dispose();
                return 0;
            }
            var gpuModel = GetOrUploadGpuModel(mount);
            if (gpuModel.Submeshes.Count == 0) return 0;

            var category = AnimationStateClassifier.Classify(entity, engaged, isLocalPlayer);
            if (!entity.IsMotionStopped || entity.MountAnimation.CurrentClip == null)
            {
                entity.MountAnimation.Advance(entity.MountAnimation.ClaimFrameTime(deltaSeconds), category, 0, mount);
            }
            var palette = _jointPaletteByEntity.GetOrAdd(key, _ => CreateJointPalette());
            UpdateJointPalette(cl, palette.Buffer, mount, entity.MountAnimation, null, Vector2.Zero, null, out _);
            if (!draw) return 0;

            var uniform = riderUniform;
            uniform.World = EntityRotMatrix * headingRot * Matrix4x4.CreateTranslation(pos);
            cl.UpdateBuffer(_entityUniformBuffer, 0, ref uniform);
            cl.SetPipeline(faded ? _skinnedDepthPipeline : _skinnedPipeline);
            cl.SetGraphicsResourceSet(0, _entityResourceSet);
            cl.SetGraphicsResourceSet(2, palette.Set);
            int draws = DrawSubmeshes(cl, gpuModel, 0, uniform);
            // The Fenrir mount carries an avatar's blur aura (Section 0x5E), drawn as on the avatar (#82).
            if (!faded && mount.BlurAura is { } aura) QueueBlurAura(uniform, palette.Set, gpuModel, 0, aura, key);
            if (faded)
            {
                cl.SetPipeline(_skinnedFadePipeline);
                cl.SetGraphicsResourceSet(0, _entityResourceSet);
                cl.SetGraphicsResourceSet(2, palette.Set);
                draws += DrawSubmeshes(cl, gpuModel, 0, uniform);
            }
            else if (!entity.HidesShadow)
            {
                QueueShadow(uniform, palette.Set, gpuModel, 0, pos, isLocalPlayer, CursorHeight(mount));
            }
            return draws;
        }
    }
}
