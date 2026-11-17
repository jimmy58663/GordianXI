// tests/Gordian.App.Tests/Graphics/PngRgbaTests.cs
using System;
using System.IO;
using Gordian.App.Graphics;
using Xunit;

namespace Gordian.App.Tests.Graphics
{
    /// <summary>The small PNG reader that loads the shipped menu hint button glyphs (#360).</summary>
    public class PngRgbaTests
    {
        [Fact]
        public void PngRgba_ReadsAFilterEncodedRgbaPng_AndDownsamplesByAnAlphaWeightedBox()
        {
            // A 2 x 2 RGBA PNG built by hand (filters 0 and 1), checked pixel for pixel.
            byte[] raw =
            {
                0, 255, 0, 0, 255, 0, 255, 0, 255,          // row 0, no filter: red, green
                1, 0, 0, 255, 255, 0, 0, 0, 0,              // row 1, Sub: blue, then blue + (0,0,0,0) = blue
            };
            using var compressed = new MemoryStream();
            using (var z = new System.IO.Compression.ZLibStream(compressed, System.IO.Compression.CompressionLevel.Optimal, leaveOpen: true)) z.Write(raw);
            using var png = new MemoryStream();
            png.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
            void Chunk(string type, byte[] data)
            {
                var length = new byte[4];
                System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
                png.Write(length);
                png.Write(System.Text.Encoding.ASCII.GetBytes(type));
                png.Write(data);
                png.Write(new byte[4]); // the CRC is not checked by the reader
            }
            Chunk("IHDR", new byte[] { 0, 0, 0, 2, 0, 0, 0, 2, 8, 6, 0, 0, 0 });
            Chunk("IDAT", compressed.ToArray());
            Chunk("IEND", Array.Empty<byte>());

            var decoded = PngRgba.Decode(png.ToArray());
            Assert.NotNull(decoded);
            var (w, h, rgba) = decoded!.Value;
            Assert.Equal((2, 2), (w, h));
            Assert.Equal(new byte[] { 255, 0, 0, 255 }, rgba[..4]);
            Assert.Equal(new byte[] { 0, 255, 0, 255 }, rgba[4..8]);
            Assert.Equal(new byte[] { 0, 0, 255, 255 }, rgba[8..12]);

            // A fully transparent pixel does not drag the average colour down: (red, clear) averages to red at half alpha.
            byte[] box = PngRgba.Downsample(new byte[] { 255, 0, 0, 255, 0, 0, 0, 0, 255, 0, 0, 255, 0, 0, 0, 0 }, 2, 2, 2);
            Assert.Equal(new byte[] { 255, 0, 0, 127 }, box);
        }
    }
}
