// src/Gordian.Core/Resources/Tables/ItemTables.cs
using System;
using System.Collections.Generic;
using System.IO;

namespace Gordian.Core.Resources.Tables
{
    /// <summary>
    /// The retail item table DATs and the item id range each one holds, shared by <see cref="ItemNameResolver"/> and
    /// <see cref="ResourceManager.TryGetItem"/> so the two cannot drift apart (#203: the resource manager lacked
    /// General 2 and everything from 28672). Record <c>n</c> of a table holds item <c>StartId + n</c>.
    /// Table paths referenced from xi-model-viewer (https://github.com/vekien/xi-model-viewer), LandSandBoat
    /// (https://github.com/LandSandBoat/server) and xi-tools (https://github.com/vekien/xi-tools,
    /// docs/reference/named-dats.md and the item table list of its item parser).
    /// </summary>
    public static class ItemTables
    {
        /// <summary>One item table: an inclusive id range and the table's path under the game directory.</summary>
        public readonly record struct Range(uint StartId, uint EndId, string RelativePath, string Name);

        /// <summary>The item id the client shows as gil.</summary>
        public const uint GilId = 65535;

        /// <summary>
        /// The ranged item tables (EN), in id order. Ids 31744-61431, 61952-62975, 62996-63007 and 63264-65534 have no
        /// item table (57344-61431 and 61952-62975 hold Records of Eminence objectives and categories). Each row's first
        /// record holds its start id in the retail install (checked 2026-10-03, <c>ItemTablesTests</c>). General 7
        /// (<c>ROM/387/14</c>) came with the 10 September 2026 retail update and is absent from older installs.
        /// <c>ROM/286/74</c> is not an item table for 28672 on: it holds ids 25600-28671 in the old 0xC00 stride.
        /// </summary>
        public static IReadOnlyList<Range> Ranges { get; } = new[]
        {
            new Range(0, 4095, Path.Combine("ROM", "118", "106.DAT"), "General 1"),
            new Range(4096, 8191, Path.Combine("ROM", "118", "107.DAT"), "Consumables"),
            new Range(8192, 8703, Path.Combine("ROM", "118", "110.DAT"), "Automaton"),
            new Range(8704, 10239, Path.Combine("ROM", "301", "115.DAT"), "General 2"),
            new Range(10240, 16383, Path.Combine("ROM", "118", "109.DAT"), "Armor 1"),
            new Range(16384, 23039, Path.Combine("ROM", "118", "108.DAT"), "Weapons 1"),
            new Range(23040, 28671, Path.Combine("ROM", "286", "73.DAT"), "Armor 2"),
            new Range(28672, 29695, Path.Combine("ROM", "217", "21.DAT"), "Moblin Maze Mongers"),
            new Range(29696, 30719, Path.Combine("ROM", "288", "80.DAT"), "Monstrosity 1"),
            new Range(30720, 31743, Path.Combine("ROM", "387", "14.DAT"), "General 7"),
            new Range(61432, 61439, Path.Combine("ROM", "314", "89.DAT"), "General 3"),
            new Range(61440, 61951, Path.Combine("ROM", "288", "67.DAT"), "Monstrosity 2"),
            new Range(62976, 62995, Path.Combine("ROM", "320", "26.DAT"), "General 4"),
            new Range(63008, 63023, Path.Combine("ROM", "332", "49.DAT"), "General 5"),
            new Range(63024, 63263, Path.Combine("ROM", "332", "48.DAT"), "General 6"),
        };

        /// <summary>The currency table that holds gil (item 65535).</summary>
        public static string GilTablePath { get; } = Path.Combine("ROM", "174", "48.DAT");

        /// <summary>The ranged table holding an item id, if any (gil is not a ranged table: see <see cref="GilTablePath"/>).</summary>
        public static bool TryGetRange(uint itemId, out Range range)
        {
            foreach (var r in Ranges)
            {
                if (itemId >= r.StartId && itemId <= r.EndId)
                {
                    range = r;
                    return true;
                }
            }
            range = default;
            return false;
        }

        /// <summary>The table path holding an item id (gil included), or empty when no table holds it.</summary>
        public static string GetTablePath(uint itemId)
        {
            if (itemId == GilId) return GilTablePath;
            return TryGetRange(itemId, out var range) ? range.RelativePath : string.Empty;
        }
    }
}
