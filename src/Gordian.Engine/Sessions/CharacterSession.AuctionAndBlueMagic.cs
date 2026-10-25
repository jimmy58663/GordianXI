// src/Gordian.Engine/Sessions/CharacterSession.AuctionAndBlueMagic.cs
using System.Collections.Generic;
using System.Threading.Tasks;
using Gordian.Core.Network.Packets;
using Gordian.Core.Network.Search;
using Gordian.Engine.Ui;

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
            // The counter talked to anchors the window: walking off past StockUiMenuController.AuctionReach closes it.
            auction.Opened += () => menus.SetAuctionAnchor(AuctionAnchor());
            auction.Opened += menus.OnAuctionOpened;
            auction.SalesChanged += menus.OnAuctionSalesChanged;
            auction.ResponseReceived += menus.OnAuctionResponse;
            ActionService.LocalPlayerMoved += (position, _) => menus.CheckAuctionDistance(position);
            World.ZoneChanged += _ => menus.CloseAuctionHouse();
        }

        /// <summary>Where the Auction House opened from: the targeted counter, else the character's place; null when neither is known.</summary>
        private System.Numerics.Vector3? AuctionAnchor()
        {
            if (ActionService.CurrentTarget is { Type: Core.World.EntityType.Npc } counter) return counter.Position;
            return World.TryGetByServerId(LocalPlayer.ServerId, out var self) ? self.Position : null;
        }

        /// <summary>
        /// Blue Magic's Set Spells window (#363): the slots come from S2C 0x044 (the main job's data, or the support job's when
        /// Blue Mage is the support job), the point bonus from S2C 0x063 (merits), the learned spells from S2C 0x0AA; sets and
        /// removals go out as C2S 0x102, and the server answers with a new S2C 0x044.
        /// </summary>
        private void AttachBlueMagic()
        {
            var menus = ActionService.Menus;
            menus.BlueMagicState = BlueMagicState;
            menus.BlueMagicCatalog = () => ActionService.Catalog;
            menus.BlueSpellSet = (slot, spell, subJob) => LoginDataModule.SendSetBlueSpellAsync(slot, spell, subJob);
            menus.BlueSpellRemove = (slot, spell, subJob) => LoginDataModule.SendRemoveBlueSpellAsync(slot, spell, subJob);
            LocalPlayer.ExtendedJobUpdated += menus.RefreshBlueMagic;
            LocalPlayer.MagicLearnedUpdated += menus.RefreshBlueMagic;
            Progression.MeritsUpdated += menus.RefreshBlueMagic;
        }

        private StockUiBlueMagicState? BlueMagicState()
        {
            var player = LocalPlayer;
            bool main = player.MainJob == JobId.BlueMage, sub = !main && player.SubJob == JobId.BlueMage;
            if (!main && !sub) return null;
            var data = main ? player.MainJobData : player.SubJobData;
            IReadOnlyList<byte> slots = data is { IsBlueMage: true } ? data.BlueSpells : new byte[StockUiBlueMagic.SlotCount];
            int level = main ? player.MainJobLevel : player.SubJobLevel;
            return new StockUiBlueMagicState(level, sub, slots, Progression.BluSpellPointBonus, player.HasSpell);
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
