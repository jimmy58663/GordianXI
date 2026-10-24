// tests/Gordian.App.Tests/Graphics/ButtonGlyphPreviewTests.cs
using System;
using System.IO;
using System.Threading;
using Gordian.App.Graphics;
using Gordian.Engine.Ui;
using Xunit;

namespace Gordian.App.Tests.Graphics
{
    /// <summary>The /buttonpreview picker (#360): the PNG reader, the command's state and, when the packs are on this machine, the sheet drawn on D3D11.</summary>
    public class ButtonGlyphPreviewTests
    {
        private static string? PacksFolder()
        {
            string? env = Environment.GetEnvironmentVariable("GORDIAN_BUTTON_PACKS");
            if (!string.IsNullOrEmpty(env) && Directory.Exists(env)) return env;
            string guess = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "OneDrive", "Documents", "Antigravity", "GordianXI_Project");
            return Directory.Exists(guess) ? guess : null;
        }

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

        [Fact]
        public void ButtonPreviewCommand_TracksFolderAndZoom()
        {
            var state = new ButtonPreviewState();
            Assert.False(state.IsActive);
            Assert.Contains("not found", state.Apply(Path.Combine(Path.GetTempPath(), "no-such-gordian-folder")));
            string folder = Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar);
            Assert.Contains("zoom 3", state.Apply($"\"{folder}\" 3"));
            Assert.Equal((folder, 3), (state.Folder, state.Zoom));
            state.Apply("off");
            Assert.False(state.IsActive);
        }

        [Fact]
        public void RendersThePickerSheet_WhenThePacksArePresent()
        {
            string? packs = PacksFolder();
            if (packs == null || ButtonGlyphPreview.Discover(packs).Count == 0) return;
            using var screen = StockUiOffscreen.TryCreate();
            if (screen == null) return;
            var state = new ButtonPreviewState();
            state.Apply($"\"{packs}\" 2");
            var preview = new ButtonGlyphPreview();
            byte[] shot = Array.Empty<byte>();
            for (int i = 0; i < 400; i++)
            {
                shot = screen.Render(r => preview.Draw(r, screen.Font, state, 1, screen.Width, screen.Height), "gpu_button_preview.png");
                if (i > 3 && screen.HasLightPixel(shot, 120, 600, 64 + 16)) break; // the first glyph row (zoom 2: 32 px tall)
                Thread.Sleep(50);
            }
            Assert.True(screen.HasLightPixel(shot, 120, 600, 64 + 16), "no glyphs drawn in the first row");
            Assert.True(screen.HasLightPixel(shot, 16, 600, 36), "no title line");
        }
    }
}
