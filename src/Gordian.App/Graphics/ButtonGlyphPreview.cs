// src/Gordian.App/Graphics/ButtonGlyphPreview.cs
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Gordian.Core.Resources.Graphics;
using Gordian.Core.Resources.Ui;
using Gordian.Core.Ui;

namespace Gordian.App.Graphics
{
    /// <summary>
    /// The <c>/buttonpreview</c> picker (#360): lays every candidate button glyph out over the HUD at the size the Options hint
    /// draws it (or magnified), so the art for the menu hints can be chosen in the game itself. The candidates are the Xbox pack's
    /// digital A / B / X / Y buttons (every variant) and the PS4 pack's Triangle / Circle / Square / Cross (every variant), read at
    /// run time from the folder given to the command (the packs are not committed), beside what is drawn now (the Xbox discs)
    /// and the DAT's own PS2 symbols (<c>marubatu</c>). The packs are CC BY 4.0 by Arks (see THIRD_PARTY_NOTICES.md).
    /// <para>The 480 px PNGs are decoded with <see cref="PngRgba"/> and box-filtered down to the drawn size on load rather
    /// than sampled from the big texture (no mips are generated per texture, and no SVG library is needed).</para>
    /// </summary>
    public sealed class ButtonGlyphPreview
    {
        private sealed record Glyph(string Row, string Label, string FilePath);

        private readonly ConcurrentDictionary<string, (int Width, int Height, byte[] Rgba)?> _source = new();
        private readonly ConcurrentDictionary<(string, int), DecodedTexture> _sized = new();
        private string? _folder;
        private volatile List<Glyph> _glyphs = new();
        private volatile bool _loading;

        private static readonly UiColor Panel = new(0x0C, 0x0A, 0x22, 0x70);
        private static readonly UiColor White = UiColor.Neutral;

        /// <summary>The glyph files of an Xbox pack and a PS4 pack under <paramref name="folder"/> (either pack folder works too).</summary>
        public static List<(string Row, string Label, string FilePath)> Discover(string folder)
        {
            var found = new List<(string, string, string)>();
            foreach (string dir in Directory.EnumerateDirectories(folder, "ABXY", SearchOption.AllDirectories))
            {
                foreach (string file in Directory.EnumerateFiles(dir, "button_xbox_digital_*.png").OrderBy(VariantNumber).ThenBy(f => f, StringComparer.OrdinalIgnoreCase))
                {
                    // button_xbox_digital_a_3.png: letter a, variant 3.
                    string[] parts = Path.GetFileNameWithoutExtension(file).Split('_');
                    if (parts.Length < 5) continue;
                    found.Add(($"Xbox {parts[3].ToUpperInvariant()}", parts[4], file));
                }
            }
            foreach (string dir in Directory.EnumerateDirectories(folder, "Main Buttons", SearchOption.AllDirectories))
            {
                foreach (string file in Directory.EnumerateFiles(dir, "Button - PS *.png").OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
                {
                    // Button - PS Triangle White 2.png: symbol Triangle, variant "White 2".
                    string name = Path.GetFileNameWithoutExtension(file)["Button - PS ".Length..];
                    string symbol = name.Split(' ')[0];
                    if (symbol == "Blank") continue;
                    found.Add(($"PS {symbol}", name[symbol.Length..].Trim(), file));
                }
            }
            return found;
        }

        /// <summary>The variant number of an Xbox glyph file (button_xbox_digital_a_10.png: 10), so 2 sorts before 10.</summary>
        private static int VariantNumber(string file) =>
            int.TryParse(Path.GetFileNameWithoutExtension(file).Split('_')[^1], out int n) ? n : 0;

        /// <summary>Draws the picker (nothing while the preview is off); the art loads in the background the first time.</summary>
        public void Draw(StockUiRenderer renderer, UiFont? font, ButtonPreviewState state, float scale, uint width, uint height)
        {
            string? folder = state.Folder;
            if (folder == null || font == null) return;
            if (!string.Equals(folder, _folder, StringComparison.Ordinal)) Load(folder);

            renderer.HintFont ??= font;
            var glyphs = _glyphs;
            float cell = 16 * scale * state.Zoom;
            float pitch = cell + 10 * scale, rowHeight = cell + 6 * scale, rowLabelWidth = 96 * scale;
            float left = 16 * scale, top = 64 * scale;
            var rows = glyphs.GroupBy(g => g.Row).ToList();
            int columns = Math.Max(12, rows.Count == 0 ? 0 : rows.Max(r => r.Count()));
            float panelWidth = rowLabelWidth + columns * pitch + 12 * scale;
            int rowCount = rows.Count + 2;
            renderer.DrawTextureRect("colorbal", 7, 23, 1, 1, left - 6 * scale, top - 40 * scale, panelWidth, (rowCount + 1) * rowHeight + 76 * scale, Panel);
            renderer.DrawText(font, _loading ? "Button preview: loading..." : $"Button preview: {glyphs.Count} glyphs (zoom {state.Zoom}; the Options hint is 16 px at UI scale)",
                left, top - 34 * scale, scale);

            float y = top;
            for (int r = 0; r < rows.Count; r++, y += rowHeight)
            {
                bool packStart = r == 0 || rows[r - 1].Key.Split(' ')[0] != rows[r].Key.Split(' ')[0];
                if (r > 0 && packStart) y += 14 * scale;
                renderer.DrawText(font, rows[r].Key, left, y + (cell - font.LineHeight * scale) * 0.5f, scale);
                int c = 0;
                foreach (var glyph in rows[r])
                {
                    float x = left + rowLabelWidth + c * pitch;
                    DrawGlyph(renderer, glyph, x, y, cell);
                    // The variant's file suffix over the first row of each pack.
                    if (packStart)
                    {
                        renderer.DrawText(font, glyph.Label.Replace(" ", string.Empty), x, y - 13 * scale, scale * 0.75f);
                    }
                    c++;
                }
            }

            // What is drawn now: the Xbox discs, then the DAT PS2 symbols.
            y += 4 * scale;
            renderer.DrawText(font, "Drawn now", left, y + (cell - font.LineHeight * scale) * 0.5f, scale);
            float cx = left + rowLabelWidth;
            foreach (char face in "ABXY")
            {
                renderer.DrawHintGlyph(new StockUiHint(face.ToString(), XboxFace: face), cx, y, scale * state.Zoom);
                cx += pitch;
            }
            y += rowHeight;
            renderer.DrawText(font, "DAT (PS2)", left, y + (cell - font.LineHeight * scale) * 0.5f, scale);
            cx = left + rowLabelWidth;
            foreach (var (sx, sy) in new[] { (16, 16), (0, 0), (0, 16), (16, 0) }) // Triangle, Circle, Square, Cross
            {
                renderer.DrawHintGlyph(new StockUiHint(string.Empty, sx, sy), cx, y, scale * state.Zoom);
                cx += pitch;
            }
        }

        private void DrawGlyph(StockUiRenderer renderer, Glyph glyph, float x, float y, float size)
        {
            if (!_source.TryGetValue(glyph.FilePath, out var source) || source is not { } art) return;
            // Pre-size to the drawn size by a whole box factor (the remainder is a bilinear shrink of under 2x).
            int factor = Math.Clamp((int)(art.Width / Math.Max(1f, size)), 1, 30);
            var texture = _sized.GetOrAdd((glyph.FilePath, factor), key =>
            {
                byte[] pixels = key.Item2 == 1 ? art.Rgba : PngRgba.Downsample(art.Rgba, art.Width, art.Height, key.Item2);
                return new DecodedTexture("buttonpreview", art.Width / key.Item2, art.Height / key.Item2, pixels);
            });
            renderer.DrawTexture($"buttonpreview:{glyph.FilePath}:{factor}", texture, x, y, size, size, White);
        }

        private void Load(string folder)
        {
            _folder = folder;
            _loading = true;
            _glyphs = new List<Glyph>();
            _sized.Clear();
            Task.Run(() =>
            {
                try
                {
                    var found = Discover(folder);
                    foreach (var (_, _, file) in found)
                    {
                        _source[file] = PngRgba.Decode(File.ReadAllBytes(file));
                    }
                    _glyphs = found.Select(f => new Glyph(f.Row, f.Label, f.FilePath)).ToList();
                }
                catch (Exception ex)
                {
                    Gordian.Core.Diagnostics.GordianLog.Warning("UI", $"Button preview could not read '{folder}': {ex.Message}");
                }
                finally
                {
                    _loading = false;
                }
            });
        }
    }
}
