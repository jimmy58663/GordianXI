// src/Gordian.Core/Ui/StockUiActionMenu.cs
using System;
using System.Collections.Generic;
using System.Globalization;
using Gordian.Core.Actions;
using Gordian.Core.Network.Packets;
using Gordian.Core.Resources.Tables;

namespace Gordian.Core.Ui
{
    /// <summary>Which client-drawn action list a window shows.</summary>
    public enum StockUiActionList
    {
        None = 0,
        /// <summary>Spells of one category (<see cref="StockUiOpenMenu.ActionCategory"/> is the spell kind).</summary>
        Spells,
        JobAbilities,
        WeaponSkills,
    }

    /// <summary>What a Magic or Abilities entry asks the action service to do.</summary>
    public enum StockUiActionType
    {
        Spell,
        JobAbility,
        WeaponSkill,
        RangedAttack,
    }

    /// <summary>
    /// An action chosen in the Magic or Abilities windows: what, which id, its name, its target bits, and the target of
    /// the command menu it was opened from (null from the main menu, where the action service picks the target).
    /// </summary>
    public readonly record struct StockUiActionRequest(StockUiActionType Type, ushort Id, string Name, ActionTargetFlags TargetFlags,
        StockUiTargetContext? Target);

    /// <summary>
    /// One row of a spell or ability list: the action, its name, the value at the right (MP cost), its recast's end
    /// (<see cref="System.Diagnostics.Stopwatch"/> ticks, 0 when ready) and why it is greyed (null when usable).
    /// </summary>
    public readonly record struct StockUiActionRow(StockUiActionType Type, ushort Id, string Name, ActionTargetFlags TargetFlags,
        string? Value, long RecastEndsAt, string? GreyedReason)
    {
        /// <summary>Whether the row is greyed at <paramref name="now"/> (a reason, or a recast still running).</summary>
        public bool IsGreyed(long now) => GreyedReason != null || RecastEndsAt > now;

        /// <summary>The time left on the row's recast at <paramref name="now"/>.</summary>
        public TimeSpan RecastLeft(long now) => RecastEndsAt > now
            ? TimeSpan.FromSeconds((RecastEndsAt - now) / (double)System.Diagnostics.Stopwatch.Frequency)
            : TimeSpan.Zero;
    }

    /// <summary>The character state the action lists are composed from (the action service fills it).</summary>
    public sealed class StockUiActionState
    {
        public Func<ushort, bool> HasSpell { get; init; } = _ => false;
        public Func<ushort, bool> HasJobAbility { get; init; } = _ => false;
        public Func<ushort, bool> HasWeaponSkill { get; init; } = _ => false;
        public JobId MainJob { get; init; }
        public int MainJobLevel { get; init; }
        public JobId SubJob { get; init; }
        public int SubJobLevel { get; init; }
        public int CurrentMp { get; init; }
        public int CurrentTp { get; init; }

        /// <summary>The time left on a spell's recast (S2C 0x028 category 4).</summary>
        public Func<ushort, TimeSpan> SpellRecast { get; init; } = _ => TimeSpan.Zero;

        /// <summary>The time left on a recast id (S2C 0x119).</summary>
        public Func<int, TimeSpan> AbilityRecast { get; init; } = _ => TimeSpan.Zero;

        /// <summary>The clock rows' recast ends are stamped with (<see cref="System.Diagnostics.Stopwatch"/> ticks).</summary>
        public long Now { get; init; } = System.Diagnostics.Stopwatch.GetTimestamp();
    }

    /// <summary>
    /// The stock Magic and Abilities windows (#240, #353), as the English menu DAT (ROM/119/51) authors them:
    /// <list type="bullet">
    /// <item><c>mgcmenu</c> (top right, title 76 "Magic Spells"): White Magic, Black Magic, Songs, Ninjutsu, Summoning, Blue
    /// Magic, Geomancy, Trust (keytops3 #8, #9, #14, #12, #11, #15, #496, #505; help 303-307, 637, 698, 718), opened by
    /// the main menu's Magic. <c>magselec</c> (bottom left 128,176) is the same list without Trust, beside the target
    /// command menu, opened by its Magic.</item>
    /// <item><c>abimenu</c> (top right, title 68 "Abilities"): Job Traits, Job Abil., Weapon Skl., Ranged Atk., Mount, Pet
    /// Command (help 279-282, 996, 505), opened by the main menu's Abilities; <c>abiselec</c> (bottom left 16,206) is the
    /// command menu's version without Job Traits.</item>
    /// <item><c>magic</c> and <c>ability</c> (bottom left 16,96, 182 x 200): twelve invisible 132 x 16 rows from (16,4),
    /// the lists the client fills, the end rows linking to themselves.</item>
    /// </list>
    /// Labels read from the composited menus (<c>GORDIAN_UI_DUMP_MENUS=mgcmenu,magselec,abimenu,abiselec</c>, 2026-10-11).
    /// The spells listed are the learned ones (S2C 0x0AA) of the category the main or support job can use (the spell
    /// table's per-job levels), sorted by the table's menu index; job abilities and weapon skills are the ones S2C 0x0AC
    /// lists, in id order. PROVISIONAL (no retail capture yet): the rows' text insets, the MP cost at the right, the
    /// recast shown at the right of job abilities, spells your level is too low for listed greyed, the greyed rules
    /// (level, MP, TP under 1000 for weapon skills, a running recast), the notices, and the lists closing once an
    /// action is chosen.
    /// </summary>
    public static class StockUiActionMenu
    {
        public const string MagicCategoryMenu = "mgcmenu";
        public const string CommandMagicCategoryMenu = "magselec";
        public const string AbilityMenu = "abimenu";
        public const string CommandAbilityMenu = "abiselec";
        public const string SpellListMenu = "magic";
        public const string AbilityListMenu = "ability";

        /// <summary>Rows the list windows show at once (their twelve row buttons).</summary>
        public const int VisibleRows = 12;

        /// <summary>The TP a weapon skill takes.</summary>
        public const int WeaponSkillTp = 1000;

        /// <summary>Spell kinds (<see cref="SpellRecord.Kind"/>) by the category list's button: the DAT's order differs from the kinds'.</summary>
        public static readonly IReadOnlyDictionary<int, int> CategoryKinds = new Dictionary<int, int>
        {
            [1] = 1, // White Magic
            [2] = 2, // Black Magic
            [3] = 5, // Songs
            [4] = 4, // Ninjutsu
            [5] = 3, // Summoning
            [6] = 6, // Blue Magic
            [7] = 7, // Geomancy
            [8] = 8, // Trust (mgcmenu only)
        };

        public const int TrustKind = 8;

        /// <summary>The category names, by spell kind, for notices.</summary>
        public static string CategoryName(int kind) => kind switch
        {
            1 => "White Magic",
            2 => "Black Magic",
            3 => "Summoning",
            4 => "Ninjutsu",
            5 => "Songs",
            6 => "Blue Magic",
            7 => "Geomancy",
            8 => "Trust",
            _ => "Magic",
        };

        /// <summary>The spells of a category the character can see in its list.</summary>
        public static List<StockUiActionRow> SpellRows(ActionCatalog catalog, StockUiActionState state, int kind, StockUiTargetContext? target)
        {
            var found = new List<(int Order, StockUiActionRow Row)>();
            var spells = catalog.Tables.Spells;
            for (int id = 0; id < spells.Count; id++)
            {
                var spell = spells[id];
                if (spell == null || spell.Kind != kind || !state.HasSpell((ushort)id) || catalog.SpellName(id) is not { } name) continue;
                int? main = spell.LevelFor((int)state.MainJob), sub = spell.LevelFor((int)state.SubJob);
                bool trust = kind == TrustKind;
                if (!trust && main == null && sub == null) continue; // neither job can use it
                string? reason = null;
                if (!trust && !(main is { } m && m <= state.MainJobLevel) && !(sub is { } s && s <= state.SubJobLevel))
                {
                    reason = $"You cannot cast {name} at your current level.";
                }
                else if (spell.MpCost > state.CurrentMp)
                {
                    reason = $"You do not have enough MP to cast {name}.";
                }
                var flags = (ActionTargetFlags)spell.TargetFlags;
                reason ??= TargetReason(flags, target, name);
                long ends = EndsAt(state, state.SpellRecast((ushort)id));
                string? value = spell.MpCost > 0 ? spell.MpCost.ToString(CultureInfo.InvariantCulture) : null;
                found.Add((spell.MenuIndex, new StockUiActionRow(StockUiActionType.Spell, (ushort)id, name, flags, value, ends, reason)));
            }
            found.Sort((a, b) => a.Order != b.Order ? a.Order.CompareTo(b.Order) : a.Row.Id.CompareTo(b.Row.Id));
            return found.ConvertAll(f => f.Row);
        }

        /// <summary>
        /// The job abilities S2C 0x0AC lists, in id order (pet commands and blood pacts, types 2, 6 and 10, belong to Pet
        /// Command and are left out).
        /// </summary>
        public static List<StockUiActionRow> JobAbilityRows(ActionCatalog catalog, StockUiActionState state, StockUiTargetContext? target)
        {
            var rows = new List<StockUiActionRow>();
            for (int id = 0; id < CommandRecord.JobAbilityBase; id++)
            {
                if (!state.HasJobAbility((ushort)id) || catalog.GetJobAbility(id) is not { } record || catalog.JobAbilityName(id) is not { } name) continue;
                if (record.Type is CommandRecord.TraitType or CommandRecord.WeaponSkillType or 2 or 6 or 10) continue;
                var flags = (ActionTargetFlags)record.TargetFlags;
                string? reason = record.TpCost > 0 && state.CurrentTp < record.TpCost ? $"You do not have enough TP to use {name}." : null;
                reason ??= TargetReason(flags, target, name);
                var left = state.AbilityRecast(record.RecastId);
                rows.Add(new StockUiActionRow(StockUiActionType.JobAbility, (ushort)id, name, flags, null, EndsAt(state, left), reason));
            }
            return rows;
        }

        /// <summary>The weapon skills S2C 0x0AC lists, in id order, greyed under 1000 TP.</summary>
        public static List<StockUiActionRow> WeaponSkillRows(ActionCatalog catalog, StockUiActionState state, StockUiTargetContext? target)
        {
            var rows = new List<StockUiActionRow>();
            for (int id = 0; id < 256; id++)
            {
                if (!state.HasWeaponSkill((ushort)id) || catalog.GetWeaponSkill(id) is not { } record || catalog.WeaponSkillName(id) is not { } name) continue;
                var flags = (ActionTargetFlags)record.TargetFlags;
                string? reason = state.CurrentTp < WeaponSkillTp ? $"You do not have enough TP to use {name}." : null;
                reason ??= TargetReason(flags, target, name);
                rows.Add(new StockUiActionRow(StockUiActionType.WeaponSkill, (ushort)id, name, flags, null, 0, reason));
            }
            return rows;
        }

        private static long EndsAt(StockUiActionState state, TimeSpan left) =>
            left > TimeSpan.Zero ? state.Now + (long)(left.TotalSeconds * System.Diagnostics.Stopwatch.Frequency) : 0;

        /// <summary>Why an action cannot be used on the command menu's target (null when it can, or without one).</summary>
        private static string? TargetReason(ActionTargetFlags flags, StockUiTargetContext? target, string name)
        {
            if (target is not { } t || flags == ActionTargetFlags.None || Accepts(flags, t.Kind)) return null;
            return $"You cannot use {name} on {t.TargetName}.";
        }

        /// <summary>
        /// Whether an action's target bits allow a command-menu target of this kind. A player may be in your party or
        /// alliance or neither, so any of those bits will do; pets and trusts count as party members (PROVISIONAL).
        /// </summary>
        public static bool Accepts(ActionTargetFlags flags, StockUiTargetKind kind) => kind switch
        {
            StockUiTargetKind.Self => (flags & ActionTargetFlags.Self) != 0,
            StockUiTargetKind.Player => (flags & (ActionTargetFlags.Player | ActionTargetFlags.Party | ActionTargetFlags.Alliance)) != 0,
            StockUiTargetKind.Pet or StockUiTargetKind.Trust => (flags & (ActionTargetFlags.Player | ActionTargetFlags.Party | ActionTargetFlags.Alliance)) != 0,
            StockUiTargetKind.Monster => (flags & ActionTargetFlags.Enemy) != 0,
            _ => false,
        };

        /// <summary>A recast as the lists show it: "m:ss" (rounded up), or nothing when ready.</summary>
        public static string FormatRecast(TimeSpan left)
        {
            if (left <= TimeSpan.Zero) return string.Empty;
            int seconds = (int)Math.Ceiling(left.TotalSeconds);
            return seconds >= 3600
                ? string.Create(CultureInfo.InvariantCulture, $"{seconds / 3600}:{seconds / 60 % 60:00}:{seconds % 60:00}")
                : string.Create(CultureInfo.InvariantCulture, $"{seconds / 60}:{seconds % 60:00}");
        }
    }
}
