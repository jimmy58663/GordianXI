// src/Gordian.Core/Ui/StockUiActionMenu.cs
using System;
using System.Collections.Generic;
using System.Globalization;
using Gordian.Engine.Actions;
using Gordian.Core.Network.Packets;
using Gordian.Core.Resources.Tables;

namespace Gordian.Engine.Ui
{
    /// <summary>Which client-drawn action list a window shows.</summary>
    public enum StockUiActionList
    {
        None = 0,
        /// <summary>Spells of one kind (<see cref="StockUiOpenMenu.ActionCategory"/>), or of every kind but Trust (category 0).</summary>
        Spells,
        JobAbilities,
        WeaponSkills,
        JobTraits,
        PetCommands,
        Mounts,
        /// <summary>
        /// A pet command's own list (retail image-41 / -42, 2026-10-12): Blood Pact: Rage (command type 6), Blood Pact:
        /// Ward (10) or Ready (18); <see cref="StockUiOpenMenu.ActionCategory"/> is the command type.
        /// </summary>
        PetAbilities,
    }

    /// <summary>What a Magic or Abilities entry asks the action service to do.</summary>
    public enum StockUiActionType
    {
        Spell,
        JobAbility,
        WeaponSkill,
        RangedAttack,
        /// <summary>A job trait: listed only, nothing to use.</summary>
        Trait,
        /// <summary>A pet command that opens a list of its own (Blood Pact: Rage / Ward, Ready): Confirm opens it.</summary>
        PetList,
        /// <summary>A mount of the mount list (#87): Confirm calls it forth (C2S 0x01A Mount with the mount id).</summary>
        Mount,
    }

    /// <summary>
    /// An action chosen in the Magic or Abilities windows: what, which id, its name, its target bits, and the target of
    /// the command menu it was opened from (null from the main menu).
    /// </summary>
    public readonly record struct StockUiActionRequest(StockUiActionType Type, ushort Id, string Name, ActionTargetFlags TargetFlags,
        StockUiTargetContext? Target);

    /// <summary>
    /// One row of a spell or ability list: the action, its name, its icon (an image of the <c>magiconw</c> group, -1 for
    /// none) and the small icon to fall back on, the MP it costs (spells; -1 otherwise), its recast's end
    /// (<see cref="System.Diagnostics.Stopwatch"/> ticks, 0 when ready), why it is greyed (null when usable), whether a
    /// running recast greys it (job abilities, retail D2 note 2026-10-12), its help line, and for a pet command that opens
    /// a list, that list's command type.
    /// </summary>
    public readonly record struct StockUiActionRow(StockUiActionType Type, ushort Id, string Name, ActionTargetFlags TargetFlags,
        int Icon, int MpCost, long RecastEndsAt, string? GreyedReason, int SmallIcon = -1, bool GreyedInRecast = false,
        string? Help = null, int OpensPetList = 0)
    {
        /// <summary>Whether the row is greyed by a reason (level, MP, TP, out of combat).</summary>
        public bool IsGreyed => GreyedReason != null;

        /// <summary>Whether the row is greyed at <paramref name="now"/>: by a reason, or by a running recast where that greys it.</summary>
        public bool IsGreyedAt(long now) => GreyedReason != null || (GreyedInRecast && RecastEndsAt > now);

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
        public Func<ushort, bool> HasTrait { get; init; } = _ => false;
        public Func<ushort, bool> HasPetAbility { get; init; } = _ => false;
        public JobId MainJob { get; init; }
        public int MainJobLevel { get; init; }
        public JobId SubJob { get; init; }
        public int SubJobLevel { get; init; }
        public int CurrentMp { get; init; }
        public int CurrentTp { get; init; }

        /// <summary>Whether the character is engaged (weapon skills are greyed otherwise).</summary>
        public bool Engaged { get; init; }

        /// <summary>The time left on a spell's recast (S2C 0x028 category 4).</summary>
        public Func<ushort, TimeSpan> SpellRecast { get; init; } = _ => TimeSpan.Zero;

        /// <summary>The time left on a recast id (S2C 0x119).</summary>
        public Func<int, TimeSpan> AbilityRecast { get; init; } = _ => TimeSpan.Zero;

        /// <summary>The clock rows' recast ends are stamped with (<see cref="System.Diagnostics.Stopwatch"/> ticks).</summary>
        public long Now { get; init; } = System.Diagnostics.Stopwatch.GetTimestamp();
    }

    /// <summary>
    /// The stock Magic and Abilities windows (#240, #353), from the English menu DAT (ROM/119/51) and the maintainer's
    /// retail captures of 2026-10-11 (2560 x 1440, UI 1:1; image-13 ... image-23 and a 30 fps video):
    /// <list type="bullet">
    /// <item><c>mgcmenu</c> (top right, "Magic Spells"): White Magic, Black Magic, Songs, Ninjutsu, Summoning, Blue Magic,
    /// Geomancy, Trust, opened by the main menu's Magic. Each lists every spell of that kind you have learned, the ones
    /// your jobs and levels cannot cast greyed (Foe Requiem VIII on a Bard below its level), in the spell table's menu
    /// order (Foe Requiem VIII, menu 738, before Foe Lullaby, 739, although its id is lower).</item>
    /// <item>The target command menu's Magic: Confirm lists all the magic you can cast (every kind but Trust); right
    /// opens <c>magselec</c> (beside the menu, the categories without Trust), whose entries list the castable spells of
    /// that kind. Its Trust lists the trusts.</item>
    /// <item><c>abimenu</c> (top right, "Abilities"): Job Traits, Job Abil., Weapon Skl., Ranged Atk. (a ranged attack),
    /// Mount, Pet Command; <c>abiselec</c> is the command menu's version without Job Traits.</item>
    /// <item><c>magic</c> / <c>ability</c> (182 x 200, twelve 16 px rows): each row a 16 x 16 icon at the row's origin and
    /// the name in the log font 19 px in; no value column. An empty list shows "No abilities available." / "No mounts
    /// available." in the window. The MP cost, the recast ("Next") or your TP show in the small window
    /// (<c>subwindo</c>) above the target window.</item>
    /// </list>
    /// </summary>
    public static class StockUiActionMenu
    {
        public const string MagicCategoryMenu = "mgcmenu";
        public const string CommandMagicCategoryMenu = "magselec";
        public const string AbilityMenu = "abimenu";
        public const string CommandAbilityMenu = "abiselec";
        public const string SpellListMenu = "magic";
        public const string AbilityListMenu = "ability";

        /// <summary>
        /// The mount list window (#87): the DAT's own <c>mount</c> menu, laid out as <c>ability</c> (182 x 200, twelve 132 x 16
        /// rows from (16, 4), each with the <c>keytops3</c> #49 icon), as the retail capture shows it (Mounts.mp4, 30 s).
        /// </summary>
        public const string MountListMenu = "mount";

        /// <summary>The main-menu spell lists' Options window (+ / Y): "+ : Sort", Auto, Manual; Blue Magic's adds a greyed "Set Spells".</summary>
        public const string OptionsMenu = "mgcsort", BlueOptionsMenu = "mgcsortw";
        public const int OptionsAutoButton = 1, OptionsManualButton = 2;

        /// <summary>The small window over the target window that shows the MP cost, the recast or your TP (112 x 42).</summary>
        public const string InfoMenu = "subwindo";

        /// <summary>The element group the list icons come from (ROM/280/15, file id 39551: 677 images over 32 x 32 textures).</summary>
        public const string IconGroup = "magiconw";

        /// <summary>Rows the list windows show at once (their twelve row buttons).</summary>
        public const int VisibleRows = 12;

        /// <summary>The TP a weapon skill takes.</summary>
        public const int WeaponSkillTp = 1000;

        /// <summary>Command table bases: pet commands (blood pacts, Ready) at 1024 + the S2C 0x0AC pet bit, job traits at 1536 + trait id.</summary>
        public const int PetCommandBase = 1024, TraitBase = 1536;

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

        public const int TrustKind = 8, BlueMagicKind = 6;

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

        /// <summary>
        /// The mount list (#87): the unlocked mounts of S2C 0x0AE in mount id order, named by the mount name table
        /// (ROM/351/84, row = mount id) and skipping ids it has no name for (id 63, the reserved slots), with the help line
        /// of ROM/351/85 for the help bar. Never greyed (retail capture Mounts.mp4: a GM with every mount, all white); one
        /// recast for all (S2C 0x119 <c>MountRecast</c>), shown as the info window's "Next".
        /// </summary>
        public static List<StockUiActionRow> MountRows(IReadOnlyList<int> unlocked, Func<int, string?> name, Func<int, string?> help,
            TimeSpan recast, long now)
        {
            long ends = recast > TimeSpan.Zero ? now + (long)(recast.TotalSeconds * System.Diagnostics.Stopwatch.Frequency) : 0;
            var rows = new List<StockUiActionRow>(unlocked.Count);
            foreach (int id in unlocked)
            {
                if (id is < 0 or > ushort.MaxValue || name(id) is not { Length: > 0 } mountName) continue;
                rows.Add(new StockUiActionRow(StockUiActionType.Mount, (ushort)id, mountName, ActionTargetFlags.Self, -1, -1, ends, null,
                    Help: help(id)));
            }
            return rows;
        }

        /// <summary>
        /// What an empty list shows in its window: retail's "No mounts available." and "No abilities available." (Pet
        /// Command) from the captures; the others are guesses in the same wording.
        /// </summary>
        public static string EmptyText(StockUiActionList list) => list switch
        {
            StockUiActionList.Mounts => "No mounts available.",
            StockUiActionList.Spells => "No magic available.",
            StockUiActionList.WeaponSkills => "No weapon skills available.",
            StockUiActionList.JobTraits => "No job traits available.",
            _ => "No abilities available.",
        };

        /// <summary>
        /// The spells of a kind (0: every kind but Trust). From the main menu (<paramref name="usableOnly"/> false) every
        /// learned spell, greyed when neither job can cast it at its level; from the command menu every spell your jobs can
        /// cast, whatever its target can take (retail note C5, 2026-10-12: the cursor then picks a valid target). Sorted
        /// by the spell table's menu index. The icon is the spell's picture (+0x42) when it has one (retail image-39, the
        /// Cures), else its small icon (+0x40: the songs' notes, image-14).
        /// </summary>
        public static List<StockUiActionRow> SpellRows(ActionCatalog catalog, StockUiActionState state, int kind, StockUiTargetContext? target,
            bool usableOnly = false)
        {
            var found = new List<(int Order, StockUiActionRow Row)>();
            var spells = catalog.Tables.Spells;
            for (int id = 0; id < spells.Count; id++)
            {
                var spell = spells[id];
                if (spell == null || !state.HasSpell((ushort)id) || catalog.SpellName(id) is not { } name) continue;
                if (kind == 0 ? spell.Kind == TrustKind : spell.Kind != kind) continue;
                int? main = spell.LevelFor((int)state.MainJob), sub = spell.LevelFor((int)state.SubJob);
                bool trust = spell.Kind == TrustKind;
                bool castable = trust || (main is { } m && m <= state.MainJobLevel) || (sub is { } s && s <= state.SubJobLevel);
                if (usableOnly && !castable) continue;
                string? reason = !castable ? $"You cannot cast {name} on your current job and level." : null;
                if (reason == null && spell.MpCost > state.CurrentMp) reason = $"You do not have enough MP to cast {name}.";
                var flags = (ActionTargetFlags)(spell.TargetFlags & 0x3F);
                long ends = EndsAt(state, state.SpellRecast((ushort)id));
                int icon = spell.Icon2 != 0xFFFF ? spell.Icon2 : spell.Icon;
                found.Add((spell.MenuIndex, new StockUiActionRow(StockUiActionType.Spell, (ushort)id, name, flags, icon, spell.MpCost, ends, reason,
                    SmallIcon: spell.Icon, GreyedInRecast: true, Help: catalog.SpellHelp(id))));
            }
            found.Sort((a, b) => a.Order != b.Order ? a.Order.CompareTo(b.Order) : a.Row.Id.CompareTo(b.Row.Id));
            return found.ConvertAll(f => f.Row);
        }

        /// <summary>
        /// The job abilities S2C 0x0AC lists, in id order (pet commands, type 2, are under Pet Command); greyed while in
        /// recast or short of TP, never by the target (retail note D2, 2026-10-12: Chi Blast stays usable with yourself
        /// targeted; the cursor then picks a monster).
        /// </summary>
        public static List<StockUiActionRow> JobAbilityRows(ActionCatalog catalog, StockUiActionState state, StockUiTargetContext? target)
        {
            var rows = new List<StockUiActionRow>();
            for (int id = 0; id < CommandRecord.JobAbilityBase; id++)
            {
                if (!state.HasJobAbility((ushort)id) || catalog.GetJobAbility(id) is not { } record || catalog.JobAbilityName(id) is not { } name) continue;
                if (record.Type is CommandRecord.TraitType or CommandRecord.WeaponSkillType or 2 or 6 or 10) continue;
                rows.Add(AbilityRow(catalog, state, record, (ushort)id, name));
            }
            return rows;
        }

        /// <summary>The LandSandBoat ability ids of the pet commands that open lists of their own, and the command type each lists.</summary>
        public static readonly IReadOnlyDictionary<int, int> PetListCommands = new Dictionary<int, int>
        {
            [91] = 6,   // Blood Pact: Rage (retail image-41)
            [172] = 10, // Blood Pact: Ward (image-42)
            [251] = 18, // Ready (BST; PROVISIONAL, by analogy)
        };

        /// <summary>
        /// Pet Command (retail image-40: Assault, Retreat, Release, Blood Pact: Rage, Blood Pact: Ward, Avatar's Favor): the
        /// job abilities of type 2 S2C 0x0AC lists, in id order. Blood Pact: Rage / Ward and Ready open their lists.
        /// </summary>
        public static List<StockUiActionRow> PetCommandRows(ActionCatalog catalog, StockUiActionState state, StockUiTargetContext? target)
        {
            var rows = new List<StockUiActionRow>();
            for (int id = 0; id < CommandRecord.JobAbilityBase; id++)
            {
                if (!state.HasJobAbility((ushort)id) || catalog.GetJobAbility(id) is not { Type: 2 } record || catalog.JobAbilityName(id) is not { } name) continue;
                var row = AbilityRow(catalog, state, record, (ushort)id, name);
                if (PetListCommands.TryGetValue(id, out int listType)) row = row with { Type = StockUiActionType.PetList, OpensPetList = listType };
                rows.Add(row);
            }
            return rows;
        }

        /// <summary>
        /// A pet command's list: the pet abilities S2C 0x0AC's pet bits name (command 1024 + bit, used as job ability 512 +
        /// bit, LandSandBoat <c>charutils::BuildingCharPetAbilityTable</c>) of one command type: 6 Blood Pact: Rage, 10
        /// Blood Pact: Ward, 18 Ready.
        /// </summary>
        public static List<StockUiActionRow> PetAbilityRows(ActionCatalog catalog, StockUiActionState state, int commandType)
        {
            var rows = new List<StockUiActionRow>();
            for (int bit = 0; bit < TraitBase - PetCommandBase; bit++)
            {
                int command = PetCommandBase + bit;
                if (!state.HasPetAbility((ushort)bit) || catalog.Tables.GetCommand(command) is not { } record || record.Type != commandType
                    || catalog.JobAbilityName(command - CommandRecord.JobAbilityBase) is not { } name) continue;
                var row = AbilityRow(catalog, state, record, (ushort)(command - CommandRecord.JobAbilityBase), name);
                if (commandType == ReadyCommandType)
                {
                    // Ready moves take charges (+0x06), not the whole recast: greyed only without enough charges.
                    int left = ReadyCharges(row.RecastLeft(state.Now));
                    row = row with { GreyedInRecast = false };
                    if (row.GreyedReason == null && left < Math.Max(1, record.Charges)) row = row with { GreyedReason = $"{name} needs {Math.Max(1, record.Charges)} charge(s)." };
                }
                if (commandType is 6 or 10)
                {
                    // A blood pact's MP cost is the record's +0x06 (retail image-46: "MP Cost"); greyed without the MP (PROVISIONAL).
                    row = row with { MpCost = record.Charges };
                    if (row.GreyedReason == null && record.Charges > state.CurrentMp) row = row with { GreyedReason = $"You do not have enough MP to use {name}." };
                }
                rows.Add(row);
            }
            return rows;
        }

        /// <summary>
        /// Ready's charges left (retail image-47: "Charge 3"): LandSandBoat gives Ready (recast id 102) 3 charges of 30 s each
        /// (<c>sql/abilities_charges.sql</c>), so the charges are 3 less one per started 30 s still on the recast. PROVISIONAL:
        /// merits shorten the charge time, which the client cannot see here.
        /// </summary>
        public static int ReadyCharges(TimeSpan recastLeft) =>
            Math.Clamp(ReadyMaxCharges - (int)Math.Ceiling(recastLeft.TotalSeconds / ReadyChargeSeconds), 0, ReadyMaxCharges);

        public const int ReadyMaxCharges = 3, ReadyChargeSeconds = 30, ReadyCommandType = 18;

        private static StockUiActionRow AbilityRow(ActionCatalog catalog, StockUiActionState state, CommandRecord record, ushort id, string name)
        {
            var flags = (ActionTargetFlags)(record.TargetFlags & 0x3F);
            string? reason = record.TpCost > 0 && state.CurrentTp < record.TpCost ? $"You do not have enough TP to use {name}." : null;
            return new StockUiActionRow(StockUiActionType.JobAbility, id, name, flags, CommandIcon(record), -1,
                EndsAt(state, state.AbilityRecast(record.RecastId)), reason, SmallIcon: record.Icon, GreyedInRecast: true,
                Help: catalog.CommandHelp(record.Id));
        }

        /// <summary>
        /// The weapon skills S2C 0x0AC lists, in id order, greyed while not engaged or under 1000 TP (the capture shows every
        /// weapon skill greyed at 3000 TP out of combat).
        /// </summary>
        public static List<StockUiActionRow> WeaponSkillRows(ActionCatalog catalog, StockUiActionState state, StockUiTargetContext? target)
        {
            var rows = new List<StockUiActionRow>();
            for (int id = 0; id < 256; id++)
            {
                if (!state.HasWeaponSkill((ushort)id) || catalog.GetWeaponSkill(id) is not { } record || catalog.WeaponSkillName(id) is not { } name) continue;
                var flags = (ActionTargetFlags)(record.TargetFlags & 0x3F);
                string? reason = !state.Engaged ? $"You must be engaged to use {name}."
                    : state.CurrentTp < WeaponSkillTp ? $"You do not have enough TP to use {name}." : null;
                rows.Add(new StockUiActionRow(StockUiActionType.WeaponSkill, (ushort)id, name, flags, CommandIcon(record), -1, 0, reason,
                    SmallIcon: record.Icon, Help: catalog.CommandHelp(record.Id)));
            }
            return rows;
        }

        /// <summary>The job traits S2C 0x0AC lists (command 1536 + trait id), in id order; never greyed (image-19), help in the bar (image-27).</summary>
        public static List<StockUiActionRow> TraitRows(ActionCatalog catalog, StockUiActionState state)
        {
            var rows = new List<StockUiActionRow>();
            for (int id = 0; id < 256; id++)
            {
                int command = TraitBase + id;
                if (!state.HasTrait((ushort)id) || catalog.Tables.GetCommand(command) is not { } record
                    || catalog.JobAbilityName(command - CommandRecord.JobAbilityBase) is not { } name) continue;
                rows.Add(new StockUiActionRow(StockUiActionType.Trait, (ushort)id, name, ActionTargetFlags.None, CommandIcon(record), -1, 0, null,
                    SmallIcon: record.Icon, Help: catalog.CommandHelp(command)));
            }
            return rows;
        }

        /// <summary>
        /// A command's list icon: its picture (+0x04, Soul Voice 75) when it has one, else its small icon (+0x03, the "Ab"
        /// tiles traits show). Pictures numbered 677 and up (Inner Strength 1065, Clarion Call 1073 and the other newer
        /// abilities, 122 spells) lie past <c>magiconw</c>'s 677 images; their source is not found yet, so the lists fall
        /// back to the small icon for them.
        /// </summary>
        public static int CommandIcon(CommandRecord record) => record.Icon2 >= 0 ? record.Icon2 : record.Icon;

        private static long EndsAt(StockUiActionState state, TimeSpan left) =>
            left > TimeSpan.Zero ? state.Now + (long)(left.TotalSeconds * System.Diagnostics.Stopwatch.Frequency) : 0;

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

        /// <summary>A recast as the "Next" line shows it: "m:ss" rounded up ("0:00" when ready), "h:mm:ss" past an hour.</summary>
        public static string FormatRecast(TimeSpan left)
        {
            int seconds = left <= TimeSpan.Zero ? 0 : (int)Math.Ceiling(left.TotalSeconds);
            return seconds >= 3600
                ? string.Create(CultureInfo.InvariantCulture, $"{seconds / 3600}:{seconds / 60 % 60:00}:{seconds % 60:00}")
                : string.Create(CultureInfo.InvariantCulture, $"{seconds / 60}:{seconds % 60:00}");
        }
    }
}
