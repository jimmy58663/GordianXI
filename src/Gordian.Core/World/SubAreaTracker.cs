// src/Gordian.Core/World/SubAreaTracker.cs
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Gordian.Core.Diagnostics;
using Gordian.Core.World.Collision;

namespace Gordian.Core.World
{
    /// <summary>
    /// Which building interiors (sub-areas, #68, <see cref="ZoneSubAreas"/>) a session's player is in and near, updated from
    /// the local player's position each locomotion tick. Near interiors are drawn and walked; the one the player is in hides
    /// its closed exterior.
    /// <para>
    /// The <c>'m'</c> volumes straddle the doorways (Lower Jeuno's food shop <c>m6t9</c> is centred 2.5 yalms outside its
    /// interior's west wall, the interior's floor runs 19 yalms east of it), so the player is in an interior while in one of
    /// its volumes or over its collision floor (the interior collision's bounds). The interiors' collision loads in the
    /// background through <see cref="InteriorCollisionLoader"/> and is merged with the zone's
    /// (<see cref="ZoneCollisionMesh.Merge"/>) into <see cref="WorldState.Collision"/>. The server is not told.
    /// </para>
    /// </summary>
    public sealed class SubAreaTracker
    {
        /// <summary>Loads an interior's collision by sub-area id (the resource manager's cache); null leaves interiors unwalked.</summary>
        public static Func<int, ZoneCollisionMesh?>? InteriorCollisionLoader { get; set; }

        private readonly WorldState _world;
        private readonly object _lock = new();
        private readonly Dictionary<int, ZoneCollisionMesh?> _interiors = new();
        private readonly HashSet<int> _loading = new();
        private readonly List<int> _near = new();
        private int[] _nearSnapshot = Array.Empty<int>();
        private int _active;
        private int _generation;
        private string _mergedKey = string.Empty;

        internal SubAreaTracker(WorldState world) => _world = world;

        /// <summary>The interior the player is in, or 0.</summary>
        public int ActiveSubArea => Volatile.Read(ref _active);

        /// <summary>The interiors near the player (sorted), the active one included; drawn by the viewport.</summary>
        public IReadOnlyList<int> NearSubAreas => Volatile.Read(ref _nearSnapshot);

        /// <summary>Raised (on the updating thread) when the active interior changes.</summary>
        public event Action<int>? ActiveChanged;

        /// <summary>Forgets everything (a new zone or collision mesh).</summary>
        internal void Reset()
        {
            lock (_lock)
            {
                _generation++;
                _interiors.Clear();
                _loading.Clear();
                _near.Clear();
                _mergedKey = string.Empty;
                Volatile.Write(ref _nearSnapshot, Array.Empty<int>());
            }
            if (Interlocked.Exchange(ref _active, 0) != 0) ActiveChanged?.Invoke(0);
        }

        /// <summary>Updates the interiors from the player's position (zone coordinates).</summary>
        public void Update(Vector3 position)
        {
            var zone = _world.ZoneCollision;
            var volumes = zone?.SubAreaVolumes;
            if (zone == null || volumes == null || volumes.Count == 0)
            {
                if (_near.Count > 0 || _active != 0) Reset();
                return;
            }

            int active;
            lock (_lock)
            {
                bool changed = ZoneSubAreas.FindNear(volumes, position, ZoneSubAreas.NearMargin, _near);
                active = ZoneSubAreas.FindActive(volumes, position);
                foreach (var (id, interior) in _interiors)
                {
                    if (interior == null) continue;
                    if (active == 0 && InsideBounds(interior, position, 0f)) active = id;
                    if (!_near.Contains(id) && InsideBounds(interior, position, ZoneSubAreas.NearMargin))
                    {
                        _near.Add(id);
                        _near.Sort();
                        changed = true;
                    }
                }
                if (changed) Volatile.Write(ref _nearSnapshot, _near.ToArray());
                RequestInteriors(zone);
            }
            if (Interlocked.Exchange(ref _active, active) != active)
            {
                GordianLog.Debug("SubArea", $"Zone {_world.CurrentZoneId}: interior {active} (near {string.Join(",", _nearSnapshot)}).");
                ActiveChanged?.Invoke(active);
            }
        }

        /// <summary>Over an interior's collision floor: its bounds in XZ, and between its floor and ceiling (+Y is down).</summary>
        private static bool InsideBounds(ZoneCollisionMesh interior, Vector3 position, float margin) =>
            position.X >= interior.MinBounds.X - margin && position.X <= interior.MaxBounds.X + margin &&
            position.Z >= interior.MinBounds.Z - margin && position.Z <= interior.MaxBounds.Z + margin &&
            position.Y >= interior.MinBounds.Y - 1f - margin && position.Y <= interior.MaxBounds.Y + 1f + margin;

        /// <summary>Loads the near interiors' collision in the background and merges what is near once it is all there.</summary>
        private void RequestInteriors(ZoneCollisionMesh zone)
        {
            var loader = InteriorCollisionLoader;
            if (loader == null) return;
            bool allLoaded = true;
            foreach (int id in _near)
            {
                if (_interiors.ContainsKey(id)) continue;
                allLoaded = false;
                if (!_loading.Add(id)) continue;
                int generation = _generation;
                Task.Run(() =>
                {
                    ZoneCollisionMesh? mesh = null;
                    try { mesh = loader(id); }
                    catch (Exception ex) { GordianLog.Warning("SubArea", $"Interior {id} collision failed: {ex.Message}"); }
                    lock (_lock)
                    {
                        if (generation != _generation) return;
                        _loading.Remove(id);
                        _interiors[id] = mesh;
                    }
                });
            }
            if (!allLoaded) return;

            string key = string.Join(",", _near);
            if (key == _mergedKey) return;
            _mergedKey = key;
            var parts = new List<ZoneCollisionMesh>();
            foreach (int id in _near)
            {
                if (_interiors.TryGetValue(id, out var mesh) && mesh != null) parts.Add(mesh);
            }
            if (parts.Count == 0)
            {
                _world.SetSubAreaCollision(zone, null);
                return;
            }
            int expected = _generation;
            Task.Run(() =>
            {
                var merged = ZoneCollisionMesh.Merge(zone, parts);
                lock (_lock)
                {
                    if (expected != _generation || _mergedKey != key) return;
                    _world.SetSubAreaCollision(zone, merged);
                }
            });
        }
    }
}
