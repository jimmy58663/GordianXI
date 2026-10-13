// tests/Gordian.App.Tests/Graphics/StockUiKeyItemWindowRenderTests.cs
using System.Collections.Generic;
using System.Linq;
using Gordian.App.Graphics;
using Gordian.Core.Network.Packets;
using Gordian.Core.Ui;
using Gordian.Core.World;
using Xunit;

namespace Gordian.App.Tests.Graphics
{
    /// <summary>Offscreen D3D11 renders of the Key Items window (#91) and the Equipment Sets windows (#92) on the retail DAT frames.</summary>
    public class StockUiKeyItemWindowRenderTests
    {
        [Fact]
        public void RendersKeyItemsListWithDescription_AndTheEquipmentSetWindows()
        {
            using var screen = StockUiOffscreen.TryCreate();
            if (screen == null) return;

            var catalog = StockUiKeyItemCatalog.FromTable(screen.Resources.GetDMsgTable(Gordian.Core.Resources.Models.DMsgCategory.KeyItems));
            Assert.NotNull(catalog);
            var held = new HashSet<ushort> { 8, 3072 };
            var menus = new StockUiMenuController
            {
                Library = screen.Library,
                KeyItemCatalog = () => catalog,
                KeyItemHeld = held.Contains,
                KeyItemSeen = id => id != 8,
                MenuHelpText = id => screen.Resources.TryGetString(Gordian.Core.Resources.Models.DMsgCategory.MenuHelp, id, out var t) ? t : null,
            };
            var layout = new StockUiScreen(screen.Width, screen.Height, null);
            var categories = menus.OpenKeyItems(null);
            Assert.NotNull(categories);
            var frame = categories!.Menu.Frame;
            var placement = StockUiLayout.Place(frame.Anchor, frame.X, frame.Y, frame.Width, frame.Height, 1, screen.Width, screen.Height);
            int px = (int)placement.X, py = (int)placement.Y;
            byte[] cats = screen.Render(r => StockUiMenuWindow.Draw(r, screen.Library, screen.Font, categories, placement, 0, logFont: screen.LogFont, screen: layout), "gpu_key_item_categories.png");
            Assert.True(screen.HasLightPixel(cats, px + 40, px + 200, py + 22 + 8), "no category name on the second row");

            // The category holding the airship pass (a new key item): its list, and the description at the bottom left.
            int index = -1;
            for (int i = 0; i < catalog!.Categories.Count; i++)
            {
                if (catalog.Categories[i].Ids.Contains((ushort)8)) index = i;
            }
            Assert.True(index >= 0);
            categories.SelectedButtonId = index + 1;
            menus.Activate();
            var list = menus.Top!;
            Assert.True(list.IsKeyItemList);
            byte[] items = screen.Render(r => StockUiMenuWindow.Draw(r, screen.Library, screen.Font, list, placement, 0, logFont: screen.LogFont, screen: layout), "gpu_key_items.png");
            Assert.True(screen.HasLightPixel(items, px + 8, px + 300, (int)screen.Height - 16 - 24), "no description at the bottom");

            // Equipment sets: the list with the names over the capsules, then a set's options with its grid beside them.
            var inventory = new InventoryState();
            inventory.SetItem(ContainerId.Inventory, 3, 12448, 1, ItemLockFlag.Normal); // Bronze Cap
            var book = new EquipmentSetBook();
            book.Set(1, new EquipmentSet("BrdLock").WithPiece(EquipSlotId.Head, EquipmentSetPiece.Item(ContainerId.Inventory, 3, 12448))
                .WithPiece(EquipSlotId.Sub, EquipmentSetPiece.Remove));
            book.Set(2, new EquipmentSet("Melee").WithPiece(EquipSlotId.Head, EquipmentSetPiece.Item(ContainerId.Inventory, 3, 12448)));
            var sets = new StockUiMenuController
            {
                Library = screen.Library,
                Inventory = inventory,
                ItemLookup = id => screen.Resources.TryGetItem(id, out var record) ? record : null,
                EquipSets = () => book,
            };
            var setList = sets.OpenEquipmentSets(null);
            Assert.NotNull(setList);
            var lf = setList!.Menu.Frame;
            var lp = StockUiLayout.Place(lf.Anchor, lf.X, lf.Y, lf.Width, lf.Height, 1, screen.Width, screen.Height);
            byte[] listShot = screen.Render(r => StockUiMenuWindow.Draw(r, screen.Library, screen.Font, setList, lp, 0, logFont: screen.LogFont, screen: layout), "gpu_equip_set_list.png");
            Assert.True(screen.HasLightPixel(listShot, (int)lp.X + 24, (int)lp.X + 90, (int)lp.Y + 22 + 8), "no set name on the second capsule");

            sets.Activate();
            var options = sets.Top!;
            Assert.True(options.IsEquipSetOptions);
            var of = options.Menu.Frame;
            var op = StockUiLayout.Place(of.Anchor, of.X, of.Y, of.Width, of.Height, 1, screen.Width, screen.Height);
            byte[] optionsShot = screen.Render(r =>
            {
                StockUiMenuWindow.Draw(r, screen.Library, screen.Font, setList, lp, 0, logFont: screen.LogFont, screen: layout);
                StockUiMenuWindow.Draw(r, screen.Library, screen.Font, options, op, 0, logFont: screen.LogFont, screen: layout);
            }, "gpu_equip_set_options.png");
            // The grid beside the options (mcresed at 16,48): Head (button 2 at 24,45) holds the cap's icon, unlike Body below it.
            int gx = (int)op.X + (16 - of.X), gy = (int)op.Y + (48 - of.Y);
            var icon = screen.Pixel(optionsShot, gx + 24 + 16, gy + 45 + 16);
            var empty = screen.Pixel(optionsShot, gx + 24 + 16, gy + 79 + 16);
            Assert.True(System.Math.Abs(icon.R - empty.R) + System.Math.Abs(icon.G - empty.G) + System.Math.Abs(icon.B - empty.B) > 30, $"no icon in Head: {icon} vs {empty}");
        }
    }
}
