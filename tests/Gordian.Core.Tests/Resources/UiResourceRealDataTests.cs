// tests/Gordian.Core.Tests/Resources/UiResourceRealDataTests.cs
using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Gordian.Core.Resources;
using Gordian.Core.Resources.Ui;
using Xunit;

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

        private static UiResourceLibrary? LoadLobbyLibrary()
        {
            if (!Directory.Exists(GameDirectory)) return null;
            var rm = new ResourceManager(GameDirectory);
            rm.InitializeFileTable();
            return UiResourceLibrary.LoadLobby(rm);
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
            // GORDIAN_UI_DUMP_LOBBY=1 loads the lobby DAT (ROM/119/50) ahead of the menu DATs, for the lobby menus.
            var ui = Environment.GetEnvironmentVariable("GORDIAN_UI_DUMP_LOBBY") == "1" ? LoadLobbyLibrary() : LoadLibrary();
            if (ui == null) return;
            Directory.CreateDirectory(dumpDir);

            // A mock HUD: every menu we will draw first, at its authored 512 x 448 layout position.
            var canvas = new SoftwareCanvas(UiResourceLibrary.LayoutWidth, UiResourceLibrary.LayoutHeight);
            foreach (string name in new[] { "logwindo", "partywin", "targetwi", "menuwind" })
            {
                if (ui.TryGetMenu(name, out var menu)) canvas.DrawMenu(ui, menu, menu.Frame.X, menu.Frame.Y);
            }
            canvas.Save(Path.Combine(dumpDir, "hud_layout.png"));

            // GORDIAN_UI_DUMP_MENUS: comma-separated menu names (a trailing '*' matches a prefix, e.g. "conf*"). Each menu is
            // composited at the origin into menu_<name>.png, and menu_<name>.txt lists its buttons (position, navigation
            // links, label sprites) so a page's options can be read before they are wired up.
            string menuSpecs = Environment.GetEnvironmentVariable("GORDIAN_UI_DUMP_MENUS") ?? string.Empty;
            foreach (string spec in menuSpecs.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                foreach (var menu in ui.Menus.Values.OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase))
                {
                    bool matches = spec.EndsWith('*')
                        ? menu.Name.StartsWith(spec[..^1], StringComparison.OrdinalIgnoreCase)
                        : menu.Name.Equals(spec, StringComparison.OrdinalIgnoreCase);
                    if (!matches) continue;
                    var page = new SoftwareCanvas(Math.Max(16, menu.Frame.Width + 48), Math.Max(16, menu.Frame.Height + 16));
                    page.DrawMenu(ui, menu, 40, 8);
                    page.Save(Path.Combine(dumpDir, $"menu_{menu.Name}.png"));

                    var text = new System.Text.StringBuilder();
                    var frame = menu.Frame;
                    text.AppendLine($"{menu.Category}/{menu.Name} type={menu.MenuType} frame=({frame.X},{frame.Y} {frame.Width}x{frame.Height}) anchor={frame.Anchor} cursor=({frame.CursorOffsetX},{frame.CursorOffsetY}) help={frame.HelpTextId} title={frame.TitleTextId}");
                    foreach (var shape in frame.Shapes) text.AppendLine($"  frame shape kind={shape.Kind} {shape.GroupName}#{shape.ImageIndex}");
                    foreach (var button in menu.Buttons.OrderBy(b => b.Y).ThenBy(b => b.X))
                    {
                        text.Append($"  button {button.ButtonId,3} at ({button.X,4},{button.Y,4}) {button.Width,3}x{button.Height,-3} nav U{button.NavUp} D{button.NavDown} L{button.NavLeft} R{button.NavRight} help={button.HelpTextId} title={button.TitleTextId}");
                        foreach (var shape in button.Shapes) text.Append($" [k{shape.Kind} {shape.GroupName}#{shape.ImageIndex}]");
                        text.AppendLine();
                    }
                    // The parts of every referenced image (once each): texture, destination corners and colours.
                    var listed = new System.Collections.Generic.HashSet<string>();
                    foreach (var shape in frame.Shapes.Concat(menu.Buttons.SelectMany(b => b.Shapes)))
                    {
                        if (shape.Kind != 0 || !listed.Add($"{shape.GroupName}#{shape.ImageIndex}") || !ui.TryGetImage(shape, out var image)) continue;
                        text.AppendLine($"  image {shape.GroupName}#{shape.ImageIndex}: {image.Parts.Count} parts");
                        foreach (var part in image.Parts)
                        {
                            text.AppendLine($"    {part.TextureName.Trim(),-16} dst=({part.TopLeft.X},{part.TopLeft.Y})-({part.BottomRight.X},{part.BottomRight.Y}) src=({part.SourceX},{part.SourceY} {part.SourceWidth}x{part.SourceHeight}) blend={part.BlendMode} flags={part.Flags} colours TL={part.ColorTopLeft} BL={part.ColorBottomLeft}");
                        }
                    }
                    File.WriteAllText(Path.Combine(dumpDir, $"menu_{menu.Name}.txt"), text.ToString());
                }
            }

            // GORDIAN_UI_DUMP_SCREENS: ';'-separated screens of '+'-joined menus, each composited at its authored frame
            // position on a 1024 x 768 canvas (the lobby's layout space) into screen_<first menu>.png.
            foreach (string screen in (Environment.GetEnvironmentVariable("GORDIAN_UI_DUMP_SCREENS") ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var names = screen.Split('+', StringSplitOptions.RemoveEmptyEntries);
                var canvasScreen = new SoftwareCanvas(1024, 768);
                foreach (string name in names)
                {
                    if (ui.TryGetMenu(name, out var menu)) canvasScreen.DrawMenu(ui, menu, menu.Frame.X, menu.Frame.Y);
                }
                canvasScreen.Save(Path.Combine(dumpDir, $"screen_{names[0]}.png"));
            }

            // GORDIAN_UI_DUMP_TEXTURES: comma-separated texture names written as texture_<name>.png (all with "*").
            string textureSpecs = Environment.GetEnvironmentVariable("GORDIAN_UI_DUMP_TEXTURES") ?? string.Empty;
            foreach (string spec in textureSpecs.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                foreach (string textureName in ui.TextureNames.ToList())
                {
                    if (spec != "*" && !textureName.Equals(spec, StringComparison.OrdinalIgnoreCase)) continue;
                    if (!ui.TryGetTexture(textureName, out var texture)) continue;
                    var sheet = new SoftwareCanvas(texture.Width, texture.Height);
                    sheet.Blit(texture, 0, 0);
                    sheet.Save(Path.Combine(dumpDir, $"texture_{textureName}.png"));
                    // The alpha channel as grey (raw decoded values, before the renderer's normalisation).
                    var alpha = new SoftwareCanvas(texture.Width, texture.Height);
                    var grey = new byte[texture.RgbaPixels.Length];
                    for (int i = 0; i < grey.Length; i += 4) { grey[i] = grey[i + 1] = grey[i + 2] = texture.RgbaPixels[i + 3]; grey[i + 3] = 255; }
                    alpha.Blit(new Gordian.Core.Resources.Graphics.DecodedTexture(texture.Name, texture.Width, texture.Height, grey), 0, 0);
                    alpha.Save(Path.Combine(dumpDir, $"texture_{textureName}_alpha.png"));
                    int top = texture.RgbaPixels[3], middle = texture.RgbaPixels[(texture.Height / 2 * texture.Width) * 4 + 3], bottom = texture.RgbaPixels[((texture.Height - 1) * texture.Width) * 4 + 3];
                    _output.WriteLine($"texture {textureName} {texture.Width}x{texture.Height} alpha top={top} middle={middle} bottom={bottom}");
                }
            }

            // groups.txt: every element group with its image count, to find sprites by name.
            {
                var text = new System.Text.StringBuilder();
                foreach (var group in ui.Groups.Values.OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase))
                {
                    text.AppendLine($"{group.Category}/{group.Name}: {group.Images.Count} images, textures {string.Join(",", group.TextureNames.Select(t => t.Trim()))}");
                }
                File.WriteAllText(Path.Combine(dumpDir, "groups.txt"), text.ToString());
            }

            // GORDIAN_UI_DUMP_IMAGES: "group:i-j+k,..." lists the parts of those images in images.txt.
            {
                var text = new System.Text.StringBuilder();
                foreach (string spec in (Environment.GetEnvironmentVariable("GORDIAN_UI_DUMP_IMAGES") ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries))
                {
                    string groupName = spec.Split(':')[0];
                    if (!ui.TryGetGroup(groupName, out var group) || !spec.Contains(':')) continue;
                    foreach (string range in spec.Split(':')[1].Split('+'))
                    {
                        var ends = range.Split('-');
                        int from = int.Parse(ends[0]), to = int.Parse(ends[^1]);
                        for (int i = from; i <= to && i < group.Images.Count; i++)
                        {
                            var image = group.Images[i];
                            text.AppendLine($"{group.Name}#{i}: {image.Parts.Count} parts");
                            foreach (var part in image.Parts)
                            {
                                text.AppendLine($"    {part.TextureName.Trim(),-16} dst=({part.TopLeft.X},{part.TopLeft.Y})-({part.BottomRight.X},{part.BottomRight.Y}) src=({part.SourceX},{part.SourceY} {part.SourceWidth}x{part.SourceHeight}) blend={part.BlendMode} flags={part.Flags} TL={part.ColorTopLeft} TR={part.ColorTopRight} BL={part.ColorBottomLeft} BR={part.ColorBottomRight}");
                            }
                        }
                    }
                }
                if (text.Length > 0) File.WriteAllText(Path.Combine(dumpDir, "images.txt"), text.ToString());
            }

            // GORDIAN_UI_DUMP_GROUPS: comma-separated groups, each optionally restricted to images ("windowps:38-40+75").
            // Every image is drawn with its index under it (in the stock font), across as many 512 x 512 sheets as needed.
            var font = UiFont.FromLibrary(ui);
            foreach (string spec in (Environment.GetEnvironmentVariable("GORDIAN_UI_DUMP_GROUPS") ?? "yubi,fontshp,framesus").Split(','))
            {
                string groupName = spec.Split(':')[0];
                if (!ui.TryGetGroup(groupName, out var group)) continue;
                var only = new System.Collections.Generic.HashSet<int>();
                if (spec.Contains(':'))
                {
                    foreach (string range in spec.Split(':')[1].Split('+'))
                    {
                        var ends = range.Split('-');
                        int from = int.Parse(ends[0]), to = int.Parse(ends[^1]);
                        for (int i = from; i <= to; i++) only.Add(i);
                    }
                }
                var sheet = new SoftwareCanvas(512, 512);
                int x = 8, y = 8, rowHeight = 0, page = 0;
                string suffix = only.Count > 0 ? "_subset" : string.Empty;
                for (int i = 0; i < group.Images.Count; i++)
                {
                    if (only.Count > 0 && !only.Contains(i)) continue;
                    var bounds = SoftwareCanvas.Bounds(group.Images[i]);
                    int w = Math.Max(24, bounds.MaxX - bounds.MinX), h = Math.Max(4, bounds.MaxY - bounds.MinY) + 12;
                    if (x + w > 504) { x = 8; y += rowHeight + 6; rowHeight = 0; }
                    if (y + h > 504)
                    {
                        sheet.Save(Path.Combine(dumpDir, $"group_{group.Name}{suffix}_{page++}.png"));
                        sheet = new SoftwareCanvas(512, 512);
                        x = 8; y = 8; rowHeight = 0;
                    }
                    sheet.DrawImage(ui, group.Images[i], x - bounds.MinX, y - bounds.MinY);
                    if (font != null)
                    {
                        int pen = x;
                        foreach (char c in i.ToString())
                        {
                            if (font.TryGetGlyph(c, out var glyph)) sheet.DrawImage(ui, glyph, pen, y + h - 12);
                            pen += font.GetAdvance(c) * 3 / 4;
                        }
                    }
                    x += w + 6;
                    rowHeight = Math.Max(rowHeight, h);
                }
                sheet.Save(Path.Combine(dumpDir, $"group_{group.Name}{suffix}_{page}.png"));
            }
        }

        [Fact]
        public void StatusIcons_DecodeByStatusId()
        {
            if (!Directory.Exists(GameDirectory)) return;
            var rm = new ResourceManager(GameDirectory);
            rm.InitializeFileTable();
            var icons = StatusIconLibrary.Load(rm);
            Assert.NotNull(icons);
            Assert.Equal(640, icons!.Count);

            int decoded = 0;
            for (int id = 0; id < icons.Count; id++) if (icons.TryGetIcon(id, out _)) decoded++;
            _output.WriteLine($"{decoded} of {icons.Count} status icons decode");
            Assert.True(decoded > 600);
            Assert.True(icons.TryGetIcon(40, out var protect));
            Assert.Equal((32, 32), (protect.Width, protect.Height));

            string? dumpDir = Environment.GetEnvironmentVariable("GORDIAN_UI_DUMP");
            if (string.IsNullOrEmpty(dumpDir)) return;
            // Icons 0-255 in a 16 x 16 grid of 32-pixel cells.
            var sheet = new SoftwareCanvas(512, 512);
            for (int id = 0; id < 256; id++)
            {
                if (icons.TryGetIcon(id, out var icon)) sheet.Blit(icon, (id % 16) * 32, (id / 16) * 32);
            }
            Directory.CreateDirectory(dumpDir);
            sheet.Save(Path.Combine(dumpDir, "status_icons.png"));
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

            public void Blit(Gordian.Core.Resources.Graphics.DecodedTexture texture, int x0, int y0)
            {
                for (int y = 0; y < texture.Height; y++)
                {
                    for (int x = 0; x < texture.Width; x++)
                    {
                        int px = x0 + x, py = y0 + y;
                        if (px >= _width || py >= _height) continue;
                        int t = (y * texture.Width + x) * 4, d = (py * _width + px) * 4;
                        float a = Math.Min(1f, texture.RgbaPixels[t + 3] / 128f);
                        for (int c = 0; c < 3; c++) _rgba[d + c] = (byte)(texture.RgbaPixels[t + c] * a + _rgba[d + c] * (1 - a));
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
