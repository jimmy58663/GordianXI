// src/Gordian.Core/Resources/Tables/ItemTableDecoder.cs
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Gordian.Core.Resources.Models;

namespace Gordian.Core.Resources.Tables
{
    /// <summary>
    /// Clean-room binary decoder for FFXI item DAT files.
    /// Handles circular bit-rotation decryption, auto-detects legacy (0xC00) vs modern retail (0x1400)
    /// record strides, decodes typed equipment headers, strings, and extracts stat modifiers.
    /// Derived from community research in xi-model-viewer (https://github.com/vekien/xi-model-viewer)
    /// and LandSandBoat (https://github.com/LandSandBoat/server).
    /// </summary>
    public static class ItemTableDecoder
    {
        public const int LegacyStride = 0xC00;       // 3,072 bytes (pre-Sept 2026)
        public const int RetailStride = 0x1400;      // 5,120 bytes (modern retail)
        public const int IconOffset = 0x280;
        public const byte RecordTerminator = 0xFF;

        private static readonly Regex StatRegex = new(
            @"(""[^""]+""|[A-Za-z][A-Za-z.' ]{0,28}?)\s*([:+-])\s*([+-]?)(\d+)(%?)",
            RegexOptions.Compiled);

        private static readonly HashSet<string> CoreStats = new(StringComparer.OrdinalIgnoreCase)
        {
            "DEF", "DMG", "Delay", "HP", "MP", "STR", "DEX", "VIT", "AGI", "INT", "MND", "CHR"
        };

        /// <summary>
        /// Decrypts a rotated item block in-place or into a destination buffer.
        /// Circular left-shift by 3 bits: (b &lt;&lt; 3) | (b &gt;&gt; 5).
        /// </summary>
        public static void DecryptBlock(ReadOnlySpan<byte> source, Span<byte> destination)
        {
            int len = Math.Min(source.Length, destination.Length);
            for (int i = 0; i < len; i++)
            {
                byte b = source[i];
                destination[i] = (byte)((b << 3) | (b >> 5));
            }
        }

        /// <summary>
        /// Automatically detects the item record stride (0xC00 or 0x1400) from buffer length and contents.
        /// </summary>
        public static int DetectStride(ReadOnlySpan<byte> data)
        {
            int len = data.Length;
            if (len == 0) return 0;

            bool fitsRetail = len >= RetailStride && len % RetailStride == 0;
            bool fitsLegacy = len >= LegacyStride && len % LegacyStride == 0;

            if (fitsRetail && !fitsLegacy) return RetailStride;
            if (fitsLegacy && !fitsRetail) return LegacyStride;

            if (fitsRetail && fitsLegacy)
            {
                // Both divide (e.g. multiple of 15,360 bytes). Check record terminator.
                int retailHits = CountTerminators(data, RetailStride);
                int legacyHits = CountTerminators(data, LegacyStride);

                if (retailHits > legacyHits) return RetailStride;
                if (legacyHits > retailHits) return LegacyStride;

                // Fall back to probing sequential item IDs
                if (ProbeSequentialIds(data, RetailStride)) return RetailStride;
                if (ProbeSequentialIds(data, LegacyStride)) return LegacyStride;

                return LegacyStride;
            }

            return 0;
        }

        private static int CountTerminators(ReadOnlySpan<byte> data, int stride)
        {
            int count = data.Length / stride;
            int hits = 0;
            for (int i = 0; i < count; i++)
            {
                int endOffset = (i + 1) * stride - 1;
                if (endOffset < data.Length && data[endOffset] == RecordTerminator)
                {
                    hits++;
                }
            }
            return hits;
        }

        private static bool ProbeSequentialIds(ReadOnlySpan<byte> data, int stride)
        {
            int count = Math.Min(4, data.Length / stride);
            if (count < 2) return false;

            Span<byte> scratch = stackalloc byte[4];
            uint? prevId = null;

            for (int i = 0; i < count; i++)
            {
                int off = i * stride;
                DecryptBlock(data.Slice(off, 4), scratch);
                uint id = BinaryPrimitives.ReadUInt32LittleEndian(scratch);

                if (prevId != null && id != prevId.Value + 1)
                {
                    return false;
                }
                prevId = id;
            }
            return true;
        }

        /// <summary>
        /// Parses an entire item DAT file and returns all decoded item records.
        /// </summary>
        public static List<ItemRecord> ParseItemDat(ReadOnlySpan<byte> data)
        {
            var results = new List<ItemRecord>();
            int stride = DetectStride(data);
            if (stride == 0) return results;

            int recordCount = data.Length / stride;
            byte[] decrypted = new byte[stride];

            for (int i = 0; i < recordCount; i++)
            {
                var blockSlice = data.Slice(i * stride, stride);
                DecryptBlock(blockSlice, decrypted);

                var item = DecodeSingleRecord(decrypted, stride == RetailStride);
                if (item != null && item.ItemId != 0)
                {
                    results.Add(item);
                }
            }

            return results;
        }

        /// <summary>
        /// Decodes a single decrypted item record block.
        /// </summary>
        public static ItemRecord? DecodeSingleRecord(ReadOnlySpan<byte> block, bool isRetail)
        {
            if (block.Length < 0x30) return null;

            uint itemId = BinaryPrimitives.ReadUInt32LittleEndian(block.Slice(0, 4));
            if (itemId == 0) return null;

            var item = new ItemRecord { ItemId = itemId };

            if (!isRetail)
            {
                // Legacy offsets
                item.Flags = BinaryPrimitives.ReadUInt16LittleEndian(block.Slice(0x04, 2));
                item.StackSize = BinaryPrimitives.ReadUInt16LittleEndian(block.Slice(0x06, 2));
                item.ItemType = BinaryPrimitives.ReadUInt16LittleEndian(block.Slice(0x08, 2));
                item.ResourceId = BinaryPrimitives.ReadUInt16LittleEndian(block.Slice(0x0A, 2));
                item.ValidTargets = BinaryPrimitives.ReadUInt16LittleEndian(block.Slice(0x0C, 2));

                if (block.Length >= 0x1A)
                {
                    item.Level = BinaryPrimitives.ReadUInt16LittleEndian(block.Slice(0x0E, 2));
                    item.EquipSlotsMask = BinaryPrimitives.ReadUInt16LittleEndian(block.Slice(0x10, 2));
                    item.RacesMask = BinaryPrimitives.ReadUInt16LittleEndian(block.Slice(0x12, 2));
                    item.JobsMask = BinaryPrimitives.ReadUInt32LittleEndian(block.Slice(0x14, 4));
                    item.SuperiorLevel = block[0x18];
                }

                if (block.Length >= 0x28)
                {
                    item.ShieldSize = block[0x1A];
                    item.MaxCharges = block[0x1C];
                    item.CastTime = BinaryPrimitives.ReadUInt16LittleEndian(block.Slice(0x1E, 2));
                    item.UseDelay = BinaryPrimitives.ReadUInt16LittleEndian(block.Slice(0x20, 2));
                    item.ReuseDelay = BinaryPrimitives.ReadUInt32LittleEndian(block.Slice(0x22, 4));
                    item.ItemLevel = (byte)BinaryPrimitives.ReadUInt16LittleEndian(block.Slice(0x26, 2));
                }

                if (block.Length >= 0x34)
                {
                    item.Damage = BinaryPrimitives.ReadUInt16LittleEndian(block.Slice(0x1C, 2));
                    item.Delay = BinaryPrimitives.ReadInt16LittleEndian(block.Slice(0x1E, 2));
                    item.Dps = BinaryPrimitives.ReadUInt16LittleEndian(block.Slice(0x20, 2));
                    item.Skill = block[0x22];
                    item.JugSize = block[0x23];
                    ushort wepIlvl = BinaryPrimitives.ReadUInt16LittleEndian(block.Slice(0x32, 2));
                    if (wepIlvl > 0) item.ItemLevel = (byte)wepIlvl;
                }
            }
            else
            {
                // Modern retail offsets
                item.Flags = BinaryPrimitives.ReadUInt32LittleEndian(block.Slice(0x04, 4));
                item.StackSize = BinaryPrimitives.ReadUInt16LittleEndian(block.Slice(0x08, 2));
                item.ItemType = BinaryPrimitives.ReadUInt16LittleEndian(block.Slice(0x0A, 2));
                item.ResourceId = BinaryPrimitives.ReadUInt16LittleEndian(block.Slice(0x0C, 2));
                item.ValidTargets = BinaryPrimitives.ReadUInt16LittleEndian(block.Slice(0x0E, 2));

                if (block.Length >= 0x1E)
                {
                    item.Level = BinaryPrimitives.ReadUInt16LittleEndian(block.Slice(0x10, 2));
                    item.EquipSlotsMask = BinaryPrimitives.ReadUInt16LittleEndian(block.Slice(0x12, 2));
                    item.RacesMask = BinaryPrimitives.ReadUInt16LittleEndian(block.Slice(0x14, 2));
                    item.JobsMask = BinaryPrimitives.ReadUInt32LittleEndian(block.Slice(0x18, 4));
                    item.SuperiorLevel = block[0x1C];
                }

                if (block.Length >= 0x2C)
                {
                    item.ShieldSize = block[0x1E];
                    item.MaxCharges = block[0x20];
                    item.CastTime = BinaryPrimitives.ReadUInt16LittleEndian(block.Slice(0x22, 2));
                    item.UseDelay = BinaryPrimitives.ReadUInt16LittleEndian(block.Slice(0x24, 2));
                    item.ReuseDelay = BinaryPrimitives.ReadUInt32LittleEndian(block.Slice(0x26, 4));
                    item.ItemLevel = (byte)BinaryPrimitives.ReadUInt16LittleEndian(block.Slice(0x2A, 2));
                }

                if (block.Length >= 0x38)
                {
                    item.Damage = BinaryPrimitives.ReadUInt16LittleEndian(block.Slice(0x20, 2));
                    item.Delay = BinaryPrimitives.ReadInt16LittleEndian(block.Slice(0x22, 2));
                    item.Dps = BinaryPrimitives.ReadUInt16LittleEndian(block.Slice(0x24, 2));
                    item.Skill = block[0x26];
                    item.JugSize = block[0x27];
                    ushort wepIlvl = BinaryPrimitives.ReadUInt16LittleEndian(block.Slice(0x36, 2));
                    if (wepIlvl > 0) item.ItemLevel = (byte)wepIlvl;
                }
            }

            // Decode embedded strings
            DecodeStrings(block, item);

            // Extract embedded 32x32 icon if present
            if (block.Length >= IconOffset + 0x40)
            {
                item.IconRgbaPixels = DecodeEmbeddedIcon(block.Slice(IconOffset));
            }

            return item;
        }

        private static void DecodeStrings(ReadOnlySpan<byte> block, ItemRecord item)
        {
            int scanStart = 0x08;
            int scanEnd = Math.Min(IconOffset, block.Length - 16);

            for (int pos = scanStart; pos <= 0x80 && pos + 4 <= scanEnd; pos += 2)
            {
                uint count = BinaryPrimitives.ReadUInt32LittleEndian(block.Slice(pos, 4));
                if (count is < 1 or > 8) continue;

                int firstExpected = 4 + (int)count * 8;
                uint firstActual = BinaryPrimitives.ReadUInt32LittleEndian(block.Slice(pos + 4, 4));

                // Check relative offset layout (standard FFXI) or absolute offset layout
                bool isRelative = firstActual == (uint)firstExpected;
                bool isAbsolute = firstActual >= (uint)pos && firstActual < (uint)IconOffset;

                if (!isRelative && !isAbsolute) continue;

                bool valid = true;
                var offsets = new int[count];
                var flags = new uint[count];

                for (int i = 0; i < (int)count; i++)
                {
                    int entryPos = pos + 4 + (i * 8);
                    if (entryPos + 8 > scanEnd) { valid = false; break; }

                    uint off = BinaryPrimitives.ReadUInt32LittleEndian(block.Slice(entryPos, 4));
                    uint flag = BinaryPrimitives.ReadUInt32LittleEndian(block.Slice(entryPos + 4, 4));

                    if (isRelative)
                    {
                        if (off < (uint)firstExpected || pos + off >= (uint)IconOffset || flag > 1)
                        {
                            valid = false;
                            break;
                        }
                        offsets[i] = pos + (int)off;
                    }
                    else
                    {
                        if (off != 0 && (off < (uint)pos || off >= (uint)IconOffset))
                        {
                            valid = false;
                            break;
                        }
                        offsets[i] = (int)off;
                    }
                    flags[i] = flag;
                }

                if (valid)
                {
                    var strings = new List<string>();
                    for (int i = 0; i < offsets.Length; i++)
                    {
                        int strStart = offsets[i];
                        if (strStart == 0 || strStart >= block.Length)
                        {
                            strings.Add(string.Empty);
                            continue;
                        }

                        if (flags[i] == 0)
                        {
                            // In standard FFXI relative item tables, text has a 0x1C-byte prefix
                            if (isRelative && strStart + 0x1C < block.Length)
                            {
                                strStart += 0x1C;
                            }
                            else if (strStart < block.Length && block[strStart] == 0x1C)
                            {
                                strStart++;
                            }
                            strings.Add(DMsgStringTable.DecodeTextWithGlyphs(block.Slice(strStart)));
                        }
                        else
                        {
                            strings.Add(string.Empty);
                        }
                    }

                    if (strings.Count == 5)
                    {
                        // EN layout: Name, Article, LogName, LogPlural, Description
                        item.Name = strings[0];
                        item.LogName = strings[2];
                        item.LogPlural = strings[3];
                        item.Description = strings[4];
                    }
                    else if (strings.Count == 4)
                    {
                        item.Name = strings[0];
                        item.LogName = strings[1];
                        item.LogPlural = strings[2];
                        item.Description = strings[3];
                    }
                    else if (strings.Count == 2)
                    {
                        item.Name = strings[0];
                        item.Description = strings[1];
                    }
                    else if (strings.Count > 0)
                    {
                        item.Name = strings[0];
                        if (strings.Count > 1) item.Description = strings[^1];
                    }

                    ParseDescriptionStats(item.Description, item.ExtractedStats);
                    return;
                }
            }
        }

        private static void ParseDescriptionStats(string desc, Dictionary<string, int> stats)
        {
            if (string.IsNullOrWhiteSpace(desc)) return;

            var matches = StatRegex.Matches(desc);
            for (int i = 0; i < matches.Count; i++)
            {
                var m = matches[i];
                string name = m.Groups[1].Value.Trim('"', ' ');
                if (string.IsNullOrWhiteSpace(name)) continue;

                string sep = m.Groups[2].Value;
                string signStr = m.Groups[3].Value;
                int val = int.Parse(m.Groups[4].Value);

                int sign = (sep == "-" || signStr == "-") ? -1 : 1;
                val *= sign;

                if (CoreStats.Contains(name))
                {
                    stats.TryAdd(name, val);
                }
            }
        }

        /// <summary>
        /// Decodes the 32x32 paletted texture icon located at offset 0x280 of an item block into raw 32-bit RGBA.
        /// </summary>
        public static byte[]? DecodeEmbeddedIcon(ReadOnlySpan<byte> iconData)
        {
            if (iconData.Length < 0x40) return null;

            uint totalSize = BinaryPrimitives.ReadUInt32LittleEndian(iconData.Slice(0, 4));
            if (totalSize == 0 || totalSize > (uint)iconData.Length) return null;

            byte type = iconData[4];
            if (type != 0x91 && type != 0x81 && type != 0xB1 && type != 0x01) return null;

            int p = 5 + 0x10 + 4; // Skip type, name (16 bytes), 0x28 marker
            int width = BinaryPrimitives.ReadInt32LittleEndian(iconData.Slice(p, 4)); p += 4;
            int height = BinaryPrimitives.ReadInt32LittleEndian(iconData.Slice(p, 4)); p += 4;
            p += 2; // skip 1
            ushort bitCount = BinaryPrimitives.ReadUInt16LittleEndian(iconData.Slice(p, 2)); p += 2;
            p += 20; // 5 * 4 zeros
            uint paletteBits = BinaryPrimitives.ReadUInt32LittleEndian(iconData.Slice(p, 4)); p += 4;

            if (type == 0xB1) p += 4;

            if (width <= 0 || height <= 0 || width > 128 || height > 128) return null;

            byte[] rgba = new byte[width * height * 4];

            if (bitCount == 32)
            {
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        if (p + 4 > iconData.Length) return rgba;
                        uint c = BinaryPrimitives.ReadUInt32LittleEndian(iconData.Slice(p, 4)); p += 4;
                        int o = ((height - 1 - y) * width + x) * 4;
                        rgba[o] = (byte)((c >> 16) & 0xFF);     // R
                        rgba[o + 1] = (byte)((c >> 8) & 0xFF);   // G
                        rgba[o + 2] = (byte)(c & 0xFF);          // B
                        rgba[o + 3] = (byte)((c >> 24) & 0xFF);  // A
                    }
                }
            }
            else
            {
                // Paletted 256 colors
                Span<uint> palette = stackalloc uint[256];
                if (paletteBits == 0x10)
                {
                    // 16-bit RGB555 palette
                    for (int i = 0; i < 256; i++)
                    {
                        if (p + 2 > iconData.Length) break;
                        ushort v = BinaryPrimitives.ReadUInt16LittleEndian(iconData.Slice(p, 2)); p += 2;
                        byte a = (byte)((v & 0x8000) != 0 ? 0x80 : 0x00);
                        byte r = (byte)(((v >> 10) & 0x1F) * 255 / 31);
                        byte g = (byte)(((v >> 5) & 0x1F) * 255 / 31);
                        byte b = (byte)((v & 0x1F) * 255 / 31);
                        palette[i] = (uint)((a << 24) | (r << 16) | (g << 8) | b);
                    }
                }
                else
                {
                    // 32-bit palette
                    for (int i = 0; i < 256; i++)
                    {
                        if (p + 4 > iconData.Length) break;
                        palette[i] = BinaryPrimitives.ReadUInt32LittleEndian(iconData.Slice(p, 4)); p += 4;
                    }
                }

                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        if (p >= iconData.Length) return rgba;
                        byte index = iconData[p++];
                        uint c = palette[index];
                        int o = ((height - 1 - y) * width + x) * 4;
                        rgba[o] = (byte)((c >> 16) & 0xFF);
                        rgba[o + 1] = (byte)((c >> 8) & 0xFF);
                        rgba[o + 2] = (byte)(c & 0xFF);
                        rgba[o + 3] = (byte)Math.Min(255, ((c >> 24) & 0xFF) * 2);
                    }
                }
            }

            return rgba;
        }
    }
}
