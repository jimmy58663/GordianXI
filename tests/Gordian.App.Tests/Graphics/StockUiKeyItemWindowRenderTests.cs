// tests/Gordian.App.Tests/Graphics/StockUiKeyItemWindowRenderTests.cs
using System.Collections.Generic;
using System.Linq;
using Gordian.App.Graphics;
using Gordian.Core.Network.Packets;
using Gordian.Core.Resources.Models;
using Gordian.Core.Ui;
using Gordian.Core.World;
using Xunit;

namespace Gordian.App.Tests.Graphics
{
    /// <summary>
    /// Offscreen D3D11 renders of the Key Items window (#91) and the Equipment Sets windows (#92) on the retail DAT frames,
    /// laid out as the maintainer's retail captures 2026-10-13 (image-6 ... image-14). GORDIAN_UI_DUMP writes the frames.
    /// </summary>
    public class StockUiKeyItemWindowRenderTests
    {
        [Fact]
        public void RendersKeyItemsCategoriesListExamineAndDescription()
        {
            using var screen = StockUiOffscreen.TryCreate();
            if (screen == null) return;

            var catalog = StockUiKeyItemCatalog.FromTable(screen.Resources.GetDMsgTable(DMsgCategory.KeyItems));
            Assert.NotNull(catalog);
            var held = new HashSet<ushort> { 8, 3072 };
            var seen = new HashSet<ushort> { 3072 };
            var menus = new StockUiMenuController
            {
                Library = screen.Library,
                KeyItemCatalog = () => catalog,
                KeyItemHeld = held.Contains,
                KeyItemSeen = seen.Contains,
                KeyItemViewed = id => seen.Add(id),
                MenuHelpText = id => screen.Resources.TryGetString(DMsgCategory.MenuHelp, id, out var t) ? t : null,
                MenuMessageText = id => screen.Resources.TryGetString(DMsgCategory.MenuMessages, id, out var t) ? t : null,
            };
            var layout = new StockUiScreen(screen.Width, screen.Height, null);
            var categories = menus.OpenKeyItems(null);
            Assert.NotNull(categories);
            var frame = categories!.Menu.Frame;
            var placement = StockUiLayout.Place(frame.Anchor, frame.X, frame.Y, frame.Width, frame.Height, 1, screen.Width, screen.Height);
            int px = (int)placement.X, py = (int)placement.Y;
            byte[] Shot(string name) => screen.Render(r => StockUiMenuWindow.Draw(r, screen.Library, screen.Font, menus.Top!, placement, 0, logFont: screen.LogFont, screen: layout), name);

            byte[] cats = Shot("gpu_key_item_categories.png");
            // The second row (image-6: y 13 + 29): its name 35 px into the row.
            Assert.True(screen.HasLightPixel(cats, px + 40 + 35, px + 250, py + 42 + 9), "no category name on the second row");

            // The category holding the airship pass (a new key item, the big bullet), then Examine, then the description.
            int index = catalog!.Categories.ToList().FindIndex(c => c.Ids.Contains((ushort)8));
            Assert.True(index >= 0);
            categories.FirstRow = index / StockUiKeyItems.VisibleRows * StockUiKeyItems.VisibleRows;
            categories.SelectedButtonId = index - categories.FirstRow + 1;
            menus.Activate();
            Assert.True(menus.Top!.IsKeyItemList);
            Shot("gpu_key_items.png");
            menus.Activate();
            Assert.True(menus.Top!.IsKeyItemExamine);
            byte[] examine = Shot("gpu_key_item_examine.png");
            Assert.True(screen.HasLightPixel(examine, px + 28, px + 300, py + 32 + 8), "no Examine question");
            menus.Activate();
            Assert.True(menus.Top!.IsKeyItemDescription);
            byte[] description = Shot("gpu_key_item_description.png");
            Assert.True(screen.HasLightPixel(description, px + 51, px + 200, py + 18 + 8), "no name on the description");
            Assert.True(screen.HasLightPixel(description, px + 42, px + 340, py + 49 + 8), "no description text");
            Assert.Contains((ushort)8, seen);
        }

        [Fact]
        public void RendersTheEquipmentSetListOptionsAndEditor()
        {
            using var screen = StockUiOffscreen.TryCreate();
            if (screen == null) return;

            var inventory = new InventoryState();
            inventory.SetContainerSizes(ContainerId.Inventory, 80, 80);
            inventory.SetItem(ContainerId.Inventory, 3, 12448, 1, ItemLockFlag.Normal); // Bronze Cap
            inventory.SetItem(ContainerId.Inventory, 4, 16535, 1, ItemLockFlag.Normal); // Bronze Sword
            var book = new EquipmentSetBook();
            book.Set(1, new EquipmentSet("BrdLock").WithPiece(EquipSlotId.Head, EquipmentSetPiece.Item(ContainerId.Inventory, 3, 12448))
                .WithPiece(EquipSlotId.Sub, EquipmentSetPiece.Remove));
            book.Set(2, new EquipmentSet("BehavResearch"));
            var sets = new StockUiMenuController
            {
                Library = screen.Library,
                Inventory = inventory,
                ItemLookup = id => screen.Resources.TryGetItem(id, out var record) ? record : null,
                EquipSets = () => book,
                MenuHelpText = id => screen.Resources.TryGetString(DMsgCategory.MenuHelp, id, out var t) ? t : null,
                MenuMessageText = id => screen.Resources.TryGetString(DMsgCategory.MenuMessages, id, out var t) ? t : null,
            };
            var layout = new StockUiScreen(screen.Width, screen.Height, null);
            var setList = sets.OpenEquipmentSets(null);
            Assert.NotNull(setList);
            StockUiPlacement Place(StockUiOpenMenu m)
            {
                var f = m.Menu.Frame;
                return StockUiLayout.Place(f.Anchor, f.X, f.Y, f.Width, f.Height, 1, screen.Width, screen.Height);
            }
            byte[] All(string name) => screen.Render(r =>
            {
                foreach (var m in sets.OpenMenus) StockUiMenuWindow.Draw(r, screen.Library, screen.Font, m, Place(m), 0, logFont: screen.LogFont, screen: layout);
            }, name);

            var lp = Place(setList!);
            byte[] listShot = All("gpu_equip_set_list.png");
            // Set002 (an empty set) is white too (image-11).
            Assert.True(screen.HasLightPixel(listShot, (int)lp.X + 20, (int)lp.X + 90, (int)lp.Y + 22 + 8), "no set name on the second capsule");

            sets.Activate();
            var options = sets.Top!;
            Assert.True(options.IsEquipSetOptions);
            byte[] optionsShot = All("gpu_equip_set_options.png");
            var op = Place(options);
            // The grid at its own place (16,48 top left), the options right against the list at the top right.
            Assert.Equal((int)lp.X - 112, (int)op.X);
            int gx = 16, gy = 48;
            var icon = screen.Pixel(optionsShot, gx + 24 + 16, gy + 45 + 16);
            var empty = screen.Pixel(optionsShot, gx + 24 + 16, gy + 79 + 16);
            Assert.True(System.Math.Abs(icon.R - empty.R) + System.Math.Abs(icon.G - empty.G) + System.Math.Abs(icon.B - empty.B) > 30, $"no icon in Head: {icon} vs {empty}");

            // Edit (image-12): the grid, the storage's equipment right of it and the slot's info under it.
            sets.Activate();
            var grid = sets.Top!;
            Assert.True(grid.IsEquipSetGrid);
            grid.SelectedButtonId = StockUiCheck.ButtonOf(EquipSlotId.Head);
            byte[] editShot = All("gpu_equip_set_edit.png");
            var gp = Place(grid);
            Assert.True(screen.HasLightPixel(editShot, (int)gp.X + 182 + 20, (int)gp.X + 182 + 140, (int)gp.Y + 6 + 18 * 3 + 8), "no item row beside the grid");
            Assert.True(screen.HasLightPixel(editShot, (int)gp.X + 48, (int)gp.X + 300, (int)gp.Y + 192 + 8), "no item info under the grid");
        }
    }
}
