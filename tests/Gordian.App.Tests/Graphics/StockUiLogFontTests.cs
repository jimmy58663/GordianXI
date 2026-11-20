// tests/Gordian.App.Tests/Graphics/StockUiLogFontTests.cs
using Gordian.App.Graphics;
using Gordian.Core.Resources.Graphics;
using Xunit;

namespace Gordian.App.Tests.Graphics
{
    /// <summary>
    /// The log font's builder on synthetic <c>moji</c> sheets (#376): the native 1-bit sheet, an anti-aliased font mod whose
    /// neighbouring glyph bleeds into the cell (XIView's ROM/119/51), and a double-resolution sheet in the same layout.
    /// </summary>
    public sealed class StockUiLogFontTests
    {
        private const char Glyph = 'l';

        /// <summary>A sheet of 64 cells to a row with <see cref="Glyph"/>'s cell filled by <paramref name="paint"/>.</summary>
        private static DecodedTexture Sheet(int cell, System.Action<System.Action<int, int, byte, byte>> paint)
        {
            int width = 64 * cell, height = 4 * cell;
            var rgba = new byte[width * height * 4];
            int sx = (Glyph - 0x20) % 64 * cell, sy = (Glyph - 0x20) / 64 * cell;
            paint((x, y, value, alpha) =>
            {
                int i = ((sy + y) * width + sx + x) * 4;
                rgba[i] = rgba[i + 1] = rgba[i + 2] = value;
                rgba[i + 3] = alpha;
            });
            return new DecodedTexture("moji", width, height, rgba);
        }

        /// <summary>The atlas alpha of <see cref="Glyph"/>'s cell at sheet pixel (x, y).</summary>
        private static byte AtlasAlpha(StockUiLogFont font, int x, int y)
        {
            int pad = System.Math.Max(1, font.SourceCell / 16), cell = font.SourceCell + 2 * pad, slot = Glyph - 0x20;
            int ax = slot % 16 * cell + pad + x, ay = slot / 16 * cell + pad + y;
            return font.Atlas.RgbaPixels[(ay * font.Atlas.Width + ax) * 4 + 3];
        }

        [Fact]
        public void NativeSheet_MeasuresByInk_AndOutlinesTheMask()
        {
            var sheet = Sheet(16, set =>
            {
                for (int y = 2; y < 14; y++) { set(4, y, 0xF0, 0xFF); set(5, y, 0xF0, 0xFF); }
            });
            var font = StockUiLogFont.Build(sheet, "native")!;
            Assert.Equal(16, font.SourceCell);
            Assert.False(font.IsAntiAliased);
            Assert.Equal(7, font.GetAdvance(Glyph));          // rightmost ink 5 + 2
            Assert.Equal(0xFF, AtlasAlpha(font, 5, 5));
            Assert.Equal(200, AtlasAlpha(font, 6, 5));        // the added dark outline
            Assert.Equal(7, font.GetAdvance(' '));
        }

        /// <summary>
        /// XIView: the glyph's soft dark edge is kept with its own alpha, and the next glyph's edge bleeding into the cell's
        /// last column (alpha 0x90, dark) neither widens the advance nor is drawn.
        /// </summary>
        [Fact]
        public void AntiAliasedSheet_MeasuresTheBrightCore_AndDropsTheNeighboursBleed()
        {
            var sheet = Sheet(16, set =>
            {
                for (int y = 2; y < 14; y++)
                {
                    set(4, y, 0xF0, 0xFF);
                    set(5, y, 0xE0, 0xC0);
                    set(6, y, 0x20, 0x80);   // the glyph's own soft dark edge
                    set(15, y, 0x30, 0x90);  // the next glyph's edge
                }
            });
            var font = StockUiLogFont.Build(sheet, "xiview")!;
            Assert.True(font.IsAntiAliased);
            Assert.Equal(8, font.GetAdvance(Glyph));          // core 5 + 2 + 1, not 15 + 2
            Assert.Equal(0xC0, AtlasAlpha(font, 5, 5));       // the sheet's own alpha
            Assert.Equal(0x80, AtlasAlpha(font, 6, 5));
            Assert.Equal(0, AtlasAlpha(font, 7, 5));          // no added outline
            Assert.Equal(0, AtlasAlpha(font, 15, 5));         // the bleed is left out
        }

        /// <summary>A sheet twice the native size keeps the layout: 32-px cells, advances in 16-px layout pixels.</summary>
        [Fact]
        public void DoubleResolutionSheet_TakesItsCellSizeFromTheSheet()
        {
            var sheet = Sheet(32, set =>
            {
                for (int y = 4; y < 28; y++) for (int x = 8; x < 12; x++) set(x, y, 0xF0, 0xFF);
            });
            var font = StockUiLogFont.Build(sheet, "hires")!;
            Assert.Equal(32, font.SourceCell);
            Assert.Equal(7.5f, font.GetAdvance(Glyph));       // rightmost ink 11 / 2 + 2
        }

        [Fact]
        public void SheetOutsideTheLayout_BuildsNoFont()
        {
            Assert.Null(StockUiLogFont.Build(new DecodedTexture("moji", 1000, 2048, new byte[1000 * 2048 * 4]), "bad"));
            Assert.Null(StockUiLogFont.Build(new DecodedTexture("moji", 1024, 32, new byte[1024 * 32 * 4]), "short"));
        }
    }
}
