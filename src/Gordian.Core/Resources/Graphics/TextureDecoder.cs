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
        public int Width { get; }
        public int Height { get; }
        public byte[] RgbaPixels { get; }

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
            if (data.Length < 0x30) return null;

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
                        if (p >= data.Length) return new DecodedTexture(name, width, height, rgba);
                        byte idx = data[p++];
                        uint c = palette[idx];
                        int o = ((height - 1 - y) * width + x) * 4;
                        rgba[o] = (byte)((c >> 16) & 0xFF);
                        rgba[o + 1] = (byte)((c >> 8) & 0xFF);
                        rgba[o + 2] = (byte)(c & 0xFF);
                        rgba[o + 3] = (byte)Math.Min(255, ((c >> 24) & 0xFF) * 2);
                    }
                }
            }

            return new DecodedTexture(name, width, height, rgba);
        }

        private static DecodedTexture? DecodeDxtTexture(ReadOnlySpan<byte> data, string name)
        {
            if (data.Length < 0x40) return null;

            int width = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(0x15, 4));
            int height = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(0x19, 4));

            if (width <= 0 || height <= 0 || width > 4096 || height > 4096) return null;

            string fourCc = ReadCString(data.Slice(0x39, 4));
            int dxtStart = 0x45;
            if (fourCc != "1TXD" && fourCc != "3TXD" && fourCc != "5TXD")
            {
                fourCc = ReadCString(data.Slice(0x3D, 4));
                if (fourCc == "1TXD" || fourCc == "3TXD" || fourCc == "5TXD")
                {
                    dxtStart = 0x49;
                }
                else
                {
                    return null;
                }
            }

            if (dxtStart >= data.Length) return null;

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

        private static string ReadCString(ReadOnlySpan<byte> span)
        {
            int end = 0;
            while (end < span.Length && span[end] != 0) end++;
            return Encoding.ASCII.GetString(span.Slice(0, end)).Trim();
        }
    }
}
