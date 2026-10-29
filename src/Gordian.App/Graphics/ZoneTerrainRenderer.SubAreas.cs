// src/Gordian.App/Graphics/ZoneTerrainRenderer.SubAreas.cs
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
using Gordian.Core.World.Collision;
using NeoVeldrid;

namespace Gordian.App.Graphics
{
    /// <summary>
    /// Building interiors (sub-areas, #68, <see cref="ZoneSubAreas"/>): the interiors near the displayed player
    /// (<see cref="SubAreaTracker.NearSubAreas"/>) are drawn with the zone and their placeholders hidden. A placeholder is not
    /// the facade but a plug filling the room behind it (Lower Jeuno's <c>r_syoku</c>: hiding it changes nothing seen from the
    /// street, and from inside it is a dark block where the tavern is; offscreen, 2026-10-20), so hiding it as soon as the
    /// interior is near shows the lit room through the open door, as the retail capture does. Interior DATs are read in the
    /// background the first time they come near and uploaded for this renderer.
    /// </summary>
    public sealed partial class ZoneTerrainRenderer
    {
        /// <summary>A building interior on the GPU: its submeshes and its textures, the parent zone's behind them.</summary>
        private sealed class SubAreaInterior
        {
            public List<GpuSubmesh> Submeshes { get; } = new();
            public IReadOnlyDictionary<string, DecodedTexture>? Textures { get; init; }

            /// <summary>The interior DAT's own environment: its sub-environments (Lower Jeuno's taverns: <c>ev10</c>) light it.</summary>
            public ZoneEnvironmentData? Environment { get; init; }
            public Dictionary<string, (DeviceBuffer Buffer, ResourceSet Set)> Scenes { get; init; } = new();
            public Dictionary<string, ZoneSceneUniform> Uniforms { get; } = new(StringComparer.OrdinalIgnoreCase);

            /// <summary>The interior's own light generators and the zone light-table slot its slot 0 is moved to.</summary>
            public List<(WeatherSkyLayer Layer, ZoneParticleEmitter Emitter)> Lights { get; } = new();
            public int SlotOffset { get; init; }

            /// <summary>
            /// The interior's other generators (#428: the Meadhouse's lamp halos <c>ltcu</c>, one at each lamp) and their
            /// running emitters, drawn like the zone's own effects.
            /// </summary>
            public ActorEffectSet? Effects { get; init; }
            public List<(WeatherSkyLayer Layer, ZoneParticleEmitter Emitter)> EffectEmitters { get; } = new();
        }

        /// <summary>The first light-table slot not taken by the zone or an interior shown since it loaded.</summary>
        private int _nextInteriorLightSlot = -1;

        /// <summary>
        /// Advances the shown interiors' light generators and writes them into their slots: an interior's placements name
        /// its own light table, so its lights are moved past the zone's (Lower Jeuno's tavern lamps; #68 round 1, retail's
        /// warm pools of lamp light were missing).
        /// </summary>
        private void WriteInteriorLights()
        {
            foreach (int id in _shownSubAreas)
            {
                if (!_interiors.TryGetValue(id, out var interior) || interior == null) continue;
                foreach (var (layer, emitter) in interior.Lights)
                {
                    if (_lastEmitterFrame is { } frame) emitter.Update(_lastEmitterFrames, frame);
                    WriteLight(emitter, interior.SlotOffset + layer.PointLightSlot, _sceneryTransform);
                }
                if (_lastEmitterFrame is { } effectFrame)
                {
                    foreach (var (_, emitter) in interior.EffectEmitters) emitter.Update(_lastEmitterFrames, effectFrame);
                }
            }
        }

        private readonly Dictionary<int, SubAreaInterior?> _interiors = new();
        private readonly Dictionary<int, Task<(ZoneGeometry? Interior, Dictionary<string, DecodedTexture>? Textures)>> _interiorLoads = new();

        /// <summary>The interiors drawn this frame, whose closed-exterior plugs are hidden.</summary>
        private IReadOnlyList<int> _shownSubAreas = Array.Empty<int>();

        /// <summary>
        /// Shows the interiors as if the player stood at a place (offscreen renders and tests); null follows the displayed
        /// world's <see cref="WorldState.SubAreas"/>.
        /// </summary>
        public int[]? SubAreaOverride { get; set; }

        /// <summary>Interiors drawn this frame (tests and diagnostics).</summary>
        internal int InteriorsDrawn { get; private set; }

        /// <summary>Takes this frame's near interiors (<see cref="SubAreaOverride"/>, else the displayed world's).</summary>
        private void ResolveShownInteriors() =>
            _shownSubAreas = SubAreaOverride ?? World?.SubAreas.NearSubAreas ?? (IReadOnlyList<int>)Array.Empty<int>();

        /// <summary>Whether an interior is drawn this frame (its placeholder is then hidden).</summary>
        private bool IsInteriorShown(int subAreaId)
        {
            var shown = _shownSubAreas;
            for (int i = 0; i < shown.Count; i++)
            {
                if (shown[i] == subAreaId) return _interiors.TryGetValue(subAreaId, out var ready) && ready != null;
            }
            return false;
        }

        private void DrawSubAreaInteriors(ResourceManager? resourceManager, ZoneSceneUniform sceneUniform, ZoneEnvironmentSettings environment,
            BoundingFrustum frustum, bool deferred, ref int draws, ref int visible, ref int culled)
        {
            if (!deferred) InteriorsDrawn = 0;
            var near = _shownSubAreas;
            if (near.Count == 0 || resourceManager == null) return;
            foreach (int id in near)
            {
                var interior = GetInterior(id, resourceManager);
                if (interior == null) continue;
                if (!deferred) UpdateSubEnvironmentScenes(interior.Environment, interior.Scenes, interior.Uniforms, null, sceneUniform, environment);
                DrawPlacedSubmeshes(interior.Submeshes, _sceneryTransform, interior.Textures, sceneUniform, frustum, deferred, ref draws, ref visible, ref culled,
                    interior.Scenes, interior.Uniforms);
                if (!deferred) InteriorsDrawn++;
            }
        }

        private SubAreaInterior? GetInterior(int subAreaId, ResourceManager resourceManager)
        {
            if (_interiors.TryGetValue(subAreaId, out var ready)) return ready;
            if (!_interiorLoads.TryGetValue(subAreaId, out var load))
            {
                _interiorLoads[subAreaId] = Task.Run(() => resourceManager.TryLoadSubAreaInterior(subAreaId, out var geometry, out var textures)
                    ? (geometry, textures)
                    : ((ZoneGeometry?)null, (Dictionary<string, DecodedTexture>?)null));
                return null;
            }
            if (!load.IsCompleted) return null;
            _interiorLoads.Remove(subAreaId);

            SubAreaInterior? interior = null;
            if (load.IsCompletedSuccessfully && load.Result.Interior is { } geometry)
            {
                // The interior's own textures first, then the zone's.
                var textures = new Dictionary<string, DecodedTexture>(load.Result.Textures ?? new Dictionary<string, DecodedTexture>(), StringComparer.OrdinalIgnoreCase);
                if (_activeDecodedTextures != null)
                {
                    foreach (var (name, texture) in _activeDecodedTextures) textures.TryAdd(name, texture);
                }
                // Light tables run to 256 entries, mostly empty: count up to the last slot in use.
                if (_nextInteriorLightSlot < 0) _nextInteriorLightSlot = UsedLightSlots(LoadedZone?.PointLightIds);
                int lightCount = UsedLightSlots(geometry.PointLightIds);
                bool lit = lightCount > 0 && _nextInteriorLightSlot + lightCount <= PointLightTableLayout.Slots;
                int offset = lit ? _nextInteriorLightSlot : 0;
                if (lit) _nextInteriorLightSlot += lightCount;
                var effectLayers = geometry.EffectLayers.FindAll(layer => layer.PointLightSlot < 0 && layer.Emitter != null);
                interior = new SubAreaInterior
                {
                    Textures = textures, Environment = geometry.EnvironmentData, Scenes = CreateSubEnvironmentScenes(geometry), SlotOffset = offset,
                    Effects = effectLayers.Count > 0 ? new ActorEffectSet(effectLayers, Array.Empty<WeatherRoutineVariant>(), textures) : null
                };
                foreach (var group in geometry.MeshGroups)
                {
                    if (group.Vertices.Length == 0 || group.Indices.Length == 0) continue;
                    int[] slots = Array.Empty<int>();
                    if (lit && group.PointLightSlots.Length > 0)
                    {
                        slots = new int[group.PointLightSlots.Length];
                        for (int i = 0; i < slots.Length; i++) slots[i] = group.PointLightSlots[i] + offset;
                    }
                    interior.Submeshes.Add(UploadSubmesh(_shared, group, pointLightSlots: slots));
                }
                if (lit)
                {
                    int seed = 0;
                    foreach (var layer in geometry.EffectLayers)
                    {
                        if (layer.PointLightSlot >= 0 && layer.Emitter != null) interior.Lights.Add((layer, new ZoneParticleEmitter(layer.Emitter, seed: seed++)));
                    }
                }
                if (interior.Effects is { } effects)
                {
                    UploadActorEffectMeshes(effects);
                    var byTemplate = new Dictionary<ZoneEmitterTemplate, ZoneParticleEmitter>(ReferenceEqualityComparer.Instance);
                    int effectSeed = 0;
                    foreach (var layer in effects.Layers)
                    {
                        var emitter = new ZoneParticleEmitter(layer.Emitter!, seed: effectSeed++) { ChildResolver = template => byTemplate.GetValueOrDefault(template) };
                        byTemplate[layer.Emitter!] = emitter;
                        interior.EffectEmitters.Add((layer, emitter));
                    }
                }
                GordianLog.Info("Graphics", $"Sub-area {subAreaId} interior: {interior.Submeshes.Count} submeshes, {interior.EffectEmitters.Count} effect generators uploaded.");
            }
            else
            {
                GordianLog.Warning("Graphics", $"Sub-area {subAreaId}: no interior DAT (file {ZoneSubAreas.InteriorFileId(subAreaId)}).");
            }
            _interiors[subAreaId] = interior;
            return interior;
        }

        /// <summary>Live particles of the shown interiors' effect generators (tests).</summary>
        internal int InteriorEffectParticleCount
        {
            get
            {
                int count = 0;
                foreach (int id in _shownSubAreas)
                {
                    if (!_interiors.TryGetValue(id, out var interior) || interior == null) continue;
                    foreach (var (_, emitter) in interior.EffectEmitters) count += emitter.Particles.Count;
                }
                return count;
            }
        }

        /// <summary>Whether a shown interior has effect generators to draw this frame.</summary>
        private bool HasInteriorEffects()
        {
            foreach (int id in _shownSubAreas)
            {
                if (_interiors.TryGetValue(id, out var interior) && interior?.EffectEmitters.Count > 0) return true;
            }
            return false;
        }

        /// <summary>Queues the shown interiors' effect particles, in the interiors' frame (the scenery's, on a voyage).</summary>
        private void AddInteriorEffectDraws(ViewportCamera camera)
        {
            Matrix4x4? anchor = _sceneryTransform.IsIdentity ? null : _sceneryTransform;
            foreach (int id in _shownSubAreas)
            {
                if (!_interiors.TryGetValue(id, out var interior) || interior?.Effects == null ||
                    !_actorEffectMeshes.TryGetValue(interior.Effects, out var byLayer)) continue;
                foreach (var (layer, emitter) in interior.EffectEmitters)
                {
                    if (emitter.Particles.Count == 0 || !byLayer.TryGetValue(layer, out var meshes)) continue;
                    float distance = Vector3.Distance(camera.Position, Vector3.Transform(layer.Position, _sceneryTransform));
                    foreach (var mesh in meshes) _effectDrawList.Add(new EffectDraw(mesh, distance, emitter, anchor));
                }
            }
        }

        private static int UsedLightSlots(List<string>? table)
        {
            if (table == null) return 0;
            for (int i = table.Count - 1; i >= 0; i--)
            {
                if (table[i].Length > 0) return i + 1;
            }
            return 0;
        }

        private void DisposeSubAreaInteriors()
        {
            foreach (var interior in _interiors.Values)
            {
                if (interior == null) continue;
                foreach (var submesh in interior.Submeshes) submesh.Dispose();
                DisposeSubEnvironmentScenes(interior.Scenes);
                if (interior.Effects != null && _actorEffectMeshes.Remove(interior.Effects, out var meshes))
                {
                    foreach (var list in meshes.Values)
                    {
                        foreach (var mesh in list) mesh.Dispose();
                    }
                }
            }
            _interiors.Clear();
            _interiorLoads.Clear();
            _nextInteriorLightSlot = -1;
            _shownSubAreas = Array.Empty<int>();
        }
    }
}
