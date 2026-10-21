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
            // No grid until Edit (round 2): the screen's left is the clear colour where the grid's Head slot would be.
            var clear = screen.Pixel(optionsShot, 2, 2);
            Assert.Equal(clear, screen.Pixel(optionsShot, 16 + 24 + 16, 48 + 45 + 16));

            // Change Name on set 2 (image-16): the name typed in its capsule, scrolled to its end.
            sets.Top!.SelectedButtonId = StockUiEquipSets.ChangeNameButton;
            sets.CloseTop();
            setList.SelectedButtonId = 2;
            sets.Activate();
            sets.Top!.SelectedButtonId = StockUiEquipSets.ChangeNameButton;
            sets.Activate();
            Assert.True(sets.IsEditingName);
            sets.MacroField.InsertText("Long");
            byte[] renameShot = All("gpu_equip_set_rename.png");
            Assert.True(screen.HasLightPixel(renameShot, (int)lp.X + 20, (int)lp.X + 100, (int)lp.Y + 22 + 8), "no name in the edited capsule");
            sets.MacroField.Cancel();
            sets.Top!.SelectedButtonId = 1;
            sets.Activate();

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

        /// <summary>Whether any light pixel lies right of a window, on its rows (text running out of it).</summary>
        private static bool SpillsRight(StockUiOffscreen screen, byte[] shot, StockUiPlacement at, Gordian.Core.Resources.Ui.UiMenuFrame frame)
        {
            int right = (int)(at.X + frame.Width * at.Scale) + 2;
            for (int y = (int)at.Y; y < (int)(at.Y + frame.Height * at.Scale); y++)
            {
                if (screen.HasLightPixel(shot, right, (int)screen.Width - 1, y)) return true;
            }
            return false;
        }

        [Fact]
        public void LongPromptsStayInsideTheirWindows()
        {
            using var screen = StockUiOffscreen.TryCreate();
            if (screen == null) return;
            var layout = new StockUiScreen(screen.Width, screen.Height, null);
            // The equipment set prompts (ROM/165/72 #277-279) in the comyn yes / no window (round 3: Paste's ran out of it).
            foreach (int id in new[] { StockUiEquipSets.PastePromptText, StockUiEquipSets.DeletePromptText, StockUiEquipSets.UseCurrentPromptText })
            {
                var menus = new StockUiMenuController { Library = screen.Library };
                Assert.True(screen.Resources.TryGetString(DMsgCategory.MenuMessages, id, out var text));
                var prompt = menus.OpenYesNo(text, defaultYes: false, _ => { });
                Assert.NotNull(prompt);
                var f = prompt!.Menu.Frame;
                var at = StockUiLayout.Place(f.Anchor, f.X, f.Y, f.Width, f.Height, 1, screen.Width, screen.Height);
                byte[] shot = screen.Render(r => StockUiMenuWindow.Draw(r, screen.Library, screen.Font, prompt, at, 0, logFont: screen.LogFont, screen: layout), $"gpu_set_prompt_{id}.png");
                Assert.False(SpillsRight(screen, shot, at, f), $"prompt {id} runs out of its window");
                Assert.True(screen.HasLightPixel(shot, (int)at.X + 8, (int)(at.X + f.Width) - 8, (int)at.Y + 30), $"no text in prompt {id}");
            }

            // "Examine ...?" with the longest key item name in the table.
            var catalog = StockUiKeyItemCatalog.FromTable(screen.Resources.GetDMsgTable(DMsgCategory.KeyItems))!;
            var longest = catalog.Categories.SelectMany(c => c.Ids).Select(id => catalog.TryGetName(id, out var n) ? (id, n) : (id, string.Empty))
                .OrderByDescending(p => screen.LogFont!.MeasureWidth(p.Item2)).First();
            var keyMenus = new StockUiMenuController
            {
                Library = screen.Library,
                KeyItemCatalog = () => catalog,
                KeyItemHeld = id => id == longest.id,
                KeyItemSeen = _ => false,
                MenuMessageText = id => screen.Resources.TryGetString(DMsgCategory.MenuMessages, id, out var t) ? t : null,
            };
            var categories = keyMenus.OpenKeyItems(null)!;
            int index = catalog.Categories.ToList().FindIndex(c => c.Ids.Contains(longest.id));
            categories.FirstRow = index / StockUiKeyItems.VisibleRows * StockUiKeyItems.VisibleRows;
            categories.SelectedButtonId = index - categories.FirstRow + 1;
            keyMenus.Activate();
            keyMenus.Activate();
            var examine = keyMenus.Top!;
            Assert.True(examine.IsKeyItemExamine);
            var ef = examine.Menu.Frame;
            var ep = StockUiLayout.Place(ef.Anchor, ef.X, ef.Y, ef.Width, ef.Height, 1, screen.Width, screen.Height);
            byte[] examineShot = screen.Render(r => StockUiMenuWindow.Draw(r, screen.Library, screen.Font, examine, ep, 0, logFont: screen.LogFont, screen: layout), "gpu_key_item_examine_long.png");
            Assert.False(SpillsRight(screen, examineShot, ep, ef), $"\"{examine.KeyItemQuestion}\" runs out of its window");
        }    }
}
