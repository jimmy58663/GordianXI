// src/Gordian.Engine/Ui/RetailEquipSets.cs
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Gordian.Core.Diagnostics;
using Gordian.Core.Network.Packets;
using Gordian.Core.World;

namespace Gordian.Engine.Ui
{
    /// <summary>
    /// The retail client's equipment set files, <c>USER/&lt;id&gt;/es&lt;n&gt;.dat</c> (#92), read for <c>/importretail</c>.
    /// Read-only: the game folder is never written.
    /// <para>
    /// <b>Beyond xi-tools / LandSandBoat / XiPackets:</b> none of them documents these files; this is our reading of the
    /// maintainer's retail install (2026-10-12, seven <c>es0.dat</c> files of 1624 bytes):
    /// </para>
    /// <list type="bullet">
    /// <item>A 24-byte header: u32 1 (a version?), u32 0, then the MD5 of the rest of the file (checked: it matches in
    /// every file).</item>
    /// <item>Twenty 80-byte sets: the name (16 bytes, NUL-padded; unnamed sets hold "Set001" ...), then sixteen u32 slots
    /// in <see cref="EquipSlotId"/> order (main, sub, range, ammo, head, body, hands, legs, feet, neck, waist, ear 1, ear 2,
    /// ring 1, ring 2, back; checked against every stored item's equip slots in the item DATs).</item>
    /// <item>Each slot is XiPackets' <c>equipsetrequestitem_t</c> (C2S 0x052 / S2C 0x116): byte 0 bit 0 "has item", bit 1
    /// "Remove piece", bits 2-7 the container (wardrobes 8, 10, 11, 12 seen); byte 1 the index in the container; u16 the
    /// item id.</item>
    /// <item><c>es&lt;n&gt;.dat</c> holds sets <c>20 n + 1</c> to <c>20 n + 20</c> (PROVISIONAL: only <c>es0.dat</c>
    /// exists in the install).</item>
    /// </list>
    /// </summary>
    public static class RetailEquipSets
    {
        public const int HeaderSize = 24, SetSize = 80, NameSize = 16, SetsPerFile = 20;
        public const int FileSize = HeaderSize + SetsPerFile * SetSize;

        /// <summary>The file holding page <paramref name="file"/> (0-based), in its exact case.</summary>
        public static string FileName(int file) => "es" + file.ToString(CultureInfo.InvariantCulture) + ".dat";

        /// <summary>Whether the file's stored MD5 matches its sets.</summary>
        public static bool HasValidChecksum(ReadOnlySpan<byte> data)
        {
            if (data.Length < HeaderSize) return false;
            Span<byte> hash = stackalloc byte[16];
            MD5.HashData(data[HeaderSize..], hash);
            return hash.SequenceEqual(data.Slice(8, 16));
        }

        /// <summary>
        /// Reads one file's sets, numbered from <paramref name="firstNumber"/>; null when the file is too short. A set that
        /// is unnamed and empty is still returned (with its default name).
        /// </summary>
        public static List<(int Number, EquipmentSet Set)>? Parse(ReadOnlySpan<byte> data, int firstNumber)
        {
            if (data.Length < FileSize) return null;
            var sets = new List<(int, EquipmentSet)>(SetsPerFile);
            for (int i = 0; i < SetsPerFile; i++)
            {
                var record = data.Slice(HeaderSize + i * SetSize, SetSize);
                int end = record[..NameSize].IndexOf((byte)0);
                string name = Encoding.ASCII.GetString(record[..(end < 0 ? NameSize : end)]);
                var pieces = new EquipmentSetPiece[EquipmentSet.SlotCount];
                for (int slot = 0; slot < EquipmentSet.SlotCount; slot++)
                {
                    var entry = record.Slice(NameSize + slot * 4, 4);
                    byte flags = entry[0];
                    pieces[slot] = EquipmentSetPiece.FromRequestItem((flags & 0x01) != 0, (flags & 0x02) != 0, (ContainerId)(flags >> 2), entry[1],
                        BinaryPrimitives.ReadUInt16LittleEndian(entry.Slice(2, 2)));
                }
                int number = firstNumber + i;
                sets.Add((number, new EquipmentSet(name.Length > 0 ? name : EquipmentSet.DefaultName(number), pieces)));
            }
            return sets;
        }

        /// <summary>
        /// Reads every <c>es&lt;n&gt;.dat</c> in a USER character folder; the sets are numbered by file. Files that cannot be
        /// read or are too short are skipped (logged).
        /// </summary>
        public static List<(int Number, EquipmentSet Set)> ReadFolder(string folderPath)
        {
            var all = new List<(int, EquipmentSet)>();
            for (int file = 0; file < EquipmentSetBook.PageCount; file++)
            {
                string path = Path.Combine(folderPath, FileName(file));
                if (!File.Exists(path)) continue;
                try
                {
                    byte[] data = File.ReadAllBytes(path);
                    var sets = Parse(data, file * SetsPerFile + 1);
                    if (sets == null)
                    {
                        GordianLog.Warning("EQUIPSET", $"'{path}' is {data.Length} bytes, not {FileSize}; skipped.");
                        continue;
                    }
                    if (!HasValidChecksum(data)) GordianLog.Warning("EQUIPSET", $"'{path}': the stored MD5 does not match; read anyway.");
                    all.AddRange(sets);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    GordianLog.Warning("EQUIPSET", $"Could not read '{path}': {ex.Message}");
                }
            }
            return all;
        }

        /// <summary>
        /// <c>/importretail</c>'s part for equipment sets: replaces the book's sets with the folder's (only the sets the files
        /// hold); returns what was imported for the chat line, or nothing when the folder has no set files.
        /// </summary>
        public static IReadOnlyList<string> Import(string folderPath, EquipmentSetBook book)
        {
            var sets = ReadFolder(folderPath);
            if (sets.Count == 0) return Array.Empty<string>();
            book.SetMany(sets);
            int used = 0;
            foreach (var (number, set) in sets)
            {
                if (!set.IsEmpty || set.Name != EquipmentSet.DefaultName(number)) used++;
            }
            return new[] { $"{used} equipment sets" };
        }
    }
}
