// src/Gordian.Core/World/InventoryState.cs
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Gordian.Core.Network.Packets;

namespace Gordian.Core.World
{
    /// <summary>
    /// Thread-safe item representation within a container slot.
    /// </summary>
    public sealed record InventoryItem(
        ushort ItemId,
        uint Count,
        byte Slot,
        ContainerId Container,
        uint Price,
        ItemLockFlag LockFlag,
        byte[]? ExtData = null
    );

    /// <summary>
    /// Individual container state containing size information and slot items.
    /// </summary>
    public sealed class ContainerState
    {
        public ContainerId Id { get; }
        public byte MaxSize { get; internal set; }
        public ushort UsableSize { get; internal set; }
        internal readonly Dictionary<byte, InventoryItem> _items = new Dictionary<byte, InventoryItem>();

        public ContainerState(ContainerId id)
        {
            Id = id;
        }

        public IReadOnlyDictionary<byte, InventoryItem> Items => _items;

        public bool TryGetItem(byte slot, out InventoryItem item)
        {
            return _items.TryGetValue(slot, out item!);
        }
    }

    /// <summary>
    /// Item available in an NPC shop window.
    /// </summary>
    public sealed record ShopItemEntry(
        uint Price,
        ushort ItemId,
        byte ShopIndex,
        ushort Skill,
        ushort GuildInfo
    );

    /// <summary>
    /// Item available in a player's bazaar.
    /// </summary>
    public sealed record BazaarItemEntry(
        uint Price,
        uint Count,
        ushort TaxRate,
        ushort ItemId,
        byte ItemIndex,
        byte[] ExtData
    );

    /// <summary>
    /// Thread-safe multi-container inventory, currency, equipment, trade, shop, and bazaar state cache.
    /// </summary>
    public sealed class InventoryState
    {
        private readonly object _lock = new object();
        private readonly ContainerState[] _containers = new ContainerState[(int)ContainerId.Count];
        private readonly (ContainerId Container, byte Slot)[] _equippedGear = new (ContainerId, byte)[(int)EquipSlotId.Count];

        public event Action<ContainerId, byte, InventoryItem?>? ItemChanged;
        public event Action<ContainerId, byte, ushort>? ContainerSizesChanged;
        public event Action<EquipSlotId, ContainerId, byte>? EquipChanged;
        public event Action? CurrenciesChanged;
        public event Action? TradeChanged;
        public event Action? ShopChanged;
        public event Action? BazaarChanged;

        public InventoryState()
        {
            for (int i = 0; i < (int)ContainerId.Count; i++)
            {
                _containers[i] = new ContainerState((ContainerId)i);
            }

            for (int i = 0; i < (int)EquipSlotId.Count; i++)
            {
                _equippedGear[i] = (ContainerId.Inventory, 0xFF);
            }
        }

        #region Container Management

        public ContainerState GetContainer(ContainerId container)
        {
            int idx = (int)container;
            if (idx < 0 || idx >= _containers.Length) throw new ArgumentOutOfRangeException(nameof(container));
            return _containers[idx];
        }

        public void SetContainerSizes(ContainerId container, byte maxSize, ushort usableSize)
        {
            int idx = (int)container;
            if (idx < 0 || idx >= _containers.Length) return;

            lock (_lock)
            {
                _containers[idx].MaxSize = maxSize;
                _containers[idx].UsableSize = usableSize;
            }

            ContainerSizesChanged?.Invoke(container, maxSize, usableSize);
        }

        public void SetItem(ContainerId container, byte slot, ushort itemId, uint count, ItemLockFlag lockFlag)
        {
            int idx = (int)container;
            if (idx < 0 || idx >= _containers.Length) return;

            InventoryItem item;
            lock (_lock)
            {
                if (count == 0 || itemId == 0)
                {
                    _containers[idx]._items.Remove(slot);
                    item = null!;
                }
                else
                {
                    _containers[idx]._items.TryGetValue(slot, out var existing);
                    item = new InventoryItem(itemId, count, slot, container, existing?.Price ?? 0, lockFlag, existing?.ExtData);
                    _containers[idx]._items[slot] = item;
                }
            }

            ItemChanged?.Invoke(container, slot, item);
        }

        public void UpdateItemCount(ContainerId container, byte slot, uint count, ItemLockFlag lockFlag)
        {
            int idx = (int)container;
            if (idx < 0 || idx >= _containers.Length) return;

            InventoryItem? item = null;
            lock (_lock)
            {
                if (count == 0)
                {
                    _containers[idx]._items.Remove(slot);
                }
                else if (_containers[idx]._items.TryGetValue(slot, out var existing))
                {
                    item = existing with { Count = count, LockFlag = lockFlag };
                    _containers[idx]._items[slot] = item;
                }
            }

            ItemChanged?.Invoke(container, slot, item);
        }

        public void SetItemAttributes(ContainerId container, byte slot, ushort itemId, uint count, uint price, ItemLockFlag lockFlag, ReadOnlySpan<byte> extData)
        {
            int idx = (int)container;
            if (idx < 0 || idx >= _containers.Length) return;

            InventoryItem item;
            lock (_lock)
            {
                if (count == 0 || itemId == 0)
                {
                    _containers[idx]._items.Remove(slot);
                    item = null!;
                }
                else
                {
                    byte[] ext = extData.IsEmpty ? Array.Empty<byte>() : extData.ToArray();
                    item = new InventoryItem(itemId, count, slot, container, price, lockFlag, ext);
                    _containers[idx]._items[slot] = item;
                }
            }

            ItemChanged?.Invoke(container, slot, item);
        }

        public void RemoveItem(ContainerId container, byte slot)
        {
            int idx = (int)container;
            if (idx < 0 || idx >= _containers.Length) return;

            lock (_lock)
            {
                _containers[idx]._items.Remove(slot);
            }

            ItemChanged?.Invoke(container, slot, null);
        }

        public void MoveItem(ContainerId srcCont, byte srcSlot, ContainerId dstCont, byte dstSlot, uint count)
        {
            int srcIdx = (int)srcCont;
            int dstIdx = (int)dstCont;
            if (srcIdx < 0 || srcIdx >= _containers.Length || dstIdx < 0 || dstIdx >= _containers.Length) return;

            InventoryItem? srcResult = null;
            InventoryItem? dstResult = null;

            lock (_lock)
            {
                if (!_containers[srcIdx]._items.TryGetValue(srcSlot, out var sourceItem)) return;

                if (sourceItem.Count <= count)
                {
                    _containers[srcIdx]._items.Remove(srcSlot);
                    srcResult = null;
                }
                else
                {
                    srcResult = sourceItem with { Count = sourceItem.Count - count };
                    _containers[srcIdx]._items[srcSlot] = srcResult;
                }

                if (_containers[dstIdx]._items.TryGetValue(dstSlot, out var destItem) && destItem.ItemId == sourceItem.ItemId)
                {
                    dstResult = destItem with { Count = destItem.Count + count };
                    _containers[dstIdx]._items[dstSlot] = dstResult;
                }
                else
                {
                    dstResult = sourceItem with { Slot = dstSlot, Container = dstCont, Count = count };
                    _containers[dstIdx]._items[dstSlot] = dstResult;
                }
            }

            ItemChanged?.Invoke(srcCont, srcSlot, srcResult);
            ItemChanged?.Invoke(dstCont, dstSlot, dstResult);
        }

        public void ClearContainer(ContainerId container)
        {
            int idx = (int)container;
            if (idx < 0 || idx >= _containers.Length) return;

            lock (_lock)
            {
                _containers[idx]._items.Clear();
            }
        }

        #endregion

        #region Equipment Management

        public (ContainerId Container, byte Slot) GetEquipped(EquipSlotId equipSlot)
        {
            int idx = (int)equipSlot;
            if (idx < 0 || idx >= _equippedGear.Length) return (ContainerId.Inventory, 0xFF);
            lock (_lock)
            {
                return _equippedGear[idx];
            }
        }

        public void SetEquip(EquipSlotId equipSlot, ContainerId container, byte slot)
        {
            int idx = (int)equipSlot;
            if (idx < 0 || idx >= _equippedGear.Length) return;

            lock (_lock)
            {
                _equippedGear[idx] = (container, slot);
            }

            EquipChanged?.Invoke(equipSlot, container, slot);
        }

        #endregion

        #region Currencies

        public int SparksOfEminence { get; private set; }
        public int UnityAccolades { get; private set; }
        public int ConquestSandoria { get; private set; }
        public int ConquestBastok { get; private set; }
        public int ConquestWindurst { get; private set; }
        public int ImperialStanding { get; private set; }
        public int AlliedNotes { get; private set; }
        public ushort LoginPoints { get; private set; }
        public int Cruor { get; private set; }
        public ushort Deeds { get; private set; }
        public ushort AncientBeastcoins { get; private set; }
        public ushort BeastmansSeals { get; private set; }
        public ushort KindredsSeals { get; private set; }
        public int Bayld { get; private set; }
        public ushort KineticUnits { get; private set; }
        public byte CoalitionImprimaturs { get; private set; }
        public int MweyaPlasm { get; private set; }
        public ushort EschaBeads { get; private set; }
        public int EschaSilt { get; private set; }
        public int Hallmarks { get; private set; }
        public int BadgesOfGallantry { get; private set; }
        public int DomainPoints { get; private set; }
        public int MogSegments { get; private set; }
        public int Gallimaufry { get; private set; }

        public void UpdateCurrencies1(in S2C_0x113_Currencies1 cur)
        {
            if (!cur.IsValid) return;

            lock (_lock)
            {
                SparksOfEminence = cur.SparksOfEminence;
                UnityAccolades = cur.UnityAccolades;
                ConquestSandoria = cur.ConquestSandoria;
                ConquestBastok = cur.ConquestBastok;
                ConquestWindurst = cur.ConquestWindurst;
                ImperialStanding = cur.ImperialStanding;
                AlliedNotes = cur.AlliedNotes;
                LoginPoints = cur.LoginPoints;
                Cruor = cur.Cruor;
                Deeds = cur.Deeds;
                AncientBeastcoins = cur.AncientBeastcoins;
                BeastmansSeals = cur.BeastmansSeals;
                KindredsSeals = cur.KindredsSeals;
            }

            CurrenciesChanged?.Invoke();
        }

        public void UpdateCurrencies2(in S2C_0x118_Currencies2 cur)
        {
            if (!cur.IsValid) return;

            lock (_lock)
            {
                Bayld = cur.Bayld;
                KineticUnits = cur.KineticUnits;
                CoalitionImprimaturs = cur.CoalitionImprimaturs;
                MweyaPlasm = cur.MweyaPlasm;
                EschaBeads = cur.EschaBeads;
                EschaSilt = cur.EschaSilt;
                Hallmarks = cur.Hallmarks;
                BadgesOfGallantry = cur.BadgesOfGallantry;
                DomainPoints = cur.DomainPoints;
                MogSegments = cur.MogSegments;
                Gallimaufry = cur.Gallimaufry;
            }

            CurrenciesChanged?.Invoke();
        }

        #endregion

        #region Trade Session

        public bool IsTrading { get; private set; }
        public uint TradePartnerServerId { get; private set; }
        public ushort TradePartnerIndex { get; private set; }
        public TradeResultKind TradeStatus { get; private set; } = TradeResultKind.End;
        private readonly Dictionary<byte, (ushort ItemId, uint Count, byte[] ExtData)> _partnerTradeItems = new Dictionary<byte, (ushort, uint, byte[])>();
        private readonly Dictionary<byte, (ushort ItemId, uint Count, byte Slot)> _myTradeItems = new Dictionary<byte, (ushort, uint, byte)>();

        public IReadOnlyDictionary<byte, (ushort ItemId, uint Count, byte[] ExtData)> PartnerTradeItems => _partnerTradeItems;
        public IReadOnlyDictionary<byte, (ushort ItemId, uint Count, byte Slot)> MyTradeItems => _myTradeItems;

        public void StartTrade(uint targetServerId, ushort targetIndex)
        {
            lock (_lock)
            {
                IsTrading = true;
                TradePartnerServerId = targetServerId;
                TradePartnerIndex = targetIndex;
                TradeStatus = TradeResultKind.Start;
                _partnerTradeItems.Clear();
                _myTradeItems.Clear();
            }

            TradeChanged?.Invoke();
        }

        public void SetTradeStatus(TradeResultKind status)
        {
            lock (_lock)
            {
                TradeStatus = status;
                if (status == TradeResultKind.End || status == TradeResultKind.Cancel || status >= TradeResultKind.ErrEtc)
                {
                    IsTrading = false;
                    _partnerTradeItems.Clear();
                    _myTradeItems.Clear();
                }
            }

            TradeChanged?.Invoke();
        }

        public void SetPartnerTradeItem(byte tradeIndex, ushort itemId, uint count, ReadOnlySpan<byte> extData)
        {
            lock (_lock)
            {
                if (count == 0 || itemId == 0)
                {
                    _partnerTradeItems.Remove(tradeIndex);
                }
                else
                {
                    byte[] ext = extData.IsEmpty ? Array.Empty<byte>() : extData.ToArray();
                    _partnerTradeItems[tradeIndex] = (itemId, count, ext);
                }
            }

            TradeChanged?.Invoke();
        }

        public void SetMyTradeItem(byte tradeIndex, ushort itemId, uint count, byte slot)
        {
            lock (_lock)
            {
                if (count == 0 || itemId == 0)
                {
                    _myTradeItems.Remove(tradeIndex);
                }
                else
                {
                    _myTradeItems[tradeIndex] = (itemId, count, slot);
                }
            }

            TradeChanged?.Invoke();
        }

        #endregion

        #region Shop Session

        public bool IsShopOpen { get; private set; }
        public ShopOpenStatus ShopStatus { get; private set; } = ShopOpenStatus.Close;
        public ushort ShopListNum { get; private set; }
        public uint AppraisedSellPrice { get; private set; }
        public byte AppraisedSlot { get; private set; }
        private readonly List<ShopItemEntry> _shopItems = new List<ShopItemEntry>();

        public IReadOnlyList<ShopItemEntry> ShopItems => _shopItems;

        public void OpenShop(ushort shopListNum)
        {
            lock (_lock)
            {
                IsShopOpen = true;
                ShopListNum = shopListNum;
                ShopStatus = ShopOpenStatus.Open;
                _shopItems.Clear();
            }

            ShopChanged?.Invoke();
        }

        public void SetGuildOpenStatus(ShopOpenStatus status)
        {
            lock (_lock)
            {
                ShopStatus = status;
                if (status == ShopOpenStatus.Close)
                {
                    IsShopOpen = false;
                }
            }

            ShopChanged?.Invoke();
        }

        public void AddShopItems(ReadOnlySpan<ShopItemEntry> items)
        {
            lock (_lock)
            {
                for (int i = 0; i < items.Length; i++)
                {
                    _shopItems.Add(items[i]);
                }
            }

            ShopChanged?.Invoke();
        }

        public void SetAppraisal(byte slot, uint price)
        {
            lock (_lock)
            {
                AppraisedSlot = slot;
                AppraisedSellPrice = price;
            }

            ShopChanged?.Invoke();
        }

        public void CloseShop()
        {
            lock (_lock)
            {
                IsShopOpen = false;
                _shopItems.Clear();
                AppraisedSellPrice = 0;
            }

            ShopChanged?.Invoke();
        }

        #endregion

        #region Bazaar Session

        public bool IsViewingBazaar { get; private set; }
        public string ViewedBazaarPlayer { get; private set; } = string.Empty;
        private readonly List<BazaarItemEntry> _browsedBazaarItems = new List<BazaarItemEntry>();
        private readonly Dictionary<byte, uint> _personalBazaarPrices = new Dictionary<byte, uint>();

        public IReadOnlyList<BazaarItemEntry> BrowsedBazaarItems => _browsedBazaarItems;
        public IReadOnlyDictionary<byte, uint> PersonalBazaarPrices => _personalBazaarPrices;

        public void StartViewingBazaar(string sellerName)
        {
            lock (_lock)
            {
                IsViewingBazaar = true;
                ViewedBazaarPlayer = sellerName;
                _browsedBazaarItems.Clear();
            }

            BazaarChanged?.Invoke();
        }

        public void AddBazaarItem(BazaarItemEntry item)
        {
            lock (_lock)
            {
                _browsedBazaarItems.Add(item);
            }

            BazaarChanged?.Invoke();
        }

        public void StopViewingBazaar()
        {
            lock (_lock)
            {
                IsViewingBazaar = false;
                ViewedBazaarPlayer = string.Empty;
                _browsedBazaarItems.Clear();
            }

            BazaarChanged?.Invoke();
        }

        public void SetPersonalBazaarPrice(byte slot, uint price)
        {
            lock (_lock)
            {
                if (price == 0)
                {
                    _personalBazaarPrices.Remove(slot);
                }
                else
                {
                    _personalBazaarPrices[slot] = price;
                }
            }

            BazaarChanged?.Invoke();
        }

        public void ClearPersonalBazaar()
        {
            lock (_lock)
            {
                _personalBazaarPrices.Clear();
            }

            BazaarChanged?.Invoke();
        }

        #endregion
    }
}
