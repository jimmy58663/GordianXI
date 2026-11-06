// tests/Gordian.App.Tests/Graphics/StockUiSeekIconRenderTests.cs
using System;
using Gordian.App.Graphics;
using Gordian.Engine.Ui;
using Xunit;

namespace Gordian.App.Tests.Graphics
{
    /// <summary>A search result's seeker shows the Seek Party icon at the row's far left (round 2 of the 2026-11-03 test).</summary>
    public class StockUiSeekIconRenderTests
    {
        [Fact]
        public void Seeker_HasTheIconLeftOfTheJobs_OthersDoNot()
        {
            using var screen = StockUiOffscreen.TryCreate();
            if (screen == null) return;

            var plain = Render(screen, seeking: false, dumpName: null, out var cell);
            var seeker = Render(screen, seeking: true, dumpName: "gpu_search_seeking.png", out _);

            int changed = 0, otherRow = 0;
            for (int y = 0; y < 16; y++)
                for (int x = 0; x < 16; x++)
                {
                    if (Differs(screen.Pixel(plain, cell.X + x, cell.Y + y), screen.Pixel(seeker, cell.X + x, cell.Y + y))) changed++;
                    if (Differs(screen.Pixel(plain, cell.X + x, cell.NextY + y), screen.Pixel(seeker, cell.X + x, cell.NextY + y))) otherRow++;
                }
            Assert.True(changed > 60, $"no icon on the seeker's row: {changed} pixels changed");
            Assert.Equal(0, otherRow);
        }

        private static bool Differs((byte R, byte G, byte B) a, (byte R, byte G, byte B) b) =>
            Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B) > 30;

        /// <summary>Renders Gemini (seeking or not) over Cybin; <paramref name="cell"/> is the first row's icon cell and the second's y.</summary>
        private static byte[] Render(StockUiOffscreen screen, bool seeking, string? dumpName, out (int X, int Y, int NextY) cell)
        {
            var menus = new StockUiMenuController { Library = screen.Library };
            menus.OpenSearchResults(new[]
            {
                new StockUiPerson("Gemini") { Jobs = "[BRD75/WHM37]Hum", IsSeeking = seeking },
                new StockUiPerson("Cybin") { Jobs = "[BRD75/WHM37]Hum" },
            });
            var menu = menus.Top!;
            menu.SelectedButtonId = 1; // the bar, so neither player has the cursor's row
            var frame = menu.Menu.Frame;
            var placement = StockUiLayout.Place(frame.Anchor, frame.X, frame.Y, frame.Width, frame.Height, 1, screen.Width, screen.Height);
            var firstRow = menu.Menu.FindButton(1)!;
            float pitch = StockUiMenuWindow.RowPitch(firstRow, menu.Menu.FindButton(2));
            int x = (int)(placement.X + firstRow.X + 3);
            int y = (int)(placement.Y + firstRow.Y + pitch + (firstRow.Height - 16) * 0.5f);
            cell = (x, y, (int)(y + pitch));
            return screen.Render(r => StockUiMenuWindow.Draw(r, screen.Library, screen.Font, menu, placement, 0, logFont: screen.LogFont), dumpName);
        }
    }
}
