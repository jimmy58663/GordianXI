// src/Gordian.Core/Ui/StockUiTreasurePool.cs
using System;
using System.Collections.Generic;
using System.Globalization;
using Gordian.Core.Network.Packets;
using Gordian.Core.Resources.Models;
using Gordian.Core.World;

namespace Gordian.Core.Ui
{
    /// <summary>One row of the Treasure Pool window: a pool slot's item and standings.</summary>
    public readonly record struct StockUiTreasureRow(byte Slot, ushort ItemId, string Name, uint Count, TreasureEntryKind Entry,
        ushort LocalLot, string LeaderName, ushort LeaderLot)
    {
        /// <summary>Whether the local player can still cast lots (neither lotted nor passed).</summary>
        public bool CanLot => Entry == TreasureEntryKind.None;

        /// <summary>Whether the local player can still pass (anything but a pass; retail lets you pass after lotting).</summary>
        public bool CanPass => Entry != TreasureEntryKind.Pass;
    }

    /// <summary>
    /// The stock Treasure Pool window (#143). The English menu DAT carries it as <c>loot</c> (16,48 top-left, 182 x 190,
    /// title 73 "Treasure Pool", help 300 "Cast lots on the treasure you want."): ten invisible 132 x 16 rows at an 18 px
    /// pitch from y 5, each with a 16 x 16 <c>itemslot</c> box at x 3 in the frame art, cursor group <c>anc_s</c>, the
    /// rows' navigation links stopping at the ends; and <c>lootope</c> (authored at 0,0, 112 x 40: "Cast Lot" windowps
    /// #239 / greyed #434, help 301, and "Pass" #240 / #435, help 302), which the client places beside the row. The
    /// command menus' Treasure entry (playermo 7, attackmo 6, battlemo / normalmo 5, help 27 "Distribute currently pooled
    /// treasure among party members.") opens it while the pool holds an item (confirmed in game); no main menu entry in
    /// the DAT does. <c>lnowin</c> (top right, "Option" / "+ : Spoils Options" and "Done", help 922 "Relinquish the right
    /// to cast lots on all spoils for which you have not yet done so.") shows beside the list; + on the keyboard or Y on
    /// the gamepad puts the cursor on Done, which passes on every item you have not lotted on (the maintainer's retail
    /// check, 2026-10-07).
    /// <para>
    /// From the maintainer's retail screenshots (2026-10-07): a row's name is white until you lot, orange-red after a lot,
    /// grey after a pass; the item's description shows in the item info window above the log; and the selected item's
    /// lots stand beside each member in the party and alliance windows (<see cref="RollText"/>) under a "roll" label.
    /// PROVISIONAL: where <c>lootope</c> sits and the countdown on the rows.
    /// </para>
    /// </summary>
    public static class StockUiTreasurePool
    {
        public const string ListMenu = "loot";
        public const string ActionMenu = "lootope";
        public const string InfoMenu = "iteminfo";

        public const int LotButton = 1, PassButton = 2;

        /// <summary>The "Spoils Options" window with its Done button (PC: "+" on the keyboard).</summary>
        public const string DoneMenu = "lnowin";
        public const int DoneButton = 1;

        /// <summary>Layout gap between the list's right edge and the Cast Lot / Pass window placed beside the row.</summary>
        public const int ActionWindowGap = 2;

        /// <summary>The rows the window lists: the occupied pool slots in slot order.</summary>
        public static List<StockUiTreasureRow> Rows(TreasurePoolState? pool, Func<ushort, ItemRecord?>? lookup)
        {
            var rows = new List<StockUiTreasureRow>(TreasurePoolState.SlotCount);
            if (pool == null) return rows;
            foreach (var slot in pool.Snapshot())
            {
                var record = StockUiShop.Lookup(lookup, slot.ItemId);
                rows.Add(new StockUiTreasureRow(slot.Slot, slot.ItemId, record.Name, slot.Count, slot.Entry, slot.LocalLot,
                    slot.LeaderName, slot.LeaderLot));
            }
            return rows;
        }

        /// <summary>The countdown as retail-style minutes and seconds ("4:59"), rounded up so "0:00" only shows at the end.</summary>
        public static string FormatTimeLeft(TimeSpan left)
        {
            int seconds = (int)Math.Ceiling(Math.Max(0, left.TotalSeconds));
            return string.Create(CultureInfo.InvariantCulture, $"{seconds / 60}:{seconds % 60:00}");
        }

        /// <summary>
        /// A member's entry beside its party row while an item is selected (retail screenshots, 2026-10-07): the lot value
        /// ("408"), "---" after a pass, "?" while the member has done neither.
        /// </summary>
        public static string RollText(TreasureMemberEntry? entry) => entry switch
        {
            null => "?",
            { Passed: true } => "---",
            { } lot => lot.Lot.ToString(CultureInfo.InvariantCulture),
        };
    }
}
