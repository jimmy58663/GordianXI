// src/Gordian.Engine/Actions/Pianissimo.cs
using System.Collections.Generic;
using System.Linq;
using Gordian.Core.Resources.Tables;

namespace Gordian.Engine.Actions
{
    /// <summary>
    /// Bard's Pianissimo: while it is on, the next party song lands on one party member instead of the whole party, so the
    /// sub-target cursor may move onto party members for a song the tables mark self-only (#589, D2.5: the maintainer's
    /// retail check, 2026-11-15 test round, range 20 yalms).
    /// <para>
    /// Rule referenced from LandSandBoat (https://github.com/LandSandBoat/server): <c>CCharEntity::ValidTarget</c> lets a
    /// spell whose target bits carry <c>TARGET_PLAYER_PARTY_PIANISSIMO</c> (0x80) land on another party member while the
    /// caster has status effect 409 (<c>xi::StatusEffect::Pianissimo</c>), and such a spell's area is
    /// <c>SPELLAOE_PIANISSIMO</c> (single target under Pianissimo). The client's spell table has no such bit: its songs read
    /// self (0x01) only. <b>Beyond LandSandBoat:</b> the client's party songs are the singing spells (skill 40) that target
    /// self with an area around the target (<see cref="AreaOfEffectShape.AroundTarget"/>); paired with LandSandBoat's
    /// <c>spell_list.validTargets</c> on 2026-11-15, all 64 of its 0x80 songs match this rule. <b>Differs from
    /// LandSandBoat:</b> the rule also takes the 14 Etudes (424-437), which LandSandBoat marks self-only (validTargets 1);
    /// retail lets Pianissimo put an Etude on a party member like any other song (the maintainer's retail check,
    /// 2026-11-15 test round 3).
    /// </para>
    /// <para>
    /// The song keeps its own range index (12, 20 yalms), so the cursor's range colour needs nothing extra, and its
    /// area-of-effect guide is still drawn around the member under the cursor (retail, 2026-11-15 test round 3).
    /// </para>
    /// </summary>
    public static class Pianissimo
    {
        /// <summary>Pianissimo's status effect id (LandSandBoat <c>xi::StatusEffect::Pianissimo</c>, scripts/enum/effect).</summary>
        public const ushort StatusEffectId = 409;

        /// <summary>The singing skill id of the spell table (<see cref="SpellRecord.Skill"/>).</summary>
        public const int SingingSkill = 40;

        /// <summary>Whether a spell is a party song, one Pianissimo can narrow onto a single party member.</summary>
        public static bool IsPartySong(SpellRecord? spell) =>
            spell != null && spell.Skill == SingingSkill
            && (ActionTargetFlags)(spell.TargetFlags & 0x3F) == ActionTargetFlags.Self
            && spell.AoeShape == AreaOfEffectShape.AroundTarget;

        /// <summary>Whether Pianissimo is among these status effects.</summary>
        public static bool IsActive(IEnumerable<ushort> statusEffects) => statusEffects.Contains(StatusEffectId);

        /// <summary>
        /// The targets a spell may take now: a party song under Pianissimo also takes party members
        /// (<see cref="ActionTargetFlags.Party"/>); everything else keeps its flags.
        /// </summary>
        public static ActionTargetFlags Targets(ActionTargetFlags flags, SpellRecord? spell, bool active) =>
            active && IsPartySong(spell) ? flags | ActionTargetFlags.Party : flags;
    }
}
