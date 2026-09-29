// src/Gordian.Core/Resources/Tables/ZoneDialogTable.cs
using System;
using System.Buffers.Binary;
using System.Collections.Generic;

namespace Gordian.Core.Resources.Tables
{
    /// <summary>
    /// A zone's dialog string table: the text an event script, S2C 0x036 (TalkNum) or S2C 0x02A (TalkNumWork)
    /// prints, indexed by message number (the ids LandSandBoat's zone <c>IDs.lua</c> tables carry).
    /// <para>
    /// File location referenced from XiEvents (https://github.com/atom0s/XiEvents, "Event DAT Files.md"): the
    /// English table of zone <c>z</c> is file id 6420 + z for zones 0-255 and 85591 + (z - 256) for zones 256-299,
    /// the Japanese one 6120 + z / 85291 + (z - 256) (checked against the retail file table, 2026-09-28: every
    /// ROM path the XiEvents list gives resolves to those ids).
    /// </para>
    /// <para>
    /// Layout (worked out from the retail Southern San d'Oria table ROM/25/39.DAT, 2026-09-28, and checked against
    /// LandSandBoat's text ids for that zone): the first dword is <c>0x10000000 | (fileSize - 4)</c> in the clear;
    /// every byte after it is XOR 0x80. The decoded body starts with a table of dword offsets, one per message,
    /// relative to the body's start (byte 4 of the file); the table's size is the first offset, so the message
    /// count is that offset / 4. Each message runs to the next offset (the last to the end of the file) and is
    /// a control-coded string (<see cref="EventMessageDecoder"/>).
    /// </para>
    /// </summary>
    public sealed class ZoneDialogTable
    {
        private const int HeaderSize = 4;
        private const byte BodyXor = 0x80;

        private readonly byte[] _body;
        private readonly int[] _offsets;

        private ZoneDialogTable(byte[] body, int[] offsets)
        {
            _body = body;
            _offsets = offsets;
        }

        /// <summary>Number of messages in the table.</summary>
        public int Count => _offsets.Length;

        /// <summary>The file id of a zone's English (or Japanese) dialog table, or -1 for a zone id out of range.</summary>
        public static int GetFileId(int zoneId, bool japanese = false)
        {
            if (zoneId < 0 || zoneId > 299) return -1;
            if (zoneId < 256) return (japanese ? 6120 : 6420) + zoneId;
            return (japanese ? 85291 : 85591) + (zoneId - 256);
        }

        /// <summary>Decodes a dialog DAT. Returns null when the buffer is not one.</summary>
        public static ZoneDialogTable? Parse(ReadOnlySpan<byte> file)
        {
            if (file.Length < HeaderSize + 4) return null;
            uint header = BinaryPrimitives.ReadUInt32LittleEndian(file);
            int declared = (int)(header & 0x00FFFFFF);
            if ((header >> 24) != 0x10 || declared != file.Length - HeaderSize) return null;

            var body = new byte[file.Length - HeaderSize];
            for (int i = 0; i < body.Length; i++) body[i] = (byte)(file[HeaderSize + i] ^ BodyXor);

            int firstOffset = BinaryPrimitives.ReadInt32LittleEndian(body);
            if (firstOffset <= 0 || firstOffset % 4 != 0 || firstOffset > body.Length) return null;
            int count = firstOffset / 4;
            var offsets = new int[count];
            for (int i = 0; i < count; i++)
            {
                int offset = BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(i * 4));
                if (offset < firstOffset || offset > body.Length) return null;
                offsets[i] = offset;
            }
            return new ZoneDialogTable(body, offsets);
        }

        /// <summary>The raw (control-coded, decrypted) bytes of a message, empty for an index out of range.</summary>
        public ReadOnlySpan<byte> GetRaw(int messageId)
        {
            if (messageId < 0 || messageId >= _offsets.Length) return ReadOnlySpan<byte>.Empty;
            int start = _offsets[messageId];
            int end = messageId + 1 < _offsets.Length ? _offsets[messageId + 1] : _body.Length;
            if (end < start) end = start;
            return _body.AsSpan(start, end - start);
        }

        /// <summary>Decodes a message, or returns null for an index out of range.</summary>
        public EventMessage? GetMessage(int messageId)
        {
            var raw = GetRaw(messageId);
            return messageId < 0 || messageId >= _offsets.Length ? null : EventMessageDecoder.Decode(raw);
        }

        /// <summary>The plain text of a message (control codes dropped, lines joined with spaces), for logs and tests.</summary>
        public string GetPlainText(int messageId) => GetMessage(messageId)?.ToPlainText() ?? string.Empty;

        /// <summary>Every message id whose plain text contains <paramref name="text"/> (case-insensitive), for tests and tools.</summary>
        public IEnumerable<int> Find(string text)
        {
            for (int i = 0; i < Count; i++)
            {
                if (GetPlainText(i).Contains(text, StringComparison.OrdinalIgnoreCase)) yield return i;
            }
        }
    }
}
