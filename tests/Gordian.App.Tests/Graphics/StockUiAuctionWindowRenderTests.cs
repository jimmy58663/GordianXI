// tests/Gordian.App.Tests/Graphics/StockUiAuctionWindowRenderTests.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Gordian.App.Graphics;
using Gordian.Core.Network.Packets;
using Gordian.Core.Network.Search;
using Gordian.Core.World;
using Gordian.Engine.Ui;
using Xunit;

namespace Gordian.App.Tests.Graphics
{
    /// <summary>
    /// Offscreen D3D11 renders of the Auction House windows (#90) on the retail DAT frames: a category list with its item info
    /// under it, an item's Price History and the Price Set field with "Current Gil" beside it (GORDIAN_UI_DUMP writes
    /// gpu_auction_list.png, gpu_auction_history.png and gpu_auction_price.png).
    /// </summary>
    public class StockUiAuctionWindowRenderTests
    {
        [Fact]
        public void RendersTheCategoryList_TheItemInfo_AndThePrice()
        {
            using var screen = StockUiOffscreen.TryCreate();
            if (screen == null) return;

            var inventory = new InventoryState();
            inventory.SetItem(ContainerId.Inventory, 0, InventoryState.GilItemId, 50000, ItemLockFlag.Normal);
            var menus = new StockUiMenuController
            {
                Library = screen.Library,
                Inventory = inventory,
                ItemLookup = id => screen.Resources.TryGetItem(id, out var record) ? record : null,
                AuctionHouse = () => inventory.AuctionHouse,
                AuctionHistoryRequested = (_, _) => Task.FromResult<IReadOnlyList<AuctionHistoryEntry>?>(new[]
                {
                    new AuctionHistoryEntry(10, 1_760_685_476, "Gemini", "Gordian"),
                }),
                AuctionListRequested = _ => Task.FromResult<IReadOnlyList<AuctionListItem>?>(new[]
                {
                    new AuctionListItem(4096, 4, 7), // Fire Crystal: a single row and a stack row
                    new AuctionListItem(16449, 2, 0), // Bronze Dagger
                }),
            };
            inventory.AuctionHouse.Opened += menus.OnAuctionOpened;
            inventory.SetAuctionResponse(new AuctionResponse(AuctionCommand.Open, -1, 1, 0, 0, 0, 0, 0, 0, string.Empty));
            menus.Top!.SelectedButtonId = StockUiAuction.BidButton;
            menus.Activate();
            menus.Top!.SelectedButtonId = 8; // Crystals
            menus.Activate();
            var list = menus.Top!;
            Assert.True(SpinWait.SpinUntil(() => list.AuctionRows.Count == 3, TimeSpan.FromSeconds(10)));

            var frame = list.Menu.Frame;
            var placement = StockUiLayout.Place(frame.Anchor, frame.X, frame.Y, frame.Width, frame.Height, 1, screen.Width, screen.Height);
            int lx = (int)placement.X, ly = (int)placement.Y;
            byte[] pixels = screen.Render(r => StockUiMenuWindow.Draw(r, screen.Library, screen.Font, list, placement, 0, logFont: screen.LogFont,
                screen: new StockUiScreen(screen.Width, screen.Height, null)), "gpu_auction_list.png");

            // The second row's name after the icon slot (the first is selected, orange), the listing count right-aligned, the crystal described under the list.
            Assert.True(screen.HasLightPixel(pixels, lx + 24, lx + 120, ly + 41 + 8), "no name on the third row");
            Assert.True(screen.HasLightPixel(pixels, lx + frame.Width - 30, lx + frame.Width - 8, ly + 23 + 8), "no listing count");
            Assert.True(screen.HasLightPixel(pixels, lx + 48, lx + 200, ly + 192 + 10), "no item info under the list");

            // The single's Price History: date, seller, arrow, buyer and price in the small text, the item under it.
            menus.Activate();
            menus.Top!.SelectedButtonId = StockUiAuction.ItemHistoryButton;
            menus.Activate();
            var history = menus.Top!;
            Assert.True(SpinWait.SpinUntil(() => history.AuctionHistory.Count == 1, TimeSpan.FromSeconds(10)));
            byte[] historyPixels = screen.Render(r => StockUiMenuWindow.Draw(r, screen.Library, screen.Font, history, placement, 0, logFont: screen.LogFont,
                screen: new StockUiScreen(screen.Width, screen.Height, null)), "gpu_auction_history.png");
            Assert.True(screen.HasLightPixel(historyPixels, lx + 4, lx + 60, ly + 5 + 8), "no date");
            Assert.True(screen.HasLightPixel(historyPixels, lx + 174, lx + 230, ly + 5 + 8), "no buyer");
            Assert.True(screen.HasLightPixel(historyPixels, lx + frame.Width - 60, lx + frame.Width - 16, ly + 5 + 8), "no price");
            Assert.True(screen.HasLightPixel(historyPixels, lx + 48, lx + 200, ly + 192 + 10), "no item info under the history");
            menus.CloseTop();

            // Bid on the stack row: the Price Set field with the typed price.
            list.SelectedButtonId = 2;
            menus.Activate();
            menus.Top!.SelectedButtonId = StockUiAuction.ItemBidButton;
            menus.Activate();
            var price = menus.Top!;
            Assert.Equal(StockUiAuctionView.Price, price.AuctionView);
            menus.MacroField.InsertText("12000");
            var pf = price.Menu.Frame;
            var pricePlacement = StockUiLayout.Place(pf.Anchor, pf.X, pf.Y, pf.Width, pf.Height, 1, screen.Width, screen.Height);
            int px = (int)pricePlacement.X, py = (int)pricePlacement.Y;
            byte[] pricePixels = screen.Render(r => StockUiMenuWindow.Draw(r, screen.Library, screen.Font, price, pricePlacement, 0, logFont: screen.LogFont,
                screen: new StockUiScreen(screen.Width, screen.Height, null)), "gpu_auction_price.png");
            Assert.True(screen.HasLightPixel(pricePixels, px + 60, px + 112, py + 29), "no price in the field");
            Assert.True(screen.HasLightPixel(pricePixels, px + 127, px + 230, py + 41 + 5), "no cap under the field");
            Assert.True(screen.HasLightPixel(pricePixels, px - 114 + 30, px - 114 + 104, py + 30 + 5), "no Current Gil beside the field");
            menus.MacroField.Cancel();
        }
    }
}
