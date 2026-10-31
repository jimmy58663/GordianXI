// src/Gordian.Core/Resources/Graphics/ZoneCullingTables.cs
using System;
using System.Buffers.Binary;
using System.Collections.Generic;

namespace Gordian.Core.Resources.Graphics
{
    /// <summary>
    /// The ZoneDef culling tables (the zone's potentially visible sets): at the payload offset stored at header +0x14, a
    /// u32 table count, then that many tables, each a u32 count followed by that many u32 placement record indices. The
    /// collision floor under the camera picks one table (its collision transform's culling-group field at +0xA8 points at
    /// the table's count word), and only the placements that table lists are drawn; a floor with no table draws everything.
    /// A placement record's own +0x48 holds the same pointer for the table of the floor it is part of.
    /// Table layout referenced from xi-tools (docs/zone/format.md "Visibility", src/xi/zone/xi_zonedef.py
    /// add_to_culling_tables, https://github.com/vekien/xi-tools). Checked on the retail data (2026-10-31, #70): record
    /// +0x48 and collision transform +0xA8 agree for every record of Lower Jeuno (82 linked), Metalworks, Southern
    /// San d'Oria (666) and West Ronfaure (1,793), and every linked record is listed in its own table.
    /// </summary>
    public sealed class ZoneCullingTables
    {
        private readonly ulong[][] _listed;
        private readonly Dictionary<int, int> _tableAtOffset;

        private ZoneCullingTables(ulong[][] listed, Dictionary<int, int> tableAtOffset, int[] sizes)
        {
            _listed = listed;
            _tableAtOffset = tableAtOffset;
            Sizes = sizes;
        }

        /// <summary>The number of tables.</summary>
        public int Count => _listed.Length;

        /// <summary>The number of placements each table lists.</summary>
        public IReadOnlyList<int> Sizes { get; }

        /// <summary>
        /// Reads the tables of a decrypted ZoneDef payload (the section data after its 16-byte chunk header), or returns
        /// null when the zone has none or they run past the payload.
        /// </summary>
        public static ZoneCullingTables? Parse(ReadOnlySpan<byte> payload)
        {
            if (payload.Length < 0x20) return null;
            uint offset = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(0x14, 4));
            if (offset == 0 || offset > (uint)(payload.Length - 4)) return null;
            int p = (int)offset;
            uint tableCount = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(p, 4));
            // A table needs at least its count word: anything larger than the payload can hold is not a table block.
            if (tableCount == 0 || tableCount > (uint)((payload.Length - p - 4) / 4)) return null;
            p += 4;

            var listed = new ulong[tableCount][];
            var sizes = new int[tableCount];
            var tableAtOffset = new Dictionary<int, int>((int)tableCount);
            for (int t = 0; t < tableCount; t++)
            {
                if (p + 4 > payload.Length) return null;
                tableAtOffset[p] = t;
                uint count = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(p, 4));
                p += 4;
                if (count > (uint)((payload.Length - p) / 4)) return null;
                int max = -1;
                for (int k = 0; k < count; k++) max = Math.Max(max, (int)Math.Min(BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(p + (k * 4), 4)), 0xFFFFFF));
                var bits = new ulong[(max + 64) / 64];
                for (int k = 0; k < count; k++)
                {
                    int record = (int)Math.Min(BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(p + (k * 4), 4)), 0xFFFFFF);
                    bits[record >> 6] |= 1UL << (record & 63);
                }
                listed[t] = bits;
                sizes[t] = (int)count;
                p += (int)count * 4;
            }
            return new ZoneCullingTables(listed, tableAtOffset, sizes);
        }

        /// <summary>
        /// The index of the table whose count word sits at a payload offset (a record's +0x48 or a collision transform's
        /// +0xA8), or -1 for 0 and for an offset that is not a table.
        /// </summary>
        public int TableAt(uint payloadOffset) =>
            payloadOffset != 0 && payloadOffset <= int.MaxValue && _tableAtOffset.TryGetValue((int)payloadOffset, out int table) ? table : -1;

        /// <summary>Whether table <paramref name="table"/> lists placement record <paramref name="record"/>.</summary>
        public bool IsListed(int table, int record)
        {
            if ((uint)table >= (uint)_listed.Length || record < 0) return false;
            var bits = _listed[table];
            int word = record >> 6;
            return word < bits.Length && (bits[word] & (1UL << (record & 63))) != 0;
        }
    }
}
