// src/Gordian.App/Graphics/PngRgba.cs
using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;

namespace Gordian.App.Graphics
{
    /// <summary>
    /// A small PNG reader for the 8-bit, non-interlaced files the button glyph packs ship (grey, grey + alpha, RGB, RGBA and
    /// paletted), written against the PNG specification (https://www.w3.org/TR/png/) so no image library is needed for them.
    /// Returns straight (not premultiplied) RGBA, or null for anything it does not read.
    /// </summary>
    public static class PngRgba
    {
        private static readonly byte[] Signature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

        public static (int Width, int Height, byte[] Rgba)? Decode(ReadOnlySpan<byte> png)
        {
            if (png.Length < 33 || !png[..8].SequenceEqual(Signature)) return null;
            int width = 0, height = 0, depth = 0, colorType = 0, interlace = 0;
            byte[]? palette = null, paletteAlpha = null;
            using var idat = new MemoryStream();
            int pos = 8;
            while (pos + 12 <= png.Length)
            {
                int length = BinaryPrimitives.ReadInt32BigEndian(png[pos..]);
                var type = png.Slice(pos + 4, 4);
                if (length < 0 || pos + 12 + length > png.Length) return null;
                var data = png.Slice(pos + 8, length);
                if (type.SequenceEqual("IHDR"u8) && length >= 13)
                {
                    width = BinaryPrimitives.ReadInt32BigEndian(data);
                    height = BinaryPrimitives.ReadInt32BigEndian(data[4..]);
                    depth = data[8];
                    colorType = data[9];
                    interlace = data[12];
                }
                else if (type.SequenceEqual("PLTE"u8)) palette = data.ToArray();
                else if (type.SequenceEqual("tRNS"u8)) paletteAlpha = data.ToArray();
                else if (type.SequenceEqual("IDAT"u8)) idat.Write(data);
                else if (type.SequenceEqual("IEND"u8)) break;
                pos += 12 + length;
            }
            if (width <= 0 || height <= 0 || width > 8192 || height > 8192 || depth != 8 || interlace != 0) return null;
            int channels = colorType switch { 0 => 1, 2 => 3, 3 => 1, 4 => 2, 6 => 4, _ => 0 };
            if (channels == 0 || (colorType == 3 && palette == null)) return null;

            int stride = width * channels;
            var raw = new byte[(stride + 1) * height];
            idat.Position = 0;
            try
            {
                using var z = new ZLibStream(idat, CompressionMode.Decompress);
                int read = 0;
                while (read < raw.Length)
                {
                    int n = z.Read(raw, read, raw.Length - read);
                    if (n <= 0) break;
                    read += n;
                }
                if (read < raw.Length) return null;
            }
            catch (InvalidDataException)
            {
                return null;
            }

            // Undo the per-row filters (PNG spec section 9).
            var pixels = new byte[stride * height];
            for (int y = 0; y < height; y++)
            {
                int filter = raw[y * (stride + 1)];
                int src = y * (stride + 1) + 1, dst = y * stride;
                for (int i = 0; i < stride; i++)
                {
                    int a = i >= channels ? pixels[dst + i - channels] : 0;
                    int b = y > 0 ? pixels[dst - stride + i] : 0;
                    int c = i >= channels && y > 0 ? pixels[dst - stride + i - channels] : 0;
                    int x = raw[src + i];
                    pixels[dst + i] = (byte)(filter switch
                    {
                        0 => x,
                        1 => x + a,
                        2 => x + b,
                        3 => x + ((a + b) >> 1),
                        4 => x + Paeth(a, b, c),
                        _ => x,
                    });
                }
            }

            var rgba = new byte[width * height * 4];
            for (int i = 0; i < width * height; i++)
            {
                int s = i * channels, d = i * 4;
                switch (colorType)
                {
                    case 0: rgba[d] = rgba[d + 1] = rgba[d + 2] = pixels[s]; rgba[d + 3] = 255; break;
                    case 2: rgba[d] = pixels[s]; rgba[d + 1] = pixels[s + 1]; rgba[d + 2] = pixels[s + 2]; rgba[d + 3] = 255; break;
                    case 3:
                        int index = pixels[s];
                        rgba[d] = index * 3 + 2 < palette!.Length ? palette[index * 3] : (byte)0;
                        rgba[d + 1] = index * 3 + 2 < palette.Length ? palette[index * 3 + 1] : (byte)0;
                        rgba[d + 2] = index * 3 + 2 < palette.Length ? palette[index * 3 + 2] : (byte)0;
                        rgba[d + 3] = paletteAlpha != null && index < paletteAlpha.Length ? paletteAlpha[index] : (byte)255;
                        break;
                    case 4: rgba[d] = rgba[d + 1] = rgba[d + 2] = pixels[s]; rgba[d + 3] = pixels[s + 1]; break;
                    default: Buffer.BlockCopy(pixels, s, rgba, d, 4); break;
                }
            }
            return (width, height, rgba);
        }

        private static int Paeth(int a, int b, int c)
        {
            int p = a + b - c, pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
            return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
        }

        /// <summary>
        /// Box-filters straight RGBA down by a whole <paramref name="factor"/> (alpha-weighted, so a transparent edge does not
        /// darken the glyph's rim): the pre-sized copies the button hints draw from, in place of mip generation or an SVG library.
        /// </summary>
        public static byte[] Downsample(byte[] rgba, int width, int height, int factor)
        {
            int w = width / factor, h = height / factor;
            var result = new byte[w * h * 4];
            int count = factor * factor;
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    long r = 0, g = 0, b = 0, a = 0;
                    for (int dy = 0; dy < factor; dy++)
                    {
                        for (int dx = 0; dx < factor; dx++)
                        {
                            int s = ((y * factor + dy) * width + x * factor + dx) * 4;
                            int alpha = rgba[s + 3];
                            r += rgba[s] * alpha;
                            g += rgba[s + 1] * alpha;
                            b += rgba[s + 2] * alpha;
                            a += alpha;
                        }
                    }
                    int d = (y * w + x) * 4;
                    if (a > 0)
                    {
                        result[d] = (byte)(r / a);
                        result[d + 1] = (byte)(g / a);
                        result[d + 2] = (byte)(b / a);
                    }
                    result[d + 3] = (byte)(a / count);
                }
            }
            return result;
        }
    }
}
