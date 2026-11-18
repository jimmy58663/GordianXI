// src/Gordian.Engine/Graphics/MountEffectTracker.cs
using System.Collections.Generic;
using Gordian.Core.Resources;
using Gordian.Core.World;

namespace Gordian.Engine.Graphics
{
    /// <summary>
    /// Follows each ridden mount's own routine (<see cref="WorldEntity.MountAnimation"/>, #437) and reports the routine it
    /// starts, so the mount's DAT effects for it can play: the chocobo's dig <c>chor</c> (<c>ROM/169/11</c>) starts the
    /// generators <c>chov</c>, <c>cho1</c> and <c>cho0</c> at ticks 64, 78, 102 and 110 (op 0x02, read 2026-11-18).
    /// A mount first seen plays nothing it was already doing, as <see cref="ActionEffectPlayer"/> does for actors.
    /// Mounts are keyed by <see cref="MountModels.ActorKey"/>.
    /// </summary>
    public sealed class MountEffectTracker
    {
        private readonly Dictionary<uint, int> _serials = new();
        private readonly HashSet<uint> _seen = new();
        private readonly List<uint> _stale = new();

        /// <summary>
        /// Advances <paramref name="entity"/>'s mount (render thread, once per frame); returns the routine its mount started
        /// since the last call, or null. Nothing for a player not riding.
        /// </summary>
        public string? Step(WorldEntity entity)
        {
            if (!entity.Mount.IsMounted) return null;
            uint key = MountModels.ActorKey(entity.ServerId);
            _seen.Add(key);
            var animation = entity.MountAnimation;
            int serial = animation.ActionSerial;
            bool known = _serials.TryGetValue(key, out int last);
            _serials[key] = serial;
            if (!known || serial == last) return null;
            return animation.ActiveRoutine?.Name;
        }

        /// <summary>Forgets the mounts not stepped since the last call (call once per frame).</summary>
        public void EndFrame()
        {
            _stale.Clear();
            foreach (uint key in _serials.Keys)
            {
                if (!_seen.Contains(key)) _stale.Add(key);
            }
            foreach (uint key in _stale) _serials.Remove(key);
            _seen.Clear();
        }

        /// <summary>Forgets every mount (a zone change, another character's world shown).</summary>
        public void Clear()
        {
            _serials.Clear();
            _seen.Clear();
        }
    }
}
