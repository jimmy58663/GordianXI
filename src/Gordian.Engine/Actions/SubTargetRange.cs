// src/Gordian.Engine/Actions/SubTargetRange.cs
using System;
using System.Numerics;
using Gordian.Core.Resources.Tables;
using Gordian.Core.World;

namespace Gordian.Engine.Actions
{
    /// <summary>How the sub-target cursor is coloured for the distance to the entity under it (#589).</summary>
    public enum SubTargetRangeBand
    {
        /// <summary>The action reaches the entity (and actions with no range: self-only, or no range known).</summary>
        InRange,

        /// <summary>Out of range by no more than <see cref="SubTargetRange.NearMissYalms"/>.</summary>
        JustOutOfRange,

        /// <summary>Further out than that.</summary>
        OutOfRange,
    }

    /// <summary>
    /// The sub-target cursor's range colour (#589): retail draws the cursor in its usual violet while the action reaches the
    /// entity under it, in yellow up to a yalm beyond, and in red further out. It is recomputed every frame (in the captures
    /// the colour followed the caster's position with the cursor up).
    /// <para>
    /// The action's range comes from the client's spell and command tables (<see cref="SpellRecord.Range"/>,
    /// <see cref="CommandRecord.Range"/>), an index into a yalm table: <see cref="RangeYalms"/>.
    /// </para>
    /// </summary>
    public static class SubTargetRange
    {
        /// <summary>The range index of an action that only lands on its user (no distance to check).</summary>
        public const int SelfOnlyIndex = 15;

        /// <summary>
        /// How far past the action's range the cursor still reads in range. Retail on local LSB (2026-11-15, #589; Retail
        /// Captures <c>589-subtarget-colour-*</c>), the target and the caster both 0.4-hitbox characters: Cure (20 yalms)
        /// stayed violet to 20.2 yalms centre to centre and turned yellow at 20.25; Divine Waltz (10 yalms) did the same at
        /// 10.2 / 10.25. The same 0.2 at both ranges, so it is an allowance, not a share of the range. PROVISIONAL: whether it
        /// is a flat 0.2 or half the target's 0.4 hitbox; the captures cannot tell them apart. LandSandBoat's server check adds
        /// both entities' hitboxes (0.8 here), so the client's colour is the stricter of the two.
        /// </summary>
        public const float RangeAllowanceYalms = 0.2f;

        /// <summary>
        /// How far past <see cref="RangeAllowanceYalms"/> the cursor stays yellow before it turns red: one yalm (same captures:
        /// yellow to 21.2 / 11.2, red from 21.25 / 11.25). The maintainer's Dia check (0-20 blue, 20.1-21.0 yellow) agrees
        /// within the Distance addon's rounding.
        /// </summary>
        public const float NearMissYalms = 1f;

        /// <summary>
        /// Yalms for each range index of the client's tables. <b>Beyond xi-tools:</b> paired with LandSandBoat's ranges for
        /// every spell and ability that has one (2026-11-15: <c>spell_list.spell_range</c> / 10 and <c>abilities.range</c>
        /// against the client's index, 1,084 records, no exceptions): 2 = 3 (melee reach: Provoke, the melee weapon skills),
        /// 3 = 4, 4 = 5, 7 = 8, 8 = 10, 9 = 12, 10 = 14, 11 = 16, 12 = 20 (Cure, Dia, nearly every spell). Indexes 1, 5, 6,
        /// 13 and 14 have no record in either table to pair with; 5 and 6 are filled by the step of one yalm the table has from
        /// 2 to 7, and 13 and 14 are PROVISIONAL guesses on the table's widening steps (no action uses them).
        /// </summary>
        private static readonly float[] Yalms = { 0, 2, 3, 4, 5, 6, 7, 8, 10, 12, 14, 16, 20, 25, 30, 0 };

        /// <summary>The range in yalms for a table range index; null for self-only (15) or an index out of the table.</summary>
        public static float? RangeYalms(int index) =>
            index > 0 && index < Yalms.Length && index != SelfOnlyIndex ? Yalms[index] : null;

        /// <summary>
        /// The range an item's use reaches. Items carry no range in the item DAT that the client reads here, and LandSandBoat
        /// checks none; PROVISIONAL at a spell's 20 yalms until retail shows otherwise.
        /// </summary>
        public const float ItemRangeYalms = 20f;

        /// <summary>The range in yalms of the action the cursor is for, or null when it has none to check.</summary>
        public static float? RangeOf(SubTargetSelection selection, ActionCatalog? catalog)
        {
            ArgumentNullException.ThrowIfNull(selection);
            if (selection.Kind == ActionKind.Item) return ItemRangeYalms;
            if (catalog == null) return null;
            int? index = selection.Kind switch
            {
                ActionKind.Spell => catalog.GetSpell(selection.Id)?.Range,
                ActionKind.JobAbility => catalog.GetJobAbility(selection.Id)?.Range,
                ActionKind.WeaponSkill => catalog.GetWeaponSkill(selection.Id)?.Range,
                _ => null,
            };
            return index is { } i ? RangeYalms(i) : null;
        }

        /// <summary>The distance the client measures against an action's range: centre to centre (the captures' distances).</summary>
        public static float DistanceTo(Vector3 user, WorldEntity target)
        {
            ArgumentNullException.ThrowIfNull(target);
            return Vector3.Distance(user, target.Position);
        }

        /// <summary>The band for a centre-to-centre distance against an action's range, both in yalms.</summary>
        public static SubTargetRangeBand Classify(float distance, float range) =>
            distance <= range + RangeAllowanceYalms ? SubTargetRangeBand.InRange
            : distance <= range + RangeAllowanceYalms + NearMissYalms ? SubTargetRangeBand.JustOutOfRange
            : SubTargetRangeBand.OutOfRange;

        /// <summary>The band for the cursor now: <see cref="SubTargetRangeBand.InRange"/> on yourself or for an action with no range.</summary>
        public static SubTargetRangeBand For(SubTargetSelection selection, ActionCatalog? catalog, WorldEntity? user)
        {
            ArgumentNullException.ThrowIfNull(selection);
            if (user == null || selection.Current.ServerId == user.ServerId) return SubTargetRangeBand.InRange;
            if (RangeOf(selection, catalog) is not { } range) return SubTargetRangeBand.InRange;
            return Classify(DistanceTo(user.Position, selection.Current), range);
        }
    }
}
