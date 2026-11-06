// tests/Gordian.App.Tests/Graphics/ScreenshotFileTests.cs
using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using System.Text;
using Gordian.App.Graphics;
using Xunit;

namespace Gordian.App.Tests.Graphics
{
    public class ScreenshotFileTests
    {
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void PngRoundTripsPixelsInEitherChannelOrder(bool bgra)
        {
            // Two rows of (R, G, B): red then grey, then blue then white.
            byte[][] rgb = [[200, 10, 20], [90, 90, 90], [5, 15, 250], [255, 255, 255]];
            byte[] pixels = new byte[4 * 4];
            for (int i = 0; i < 4; i++)
            {
                pixels[i * 4] = rgb[i][bgra ? 2 : 0];
                pixels[i * 4 + 1] = rgb[i][1];
                pixels[i * 4 + 2] = rgb[i][bgra ? 0 : 2];
                pixels[i * 4 + 3] = 0;  // Alpha is dropped: a frame's alpha is not meaningful.
            }

            using var file = new MemoryStream();
            PngEncoder.Write(file, pixels, 2, 2, bgra);
            byte[] png = file.ToArray();

            Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, png[..8]);
            Assert.Equal("IHDR", Encoding.ASCII.GetString(png, 12, 4));
            Assert.Equal(2, BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16)));
            Assert.Equal(2, BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20)));
            Assert.Equal(2, png[25]);

            int idat = 8 + 25;
            Assert.Equal("IDAT", Encoding.ASCII.GetString(png, idat + 4, 4));
            int length = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(idat));
            using var zlib = new ZLibStream(new MemoryStream(png, idat + 8, length), CompressionMode.Decompress);
            byte[] rows = new byte[2 * (1 + 2 * 3)];
            zlib.ReadExactly(rows);

            for (int y = 0; y < 2; y++)
            {
                int row = y * 7;
                Assert.Equal(1, rows[row]);
                for (int c = 0; c < 3; c++)
                {
                    byte first = rows[row + 1 + c];
                    byte second = (byte)(rows[row + 4 + c] + first);
                    Assert.Equal(rgb[y * 2][c], first);
                    Assert.Equal(rgb[y * 2 + 1][c], second);
                }
            }
            Assert.Equal("IEND", Encoding.ASCII.GetString(png, png.Length - 8, 4));
        }

        [Fact]
        public void TimestampNameMatchesWindower()
        {
            string directory = NewDirectory();

            string path = ScreenshotFiles.ResolvePath(directory, null, new DateTime(2026, 11, 6, 15, 4, 9));

            Assert.Equal(Path.Combine(directory, "img_20261106_150409.png"), path);
        }

        [Fact]
        public void ExistingFileIsKept()
        {
            string directory = NewDirectory();
            File.WriteAllText(Path.Combine(directory, "shot.png"), "first");

            string path = ScreenshotFiles.ResolvePath(directory, "shot", DateTime.Now);

            Assert.Equal(Path.Combine(directory, "shot-2.png"), path);
        }

        private static string NewDirectory()
        {
            string directory = Path.Combine(Path.GetTempPath(), "GordianScreenshot_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            return directory;
        }
    }
}
