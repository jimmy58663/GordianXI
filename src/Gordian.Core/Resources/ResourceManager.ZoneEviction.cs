// src/Gordian.Core/Resources/ResourceManager.ZoneEviction.cs
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Gordian.Core.Diagnostics;
using Gordian.Core.Resources.Graphics;
using Gordian.Core.Resources.Models;

namespace Gordian.Core.Resources
{
    /// <summary>The zone caches at a glance, for the performance overlay and logs (#576).</summary>
    public readonly record struct ZoneCacheStats(int Zones, int Collisions, int Interiors, int InteriorCollisions);

    public sealed partial class ResourceManager
    {
        /// <summary>
        /// A zone (geometry, textures, collision) or building interior no logged-in character or window has used for this
        /// long is dropped by <see cref="PurgeUnusedZones"/> (#576). Long enough that zoning straight back finds the zone
        /// still loaded, so no separate grace for the last zone is needed.
        /// </summary>
        public const double ZoneIdleSeconds = 60.0;

        // When each cached zone / interior was last in use (Stopwatch ticks). Stamped by every load and by every purge
        // that finds it in use; a cached entry without a stamp is stamped when a purge first sees it unused.
        private readonly ConcurrentDictionary<int, long> _zoneLastUse = new();
        private readonly ConcurrentDictionary<int, long> _interiorLastUse = new();

        private void MarkZoneUsed(int zoneId) => _zoneLastUse[zoneId] = Stopwatch.GetTimestamp();

        private void MarkInteriorUsed(int subAreaId) => _interiorLastUse[subAreaId] = Stopwatch.GetTimestamp();

        /// <summary>The number of cached zones, collision meshes and interiors.</summary>
        public ZoneCacheStats GetZoneCacheStats() =>
            new(_zoneCache.Count, _collisionCache.Count, _interiorCache.Count, _interiorCollisionCache.Count);

        /// <summary>
        /// Drops the zones nothing uses any more (#576): a zone in neither <paramref name="zonesInUse"/> nor used by a load
        /// within <paramref name="idleSeconds"/> leaves every zone cache (geometry and textures, collision), and so does a
        /// building interior whose parent zone is not in use (an interior is in use while its zone is, since every session
        /// in a zone loads all of its interiors' collision). The caller lists the zones of every logged-in character and of
        /// every window (see the App's <c>ZoneCachePurger</c>). Returns the zone geometries dropped, so the GPU copies made
        /// from them can be dropped too, and adds the dropped interiors' sub-area ids to <paramref name="droppedInteriorIds"/>
        /// (their shared GPU copies go with them, #585).
        /// </summary>
        public IReadOnlyList<ZoneGeometry> PurgeUnusedZones(IEnumerable<int> zonesInUse, double idleSeconds = ZoneIdleSeconds, ICollection<int>? droppedInteriorIds = null)
        {
            ArgumentNullException.ThrowIfNull(zonesInUse);
            long now = Stopwatch.GetTimestamp();
            long idleTicks = (long)(idleSeconds * Stopwatch.Frequency);

            var inUse = new HashSet<int>();
            foreach (int zoneId in zonesInUse)
            {
                if (zoneId <= 0 || !inUse.Add(zoneId)) continue;
                _zoneLastUse[zoneId] = now;
            }

            var interiorsInUse = new HashSet<int>();
            foreach (int zoneId in inUse)
            {
                var collision = _collisionCache.TryGetValue(zoneId, out var mesh) ? mesh
                    : _zoneCache.TryGetValue(zoneId, out var zone) ? zone.Geometry.Collision : null;
                if (collision == null) continue;
                foreach (var volume in collision.SubAreaVolumes)
                {
                    if (volume.Param > 0 && interiorsInUse.Add(volume.Param)) _interiorLastUse[volume.Param] = now;
                }
            }

            var dropped = new List<ZoneGeometry>();
            var droppedIds = new List<int>();
            var zoneIds = new HashSet<int>(_zoneCache.Keys);
            zoneIds.UnionWith(_collisionCache.Keys);
            foreach (int zoneId in zoneIds)
            {
                if (inUse.Contains(zoneId) || !IsIdle(_zoneLastUse, zoneId, now, idleTicks)) continue;
                if (_zoneCache.TryRemove(zoneId, out var removed)) dropped.Add(removed.Geometry);
                _collisionCache.TryRemove(zoneId, out _);
                _zoneLastUse.TryRemove(zoneId, out _);
                droppedIds.Add(zoneId);
            }

            int interiors = 0;
            var interiorIds = new HashSet<int>(_interiorCache.Keys);
            interiorIds.UnionWith(_interiorCollisionCache.Keys);
            foreach (int subAreaId in interiorIds)
            {
                if (interiorsInUse.Contains(subAreaId) || !IsIdle(_interiorLastUse, subAreaId, now, idleTicks)) continue;
                _interiorCache.TryRemove(subAreaId, out _);
                _interiorCollisionCache.TryRemove(subAreaId, out _);
                _interiorLastUse.TryRemove(subAreaId, out _);
                droppedInteriorIds?.Add(subAreaId);
                interiors++;
            }

            if (droppedIds.Count > 0 || interiors > 0)
            {
                var stats = GetZoneCacheStats();
                GordianLog.Info(LogCategories.Res, $"Zone cache purge: dropped zone(s) {(droppedIds.Count > 0 ? string.Join(", ", droppedIds) : "none")} " +
                    $"and {interiors} interior(s) no character or window used for {idleSeconds:F0} s; {stats.Zones} zones, {stats.Collisions} collision meshes, " +
                    $"{stats.Interiors} interiors left (in use: {string.Join(", ", inUse)}).");
            }
            return dropped;
        }

        // Idle when last used longer ago than the idle time; an entry never stamped is stamped now (first seen unused).
        private static bool IsIdle(ConcurrentDictionary<int, long> lastUse, int id, long now, long idleTicks)
        {
            long stamp = lastUse.GetOrAdd(id, now);
            return now - stamp >= idleTicks;
        }

        /// <summary>
        /// Reads a zone again for a GPU upload when the cached copy's render data was released (#576) and the GPU copy made
        /// from it is gone: the new geometry replaces it in the cache (sharing its collision). When another reader already
        /// replaced it, that copy is returned. Returns the cached copy as it is when its render data was not released.
        /// </summary>
        public bool TryReloadZoneRenderData(int zoneId, ZoneGeometry released, [NotNullWhen(true)] out ZoneGeometry? zone, out Dictionary<string, DecodedTexture> textures)
        {
            ArgumentNullException.ThrowIfNull(released);
            MarkZoneUsed(zoneId);
            if (_zoneCache.TryGetValue(zoneId, out var cached) && !ReferenceEquals(cached.Geometry, released) && !cached.Geometry.IsRenderDataReleased)
            {
                zone = cached.Geometry;
                textures = cached.Textures;
                return true;
            }
            if (!released.IsRenderDataReleased && cached.Geometry != null && ReferenceEquals(cached.Geometry, released))
            {
                zone = cached.Geometry;
                textures = cached.Textures;
                return true;
            }
            return TryParseZone(zoneId, released, out zone, out textures);
        }
    }
}
