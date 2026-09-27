// src/Gordian.App/Graphics/StockUiLogFont.cs
using System;
using Gordian.Core.Resources.Graphics;
using Gordian.Core.Resources.Ui;

namespace Gordian.App.Graphics
{
    /// <summary>
    /// The chat log's text font: the <c>moji</c> texture of the menu DATs (1024 x 2048, "font    moji" in the English
    /// menu DAT ROM/119/51 and in ROM/0/1), a grid of 16 x 16 cells, 64 to a row, with printable ASCII in the first
    /// cells (cell = code - 0x20) followed by kana, Greek, Cyrillic, Latin-1 and symbols. Its alpha is a 1-bit mask;
    /// the colour channels carry the glyph's own shading and dark edge.
    /// <para>
    /// Spacing is proportional (checked against a retail capture of every keyboard character, 2026-09-27): each
    /// cell is drawn at the pen and the pen moves to two pixels past the glyph's rightmost ink; a space is 7 px.
    /// Retail draws the text with a dark outline, added here as a one-pixel dilation of the mask under the glyph.
    /// The ASCII cells are copied into a small atlas of their own (with room for the outline) that the stock UI
    /// renderer draws like any other texture.
    /// </para>
    /// </summary>
    public sealed class StockUiLogFont
    {
        /// <summary>Height of a glyph cell in <c>moji</c>: one 16-px log row.</summary>
        public const int CellHeight = 16;

        public const string TextureName = "moji";

        private const int SourceCell = 16, SourceColumns = 64;
        private const int FirstChar = 0x20, LastChar = 0x7E, Pad = 1;
        private const int SpaceAdvance = 7, InkGap = 2;
        private const byte OutlineAlpha = 200;

        private readonly float[] _advances = new float[LastChar - FirstChar + 1];
        private readonly int _columns;

        public DecodedTexture Atlas { get; }
        public string CacheKey { get; }

        private StockUiLogFont(DecodedTexture atlas, int columns, string cacheKey)
        {
            Atlas = atlas;
            _columns = columns;
            CacheKey = cacheKey;
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
            const int cell = SourceCell + 2 * Pad;
            float pen = x;
            foreach (char c in text)
            {
                if (c > FirstChar && c <= LastChar)
                {
                    int i = c - FirstChar;
                    renderer.DrawTextureRegion(CacheKey, Atlas, i % _columns * cell, i / _columns * cell, cell, cell,
                        MathF.Round(pen - Pad * scale), MathF.Round(y - Pad * scale), cell * scale, cell * scale, color);
                }
                pen += GetAdvance(c) * scale;
            }
            return pen;
        }

        /// <summary>Builds the font from the library's <c>moji</c> texture, or null when it is missing.</summary>
        public static StockUiLogFont? FromLibrary(UiResourceLibrary library)
        {
            if (!library.TryGetTexture(TextureName, out var moji) || moji == null || moji.Width < SourceColumns * SourceCell) return null;
            return Build(moji, $"logfont:{TextureName}:{library.WindowSkin}");
        }

        /// <summary>
        /// Copies the ASCII cells of a <c>moji</c>-layout texture into an outlined atlas and measures each glyph's
        /// advance.
        /// </summary>
        public static StockUiLogFont Build(DecodedTexture moji, string cacheKey)
        {
            const int cell = SourceCell + 2 * Pad, columns = 16;
            int count = LastChar - FirstChar + 1;
            int rows = (count + columns - 1) / columns;
            int width = columns * cell, height = rows * cell;
            var rgba = new byte[width * height * 4];
            var mask = new bool[cell * cell];

            var font = new StockUiLogFont(new DecodedTexture("logfont", width, height, rgba), columns, cacheKey);
            byte[] src = moji.RgbaPixels;
            for (int i = 0; i < count; i++)
            {
                int sx = i % SourceColumns * SourceCell, sy = i / SourceColumns * SourceCell;
                int dx = i % columns * cell, dy = i / columns * cell;
                Array.Clear(mask);
                int right = -1;
                for (int y = 0; y < SourceCell; y++)
                {
                    for (int x = 0; x < SourceCell; x++)
                    {
                        int s = ((sy + y) * moji.Width + sx + x) * 4;
                        if (src[s + 3] < 0x40) continue;
                        mask[(y + Pad) * cell + x + Pad] = true;
                        right = Math.Max(right, x);
                        int d = ((dy + y + Pad) * width + dx + x + Pad) * 4;
                        rgba[d] = src[s];
                        rgba[d + 1] = src[s + 1];
                        rgba[d + 2] = src[s + 2];
                        rgba[d + 3] = 0xFF;
                    }
                }

                // The outline: every empty pixel next to the glyph turns dark.
                for (int y = 0; y < cell; y++)
                {
                    for (int x = 0; x < cell; x++)
                    {
                        if (mask[y * cell + x] || !Touches(mask, cell, x, y)) continue;
                        int d = ((dy + y) * width + dx + x) * 4;
                        rgba[d] = rgba[d + 1] = rgba[d + 2] = 0;
                        rgba[d + 3] = OutlineAlpha;
                    }
                }
                font._advances[i] = right < 0 ? SpaceAdvance : right + InkGap;
            }
            return font;
        }

        private static bool Touches(bool[] mask, int size, int x, int y)
        {
            for (int oy = -1; oy <= 1; oy++)
            {
                for (int ox = -1; ox <= 1; ox++)
                {
                    int xx = x + ox, yy = y + oy;
                    if (xx >= 0 && yy >= 0 && xx < size && yy < size && mask[yy * size + xx]) return true;
                }
            }
            return false;
        }
    }
}
