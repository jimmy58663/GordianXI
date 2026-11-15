// src/Gordian.App/Graphics/ZoneTerrainRenderer.Vessels.cs
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;
using Gordian.Core.Diagnostics;
using Gordian.Engine.Graphics;
using Gordian.Core.Resources;
using Gordian.Core.Resources.Graphics;
using Gordian.Core.Resources.Models;
using Gordian.Core.World;
using NeoVeldrid;

namespace Gordian.App.Graphics
{
    /// <summary>
    /// Vessels (#403): a voyage zone's own vessel (drawn in the passengers' frame while the scenery moves past, see
    /// <see cref="VoyageSchedule"/>) and the ships that call at a dock zone (S2C 0x00E SubKind 4), drawn with the vessel
    /// of the zone they sail to (<see cref="ShipModels"/>) at the ship entity's position and heading.
    /// </summary>
    public sealed partial class ZoneTerrainRenderer
    {
        // The ship models this renderer holds a reference to (ZoneModelCache, #585), by ship zone; null when the zone has no vessel.
        private readonly Dictionary<int, ZoneModelCache.SharedModel?> _transportVessels = new();
        private readonly Dictionary<int, Task<(ZoneModelCache.ModelKey Key, IReadOnlyList<MeshGroup>? Groups, IReadOnlyDictionary<string, DecodedTexture>? Textures)>> _transportLoads = new();

        /// <summary>Ship entities drawn with a vessel model this frame (tests and diagnostics).</summary>
        internal int TransportVesselsDrawn { get; private set; }

        /// <summary>
        /// The vessel's yaw for a ship entity's heading: the barge at Carpenters' Landing's central dock (LandSandBoat heading
        /// 160) lies along the pier with its bow downstream. Display space turns about Y against the wire heading.
        /// </summary>
        internal static Matrix4x4 TransportVesselWorld(Vector3 internalPosition, float headingRadians) =>
            Matrix4x4.CreateRotationY(TransportYawOffset - headingRadians) *
            Matrix4x4.CreateTranslation(new Vector3(-internalPosition.X, -internalPosition.Y, internalPosition.Z));

        /// <summary>The yaw between a vessel's own frame and an entity heading of 0 (see <see cref="TransportVesselWorld"/>).</summary>
        internal static float TransportYawOffset { get; set; } = -MathF.PI * 0.5f;

        /// <summary>
        /// Draws placed submeshes with one world transform, in the scene uniform of each one's environment (restored after
        /// each draw), with the given texture table.
        /// </summary>
        private void DrawPlacedSubmeshes(List<GpuSubmesh> submeshes, Matrix4x4 world, IReadOnlyDictionary<string, DecodedTexture>? textures,
            ZoneSceneUniform sceneUniform, BoundingFrustum frustum, bool deferred, ref int draws, ref int visible, ref int culled,
            Dictionary<string, (DeviceBuffer Buffer, ResourceSet Set)>? scenes = null, Dictionary<string, ZoneSceneUniform>? uniforms = null)
        {
            if (submeshes.Count == 0) return;
            // A building interior lights its placements by its own sub-environments (#68); everything else by the zone's.
            scenes ??= _subEnvironmentScenes;
            uniforms ??= _subEnvironmentUniforms;
            foreach (var submesh in submeshes)
            {
                // Blended water and the blended parts of alpha-tested meshes draw after the entities, as the zone's do
                // (round 1: the Manaclipper's lamp glows drew as solid yellow panels in the opaque pass).
                if (submesh.Pass.IsDeferred() != deferred) continue;
                TransformBounds(submesh.MinBounds, submesh.MaxBounds, world, out var min, out var max);
                if (!frustum.IntersectsBox(min, max))
                {
                    culled++;
                    continue;
                }

                (DeviceBuffer Buffer, ResourceSet Set) subScene = default;
                bool sub = submesh.EnvironmentId.Length > 0 && scenes.TryGetValue(submesh.EnvironmentId, out subScene);
                var buffer = sub ? subScene.Buffer : _sceneUniformBuffer;
                var baseUniform = sub && uniforms.TryGetValue(submesh.EnvironmentId, out var subUniform) ? subUniform : sceneUniform;
                var moved = baseUniform;
                moved.World = world;
                _commandList.UpdateBuffer(buffer, 0, ref moved);

                var pass = submesh.Pass;
                var pipeline = pass switch
                {
                    ZoneSubmeshPass.DeferredBlend or ZoneSubmeshPass.Water => _blendPipeline,
                    ZoneSubmeshPass.BlendDecal => _terrainBlendPipeline,
                    ZoneSubmeshPass.Cutout => _cutoutPipeline,
                    _ => _pipeline
                };
                _commandList.SetPipeline(ForSubmesh(pipeline, submesh.NoCull));
                _commandList.SetGraphicsResourceSet(0, sub ? subScene.Set : _sceneResourceSet);
                _commandList.SetGraphicsResourceSet(1, _textureCache.GetOrCreateResourceSet(submesh.TextureName, textures));
                _commandList.SetGraphicsResourceSet(2, submesh.LightSet ?? _noLightSet);
                _commandList.SetVertexBuffer(0, submesh.VertexBuffer);
                _commandList.SetIndexBuffer(submesh.IndexBuffer, IndexFormat.UInt16);
                _commandList.DrawIndexed(submesh.IndexCount, 1, 0, 0, 0);
                _commandList.UpdateBuffer(buffer, 0, ref baseUniform);
                draws++;
                visible++;
            }
        }

        /// <summary>
        /// Draws every ship entity whose ship number has a vessel model. A model's zone is read in the background the first
        /// time a ship needs it (a zone DAT parse); the ship appears when it is ready.
        /// </summary>
        private void DrawTransportVessels(IEnumerable<WorldEntity>? entities, ResourceManager? resourceManager, ZoneSceneUniform sceneUniform,
            BoundingFrustum frustum, bool deferred, ref int draws, ref int visible, ref int culled)
        {
            if (!deferred) TransportVesselsDrawn = 0;
            if (entities == null || resourceManager == null) return;
            foreach (var entity in entities)
            {
                if (entity.Type != EntityType.Ship || entity.ShipNumber == 0 || !entity.IsSpawned || entity.IsHidden) continue;
                if (!ShipModels.TryGetVesselZone(entity.ShipNumber, out int vesselZone)) continue;
                // A voyage zone draws its own vessel; a ship entity there is the same vessel.
                if (LoadedZone?.Vessel is { IsVoyage: true } && LoadedZone.ZoneId == vesselZone) continue;
                var vessel = GetTransportVessel(vesselZone, resourceManager);
                if (vessel == null) continue;
                float heading = entity.RenderHeadingRadians != 0f || entity.Direction != 0 ? entity.RenderHeadingRadians : entity.HeadingRadians;
                DrawPlacedSubmeshes(vessel.Submeshes, TransportVesselWorld(entity.Position, heading), vessel.Textures, sceneUniform, frustum, deferred, ref draws, ref visible, ref culled);
                if (!deferred) TransportVesselsDrawn++;
            }
        }

        /// <summary>
        /// A ship zone's vessel on the GPU with this renderer's reference, from the device's shared copy (#585). The first
        /// time any window needs it, the vessel is read in the background from a copy of the ship zone of its own
        /// (<see cref="ResourceManager.TryReadZoneVessel"/>), uploaded once, and that copy dropped.
        /// </summary>
        private ZoneModelCache.SharedModel? GetTransportVessel(int zoneId, ResourceManager resourceManager)
        {
            if (_transportVessels.TryGetValue(zoneId, out var ready)) return ready;
            var models = _shared.ZoneModels;
            if (!_transportLoads.TryGetValue(zoneId, out var load))
            {
                var key = new ZoneModelCache.ModelKey(ZoneModelCache.ModelKind.Vessel, zoneId, resourceManager.CacheGeneration);
                if (models.TryAcquire(key, out var shared))
                {
                    _transportVessels[zoneId] = shared;
                    return shared;
                }
                _transportLoads[zoneId] = Task.Run(() =>
                {
                    if (!resourceManager.TryReadZoneVessel(zoneId, out var vessel, out var textures))
                    {
                        GordianLog.Warn(LogCategories.Graphics, $"Ship model: zone {zoneId} has no vessel.");
                        return (key, (IReadOnlyList<MeshGroup>?)null, (IReadOnlyDictionary<string, DecodedTexture>?)null);
                    }
                    return (key, vessel.MeshGroups, VesselTextures(vessel.MeshGroups, textures));
                });
                return null;
            }
            if (!load.IsCompleted) return null;
            _transportLoads.Remove(zoneId);

            ZoneModelCache.SharedModel? model = null;
            if (load.IsCompletedSuccessfully && load.Result is { Groups: { } groups, Textures: { } vesselTextures } result)
            {
                // Its own copy of the ship zone: nothing else holds it, so it needs no release after the upload.
                model = models.Acquire(result.Key, zoneId, groups, vesselTextures);
                if (model == null) return null;
                GordianLog.Info(LogCategories.Graphics, $"Ship model: the vessel of zone {zoneId} ({model.Submeshes.Count} submeshes) is ready.");
            }
            _transportVessels[zoneId] = model;
            return model;
        }

        /// <summary>The textures a vessel's submeshes name, as <see cref="GpuTextureCache.Resolve"/> finds them in the ship zone's table.</summary>
        internal static Dictionary<string, DecodedTexture> VesselTextures(IEnumerable<MeshGroup> groups, IReadOnlyDictionary<string, DecodedTexture> zoneTextures)
        {
            var textures = new Dictionary<string, DecodedTexture>(StringComparer.OrdinalIgnoreCase);
            foreach (var group in groups)
            {
                string name = group.TextureName.Trim();
                if (name.Length == 0 || textures.ContainsKey(name)) continue;
                if (GpuTextureCache.Resolve(name, zoneTextures) is { } texture) textures[name] = texture;
            }
            return textures;
        }

        /// <summary>Gives back this renderer's ship model references (the models stay on the GPU for other windows).</summary>
        private void DisposeTransportVessels()
        {
            foreach (var vessel in _transportVessels.Values) _shared.ZoneModels.Release(vessel);
            _transportVessels.Clear();
            _transportLoads.Clear();
        }
    }
}
