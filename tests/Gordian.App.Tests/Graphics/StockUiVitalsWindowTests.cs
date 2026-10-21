// tests/Gordian.App.Tests/Graphics/StockUiVitalsWindowTests.cs
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
        public void RendersPartyWindowWithThePendingInviteOrb()
        {
            using var screen = StockUiOffscreen.TryCreate(640, 200);
            if (screen == null) return;
            Assert.True(screen.Library.TryGetMenu("ptw0", out var menu));
            var f = menu.Frame;
            var placement = StockUiLayout.Place(f.Anchor, f.X, f.Y, f.Width, f.Height, 1, screen.Width, screen.Height);
            var shot = screen.Render(r =>
            {
                r.DrawMenu(menu, placement, includeButtons: false);
                StockUiPartyWindow.DrawInviteIcon(r, placement);
            }, "gpu_party_invite_orb.png");

            // A strongly green pixel inside the orb's box.
            int cx = (int)(placement.X + StockUiPartyWindow.InviteIconX + 4), cy = (int)(placement.Y + StockUiPartyWindow.InviteIconY + 4);
            var (red, green, blue) = screen.Pixel(shot, cx, cy);
            Assert.True(green > red && green > blue, $"expected a green orb at ({cx},{cy}), got {red},{green},{blue}");
        }
    }
}
