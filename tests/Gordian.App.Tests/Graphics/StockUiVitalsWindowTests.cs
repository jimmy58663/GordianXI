// tests/Gordian.App.Tests/Graphics/StockUiVitalsWindowTests.cs
using System;
using Gordian.App.Graphics;
using Gordian.Core.Ui;
using Xunit;

namespace Gordian.App.Tests.Graphics
{
    /// <summary>The engaged HP / MP / TP block (#391) and the pending-invite orb (#397) rendered from the retail DATs on D3D11 (GORDIAN_UI_DUMP writes the frames).</summary>
    public class StockUiVitalsWindowTests
    {
        [Fact]
        public void RendersVitalsBlockAtThePalettesAuthoredPlace()
        {
            using var screen = StockUiOffscreen.TryCreate(640, 200);
            if (screen == null) return;

            StockUiPlacement? at = null;
            var shot = screen.Render(r => at = StockUiVitalsWindow.Draw(r, screen.Library, screen.Font, 9999, 9999, 1400, 2794, 3000, 1, screen.Width, screen.Height), "gpu_vitals_block.png");

            Assert.NotNull(at);
            Assert.Equal(16, at!.Value.X);
            Assert.Equal(48, at.Value.Y);
            // The "HP" label's light glyph pixels sit near the first row's left.
            Assert.True(screen.HasLightPixel(shot, 16 + 6, 16 + 24, 48 + 6, 150) || screen.HasLightPixel(shot, 16 + 6, 16 + 24, 48 + 8, 150));
        }

        [Fact]
        public void GaugeShareFollowsTheValues()
        {
            Assert.Equal(100, StockUiVitalsWindow.Percent(3000, StockUiVitalsWindow.FullTp));
            Assert.Equal(50, StockUiVitalsWindow.Percent(1400, 2800));
            Assert.Equal(0, StockUiVitalsWindow.Percent(5, 0));
            Assert.Equal(100, StockUiVitalsWindow.Percent(20, 10));
        }

        [Fact]
        public void TpGaugeFlashesOnlyFromAThousand()
        {
            Assert.Equal(StockUiVitalsWindow.TpGaugeColor, StockUiVitalsWindow.TpColorAt(999, 0.3));
            Assert.Equal(StockUiVitalsWindow.TpGaugeColor, StockUiVitalsWindow.TpColorAt(1000, 0));
            Assert.Equal(StockUiVitalsWindow.TpFlashColor, StockUiVitalsWindow.TpColorAt(3000, StockUiVitalsWindow.TpFlashPeriod / 2));
        }

        [Fact]
        public void TpPulseIsASmoothLowFrequencyEase()
        {
            Assert.True(1 / StockUiVitalsWindow.TpFlashPeriod < 3);
            // No step between neighbouring frames (60 fps) and a bounded swing.
            int previous = StockUiVitalsWindow.TpColorAt(3000, 0).B, max = 0;
            for (double t = 1.0 / 60; t < 2; t += 1.0 / 60)
            {
                int b = StockUiVitalsWindow.TpColorAt(3000, t).B;
                max = Math.Max(max, Math.Abs(b - previous));
                previous = b;
            }
            Assert.True(max <= 8, $"largest per-frame step {max}");
            Assert.True(StockUiVitalsWindow.TpFlashColor.B - StockUiVitalsWindow.TpGaugeColor.B <= 0x70);
        }

        [Fact]
        public void RendersPartyWindowWithThePendingInviteIcon()
        {
            using var screen = StockUiOffscreen.TryCreate(640, 200);
            if (screen == null) return;
            Assert.True(screen.Library.TryGetMenu("ptw0", out var menu));
            var f = menu.Frame;
            var placement = StockUiLayout.Place(f.Anchor, f.X, f.Y, f.Width, f.Height, 1, screen.Width, screen.Height);
            var shot = screen.Render(r =>
            {
                r.DrawMenu(menu, placement, includeButtons: false);
                StockUiPartyWindow.DrawInviteIcon(r, screen.Font, placement);
            }, "gpu_party_invite_icon.png");

            // Some pixel of the icon's box differs from the grey window frame behind it.
            int x0 = (int)(placement.X + StockUiPartyWindow.InviteIconX), y0 = (int)(placement.Y + StockUiPartyWindow.InviteIconY);
            bool greenFound = false;
            for (int y = y0; y < y0 + 12 && !greenFound; y++)
            {
                for (int x = x0; x < x0 + 12; x++)
                {
                    var (red, green, blue) = screen.Pixel(shot, x, y);
                    if (green > red + 40 && green > blue + 40 && green > 120) { greenFound = true; break; }
                }
            }
            Assert.True(greenFound, "expected the icon's green '!'");
        }
    }
}
