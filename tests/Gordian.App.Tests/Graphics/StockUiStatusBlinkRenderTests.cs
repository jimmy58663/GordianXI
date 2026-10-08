// tests/Gordian.App.Tests/Graphics/StockUiStatusBlinkRenderTests.cs
using System;
using Gordian.App.Graphics;
using Gordian.Core.Resources.Ui;
using Gordian.Core.Ui;
using Xunit;

namespace Gordian.App.Tests.Graphics
{
    /// <summary>Expiring status icons fade while they blink (#17), rendered from the retail DATs on D3D11.</summary>
    public class StockUiStatusBlinkRenderTests
    {
        [Fact]
        public void BlinkingIconFadesTowardTheBackground()
        {
            using var screen = StockUiOffscreen.TryCreate();
            if (screen == null) return;
            var icons = StatusIconLibrary.Load(screen.Resources);
            if (icons == null || !screen.Library.TryGetMenu("buff", out var grid)) return;
            var placement = StockUiLayout.Place(grid.Frame.Anchor, grid.Frame.X, grid.Frame.Y, grid.Frame.Width, grid.Frame.Height, 1, screen.Width, screen.Height);

            // Protect (40) twice: steady, then at the faintest point of its blink.
            var ids = new ushort[] { 40, 40 };
            var opacities = new[] { 1f, StockUiStatusBlink.Opacity(5, StockUiStatusBlink.BlinkPeriodSeconds / 2) };
            var frame = screen.Render(r => StockUiTargetWindow.DrawStatusIcons(r, icons, grid, placement, ids, opacities), "gpu_status_blink.png");
            var background = screen.Render(_ => { });

            // The time left under the second icon only (a minute or less left), in light glyphs from its lower edge down.
            var durations = new[] { null, StockUiStatusBlink.DurationText(42) };
            var timed = screen.Render(r => StockUiTargetWindow.DrawStatusIcons(r, icons, grid, placement, ids, null, durations, screen.Font), "gpu_status_duration.png");
            var first = grid.Buttons[0];
            var second = grid.Buttons[1];
            Assert.False(screen.HasLightPixel(timed, (int)placement.X + first.X, (int)placement.X + first.X + first.Width, (int)placement.Y + first.Y + first.Height + 2),
                "a duration under the first icon");
            Assert.True(screen.HasLightPixel(timed, (int)placement.X + second.X, (int)placement.X + second.X + second.Width, (int)placement.Y + second.Y + second.Height + 1, 150),
                "no duration under the second icon");

            int Difference(int slot)
            {
                var button = grid.Buttons[slot];
                int total = 0;
                for (int dy = 4; dy < button.Height - 4; dy++)
                {
                    for (int dx = 4; dx < button.Width - 4; dx++)
                    {
                        int x = (int)placement.X + button.X + dx, y = (int)placement.Y + button.Y + dy;
                        var a = screen.Pixel(frame, x, y);
                        var b = screen.Pixel(background, x, y);
                        total += Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B);
                    }
                }
                return total;
            }
            int steady = Difference(0), faded = Difference(1);
            Assert.True(steady > 0, "no icon drawn");
            Assert.InRange(faded, steady / 10, steady / 3); // about MinimumOpacity of the steady icon's contrast
        }
    }
}
