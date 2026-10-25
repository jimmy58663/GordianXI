// tests/Gordian.App.Tests/Graphics/StockUiMapWindowTests.cs
using System.Diagnostics;
using Gordian.App.Graphics;
using Gordian.Core.Resources.Tables;
using Gordian.Engine.Ui;
using Xunit;

namespace Gordian.App.Tests.Graphics
{
    /// <summary>The Map window (#23): Northern San d'Oria's retail map behind the <c>map0</c> list, the help bar over it (GORDIAN_UI_DUMP writes the frame).</summary>
    public class StockUiMapWindowTests
    {
        [Fact]
        public void RendersZoneMapBehindTheMapList()
        {
            using var screen = StockUiOffscreen.TryCreate();
            if (screen == null) return;
            var menus = new StockUiMenuController { Library = screen.Library, CurrentZone = () => 231 };
            Assert.True(menus.OpenMainMenu());
            menus.Top!.SelectedButtonId = 12; // Map
            menus.Activate();
            var map = menus.MapWindow;
            Assert.NotNull(map);
            var texture = ZoneMapFiles.Load(screen.Resources, map!.MapZone, map.MapNumber);
            Assert.NotNull(texture);
            var shot = screen.Render(r =>
            {
                StockUiMapWindow.Draw(r, screen.Library, screen.Font, screen.LogFont, texture, "Northern San d'Oria", 1, screen.Width, screen.Height);
                var frame = map.Menu.Frame;
                var at = StockUiLayout.Place(frame.Anchor, frame.X, frame.Y, frame.Width, frame.Height, 1, screen.Width, screen.Height);
                StockUiMenuWindow.Draw(r, screen.Library, screen.Font, map, at, Stopwatch.GetTimestamp(), logFont: screen.LogFont);
            }, "gpu_map_window.png");
            // The parchment fills the middle of the screen (warm: red above blue), not the green clear colour.
            var middle = screen.Pixel(shot, (int)screen.Width / 2, (int)screen.Height / 2 + 20);
            Assert.True(middle.R > middle.B && middle.R > 80, $"map pixel {middle}");
        }
    }
}
