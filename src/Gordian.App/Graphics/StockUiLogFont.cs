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
    /// cell is drawn at the pen and the pen moves to two pixels past the glyph's rightmost ink; a space is 7 px, and
    /// every digit takes the widest digit's advance (retail spaces digits evenly, so timestamps line up).
    /// Retail draws the text with a dark outline, added here as a one-pixel dilation of the mask under the glyph.
    /// The ASCII cells are copied into a small atlas of their own (with room for the outline) that the stock UI
    /// renderer draws like any other texture.
    /// </para>
    /// <para>
    /// After the ASCII the cells follow the CP932 (Shift-JIS) order from 0x8140 (cell 96; checked on the dump: 0x819F
    /// "◆" ends row 2, 0x81A1 "■" starts row 3). The few non-ASCII symbols the client writes itself are copied too
    /// (<see cref="Symbols"/>).
    /// </para>
    /// <para>
    /// Font mods (#376): the cell size is taken from the sheet (its width / 64, 16 on the native sheet), so a higher-resolution
    /// sheet in the same layout is drawn scaled down to the 16-px row. An anti-aliased sheet (alpha with levels between 0 and
    /// 255, such as XIView's ROM/119/51) keeps its own soft alpha and dark edge instead of the 1-bit mask and outline, and is
    /// measured by its bright core: XIView's glyphs sit about a pixel up and left of their cells and their soft edges run into
    /// the neighbouring cells, so measuring by alpha alone gave about 17-px advances with a stray bar of the next glyph's edge.
    /// </para>
    /// </summary>
    public sealed class StockUiLogFont
    {
        /// <summary>Height of a glyph cell in <c>moji</c>: one 16-px log row.</summary>
        public const int CellHeight = 16;

        public const string TextureName = "moji";

        /// <summary>The native sheet's cell, in pixels: the layout size every sheet's cells are drawn at.</summary>
        private const int NativeCell = 16;
        private const int SourceColumns = 64;
        private const int FirstChar = 0x20, LastChar = 0x7E;
        private const int SpaceAdvance = 7, InkGap = 2;
        private const byte OutlineAlpha = 200;

        /// <summary>Alpha from which a pixel counts as ink (both kinds of sheet).</summary>
        private const byte InkAlpha = 0x40;

        /// <summary>Brightness (the largest colour channel) from which an anti-aliased sheet's ink is the glyph's core, not its dark edge.</summary>
        private const byte CoreBrightness = 0x80;

        private const int AsciiCount = LastChar - FirstChar + 1;

        /// <summary>
        /// Non-ASCII characters the log draws, with their <c>moji</c> cell: the arrow of an Auction House sale line
        /// (CP932 0x81A8, cell 96 + 0xA8 - 0x40 = 199; round 2 N21, retail prints it between seller and buyer), and the eighth note
        /// mount key item names start with (U+266A, CP932 0x81F4, cell 238: the sheet skips CP932's unassigned codes 0x81AD-0x81B7,
        /// 0x81C0-0x81C7, 0x81CF-0x81D9 and 0x81E9-0x81EF, checked on a dump of the cells; #385, retail capture on local LSB
        /// 2026-11-08, <c>385-key-items-mount-note-glyph.png</c>: one eighth note about 8 px wide in the log font, no gap after it).
        /// </summary>
        private static readonly (char Char, int Cell)[] Symbols = { ('→', 199), ('♪', 238) };

        private readonly float[] _advances = new float[AsciiCount + Symbols.Length];
        private readonly int _columns;
        private readonly int _atlasCell, _pad;
        private readonly float _sourceScale;

        public DecodedTexture Atlas { get; }
        public string CacheKey { get; }

        /// <summary>The sheet's cell size in pixels (16 native; a higher-resolution font mod's is larger).</summary>
        public int SourceCell { get; }

        /// <summary>Whether the sheet's alpha is anti-aliased (a font mod), not the native 1-bit mask.</summary>
        public bool IsAntiAliased { get; }

        private StockUiLogFont(DecodedTexture atlas, int columns, int sourceCell, int pad, bool antiAliased, string cacheKey)
        {
            Atlas = atlas;
            _columns = columns;
            SourceCell = sourceCell;
            _pad = pad;
            _atlasCell = sourceCell + 2 * pad;
            _sourceScale = sourceCell / (float)NativeCell;
            IsAntiAliased = antiAliased;
            CacheKey = cacheKey;
        }

        /// <summary>Pen advance of a character in layout pixels (printable ASCII and <see cref="Symbols"/>; others advance 0).</summary>
        public float GetAdvance(char c) => SlotOf(c) is var i and >= 0 ? _advances[i] : 0;

        /// <summary>The character's slot in the atlas (ASCII first, then <see cref="Symbols"/>), or -1 without a glyph.</summary>
        private static int SlotOf(char c)
        {
            if (c >= FirstChar && c <= LastChar) return c - FirstChar;
            for (int i = 0; i < Symbols.Length; i++)
            {
                if (Symbols[i].Char == c) return AsciiCount + i;
            }
            return -1;
        }

        public float MeasureWidth(ReadOnlySpan<char> text)
        {
            float width = 0;
            foreach (char c in text) width += GetAdvance(c);
            return width;
        }

        /// <summary>
        /// Draws a line whose row (16 layout pixels) starts at (<paramref name="x"/>, <paramref name="y"/>) in screen
        /// pixels; returns the pen position after the last glyph. Each glyph snaps to a whole pixel unless
        /// <paramref name="snap"/> is false (text that moves smoothly, like the help bar's scrolling title).
        /// </summary>
        public float Draw(StockUiRenderer renderer, ReadOnlySpan<char> text, float x, float y, float scale, UiColor color, bool snap = true)
        {
            int cell = _atlasCell;
            float size = cell / _sourceScale * scale, pad = _pad / _sourceScale * scale;
            float pen = x;
            foreach (char c in text)
            {
                if (c != ' ' && SlotOf(c) is var i and >= 0)
                {
                    renderer.DrawTextureRegion(CacheKey, Atlas, i % _columns * cell, i / _columns * cell, cell, cell,
                        snap ? MathF.Round(pen - pad) : pen - pad, MathF.Round(y - pad), size, size, color);
                }
                pen += GetAdvance(c) * scale;
            }
            return pen;
        }

        /// <summary>Builds the font from the library's <c>moji</c> texture, or null when it is missing.</summary>
        public static StockUiLogFont? FromLibrary(UiResourceLibrary library)
        {
            if (!library.TryGetTexture(TextureName, out var moji) || moji == null) return null;
            return Build(moji, $"logfont:{TextureName}:{library.WindowSkin}");
        }

        /// <summary>
        /// The cell size of a <c>moji</c>-layout sheet (64 square cells to a row), or 0 when the sheet cannot hold the cells the
        /// log draws: narrower than 64 native cells, not a whole number of cells wide, or too short.
        /// </summary>
        public static int CellSizeOf(DecodedTexture moji)
        {
            if (moji.Width < SourceColumns * NativeCell || moji.Width % SourceColumns != 0) return 0;
            int cell = moji.Width / SourceColumns;
            int lastCell = 0;
            foreach (var (_, c) in Symbols) lastCell = Math.Max(lastCell, c);
            return moji.Height >= (lastCell / SourceColumns + 1) * cell ? cell : 0;
        }

        /// <summary>
        /// Copies the ASCII and <see cref="Symbols"/> cells of a <c>moji</c>-layout texture into an atlas and measures each glyph's
        /// advance; null when the sheet's layout does not fit (<see cref="CellSizeOf"/>).
        /// </summary>
        public static StockUiLogFont? Build(DecodedTexture moji, string cacheKey)
        {
            int sourceCell = CellSizeOf(moji);
            if (sourceCell == 0) return null;
            int pad = Math.Max(1, sourceCell / NativeCell);
            int cell = sourceCell + 2 * pad;
            const int columns = 16;
            int count = AsciiCount + Symbols.Length;
            int rows = (count + columns - 1) / columns;
            int width = columns * cell, height = rows * cell;
            var rgba = new byte[width * height * 4];
            var mask = new bool[cell * cell];
            byte[] src = moji.RgbaPixels;
            bool antiAliased = HasSoftAlpha(moji, sourceCell);
            float sourceScale = sourceCell / (float)NativeCell;

            var font = new StockUiLogFont(new DecodedTexture("logfont", width, height, rgba), columns, sourceCell, pad, antiAliased, cacheKey);
            for (int i = 0; i < count; i++)
            {
                int source = i < AsciiCount ? i : Symbols[i - AsciiCount].Cell;
                int sx = source % SourceColumns * sourceCell, sy = source / SourceColumns * sourceCell;
                int dx = i % columns * cell, dy = i / columns * cell;
                Array.Clear(mask);

                // An anti-aliased glyph's right edge is its rightmost bright core, and only the soft edge joined to it is
                // kept (ink columns that touch it, at most two native pixels): the next glyph's edge bleeding into the cell
                // is left out. The native glyph's is its rightmost ink.
                int right = -1, keep = sourceCell - 1;
                if (antiAliased)
                {
                    right = CoreRight(src, moji.Width, sx, sy, sourceCell);
                    keep = right;
                    int edgeLimit = Math.Min(sourceCell - 1, right + (int)MathF.Round(InkGap * sourceScale));
                    while (right >= 0 && keep < edgeLimit && ColumnHasInk(src, moji.Width, sx + keep + 1, sy, sourceCell)) keep++;
                }

                for (int y = 0; y < sourceCell; y++)
                {
                    for (int x = 0; x <= keep; x++)
                    {
                        int s = ((sy + y) * moji.Width + sx + x) * 4;
                        int d = ((dy + y + pad) * width + dx + x + pad) * 4;
                        if (antiAliased)
                        {
                            // The sheet's own soft alpha and dark edge are the glyph's outline.
                            if (src[s + 3] == 0) continue;
                            rgba[d] = src[s];
                            rgba[d + 1] = src[s + 1];
                            rgba[d + 2] = src[s + 2];
                            rgba[d + 3] = src[s + 3];
                            continue;
                        }
                        if (src[s + 3] < InkAlpha) continue;
                        mask[(y + pad) * cell + x + pad] = true;
                        right = Math.Max(right, x);
                        rgba[d] = src[s];
                        rgba[d + 1] = src[s + 1];
                        rgba[d + 2] = src[s + 2];
                        rgba[d + 3] = 0xFF;
                    }
                }

                // The native sheet's outline: every empty pixel next to the glyph turns dark.
                if (!antiAliased)
                {
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
                }

                // In layout pixels. The bright core ends about a pixel inside where a 1-bit mask's ink would, so an
                // anti-aliased glyph advances one pixel more to keep the calibrated spacing.
                font._advances[i] = right < 0 ? SpaceAdvance : right / sourceScale + InkGap + (antiAliased ? 1 : 0);
            }

            // Digits share one advance (the widest), so numbers and timestamps line up: a retail capture spaces
            // "1234567890" evenly, the 1 included.
            float digit = 0;
            for (char c = '0'; c <= '9'; c++) digit = Math.Max(digit, font._advances[c - FirstChar]);
            for (char c = '0'; c <= '9'; c++) font._advances[c - FirstChar] = digit;
            return font;
        }

        /// <summary>Whether any pixel of the sheet's ASCII cells has an alpha between 0 and 255 (an anti-aliased font mod).</summary>
        private static bool HasSoftAlpha(DecodedTexture moji, int sourceCell)
        {
            byte[] src = moji.RgbaPixels;
            for (int i = 0; i < AsciiCount; i++)
            {
                int sx = i % SourceColumns * sourceCell, sy = i / SourceColumns * sourceCell;
                for (int y = 0; y < sourceCell; y++)
                {
                    for (int x = 0; x < sourceCell; x++)
                    {
                        byte a = src[((sy + y) * moji.Width + sx + x) * 4 + 3];
                        if (a != 0 && a != 0xFF) return true;
                    }
                }
            }
            return false;
        }

        /// <summary>The rightmost column of a cell holding bright ink (the glyph's core, not its dark edge), or -1.</summary>
        private static int CoreRight(byte[] src, int sheetWidth, int sx, int sy, int size)
        {
            for (int x = size - 1; x >= 0; x--)
            {
                for (int y = 0; y < size; y++)
                {
                    int s = ((sy + y) * sheetWidth + sx + x) * 4;
                    if (src[s + 3] >= InkAlpha && Math.Max(src[s], Math.Max(src[s + 1], src[s + 2])) >= CoreBrightness) return x;
                }
            }
            return -1;
        }

        private static bool ColumnHasInk(byte[] src, int sheetWidth, int x, int sy, int height)
        {
            for (int y = 0; y < height; y++)
            {
                if (src[((sy + y) * sheetWidth + x) * 4 + 3] >= InkAlpha) return true;
            }
            return false;
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
