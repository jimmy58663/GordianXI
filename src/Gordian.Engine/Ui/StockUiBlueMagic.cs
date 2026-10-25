// src/Gordian.Engine/Ui/StockUiBlueMagic.cs
using System;
using System.Collections.Generic;
using Gordian.Core.Network.Packets;
using Gordian.Engine.Actions;

namespace Gordian.Engine.Ui
{
    /// <summary>What the Set Spells window needs from the character: the Blue Mage level and slots, and the learned spells.</summary>
    /// <param name="Level">The Blue Mage level (main, or support when Blue Mage is the support job); 0 when neither.</param>
    /// <param name="SubJob">Whether Blue Mage is the support job (C2S 0x102's <c>SupportJobFlg</c>).</param>
    /// <param name="Slots">The twenty set slots from S2C 0x044, each the spell id less 512 (0 = empty).</param>
    /// <param name="BonusPoints">Blue Magic Points from merits and job points (S2C 0x063 merits, <c>BluBonus</c>).</param>
    /// <param name="HasSpell">Whether a spell is learned (S2C 0x0AA).</param>
    public sealed record StockUiBlueMagicState(int Level, bool SubJob, IReadOnlyList<byte> Slots, int BonusPoints, Func<ushort, bool> HasSpell);

    /// <summary>One of the twenty set slots: its number, the spell in it (0 = empty), the level it opens at.</summary>
    public readonly record struct StockUiBlueSlotRow(int Slot, ushort SpellId, string Name, int Icon, int UnlockLevel, bool Locked);

    /// <summary>
    /// One learned blue spell: its level, MP cost and Blue Magic Points, whether it is set, and why it cannot be set (null when it
    /// can). <see cref="RemoveAll"/> marks the list's first row, "(Remove All)".
    /// </summary>
    public readonly record struct StockUiBlueSpellRow(ushort SpellId, string Name, int Icon, int Level, int MpCost, int Points, bool IsSet,
        string? GreyedReason, string? Help, bool RemoveAll = false);

    /// <summary>
    /// Blue Magic's Set Spells window (#363), from the English menu DAT: <c>blusortw</c> (the Blue Magic list's Options, + / Y:
    /// Auto, Manual and "Set Spells", button 3, help 643 "Set blue magic spells."), <c>bluequip</c> (130,48, 182 x 190, title 141
    /// "Blue Magic", ten 132 x 16 rows at an 18 px pitch: the set slots), <c>bluinven</c> (314,48, the same frame, help 637 "View
    /// list of blue magic. Exceeding certain values (8/16/24) allows the bestowal of Job Trait effects.": the learned spells),
    /// <c>bluepoin</c> (16,240, 112 x 56, "Total Points/Max" baked in) and <c>bluehelp</c> (130,240, 366 x 56: the spell's
    /// details); <c>bluesibo</c> ("Refine by Type", the monster families) is not used, as the spell table carries no family.
    /// The maintainer's retail capture (image-38 of the 2026-10-11 notes) shows the same panel with a status column on the left;
    /// the menus' own texts are ROM/165/72 #227 "(Remove All)" and #228 "Remove all set blue magic spells?".
    /// <para>
    /// The rules are the server's, from LandSandBoat (<c>utils/blueutils.cpp</c>): slots <c>clamp(((level - 1) / 10) * 2 + 6, 6,
    /// 20)</c>, points <c>clamp(((level - 1) / 10) * 5 + 10, 0, 55)</c> plus the merit and job point bonus the server reports, a
    /// spell settable at its Blue Mage level, once. A spell's Blue Magic Points are not in the client's spell table (<c>mgc_</c>
    /// bytes 0x44-0x63 checked against them, 2026-10-24); they are the server's <c>blue_spell_list.set_points</c> (LandSandBoat
    /// <c>sql/blue_spell_list.sql</c>), which the server enforces.
    /// </para>
    /// </summary>
    public static class StockUiBlueMagic
    {
        public const string OptionsMenu = "blusortw", SlotsMenu = "bluequip", LearnedMenu = "bluinven", PointsMenu = "bluepoin", DetailMenu = "bluehelp";
        public const int OptionsSetSpellsButton = 3;
        public const int SlotCount = S2C_0x044_ExtendedJob.BlueSpellSlots;
        public const int HelpSetSpells = 643, HelpLearned = 637;
        public const int MessageRemoveAllLabel = 227, MessageRemoveAllPrompt = 228;
        public const string Title = "Blue Magic";

        /// <summary>Blue Magic Points by spell id from 512 (LandSandBoat <c>blue_spell_list.set_points</c>); 0 past the table.</summary>
        private static readonly byte[] Points =
        {
            0, 3, 0, 5, 0, 1, 0, 3, 0, 4, 2, 0, 2, 0, 0, 3, 0, 2, 4, 3, // 512-531
            4, 3, 4, 1, 1, 2, 4, 3, 4, 2, 2, 2, 2, 4, 0, 1, 3, 1, 0, 1, // 532-551
            0, 0, 5, 3, 0, 4, 0, 0, 3, 3, 0, 3, 4, 4, 0, 2, 0, 4, 2, 0, // 552-571
            1, 3, 2, 4, 3, 2, 3, 4, 0, 4, 2, 0, 2, 4, 0, 2, 2, 5, 0, 4, // 572-591
            2, 3, 3, 5, 2, 2, 4, 2, 0, 0, 0, 3, 5, 3, 2, 0, 3, 0, 4, 5, // 592-611
            4, 5, 3, 5, 5, 3, 2, 0, 3, 2, 2, 3, 0, 0, 3, 0, 3, 3, 0, 3, // 612-631
            3, 5, 5, 0, 4, 5, 3, 0, 4, 5, 3, 3, 4, 4, 4, 2, 1, 0, 2, 4, // 632-651
            3, 2, 4, 3, 3, 3, 4, 4, 3, 5, 3, 4, 2, 1, 3, 2, 3, 2, 4, 4, // 652-671
            5, 4, 1, 3, 0, 3, 3, 3, 4, 5, 2, 4, 4, 3, 4, 2, 2, 3, 5, 0, // 672-691
            4, 5, 3, 4, 5, 4, 2, 2, // 692-699
        };

        /// <summary>A blue spell's Blue Magic Points (0 when unknown).</summary>
        public static int PointsOf(int spellId)
        {
            int index = spellId - S2C_0x044_ExtendedJob.BlueSpellIdBase;
            return index >= 0 && index < Points.Length ? Points[index] : 0;
        }

        /// <summary>The set slots open at a Blue Mage level (6 to 20).</summary>
        public static int SlotsAt(int level) => level <= 0 ? 0 : Math.Clamp(((level - 1) / 10) * 2 + 6, 6, SlotCount);

        /// <summary>The level slot <paramref name="slot"/> (1-20) opens at: 1 for the first six, then 11, 21 ... 71 two by two.</summary>
        public static int UnlockLevel(int slot) => slot <= 6 ? 1 : (slot - 5) / 2 * 10 + 1;

        /// <summary>The Blue Magic Points at a level, before the merit and job point bonus.</summary>
        public static int BasePointsAt(int level) => level <= 0 ? 0 : Math.Clamp(((level - 1) / 10) * 5 + 10, 0, 55);

        /// <summary>The points in use and the most the character may use.</summary>
        public static (int Used, int Max) PointsIn(StockUiBlueMagicState state)
        {
            int used = 0;
            foreach (byte slot in state.Slots)
            {
                if (slot != 0) used += PointsOf(slot + S2C_0x044_ExtendedJob.BlueSpellIdBase);
            }
            return (used, state.Level <= 0 ? 0 : BasePointsAt(state.Level) + state.BonusPoints);
        }

        /// <summary>The twenty slots: the spell in each, and those the level does not open yet locked.</summary>
        public static List<StockUiBlueSlotRow> SlotRows(StockUiBlueMagicState state, ActionCatalog? catalog)
        {
            var rows = new List<StockUiBlueSlotRow>(SlotCount);
            int open = SlotsAt(state.Level);
            for (int i = 0; i < SlotCount; i++)
            {
                byte value = i < state.Slots.Count ? state.Slots[i] : (byte)0;
                ushort id = value == 0 ? (ushort)0 : (ushort)(value + S2C_0x044_ExtendedJob.BlueSpellIdBase);
                var spell = id != 0 ? catalog?.GetSpell(id) : null;
                string name = id == 0 ? string.Empty : catalog?.SpellName(id) ?? $"Spell {id}";
                int icon = spell == null ? -1 : spell.Icon2 != 0xFFFF ? spell.Icon2 : spell.Icon;
                rows.Add(new StockUiBlueSlotRow(i + 1, id, name, icon, UnlockLevel(i + 1), i >= open));
            }
            return rows;
        }

        /// <summary>
        /// The learned list: "(Remove All)" first, then every learned blue spell in the spell table's menu order with its level,
        /// MP and points; one not yet set is greyed when its level is above the Blue Mage level or its points do not fit (the
        /// points of the spell in <paramref name="targetSlot"/>, which it would replace, are freed first).
        /// </summary>
        public static List<StockUiBlueSpellRow> LearnedRows(StockUiBlueMagicState state, ActionCatalog catalog, int targetSlot, Func<int, string?>? menuText = null)
        {
            var (used, max) = PointsIn(state);
            int freed = targetSlot >= 0 && targetSlot < state.Slots.Count && state.Slots[targetSlot] != 0
                ? PointsOf(state.Slots[targetSlot] + S2C_0x044_ExtendedJob.BlueSpellIdBase) : 0;
            var set = new HashSet<int>();
            foreach (byte slot in state.Slots)
            {
                if (slot != 0) set.Add(slot + S2C_0x044_ExtendedJob.BlueSpellIdBase);
            }
            var found = new List<(int Order, StockUiBlueSpellRow Row)>();
            var spells = catalog.Tables.Spells;
            for (int id = S2C_0x044_ExtendedJob.BlueSpellIdBase; id < spells.Count; id++)
            {
                var spell = spells[id];
                if (spell == null || spell.Kind != StockUiActionMenu.BlueMagicKind || !state.HasSpell((ushort)id) || catalog.SpellName(id) is not { } name) continue;
                int level = spell.LevelFor((int)JobId.BlueMage) ?? 0;
                int points = PointsOf(id);
                bool isSet = set.Contains(id);
                string? reason = isSet ? null
                    : level > state.Level ? $"You cannot set {name} at your current level."
                    : used - freed + points > max ? $"You do not have enough Blue Magic Points to set {name}."
                    : null;
                int icon = spell.Icon2 != 0xFFFF ? spell.Icon2 : spell.Icon;
                found.Add((spell.MenuIndex, new StockUiBlueSpellRow((ushort)id, name, icon, level, spell.MpCost, points, isSet, reason, catalog.SpellHelp(id))));
            }
            found.Sort((a, b) => a.Order != b.Order ? a.Order.CompareTo(b.Order) : a.Row.SpellId.CompareTo(b.Row.SpellId));
            var rows = new List<StockUiBlueSpellRow>(found.Count + 1)
            {
                new(0, menuText?.Invoke(MessageRemoveAllLabel) is { Length: > 0 } label ? label : "(Remove All)", -1, 0, 0, 0, false, null, null, RemoveAll: true),
            };
            rows.AddRange(found.ConvertAll(f => f.Row));
            return rows;
        }

        /// <summary>The slot a spell is set in (0-19), or -1.</summary>
        public static int SlotOf(StockUiBlueMagicState state, ushort spellId)
        {
            for (int i = 0; i < state.Slots.Count; i++)
            {
                if (state.Slots[i] != 0 && state.Slots[i] + S2C_0x044_ExtendedJob.BlueSpellIdBase == spellId) return i;
            }
            return -1;
        }

        /// <summary>The first open, empty slot at or after <paramref name="from"/> (wrapping), or -1.</summary>
        public static int NextEmptySlot(StockUiBlueMagicState state, int from = 0)
        {
            int open = SlotsAt(state.Level);
            for (int n = 0; n < open; n++)
            {
                int i = (from + n) % open;
                if (i >= state.Slots.Count || state.Slots[i] == 0) return i;
            }
            return -1;
        }
    }
}
