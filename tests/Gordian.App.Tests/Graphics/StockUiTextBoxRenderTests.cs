// tests/Gordian.App.Tests/Graphics/StockUiTextBoxRenderTests.cs
using System;
using System.Linq;
using Gordian.Core.Resources.Models;
using Gordian.Core.Resources.Tables;
using Gordian.App.Graphics;
using Gordian.Core.Network.Packets;
using Gordian.Engine.Ui;
using Gordian.Core.World;
using Xunit;

namespace Gordian.App.Tests.Graphics
{
    /// <summary>
    /// The shared text boxes (<see cref="StockUiTextBoxes"/>, <see cref="StockUiTextLayout"/>) on D3D11 with the retail fonts:
    /// the Leather Bandana's job list (round 4, image-17, ran past the item info window) stays inside the window
    /// (GORDIAN_UI_DUMP writes gpu_text_box_bandana.png).
    /// </summary>
    public class StockUiTextBoxRenderTests
    {
        [Fact]
        public void LeatherBandanaDescription_StaysInsideItsWindow()
        {
            using var screen = StockUiOffscreen.TryCreate();
            if (screen == null || screen.LogFont is not { } logFont) return;
            var previous = StockUiItemDescription.FontMeasure;
            StockUiItemDescription.FontMeasure = t => logFont.MeasureWidth(t);
            try
            {
                Assert.True(screen.Resources.TryGetItem(12440, out var bandana));
                var page = StockUiItemDescription.Pages(bandana!).First();
                Assert.All(page.Lines, l => Assert.True(logFont.MeasureWidth(l) <= StockUiItemDescription.TextWidth, l));

                var inventory = new InventoryState();
                inventory.SetContainerSizes(ContainerId.Inventory, 30, 30);
                inventory.SetItem(ContainerId.Inventory, 1, 12440, 1, ItemLockFlag.Normal);
                var menus = new StockUiMenuController
                {
                    Library = screen.Library,
                    Inventory = inventory,
                    ItemLookup = id => screen.Resources.TryGetItem(id, out var record) ? record : null,
                };
                var list = menus.OpenItems(null)!;
                var frame = list.Menu.Frame;
                var placement = StockUiLayout.Place(frame.Anchor, frame.X, frame.Y, frame.Width, frame.Height, 1, screen.Width, screen.Height);
                var layout = new StockUiScreen(screen.Width, screen.Height, null);
                byte[] pixels = screen.Render(r => StockUiMenuWindow.Draw(r, screen.Library, screen.Font, list, placement, 0, logFont: logFont, screen: layout),
                    "gpu_text_box_bandana.png");

                // The description window sits at the bottom left (x 16, 366 wide, its bottom 16 px above the screen's): nothing is
                // drawn in the band just right of it.
                Assert.True(screen.Library.TryGetMenu(StockUiItemDescription.FrameFor(page.LineCount), out var info));
                int right = (int)placement.X + info.Frame.Width;
                int bottom = (int)screen.Height - 16, top = bottom - info.Frame.Height;
                for (int y = top + 4; y < bottom - 2; y += 2)
                {
                    Assert.False(screen.HasLightPixel(pixels, right + 2, right + 80, y), $"text past the window's right edge at y {y}");
                }
                // The job list did wrap: the window's text reaches its second level line.
                Assert.True(page.Lines.Count(l => l.StartsWith("Lv.", System.StringComparison.Ordinal)) == 1);
            }
            finally
            {
                StockUiItemDescription.FontMeasure = previous;
            }
        }

        /// <summary>
        /// #464: the item info window of a stack (the Auction House's bid question, image-27: "12" on a Fire crystal) draws the
        /// count over the icon's bottom right in pale yellow, its ink ending 2 px right of the icon, on the icon's bottom row,
        /// 9 px tall (GORDIAN_UI_DUMP writes gpu_item_info_stack.png).
        /// </summary>
        [Fact]
        public void ItemInfo_DrawsAStacksCountOverTheIcon()
        {
            using var screen = StockUiOffscreen.TryCreate();
            if (screen == null || screen.LogFont is not { } logFont) return;
            Assert.True(screen.Resources.TryGetItem(4096, out var crystal));
            var page = StockUiItemDescription.Pages(crystal!).First();
            ItemRecord? Lookup(ushort id) => screen.Resources.TryGetItem(id, out var record) ? record : null;
            var at = new StockUiPlacement(100, 100, 1, false);
            byte[] pixels = screen.Render(r => StockUiItemInfoWindow.Draw(r, screen.Library, screen.Font, logFont, Lookup, 4096, page, at, 0, stackCount: 12),
                "gpu_item_info_stack.png");
            byte[] plain = screen.Render(r => StockUiItemInfoWindow.Draw(r, screen.Library, screen.Font, logFont, Lookup, 4096, page, at, 0));

            // The count's ink: pixels that differ from the window drawn without it and read pale yellow.
            int left = int.MaxValue, right = -1, top = int.MaxValue, bottom = -1;
            for (int y = 100; y < 160; y++)
            {
                for (int x = 100; x < 160; x++)
                {
                    var c = screen.Pixel(pixels, x, y);
                    if (c == screen.Pixel(plain, x, y) || c.R < 170 || c.G < 170 || c.B > c.R - 40) continue;
                    left = Math.Min(left, x); right = Math.Max(right, x); top = Math.Min(top, y); bottom = Math.Max(bottom, y);
                }
            }
            Assert.True(right >= 0, "no count drawn");
            // The icon spans x 108-139 and y 112-143.
            Assert.InRange(right, 140, 142);
            Assert.InRange(bottom, 142, 143);
            Assert.InRange(bottom - top + 1, 8, 10);
            Assert.InRange(left, 126, 136);
        }

        [Fact]
        public void ElementIcons_DrawAsTheirSpritesInTheLine()
        {
            // #417: the Paewr Belt's "DEF:14 [Fire]+25": the DAT's 0xEF 0x1F stays an icon, drawn as elfire on the line.
            using var screen = StockUiOffscreen.TryCreate();
            if (screen == null || screen.LogFont is not { } logFont) return;
            Assert.True(screen.Resources.TryGetItem(10831, out var belt));
            Assert.Contains(ElementGlyphs.IconChar(0), belt!.Description);
            Assert.Contains("Fire+25", ElementGlyphs.ToPlainText(belt.Description));

            string line = ElementGlyphs.IconChar(0) + "+25";
            Assert.Equal(StockUiTextBoxes.IconAdvance + logFont.MeasureWidth("+25"), StockUiTextBoxes.MeasureLine(screen.Font, logFont, line));
            byte[] pixels = screen.Render(r => StockUiTextBoxes.DrawLine(r, screen.Font, logFont, line, 100, 100, 1), "gpu_text_box_element_icon.png");
            // The fire icon's warm orange where the icon stands, the "+25" after it.
            bool warm = false;
            for (int y = 102; y < 114 && !warm; y++)
            for (int x = 102; x < 114 && !warm; x++)
            {
                var p = screen.Pixel(pixels, x, y);
                warm = p.R > 120 && p.R > p.B + 50;
            }
            Assert.True(warm, "no fire icon at the start of the line");
            Assert.True(screen.HasLightPixel(pixels, 116, 140, 108), "no \"+25\" after the icon");
        }
    }
}
