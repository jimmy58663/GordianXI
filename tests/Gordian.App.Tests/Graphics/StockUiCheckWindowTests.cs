// tests/Gordian.App.Tests/Graphics/StockUiCheckWindowTests.cs
using System;
using Gordian.App.Graphics;
using Gordian.Core.Network.Packets;
using Gordian.Core.Ui;
using Gordian.Core.World;
using Xunit;

namespace Gordian.App.Tests.Graphics
{
    /// <summary>The player check window (#64) rendered from the retail DATs on D3D11 (GORDIAN_UI_DUMP writes the frames).</summary>
    public class StockUiCheckWindowTests
    {
        [Fact]
        public void RendersCheckWindowWithHelpBarDescriptionPagesAndComment()
        {
            using var screen = StockUiOffscreen.TryCreate();
            if (screen == null) return;

            // The retail screenshots' Gemini (2026-10-07): Bard 99 / White Mage 49, Bihu roundlet +3 on the head, a linkshell leader.
            var state = new EquipInspectState();
            state.AddItems(1, new[]
            {
                new EquipInspectItem(16385, EquipSlotId.Main), new EquipInspectItem(23407, EquipSlotId.Head),
                new EquipInspectItem(12638, EquipSlotId.Body), new EquipInspectItem(13469, EquipSlotId.Ring2),
            });
            var message = new InspectMessageInfo("Gemini", "Testing", true, false, 1, 0);
            var info = state.Complete(1, 0x456, 10, 99, 3, 49, 10, 0, 0, LinkshellItemIds.Linkshell, "GordianXI", 0x0F3A, message);

            var menus = new StockUiMenuController
            {
                Library = screen.Library,
                ItemLookup = id => screen.Resources.TryGetItem(id, out var record) ? record : null,
            };
            var menu = menus.OpenCheck(new StockUiCheckData("Gemini", info, HasBazaar: false));
            Assert.NotNull(menu);
            var frame = menu!.Menu.Frame;
            var placement = StockUiLayout.Place(frame.Anchor, frame.X, frame.Y, frame.Width, frame.Height, 1, screen.Width, screen.Height);
            int px = (int)placement.X, py = (int)placement.Y;
            float window1Top = screen.Height - 150;
            var layout = new StockUiScreen(screen.Width, screen.Height, window1Top);
            byte[] Draw(string name) => screen.Render(r => StockUiMenuWindow.Draw(r, screen.Library, screen.Font, menu, placement, 0, logFont: screen.LogFont, screen: layout), name);

            // Cursor on View Wares (where it starts): the comment on Window 1's top edge, the help bar at the top.
            var onWares = Draw("gpu_check_comment.png");
            Assert.True(screen.HasLightPixel(onWares, px + 6, px + 120, (int)window1Top - 56 + 4 + 8), "no comment line");
            Assert.True(screen.HasLightPixel(onWares, px + 8, px + 100, py - 32 + 15), "no name in the title box");
            Assert.True(screen.HasLightPixel(onWares, px + 122, px + 400, py - 32 + 15), "no jobs in the help line");
            // The help line stretches toward the screen's right edge.
            var bar = screen.Pixel(onWares, (int)screen.Width - 40, py - 32 + 4);
            var clear = screen.Pixel(onWares, (int)screen.Width - 4, py - 32 + 4);
            Assert.True(Math.Abs(bar.R - clear.R) + Math.Abs(bar.G - clear.G) + Math.Abs(bar.B - clear.B) > 20, $"help line not stretched: {bar} vs {clear}");

            // Main (button 1, at 24,11) holds an icon; Sub (button 5, at 58,11) is the empty slot art: they differ.
            var icon = screen.Pixel(onWares, px + 24 + 16, py + 11 + 16);
            var empty = screen.Pixel(onWares, px + 58 + 16, py + 11 + 16);
            Assert.True(Math.Abs(icon.R - empty.R) + Math.Abs(icon.G - empty.G) + Math.Abs(icon.B - empty.B) > 30, $"no icon in Main: {icon} vs {empty}");

            // Cursor on the head: page 1 of its description in the twelve-line window under the grid, then page 2.
            menus.Move(Gordian.Core.Input.InputAction.MenuDown); // Main
            menus.Move(Gordian.Core.Input.InputAction.MenuDown); // Head
            Assert.Equal(23407, menu.SelectedCheckItem);
            var page1 = Draw("gpu_check_item.png");
            Assert.True(screen.HasLightPixel(page1, px + 48, px + 300, py + 192 + 4 + 16 * 11 + 8), "no footer on the twelfth line");
            Assert.True(menus.NextCheckPage());
            var page2 = Draw("gpu_check_item_page2.png");
            Assert.False(screen.HasLightPixel(page2, px + 48, px + 300, py + 192 + 4 + 16 * 8 + 8), "page 2 should be five lines tall");
            Assert.True(screen.HasLightPixel(page2, px + 48, px + 300, py + 192 + 4 + 16 * 2 + 8), "no description on page 2");
        }
    }
}
