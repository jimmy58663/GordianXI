// src/Gordian.Core/Resources/Ui/UiElementGroupDecoder.cs
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;

namespace Gordian.Core.Resources.Ui
{
    /// <summary>
    /// Clean-room binary decoder for FFXI DAT Section 0x31 (UiElementGroup) chunks.
    /// Layout (payload after the 16-byte section header):
    /// <code>
    /// +0x00  char[16]  resource id (8-char category + 8-char name)
    /// +0x10  u8        textureCount, then textureCount x char[16] texture resource ids
    ///        u16       imageCount
    ///        per image: u8 partCount, then partCount x 61-byte parts:
    ///          +0x00  4 x (i16 x, i16 y)     destination quad: TL, TR, BL, BR
    ///          +0x10  u16 srcW, srcH, srcX, srcY
    ///          +0x18  u8                     flags
    ///          +0x19  4 x RGBA                per-corner colours, bottom row first (BL, BR, TL, TR)
    ///          +0x29  u32                    texture attributes
    ///          +0x2D  char[16]               sampled texture resource id
    /// </code>
    /// The sprite payload (quad, source rectangle, colours) and the 16-character resource ids are referenced from
    /// xi-tools (https://github.com/vekien/xi-tools, docs/title/ui_chrome.md, src/xi/ui/xi_core.py) and
    /// xi-model-viewer (https://github.com/vekien/xi-model-viewer, ui/js/images.js); the texture list, image/part
    /// counts and fixed 61-byte part framing were derived from the retail data (every group in ROM/119/51.DAT
    /// decodes to within its 16-byte section padding).
    /// </summary>
    public static class UiElementGroupDecoder
    {
        public const int ResourceIdLength = 16;
        public const int PartSize = 61;

        public static UiElementGroup? Decode(ReadOnlySpan<byte> payload, string datId)
        {
            int p = 0;
            if (payload.Length < ResourceIdLength + 3) return null;

            string id = ReadResourceId(payload.Slice(p, ResourceIdLength));
            p += ResourceIdLength;

            int textureCount = payload[p++];
            if (p + textureCount * ResourceIdLength + 2 > payload.Length) return null;
            var textures = new string[textureCount];
            for (int i = 0; i < textureCount; i++)
            {
                textures[i] = ReadResourceId(payload.Slice(p, ResourceIdLength));
                p += ResourceIdLength;
            }

            int imageCount = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(p));
            p += 2;

            var images = new List<UiImage>(imageCount);
            for (int i = 0; i < imageCount; i++)
            {
                if (p >= payload.Length) return null;
                int partCount = payload[p++];
                if (p + partCount * PartSize > payload.Length) return null;

                var parts = new UiSpritePart[partCount];
                for (int j = 0; j < partCount; j++)
                {
                    parts[j] = ReadPart(payload.Slice(p, PartSize));
                    p += PartSize;
                }
                images.Add(new UiImage { Parts = parts });
            }

            SplitResourceId(id, out string category, out string name);
            return new UiElementGroup
            {
                DatId = datId,
                Category = category,
                Name = name,
                TextureNames = textures,
                Images = images,
            };
        }

        private static UiSpritePart ReadPart(ReadOnlySpan<byte> s)
        {
            return new UiSpritePart
            {
                TopLeft = ReadPoint(s, 0x00),
                TopRight = ReadPoint(s, 0x04),
                BottomLeft = ReadPoint(s, 0x08),
                BottomRight = ReadPoint(s, 0x0C),
                SourceWidth = BinaryPrimitives.ReadUInt16LittleEndian(s.Slice(0x10)),
                SourceHeight = BinaryPrimitives.ReadUInt16LittleEndian(s.Slice(0x12)),
                SourceX = BinaryPrimitives.ReadUInt16LittleEndian(s.Slice(0x14)),
                SourceY = BinaryPrimitives.ReadUInt16LittleEndian(s.Slice(0x16)),
                Flags = s[0x18],
                // Colours run bottom row first, unlike the quad: window backgrounds author 7F 7F 7F 7F then
                // 40 40 40 40, and retail (compared against Windower) is translucent at the top, opaque at the bottom.
                ColorBottomLeft = ReadColor(s, 0x19),
                ColorBottomRight = ReadColor(s, 0x1D),
                ColorTopLeft = ReadColor(s, 0x21),
                ColorTopRight = ReadColor(s, 0x25),
                TextureAttributes = BinaryPrimitives.ReadUInt32LittleEndian(s.Slice(0x29)),
                TextureName = ReadResourceId(s.Slice(0x2D, ResourceIdLength)),
            };
        }

        private static UiPoint ReadPoint(ReadOnlySpan<byte> s, int offset) =>
            new(BinaryPrimitives.ReadInt16LittleEndian(s.Slice(offset)), BinaryPrimitives.ReadInt16LittleEndian(s.Slice(offset + 2)));

        private static UiColor ReadColor(ReadOnlySpan<byte> s, int offset) => new(s[offset], s[offset + 1], s[offset + 2], s[offset + 3]);

        /// <summary>
        /// Reads a 16-character resource id, keeping its padding (terminating at the first NUL).
        /// </summary>
        public static string ReadResourceId(ReadOnlySpan<byte> s)
        {
            int length = s.IndexOf((byte)0);
            if (length < 0) length = s.Length;
            return Encoding.ASCII.GetString(s.Slice(0, length));
        }

        /// <summary>
        /// Splits a 16-character resource id into its trimmed 8-character category and name.
        /// </summary>
        public static void SplitResourceId(string resourceId, out string category, out string name)
        {
            category = resourceId.Length >= 8 ? resourceId.Substring(0, 8).Trim() : resourceId.Trim();
            name = resourceId.Length > 8 ? resourceId.Substring(8).Trim() : string.Empty;
        }
    }
}
