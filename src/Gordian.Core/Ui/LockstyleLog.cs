// src/Gordian.Core/Ui/LockstyleLog.cs
using System;
using System.Collections.Generic;
using Gordian.Core.Resources.Models;

namespace Gordian.Core.Ui
{
    /// <summary>
    /// The message log lines of a style lock error (S2C 0x11C): one line per item the lock could not use, in the order
    /// the server lists them. XiPackets (https://github.com/atom0s/XiPackets/tree/main/world/server/0x011C) says the
    /// client prints an error line for each item that failed; the retail text is not in the system message table
    /// (ROM/27/76 holds only "Style lock mode ..." 267-270) nor anywhere else in the install's DATs (searched
    /// 2026-10-10 for "style lock" / "lockstyle"; only zone dialog lines about trading turned up), so it is probably in
    /// the client executable, and the packet is not seen on retail. PROVISIONAL: the wording below is GordianXI's.
    /// </summary>
    public static class LockstyleLog
    {
        /// <summary>The error line for one item.</summary>
        public static string FormatError(ItemRecord item)
        {
            ArgumentNullException.ThrowIfNull(item);
            string name = item.LogName.Length > 0 ? item.LogName : item.Name.Length > 0 ? item.Name : $"item {item.ItemId}";
            return $"Unable to use {name} for style lock.";
        }

        /// <summary>The error lines for the items of one S2C 0x11C (item id 0 entries skipped).</summary>
        public static IEnumerable<string> FormatErrors(IReadOnlyList<ushort> itemIds, Func<ushort, ItemRecord?>? lookup)
        {
            foreach (ushort id in itemIds)
            {
                if (id == 0) continue;
                yield return FormatError(StockUiShop.Lookup(lookup, id));
            }
        }
    }
}
