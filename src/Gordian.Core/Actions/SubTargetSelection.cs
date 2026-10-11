// src/Gordian.Core/Actions/SubTargetSelection.cs
using System;
using System.Collections.Generic;
using Gordian.Core.Input;
using Gordian.Core.World;

namespace Gordian.Core.Actions
{
    /// <summary>
    /// The sub-target cursor (retail's <c>&lt;st&gt;</c>): an action waiting for its target. The cursor starts on the
    /// current target when the action may land on it, else on yourself, else on the nearest entity it may land on; the
    /// target keys move it left and right through the allowed entities in screen order (wrapping), Confirm runs the
    /// action on the one under it and Cancel drops it. Your own target does not change. PROVISIONAL (no retail capture):
    /// the start rule, the screen-order cycling with wrapping, and that the cursor alone (not the target window) marks
    /// the entity.
    /// </summary>
    public sealed class SubTargetSelection
    {
        public SubTargetSelection(ActionKind kind, ushort id, string name, ActionTargetFlags allowed, WorldEntity start)
        {
            Kind = kind;
            Id = id;
            Name = name;
            Allowed = allowed;
            Current = start ?? throw new ArgumentNullException(nameof(start));
        }

        /// <summary>The action waiting: a spell, job ability or weapon skill (a ranged attack uses <see cref="ActionKind.WeaponSkill"/> with <see cref="IsRangedAttack"/>).</summary>
        public ActionKind Kind { get; }
        public ushort Id { get; }
        public string Name { get; }

        /// <summary>A ranged attack waiting for its target (Abilities' Ranged Atk. without one).</summary>
        public bool IsRangedAttack { get; init; }

        /// <summary>The entity kinds the cursor may land on.</summary>
        public ActionTargetFlags Allowed { get; }

        /// <summary>The entity under the cursor.</summary>
        public WorldEntity Current { get; internal set; }

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
