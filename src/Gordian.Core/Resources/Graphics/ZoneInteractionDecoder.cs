// src/Gordian.Core/Resources/Graphics/ZoneInteractionDecoder.cs
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Numerics;
using Gordian.Core.Resources.Containers;

namespace Gordian.Core.Resources.Graphics
{
    /// <summary>
    /// One Section 0x36 ("RID") zone interaction volume: a box turned about Y, with the 4-char id whose first character
    /// gives its kind (<c>_</c> door, <c>@</c> lift, <c>z</c> zone line, <c>m</c> sub-area, ...).
    /// </summary>
    /// <param name="Id">Record +0x24 FourCC.</param>
    /// <param name="Center">Record +0x00: the box centre in zone coordinates (internal space, +Y down).</param>
    /// <param name="RotationY">Record +0x10: the box's turn about Y in radians (its only rotation).</param>
    /// <param name="Size">Record +0x18: the box's full extents along its own X (width), Y (height) and Z (depth).</param>
    public readonly record struct ZoneInteraction(string Id, Vector3 Center, float RotationY, Vector3 Size)
    {
        /// <summary>A door's volume: a thin box filling the doorway (Metalworks' guild doors: 2.01 x 2.83 x 0.07).</summary>
        public bool IsDoor => Id.Length > 0 && Id[0] == '_';
    }

    /// <summary>
    /// Clean-room decoder for the FFXI zone DAT Section 0x36 ZoneInteraction ("RID") table. The section is not
    /// encrypted; it starts with the magic <c>RID</c>, payload +0x10 holds the offset of the first table, which is a u32
    /// count, 12 bytes of padding and then 0x40-byte entries. Format referenced from xi-tools
    /// (https://github.com/vekien/xi-tools, docs/zone/subareas.md §1 and docs/zone/doors.md, after the PS2 client's
    /// <c>KO_RectData</c>); checked against Metalworks (zone 237), whose door records match its door leaves.
    /// </summary>
    public static class ZoneInteractionDecoder
    {
        private const int EntrySize = 0x40;

        /// <summary>Decodes a Section 0x36 payload; an empty list when it is short or malformed.</summary>
        public static List<ZoneInteraction> Decode(ReadOnlySpan<byte> payload)
        {
            var result = new List<ZoneInteraction>();
            if (payload.Length < 0x14 || payload[0] != (byte)'R' || payload[1] != (byte)'I' || payload[2] != (byte)'D') return result;

            int dataOffset = BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(0x10));
            if (dataOffset < 0x14 || dataOffset + 16 > payload.Length) return result;
            int count = BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(dataOffset));
            int first = dataOffset + 16;
            if (count <= 0 || count > (payload.Length - first) / EntrySize) return result;

            for (int i = 0; i < count; i++)
            {
                var entry = payload.Slice(first + (i * EntrySize), EntrySize);
                string id = ReadId(entry.Slice(0x24, 4));
                if (id.Length == 0) continue;
                result.Add(new ZoneInteraction(
                    id,
                    new Vector3(Single(entry, 0x00), Single(entry, 0x04), Single(entry, 0x08)),
                    Single(entry, 0x10),
                    new Vector3(Single(entry, 0x18), Single(entry, 0x1C), Single(entry, 0x20))));
            }
            return result;
        }

        /// <summary>Decodes the first Section 0x36 of a zone DAT; empty when the zone has none.</summary>
        public static List<ZoneInteraction> DecodeFromDat(ReadOnlySpan<byte> datBytes)
        {
            foreach (var header in DatSectionWalker.ReadHeaders(datBytes))
            {
                if (header.TypeCode != DatSectionType.ZoneInteractions || header.DataOffset + header.DataSizeBytes > datBytes.Length) continue;
                return Decode(datBytes.Slice(header.DataOffset, header.DataSizeBytes));
            }
            return new List<ZoneInteraction>();
        }

        private static float Single(ReadOnlySpan<byte> entry, int offset) => BinaryPrimitives.ReadSingleLittleEndian(entry.Slice(offset));

        private static string ReadId(ReadOnlySpan<byte> span)
        {
            int end = span.IndexOf((byte)0);
            if (end < 0) end = span.Length;
            Span<char> chars = stackalloc char[4];
            for (int i = 0; i < end; i++)
            {
                byte b = span[i];
                if (b < 0x20 || b > 0x7E) return string.Empty;
                chars[i] = (char)b;
            }
            return new string(chars.Slice(0, end));
        }
    }
}
