// src/Gordian.App/Graphics/ZoneResidencyCache.cs
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Gordian.Core.Diagnostics;
using Gordian.Core.Network;
using Gordian.Core.Resources;
using Gordian.Core.Resources.Graphics;
using Gordian.Core.Resources.Models;

namespace Gordian.App.Graphics
{
    /// <summary>
    /// The zones kept on the GPU (#322): the vertex, index and generator buffers of a zone and its uploaded textures
    /// (<see cref="ZoneTerrainRenderer.ResidentZone"/>), one copy per zone shared by every viewport window on the device.
    /// A window showing a zone holds a reference to it; a zone no window shows stays resident while it fits in
    /// <see cref="BudgetBytes"/>, so switching the view to a character in another zone (Ctrl+Tab) only rebinds it instead of
    /// uploading the zone again. Zones a logged-in character is in are pinned (<see cref="SetPinnedZones"/>) and preloaded in
    /// the background (<see cref="PreloadSessionZones"/>); unpinned ones go first when over budget, least recently used
    /// first. A zone read before a VFS reload (an older ResourceManager cache generation) is dropped once no window shows it.
    /// </summary>
    public sealed class ZoneResidencyCache : IDisposable
    {
        private sealed class Entry
        {
            public required ZoneTerrainRenderer.ResidentZone Zone;
            public int References;
            public long LastUse;
        }

        private readonly GpuSharedResources _shared;
        private readonly object _gate = new();
        private readonly Dictionary<ZoneGeometry, Entry> _entries = new(ReferenceEqualityComparer.Instance);
        private readonly HashSet<ZoneGeometry> _building = new(ReferenceEqualityComparer.Instance);
        private readonly HashSet<int> _preloading = new();
        private readonly ConcurrentDictionary<int, object> _readLocks = new();
        private HashSet<int> _pinned = new();
        private long _useClock;
        private int _latestGeneration;
        private long _lastPreloadTick;
        private bool _disposed;

        internal ZoneResidencyCache(GpuSharedResources shared) => _shared = shared;

        /// <summary>
        /// GPU memory the resident zones may use; zones on screen stay whatever their size. Default from
        /// <see cref="ViewportRenderSettings.ZoneCacheBudgetMb"/>.
        /// </summary>
        public long BudgetBytes { get; set; } = (long)ViewportRenderSettings.DefaultZoneCacheBudgetMb << 20;

        /// <summary>Most zones kept resident, whatever their size.</summary>
        public int MaxResidentZones { get; set; } = 12;

        /// <summary>The bytes the resident zones hold (buffers and textures).</summary>
        public long ResidentBytes
        {
            get { lock (_gate) return _entries.Values.Sum(e => e.Zone.Bytes); }
        }

        /// <summary>The number of resident zones.</summary>
        public int Count
        {
            get { lock (_gate) return _entries.Count; }
        }

        /// <summary>The zone ids resident now (diagnostics and tests).</summary>
        public IReadOnlyList<int> ResidentZoneIds
        {
            get { lock (_gate) return _entries.Values.Select(e => e.Zone.ZoneId).ToList(); }
        }

        /// <summary>Whether this zone geometry is on the GPU.</summary>
        public bool IsResident(ZoneGeometry? zone)
        {
            if (zone == null) return false;
            lock (_gate) return _entries.ContainsKey(zone);
        }

        /// <summary>
        /// The resident copy of a zone with a reference taken (give it back with <see cref="Release"/>, or hand it to
        /// <see cref="ZoneTerrainRenderer.ActivateZone"/>), uploading the zone first when it is not resident. The upload
        /// takes the device lock per chunk, so other windows keep drawing meanwhile.
        /// </summary>
        public ZoneTerrainRenderer.ResidentZone Acquire(ZoneGeometry zone, IReadOnlyDictionary<string, DecodedTexture>? textures, int generation = 0)
        {
            ArgumentNullException.ThrowIfNull(zone);
            while (true)
            {
                lock (_gate)
                {
                    ObjectDisposedException.ThrowIf(_disposed, this);
                    _latestGeneration = Math.Max(_latestGeneration, generation);
                    if (_entries.TryGetValue(zone, out var entry))
                    {
                        entry.References++;
                        entry.LastUse = ++_useClock;
                        return entry.Zone;
                    }
                    // Another thread is uploading it: wait for that rather than uploading twice, unless this thread holds
                    // the device lock the upload needs (then upload here; the slower copy is dropped below).
                    if (!_building.Contains(zone) || Monitor.IsEntered(_shared.GpuLock))
                    {
                        _building.Add(zone);
                        break;
                    }
                }
                Thread.Sleep(5);
            }

            ZoneTerrainRenderer.ResidentZone built;
            try
            {
                built = ZoneTerrainRenderer.ResidentZone.Build(_shared, zone, textures, generation);
            }
            catch
            {
                lock (_gate) _building.Remove(zone);
                throw;
            }

            ZoneTerrainRenderer.ResidentZone result;
            bool duplicate = false;
            lock (_gate)
            {
                _building.Remove(zone);
                if (_entries.TryGetValue(zone, out var existing))
                {
                    existing.References++;
                    existing.LastUse = ++_useClock;
                    result = existing.Zone;
                    duplicate = true;
                }
                else
                {
                    _entries[zone] = new Entry { Zone = built, References = 1, LastUse = ++_useClock };
                    result = built;
                }
            }
            if (duplicate)
            {
                lock (_shared.GpuLock) built.Dispose();
            }
            Trim();
            return result;
        }

        /// <summary>
        /// Reads a zone's DATs through the ResourceManager, one reader per zone at a time: a window's load, a switch's
        /// preparation and the background preload asking for the same zone together get one geometry (and one upload)
        /// instead of parsing it twice.
        /// </summary>
        public bool TryReadZone(ResourceManager resources, int zoneId, [NotNullWhen(true)] out ZoneGeometry? zone, out Dictionary<string, DecodedTexture> textures)
        {
            ArgumentNullException.ThrowIfNull(resources);
            lock (_readLocks.GetOrAdd(zoneId, _ => new object()))
            {
                return resources.TryLoadZone(zoneId, out zone, out textures);
            }
        }

        /// <summary>The resident copy of a zone with a reference taken, without uploading anything; false when not resident.</summary>
        public bool TryAcquireResident(ZoneGeometry? zone, out ZoneTerrainRenderer.ResidentZone? resident)
        {
            resident = null;
            if (zone == null) return false;
            lock (_gate)
            {
                if (!_entries.TryGetValue(zone, out var entry)) return false;
                entry.References++;
                entry.LastUse = ++_useClock;
                resident = entry.Zone;
                return true;
            }
        }

        /// <summary>Gives back a reference from <see cref="Acquire"/> or <see cref="TryAcquireResident"/>.</summary>
        public void Release(ZoneTerrainRenderer.ResidentZone? zone)
        {
            if (zone == null) return;
            lock (_gate)
            {
                if (_entries.TryGetValue(zone.Zone, out var entry) && ReferenceEquals(entry.Zone, zone))
                {
                    entry.References = Math.Max(0, entry.References - 1);
                    entry.LastUse = ++_useClock;
                }
            }
            Trim();
        }

        /// <summary>The zones logged-in characters are in: kept before other zones when over budget.</summary>
        public void SetPinnedZones(IEnumerable<int> zoneIds)
        {
            var pinned = new HashSet<int>(zoneIds.Where(id => id > 0));
            lock (_gate)
            {
                if (_pinned.SetEquals(pinned)) return;
                _pinned = pinned;
            }
            Trim();
        }

        /// <summary>
        /// Keeps the zones of the logged-in characters resident (#322), at most once a second: pins them and uploads in the
        /// background those not resident yet, one at a time and only while they are expected to fit in the budget.
        /// </summary>
        public void PreloadSessionZones(ResourceManager? resources, IEnumerable<CharacterSession> sessions)
        {
            long now = Stopwatch.GetTimestamp();
            lock (_gate)
            {
                if (_disposed || (_lastPreloadTick != 0 && Stopwatch.GetElapsedTime(_lastPreloadTick, now).TotalSeconds < 1.0)) return;
                _lastPreloadTick = now;
            }
            BudgetBytes = (long)ViewportRenderSettings.ZoneCacheBudgetMb << 20;
            var zones = SessionZones(sessions);
            SetPinnedZones(zones);
            if (resources == null || BudgetBytes == 0) return;
            foreach (int zoneId in zones)
            {
                var loaded = resources.TryGetLoadedZone(zoneId);
                if (loaded != null && IsResident(loaded)) continue;
                PreloadInBackground(resources, zoneId);
                break;
            }
        }

        /// <summary>The distinct zones of the characters in the world.</summary>
        internal static List<int> SessionZones(IEnumerable<CharacterSession> sessions) =>
            SessionZones(sessions.Select(session => (session.State, (int)session.World.CurrentZoneId)));

        /// <summary>The distinct zones of the characters in the world, in order (state and zone of each character).</summary>
        internal static List<int> SessionZones(IEnumerable<(SessionState State, int ZoneId)> characters)
        {
            var zones = new List<int>();
            foreach (var (state, zone) in characters)
            {
                if (state != SessionState.ActiveInWorld) continue;
                if (zone > 0 && !zones.Contains(zone)) zones.Add(zone);
            }
            return zones;
        }

        /// <summary>
        /// Uploads a zone in the background (reading its DATs first if needed) and leaves it resident without a
        /// reference, unless it is already being preloaded or is not expected to fit in the budget.
        /// </summary>
        public bool PreloadInBackground(ResourceManager resources, int zoneId)
        {
            ArgumentNullException.ThrowIfNull(resources);
            lock (_gate)
            {
                if (_disposed || BudgetBytes == 0 || !_preloading.Add(zoneId)) return false;
                long resident = _entries.Values.Sum(e => e.Zone.Bytes);
                long typical = _entries.Count > 0 ? resident / _entries.Count : 128L << 20;
                if (resident + typical > BudgetBytes)
                {
                    _preloading.Remove(zoneId);
                    return false;
                }
            }

            Task.Run(() =>
            {
                try
                {
                    var watch = Stopwatch.StartNew();
                    int generation = resources.CacheGeneration;
                    if (!TryReadZone(resources, zoneId, out var geometry, out var textures)) return;
                    double readMs = watch.Elapsed.TotalMilliseconds;
                    if (IsResident(geometry)) return;
                    var resident = Acquire(geometry, textures, generation);
                    Release(resident);
                    GordianLog.Info("Graphics", $"Preloaded Zone {zoneId} for a character in it: DATs {readMs:F0} ms, GPU upload {watch.Elapsed.TotalMilliseconds - readMs:F0} ms, {resident.Bytes >> 20} MB ({Count} zones, {ResidentBytes >> 20} MB resident).");
                }
                catch (Exception ex)
                {
                    GordianLog.Warning("Graphics", $"Background preload of Zone {zoneId} failed: {ex.Message}");
                }
                finally
                {
                    lock (_gate) _preloading.Remove(zoneId);
                }
            });
            return true;
        }

        /// <summary>One resident zone as <see cref="SelectVictims"/> sees it.</summary>
        internal readonly record struct Candidate(long Bytes, int References, bool Pinned, bool Stale, long LastUse);

        /// <summary>
        /// The resident zones to drop (indexes into <paramref name="zones"/>): every unreferenced stale one, then, while
        /// over <paramref name="budgetBytes"/> or <paramref name="maxZones"/>, the least recently used unreferenced
        /// unpinned zone, then the least recently used unreferenced pinned one. A zone a window shows is never dropped.
        /// </summary>
        internal static List<int> SelectVictims(IReadOnlyList<Candidate> zones, long budgetBytes, int maxZones)
        {
            var victims = new List<int>();
            long total = 0;
            int count = zones.Count;
            for (int i = 0; i < zones.Count; i++)
            {
                if (zones[i].References == 0 && zones[i].Stale)
                {
                    victims.Add(i);
                    count--;
                }
                else
                {
                    total += zones[i].Bytes;
                }
            }

            foreach (bool pinnedPass in new[] { false, true })
            {
                var order = Enumerable.Range(0, zones.Count)
                    .Where(i => zones[i].References == 0 && zones[i].Pinned == pinnedPass && !victims.Contains(i))
                    .OrderBy(i => zones[i].LastUse)
                    .ToList();
                foreach (int i in order)
                {
                    if (total <= budgetBytes && count <= maxZones) return victims;
                    victims.Add(i);
                    total -= zones[i].Bytes;
                    count--;
                }
            }
            return victims;
        }

        /// <summary>Drops what <see cref="SelectVictims"/> picks and frees its buffers and textures.</summary>
        public void Trim()
        {
            List<ZoneTerrainRenderer.ResidentZone> dropped;
            List<ZoneTerrainRenderer.ResidentZone> kept;
            lock (_gate)
            {
                if (_disposed) return;
                var entries = _entries.Values.ToList();
                var candidates = entries.Select(e => new Candidate(e.Zone.Bytes, e.References, _pinned.Contains(e.Zone.ZoneId),
                    e.Zone.Generation < _latestGeneration, e.LastUse)).ToList();
                var victims = SelectVictims(candidates, BudgetBytes, MaxResidentZones);
                if (victims.Count == 0) return;
                dropped = victims.Select(i => entries[i].Zone).ToList();
                foreach (var zone in dropped) _entries.Remove(zone.Zone);
                kept = _entries.Values.Select(e => e.Zone).ToList();
            }

            var keepSources = new HashSet<string>(StringComparer.Ordinal);
            foreach (var zone in kept)
            {
                if (zone.Textures == null) continue;
                foreach (var texture in zone.Textures.Values)
                {
                    if (texture.Source.Length > 0) keepSources.Add(texture.Source);
                }
            }
            lock (_shared.GpuLock)
            {
                foreach (var zone in dropped)
                {
                    int textures = zone.Textures != null ? _shared.ZoneTextures.Evict(zone.Textures.Values, keepSources) : 0;
                    zone.Dispose();
                    GordianLog.Info("Graphics", $"Unloaded Zone {zone.ZoneId} from the GPU ({zone.Bytes >> 20} MB, {textures} textures; zone cache budget {BudgetBytes >> 20} MB).");
                }
            }
        }

        public void Dispose()
        {
            List<ZoneTerrainRenderer.ResidentZone> all;
            lock (_gate)
            {
                if (_disposed) return;
                _disposed = true;
                all = _entries.Values.Select(e => e.Zone).ToList();
                _entries.Clear();
            }
            lock (_shared.GpuLock)
            {
                foreach (var zone in all) zone.Dispose();
            }
        }
    }
}
