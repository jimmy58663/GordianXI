// src/Gordian.App/Graphics/ZoneCachePurger.cs
using System;
using System.Collections.Generic;
using System.Threading;
using Gordian.App.Services;
using Gordian.Core.Diagnostics;
using Gordian.Core.Network;
using Gordian.Engine.Sessions;

namespace Gordian.App.Graphics
{
    /// <summary>
    /// Drops zones nothing uses any more (#576), every <see cref="Interval"/>, whether or not any window is drawing: the
    /// ResourceManager's zone caches (geometry, textures, collision, building interiors) through
    /// <see cref="Gordian.Core.Resources.ResourceManager.PurgeUnusedZones"/>, and the GPU copies made from what it dropped.
    /// A zone is in use (<see cref="CollectZonesInUse"/>) while any logged-in character is in it, whether or not a window
    /// renders that character (collision, grounding and automation read it every tick), or while a window holds it; a
    /// zone stays <see cref="Gordian.Core.Resources.ResourceManager.ZoneIdleSeconds"/> after its last use.
    /// </summary>
    public sealed class ZoneCachePurger : IDisposable
    {
        /// <summary>How often the purge runs (a zone goes between 60 and 70 s after its last use).</summary>
        public static readonly TimeSpan Interval = TimeSpan.FromSeconds(10);

        private readonly SessionRegistry _registry;
        private readonly Timer _timer;
        private int _running;

        public ZoneCachePurger(SessionRegistry registry)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _timer = new Timer(_ => Purge(), null, Interval, Interval);
        }

        /// <summary>One purge (the timer's; also callable directly).</summary>
        public void Purge()
        {
            if (Interlocked.Exchange(ref _running, 1) != 0) return;
            try
            {
                var resources = AppResourceManager.Loaded;
                if (resources == null) return;
                var device = SharedGraphicsDevice.Current;
                var inUse = CollectZonesInUse(_registry.ActiveSessions, device?.Resources);
                var dropped = resources.PurgeUnusedZones(inUse);
                if (dropped.Count > 0) device?.Resources.Zones.Drop(dropped);
            }
            catch (Exception ex)
            {
                GordianLog.Warn(LogCategories.Graphics, $"Zone cache purge failed: {ex.Message}");
            }
            finally
            {
                Volatile.Write(ref _running, 0);
            }
        }

        /// <summary>
        /// The zones in use: those of every logged-in character (<see cref="CharacterZones"/>), the zones a window holds or
        /// is uploading (<see cref="ZoneResidencyCache.ZonesInUse"/>), and the ship zones a window read for their vessel
        /// models. Building interiors count through their zone (see <c>ResourceManager.PurgeUnusedZones</c>).
        /// </summary>
        internal static HashSet<int> CollectZonesInUse(IEnumerable<CharacterSession> sessions, GpuSharedResources? shared)
        {
            var characters = new List<(SessionState, int, int, int)>();
            foreach (var session in sessions)
            {
                var world = session.World;
                characters.Add((session.State, world.CurrentZoneId, world.MapZoneId, world.SceneZoneId));
            }
            var zones = CharacterZones(characters);
            if (shared != null)
            {
                foreach (int zoneId in shared.Zones.ZonesInUse) Add(zones, zoneId);
                foreach (var renderer in shared.Renderers)
                {
                    foreach (int zoneId in renderer.VesselZoneIds) Add(zones, zoneId);
                }
            }
            return zones;
        }

        /// <summary>
        /// The zones logged-in characters need: for every session not logged out (<see cref="SessionState.Disconnected"/>),
        /// whatever its state (still loading into the world, or zoning: the zone it is entering is its current zone as soon
        /// as the server names it), its current zone, the zone it walks (a Mog House room's key, <c>MapZoneId</c>) and the
        /// zone an event shows (<c>SceneZoneId</c>).
        /// </summary>
        internal static HashSet<int> CharacterZones(IEnumerable<(SessionState State, int CurrentZone, int MapZone, int SceneZone)> characters)
        {
            var zones = new HashSet<int>();
            foreach (var (state, current, map, scene) in characters)
            {
                if (state == SessionState.Disconnected) continue;
                Add(zones, current);
                Add(zones, map);
                Add(zones, scene);
            }
            return zones;
        }

        private static void Add(HashSet<int> zones, int zoneId)
        {
            if (zoneId > 0) zones.Add(zoneId);
        }

        public void Dispose() => _timer.Dispose();
    }
}
