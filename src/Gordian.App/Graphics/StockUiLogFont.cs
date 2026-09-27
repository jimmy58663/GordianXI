// src/Gordian.App/Graphics/StockUiLogFont.cs
using System;
using System.Linq;
using Gordian.Core.Diagnostics;
using Gordian.Core.Resources.Graphics;
using Gordian.Core.Resources.Ui;
using SkiaSharp;

namespace Gordian.App.Graphics
{
    /// <summary>
    /// The chat log's text font. Retail PC does not draw the log with a DAT font: its log text is a smooth, upright,
    /// monospaced system font (MS Gothic's half-width glyphs, 8-px cells at 16 px) with a dark outline, measured from
    /// a retail capture at 1:1 (2026-09-27: a 94-character line spans 752 px, rows 16 px apart). This rasterises the
    /// printable ASCII range of the first installed font in <see cref="PreferredFamilies"/> once into a glyph atlas
    /// (white glyph over a 1-px dark outline, so a colour tint keeps the outline dark) that the stock UI renderer
    /// draws like any other texture.
    /// </summary>
    public sealed class StockUiLogFont
    {
        /// <summary>Font size in pixels at UI scale 1 (retail's 16-px rows).</summary>
        public const float PixelSize = 16;

        /// <summary>Height of a glyph cell: one log row.</summary>
        public const int CellHeight = 16;

        /// <summary>MS Gothic first (retail's look); then monospaced fonts found on other systems.</summary>
        public static readonly string[] PreferredFamilies =
        {
            "MS Gothic", "Osaka-Mono", "Noto Sans Mono CJK JP", "DejaVu Sans Mono", "Liberation Mono", "Menlo", "Consolas",
        };

        private const int FirstChar = 0x20, LastChar = 0x7E, Pad = 2;
        private const byte OutlineAlpha = 200;

        private readonly float[] _advances = new float[LastChar - FirstChar + 1];
        private readonly (int X, int Y, int Width)[] _cells = new (int, int, int)[LastChar - FirstChar + 1];
        private readonly int _cellHeight;

        public DecodedTexture Atlas { get; }
        public string FamilyName { get; }
        public string CacheKey { get; }

        private StockUiLogFont(DecodedTexture atlas, string family, int cellHeight)
        {
            Atlas = atlas;
            FamilyName = family;
            CacheKey = "logfont:" + family;
            _cellHeight = cellHeight;
        }

        /// <summary>Pen advance of a character in layout pixels (printable ASCII only; others advance 0).</summary>
        public float GetAdvance(char c) => c >= FirstChar && c <= LastChar ? _advances[c - FirstChar] : 0;

        public float MeasureWidth(ReadOnlySpan<char> text)
        {
            float width = 0;
            foreach (char c in text) width += GetAdvance(c);
            return width;
        }

        /// <summary>
        /// Draws a line whose row (16 layout pixels) starts at (<paramref name="x"/>, <paramref name="y"/>) in screen
        /// pixels; returns the pen position after the last glyph.
        /// </summary>
        public float Draw(StockUiRenderer renderer, ReadOnlySpan<char> text, float x, float y, float scale, UiColor color)
        {
            float pen = x;
            foreach (char c in text)
            {
                if (c > FirstChar && c <= LastChar)
                {
                    var cell = _cells[c - FirstChar];
                    renderer.DrawTextureRegion(CacheKey, Atlas, cell.X, cell.Y, cell.Width, _cellHeight,
                        MathF.Round(pen - Pad * scale), MathF.Round(y - Pad * scale), cell.Width * scale, _cellHeight * scale, color);
                }
                pen += GetAdvance(c) * scale;
            }
            return pen;
        }

        /// <summary>Builds the atlas from the first installed preferred family (the platform default if none is).</summary>
        public static StockUiLogFont? Create()
        {
            try
            {
                var installed = SKFontManager.Default.GetFontFamilies();
                string? family = PreferredFamilies.FirstOrDefault(f => installed.Contains(f, StringComparer.OrdinalIgnoreCase));
                using var typeface = family != null ? SKTypeface.FromFamilyName(family) : SKTypeface.Default;
                return Build(typeface, typeface.FamilyName);
            }
            catch (Exception ex)
            {
                GordianLog.Error("UI", $"Failed to build the chat log font: {ex.Message}");
                return null;
            }
        }

        private static StockUiLogFont Build(SKTypeface typeface, string family)
        {
            using var font = new SKFont(typeface, PixelSize) { Edging = SKFontEdging.Antialias, Subpixel = false, EmbeddedBitmaps = false };
            using var paint = new SKPaint { Color = SKColors.White, IsAntialias = true };
            var metrics = font.Metrics;
            // Centre the glyph box (ascent + descent) in the 16-px row, as the capture's rows sit.
            float baseline = Pad + (CellHeight - (metrics.Descent - metrics.Ascent)) * 0.5f - metrics.Ascent;
            int cellHeight = CellHeight + 2 * Pad;

            int count = LastChar - FirstChar + 1;
            var advances = new float[count];
            int maxWidth = 0;
            for (int i = 0; i < count; i++)
            {
                advances[i] = MathF.Round(font.MeasureText(((char)(FirstChar + i)).ToString(), paint));
                maxWidth = Math.Max(maxWidth, (int)advances[i]);
            }

            int cellWidth = maxWidth + 2 * Pad + 2; // italic overhang
            const int columns = 16;
            int rows = (count + columns - 1) / columns;
            int width = columns * cellWidth, height = rows * cellHeight;

            using var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul));
            using (var canvas = new SKCanvas(bitmap))
            {
                canvas.Clear(SKColors.Transparent);
                for (int i = 0; i < count; i++)
                {
                    int cx = i % columns * cellWidth, cy = i / columns * cellHeight;
                    canvas.DrawText(((char)(FirstChar + i)).ToString(), cx + Pad, cy + baseline, SKTextAlign.Left, font, paint);
                }
            }

            // Coverage from the rasterised glyphs, then a 1-px outline (3 x 3 dilation) under each glyph.
            byte[] src = bitmap.Bytes;
            var coverage = new byte[width * height];
            for (int p = 0; p < coverage.Length; p++) coverage[p] = src[p * 4 + 3];
            var rgba = new byte[width * height * 4];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int around = 0;
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        int yy = y + dy;
                        if (yy < 0 || yy >= height) continue;
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int xx = x + dx;
                            if (xx >= 0 && xx < width) around = Math.Max(around, coverage[yy * width + xx]);
                        }
                    }
                    float glyph = coverage[y * width + x] / 255f;
                    float outline = around / 255f * (OutlineAlpha / 255f);
                    float alpha = glyph + outline * (1 - glyph);
                    int o = (y * width + x) * 4;
                    byte grey = alpha > 0 ? (byte)Math.Clamp(MathF.Round(255 * glyph / alpha), 0, 255) : (byte)0;
                    rgba[o] = rgba[o + 1] = rgba[o + 2] = grey;
                    rgba[o + 3] = (byte)Math.Clamp(MathF.Round(alpha * 255), 0, 255);
                }
            }

            var result = new StockUiLogFont(new DecodedTexture("logfont", width, height, rgba), family, cellHeight);
            for (int i = 0; i < count; i++)
            {
                result._advances[i] = advances[i];
                result._cells[i] = (i % columns * cellWidth, i / columns * cellHeight, cellWidth);
            }
            return result;
        }
    }
}
