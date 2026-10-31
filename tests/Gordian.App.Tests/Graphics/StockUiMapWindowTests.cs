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
            var menus = new StockUiMenuController
            {
                Library = screen.Library,
                CurrentZone = () => 231,
                ZoneName = z => screen.Resources.TryGetString(Gordian.Core.Resources.Models.DMsgCategory.ZoneNames, z, out var n) ? n : null,
                RegionName = r => screen.Resources.TryGetString(Gordian.Core.Resources.Models.DMsgCategory.RegionNames, r, out var n) ? n : null,
            };
            Assert.True(menus.OpenMainMenu());
            menus.Top!.SelectedButtonId = 12; // Map
            menus.Activate();
            var map = menus.MapWindow;
            Assert.NotNull(map);
            var texture = ZoneMapFiles.Load(screen.Resources, map!.MapZone, map.MapNumber);
            Assert.NotNull(texture);
            var shot = screen.Render(r =>
            {
                StockUiMapWindow.Draw(r, screen.Library, screen.Font, screen.LogFont, map, texture, "Northern San d'Oria", 1, screen.Width, screen.Height);
                var frame = map.Menu.Frame;
                var at = StockUiLayout.Place(frame.Anchor, frame.X, frame.Y, frame.Width, frame.Height, 1, screen.Width, screen.Height);
                StockUiMenuWindow.Draw(r, screen.Library, screen.Font, map, at, Stopwatch.GetTimestamp(), logFont: screen.LogFont);
            }, "gpu_map_window.png");
            // The parchment fills the middle of the screen (warm: red above blue), not the green clear colour.
            var middle = screen.Pixel(shot, (int)screen.Width / 2, (int)screen.Height / 2 + 20);
            Assert.True(middle.R > middle.B && middle.R > 80, $"map pixel {middle}");
            // It covers the whole window (round 1): the corners are parchment too.
            var corner = screen.Pixel(shot, 4, (int)screen.Height - 4);
            Assert.True(corner.R > corner.G - 20 && corner.R > 60, $"corner pixel {corner}");

            // Change Map: the area list over the chat, "Current Area (Area)" then the regions by name.
            menus.Move(Gordian.Engine.Input.InputAction.MenuDown);
            menus.Move(Gordian.Engine.Input.InputAction.MenuDown);
            menus.Activate();
            var areas = menus.Top!;
            Assert.Equal(StockUiMap.AreaListMenu, areas.Name);
            Assert.Equal("San d'Oria", areas.Rows[1].Text);
            menus.MoveMapView(0, 0, 0, 3);
            var areaFrame = areas.Menu.Frame;
            var areaAt = StockUiLayout.Place(areaFrame.Anchor, areaFrame.X, areaFrame.Y, areaFrame.Width, areaFrame.Height, 1, screen.Width, screen.Height);
            var areaRow = areas.Menu.FindButton(1)!;
            var listShot = screen.Render(r =>
            {
                StockUiMapWindow.Draw(r, screen.Library, screen.Font, screen.LogFont, map, texture, "Northern San d'Oria", 1, screen.Width, screen.Height);
                foreach (var open in new[] { map, areas })
                {
                    var frame = open.Menu.Frame;
                    var at = StockUiLayout.Place(frame.Anchor, frame.X, frame.Y, frame.Width, frame.Height, 1, screen.Width, screen.Height);
                    StockUiMenuWindow.Draw(r, screen.Library, screen.Font, open, at, Stopwatch.GetTimestamp(), logFont: screen.LogFont);
                }
            }, "gpu_map_arealist.png");
            // The selected row's red bar (retail round 3 gap, image-25): about (112, 54, 51) just below the row's middle.
            var bar = screen.Pixel(listShot, (int)(areaAt.X + 200), (int)(areaAt.Y + areaRow.Y + areaRow.Height * 0.55f));
            Assert.True(bar.R > 80 && bar.R > bar.G + 30 && bar.R > bar.B + 30, $"selected row bar pixel {bar}");
            var unselected = screen.Pixel(listShot, (int)(areaAt.X + 200), (int)(areaAt.Y + areaRow.Y + areaRow.Height * 1.55f));
            Assert.True(unselected.R - unselected.G < 25, $"unselected row pixel {unselected}");

            // Beyond retail (round 2): zoomed out and panned to the top, the map stops a margin under the help bar, black above.
            menus.CloseTop();
            menus.MoveMapView(0, -10, 0, -100);
            shot = screen.Render(r => StockUiMapWindow.Draw(r, screen.Library, screen.Font, screen.LogFont, map, texture, "Northern San d'Oria", 1, screen.Width, screen.Height),
                "gpu_map_margin.png");
            float margin = StockUiMapWindow.Margin(screen.Library, 1, screen.Width, screen.Height);
            Assert.True(margin > 20, $"margin {margin}");
            var above = screen.Pixel(shot, (int)screen.Width / 2, (int)margin - 3);
            Assert.True(above.R < 16 && above.G < 16 && above.B < 16, $"margin pixel {above}");
            var below = screen.Pixel(shot, (int)screen.Width / 2, (int)margin + 6);
            Assert.True(below.R > 60, $"map's top edge pixel {below}");
        }

        /// <summary>The zone's home points on the Map window (round 1 of #168): the pale-blue crystal on each, here Northern San d'Oria #4.</summary>
        [Fact]
        public void ShowsTheZonesHomePoints()
        {
            using var screen = StockUiOffscreen.TryCreate();
            if (screen == null) return;
            var menus = new StockUiMenuController { Library = screen.Library, CurrentZone = () => 231 };
            Assert.True(menus.OpenMainMenu());
            menus.Top!.SelectedButtonId = 12; // Map
            menus.Activate();
            var map = menus.MapWindow!;
            var loaded = ZoneMapFiles.LoadWithOrigin(screen.Resources, map.MapZone, map.MapNumber)!.Value;
            var shot = screen.Render(r => StockUiMapWindow.Draw(r, screen.Library, screen.Font, screen.LogFont, map, loaded.Texture, "Northern San d'Oria", 1,
                screen.Width, screen.Height, loaded.Origin), "gpu_map_home_points.png");
            var (x, y, w, h) = StockUiMapWindow.Placement(map, loaded.Texture.Width, loaded.Texture.Height, screen.Width, screen.Height,
                StockUiMapWindow.Margin(screen.Library, 1, screen.Width, screen.Height));
            var (u, v) = ZoneMapProjection.ToMap(loaded.Origin!.Value, ZoneMapProjection.TownScale, -133, 195);
            int px = (int)(x + u * w / loaded.Texture.Width), py = (int)(y + v * h / loaded.Texture.Height);
            bool blue = false;
            for (int dy = -6; dy <= 6 && !blue; dy++)
            {
                for (int dx = -6; dx <= 6 && !blue; dx++)
                {
                    var p = screen.Pixel(shot, px + dx, py + dy);
                    blue = p.B > p.R + 20 && p.B > 120;
                }
            }
            Assert.True(blue, $"no crystal near ({px}, {py})");
        }
    }
}
