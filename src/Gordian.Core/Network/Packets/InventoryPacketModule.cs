// src/Gordian.Core/Network/Packets/InventoryPacketModule.cs
using System;
using System.Buffers;
using System.Threading.Tasks;
using Gordian.Core.Diagnostics;
using Gordian.Core.World;

namespace Gordian.Core.Network.Packets
{
    /// <summary>
    /// Domain module managing multi-container inventory, currencies, equipment sets,
    /// player/NPC trade sessions, NPC/guild shops, player bazaars, and the Auction House.
    /// Operates zero-allocation on inbound packet parsing.
    /// </summary>
    public sealed class InventoryPacketModule
    {
        private readonly InventoryState _inventoryState;
        private readonly LocalPlayerState _localPlayerState;
        private readonly Func<ReadOnlyMemory<byte>, bool, Task> _sendChunkCallback;
        private readonly Action<PacketDirection, ushort, ushort, ReadOnlySpan<byte>>? _logPacketCallback;
        private ushort _sequenceNumber;

        public InventoryState State => _inventoryState;
        public bool LogOutboundOnRoute { get; set; } = true;

        public InventoryPacketModule(
            InventoryState inventoryState,
            LocalPlayerState localPlayerState,
            Func<ReadOnlyMemory<byte>, bool, Task> sendChunkCallback,
            Action<PacketDirection, ushort, ushort, ReadOnlySpan<byte>>? logPacketCallback = null)
        {
            _inventoryState = inventoryState ?? throw new ArgumentNullException(nameof(inventoryState));
            _localPlayerState = localPlayerState ?? throw new ArgumentNullException(nameof(localPlayerState));
            _sendChunkCallback = sendChunkCallback ?? throw new ArgumentNullException(nameof(sendChunkCallback));
            _logPacketCallback = logPacketCallback;
        }

        public void Register(IPacketDispatcher dispatcher)
        {
            ArgumentNullException.ThrowIfNull(dispatcher);

            dispatcher.Register(S2C_0x01C_ItemMax.PacketId, HandleItemMax);
            dispatcher.Register(S2C_0x01D_ItemSame.PacketId, HandleItemSame);
            dispatcher.Register(S2C_0x01E_ItemNum.PacketId, HandleItemNum);
            dispatcher.Register(S2C_0x01F_ItemList.PacketId, HandleItemList);
            dispatcher.Register(S2C_0x020_ItemAttr.PacketId, HandleItemAttr);
            dispatcher.Register(S2C_0x021_ItemTradeReq.PacketId, HandleItemTradeReq);
            dispatcher.Register(S2C_0x022_ItemTradeRes.PacketId, HandleItemTradeRes);
            dispatcher.Register(S2C_0x023_ItemTradeList.PacketId, HandleItemTradeList);
            dispatcher.Register(S2C_0x025_ItemTradeMyList.PacketId, HandleItemTradeMyList);
            dispatcher.Register(S2C_0x026_ItemSubcontainer.PacketId, HandleItemSubcontainer);
            dispatcher.Register(S2C_0x03C_ShopList.PacketId, HandleShopList);
            dispatcher.Register(S2C_0x03D_ShopSell.PacketId, HandleShopSell);
            dispatcher.Register(S2C_0x03E_ShopOpen.PacketId, HandleShopOpen);
            dispatcher.Register(S2C_0x03F_ShopBuy.PacketId, HandleShopBuy);
            dispatcher.Register(S2C_0x04C_Auc.PacketId, HandleAuc);
            dispatcher.Register(S2C_0x050_EquipList.PacketId, HandleEquipList);
            dispatcher.Register(S2C_0x082_GuildBuy.PacketId, HandleGuildBuy);
            dispatcher.Register(S2C_0x083_GuildBuyList.PacketId, HandleGuildBuyList);
            dispatcher.Register(S2C_0x084_GuildSell.PacketId, HandleGuildSell);
            dispatcher.Register(S2C_0x085_GuildSellList.PacketId, HandleGuildSellList);
            dispatcher.Register(S2C_0x086_GuildOpen.PacketId, HandleGuildOpen);
            dispatcher.Register(S2C_0x105_BazaarList.PacketId, HandleBazaarList);
            dispatcher.Register(S2C_0x106_BazaarBuy.PacketId, HandleBazaarBuy);
            dispatcher.Register(S2C_0x107_BazaarClose.PacketId, HandleBazaarClose);
            dispatcher.Register(S2C_0x108_BazaarShopping.PacketId, HandleBazaarShopping);
            dispatcher.Register(S2C_0x109_BazaarSell.PacketId, HandleBazaarSell);
            dispatcher.Register(S2C_0x10A_BazaarSale.PacketId, HandleBazaarSale);
            dispatcher.Register(S2C_0x113_Currencies1.PacketId, HandleCurrencies1);
            dispatcher.Register(S2C_0x116_EquipsetValid.PacketId, HandleEquipsetValid);
            dispatcher.Register(S2C_0x117_EquipsetRes.PacketId, HandleEquipsetRes);
            dispatcher.Register(S2C_0x118_Currencies2.PacketId, HandleCurrencies2);
        }

        public void Unregister(IPacketDispatcher dispatcher)
        {
            ArgumentNullException.ThrowIfNull(dispatcher);

            dispatcher.Unregister(S2C_0x01C_ItemMax.PacketId);
            dispatcher.Unregister(S2C_0x01D_ItemSame.PacketId);
            dispatcher.Unregister(S2C_0x01E_ItemNum.PacketId);
            dispatcher.Unregister(S2C_0x01F_ItemList.PacketId);
            dispatcher.Unregister(S2C_0x020_ItemAttr.PacketId);
            dispatcher.Unregister(S2C_0x021_ItemTradeReq.PacketId);
            dispatcher.Unregister(S2C_0x022_ItemTradeRes.PacketId);
            dispatcher.Unregister(S2C_0x023_ItemTradeList.PacketId);
            dispatcher.Unregister(S2C_0x025_ItemTradeMyList.PacketId);
            dispatcher.Unregister(S2C_0x026_ItemSubcontainer.PacketId);
            dispatcher.Unregister(S2C_0x03C_ShopList.PacketId);
            dispatcher.Unregister(S2C_0x03D_ShopSell.PacketId);
            dispatcher.Unregister(S2C_0x03E_ShopOpen.PacketId);
            dispatcher.Unregister(S2C_0x03F_ShopBuy.PacketId);
            dispatcher.Unregister(S2C_0x04C_Auc.PacketId);
            dispatcher.Unregister(S2C_0x050_EquipList.PacketId);
            dispatcher.Unregister(S2C_0x082_GuildBuy.PacketId);
            dispatcher.Unregister(S2C_0x083_GuildBuyList.PacketId);
            dispatcher.Unregister(S2C_0x084_GuildSell.PacketId);
            dispatcher.Unregister(S2C_0x085_GuildSellList.PacketId);
            dispatcher.Unregister(S2C_0x086_GuildOpen.PacketId);
            dispatcher.Unregister(S2C_0x105_BazaarList.PacketId);
            dispatcher.Unregister(S2C_0x106_BazaarBuy.PacketId);
            dispatcher.Unregister(S2C_0x107_BazaarClose.PacketId);
            dispatcher.Unregister(S2C_0x108_BazaarShopping.PacketId);
            dispatcher.Unregister(S2C_0x109_BazaarSell.PacketId);
            dispatcher.Unregister(S2C_0x10A_BazaarSale.PacketId);
            dispatcher.Unregister(S2C_0x113_Currencies1.PacketId);
            dispatcher.Unregister(S2C_0x116_EquipsetValid.PacketId);
            dispatcher.Unregister(S2C_0x117_EquipsetRes.PacketId);
            dispatcher.Unregister(S2C_0x118_Currencies2.PacketId);
        }

        #region Inbound Handlers

        private void HandleItemMax(PacketHeader header, ReadOnlySpan<byte> payload)
        {
            var p = new S2C_0x01C_ItemMax(payload);
            if (!p.IsValid) return;

            for (int i = 0; i < S2C_0x01C_ItemMax.ContainerCount; i++)
            {
                var cid = (ContainerId)i;
                _inventoryState.SetContainerSizes(cid, p.GetMaxSize(cid), p.GetUsableSize(cid));
            }
        }

        private void HandleItemSame(PacketHeader header, ReadOnlySpan<byte> payload)
        {
            var p = new S2C_0x01D_ItemSame(payload);
            if (!p.IsValid) return;

            GordianLog.Info("INVENTORY", $"Container update sync: State={p.State}, Flags=0x{p.Flags:X8}");
        }

        private void HandleItemNum(PacketHeader header, ReadOnlySpan<byte> payload)
        {
            var p = new S2C_0x01E_ItemNum(payload);
            if (!p.IsValid) return;

            _inventoryState.UpdateItemCount(p.Container, p.Slot, p.Count, p.LockFlag);
        }

        private void HandleItemList(PacketHeader header, ReadOnlySpan<byte> payload)
        {
            var p = new S2C_0x01F_ItemList(payload);
            if (!p.IsValid) return;

            _inventoryState.SetItem(p.Container, p.Slot, p.ItemId, p.Count, p.LockFlag);
        }

        private void HandleItemAttr(PacketHeader header, ReadOnlySpan<byte> payload)
        {
            var p = new S2C_0x020_ItemAttr(payload);
            if (!p.IsValid) return;

            _inventoryState.SetItemAttributes(p.Container, p.Slot, p.ItemId, p.Count, p.Price, p.LockFlag, p.ExtData);
        }

        private void HandleItemTradeReq(PacketHeader header, ReadOnlySpan<byte> payload)
        {
            var p = new S2C_0x021_ItemTradeReq(payload);
            if (!p.IsValid) return;

            GordianLog.Info("TRADE", $"Trade request received from Entity ServerId={p.UniqueNo}, ActIndex={p.ActIndex}");
            _inventoryState.StartTrade(p.UniqueNo, p.ActIndex);
        }

        private void HandleItemTradeRes(PacketHeader header, ReadOnlySpan<byte> payload)
        {
            var p = new S2C_0x022_ItemTradeRes(payload);
            if (!p.IsValid) return;

            GordianLog.Info("TRADE", $"Trade status result: Kind={p.Kind}");
            _inventoryState.SetTradeStatus(p.Kind);
        }

        private void HandleItemTradeList(PacketHeader header, ReadOnlySpan<byte> payload)
        {
            var p = new S2C_0x023_ItemTradeList(payload);
            if (!p.IsValid) return;

            _inventoryState.SetPartnerTradeItem(p.TradeIndex, p.ItemId, p.Count, p.ExtData);
        }

        private void HandleItemTradeMyList(PacketHeader header, ReadOnlySpan<byte> payload)
        {
            var p = new S2C_0x025_ItemTradeMyList(payload);
            if (!p.IsValid) return;

            _inventoryState.SetMyTradeItem(p.TradeIndex, p.ItemId, p.Count, p.Slot);
        }

        private void HandleItemSubcontainer(PacketHeader header, ReadOnlySpan<byte> payload)
        {
            var p = new S2C_0x026_ItemSubcontainer(payload);
            if (!p.IsValid) return;

            GordianLog.Info("INVENTORY", $"Subcontainer item in {p.Container} slot {p.Slot}: Head={p.ModelIdHead}, Body={p.ModelIdBody}");
        }

        private void HandleShopList(PacketHeader header, ReadOnlySpan<byte> payload)
        {
            var p = new S2C_0x03C_ShopList(payload);
            if (!p.IsValid) return;

            var items = new ShopItemEntry[p.ItemCount];
            for (int i = 0; i < p.ItemCount; i++)
            {
                var item = p.GetItem(i);
                items[i] = new ShopItemEntry(item.Price, item.ItemId, item.ShopIndex, item.Skill, item.GuildInfo);
            }
            _inventoryState.AddShopItems(items);
        }

        private void HandleShopSell(PacketHeader header, ReadOnlySpan<byte> payload)
        {
            var p = new S2C_0x03D_ShopSell(payload);
            if (!p.IsValid) return;

            _inventoryState.SetAppraisal(p.Slot, p.Price);
        }

        private void HandleShopOpen(PacketHeader header, ReadOnlySpan<byte> payload)
        {
            var p = new S2C_0x03E_ShopOpen(payload);
            if (!p.IsValid) return;

            _inventoryState.OpenShop(p.ShopListNum);
        }

        private void HandleShopBuy(PacketHeader header, ReadOnlySpan<byte> payload)
        {
            var p = new S2C_0x03F_ShopBuy(payload);
            if (!p.IsValid) return;

            GordianLog.Info("SHOP", $"Purchased shop item {p.ShopItemIndex} x{p.Count}, Result={p.BuyState}");
        }

        private void HandleAuc(PacketHeader header, ReadOnlySpan<byte> payload)
        {
            var p = new S2C_0x04C_Auc(payload);
            if (!p.IsValid) return;

            GordianLog.Info("AUCTION", $"Auction response: Command={p.Command}, Result={p.Result}, Item={p.ItemId}, Price={p.Price}");
        }

        private void HandleEquipList(PacketHeader header, ReadOnlySpan<byte> payload)
        {
            var p = new S2C_0x050_EquipList(payload);
            if (!p.IsValid) return;

            _inventoryState.SetEquip(p.EquipSlot, p.Container, p.Slot);
        }

        private void HandleGuildBuy(PacketHeader header, ReadOnlySpan<byte> payload)
        {
            var p = new S2C_0x082_GuildBuy(payload);
            if (!p.IsValid) return;

            GordianLog.Info("GUILD", $"Guild buy confirmation: Item={p.ItemId}, Count={p.Count}");
        }

        private void HandleGuildBuyList(PacketHeader header, ReadOnlySpan<byte> payload)
        {
            var p = new S2C_0x083_GuildBuyList(payload);
            if (!p.IsValid) return;

            var items = new ShopItemEntry[p.Count];
            for (int i = 0; i < p.Count; i++)
            {
                var item = p.GetItem(i);
                items[i] = new ShopItemEntry((uint)Math.Max(0, item.Price), item.ItemId, (byte)i, 0, 0);
            }
            _inventoryState.AddShopItems(items);
        }

        private void HandleGuildSell(PacketHeader header, ReadOnlySpan<byte> payload)
        {
            var p = new S2C_0x084_GuildSell(payload);
            if (!p.IsValid) return;

            GordianLog.Info("GUILD", $"Guild sell confirmation: Item={p.ItemId}, Count={p.Count}");
        }

        private void HandleGuildSellList(PacketHeader header, ReadOnlySpan<byte> payload)
        {
            var p = new S2C_0x085_GuildSellList(payload);
            if (!p.IsValid) return;

            GordianLog.Info("GUILD", $"Guild accepted sales list populated with {p.Count} entries.");
        }

        private void HandleGuildOpen(PacketHeader header, ReadOnlySpan<byte> payload)
        {
            var p = new S2C_0x086_GuildOpen(payload);
            if (!p.IsValid) return;

            _inventoryState.SetGuildOpenStatus(p.Status);
        }

        private void HandleBazaarList(PacketHeader header, ReadOnlySpan<byte> payload)
        {
            var p = new S2C_0x105_BazaarList(payload);
            if (!p.IsValid) return;

            _inventoryState.AddBazaarItem(new BazaarItemEntry(
                p.Price,
                p.Count,
                p.TaxRate,
                p.ItemId,
                p.ItemIndex,
                p.ExtData.ToArray()
            ));
        }

        private void HandleBazaarBuy(PacketHeader header, ReadOnlySpan<byte> payload)
        {
            var p = new S2C_0x106_BazaarBuy(payload);
            if (!p.IsValid) return;

            GordianLog.Info("BAZAAR", $"Bazaar purchase: State={p.State}, Seller={p.TargetName}");
        }

        private void HandleBazaarClose(PacketHeader header, ReadOnlySpan<byte> payload)
        {
            var p = new S2C_0x107_BazaarClose(payload);
            if (!p.IsValid) return;

            GordianLog.Info("BAZAAR", $"Bazaar closed by seller {p.SellerName}");
            _inventoryState.StopViewingBazaar();
        }

        private void HandleBazaarShopping(PacketHeader header, ReadOnlySpan<byte> payload)
        {
            var p = new S2C_0x108_BazaarShopping(payload);
            if (!p.IsValid) return;

            GordianLog.Info("BAZAAR", $"Visitor {p.BuyerName} {p.State} local bazaar.");
        }

        private void HandleBazaarSell(PacketHeader header, ReadOnlySpan<byte> payload)
        {
            var p = new S2C_0x109_BazaarSell(payload);
            if (!p.IsValid) return;

            GordianLog.Info("BAZAAR", $"{p.BuyerName} bought item in slot {p.Slot} x{p.Count} from personal bazaar.");
        }

        private void HandleBazaarSale(PacketHeader header, ReadOnlySpan<byte> payload)
        {
            var p = new S2C_0x10A_BazaarSale(payload);
            if (!p.IsValid) return;

            GordianLog.Info("BAZAAR", $"Sold Item {p.ItemId} x{p.Count} to {p.BuyerName}.");
        }

        private void HandleCurrencies1(PacketHeader header, ReadOnlySpan<byte> payload)
        {
            var p = new S2C_0x113_Currencies1(payload);
            if (!p.IsValid) return;

            _inventoryState.UpdateCurrencies1(in p);
        }

        private void HandleEquipsetValid(PacketHeader header, ReadOnlySpan<byte> payload)
        {
            var p = new S2C_0x116_EquipsetValid(payload);
            if (!p.IsValid) return;

            GordianLog.Info("EQUIPSET", "Equipset validity check passed.");
        }

        private void HandleEquipsetRes(PacketHeader header, ReadOnlySpan<byte> payload)
        {
            var p = new S2C_0x117_EquipsetRes(payload);
            if (!p.IsValid) return;

            GordianLog.Info("EQUIPSET", $"Equipset applied with {p.Count} items.");
        }

        private void HandleCurrencies2(PacketHeader header, ReadOnlySpan<byte> payload)
        {
            var p = new S2C_0x118_Currencies2(payload);
            if (!p.IsValid) return;

            _inventoryState.UpdateCurrencies2(in p);
        }

        #endregion

        #region Outbound Action Methods

        public async Task DropItemAsync(ContainerId container, byte slot, uint count)
        {
            byte[] buf = ArrayPool<byte>.Shared.Rent(12);
            try
            {
                int len = InventoryPacketBuilders.BuildItemDump(buf, ++_sequenceNumber, count, container, slot);
                if (LogOutboundOnRoute) _logPacketCallback?.Invoke(PacketDirection.Outbound, 0x028, _sequenceNumber, buf.AsSpan(0, len));
                await _sendChunkCallback(buf.AsMemory(0, len), false).ConfigureAwait(false);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buf);
            }
        }

        public async Task MoveItemAsync(ContainerId srcCont, byte srcSlot, ContainerId dstCont, byte dstSlot, uint count)
        {
            byte[] buf = ArrayPool<byte>.Shared.Rent(12);
            try
            {
                int len = InventoryPacketBuilders.BuildItemMove(buf, ++_sequenceNumber, count, srcCont, srcSlot, dstCont, dstSlot);
                if (LogOutboundOnRoute) _logPacketCallback?.Invoke(PacketDirection.Outbound, 0x029, _sequenceNumber, buf.AsSpan(0, len));
                await _sendChunkCallback(buf.AsMemory(0, len), false).ConfigureAwait(false);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buf);
            }
        }

        public async Task EquipItemAsync(byte slot, EquipSlotId equipSlot, ContainerId container)
        {
            byte[] buf = ArrayPool<byte>.Shared.Rent(8);
            try
            {
                int len = InventoryPacketBuilders.BuildEquipSet(buf, ++_sequenceNumber, slot, equipSlot, container);
                if (LogOutboundOnRoute) _logPacketCallback?.Invoke(PacketDirection.Outbound, 0x050, _sequenceNumber, buf.AsSpan(0, len));
                await _sendChunkCallback(buf.AsMemory(0, len), false).ConfigureAwait(false);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buf);
            }
        }

        public async Task EquipSetAsync(params (byte Slot, EquipSlotId EquipSlot, ContainerId Container)[] items)
        {
            byte[] buf = ArrayPool<byte>.Shared.Rent(72);
            try
            {
                int len = InventoryPacketBuilders.BuildEquipsetSet(buf, ++_sequenceNumber, items);
                if (LogOutboundOnRoute) _logPacketCallback?.Invoke(PacketDirection.Outbound, 0x051, _sequenceNumber, buf.AsSpan(0, len));
                await _sendChunkCallback(buf.AsMemory(0, len), false).ConfigureAwait(false);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buf);
            }
        }

        public async Task SortContainerAsync(ContainerId container)
        {
            byte[] buf = ArrayPool<byte>.Shared.Rent(8);
            try
            {
                int len = InventoryPacketBuilders.BuildItemStack(buf, ++_sequenceNumber, container);
                if (LogOutboundOnRoute) _logPacketCallback?.Invoke(PacketDirection.Outbound, 0x03A, _sequenceNumber, buf.AsSpan(0, len));
                await _sendChunkCallback(buf.AsMemory(0, len), false).ConfigureAwait(false);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buf);
            }
        }

        public async Task UseItemAsync(uint targetServerId, ushort targetIndex, uint count, byte slot, ContainerId container)
        {
            byte[] buf = ArrayPool<byte>.Shared.Rent(20);
            try
            {
                int len = InventoryPacketBuilders.BuildItemUse(buf, ++_sequenceNumber, targetServerId, targetIndex, count, slot, container);
                if (LogOutboundOnRoute) _logPacketCallback?.Invoke(PacketDirection.Outbound, 0x037, _sequenceNumber, buf.AsSpan(0, len));
                await _sendChunkCallback(buf.AsMemory(0, len), false).ConfigureAwait(false);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buf);
            }
        }

        public async Task RequestTradeAsync(uint targetServerId, ushort targetIndex)
        {
            byte[] buf = ArrayPool<byte>.Shared.Rent(12);
            try
            {
                int len = InventoryPacketBuilders.BuildTradeReq(buf, ++_sequenceNumber, targetServerId, targetIndex);
                if (LogOutboundOnRoute) _logPacketCallback?.Invoke(PacketDirection.Outbound, 0x032, _sequenceNumber, buf.AsSpan(0, len));
                await _sendChunkCallback(buf.AsMemory(0, len), false).ConfigureAwait(false);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buf);
            }
        }

        public async Task RespondTradeAsync(TradeClientKind kind, ushort tradeCounter)
        {
            byte[] buf = ArrayPool<byte>.Shared.Rent(12);
            try
            {
                int len = InventoryPacketBuilders.BuildTradeRes(buf, ++_sequenceNumber, kind, tradeCounter);
                if (LogOutboundOnRoute) _logPacketCallback?.Invoke(PacketDirection.Outbound, 0x033, _sequenceNumber, buf.AsSpan(0, len));
                await _sendChunkCallback(buf.AsMemory(0, len), false).ConfigureAwait(false);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buf);
            }
        }

        public async Task SetTradeItemAsync(uint count, ushort itemId, byte slot, byte tradeIndex)
        {
            byte[] buf = ArrayPool<byte>.Shared.Rent(12);
            try
            {
                int len = InventoryPacketBuilders.BuildTradeList(buf, ++_sequenceNumber, count, itemId, slot, tradeIndex);
                if (LogOutboundOnRoute) _logPacketCallback?.Invoke(PacketDirection.Outbound, 0x034, _sequenceNumber, buf.AsSpan(0, len));
                await _sendChunkCallback(buf.AsMemory(0, len), false).ConfigureAwait(false);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buf);
            }
        }

        public async Task TransferNpcTradeAsync(uint targetServerId, ushort targetIndex, params (byte Slot, uint Count)[] items)
        {
            byte[] buf = ArrayPool<byte>.Shared.Rent(64);
            try
            {
                int len = InventoryPacketBuilders.BuildItemTransfer(buf, ++_sequenceNumber, targetServerId, targetIndex, items);
                if (LogOutboundOnRoute) _logPacketCallback?.Invoke(PacketDirection.Outbound, 0x036, _sequenceNumber, buf.AsSpan(0, len));
                await _sendChunkCallback(buf.AsMemory(0, len), false).ConfigureAwait(false);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buf);
            }
        }

        public async Task BuyShopItemAsync(uint count, ushort shopNo, ushort shopItemIndex, byte propertyItemIndex)
        {
            byte[] buf = ArrayPool<byte>.Shared.Rent(16);
            try
            {
                int len = InventoryPacketBuilders.BuildShopBuy(buf, ++_sequenceNumber, count, shopNo, shopItemIndex, propertyItemIndex);
                if (LogOutboundOnRoute) _logPacketCallback?.Invoke(PacketDirection.Outbound, 0x083, _sequenceNumber, buf.AsSpan(0, len));
                await _sendChunkCallback(buf.AsMemory(0, len), false).ConfigureAwait(false);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buf);
            }
        }

        public async Task AppraiseShopItemAsync(uint count, ushort itemId, byte slot)
        {
            byte[] buf = ArrayPool<byte>.Shared.Rent(12);
            try
            {
                int len = InventoryPacketBuilders.BuildShopSellReq(buf, ++_sequenceNumber, count, itemId, slot);
                if (LogOutboundOnRoute) _logPacketCallback?.Invoke(PacketDirection.Outbound, 0x084, _sequenceNumber, buf.AsSpan(0, len));
                await _sendChunkCallback(buf.AsMemory(0, len), false).ConfigureAwait(false);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buf);
            }
        }

        public async Task ConfirmShopSaleAsync(ushort sellFlag = 1)
        {
            byte[] buf = ArrayPool<byte>.Shared.Rent(8);
            try
            {
                int len = InventoryPacketBuilders.BuildShopSellSet(buf, ++_sequenceNumber, sellFlag);
                if (LogOutboundOnRoute) _logPacketCallback?.Invoke(PacketDirection.Outbound, 0x085, _sequenceNumber, buf.AsSpan(0, len));
                await _sendChunkCallback(buf.AsMemory(0, len), false).ConfigureAwait(false);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buf);
            }
        }

        public async Task BrowseBazaarAsync(uint targetServerId, ushort targetIndex)
        {
            byte[] buf = ArrayPool<byte>.Shared.Rent(12);
            try
            {
                int len = InventoryPacketBuilders.BuildBazaarList(buf, ++_sequenceNumber, targetServerId, targetIndex);
                if (LogOutboundOnRoute) _logPacketCallback?.Invoke(PacketDirection.Outbound, 0x105, _sequenceNumber, buf.AsSpan(0, len));
                await _sendChunkCallback(buf.AsMemory(0, len), false).ConfigureAwait(false);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buf);
            }
        }

        public async Task BuyBazaarItemAsync(byte bazaarItemIndex, uint count)
        {
            byte[] buf = ArrayPool<byte>.Shared.Rent(12);
            try
            {
                int len = InventoryPacketBuilders.BuildBazaarBuy(buf, ++_sequenceNumber, bazaarItemIndex, count);
                if (LogOutboundOnRoute) _logPacketCallback?.Invoke(PacketDirection.Outbound, 0x106, _sequenceNumber, buf.AsSpan(0, len));
                await _sendChunkCallback(buf.AsMemory(0, len), false).ConfigureAwait(false);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buf);
            }
        }

        public async Task OpenBazaarAsync()
        {
            byte[] buf = ArrayPool<byte>.Shared.Rent(4);
            try
            {
                int len = InventoryPacketBuilders.BuildBazaarOpen(buf, ++_sequenceNumber);
                if (LogOutboundOnRoute) _logPacketCallback?.Invoke(PacketDirection.Outbound, 0x109, _sequenceNumber, buf.AsSpan(0, len));
                await _sendChunkCallback(buf.AsMemory(0, len), false).ConfigureAwait(false);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buf);
            }
        }

        public async Task SetBazaarItemPriceAsync(byte slot, uint price)
        {
            byte[] buf = ArrayPool<byte>.Shared.Rent(12);
            try
            {
                int len = InventoryPacketBuilders.BuildBazaarItemSet(buf, ++_sequenceNumber, slot, price);
                if (LogOutboundOnRoute) _logPacketCallback?.Invoke(PacketDirection.Outbound, 0x10A, _sequenceNumber, buf.AsSpan(0, len));
                await _sendChunkCallback(buf.AsMemory(0, len), false).ConfigureAwait(false);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buf);
            }
        }

        public async Task CloseBazaarAsync(uint allListClearFlag = 0)
        {
            byte[] buf = ArrayPool<byte>.Shared.Rent(8);
            try
            {
                int len = InventoryPacketBuilders.BuildBazaarClose(buf, ++_sequenceNumber, allListClearFlag);
                if (LogOutboundOnRoute) _logPacketCallback?.Invoke(PacketDirection.Outbound, 0x10B, _sequenceNumber, buf.AsSpan(0, len));
                await _sendChunkCallback(buf.AsMemory(0, len), false).ConfigureAwait(false);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buf);
            }
        }

        public async Task ExitBazaarAsync()
        {
            byte[] buf = ArrayPool<byte>.Shared.Rent(4);
            try
            {
                int len = InventoryPacketBuilders.BuildBazaarExit(buf, ++_sequenceNumber);
                if (LogOutboundOnRoute) _logPacketCallback?.Invoke(PacketDirection.Outbound, 0x104, _sequenceNumber, buf.AsSpan(0, len));
                await _sendChunkCallback(buf.AsMemory(0, len), false).ConfigureAwait(false);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buf);
            }
        }

        public async Task RequestCurrencies1Async()
        {
            byte[] buf = ArrayPool<byte>.Shared.Rent(4);
            try
            {
                int len = InventoryPacketBuilders.BuildCurrencies1Request(buf, ++_sequenceNumber);
                if (LogOutboundOnRoute) _logPacketCallback?.Invoke(PacketDirection.Outbound, 0x10F, _sequenceNumber, buf.AsSpan(0, len));
                await _sendChunkCallback(buf.AsMemory(0, len), false).ConfigureAwait(false);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buf);
            }
        }

        public async Task RequestCurrencies2Async()
        {
            byte[] buf = ArrayPool<byte>.Shared.Rent(4);
            try
            {
                int len = InventoryPacketBuilders.BuildCurrencies2Request(buf, ++_sequenceNumber);
                if (LogOutboundOnRoute) _logPacketCallback?.Invoke(PacketDirection.Outbound, 0x115, _sequenceNumber, buf.AsSpan(0, len));
                await _sendChunkCallback(buf.AsMemory(0, len), false).ConfigureAwait(false);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buf);
            }
        }

        #endregion
    }
}
