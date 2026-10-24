// tests/Gordian.App.Tests/Graphics/StockUiSynthesisWindowRenderTests.cs
using System;
using System.Linq;
using Gordian.App.Graphics;
using Gordian.Core.Network.Packets;
using Gordian.Engine.Ui;
using Gordian.Core.World;
using Xunit;

namespace Gordian.App.Tests.Graphics
{
    /// <summary>Offscreen D3D11 render of the synthesis grid (#330) on the retail DAT frames (GORDIAN_UI_DUMP writes gpu_synthesis_grid.png).</summary>
    public class StockUiSynthesisWindowRenderTests
    {
        [Fact]
        public void RendersTheGridWithTheCrystalWordSlotIconsCountAndTheInventoryBeside()
        {
            using var screen = StockUiOffscreen.TryCreate();
            if (screen == null) return;

            var inventory = new InventoryState();
            inventory.SetContainerSizes(ContainerId.Inventory, 30, 30);
            inventory.SetItem(ContainerId.Inventory, 1, 4096, 9, ItemLockFlag.Normal);   // Fire Crystal x9
            inventory.SetItem(ContainerId.Inventory, 2, 642, 9, ItemLockFlag.Normal);    // Zinc Ore x9
            inventory.SetItem(ContainerId.Inventory, 3, 1193, 12, ItemLockFlag.Normal);  // H.Q. Crab Shell x12
            var menus = new StockUiMenuController
            {
                Library = screen.Library,
                Inventory = inventory,
                ItemLookup = id => screen.Resources.TryGetItem(id, out var record) ? record : null,
            };
            var crystal = new StockUiItemRow(4096, "Fire Crystal", 9, ContainerId.Inventory, 1, 12);
            var grid = menus.OpenSynthesisGrid(null, crystal);
            Assert.NotNull(grid);
            grid!.SynthesisSlots[0] = new StockUiSynthesisSlot(642, 3, 2);
            grid.SelectedButtonId = 2; // an empty slot: the empty description window
            var frame = grid.Menu.Frame;
            var placement = StockUiLayout.Place(frame.Anchor, frame.X, frame.Y, frame.Width, frame.Height, 1, screen.Width, screen.Height);
            int gx = (int)placement.X, gy = (int)placement.Y;
            var layout = new StockUiScreen(screen.Width, screen.Height, null);
            byte[] pixels = screen.Render(r => StockUiMenuWindow.Draw(r, screen.Library, screen.Font, grid, placement, 0, logFont: screen.LogFont, screen: layout), "gpu_synthesis_grid.png");

            // "Heat" after the crystal's icon in the top band.
            Assert.True(screen.HasLightPixel(pixels, gx + 37, gx + 80, gy + 9), "no element word");
            // Slot 1 (7,22) holds the ore's icon; slot 6 (41,56) is the empty slot art.
            var icon = screen.Pixel(pixels, gx + 7 + 16, gy + 22 + 16);
            var empty = screen.Pixel(pixels, gx + 41 + 16, gy + 56 + 16);
            Assert.True(Math.Abs(icon.R - empty.R) + Math.Abs(icon.G - empty.G) + Math.Abs(icon.B - empty.B) > 30, $"no icon in slot 1: {icon} vs {empty}");
            // The "3" over the icon's bottom right.
            Assert.True(screen.HasLightPixel(pixels, gx + 7 + 24, gx + 7 + 32, gy + 22 + 28), "no count in slot 1");
            // The inventory beside the grid at x 198: the second row's name (the first, the crystal in use, is greyed).
            Assert.True(screen.HasLightPixel(pixels, gx + 182 + 24, gx + 182 + 120, gy + 24 + 8), "no inventory beside the grid");
            Assert.Equal(3, grid.CompanionRows.Count);
            Assert.True(grid.CompanionRows.First().Greyed); // the crystal in use
        }
    }
}
