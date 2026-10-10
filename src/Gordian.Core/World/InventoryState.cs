// src/Gordian.Core/World/InventoryState.cs
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
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
    /// An Auction House answer (S2C 0x04C): the command it answers, the sale slot (work index, -1 when none), the
    /// result codes, and the parcel (the item, quantity, price and seller) it carries.
    /// </summary>
    public sealed record AuctionResponse(
        AuctionCommand Command,
        sbyte WorkIndex,
        sbyte Result,
        sbyte ResultStatus,
        byte ParcelStat,
        byte ItemIndex,
        ushort ItemId,
        uint Count,
        uint Price,
        string SellerName,
        byte Category = 0,
        uint MarketNo = 0,
        uint LotNo = 0,
        uint TimeStamp = 0,
        uint ParamStacks = 0,
        ushort ParamWorkIndex = 0
    )
    {
        /// <summary>The parcel status as a named value.</summary>
        public AuctionParcelStat Stat => (AuctionParcelStat)ParcelStat;

        /// <summary>The <c>Result</c> byte as the client switches on it (success 1, fail 0, interim 2, errors 0xC5-0xFF).</summary>
        public AuctionResultCode ResultCode => (AuctionResultCode)(byte)Result;

        /// <summary>Where the item stands in the market.</summary>
        public AuctionResultStatus Status => (AuctionResultStatus)(byte)ResultStatus;
    }

    /// <summary>
    /// The hours or holiday of a closed guild (S2C 0x086): the opening and closing hour of an after-hours close, or the
    /// day-of-week number of the weekly holiday (-1 for the field that does not apply).
    /// </summary>
    public sealed record GuildHoursInfo(int OpenHour, int CloseHour, int HolidayDay);

    /// <summary>
    /// A guild shop purchase (S2C 0x082) or sale (S2C 0x084) result. <see cref="ItemId"/> is 0 when the
    /// transaction failed outright, and <see cref="Trade"/> then says why.
    /// </summary>
    public sealed record GuildTransaction(bool IsPurchase, ushort ItemId, byte Count, sbyte Trade)
    {
        public bool Succeeded => ItemId != 0;
    }

    /// <summary>An item a guild shop will buy from the player (S2C 0x085): its stock, stock limit and price.</summary>
    public sealed record GuildItemEntry(ushort ItemId, byte Stock, byte Max, int Price);

    /// <summary>The result of buying from another player's bazaar (S2C 0x106).</summary>
    public sealed record BazaarPurchaseResult(BazaarBuyState State, string SellerName);

    /// <summary>A player browsing the local player's bazaar (S2C 0x108).</summary>
    public sealed record BazaarVisitor(uint ServerId, ushort TargetIndex, string Name);

    /// <summary>A buyer took items from one of the local player's bazaar slots (S2C 0x109).</summary>
    public sealed record BazaarSlotSold(uint BuyerId, ushort BuyerIndex, string BuyerName, byte Slot, uint Count);

    /// <summary>An item sold from the local player's bazaar (S2C 0x10A).</summary>
    public sealed record BazaarSale(ushort ItemId, uint Count, string BuyerName);

    /// <summary>One equipment set slot as the server validated it (S2C 0x116); all zero when the server removed it.</summary>
    public readonly record struct EquipsetSlotEntry(bool HasItem, bool RemoveItem, ContainerId Container, byte ItemIndex, ushort ItemId);

    /// <summary>One gear piece in an equipment set change result (S2C 0x117).</summary>
    public readonly record struct EquipsetItemEntry(byte ItemIndex, EquipSlotId EquipSlot, ContainerId Container);

    /// <summary>
    /// The result of equipping an equipment set (S2C 0x117): the pieces the set changed and what is worn in each
    /// equipment slot afterwards. A changed piece that is not what its slot now wears failed to equip.
    /// </summary>
    public sealed record EquipsetResult(IReadOnlyList<EquipsetItemEntry> Changed, IReadOnlyList<EquipsetItemEntry> Equipped)
    {
        /// <summary>The changed pieces that did not end up in their slot.</summary>
        public IEnumerable<EquipsetItemEntry> FailedItems => Changed.Where(c => !Equipped.Contains(c));
    }

    /// <summary>
    /// Thread-safe multi-container inventory, currency, equipment, trade, shop, bazaar, guild shop, Auction House,
    /// and equipment set state cache.
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

        /// <summary>S2C 0x03F: a purchase went through (the shop slot bought and how many).</summary>
        public event Action<ushort, uint>? ShopPurchased;

        public void NotifyPurchase(ushort shopItemIndex, uint count) => ShopPurchased?.Invoke(shopItemIndex, count);
        public event Action? BazaarChanged;
        public event Action? AuctionChanged;
        public event Action? EquipsetChanged;

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

        /// <summary>The character's gil: inventory slot 0 holds item 65535 with the amount as its count (0 until the server sends it).</summary>
        public uint Gil
        {
            get
            {
                lock (_lock)
                {
                    return _containers[(int)ContainerId.Inventory]._items.TryGetValue(0, out var gil) && gil.ItemId == GilItemId ? gil.Count : 0;
                }
            }
        }

        /// <summary>The item id of gil (inventory slot 0).</summary>
        public const ushort GilItemId = 65535;

        /// <summary>A copy of a container's items taken under the state lock (safe to read off the packet thread).</summary>
        public InventoryItem[] SnapshotItems(ContainerId container)
        {
            int idx = (int)container;
            if (idx < 0 || idx >= _containers.Length) return Array.Empty<InventoryItem>();
            lock (_lock)
            {
                var items = new InventoryItem[_containers[idx]._items.Count];
                _containers[idx]._items.Values.CopyTo(items, 0);
                return items;
            }
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

        /// <summary>
        /// Empties every equipment slot (S2C 0x04F). Raises <see cref="EquipChanged"/> for each slot that held an item.
        /// </summary>
        public void ClearEquipment()
        {
            Span<bool> cleared = stackalloc bool[(int)EquipSlotId.Count];
            lock (_lock)
            {
                for (int i = 0; i < _equippedGear.Length; i++)
                {
                    cleared[i] = _equippedGear[i].Slot != 0xFF;
                    _equippedGear[i] = (ContainerId.Inventory, 0xFF);
                }
            }

            for (int i = 0; i < cleared.Length; i++)
            {
                if (cleared[i]) EquipChanged?.Invoke((EquipSlotId)i, ContainerId.Inventory, 0xFF);
            }
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

        private readonly int[] _currencies1 = new int[CurrencyLayout.Count1];
        private readonly int[] _currencies2 = new int[CurrencyLayout.Count2];

        /// <summary>The last value of a currency of S2C 0x113 (guild points, cinders, zeni, tokens, assault points ...); 0 before the packet arrives.</summary>
        public int GetCurrency(Currency1Kind kind)
        {
            lock (_lock) return _currencies1[(int)kind];
        }

        /// <summary>The last value of a currency of S2C 0x118 (stones, canteens, vouchers, crafter points ...); 0 before the packet arrives.</summary>
        public int GetCurrency(Currency2Kind kind)
        {
            lock (_lock) return _currencies2[(int)kind];
        }

        public void UpdateCurrencies1(in S2C_0x113_Currencies1 cur)
        {
            if (!cur.IsValid) return;

            lock (_lock)
            {
                for (int i = 0; i < _currencies1.Length; i++) _currencies1[i] = cur.GetCurrency((Currency1Kind)i);
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
                for (int i = 0; i < _currencies2.Length; i++) _currencies2[i] = cur.GetCurrency((Currency2Kind)i);
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

        /// <summary>How many appraisals (S2C 0x03D) have arrived; a change means <see cref="AppraisedSellPrice"/> is a new answer.</summary>
        public int AppraisalCount { get; private set; }

        /// <summary>A copy of the shop's items taken under the state lock.</summary>
        public ShopItemEntry[] SnapshotShopItems()
        {
            lock (_lock) return _shopItems.ToArray();
        }

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

        /// <summary>Opening hours or the holiday of a closed guild (S2C 0x086), or null when it is open or no packet came.</summary>
        public GuildHoursInfo? GuildHours { get; private set; }

        /// <summary>Records a guild status (S2C 0x086) with the hours or holiday it names.</summary>
        public void SetGuildOpenStatus(ShopOpenStatus status, GuildHoursInfo? hours)
        {
            lock (_lock) GuildHours = status == ShopOpenStatus.Open ? null : hours;
            SetGuildOpenStatus(status);
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
                AppraisalCount++;
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

        /// <summary>The result of the last purchase from another player's bazaar (S2C 0x106), or null.</summary>
        public BazaarPurchaseResult? LastBazaarPurchase { get; private set; }

        /// <summary>The last buyer to take items from the local player's bazaar (S2C 0x109), or null.</summary>
        public BazaarSlotSold? LastBazaarSlotSold { get; private set; }

        private readonly Dictionary<uint, BazaarVisitor> _bazaarVisitors = new Dictionary<uint, BazaarVisitor>();
        private readonly List<BazaarSale> _bazaarSales = new List<BazaarSale>();

        /// <summary>How many sales <see cref="SnapshotBazaarSales"/> keeps (oldest dropped first).</summary>
        public const int MaxBazaarSales = 50;

        public void SetBazaarPurchaseResult(BazaarPurchaseResult result)
        {
            lock (_lock) LastBazaarPurchase = result;
            BazaarChanged?.Invoke();
        }

        /// <summary>S2C 0x108: a player entered (<paramref name="entered"/>) or left the local player's bazaar.</summary>
        public void SetBazaarVisitor(BazaarVisitor visitor, bool entered)
        {
            lock (_lock)
            {
                if (entered) _bazaarVisitors[visitor.ServerId] = visitor;
                else _bazaarVisitors.Remove(visitor.ServerId);
            }

            BazaarChanged?.Invoke();
        }

        /// <summary>The players browsing the local player's bazaar, copied under the state lock.</summary>
        public BazaarVisitor[] SnapshotBazaarVisitors()
        {
            lock (_lock) return _bazaarVisitors.Values.ToArray();
        }

        public void RecordBazaarSlotSold(BazaarSlotSold sold)
        {
            lock (_lock) LastBazaarSlotSold = sold;
            BazaarChanged?.Invoke();
        }

        public void RecordBazaarSale(BazaarSale sale)
        {
            lock (_lock)
            {
                if (_bazaarSales.Count == MaxBazaarSales) _bazaarSales.RemoveAt(0);
                _bazaarSales.Add(sale);
            }

            BazaarChanged?.Invoke();
        }

        /// <summary>The local player's bazaar sales this session, oldest first, copied under the state lock.</summary>
        public BazaarSale[] SnapshotBazaarSales()
        {
            lock (_lock) return _bazaarSales.ToArray();
        }

        #endregion

        #region Guild Shop

        /// <summary>The last guild shop purchase or sale result (S2C 0x082 / 0x084), or null.</summary>
        public GuildTransaction? LastGuildTransaction { get; private set; }

        private readonly List<GuildItemEntry> _guildSellList = new List<GuildItemEntry>();
        private readonly List<GuildItemEntry> _guildBuyList = new List<GuildItemEntry>();

        /// <summary>Raised when a guild shop purchase or sale answer arrives (S2C 0x082 / 0x084).</summary>
        public event Action<GuildTransaction>? GuildTransactionReceived;

        /// <summary>Raised when a guild stock list is complete: <c>true</c> for what the guild sells (S2C 0x083), <c>false</c> for what it buys (0x085).</summary>
        public event Action<bool, GuildItemEntry[]>? GuildListCompleted;

        /// <summary>Raised when the guild's open, closed or holiday status arrives (S2C 0x086).</summary>
        public event Action<ShopOpenStatus, GuildHoursInfo?>? GuildStatusReceived;

        public void SetGuildTransaction(GuildTransaction transaction)
        {
            lock (_lock) LastGuildTransaction = transaction;
            ShopChanged?.Invoke();
            GuildTransactionReceived?.Invoke(transaction);
        }

        /// <summary>Raises <see cref="GuildStatusReceived"/> for a status the module has just recorded.</summary>
        internal void NotifyGuildStatus(ShopOpenStatus status, GuildHoursInfo? hours) => GuildStatusReceived?.Invoke(status, hours);

        /// <summary>
        /// Adds one S2C 0x083 packet of the items the guild sells. <paramref name="packetIndex"/> is the packet's place in
        /// the list (the low six bits of Stat); packet 0 starts a new list.
        /// </summary>
        public void AddGuildBuyItems(int packetIndex, ReadOnlySpan<GuildItemEntry> items)
        {
            lock (_lock)
            {
                if (packetIndex == 0) _guildBuyList.Clear();
                for (int i = 0; i < items.Length; i++) _guildBuyList.Add(items[i]);
            }
        }

        /// <summary>The items the open guild shop sells to the player, copied under the state lock.</summary>
        public GuildItemEntry[] SnapshotGuildBuyList()
        {
            lock (_lock) return _guildBuyList.ToArray();
        }

        /// <summary>Marks the guild list the last packet completed and raises <see cref="GuildListCompleted"/>.</summary>
        public void CompleteGuildList(bool sells)
        {
            var items = sells ? SnapshotGuildBuyList() : SnapshotGuildSellList();
            GuildListCompleted?.Invoke(sells, items);
        }

        /// <summary>
        /// Adds one S2C 0x085 packet of the items the guild buys. <paramref name="packetIndex"/> is the packet's
        /// place in the list (the low six bits of Stat); packet 0 starts a new list.
        /// </summary>
        public void AddGuildSellItems(int packetIndex, ReadOnlySpan<GuildItemEntry> items)
        {
            lock (_lock)
            {
                if (packetIndex == 0) _guildSellList.Clear();
                for (int i = 0; i < items.Length; i++) _guildSellList.Add(items[i]);
            }

            ShopChanged?.Invoke();
        }

        /// <summary>The items the open guild shop buys from the player, copied under the state lock.</summary>
        public GuildItemEntry[] SnapshotGuildSellList()
        {
            lock (_lock) return _guildSellList.ToArray();
        }

        #endregion

        #region Auction House

        /// <summary>The last Auction House answer (S2C 0x04C), or null.</summary>
        public AuctionResponse? LastAuctionResponse { get; private set; }

        private readonly Dictionary<sbyte, AuctionResponse> _auctionSlots = new Dictionary<sbyte, AuctionResponse>();

        /// <summary>
        /// Records an Auction House answer. An answer that names a sale slot (work index 0 or more) also replaces
        /// that slot's entry, which is how the Sales Status list is built.
        /// </summary>
        public void SetAuctionResponse(AuctionResponse response)
        {
            lock (_lock)
            {
                LastAuctionResponse = response;
                if (response.WorkIndex >= 0) _auctionSlots[response.WorkIndex] = response;
            }

            AuctionChanged?.Invoke();
        }

        /// <summary>The latest answer for each Auction House sale slot, by work index, copied under the state lock.</summary>
        public IReadOnlyDictionary<sbyte, AuctionResponse> SnapshotAuctionSlots()
        {
            lock (_lock) return new Dictionary<sbyte, AuctionResponse>(_auctionSlots);
        }

        #endregion

        #region Equipment Sets

        private EquipsetSlotEntry[]? _equipsetValidation;

        /// <summary>The last result of equipping an equipment set (S2C 0x117), or null.</summary>
        public EquipsetResult? LastEquipsetResult { get; private set; }

        /// <summary>
        /// Stores the server's validation of the equipment set being edited (S2C 0x116): entry 0 is the piece
        /// just changed and entries 1-16 follow the equipment slot order.
        /// </summary>
        public void SetEquipsetValidation(ReadOnlySpan<EquipsetSlotEntry> entries)
        {
            lock (_lock) _equipsetValidation = entries.ToArray();
            EquipsetChanged?.Invoke();
        }

        /// <summary>The last equipment set validation (S2C 0x116), copied under the state lock; empty until one arrives.</summary>
        public EquipsetSlotEntry[] SnapshotEquipsetValidation()
        {
            lock (_lock) return _equipsetValidation?.ToArray() ?? Array.Empty<EquipsetSlotEntry>();
        }

        public void SetEquipsetResult(EquipsetResult result)
        {
            lock (_lock) LastEquipsetResult = result;
            EquipsetChanged?.Invoke();
        }

        #endregion
    }
}
