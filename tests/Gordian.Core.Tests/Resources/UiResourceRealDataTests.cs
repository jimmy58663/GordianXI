// tests/Gordian.Core.Tests/Resources/UiResourceRealDataTests.cs
using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Gordian.Core.Resources;
using Gordian.Core.Resources.Ui;
using Xunit;
using Xunit.Abstractions;

namespace Gordian.Core.Tests.Resources
{
    /// <summary>
    /// Checks the stock UI decoders against a retail install, when one is present. Set GORDIAN_UI_DUMP to a
    /// directory to also write software-composited PNGs of the decoded menus for visual inspection.
    /// </summary>
    public class UiResourceRealDataTests
    {
        private const string GameDirectory = @"G:\Program Files (x86)\PlayOnline\SquareEnix\FINAL FANTASY XI";
        private readonly ITestOutputHelper _output;

        public UiResourceRealDataTests(ITestOutputHelper output) => _output = output;

        private static UiResourceLibrary? LoadLibrary()
        {
            if (!Directory.Exists(GameDirectory)) return null;
            var rm = new ResourceManager(GameDirectory);
            rm.InitializeFileTable();
            return UiResourceLibrary.Load(rm);
        }

        [Fact]
        public void RetailMenus_DecodeWithAuthoredLayout()
        {
            var ui = LoadLibrary();
            if (ui == null) return;
            _output.WriteLine($"{ui.Menus.Count} menus, {ui.Groups.Count} groups, {ui.TextureNames.Count()} textures");

            Assert.True(ui.TryGetMenu("logwindo", out var log));
            Assert.Equal((16, 298, 366, 134), (log.Frame.X, log.Frame.Y, log.Frame.Width, log.Frame.Height));
            Assert.Equal(UiAnchor.BottomLeft, log.Frame.Anchor);

            Assert.True(ui.TryGetMenu("partywin", out var party));
            Assert.Equal(UiAnchor.BottomRight, party.Frame.Anchor);
            Assert.Equal(6, party.Buttons.Count);

            Assert.True(ui.TryGetMenu("menu    menuwind", out var main));
            Assert.Equal(UiAnchor.TopRight, main.Frame.Anchor);
            Assert.Equal(15, main.Buttons.Count);
            // Every navigation link of the main menu names one of its own buttons.
            foreach (var button in main.Buttons)
            {
                foreach (var link in new[] { button.NavUp, button.NavDown, button.NavLeft, button.NavRight })
                {
                    Assert.True(link == -1 || main.FindButton(link) != null, $"button {button.ButtonId} links to {link}");
                }
            }

            Assert.True(ui.TryGetMenu("inventor", out var inventory));
            Assert.Equal(UiAnchor.TopLeft, inventory.Frame.Anchor);
        }

        [Fact]
        public void RetailShapesAndParts_ResolveToGroupsAndTextures()
        {
            var ui = LoadLibrary();
            if (ui == null) return;

            int shapes = 0, unresolvedShapes = 0;
            foreach (var menu in ui.Menus.Values)
            {
                foreach (var shape in menu.Buttons.SelectMany(b => b.Shapes).Concat(menu.Frame.Shapes))
                {
                    shapes++;
                    if (!ui.TryGetImage(shape, out _))
                    {
                        unresolvedShapes++;
                        if (unresolvedShapes <= 20) _output.WriteLine($"unresolved shape {menu.Name}: {shape.GroupId}#{shape.ImageIndex}");
                    }
                }
            }
            _output.WriteLine($"{shapes} shape references, {unresolvedShapes} unresolved");
            Assert.True(unresolvedShapes * 50 < shapes, $"{unresolvedShapes} of {shapes} shape references unresolved");

            var missingTextures = ui.Groups.Values
                .SelectMany(g => g.Images).SelectMany(i => i.Parts)
                .Select(p => UiResourceLibrary.TrimResourceName(p.TextureName))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(name => !ui.TryGetTexture(name, out _))
                .ToList();
            _output.WriteLine($"missing textures: {string.Join(", ", missingTextures)}");
            Assert.Empty(missingTextures);

            Assert.True(ui.TryGetTexture("yubi", out var finger));
            Assert.Equal((32, 32), (finger.Width, finger.Height));
            Assert.True(ui.TryGetGroup("frames", out var frames)); // the English group is "framesus"
            Assert.Equal("framesus", frames.Name);
        }

        [Fact]
        public void DumpMenuComposites()
        {
            string? dumpDir = Environment.GetEnvironmentVariable("GORDIAN_UI_DUMP");
            if (string.IsNullOrEmpty(dumpDir)) return;
            var ui = LoadLibrary();
            if (ui == null) return;
            Directory.CreateDirectory(dumpDir);

            // A mock HUD: every menu we will draw first, at its authored 512 x 448 layout position.
            var canvas = new SoftwareCanvas(UiResourceLibrary.LayoutWidth, UiResourceLibrary.LayoutHeight);
            foreach (string name in new[] { "logwindo", "partywin", "targetwi", "menuwind" })
            {
                if (ui.TryGetMenu(name, out var menu)) canvas.DrawMenu(ui, menu, menu.Frame.X, menu.Frame.Y);
            }
            canvas.Save(Path.Combine(dumpDir, "hud_layout.png"));

            foreach (string groupName in (Environment.GetEnvironmentVariable("GORDIAN_UI_DUMP_GROUPS") ?? "yubi,fontshp,framesus").Split(','))
            {
                if (!ui.TryGetGroup(groupName, out var group)) continue;
                var sheet = new SoftwareCanvas(512, 512);
                int x = 8, y = 8, rowHeight = 0;
                for (int i = 0; i < group.Images.Count && y < 480; i++)
                {
                    var bounds = SoftwareCanvas.Bounds(group.Images[i]);
                    int w = Math.Max(4, bounds.MaxX - bounds.MinX), h = Math.Max(4, bounds.MaxY - bounds.MinY);
                    if (x + w > 504) { x = 8; y += rowHeight + 6; rowHeight = 0; }
                    sheet.DrawImage(ui, group.Images[i], x - bounds.MinX, y - bounds.MinY);
                    x += w + 6;
                    rowHeight = Math.Max(rowHeight, h);
                }
                sheet.Save(Path.Combine(dumpDir, $"group_{group.Name}.png"));
            }
        }

        /// <summary>
        /// Minimal software rasterizer for axis-aligned UI parts (nearest sampling, half-scale colour modulation,
        /// alpha blending) used only to eyeball decoded data.
        /// </summary>
        private sealed class SoftwareCanvas
        {
            private readonly int _width, _height;
            private readonly byte[] _rgba;

            public SoftwareCanvas(int width, int height)
            {
                _width = width;
                _height = height;
                _rgba = new byte[width * height * 4];
                for (int i = 0; i < _rgba.Length; i += 4) { _rgba[i] = 40; _rgba[i + 1] = 60; _rgba[i + 2] = 40; _rgba[i + 3] = 255; }
            }

            public static (int MinX, int MinY, int MaxX, int MaxY) Bounds(UiImage image)
            {
                if (image.Parts.Count == 0) return (0, 0, 0, 0);
                return (image.Parts.Min(p => (int)p.TopLeft.X), image.Parts.Min(p => (int)p.TopLeft.Y),
                        image.Parts.Max(p => (int)p.BottomRight.X), image.Parts.Max(p => (int)p.BottomRight.Y));
            }

            public void DrawMenu(UiResourceLibrary ui, UiMenuDefinition menu, int originX, int originY)
            {
                foreach (var shape in menu.Frame.Shapes)
                {
                    if (shape.Kind == 0 && ui.TryGetImage(shape, out var image)) DrawImage(ui, image, originX, originY);
                }
                foreach (var button in menu.Buttons)
                {
                    foreach (var shape in button.Shapes)
                    {
                        if (shape.Kind == 0 && ui.TryGetImage(shape, out var image)) DrawImage(ui, image, originX + button.X, originY + button.Y);
                    }
                }
            }

            public void DrawImage(UiResourceLibrary ui, UiImage image, int originX, int originY)
            {
                foreach (var part in image.Parts)
                {
                    if (!ui.TryGetTexture(part.TextureName, out var texture)) continue;
                    int x0 = originX + part.TopLeft.X, y0 = originY + part.TopLeft.Y;
                    int w = part.BottomRight.X - part.TopLeft.X, h = part.BottomRight.Y - part.TopLeft.Y;
                    if (w <= 0 || h <= 0) continue;
                    var c = part.ColorTopLeft;
                    for (int dy = 0; dy < h; dy++)
                    {
                        for (int dx = 0; dx < w; dx++)
                        {
                            int px = x0 + dx, py = y0 + dy;
                            if (px < 0 || py < 0 || px >= _width || py >= _height) continue;
                            // Source rectangles may run past the texture edge: UI textures wrap (window backgrounds tile).
                            int sx = (part.SourceX + dx * part.SourceWidth / w) % texture.Width;
                            int sy = (part.SourceY + dy * part.SourceHeight / h) % texture.Height;
                            int t = (sy * texture.Width + sx) * 4;
                            float r = Math.Min(255, texture.RgbaPixels[t] * c.R / 128f);
                            float g = Math.Min(255, texture.RgbaPixels[t + 1] * c.G / 128f);
                            float b = Math.Min(255, texture.RgbaPixels[t + 2] * c.B / 128f);
                            float a = Math.Min(1f, texture.RgbaPixels[t + 3] * c.A / (128f * 128f));
                            int d = (py * _width + px) * 4;
                            _rgba[d] = (byte)(r * a + _rgba[d] * (1 - a));
                            _rgba[d + 1] = (byte)(g * a + _rgba[d + 1] * (1 - a));
                            _rgba[d + 2] = (byte)(b * a + _rgba[d + 2] * (1 - a));
                        }
                    }
                }
            }

            public void Save(string path)
            {
                using var raw = new MemoryStream();
                for (int y = 0; y < _height; y++)
                {
                    raw.WriteByte(0);
                    raw.Write(_rgba, y * _width * 4, _width * 4);
                }
                using var compressed = new MemoryStream();
                using (var z = new ZLibStream(compressed, CompressionLevel.Fastest, leaveOpen: true)) raw.WriteTo(z);

                using var file = File.Create(path);
                file.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
                var ihdr = new byte[13];
                System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(ihdr, _width);
                System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(4), _height);
                ihdr[8] = 8; ihdr[9] = 6;
                WriteChunk(file, "IHDR", ihdr);
                WriteChunk(file, "IDAT", compressed.ToArray());
                WriteChunk(file, "IEND", Array.Empty<byte>());
            }

            private static void WriteChunk(Stream s, string type, byte[] data)
            {
                var header = new byte[8];
                System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(header, data.Length);
                System.Text.Encoding.ASCII.GetBytes(type).CopyTo(header, 4);
                s.Write(header);
                s.Write(data);
                uint crc = 0xFFFFFFFF;
                foreach (byte b in header.AsSpan(4)) crc = Crc(crc, b);
                foreach (byte b in data) crc = Crc(crc, b);
                var tail = new byte[4];
                System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(tail, crc ^ 0xFFFFFFFF);
                s.Write(tail);
            }

            private static uint Crc(uint crc, byte b)
            {
                crc ^= b;
                for (int k = 0; k < 8; k++) crc = (crc & 1) != 0 ? 0xEDB88320 ^ (crc >> 1) : crc >> 1;
                return crc;
            }
        }
    }
}
