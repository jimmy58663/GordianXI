// tests/Gordian.App.Tests/Graphics/StockUiMogHouseRenderTests.cs
using System;
using System.Threading.Tasks;
using Gordian.App.Graphics;
using Gordian.Core.Network.Packets;
using Gordian.Core.World;
using Gordian.Engine.Ui;
using Xunit;

namespace Gordian.App.Tests.Graphics
{
    /// <summary>Offscreen D3D11 renders of the Mog House menus (#93) on the retail DAT frames and the layout grid.</summary>
    public class StockUiMogHouseRenderTests
    {
        private sealed class NoHouse : IStockUiMogHouse
        {
            public bool OnSecondFloor => false;
            public Task PlaceAsync(FurniturePlacement piece, bool secondFloor, byte x, byte level, byte z, byte rotation) => Task.CompletedTask;
            public Task FinishLayoutAsync() => Task.CompletedTask;
            public Task ClearAwayAsync(FurniturePlacement piece) => Task.CompletedTask;
            public Task SetHouseOpenAsync(bool open) => Task.CompletedTask;
            public Task RemodelAsync(ushort style) => Task.CompletedTask;
            public Task MannequinAsync(SubcontainerKind kind, FurniturePlacement mannequin, SubcontainerSlotIndex slot, byte storageSlot) => Task.CompletedTask;
        }

        [Fact]
        public void RendersTheMoogleMenu_TheLayoutGrid_AndAPieceMenu()
        {
            using var screen = StockUiOffscreen.TryCreate();
            if (screen == null) return;
            var inventory = new InventoryState();
            var ext = new byte[24];
            ext[1] = 0x40; // placed
            ext[6] = 2;
            ext[8] = 20;
            inventory.SetItemAttributes(ContainerId.MogSafe, 1, 2, 1, 0, ItemLockFlag.Normal, ext); // Simple Bed at (2, 20)
            var menus = new StockUiMenuController
            {
                Library = screen.Library,
                Inventory = inventory,
                ItemLookup = id => screen.Resources.TryGetItem(id, out var record) ? record : null,
                MogHouse = new NoHouse(),
                InMogHouse = () => true,
            };
            var layout = new StockUiScreen(screen.Width, screen.Height, null);

            Assert.True(menus.OpenMoogleMenu());
            var moogle = menus.Top!;
            var f = moogle.Menu.Frame;
            var placement = StockUiLayout.Place(f.Anchor, f.X, f.Y, f.Width, f.Height, 1, screen.Width, screen.Height);
            byte[] shot = screen.Render(r => StockUiMenuWindow.Draw(r, screen.Library, screen.Font, moogle, placement, 0, logFont: screen.LogFont, screen: layout), "gpu_moghouse_myroom.png");
            // The labels are the DAT's own sprites ("Storage" ... "Remodel"): light text on the third row, Change Jobs.
            Assert.True(screen.HasLightPixel(shot, (int)placement.X + 24, (int)placement.X + 100, (int)placement.Y + 37 + 8), "no label on myroom's third row");

            moogle.SelectedButtonId = 5; // Layout
            menus.Activate();
            Assert.True(menus.MogHouseLayout.Active);
            menus.MoveLayoutCursor(2 - menus.MogHouseLayout.CursorX, 20 - menus.MogHouseLayout.CursorZ);
            menus.ConfirmLayoutCell();
            var piece = menus.Top!;
            Assert.Equal(StockUiMogHouse.LayoutPieceMenu, piece.Name);
            var pf = piece.Menu.Frame;
            var pieceAt = StockUiLayout.Place(pf.Anchor, pf.X, pf.Y, pf.Width, pf.Height, 1, screen.Width, screen.Height);
            shot = screen.Render(r =>
            {
                StockUiMogHouseLayoutWindow.Draw(r, screen.Font, menus.MogHouseLayout, 1, screen.Width, screen.Height);
                StockUiMenuWindow.Draw(r, screen.Library, screen.Font, piece, pieceAt, 0, logFont: screen.LogFont, screen: layout);
            }, "gpu_moghouse_layout.png");
            // The grid at the top right: the cursor's light outline over the bed's cell (column 2, row 20 of 24).
            float gridLeft = screen.Width - 16 - (20 * 7 + 12) + 6, gridBottom = 48 + 24 * 7 + 12 + 14 - 6;
            int cy = (int)(gridBottom - 21 * 7);
            Assert.True(screen.HasLightPixel(shot, (int)gridLeft + 14, (int)gridLeft + 21, cy, 150), "no cursor outline on the grid");

            // The other layout menus and the away menu, each over its authored place.
            menus.CloseAll();
            foreach (string name in new[] { StockUiMogHouse.LayoutFloorMenu, StockUiMogHouse.ClearAllMenu, StockUiMogHouse.LayoutMannequinMenu, StockUiMogHouse.AwayMoogleMenu })
            {
                menus.CloseAll();
                Assert.True(menus.Open(name));
                var menu = menus.Top!;
                var mf = menu.Menu.Frame;
                var at = StockUiLayout.Place(mf.Anchor, mf.X, mf.Y, mf.Width, mf.Height, 1, screen.Width, screen.Height);
                screen.Render(r => StockUiMenuWindow.Draw(r, screen.Library, screen.Font, menu, at, 0, logFont: screen.LogFont, screen: layout), $"gpu_moghouse_{name}.png");
            }
        }
    }
}
