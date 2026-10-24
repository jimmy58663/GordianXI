// src/Gordian.Core/Actions/SubTargetSelection.cs
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Gordian.Engine.Input;
using Gordian.Core.World;

namespace Gordian.Engine.Actions
{
    /// <summary>
    /// The sub-target cursor (retail's <c>&lt;st&gt;</c>, and what every spell or ability chosen in a menu opens): an
    /// action waiting for its target. The cursor starts on the preferred entity (the command menu's target) when the
    /// action may land on it, else on the current target, else on yourself, else on the nearest entity it may land on;
    /// the target keys move it through the allowed entities in screen order (wrapping), Confirm runs the action on the
    /// one under it and Cancel drops it. Your own target does not change.
    /// <para>
    /// Retail capture 2026-10-11 (maintainer, a 30 fps video at 1:1): choosing Advancing March (target bits 0x01,
    /// yourself only) in the Magic list still opens the cursor; the lists leave the screen while it is up, the help bar
    /// reads "Advancing March" / "Select target of spell.", and the cursor is the target arrow in violet, drawn 12 px above
    /// the gold target arrow when both mark the same entity. After the cast the list is back with its cursor where it was.
    /// PROVISIONAL: the start rule beyond yourself, the screen-order cycling, and the list returning after Cancel.
    /// </para>
    /// </summary>
    public sealed class SubTargetSelection
    {
        public SubTargetSelection(string name, ActionTargetFlags allowed, WorldEntity start, Func<WorldEntity, Task<PlayerActionResult>> run)
        {
            Name = name ?? string.Empty;
            Allowed = allowed;
            Current = start ?? throw new ArgumentNullException(nameof(start));
            Run = run ?? throw new ArgumentNullException(nameof(run));
        }

        /// <summary>The action's name (the help bar's "Select target of spell." line names it).</summary>
        public string Name { get; }

        /// <summary>The entity kinds the cursor may land on.</summary>
        public ActionTargetFlags Allowed { get; }

        /// <summary>The entity under the cursor.</summary>
        public WorldEntity Current { get; internal set; }

        /// <summary>Runs the action on the picked entity (Confirm).</summary>
        public Func<WorldEntity, Task<PlayerActionResult>> Run { get; }

        /// <summary>Whether the stock menus were set aside for the cursor and come back when it ends (a menu choice).</summary>
        public bool ResumesMenus { get; init; }

        /// <summary>What waits: a spell, ability or weapon skill (<see cref="Id"/>), or an item (<see cref="ItemContainer"/> / <see cref="ItemSlot"/>).</summary>
        public ActionKind Kind { get; init; }

        /// <summary>The spell, ability or weapon skill id, or the item id for an item.</summary>
        public ushort Id { get; init; }

        /// <summary>Runs once when the cursor ends, after Confirm (the use sent) or Cancel: Lane B's Items list comes back through it.</summary>
        public Action? OnFinished { get; init; }

        /// <summary>
        /// The locked-on target the cursor unlocked to let the player pick (retail note C3, 2026-10-12); the lock comes back
        /// to it when the cursor ends, if it is still the target.
        /// </summary>
        public WorldEntity? RestoreLockOn { get; init; }

        /// <summary>The container and slot of the item being used (<see cref="ActionKind.Item"/> only).</summary>
        public Gordian.Core.Network.Packets.ContainerId ItemContainer { get; init; }
        public byte ItemSlot { get; init; }

        /// <summary>
        /// The server id the cursor moves to: the next candidate to the right (<paramref name="step"/> &gt; 0) or left in
        /// screen order from <paramref name="currentServerId"/>, wrapping at the ends; 0 when there is none.
        /// </summary>
        public static uint Step(IReadOnlyList<TargetCycling.Candidate> candidates, uint currentServerId, int step)
        {
            if (candidates.Count == 0) return 0;
            var ordered = new List<TargetCycling.Candidate>(candidates);
            ordered.Sort((a, b) =>
            {
                int byX = a.ScreenX.CompareTo(b.ScreenX);
                if (byX != 0) return byX;
                int byDistance = a.DistanceSquared.CompareTo(b.DistanceSquared);
                return byDistance != 0 ? byDistance : a.ServerId.CompareTo(b.ServerId);
            });
            int index = ordered.FindIndex(c => c.ServerId == currentServerId);
            if (index < 0) return ordered[step >= 0 ? 0 : ordered.Count - 1].ServerId;
            int next = ((index + Math.Sign(step)) % ordered.Count + ordered.Count) % ordered.Count;
            return ordered[next].ServerId;
        }
    }
}
