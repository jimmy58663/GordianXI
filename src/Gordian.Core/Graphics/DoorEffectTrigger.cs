// src/Gordian.Core/Graphics/DoorEffectTrigger.cs
using System;
using System.Collections.Generic;
using Gordian.Core.World;
using Gordian.Core.World.Collision;

namespace Gordian.Engine.Graphics
{
    /// <summary>
    /// Plays a door entity's own effect routines (#227) as its status changes, the way <see cref="DoorAnimator"/> plays
    /// its leaves: <c>intc</c> when the door is first seen closed, <c>into</c> when first seen open, <c>open</c> when
    /// the status turns to open (8) and <c>clos</c> when it leaves it. The routines are the ones of the door's directory
    /// (<c>&lt;zone code&gt;/door/&lt;door id&gt;</c>, <see cref="Resources.Models.ZoneGeometry.DoorDirectories"/>),
    /// played through the zone's <see cref="ZoneRoutinePlayer"/> with the door entity as both actors, so generators that
    /// ride an actor (ZoneActor0xA in Alzadaal's <c>_20t</c>) are placed on the door.
    /// <para>
    /// Alzadaal Undersea Ruins' door <c>_20t</c> (retail DAT, read 2026-10-18): <c>intc</c> and <c>clos</c> start
    /// <c>map1</c>, which starts the never-expiring meshes <c>g000</c>-<c>g009</c> about two yalms up the door; <c>open</c>
    /// starts <c>stat</c>: the sound <c>se01</c>, the burst <c>s001</c> (rings <c>f001</c> / <c>f002</c>, sparks
    /// <c>h000</c>-<c>h009</c>, <c>e001</c> / <c>e002</c>), <c>kill</c> and the closing flash <c>s000</c>. Door behaviour
    /// referenced from xi-tools (https://github.com/vekien/xi-tools, docs/zone/doors.md, after the PS2 client's
    /// <c>XiDoorActor</c>).
    /// </para>
    /// </summary>
    public sealed class DoorEffectTrigger
    {
        private readonly IReadOnlyDictionary<string, string> _directories;
        private readonly Dictionary<string, bool> _open = new(StringComparer.Ordinal);

        public DoorEffectTrigger(IReadOnlyDictionary<string, string> doorDirectories)
        {
            _directories = doorDirectories ?? throw new ArgumentNullException(nameof(doorDirectories));
        }

        /// <summary>The number of doors with effect routines.</summary>
        public int Count => _directories.Count;

        /// <summary>The directory of a door's routines, or null when it has none.</summary>
        public string? DirectoryOf(string doorId) => doorId != null && _directories.TryGetValue(doorId, out var dir) ? dir : null;

        /// <summary>Plays the routines of doors whose entity was first seen or changed status since the last update.</summary>
        public void Update(WorldState world, ZoneRoutinePlayer player)
        {
            ArgumentNullException.ThrowIfNull(world);
            ArgumentNullException.ThrowIfNull(player);
            if (_directories.Count == 0) return;
            foreach (var (id, entity) in ZoneDoors.DoorEntities(world))
            {
                Observe(id, entity.ServerId, ZoneDoors.StatusOf(entity) == ZoneDoors.StatusOpen, player);
            }
        }

        /// <summary>Sets a door's state as its entity would (an event's 0x2D naming the door's open / clos, tests).</summary>
        public void Observe(string doorId, uint doorServerId, bool open, ZoneRoutinePlayer player)
        {
            if (!_directories.TryGetValue(doorId, out var directory)) return;
            bool first = !_open.TryGetValue(doorId, out bool was);
            if (!first && was == open) return;
            _open[doorId] = open;
            string routine = first ? (open ? "into" : "intc") : (open ? "open" : "clos");
            player.PlayAt(directory, routine, new ZoneRoutineActors(doorServerId, doorServerId));
        }

        /// <summary>
        /// Plays a door routine an event names (0x2D on the door entity with <c>open</c> / <c>clos</c> / <c>into</c> /
        /// <c>intc</c>, #233): <c>open</c> and <c>clos</c> also become the door's state, so the status the event sets
        /// next does not play them again. False when the door has no such routine.
        /// </summary>
        public bool Play(string doorId, uint doorServerId, string routine, ZoneRoutinePlayer player)
        {
            ArgumentNullException.ThrowIfNull(player);
            if (doorId == null || !_directories.TryGetValue(doorId, out var directory)) return false;
            if (string.Equals(routine, "open", StringComparison.Ordinal)) _open[doorId] = true;
            else if (string.Equals(routine, "clos", StringComparison.Ordinal)) _open[doorId] = false;
            return player.PlayAt(directory, routine, new ZoneRoutineActors(doorServerId, doorServerId));
        }

        /// <summary>Whether <paramref name="routine"/> is one of a door's own routines (<c>open</c>, <c>clos</c>, <c>into</c>, <c>intc</c>).</summary>
        public static bool IsDoorRoutine(string routine) => routine is "open" or "clos" or "into" or "intc";

        /// <summary>Forgets every door's state (the entities left: a zone change).</summary>
        public void Reset() => _open.Clear();
    }
}
