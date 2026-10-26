// tests/Gordian.App.Tests/Graphics/StockUiItemWindowRenderTests.cs
using System;
using System.Threading.Tasks;
using Gordian.App.Graphics;
using Gordian.Core.Network.Packets;
using Gordian.Engine.Ui;
using Gordian.Core.World;
using Xunit;

namespace Gordian.App.Tests.Graphics
{
    /// <summary>Offscreen D3D11 renders of the Items (#351) and Equipment (#352) windows on the retail DAT frames.</summary>
    public class StockUiItemWindowRenderTests
    {
        [Fact]
        public void RendersItemsListWithIconsCountsAndDescription_AndTheEquipmentGrid()
        {
            using var screen = StockUiOffscreen.TryCreate();
            if (screen == null) return;

            var inventory = new InventoryState();
            inventory.SetContainerSizes(ContainerId.Inventory, 80, 80);
            inventory.SetItem(ContainerId.Inventory, 1, 4112, 12, ItemLockFlag.Normal);   // Potion x12
            inventory.SetItem(ContainerId.Inventory, 2, 4096, 3, ItemLockFlag.Normal);    // Fire Crystal x3
            inventory.SetItem(ContainerId.Inventory, 3, 12448, 1, ItemLockFlag.Normal);   // Bronze Cap
            inventory.SetItem(ContainerId.Inventory, 4, 16535, 1, ItemLockFlag.Normal);   // Bronze Sword
            inventory.SetEquip(EquipSlotId.Head, ContainerId.Inventory, 3);
            var menus = new StockUiMenuController
            {
                Library = screen.Library,
                Inventory = inventory,
                ItemLookup = id => screen.Resources.TryGetItem(id, out var record) ? record : null,
                EquipCharacter = () => new StockUiEquipCharacter(1, 30),
                CharacterStats = () => new StockUiCharacterStats("Gordian", "Lv30 Warrior", "Lv15 Ninja", "ILv 0/Su 0", 580, 600, 0, 0, 1000,
                    new[] { 30, 28, 29, 27, 20, 22, 21 }, new[] { 5, 0, 2, 0, 0, -1, 0 }, 123, 145, new[] { 10, 11, 12, 13, 14, 15, 16, 17 }),
            };
            var list = menus.OpenItems(null);
            Assert.NotNull(list);
            var frame = list!.Menu.Frame;
            var placement = StockUiLayout.Place(frame.Anchor, frame.X, frame.Y, frame.Width, frame.Height, 1, screen.Width, screen.Height);
            int px = (int)placement.X, py = (int)placement.Y;
            var layout = new StockUiScreen(screen.Width, screen.Height, null);
            byte[] items = screen.Render(r => StockUiMenuWindow.Draw(r, screen.Library, screen.Font, list, placement, 0, logFont: screen.LogFont, screen: layout), "gpu_items_list.png");

            // Row 2 (y 24): the count over the icon (retail capture 2026-10-11), the name after it; the description at the
            // screen's bottom left (the log hidden), 16 px up.
            Assert.False(list.ItemRows[3].Equipped);
            Assert.True(list.ItemRows[2].Equipped);
            Assert.True(screen.HasLightPixel(items, px + 24, px + 100, py + 24 + 8), "no name on the second row");
            Assert.True(screen.HasLightPixel(items, px + 8, px + 20, py + 24 + 9), "no count on the second row's icon");
            Assert.True(screen.HasLightPixel(items, px + 48, px + 300, (int)screen.Height - 16 - 56 + 4 + 8), "no description name at the bottom");
            // The fifth row has no item.
            Assert.False(screen.HasLightPixel(items, px + 24, px + 130, py + 6 + 18 * 4 + 8), "an item on an empty row");

            var grid = menus.OpenEquipment(null);
            Assert.NotNull(grid);
            grid!.SelectedButtonId = StockUiCheck.ButtonOf(EquipSlotId.Head);
            var gridFrame = grid.Menu.Frame;
            var at = StockUiLayout.Place(gridFrame.Anchor, gridFrame.X, gridFrame.Y, gridFrame.Width, gridFrame.Height, 1, screen.Width, screen.Height);
            int gx = (int)at.X, gy = (int)at.Y;
            byte[] equipment = screen.Render(r => StockUiMenuWindow.Draw(r, screen.Library, screen.Font, grid, at, 0, logFont: screen.LogFont, screen: layout), "gpu_equipment.png");
            // Head (button 2 at 24,45) holds the cap's icon; Body (button 3 at 24,79) is the empty slot art.
            var icon = screen.Pixel(equipment, gx + 24 + 16, gy + 45 + 16);
            var empty = screen.Pixel(equipment, gx + 24 + 16, gy + 79 + 16);
            Assert.True(Math.Abs(icon.R - empty.R) + Math.Abs(icon.G - empty.G) + Math.Abs(icon.B - empty.B) > 30, $"no icon in Head: {icon} vs {empty}");
            // The stats side left of the grid (persona at 16,48): the STR value on its row.
            Assert.True(screen.HasLightPixel(equipment, gx - 114 + 60, gx - 114 + 108, gy + 109 + 5), "no DEX value");
            // Attack under the grid, before Defense.
            Assert.True(screen.HasLightPixel(equipment, gx + 55, gx + 88, gy + 149 + 5), "no Attack value");
        }

        /// <summary>
        /// View House (#23, round 1 image-7): a storage's list at the left with the inventory beside it at x 198; Right moves the
        /// cursor to the inventory, which then draws at the right (as the HUD places it) with the storage beside it at the left.
        /// </summary>
        [Fact]
        public void RendersViewHouseStorageBesideTheInventory()
        {
            using var screen = StockUiOffscreen.TryCreate();
            if (screen == null) return;
            var inventory = new InventoryState();
            inventory.SetContainerSizes(ContainerId.Inventory, 80, 80);
            inventory.SetContainerSizes(ContainerId.Wardrobe, 80, 80);
            inventory.SetItem(ContainerId.Inventory, 1, 12448, 1, ItemLockFlag.Normal);   // Bronze Cap (a Wardrobe greys what is not equipment)
            inventory.SetItem(ContainerId.Inventory, 2, 4112, 12, ItemLockFlag.Normal);   // Potion x12
            inventory.SetItem(ContainerId.Wardrobe, 1, 16535, 1, ItemLockFlag.Normal);    // Bronze Sword
            var menus = new StockUiMenuController
            {
                Library = screen.Library,
                Inventory = inventory,
                ItemLookup = id => screen.Resources.TryGetItem(id, out var record) ? record : null,
            };
            Assert.True(menus.Open(StockUiMenuEntries.MainMenuPage2));
            menus.Top!.SelectedButtonId = 4; // View House
            menus.Activate();
            menus.Top!.SelectedButtonId = 7; // Mog Wardrobe
            menus.Activate();
            var list = menus.Top!;
            Assert.Equal(StockUiItemListKind.Storage, list.ItemListKind);
            var frame = list.Menu.Frame;
            var placement = StockUiLayout.Place(frame.Anchor, frame.X, frame.Y, frame.Width, frame.Height, 1, screen.Width, screen.Height);
            int px = (int)placement.X, py = (int)placement.Y;
            var layout = new StockUiScreen(screen.Width, screen.Height, null);
            byte[] shot = screen.Render(r => StockUiMenuWindow.Draw(r, screen.Library, screen.Font, list, placement, 0, logFont: screen.LogFont, screen: layout), "gpu_viewhouse_storage.png");
            // The wardrobe's sword on the left list's first row, the inventory's cap on the right list's.
            Assert.True(screen.HasLightPixel(shot, px + 24, px + 120, py + 6 + 8), "no item on the storage's first row");
            Assert.True(screen.HasLightPixel(shot, px + 182 + 24, px + 182 + 120, py + 6 + 8), "no item on the inventory's first row");

            menus.Move(Gordian.Engine.Input.InputAction.MenuRight);
            Assert.True(list.StorageInventorySide);
            var right = placement with { X = placement.X + (StockUiViewHouse.InventoryX - frame.X) };
            shot = screen.Render(r => StockUiMenuWindow.Draw(r, screen.Library, screen.Font, list, right, 0, logFont: screen.LogFont, screen: layout), "gpu_viewhouse_inventory.png");
            Assert.True(screen.HasLightPixel(shot, px + 24, px + 120, py + 6 + 8), "no item on the storage's first row (inventory side)");
            Assert.True(screen.HasLightPixel(shot, px + 182 + 24, px + 182 + 120, py + 6 + 8), "no item on the inventory's first row (inventory side)");
        }
    }
}
