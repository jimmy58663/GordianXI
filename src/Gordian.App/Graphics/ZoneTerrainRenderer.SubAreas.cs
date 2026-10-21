// src/Gordian.App/Graphics/ZoneTerrainRenderer.SubAreas.cs
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Gordian.Core.Diagnostics;
using Gordian.Core.Graphics;
using Gordian.Core.Resources;
using Gordian.Core.Resources.Graphics;
using Gordian.Core.Resources.Models;
using Gordian.Core.World;
using Gordian.Core.World.Collision;

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

        private void DrawSubAreaInteriors(ResourceManager? resourceManager, ZoneSceneUniform sceneUniform, BoundingFrustum frustum,
            ref int draws, ref int visible, ref int culled)
        {
            InteriorsDrawn = 0;
            var near = _shownSubAreas;
            if (near.Count == 0 || resourceManager == null) return;
            foreach (int id in near)
            {
                var interior = GetInterior(id, resourceManager);
                if (interior == null) continue;
                DrawPlacedSubmeshes(interior.Submeshes, _sceneryTransform, interior.Textures, sceneUniform, frustum, ref draws, ref visible, ref culled);
                InteriorsDrawn++;
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
                interior = new SubAreaInterior { Textures = textures };
                foreach (var group in geometry.MeshGroups)
                {
                    if (group.Vertices.Length == 0 || group.Indices.Length == 0) continue;
                    interior.Submeshes.Add(UploadSubmesh(_shared, group));
                }
                GordianLog.Info("Graphics", $"Sub-area {subAreaId} interior: {interior.Submeshes.Count} submeshes uploaded.");
            }
            else
            {
                GordianLog.Warning("Graphics", $"Sub-area {subAreaId}: no interior DAT (file {ZoneSubAreas.InteriorFileId(subAreaId)}).");
            }
            _interiors[subAreaId] = interior;
            return interior;
        }

        private void DisposeSubAreaInteriors()
        {
            foreach (var interior in _interiors.Values)
            {
                if (interior == null) continue;
                foreach (var submesh in interior.Submeshes) submesh.Dispose();
            }
            _interiors.Clear();
            _interiorLoads.Clear();
            _shownSubAreas = Array.Empty<int>();
        }
    }
}
