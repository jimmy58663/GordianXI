// tests/Gordian.App.Tests/Graphics/StockUiButtonHintRenderTests.cs
using Gordian.App.Graphics;
using Gordian.Core.Ui;
using Xunit;

namespace Gordian.App.Tests.Graphics
{
    /// <summary>
    /// The "+ : Sort" hint of the Items options window (<c>itmsort2</c>) drawn with the key or button in use (#360) on D3D11
    /// (GORDIAN_UI_DUMP writes the frames): the keyboard's "+", an Xbox lettered button and a PlayStation Triangle.
    /// </summary>
    public class StockUiButtonHintRenderTests
    {
        [Fact]
        public void TheEightChosenGlyphsAreEmbedded_AtBothSizes()
        {
            foreach (char face in "ABXY")
            {
                Assert.True(ButtonGlyphArt.TryGet(new StockUiHint(face.ToString(), XboxFace: face), 20, out var small, out _));
                Assert.Equal(48, small.Width);
                Assert.True(ButtonGlyphArt.TryGet(new StockUiHint(face.ToString(), XboxFace: face), 60, out var large, out _));
                Assert.Equal(96, large.Width);
            }
            foreach (var (x, y) in new[] { (16, 16), (0, 0), (0, 16), (16, 0) })
            {
                Assert.True(ButtonGlyphArt.TryGet(new StockUiHint(string.Empty, x, y), 20, out var art, out _));
                Assert.Equal(art.Width, art.Height);
            }
            Assert.False(ButtonGlyphArt.TryGet(new StockUiHint("+"), 20, out _, out _)); // the keyboard's text hint has no art
        }

        [Fact]
        public void RendersTheHintForKeyboardXboxAndPlayStation()
        {
            using var screen = StockUiOffscreen.TryCreate();
            if (screen == null || !screen.Library.TryGetMenu(StockUiItems.OptionsMenu, out var options)) return;
            var at = new StockUiPlacement(40, 40, 2, false);
            byte[] Shot(StockUiHint hint, string name) => screen.Render(r =>
            {
                r.HintFont = screen.Font;
                r.HintLabel = () => hint;
                r.DrawMenu(options, at, includeButtons: true, opaqueBody: true);
            }, name);

            byte[] keyboard = Shot(new StockUiHint("+"), "gpu_hint_keyboard.png");
            byte[] xbox = Shot(new StockUiHint("Y", XboxFace: 'Y'), "gpu_hint_xbox_y.png");
            byte[] playStation = Shot(new StockUiHint("Triangle", 16, 16), "gpu_hint_ps_triangle.png");
            // The glyph room is x 4-22, y 4-20 of the frame at scale 2: each hint puts something coloured or light there, and they differ.
            int x0 = 40 + 8, x1 = 40 + 44, y0 = 40 + 8, y1 = 40 + 40;
            Assert.True(screen.HasLightPixel(keyboard, x0, x1, (y0 + y1) / 2), "no key text");
            bool coloured = false;
            for (int x = x0; x < x1 && !coloured; x++)
            {
                for (int y = y0; y < y1 && !coloured; y++)
                {
                    var p = screen.Pixel(xbox, x, y);
                    coloured = p.R > 120 && p.G > 100 && p.B < 80; // the yellow Y
                }
            }
            Assert.True(coloured, "no Xbox Y disc");
            Assert.NotEqual(xbox, playStation);
            Assert.NotEqual(keyboard, playStation);
        }
    }
}
