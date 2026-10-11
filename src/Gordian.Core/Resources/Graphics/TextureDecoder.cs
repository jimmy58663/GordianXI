// src/Gordian.Core/Resources/Graphics/TextureDecoder.cs
using System;
using System.Buffers.Binary;
using System.Text;

namespace Gordian.Core.Resources.Graphics
{
    /// <summary>
    /// Decoded image buffer with dimensions and raw 32-bit RGBA pixels.
    /// </summary>
    public sealed class DecodedTexture
    {
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Where the texture was decoded from, unique across the install: the DAT (file id or path) and the section's
        /// offset in it (<see cref="SourceOf"/>). Caches key on this, never on <see cref="Name"/>, which repeats across
        /// DATs with different pixels. Empty for a texture built in code or from an unknown DAT (cached on its own).
        /// </summary>
        public string Source { get; set; } = string.Empty;

        /// <summary>A <see cref="Source"/> for the section at <paramref name="sectionOffset"/> of DAT <paramref name="dat"/> (empty without a DAT).</summary>
        public static string SourceOf(string? dat, int sectionOffset) => string.IsNullOrEmpty(dat) ? string.Empty : $"{dat}@{sectionOffset}";

        /// <summary>The DAT label of a file id, for <see cref="SourceOf"/>.</summary>
        public static string FileLabel(int fileId) => $"file{fileId}";

        public int Width { get; }
        public int Height { get; }
        public byte[] RgbaPixels { get; }

        /// <summary>
        /// True when <see cref="TextureDecoder"/> doubled the alpha of a paletted (8 / 16 bpp) texture (0x80 -> 0xFF,
        /// clamped). FFXI authors texture alpha at half scale (0x80 = opaque) in paletted and DXT3 textures alike (DXT3
        /// peaks at 0x88), but only paletted alpha is doubled here, for the terrain and model shaders. The particle shader
        /// expects half-scale texels, so it halves these again (#208).
        /// </summary>
        public bool AlphaDoubled { get; init; }

        public DecodedTexture(string name, int width, int height, byte[] rgbaPixels)
        {
            Name = name;
            Width = width;
            Height = height;
            RgbaPixels = rgbaPixels;
        }

        public override string ToString() => $"Texture [{Name}] ({Width}x{Height}, {RgbaPixels.Length} bytes RGBA)";
    }

    /// <summary>
    /// Clean-room binary decoder for FFXI Section 0x20 Texture resources.
    /// Supports Paletted (8bpp, 16bpp, RGBA32) and S3TC / DXT (DXT1, DXT3, DXT5) compressed textures.
    /// Derived from community research in xi-model-viewer (https://github.com/vekien/xi-model-viewer)
    /// and xi-tools (xi/entity/mesh/xi_export.py).
    /// </summary>
    public static class TextureDecoder
    {
        /// <summary>
        /// Decodes a Section 0x20 texture payload into a 32-bit RGBA pixel buffer.
        /// </summary>
        public static DecodedTexture? DecodeTexture(ReadOnlySpan<byte> data)
        {
            if (data.Length < 0x20) return null;

            byte texType = data[0];
            string name = ReadCString(data.Slice(1, 16));

            if (texType == 0xA1)
            {
                // DXT compressed texture
                return DecodeDxtTexture(data, name);
            }

            if (texType is 0x01 or 0x05 or 0x81 or 0x91 or 0xB1)
            {
                // Paletted or uncompressed texture
                return DecodePalettedTexture(data, name, texType);
            }

            return null;
        }

        private static DecodedTexture? DecodePalettedTexture(ReadOnlySpan<byte> data, string name, byte texType)
        {
            // The header runs to 0x39 (the palette-bits word ends there): a shorter payload would read past its end.
            if (data.Length < 0x39) return null;

            int p = 1 + 16 + 4; // skip type, name, 0x28
            int width = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(p, 4)); p += 4;
            int height = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(p, 4)); p += 4;
            p += 2; // skip 1
            ushort bitCount = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(p, 2)); p += 2;
            p += 20; // 5 * 4 zeros
            uint paletteBits = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(p, 4)); p += 4;

            if (texType == 0xB1) p += 4;

            if (width <= 0 || height <= 0 || width > 4096 || height > 4096) return null;

            byte[] rgba = new byte[width * height * 4];

            if (bitCount == 32)
            {
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        if (p + 4 > data.Length) return new DecodedTexture(name, width, height, rgba);
                        uint c = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(p, 4)); p += 4;
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
                Span<uint> palette = stackalloc uint[256];
                if (paletteBits == 0x10)
                {
                    for (int i = 0; i < 256; i++)
                    {
                        if (p + 2 > data.Length) break;
                        ushort v = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(p, 2)); p += 2;
                        byte a = (byte)((v & 0x8000) != 0 ? 0x80 : 0x00);
                        byte r = (byte)(((v >> 10) & 0x1F) * 255 / 31);
                        byte g = (byte)(((v >> 5) & 0x1F) * 255 / 31);
                        byte b = (byte)((v & 0x1F) * 255 / 31);
                        palette[i] = (uint)((a << 24) | (r << 16) | (g << 8) | b);
                    }
                }
                else
                {
                    for (int i = 0; i < 256; i++)
                    {
                        if (p + 4 > data.Length) break;
                        palette[i] = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(p, 4)); p += 4;
                    }
                }

                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        if (p >= data.Length) return new DecodedTexture(name, width, height, rgba) { AlphaDoubled = true };
                        byte idx = data[p++];
                        uint c = palette[idx];
                        int o = ((height - 1 - y) * width + x) * 4;
                        rgba[o] = (byte)((c >> 16) & 0xFF);
                        rgba[o + 1] = (byte)((c >> 8) & 0xFF);
                        rgba[o + 2] = (byte)(c & 0xFF);
                        rgba[o + 3] = (byte)Math.Min(255, ((c >> 24) & 0xFF) * 2);
                    }
                }
                return new DecodedTexture(name, width, height, rgba) { AlphaDoubled = true };
            }

            return new DecodedTexture(name, width, height, rgba);
        }

        /// <summary>Offset of a DXT texture's FourCC ("1TXD"/"3TXD"/"5TXD", reversed "DXTn").</summary>
        private const int DxtFourCcOffset = 0x39;

        /// <summary>Offset of the FourCC in the variant with one extra header word before it.</summary>
        private const int DxtFourCcFallbackOffset = 0x3D;

        /// <summary>
        /// Texture names already reported as skipped by <see cref="ReportSkipped"/>, so a stub that repeats on every
        /// character load is logged once per process (#310).
        /// </summary>
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> ReportedSkips = new(StringComparer.Ordinal);

        private static void ReportSkipped(string name, string reason)
        {
            if (ReportedSkips.TryAdd(name, 0))
                Gordian.Core.Diagnostics.GordianLog.Debug("RES", $"Texture '{name}' skipped: {reason}.");
        }

        /// <summary>
        /// Decodes a type 0xA1 (DXT) texture. Every read is bounded by the payload length: the FourCC is looked up at
        /// 0x39, then at 0x3D, only where the payload holds it, and a texture whose payload ends before its first DXT
        /// block is skipped (null) instead of throwing. Retail ships such a header-only stub: ROM/30/66 (Hume male main
        /// weapon model 221, file 8613) has a 64-byte <c>hf_sti1_</c> tim0 with a 64x64 8 bpp header, no FourCC and no
        /// pixels; the full <c>hf_sti1_</c> texture is in its neighbours ROM/30/63-69 (#310).
        /// <para>Layout referenced from xi-model-viewer (https://github.com/vekien/xi-model-viewer) and xi-tools.
        /// <b>Beyond xi-tools / xi-model-viewer:</b> the header-only stub and its handling.</para>
        /// </summary>
        private static DecodedTexture? DecodeDxtTexture(ReadOnlySpan<byte> data, string name)
        {
            if (data.Length < 0x40) return null;

            int width = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(0x15, 4));
            int height = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(0x19, 4));

            if (width <= 0 || height <= 0 || width > 4096 || height > 4096) return null;

            string fourCc = ReadFourCc(data, DxtFourCcOffset);
            int dxtStart = DxtFourCcOffset + 12;
            if (!IsDxtFourCc(fourCc))
            {
                fourCc = ReadFourCc(data, DxtFourCcFallbackOffset);
                if (!IsDxtFourCc(fourCc))
                {
                    if (data.Length < DxtFourCcFallbackOffset + 4)
                        ReportSkipped(name, $"header-only DXT payload ({data.Length} bytes, no FourCC or pixel data)");
                    return null;
                }
                dxtStart = DxtFourCcFallbackOffset + 12;
            }

            int blockSize = fourCc == "1TXD" ? 8 : 16;
            if (dxtStart + blockSize > data.Length)
            {
                ReportSkipped(name, $"{fourCc} payload of {data.Length} bytes ends before its first block at 0x{dxtStart:X}");
                return null;
            }

            var dxtPayload = data.Slice(dxtStart);
            byte[] rgba = new byte[width * height * 4];

            if (fourCc == "1TXD")
            {
                DecompressDxt1(dxtPayload, rgba, width, height);
            }
            else if (fourCc == "3TXD")
            {
                DecompressDxt3(dxtPayload, rgba, width, height);
            }
            else if (fourCc == "5TXD")
            {
                DecompressDxt5(dxtPayload, rgba, width, height);
            }
            else
            {
                return null;
            }

            return new DecodedTexture(name, width, height, rgba);
        }

        #region DXT Decompression

        public static void DecompressDxt1(ReadOnlySpan<byte> dxt, Span<byte> rgba, int width, int height)
        {
            int blockOffset = 0;
            Span<RgbaColor> colors = stackalloc RgbaColor[4];

            for (int by = 0; by < height; by += 4)
            {
                for (int bx = 0; bx < width; bx += 4)
                {
                    if (blockOffset + 8 > dxt.Length) return;

                    ushort c0 = BinaryPrimitives.ReadUInt16LittleEndian(dxt.Slice(blockOffset, 2));
                    ushort c1 = BinaryPrimitives.ReadUInt16LittleEndian(dxt.Slice(blockOffset + 2, 2));
                    uint lookup = BinaryPrimitives.ReadUInt32LittleEndian(dxt.Slice(blockOffset + 4, 4));
                    blockOffset += 8;

                    colors[0] = UnpackRgb565(c0);
                    colors[1] = UnpackRgb565(c1);

                    if (c0 > c1)
                    {
                        colors[2] = new RgbaColor(
                            (byte)((2 * colors[0].R + colors[1].R) / 3),
                            (byte)((2 * colors[0].G + colors[1].G) / 3),
                            (byte)((2 * colors[0].B + colors[1].B) / 3),
                            255);
                        colors[3] = new RgbaColor(
                            (byte)((colors[0].R + 2 * colors[1].R) / 3),
                            (byte)((colors[0].G + 2 * colors[1].G) / 3),
                            (byte)((colors[0].B + 2 * colors[1].B) / 3),
                            255);
                    }
                    else
                    {
                        colors[2] = new RgbaColor(
                            (byte)((colors[0].R + colors[1].R) / 2),
                            (byte)((colors[0].G + colors[1].G) / 2),
                            (byte)((colors[0].B + colors[1].B) / 2),
                            255);
                        colors[3] = new RgbaColor(0, 0, 0, 0);
                    }

                    for (int py = 0; py < 4; py++)
                    {
                        int y = by + py;
                        if (y >= height) continue;

                        for (int px = 0; px < 4; px++)
                        {
                            int x = bx + px;
                            if (x >= width) continue;

                            int code = (int)((lookup >> ((py * 4 + px) * 2)) & 3);
                            var col = colors[code];

                            int o = (y * width + x) * 4;
                            rgba[o] = col.R;
                            rgba[o + 1] = col.G;
                            rgba[o + 2] = col.B;
                            rgba[o + 3] = col.A;
                        }
                    }
                }
            }
        }

        public static void DecompressDxt3(ReadOnlySpan<byte> dxt, Span<byte> rgba, int width, int height)
        {
            int blockOffset = 0;
            Span<RgbaColor> colors = stackalloc RgbaColor[4];

            for (int by = 0; by < height; by += 4)
            {
                for (int bx = 0; bx < width; bx += 4)
                {
                    if (blockOffset + 16 > dxt.Length) return;

                    var alphaBlock = dxt.Slice(blockOffset, 8);
                    ushort c0 = BinaryPrimitives.ReadUInt16LittleEndian(dxt.Slice(blockOffset + 8, 2));
                    ushort c1 = BinaryPrimitives.ReadUInt16LittleEndian(dxt.Slice(blockOffset + 10, 2));
                    uint lookup = BinaryPrimitives.ReadUInt32LittleEndian(dxt.Slice(blockOffset + 12, 4));
                    blockOffset += 16;

                    colors[0] = UnpackRgb565(c0);
                    colors[1] = UnpackRgb565(c1);
                    colors[2] = new RgbaColor(
                        (byte)((2 * colors[0].R + colors[1].R) / 3),
                        (byte)((2 * colors[0].G + colors[1].G) / 3),
                        (byte)((2 * colors[0].B + colors[1].B) / 3),
                        255);
                    colors[3] = new RgbaColor(
                        (byte)((colors[0].R + 2 * colors[1].R) / 3),
                        (byte)((colors[0].G + 2 * colors[1].G) / 3),
                        (byte)((colors[0].B + 2 * colors[1].B) / 3),
                        255);

                    for (int py = 0; py < 4; py++)
                    {
                        int y = by + py;
                        if (y >= height) continue;

                        ushort alphaRow = BinaryPrimitives.ReadUInt16LittleEndian(alphaBlock.Slice(py * 2, 2));

                        for (int px = 0; px < 4; px++)
                        {
                            int x = bx + px;
                            if (x >= width) continue;

                            int code = (int)((lookup >> ((py * 4 + px) * 2)) & 3);
                            var col = colors[code];
                            byte a4 = (byte)((alphaRow >> (px * 4)) & 0x0F);
                            byte a = (byte)((a4 << 4) | a4);

                            int o = (y * width + x) * 4;
                            rgba[o] = col.R;
                            rgba[o + 1] = col.G;
                            rgba[o + 2] = col.B;
                            rgba[o + 3] = a;
                        }
                    }
                }
            }
        }

        public static void DecompressDxt5(ReadOnlySpan<byte> dxt, Span<byte> rgba, int width, int height)
        {
            int blockOffset = 0;
            Span<RgbaColor> colors = stackalloc RgbaColor[4];
            Span<byte> alphas = stackalloc byte[8];

            for (int by = 0; by < height; by += 4)
            {
                for (int bx = 0; bx < width; bx += 4)
                {
                    if (blockOffset + 16 > dxt.Length) return;

                    byte a0 = dxt[blockOffset];
                    byte a1 = dxt[blockOffset + 1];
                    ulong alphaLookup = BinaryPrimitives.ReadUInt64LittleEndian(dxt.Slice(blockOffset, 8)) >> 16;

                    ushort c0 = BinaryPrimitives.ReadUInt16LittleEndian(dxt.Slice(blockOffset + 8, 2));
                    ushort c1 = BinaryPrimitives.ReadUInt16LittleEndian(dxt.Slice(blockOffset + 10, 2));
                    uint lookup = BinaryPrimitives.ReadUInt32LittleEndian(dxt.Slice(blockOffset + 12, 4));
                    blockOffset += 16;

                    alphas[0] = a0;
                    alphas[1] = a1;
                    if (a0 > a1)
                    {
                        alphas[2] = (byte)((6 * a0 + 1 * a1) / 7);
                        alphas[3] = (byte)((5 * a0 + 2 * a1) / 7);
                        alphas[4] = (byte)((4 * a0 + 3 * a1) / 7);
                        alphas[5] = (byte)((3 * a0 + 4 * a1) / 7);
                        alphas[6] = (byte)((2 * a0 + 5 * a1) / 7);
                        alphas[7] = (byte)((1 * a0 + 6 * a1) / 7);
                    }
                    else
                    {
                        alphas[2] = (byte)((4 * a0 + 1 * a1) / 5);
                        alphas[3] = (byte)((3 * a0 + 2 * a1) / 5);
                        alphas[4] = (byte)((2 * a0 + 3 * a1) / 5);
                        alphas[5] = (byte)((1 * a0 + 4 * a1) / 5);
                        alphas[6] = 0;
                        alphas[7] = 255;
                    }

                    colors[0] = UnpackRgb565(c0);
                    colors[1] = UnpackRgb565(c1);
                    colors[2] = new RgbaColor(
                        (byte)((2 * colors[0].R + colors[1].R) / 3),
                        (byte)((2 * colors[0].G + colors[1].G) / 3),
                        (byte)((2 * colors[0].B + colors[1].B) / 3),
                        255);
                    colors[3] = new RgbaColor(
                        (byte)((colors[0].R + 2 * colors[1].R) / 3),
                        (byte)((colors[0].G + 2 * colors[1].G) / 3),
                        (byte)((colors[0].B + 2 * colors[1].B) / 3),
                        255);

                    for (int py = 0; py < 4; py++)
                    {
                        int y = by + py;
                        if (y >= height) continue;

                        for (int px = 0; px < 4; px++)
                        {
                            int x = bx + px;
                            if (x >= width) continue;

                            int pixelIndex = py * 4 + px;
                            int colCode = (int)((lookup >> (pixelIndex * 2)) & 3);
                            int alphaCode = (int)((alphaLookup >> (pixelIndex * 3)) & 7);

                            var col = colors[colCode];
                            byte a = alphas[alphaCode];

                            int o = (y * width + x) * 4;
                            rgba[o] = col.R;
                            rgba[o + 1] = col.G;
                            rgba[o + 2] = col.B;
                            rgba[o + 3] = a;
                        }
                    }
                }
            }
        }

        private static RgbaColor UnpackRgb565(ushort v)
        {
            byte r = (byte)(((v >> 11) & 0x1F) * 255 / 31);
            byte g = (byte)(((v >> 5) & 0x3F) * 255 / 63);
            byte b = (byte)((v & 0x1F) * 255 / 31);
            return new RgbaColor(r, g, b, 255);
        }

        private readonly record struct RgbaColor(byte R, byte G, byte B, byte A);

        #endregion

        private static bool IsDxtFourCc(string fourCc) => fourCc is "1TXD" or "3TXD" or "5TXD";

        /// <summary>The 4-byte FourCC at <paramref name="offset"/>, or empty when the payload ends before it.</summary>
        private static string ReadFourCc(ReadOnlySpan<byte> data, int offset) =>
            offset + 4 <= data.Length ? ReadCString(data.Slice(offset, 4)) : string.Empty;

        private static string ReadCString(ReadOnlySpan<byte> span)
        {
            int end = 0;
            while (end < span.Length && span[end] != 0) end++;
            return Encoding.ASCII.GetString(span.Slice(0, end)).Trim();
        }
    }
}
