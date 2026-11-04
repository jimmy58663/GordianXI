// tests/Gordian.App.Tests/Graphics/StockUiMogHouseRenderTests.cs
using System;
using System.Numerics;
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
            inventory.SetContainerSizes(ContainerId.MogSafe, 80, 80);
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
            menus.MogHouseLayout.InfoOf = screen.Resources.GetFurnitureInfo;
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
            menus.ConfirmLayoutCell();
            var piece = menus.Top!;
            Assert.Equal(StockUiMogHouse.LayoutPieceMenu, piece.Name);
            piece.SelectedButtonId = 1; // Rearrange
            menus.Activate();
            Assert.NotNull(menus.MogHouseLayout.Held);

            // The grid and footprint in the camera's view: the Simple Bed is 4 x 6 cells at (2, 20)... clamped to the rows.
            var camera = new Gordian.Engine.Graphics.ViewportCamera { AspectRatio = screen.Width / (float)screen.Height };
            camera.SetLookAt(new Vector3(0, 9.16f, 0.53f), Vector3.Zero, Vector3.UnitZ);
            var view = camera.ViewProjectionMatrix;
            byte[] shot2 = screen.Render(r => StockUiMogHouseLayoutWindow.Draw(r, screen.Library, menus.MogHouseLayout, view, _ => true, 1, screen.Width, screen.Height, 0), "gpu_moghouse_layout.png");
            int changed = 0;
            byte[] blank = screen.Render(_ => { });
            for (int i = 0; i < shot2.Length; i += 4) if (shot2[i] != blank[i] || shot2[i + 1] != blank[i + 1]) changed++;
            Assert.True(changed > 2000, $"only {changed} pixels of grid drawn");
            menus.CancelLayout();

            // The other layout menus and the away menu, each over its authored place.
            menus.CloseAll();
            foreach (string name in new[] { StockUiMogHouse.LayoutFloorMenu, StockUiMogHouse.ClearAllMenu, StockUiMogHouse.LayoutMannequinMenu, StockUiMogHouse.AwayMoogleMenu, StockUiMogHouse.SafeMenu, StockUiMenuEntries.MainMenu })
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
