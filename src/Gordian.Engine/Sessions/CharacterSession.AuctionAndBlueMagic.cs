// src/Gordian.Engine/Sessions/CharacterSession.AuctionAndBlueMagic.cs
using System.Collections.Generic;
using System.Threading.Tasks;
using Gordian.Core.Network.Search;

namespace Gordian.Engine.Sessions
{
    public sealed partial class CharacterSession
    {
        /// <summary>
        /// The Auction House window (#90): S2C 0x04C MOPEN opens it; its sends are C2S 0x04E (WorkCheck, Info, LotCheck,
        /// LotCancel, AskCommit, LotIn, Bid) through the inventory module; category lists and price histories come from the
        /// search server (<see cref="Search"/>). Every 0x04C answer is fed to the window after <see cref="Core.World.AuctionHouseState"/>
        /// took it in; a zone change closes the window.
        /// </summary>
        private void AttachAuctionHouse()
        {
            var menus = ActionService.Menus;
            var auction = Inventory.AuctionHouse;
            menus.AuctionHouse = () => auction;
            menus.AuctionOpenRequested = () => InventoryModule.OpenAuctionAsync();
            menus.AuctionSalesRequested = () => InventoryModule.RequestAuctionSalesAsync();
            menus.AuctionBid = (item, price, stack, slot) => InventoryModule.BidAuctionAsync(item, price, stack, slot);
            menus.AuctionAskCommit = (slot, item, price, stack) => InventoryModule.AskAuctionCommitAsync(slot, item, price, stack);
            menus.AuctionListForSale = (saleSlot, slot, price, stack) => InventoryModule.ListAuctionItemAsync(saleSlot, slot, price, stack);
            menus.AuctionCancelSale = slot => InventoryModule.CancelAuctionSaleAsync(slot);
            menus.AuctionListRequested = LoadAuctionListAsync;
            menus.AuctionHistoryRequested = LoadAuctionHistoryAsync;
            auction.Opened += menus.OnAuctionOpened;
            auction.SalesChanged += menus.OnAuctionSalesChanged;
            auction.ResponseReceived += menus.OnAuctionResponse;
            World.ZoneChanged += _ => menus.CloseAuctionHouse();
        }

        private async Task<IReadOnlyList<AuctionListItem>?> LoadAuctionListAsync(byte category)
        {
            if (!Search.IsConfigured) return null;
            var result = await Search.RefreshAuctionListAsync(category).ConfigureAwait(false);
            return result.Items;
        }

        private async Task<IReadOnlyList<AuctionHistoryEntry>?> LoadAuctionHistoryAsync(ushort itemId, bool stack)
        {
            if (!Search.IsConfigured) return null;
            var result = await Search.RefreshAuctionHistoryAsync(itemId, stack).ConfigureAwait(false);
            return result?.Sales;
        }
    }
}
