// src/Gordian.Core/Network/Packets/CombatLogFormatter.cs
// Protocol specification referenced from LandSandBoat (https://github.com/LandSandBoat/server)
// and Atom0s XiPackets research (https://github.com/atom0s/XiPackets).

using System;
using System.Collections.Generic;

namespace Gordian.Core.Network.Packets
{
    /// <summary>
    /// Formatter mapping low-level FFXI combat action packets (0x028) and battle message notifications (0x029/0x02D)
    /// into human-readable in-game combat log entries.
    /// Operates zero-allocation on common paths and adheres to authentic FFXI client message phrasing.
    /// </summary>
    public static class CombatLogFormatter
    {
        private static readonly Dictionary<ushort, string> WellKnownSpells = new()
        {
            [1] = "Cure", [2] = "Cure II", [3] = "Cure III", [4] = "Cure IV", [5] = "Cure V", [6] = "Cure VI",
            [7] = "Curaga", [8] = "Curaga II", [9] = "Curaga III", [10] = "Curaga IV", [11] = "Curaga V",
            [14] = "Raise", [15] = "Raise II", [16] = "Raise III", [17] = "Reraise",
            [23] = "Dia", [24] = "Dia II", [25] = "Dia III", [28] = "Diaga",
            [33] = "Banish", [34] = "Banish II", [35] = "Banish III", [38] = "Banishga",
            [43] = "Protect", [44] = "Protect II", [45] = "Protect III", [46] = "Protect IV", [47] = "Protect V",
            [53] = "Shell", [54] = "Shell II", [55] = "Shell III", [56] = "Shell IV", [57] = "Shell V",
            [144] = "Fire", [145] = "Fire II", [146] = "Fire III", [147] = "Fire IV", [148] = "Fire V",
            [149] = "Blizzard", [150] = "Blizzard II", [151] = "Blizzard III", [152] = "Blizzard IV", [153] = "Blizzard V",
            [154] = "Aero", [155] = "Aero II", [156] = "Aero III", [157] = "Aero IV", [158] = "Aero V",
            [159] = "Stone", [160] = "Stone II", [161] = "Stone III", [162] = "Stone IV", [163] = "Stone V",
            [164] = "Thunder", [165] = "Thunder II", [166] = "Thunder III", [167] = "Thunder IV", [168] = "Thunder V",
            [169] = "Water", [170] = "Water II", [171] = "Water III", [172] = "Water IV", [173] = "Water V",
            [216] = "Poison", [217] = "Poison II", [220] = "Paralyze", [221] = "Paralyze II",
            [230] = "Bio", [231] = "Bio II", [232] = "Bio III", [245] = "Drain", [246] = "Aspir",
            [253] = "Warp", [254] = "Warp II", [261] = "Escape"
        };

        private static readonly Dictionary<ushort, string> WellKnownWeaponSkills = new()
        {
            [1] = "Combo", [2] = "Shoulder Tackle", [3] = "One Knee", [4] = "Raging Fists", [5] = "Spinning Attack", [6] = "Howling Fist", [7] = "Dragon Kick", [8] = "Asuran Fists",
            [16] = "Wasp Sting", [17] = "Viper Bite", [18] = "Shadowstitch", [19] = "Gust Slash", [20] = "Cyclone", [21] = "Energy Steal", [22] = "Energy Drain", [23] = "Dancing Edge", [24] = "Shark Bite", [25] = "Evisceration",
            [32] = "Fast Blade", [33] = "Burning Blade", [34] = "Red Lotus Blade", [35] = "Flat Blade", [36] = "Shining Blade", [37] = "Seraph Blade", [38] = "Circle Blade", [39] = "Spirits Within", [40] = "Vorpal Blade", [41] = "Swift Blade", [42] = "Savage Blade",
            [48] = "Hard Slash", [49] = "Power Slash", [50] = "Frostbite", [51] = "Freeze Bite", [52] = "Shockwave", [53] = "Crescent Moon", [54] = "Sickle Moon", [55] = "Spinning Slash", [56] = "Ground Strike"
        };

        private static readonly Dictionary<ushort, string> WellKnownJobAbilities = new()
        {
            [1] = "Mighty Strikes", [2] = "Hundred Fists", [3] = "Benediction", [4] = "Manafont",
            [5] = "Chainspell", [6] = "Perfect Dodge", [7] = "Invincible", [8] = "Blood Weapon",
            [16] = "Berserk", [17] = "Defender", [18] = "Warcry", [19] = "Aggressor", [20] = "Provoke",
            [21] = "Focus", [22] = "Dodge", [23] = "Chakra", [24] = "Boost", [25] = "Counterstance",
            [26] = "Steal", [27] = "Flee", [28] = "Hide", [29] = "Sneak Attack", [30] = "Mug", [31] = "Trick Attack"
        };

        /// <summary>
        /// Retrieves the localized name of a spell by ID, or formats a fallback identifier.
        /// </summary>
        public static string ResolveSpellName(ushort spellId, Func<ushort, string?>? customResolver = null)
        {
            if (customResolver != null && customResolver(spellId) is { } resolved && !string.IsNullOrWhiteSpace(resolved))
            {
                return resolved;
            }
            if (WellKnownSpells.TryGetValue(spellId, out var name))
            {
                return name;
            }
            return $"Spell #{spellId}";
        }

        /// <summary>
        /// Retrieves the localized name of a weapon skill by ID, or formats a fallback identifier.
        /// </summary>
        public static string ResolveWeaponSkillName(ushort wsId, Func<ushort, string?>? customResolver = null)
        {
            if (customResolver != null && customResolver(wsId) is { } resolved && !string.IsNullOrWhiteSpace(resolved))
            {
                return resolved;
            }
            if (WellKnownWeaponSkills.TryGetValue(wsId, out var name))
            {
                return name;
            }
            return $"Weapon Skill #{wsId}";
        }

        /// <summary>
        /// Retrieves the localized name of an ability by ID, or formats a fallback identifier.
        /// </summary>
        public static string ResolveAbilityName(ushort abilityId, Func<ushort, string?>? customResolver = null)
        {
            if (customResolver != null && customResolver(abilityId) is { } resolved && !string.IsNullOrWhiteSpace(resolved))
            {
                return resolved;
            }
            if (WellKnownJobAbilities.TryGetValue(abilityId, out var name))
            {
                return name;
            }
            return $"Ability #{abilityId}";
        }

        /// <summary>
        /// Formats an inbound S2C 0x029 / 0x02D BattleMessage record into user-facing combat text.
        /// </summary>
        public static string FormatBattleMessage(
            CombatMessageRecord record,
            Func<uint, string?> resolveEntityName,
            Func<ushort, string?>? resolveSpellName = null,
            Func<ushort, string?>? resolveAbilityName = null)
        {
            ArgumentNullException.ThrowIfNull(record);
            ArgumentNullException.ThrowIfNull(resolveEntityName);

            string caster = resolveEntityName(record.CasterId) ?? $"Entity_{record.CasterId:X}";
            string target = resolveEntityName(record.TargetId) ?? $"Entity_{record.TargetId:X}";

            return record.MessageId switch
            {
                1 => $"{caster} hits {target} for {record.Param} points of damage.",
                2 => $"{caster} casts {ResolveSpellName((ushort)record.Param, resolveSpellName)}. {target} takes {record.Value} points of damage.",
                3 => $"{caster} starts casting {ResolveSpellName((ushort)record.Param, resolveSpellName)}.",
                4 => $"{target} is out of range.",
                5 => $"Unable to see {target}.",
                6 => $"{caster} defeats {target}.",
                7 => $"{caster} casts {ResolveSpellName((ushort)record.Param, resolveSpellName)}. {target} recovers {record.Value} HP.",
                8 => $"{caster} gains {record.Param} experience points.",
                9 => $"{caster} attains level {record.Param}!",
                11 => $"{caster} falls to level {record.Param}.",
                12 => "Cannot attack. Your target is already claimed.",
                14 => $"{caster}'s attack is countered by {target}. A shadow absorbs the damage.",
                15 => $"{caster} misses {target}.",
                16 => $"{caster}'s casting is interrupted.",
                18 => "Unable to cast spells at this time.",
                19 => $"{caster} calls for help!",
                20 => $"The {target} falls to the ground.",
                24 => $"{target} recovers {record.Param} HP.",
                28 => $"{caster} uses item.",
                29 => $"{caster} is paralyzed.",
                30 => $"{target} anticipates the attack.",
                31 => $"A shadow absorbs the damage and disappears.",
                32 => $"{target} dodges the attack.",
                33 => $"{caster}'s attack is countered by {target}. {caster} takes {record.Param} points of damage.",
                34 => $"{caster} does not have enough MP to cast.",
                36 => $"You lose sight of {target}.",
                37 => "You are too far from the battle to gain experience.",
                38 => $"{target}'s skill rises by {record.Param} points.",
                43 => $"{caster} readies {ResolveWeaponSkillName((ushort)record.Param, resolveAbilityName)}.",
                44 => $"{target}'s spikes deal {record.Param} damage to {caster}.",
                47 => $"{caster} cannot cast spells.",
                50 => $"{caster} earns a merit point! (Total: {record.Param})",
                53 => $"{target}'s skill reaches level {record.Param}.",
                67 => $"{caster} scores a critical hit! {target} takes {record.Param} points of damage.",
                70 => $"{target} parries {caster}'s attack.",
                75 => $"{caster}'s spell has no effect on {target}.",
                78 => $"{target} is too far away.",
                84 => $"{target} is paralyzed.",
                85 => $"{caster} casts {ResolveSpellName((ushort)record.Param, resolveSpellName)}. {target} resists the spell.",
                87 or 88 => "Unable to use job ability.",
                89 => "Unable to use weaponskill.",
                94 => "You must wait longer to perform that action.",
                97 => $"{caster} was defeated by {target}.",
                100 or 101 => $"{caster} uses {ResolveAbilityName((ushort)record.Param, resolveAbilityName)}.",
                102 or 103 => $"{caster} uses {ResolveAbilityName((ushort)record.Param, resolveAbilityName)}. {target} recovers {record.Value} HP.",
                110 => $"{caster} uses {ResolveAbilityName((ushort)record.Param, resolveAbilityName)}. {target} takes {record.Value} points of damage.",
                114 => $"{caster} casts {ResolveSpellName((ushort)record.Param, resolveSpellName)} on {target}, but the spell fails to take effect.",
                158 => $"{caster} uses ability, but misses.",
                161 => $"Additional effect: {record.Param} HP drained from {target}.",
                162 => $"Additional effect: {record.Param} MP drained from {target}.",
                163 => $"Additional effect: {record.Param} points of damage.",
                185 => $"{caster} uses {ResolveWeaponSkillName((ushort)record.Param, resolveAbilityName)}. {target} takes {record.Value} points of damage.",
                186 => $"{caster} uses {ResolveWeaponSkillName((ushort)record.Param, resolveAbilityName)}. {target} gains effect.",
                187 => $"{caster} uses {ResolveWeaponSkillName((ushort)record.Param, resolveAbilityName)}. {record.Value} HP drained from {target}.",
                188 => $"{caster} uses {ResolveWeaponSkillName((ushort)record.Param, resolveAbilityName)}, but misses {target}.",
                189 => $"{caster} uses {ResolveWeaponSkillName((ushort)record.Param, resolveAbilityName)}. No effect on {target}.",
                191 => "The player is unable to use weapon skills.",
                192 => "Not enough TP.",
                224 => $"{caster} uses ability. {target} recovers {record.Param} MP.",
                227 => $"{caster} casts {ResolveSpellName((ushort)record.Param, resolveSpellName)}. {record.Value} HP drained from {target}.",
                229 => $"Additional effect: {target} takes {record.Param} additional points of damage.",
                252 => $"{caster} casts {ResolveSpellName((ushort)record.Param, resolveSpellName)}. Magic Burst! {target} takes {record.Value} points of damage.",
                253 => $"EXP chain #{record.Value}! {caster} gains {record.Param} experience points.",
                264 => $"{target} takes {record.Param} points of damage.",
                282 => $"{target} evades.",
                317 => $"{caster} uses ability. {target} takes {record.Param} points of damage.",
                324 => $"{caster} uses ability, but misses {target}.",
                327 => $"{caster} starts casting {ResolveSpellName((ushort)record.Param, resolveSpellName)} on {target}.",
                343 => $"{target}'s effect disappears!",
                352 => $"{caster} ranged attack hits {target} for {record.Param} points of damage.",
                353 => $"{caster} ranged attack scores a critical hit! {target} takes {record.Param} points of damage.",
                354 => $"{caster} ranged attack misses {target}.",
                371 => $"{caster} gains {record.Param} limit points.",
                372 => $"Limit chain #{record.Value}! {caster} gains {record.Param} limit points.",
                565 => $"{target} obtains {record.Param} gil.",
                576 => $"{caster} ranged attack hits {target} squarely for {record.Param} points of damage.",
                577 => $"{caster} ranged attack strikes true, pummeling {target} for {record.Param} points of damage!",
                _ => record.Param != 0
                    ? $"{caster} -> {target}: Msg#{record.MessageId} (Param={record.Param}, Val={record.Value})"
                    : $"{caster} -> {target}: Msg#{record.MessageId}"
            };
        }

        /// <summary>
        /// Formats an inbound S2C 0x028 CombatAction record into one or more user-facing combat log lines.
        /// </summary>
        public static List<string> FormatAction(
            CombatActionRecord record,
            Func<uint, string?> resolveEntityName,
            Func<ushort, string?>? resolveSpellName = null,
            Func<ushort, string?>? resolveAbilityName = null)
        {
            ArgumentNullException.ThrowIfNull(record);
            ArgumentNullException.ThrowIfNull(resolveEntityName);

            var lines = new List<string>(record.Targets.Count);
            string actor = resolveEntityName(record.ActorId) ?? $"Entity_{record.ActorId:X}";

            for (int t = 0; t < record.Targets.Count; t++)
            {
                var targetRecord = record.Targets[t];
                string target = resolveEntityName(targetRecord.TargetId) ?? $"Entity_{targetRecord.TargetId:X}";

                for (int r = 0; r < targetRecord.Results.Count; r++)
                {
                    var result = targetRecord.Results[r];

                    // Primary action resolution line
                    string primaryLine = FormatActionResult(record, actor, target, result, resolveSpellName, resolveAbilityName);
                    if (!string.IsNullOrEmpty(primaryLine))
                    {
                        lines.Add(primaryLine);
                    }

                    // Additional effect proc
                    if (result.HasProc)
                    {
                        string procLine = result.ProcParam > 0
                            ? $"Additional effect: {target} takes {result.ProcParam} points of {result.ProcKind} damage."
                            : $"Additional effect: {result.ProcKind}.";
                        lines.Add(procLine);
                    }

                    // Spikes / Reaction
                    if (result.HasReaction)
                    {
                        string reactLine = result.ReactionKind == ActionReactKind.Counter
                            ? $"{target} counters {actor}'s attack for {result.ReactionParam} points of damage."
                            : $"{target}'s {result.ReactionKind} deals {result.ReactionParam} damage to {actor}.";
                        lines.Add(reactLine);
                    }
                }
            }

            return lines;
        }

        private static string FormatActionResult(
            CombatActionRecord record,
            string actor,
            string target,
            in CombatActionResult result,
            Func<ushort, string?>? resolveSpellName,
            Func<ushort, string?>? resolveAbilityName)
        {
            return record.Category switch
            {
                ActionCategory.BasicAttack => result.Resolution switch
                {
                    ActionResolution.Hit => result.MessageId == 67
                        ? $"{actor} scores a critical hit! {target} takes {result.Param} points of damage."
                        : $"{actor} hits {target} for {result.Param} points of damage.",
                    ActionResolution.Miss => $"{actor} misses {target}.",
                    ActionResolution.Parry => $"{target} parries {actor}'s attack.",
                    ActionResolution.Guard => $"{actor} hits {target} for {result.Param} points of damage (guarded).",
                    ActionResolution.Block => $"{actor} hits {target} for {result.Param} points of damage (blocked).",
                    _ => $"{actor} hits {target} for {result.Param} points of damage."
                },

                ActionCategory.RangedFinish => result.Resolution switch
                {
                    ActionResolution.Hit => result.MessageId == 353
                        ? $"{actor} ranged attack scores a critical hit! {target} takes {result.Param} points of damage."
                        : $"{actor} ranged attack hits {target} for {result.Param} points of damage.",
                    _ => $"{actor} ranged attack misses {target}."
                },

                ActionCategory.MagicFinish => FormatMagicFinish(actor, target, record.ActionId, result.Param, result.MessageId, resolveSpellName),

                ActionCategory.SkillFinish => result.Resolution == ActionResolution.Miss || result.MessageId == 188
                    ? $"{actor} uses {ResolveWeaponSkillName((ushort)record.ActionId, resolveAbilityName)}, but misses {target}."
                    : $"{actor} uses {ResolveWeaponSkillName((ushort)record.ActionId, resolveAbilityName)}. {target} takes {result.Param} points of damage.",

                ActionCategory.AbilityFinish => result.Param > 0
                    ? $"{actor} uses {ResolveAbilityName((ushort)record.ActionId, resolveAbilityName)}. {target} takes {result.Param} points of damage."
                    : $"{actor} uses {ResolveAbilityName((ushort)record.ActionId, resolveAbilityName)} on {target}.",

                ActionCategory.ItemFinish => $"{actor} uses item on {target}.",

                _ => $"{actor} acts on {target} ({record.Category}): {result.Param} points."
            };
        }

        private static string FormatMagicFinish(
            string actor,
            string target,
            uint spellId,
            int param,
            ushort messageId,
            Func<ushort, string?>? resolveSpellName)
        {
            string spell = ResolveSpellName((ushort)spellId, resolveSpellName);

            // Healing spells (Cure, Curaga, etc.)
            if (messageId == 7 || messageId == 24 || (spellId >= 1 && spellId <= 11))
            {
                return $"{actor} casts {spell}. {target} recovers {param} HP.";
            }

            // Resisted / No effect
            if (messageId == 85)
            {
                return $"{actor} casts {spell}. {target} resists the spell.";
            }
            if (messageId == 75)
            {
                return $"{actor}'s {spell} has no effect on {target}.";
            }

            // Damage
            return $"{actor} casts {spell}. {target} takes {param} points of damage.";
        }
    }
}
