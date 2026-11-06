// src/Gordian.App/Graphics/PngEncoder.cs
using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace Gordian.App.Graphics
{
    /// <summary>
    /// Writes 8-bit RGB PNG files from 32-bit pixels, dropping alpha because a rendered frame is opaque. Each row uses
    /// the Sub filter, which suits the smooth gradients of a game frame. Format per the PNG specification (W3C).
    /// </summary>
    internal static class PngEncoder
    {
        private static readonly byte[] Signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        private static readonly uint[] CrcTable = BuildCrcTable();

        /// <summary>Encodes <paramref name="pixels"/>, tightly packed, top row first.</summary>
        /// <param name="output">The stream to write the file to.</param>
        /// <param name="pixels">Four bytes a pixel.</param>
        /// <param name="width">The width in pixels.</param>
        /// <param name="height">The height in pixels.</param>
        /// <param name="bgra">The pixels are in B, G, R, A order rather than R, G, B, A.</param>
        public static void Write(Stream output, ReadOnlySpan<byte> pixels, int width, int height, bool bgra)
        {
            if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width));
            if (pixels.Length < width * height * 4) throw new ArgumentException("Too few pixels for the size.", nameof(pixels));

            output.Write(Signature);

            Span<byte> header = stackalloc byte[13];
            header.Clear();
            BinaryPrimitives.WriteInt32BigEndian(header, width);
            BinaryPrimitives.WriteInt32BigEndian(header[4..], height);
            header[8] = 8;  // bit depth
            header[9] = 2;  // colour type: RGB
            WriteChunk(output, "IHDR", header);

            using (var compressed = new MemoryStream())
            {
                using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
                {
                    byte[] row = new byte[1 + width * 3];
                    row[0] = 1;  // filter type: Sub
                    int red = bgra ? 2 : 0;
                    int blue = bgra ? 0 : 2;
                    for (int y = 0; y < height; y++)
                    {
                        var source = pixels.Slice(y * width * 4, width * 4);
                        byte previousR = 0, previousG = 0, previousB = 0;
                        for (int x = 0; x < width; x++)
                        {
                            byte r = source[x * 4 + red], g = source[x * 4 + 1], b = source[x * 4 + blue];
                            int at = 1 + x * 3;
                            row[at] = (byte)(r - previousR);
                            row[at + 1] = (byte)(g - previousG);
                            row[at + 2] = (byte)(b - previousB);
                            previousR = r;
                            previousG = g;
                            previousB = b;
                        }
                        zlib.Write(row);
                    }
                }
                WriteChunk(output, "IDAT", compressed.GetBuffer().AsSpan(0, (int)compressed.Length));
            }

            WriteChunk(output, "IEND", []);
        }

        private static void WriteChunk(Stream output, string type, ReadOnlySpan<byte> data)
        {
            Span<byte> field = stackalloc byte[4];
            BinaryPrimitives.WriteInt32BigEndian(field, data.Length);
            output.Write(field);

            Encoding.ASCII.GetBytes(type, field);
            output.Write(field);
            output.Write(data);

            uint crc = Update(0xFFFFFFFFu, field);
            crc = Update(crc, data) ^ 0xFFFFFFFFu;
            BinaryPrimitives.WriteUInt32BigEndian(field, crc);
            output.Write(field);
        }

        private static uint Update(uint crc, ReadOnlySpan<byte> data)
        {
            foreach (byte value in data) crc = CrcTable[(crc ^ value) & 0xFF] ^ (crc >> 8);
            return crc;
        }

        private static uint[] BuildCrcTable()
        {
            var table = new uint[256];
            for (uint n = 0; n < 256; n++)
            {
                uint c = n;
                for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                table[n] = c;
            }
            return table;
        }
    }
}
