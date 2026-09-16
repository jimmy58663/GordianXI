// src/Gordian.Core/Resources/Tables/DMsgStringTable.cs
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;
using Gordian.Core.Resources.Models;

namespace Gordian.Core.Resources.Tables
{
    /// <summary>
    /// Clean-room binary parser for FFXI d_msg string table resource DAT files.
    /// Handles XOR 0xFF decryption, fixed and variable stride indexing, CP932 text decoding,
    /// and client elemental glyph mapping.
    /// Derived from community research in xi-model-viewer (https://github.com/vekien/xi-model-viewer)
    /// and xi-tools (xi_database.py).
    /// </summary>
    public sealed class DMsgStringTable
    {
        private const int DmsgMetaSize = 0x18;
        private static readonly string[] ElementGlyphs = { "Fire", "Ice", "Wind", "Earth", "Lightning", "Water", "Light", "Dark" };

        static DMsgStringTable()
        {
            try
            {
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            }
            catch
            {
                // Fallback handled gracefully in decoding routine
            }
        }

        private readonly List<DMsgRecord> _records = new();

        public IReadOnlyList<DMsgRecord> Records => _records;
        public int Count => _records.Count;

        /// <summary>
        /// Checks whether the specified buffer starts with the d_msg container signature.
        /// </summary>
        public static bool IsDMsg(ReadOnlySpan<byte> data)
        {
            return data.Length >= 5 &&
                   data[0] == (byte)'d' &&
                   data[1] == (byte)'_' &&
                   data[2] == (byte)'m' &&
                   data[3] == (byte)'s' &&
                   data[4] == (byte)'g';
        }

        /// <summary>
        /// Decodes a d_msg binary DAT buffer into a structured string table.
        /// </summary>
        public static DMsgStringTable? Parse(ReadOnlySpan<byte> buffer, IReadOnlyList<string>? fieldNames = null)
        {
            if (buffer.Length < 0x40 || !IsDMsg(buffer))
            {
                return null;
            }

            byte xorMask = buffer[0x0A] != 0 ? (byte)0xFF : (byte)0x00;
            uint rawFileSize = BinaryPrimitives.ReadUInt32LittleEndian(buffer.Slice(0x14, 4));
            int fileSize = (int)Math.Min(rawFileSize > 0 ? rawFileSize : (uint)buffer.Length, (uint)buffer.Length);

            int tableOffset = (int)BinaryPrimitives.ReadUInt32LittleEndian(buffer.Slice(0x18, 4));
            int tableSize = (int)BinaryPrimitives.ReadUInt32LittleEndian(buffer.Slice(0x1C, 4));
            int stride = (int)BinaryPrimitives.ReadUInt32LittleEndian(buffer.Slice(0x20, 4));
            int num = (int)BinaryPrimitives.ReadUInt32LittleEndian(buffer.Slice(0x28, 4));

            if (tableOffset >= fileSize)
            {
                return null;
            }

            // Copy and decrypt the payload body slice
            int bodyLen = fileSize - tableOffset;
            byte[] body = new byte[bodyLen];
            buffer.Slice(tableOffset, bodyLen).CopyTo(body);

            if (xorMask != 0)
            {
                for (int i = 0; i < body.Length; i++)
                {
                    body[i] ^= xorMask;
                }
            }

            var table = new DMsgStringTable();

            if (tableSize == 0)
            {
                // Fixed stride layout
                if (stride <= 0) return null;
                int actualCount = Math.Min(num, body.Length / stride);

                for (int i = 0; i < actualCount; i++)
                {
                    int blockOffset = i * stride;
                    var slice = body.AsSpan(blockOffset, stride);
                    var record = DecodeBlock(i, tableOffset + blockOffset, slice, fieldNames);
                    table._records.Add(record);
                }
            }
            else
            {
                // Variable stride layout: tableSize bytes contain index pairs (offset, length)
                int actualCount = Math.Min(num, tableSize / 8);
                int baseDataOffset = tableSize;

                for (int i = 0; i < actualCount; i++)
                {
                    int entryOffset = i * 8;
                    if (entryOffset + 8 > body.Length) break;

                    uint relOff = BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(entryOffset, 4));
                    uint relLen = BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(entryOffset + 4, 4));

                    int start = baseDataOffset + (int)relOff;
                    int len = (int)relLen;

                    if (start + len > body.Length)
                    {
                        table._records.Add(new DMsgRecord(i, tableOffset + start, Array.Empty<string>()));
                        continue;
                    }

                    var slice = body.AsSpan(start, len);
                    var record = DecodeBlock(i, tableOffset + start, slice, fieldNames);
                    table._records.Add(record);
                }
            }

            return table;
        }

        private static DMsgRecord DecodeBlock(int index, int absoluteOffset, ReadOnlySpan<byte> block, IReadOnlyList<string>? fieldNames)
        {
            if (block.Length < 4)
            {
                return new DMsgRecord(index, absoluteOffset, Array.Empty<string>());
            }

            uint subCount = BinaryPrimitives.ReadUInt32LittleEndian(block.Slice(0, 4));
            if (subCount == 0 || subCount > 64)
            {
                return new DMsgRecord(index, absoluteOffset, Array.Empty<string>());
            }

            var subStrings = new List<string>((int)subCount);
            var named = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < (int)subCount; i++)
            {
                int entryOff = 4 + (i * 8);
                if (entryOff + 8 > block.Length) break;

                uint strOffset = BinaryPrimitives.ReadUInt32LittleEndian(block.Slice(entryOff, 4));
                if (strOffset < 4 || strOffset + 4 > (uint)block.Length)
                {
                    subStrings.Add(string.Empty);
                    continue;
                }

                uint marker = BinaryPrimitives.ReadUInt32LittleEndian(block.Slice((int)strOffset, 4));

                if (marker == 1)
                {
                    int textStart = (int)strOffset + 4 + DmsgMetaSize;
                    if (textStart < block.Length)
                    {
                        string decoded = DecodeTextWithGlyphs(block.Slice(textStart));
                        subStrings.Add(decoded);
                    }
                    else
                    {
                        subStrings.Add(string.Empty);
                    }
                }
                else
                {
                    // Numeric sub-entry
                    subStrings.Add(marker.ToString());
                }

                if (fieldNames != null && i < fieldNames.Count)
                {
                    named[fieldNames[i]] = subStrings[^1];
                }
            }

            return new DMsgRecord(index, absoluteOffset, subStrings, named);
        }

        /// <summary>
        /// Decodes null-terminated Shift-JIS/CP932 text while converting FFXI custom elemental glyph codes.
        /// </summary>
        public static string DecodeTextWithGlyphs(ReadOnlySpan<byte> span)
        {
            int end = 0;
            while (end < span.Length && span[end] != 0)
            {
                end++;
            }

            if (end == 0) return string.Empty;

            var slice = span.Slice(0, end);
            var sb = new StringBuilder(end);
            int runStart = 0;

            for (int i = 0; i < slice.Length; i++)
            {
                byte b = slice[i];

                if (b == 0xEF && i + 1 < slice.Length)
                {
                    byte next = slice[i + 1];
                    int glyphIdx = next - 0x1F;

                    // Flush prior text
                    if (i > runStart)
                    {
                        sb.Append(DecodeCp932(slice.Slice(runStart, i - runStart)));
                    }

                    if (glyphIdx >= 0 && glyphIdx < ElementGlyphs.Length)
                    {
                        sb.Append(ElementGlyphs[glyphIdx]);
                    }

                    i++; // Skip glyph second byte
                    runStart = i + 1;
                }
                else if (b == 0xFD && i + 1 < slice.Length)
                {
                    // Auto-translate bracket token (skip next 4 bytes id)
                    if (i > runStart)
                    {
                        sb.Append(DecodeCp932(slice.Slice(runStart, i - runStart)));
                    }

                    int skipLen = Math.Min(4, slice.Length - 1 - i);
                    i += skipLen;
                    runStart = i + 1;
                }
            }

            if (runStart < slice.Length)
            {
                sb.Append(DecodeCp932(slice.Slice(runStart)));
            }

            return sb.ToString().TrimEnd();
        }

        private static string DecodeCp932(ReadOnlySpan<byte> bytes)
        {
            if (bytes.IsEmpty) return string.Empty;

            try
            {
                var enc = Encoding.GetEncoding(932);
                return enc.GetString(bytes);
            }
            catch
            {
                // Fallback to ASCII printable characters
                var sb = new StringBuilder(bytes.Length);
                for (int i = 0; i < bytes.Length; i++)
                {
                    byte b = bytes[i];
                    if (b >= 0x20 && b < 0x7F) sb.Append((char)b);
                    else if (b == 0x0A) sb.Append('\n');
                }
                return sb.ToString();
            }
        }
    }
}
