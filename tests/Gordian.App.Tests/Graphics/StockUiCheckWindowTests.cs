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
        public void RendersCheckWindowWithIconsJobsAndComment()
        {
            using var screen = StockUiOffscreen.TryCreate();
            if (screen == null) return;

            var state = new EquipInspectState();
            state.AddItems(1, new[]
            {
                new EquipInspectItem(16385, EquipSlotId.Main), new EquipInspectItem(12511, EquipSlotId.Head),
                new EquipInspectItem(12638, EquipSlotId.Body), new EquipInspectItem(13469, EquipSlotId.Ring2),
            });
            var message = new InspectMessageInfo("Cybin", "Selling crystals, cheap!\nAsk for bulk prices.", true, false, 1, 0);
            var info = state.Complete(1, 0x456, 1, 75, 13, 37, 1, 0, 0, 513, "Gordian", 0x0F3A, message);

            var menus = new StockUiMenuController
            {
                Library = screen.Library,
                ItemLookup = id => screen.Resources.TryGetItem(id, out var record) ? record : null,
            };
            var menu = menus.OpenCheck(new StockUiCheckData("Cybin", info));
            Assert.NotNull(menu);
            var frame = menu!.Menu.Frame;
            var placement = StockUiLayout.Place(frame.Anchor, frame.X, frame.Y, frame.Width, frame.Height, 1, screen.Width, screen.Height);

            // Cursor on Main: the item's info window under the grid.
            var onItem = screen.Render(r => StockUiMenuWindow.Draw(r, screen.Library, screen.Font, menu, placement, 0, logFont: screen.LogFont), "gpu_check_item.png");
            int px = (int)placement.X, py = (int)placement.Y;
            // Main (button 1, at 24,11) holds an icon; Sub (button 5, at 58,11) is the empty slot art: they differ.
            var icon = screen.Pixel(onItem, px + 24 + 16, py + 11 + 16);
            var empty = screen.Pixel(onItem, px + 58 + 16, py + 11 + 16);
            Assert.True(Math.Abs(icon.R - empty.R) + Math.Abs(icon.G - empty.G) + Math.Abs(icon.B - empty.B) > 30, $"no icon in Main: {icon} vs {empty}");
            // The name and jobs line under View Wares.
            Assert.True(screen.HasLightPixel(onItem, px + 8, px + 60, py + 170), "no name under View Wares");
            Assert.True(screen.HasLightPixel(onItem, px + 120, px + 174, py + 170), "no jobs under View Wares");
            // The item info window right under the grid (layout y 240): the long name after the icon.
            Assert.True(screen.HasLightPixel(onItem, px + 48, px + 200, py + 192 + 13), "no item name in the info window");

            // Cursor on an empty slot: the bazaar message in the comment window instead.
            menus.Move(Gordian.Core.Input.InputAction.MenuRight); // Sub: empty
            Assert.Equal(0, menu.SelectedCheckItem);
            var onEmpty = screen.Render(r => StockUiMenuWindow.Draw(r, screen.Library, screen.Font, menu, placement, 0, logFont: screen.LogFont), "gpu_check_comment.png");
            Assert.True(screen.HasLightPixel(onEmpty, px + 6, px + 200, py + 192 + 4 + 8), "no comment line");
            Assert.True(screen.HasLightPixel(onEmpty, px + 6, px + 200, py + 192 + 20 + 8), "no second comment line");
        }
    }
}
