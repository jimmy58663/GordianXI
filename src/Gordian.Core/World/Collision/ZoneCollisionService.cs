// src/Gordian.Core/World/Collision/ZoneCollisionService.cs
using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using Gordian.Core.Diagnostics;
using Gordian.Core.Network;

namespace Gordian.Core.World.Collision
{
    /// <summary>
    /// Gives every registered session the collision mesh of the zone it is in, whether or not a viewport displays it,
    /// so multi-boxed characters (and automation driving them) follow the ground too. Meshes load in the background
    /// through <paramref name="loader"/> (which should cache per zone) and are dropped if the session has moved on.
    /// </summary>
    public sealed class ZoneCollisionService : IDisposable
    {
        private readonly SessionRegistry _registry;
        private readonly Func<ushort, ZoneCollisionMesh?> _loader;
        private readonly ConcurrentDictionary<Guid, (WorldState World, Action<ushort> Handler)> _attached = new();

        public ZoneCollisionService(SessionRegistry registry, Func<ushort, ZoneCollisionMesh?> loader)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _loader = loader ?? throw new ArgumentNullException(nameof(loader));
            _registry.SessionRegistered += OnSessionRegistered;
            _registry.SessionUnregistered += OnSessionUnregistered;
            foreach (var session in _registry.ActiveSessions) Attach(session);
        }

        private void OnSessionRegistered(object? sender, CharacterSession session) => Attach(session);

        private void OnSessionUnregistered(object? sender, CharacterSession session)
        {
            if (_attached.TryRemove(session.SessionId, out var entry)) entry.World.ZoneChanged -= entry.Handler;
        }

        private void Attach(CharacterSession session)
        {
            var world = session.World;
            Action<ushort> handler = zoneId => _ = LoadForAsync(world, zoneId);
            if (!_attached.TryAdd(session.SessionId, (world, handler))) return;
            world.ZoneChanged += handler;
            if (world.CurrentZoneId != 0) handler(world.CurrentZoneId);
        }

        /// <summary>
        /// Loads the zone's collision off the calling (network) thread and hands it over if the world is still there.
        /// </summary>
        public Task LoadForAsync(WorldState world, ushort zoneId)
        {
            if (zoneId == 0) return Task.CompletedTask;
            return Task.Run(() =>
            {
                try
                {
                    var collision = _loader(zoneId);
                    if (collision != null && world.CurrentZoneId == zoneId) world.Collision = collision;
                }
                catch (Exception ex)
                {
                    GordianLog.Warning("Collision", $"Loading zone {zoneId} collision failed: {ex.Message}");
                }
            });
        }

        public void Dispose()
        {
            _registry.SessionRegistered -= OnSessionRegistered;
            _registry.SessionUnregistered -= OnSessionUnregistered;
            foreach (var entry in _attached.Values) entry.World.ZoneChanged -= entry.Handler;
            _attached.Clear();
        }
    }
}
