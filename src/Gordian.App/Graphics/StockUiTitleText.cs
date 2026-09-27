// src/Gordian.App/Graphics/StockUiTitleText.cs
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Gordian.Core.Resources.Graphics;
using Gordian.Core.Resources.Ui;
using Gordian.Core.Ui;

namespace Gordian.App.Graphics
{
    /// <summary>
    /// The log windows' titles ("Say", "Window 1:Say", "Window 2"), written over the top border in the bold italic
    /// serif of the menu labels. The DAT has no glyph table for that font (<c>menu2fon</c> is an atlas used by
    /// pre-rendered labels), so titles are laid out from the <c>fep</c> chat-mode labels, whose text is known and
    /// covers every title: each label keeps its hand-set spacing, and a character none of them spells (the colon)
    /// comes from <c>fontshp</c>. Checked against a retail capture (2026-09-27): "Window 1:Say" spaced like the
    /// "Window 1" label, white.
    /// </summary>
    public sealed class StockUiTitleText
    {
        private const string AtlasName = "menu2fon";

        // The atlas cells carry a faint dark background (raw alpha 0x22) that the labels hide under their pill; the
        // copy the titles draw from drops it, keeping the glyphs' dark outline (0x33 and up) and fill.
        private const byte BackgroundAlpha = 0x28;

        private readonly record struct LabelGlyph(float X, float Y, float Width, float Height, ushort SrcX, ushort SrcY, ushort SrcWidth, ushort SrcHeight);
        private sealed record Label(string Text, List<LabelGlyph> Glyphs, float Width);

        private static readonly ConditionalWeakTable<UiResourceLibrary, StockUiTitleText> Cache = new();
        private static readonly UiColor White = new(0x80, 0x80, 0x80, 0x80);

        // The colon's dots: the white centre of the "colorbal" ball (as the config pages' red bar uses), tinted.
        private const string DotTexture = "colorbal";
        private const float DotX = 7, DotY = 23;
        private static readonly UiColor DotEdge = new(0x08, 0x08, 0x10, 0x60);

        private readonly List<Label> _labels = new();
        private DecodedTexture? _atlas;
        private string _cacheKey = "title:" + AtlasName;

        /// <summary>The title font for a library (built once per library, from its <c>fep</c> labels).</summary>
        public static StockUiTitleText For(UiResourceLibrary library) => Cache.GetValue(library, Build);

        private static StockUiTitleText Build(UiResourceLibrary library)
        {
            var text = new StockUiTitleText();
            if (!library.TryGetGroup("fep", out var fep) || !library.TryGetTexture(AtlasName, out var atlas)) return text;

            var pixels = (byte[])atlas.RgbaPixels.Clone();
            for (int i = 3; i < pixels.Length; i += 4)
            {
                if (pixels[i] < BackgroundAlpha) pixels[i] = 0;
            }
            text._atlas = new DecodedTexture(AtlasName, atlas.Width, atlas.Height, pixels);
            text._cacheKey = $"title:{AtlasName}:{library.WindowSkin}";

            foreach (var mode in (ChatInputMode[])Enum.GetValues(typeof(ChatInputMode)))
            {
                if ((int)mode < fep.Images.Count) text.Harvest(fep.Images[(int)mode], StockUiChatInput.Label(mode));
            }
            // Longest first, so "Linkshell 2" wins over "Linkshell".
            text._labels.Sort((a, b) => b.Text.Length.CompareTo(a.Text.Length));
            return text;
        }

        private void Harvest(UiImage image, string labelText)
        {
            var glyphs = new List<LabelGlyph>();
            float left = float.MaxValue, right = float.MinValue;
            foreach (var part in image.Parts)
            {
                if (!UiResourceLibrary.TrimResourceName(part.TextureName).Equals(AtlasName, StringComparison.OrdinalIgnoreCase)) continue;
                glyphs.Add(new LabelGlyph(part.TopLeft.X, part.TopLeft.Y, part.TopRight.X - part.TopLeft.X, part.BottomLeft.Y - part.TopLeft.Y,
                    part.SourceX, part.SourceY, part.SourceWidth, part.SourceHeight));
                left = Math.Min(left, part.TopLeft.X);
                right = Math.Max(right, part.TopRight.X);
            }
            if (glyphs.Count != labelText.Replace(" ", string.Empty).Length) return;
            for (int i = 0; i < glyphs.Count; i++) glyphs[i] = glyphs[i] with { X = glyphs[i].X - left };
            _labels.Add(new Label(labelText, glyphs, right - left - 1));
        }

        /// <summary>Width of a title in layout pixels.</summary>
        public float Measure(string text, UiFont? fallback) => Layout(null, text, 0, 0, 1, fallback);

        /// <summary>Draws a title with its glyph box's top-left at (x, y) screen pixels.</summary>
        public void Draw(StockUiRenderer renderer, string text, float x, float y, float scale, UiFont? fallback) =>
            Layout(renderer, text, x, y, scale, fallback);

        private float Layout(StockUiRenderer? renderer, string text, float x, float y, float scale, UiFont? fallback)
        {
            float pen = 0;
            int i = 0;
            while (i < text.Length)
            {
                var label = Match(text, i);
                if (label != null && _atlas != null)
                {
                    if (renderer != null)
                    {
                        foreach (var g in label.Glyphs)
                        {
                            renderer.DrawTextureRegion(_cacheKey, _atlas, g.SrcX, g.SrcY, g.SrcWidth, g.SrcHeight,
                                x + (pen + g.X) * scale, y + g.Y * scale, g.Width * scale, g.Height * scale, White);
                        }
                    }
                    pen += label.Width;
                    i += label.Text.Length;
                    continue;
                }

                char c = text[i++];
                if (c == ':')
                {
                    // No label spells a colon and fontshp's comes in its own dark cell: two slanted 2-px dots with a
                    // dark edge, as the capture's "Window 1:Say" shows.
                    if (renderer != null)
                    {
                        foreach (var (dx, dy) in new[] { (2f, 3f), (1f, 7f) })
                        {
                            renderer.DrawTextureRect(DotTexture, DotX, DotY, 1, 1, x + (pen + dx - 1) * scale, y + (dy - 1) * scale, 4 * scale, 4 * scale, DotEdge);
                            renderer.DrawTextureRect(DotTexture, DotX, DotY, 1, 1, x + (pen + dx) * scale, y + dy * scale, 2 * scale, 2 * scale, White);
                        }
                    }
                    pen += 5;
                    continue;
                }
                if (fallback == null) continue;
                // fontshp glyphs (the colon) sit lower in their line than the label glyphs; nudged to share a baseline.
                if (renderer != null && c != ' ' && fallback.TryGetGlyph(c, out var glyph))
                {
                    renderer.DrawImage(glyph, x + pen * scale, y - 1 * scale, scale);
                }
                pen += c == ' ' ? 4 : fallback.GetAdvance(c) - 1;
            }
            return pen;
        }

        private Label? Match(string text, int index)
        {
            foreach (var label in _labels)
            {
                if (string.CompareOrdinal(text, index, label.Text, 0, label.Text.Length) == 0) return label;
            }
            return null;
        }
    }
}
