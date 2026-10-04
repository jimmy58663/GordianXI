// src/Gordian.Core/Resources/Tables/XiStringTable.cs
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;

namespace Gordian.Core.Resources.Tables
{
    /// <summary>
    /// A client string table in the <c>XISTRING</c> format (ROM/97 and some ROM/165 files, e.g. the lobby's status and
    /// help text ROM/165/71): the magic <c>XISTRING</c>, the string count as a u32 at 0x24, an index of 12-byte entries
    /// from 0x38 (u32 offset into the data, u16 length, u16 flags where bit 0 marks format codes, 4 unused bytes), then
    /// NUL-terminated CP932 strings. Layout as recorded in docs/ui/stock-ui.md (menu string tables, from the retail
    /// files); xi-tools (https://github.com/vekien/xi-tools) points at the same files.
    /// </summary>
    public sealed class XiStringTable
    {
        private static readonly byte[] Magic = "XISTRING"u8.ToArray();
        private const int CountOffset = 0x24, IndexOffset = 0x38, EntrySize = 12;

        static XiStringTable()
        {
            try { Encoding.RegisterProvider(CodePagesEncodingProvider.Instance); } catch { }
        }

        private readonly List<string> _strings;

        private XiStringTable(List<string> strings) => _strings = strings;

        public int Count => _strings.Count;

        /// <summary>The string at <paramref name="index"/>, or an empty string when out of range.</summary>
        public string this[int index] => (uint)index < (uint)_strings.Count ? _strings[index] : string.Empty;

        public static bool IsXiString(ReadOnlySpan<byte> data) => data.Length >= Magic.Length && data[..Magic.Length].SequenceEqual(Magic);

        /// <summary>Parses a table; null when the buffer is not an <c>XISTRING</c> table.</summary>
        public static XiStringTable? Parse(ReadOnlySpan<byte> data)
        {
            if (!IsXiString(data) || data.Length < IndexOffset) return null;
            int count = BinaryPrimitives.ReadInt32LittleEndian(data[CountOffset..]);
            if (count < 0 || IndexOffset + (long)count * EntrySize > data.Length) return null;

            Encoding text;
            try { text = Encoding.GetEncoding(932); }
            catch { text = Encoding.ASCII; }

            int dataStart = IndexOffset + count * EntrySize;
            var strings = new List<string>(count);
            for (int i = 0; i < count; i++)
            {
                int entry = IndexOffset + i * EntrySize;
                int offset = BinaryPrimitives.ReadInt32LittleEndian(data[entry..]);
                int at = dataStart + offset;
                if (offset < 0 || at >= data.Length)
                {
                    strings.Add(string.Empty);
                    continue;
                }
                var rest = data[at..];
                int end = rest.IndexOf((byte)0);
                strings.Add(text.GetString(end < 0 ? rest : rest[..end]));
            }
            return new XiStringTable(strings);
        }
    }
}
