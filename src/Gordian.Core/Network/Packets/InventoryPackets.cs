// src/Gordian.Core/Network/Packets/InventoryPackets.cs
using System;
using System.Buffers.Binary;
using System.Text;

namespace Gordian.Core.Network.Packets
{
    #region Protocol Enums

    /// <summary>
    /// Inventory container identifiers in Final Fantasy XI.
    /// Protocol specification referenced from LandSandBoat (https://github.com/LandSandBoat/server/blob/base/src/map/item_container.h).
    /// </summary>
    public enum ContainerId : byte
    {
        Inventory   = 0,
        MogSafe     = 1,
        Storage     = 2,
        TempItems   = 3,
        MogLocker   = 4,
        MogSatchel  = 5,
        MogSack     = 6,
        MogCase     = 7,
        Wardrobe    = 8,
        MogSafe2    = 9,
        Wardrobe2   = 10,
        Wardrobe3   = 11,
        Wardrobe4   = 12,
        Wardrobe5   = 13,
        Wardrobe6   = 14,
        Wardrobe7   = 15,
        Wardrobe8   = 16,
        RecycleBin  = 17,
        Count       = 18
    }

    /// <summary>
    /// Equipment slot types in Final Fantasy XI.
    /// Protocol specification referenced from LandSandBoat (https://github.com/LandSandBoat/server/blob/base/src/map/entities/battle_entity.h).
    /// </summary>
    public enum EquipSlotId : byte
    {
        Main    = 0x00,
        Sub     = 0x01,
        Ranged  = 0x02,
        Ammo    = 0x03,
        Head    = 0x04,
        Body    = 0x05,
        Hands   = 0x06,
        Legs    = 0x07,
        Feet    = 0x08,
        Neck    = 0x09,
        Waist   = 0x0A,
        Ear1    = 0x0B,
        Ear2    = 0x0C,
        Ring1   = 0x0D,
        Ring2   = 0x0E,
        Back    = 0x0F,
        Link1   = 0x10,
        Link2   = 0x11,
        Count   = 18
    }

    /// <summary>
    /// Item lock flags controlling item security and transferability.
    /// Protocol specification referenced from LandSandBoat (https://github.com/LandSandBoat/server/blob/base/src/map/enums/item_lockflg.h).
    /// </summary>
    public enum ItemLockFlag : byte
    {
        Normal    = 0x00,
        NoDrop    = 0x05,
        NoSelect  = 0x0F,
        Linkshell = 0x13,
        Unknown0  = 0x19,
        Mannequin = 0x1B
    }

    /// <summary>
    /// Container sync state for S2C 0x01D.
    /// Protocol specification referenced from LandSandBoat (https://github.com/LandSandBoat/server/blob/base/src/map/packets/s2c/0x01d_item_same.h).
    /// </summary>
    public enum ItemSameState : byte
    {
        StillLoading = 0,
        AllLoaded    = 1
    }

    /// <summary>
    /// S2C 0x022 Trade result kinds.
    /// Protocol specification referenced from LandSandBoat (https://github.com/LandSandBoat/server/blob/base/src/map/packets/s2c/0x022_item_trade_res.h).
    /// </summary>
    public enum TradeResultKind : uint
    {
        Start          = 0,
        Cancel         = 1,
        Make           = 2,
        MakeCancel     = 3,
        ErrEtc         = 4,
        ErrNoSearchYou = 5,
        ErrNowReq      = 6,
        ErrYouTrade    = 7,
        ErrLogout      = 8,
        End            = 9
    }

    /// <summary>
    /// C2S 0x033 Trade response kinds.
    /// Protocol specification referenced from LandSandBoat (https://github.com/LandSandBoat/server/blob/base/src/map/packets/c2s/0x033_trade_res.h).
    /// </summary>
    public enum TradeClientKind : uint
    {
        Start       = 0,
        Cancel      = 1,
        Make        = 2,
        MakeCancel  = 3
    }

    /// <summary>
    /// Guild or NPC shop operational status.
    /// Protocol specification referenced from LandSandBoat (https://github.com/LandSandBoat/server/blob/base/src/map/packets/s2c/0x086_guild_open.h).
    /// </summary>
    public enum ShopOpenStatus : byte
    {
        Open    = 0,
        Close   = 1,
        Holiday = 2
    }

    /// <summary>
    /// Bazaar purchase state for S2C 0x106.
    /// Protocol specification referenced from LandSandBoat (https://github.com/LandSandBoat/server/blob/base/src/map/packets/s2c/0x106_bazaar_buy.h).
    /// </summary>
    public enum BazaarBuyState : uint
    {
        Ok  = 0,
        Err = 1,
        End = 2
    }

    /// <summary>
    /// Bazaar visitor state for S2C 0x108.
    /// Protocol specification referenced from LandSandBoat (https://github.com/LandSandBoat/server/blob/base/src/map/packets/s2c/0x108_bazaar_shopping.h).
    /// </summary>
    public enum BazaarShoppingState : uint
    {
        Enter = 0,
        Exit  = 1,
        End   = 2
    }

    /// <summary>
    /// Auction House commands for S2C 0x04C and C2S 0x04E.
    /// Protocol specification referenced from LandSandBoat (https://github.com/LandSandBoat/server/blob/base/src/map/packets/c2s/0x04e_auc.h).
    /// </summary>
    public enum AuctionCommand : byte
    {
        Open      = 0x02,
        AskCommit = 0x04,
        Info      = 0x05,
        WorkCheck = 0x0A,
        LotIn     = 0x0B,
        LotCancel = 0x0C,
        LotCheck  = 0x0D,
        Bid       = 0x0E
    }

    /// <summary>
    /// Lockstyle command modes for C2S 0x053.
    /// Protocol specification referenced from LandSandBoat (https://github.com/LandSandBoat/server/blob/base/src/map/packets/c2s/0x053_lockstyle.h).
    /// </summary>
    public enum LockstyleMode : byte
    {
        Disable  = 0,
        Continue = 1,
        Query    = 2,
        Set      = 3,
        Enable   = 4
    }

    /// <summary>
    /// Subcontainer (mannequin) interaction mode for C2S 0x03B.
    /// Protocol specification referenced from LandSandBoat (https://github.com/LandSandBoat/server/blob/base/src/map/packets/c2s/0x03b_subcontainer.h).
    /// </summary>
    public enum SubcontainerKind : uint
    {
        Equip      = 1,
        Unequip    = 2,
        UnequipAll = 5
    }

    /// <summary>
    /// Subcontainer slot index for C2S 0x03B.
    /// Protocol specification referenced from LandSandBoat (https://github.com/LandSandBoat/server/blob/base/src/map/packets/c2s/0x03b_subcontainer.h).
    /// </summary>
    public enum SubcontainerSlotIndex : byte
    {
        MainWeapon   = 0,
        SubWeapon    = 1,
        RangedWeapon = 2,
        Head         = 3,
        Body         = 4,
        Hands        = 5,
        Legs         = 6,
        Feet         = 7
    }

    #endregion

    #region Inbound S2C Decoders

    /// <summary>
    /// S2C 0x01C: Container sizes and usable capacity.
    /// Protocol specification referenced from LandSandBoat (https://github.com/LandSandBoat/server/blob/base/src/map/packets/s2c/0x01c_item_max.h).
    /// </summary>
    public readonly ref struct S2C_0x01C_ItemMax
    {
        public const ushort PacketId = 0x01C;
        public const int ContainerCount = 18;

        public ReadOnlySpan<byte> MaxSizes { get; }
        public ReadOnlySpan<byte> UsableSizesPayload { get; }
        public bool IsValid { get; }

        public S2C_0x01C_ItemMax(ReadOnlySpan<byte> payload)
        {
            if (payload.Length < 68)
            {
                MaxSizes = ReadOnlySpan<byte>.Empty;
                UsableSizesPayload = ReadOnlySpan<byte>.Empty;
                IsValid = false;
                return;
            }

            MaxSizes = payload.Slice(0, ContainerCount);
            UsableSizesPayload = payload.Slice(32, ContainerCount * 2);
            IsValid = true;
        }

        public byte GetMaxSize(ContainerId container)
        {
            int index = (int)container;
            if (index < 0 || index >= ContainerCount || !IsValid) return 0;
            byte val = MaxSizes[index];
            return val > 0 ? (byte)(val - 1) : (byte)0;
        }

        public ushort GetUsableSize(ContainerId container)
        {
            int index = (int)container;
            if (index < 0 || index >= ContainerCount || !IsValid) return 0;
            ushort val = BinaryPrimitives.ReadUInt16LittleEndian(UsableSizesPayload.Slice(index * 2, 2));
            return val > 0 ? (ushort)(val - 1) : (ushort)0;
        }
    }

    /// <summary>
    /// S2C 0x01D: Container loading sync state.
    /// Protocol specification referenced from LandSandBoat (https://github.com/LandSandBoat/server/blob/base/src/map/packets/s2c/0x01d_item_same.h).
    /// </summary>
    public readonly ref struct S2C_0x01D_ItemSame
    {
        public const ushort PacketId = 0x01D;

        public ItemSameState State { get; }
        public uint Flags { get; }
        public bool IsValid { get; }

        public S2C_0x01D_ItemSame(ReadOnlySpan<byte> payload)
        {
            if (payload.Length < 8)
            {
                State = ItemSameState.StillLoading;
                Flags = 0;
                IsValid = false;
                return;
            }

            State = (ItemSameState)payload[0];
            Flags = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(4, 4));
            IsValid = true;
        }
    }

    /// <summary>
    /// S2C 0x01E: Single item count/lock update in a container slot.
    /// Protocol specification referenced from LandSandBoat (https://github.com/LandSandBoat/server/blob/base/src/map/packets/s2c/0x01e_item_num.h).
    /// </summary>
    public readonly ref struct S2C_0x01E_ItemNum
    {
        public const ushort PacketId = 0x01E;

        public uint Count { get; }
        public ContainerId Container { get; }
        public byte Slot { get; }
        public ItemLockFlag LockFlag { get; }
        public bool IsValid { get; }

        public S2C_0x01E_ItemNum(ReadOnlySpan<byte> payload)
        {
            if (payload.Length < 8)
            {
                Count = 0;
                Container = ContainerId.Inventory;
                Slot = 0;
                LockFlag = ItemLockFlag.Normal;
                IsValid = false;
                return;
            }

            Count = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(0, 4));
            Container = (ContainerId)payload[4];
            Slot = payload[5];
            LockFlag = (ItemLockFlag)payload[6];
            IsValid = true;
        }
    }

    /// <summary>
    /// S2C 0x01F: Item entry definition within a container.
    /// Protocol specification referenced from LandSandBoat (https://github.com/LandSandBoat/server/blob/base/src/map/packets/s2c/0x01f_item_list.h).
    /// </summary>
    public readonly ref struct S2C_0x01F_ItemList
    {
        public const ushort PacketId = 0x01F;

        public uint Count { get; }
        public ushort ItemId { get; }
        public ContainerId Container { get; }
        public byte Slot { get; }
        public ItemLockFlag LockFlag { get; }
        public bool IsValid { get; }

        public S2C_0x01F_ItemList(ReadOnlySpan<byte> payload)
        {
            if (payload.Length < 12)
            {
                Count = 0;
                ItemId = 0;
                Container = ContainerId.Inventory;
                Slot = 0;
                LockFlag = ItemLockFlag.Normal;
                IsValid = false;
                return;
            }

            Count = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(0, 4));
            ItemId = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(4, 2));
            Container = (ContainerId)payload[6];
            Slot = payload[7];
            LockFlag = (ItemLockFlag)payload[8];
            IsValid = true;
        }
    }

    /// <summary>
    /// S2C 0x020: Full item details including bazaar price and 24-byte extdata/augments.
    /// Protocol specification referenced from LandSandBoat (https://github.com/LandSandBoat/server/blob/base/src/map/packets/s2c/0x020_item_attr.h).
    /// </summary>
    public readonly ref struct S2C_0x020_ItemAttr
    {
        public const ushort PacketId = 0x020;

        public uint Count { get; }
        public uint Price { get; }
        public ushort ItemId { get; }
        public ContainerId Container { get; }
        public byte Slot { get; }
        public ItemLockFlag LockFlag { get; }
        public ReadOnlySpan<byte> ExtData { get; }
        public bool IsValid { get; }

        public S2C_0x020_ItemAttr(ReadOnlySpan<byte> payload)
        {
            if (payload.Length < 36)
            {
                Count = 0;
                Price = 0;
                ItemId = 0;
                Container = ContainerId.Inventory;
                Slot = 0;
                LockFlag = ItemLockFlag.Normal;
                ExtData = ReadOnlySpan<byte>.Empty;
                IsValid = false;
                return;
            }

            Count = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(0, 4));
            Price = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(4, 4));
            ItemId = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(8, 2));
            Container = (ContainerId)payload[10];
            Slot = payload[11];
            LockFlag = (ItemLockFlag)payload[12];
            int extLen = Math.Min(24, Math.Max(0, payload.Length - 13));
            ExtData = payload.Slice(13, extLen);
            IsValid = true;
        }
    }

    /// <summary>
    /// S2C 0x021: Trade request from another player.
    /// Protocol specification referenced from LandSandBoat (https://github.com/LandSandBoat/server/blob/base/src/map/packets/s2c/0x021_item_trade_req.h).
    /// </summary>
    public readonly ref struct S2C_0x021_ItemTradeReq
    {
        public const ushort PacketId = 0x021;

        public uint UniqueNo { get; }
        public ushort ActIndex { get; }
        public bool IsValid { get; }

        public S2C_0x021_ItemTradeReq(ReadOnlySpan<byte> payload)
        {
            if (payload.Length < 8)
            {
                UniqueNo = 0;
                ActIndex = 0;
                IsValid = false;
                return;
            }

            UniqueNo = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(0, 4));
            ActIndex = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(4, 2));
            IsValid = true;
        }
    }

    /// <summary>
    /// S2C 0x022: Trade response or lifecycle event.
    /// Protocol specification referenced from LandSandBoat (https://github.com/LandSandBoat/server/blob/base/src/map/packets/s2c/0x022_item_trade_res.h).
    /// </summary>
    public readonly ref struct S2C_0x022_ItemTradeRes
    {
        public const ushort PacketId = 0x022;

        public uint UniqueNo { get; }
        public TradeResultKind Kind { get; }
        public ushort ActIndex { get; }
        public bool IsValid { get; }

        public S2C_0x022_ItemTradeRes(ReadOnlySpan<byte> payload)
        {
            if (payload.Length < 10)
            {
                UniqueNo = 0;
                Kind = TradeResultKind.End;
                ActIndex = 0;
                IsValid = false;
                return;
            }

            UniqueNo = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(0, 4));
            Kind = (TradeResultKind)BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(4, 4));
            ActIndex = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(8, 2));
            IsValid = true;
        }
    }

    /// <summary>
    /// S2C 0x023: Trade item offered by the other party.
    /// Protocol specification referenced from LandSandBoat (https://github.com/LandSandBoat/server/blob/base/src/map/packets/s2c/0x023_item_trade_list.h).
    /// </summary>
    public readonly ref struct S2C_0x023_ItemTradeList
    {
        public const ushort PacketId = 0x023;

        public uint Count { get; }
        public ushort TradeCounter { get; }
        public ushort ItemId { get; }
        public byte ItemFreeSpaceNum { get; }
        public byte TradeIndex { get; }
        public ReadOnlySpan<byte> ExtData { get; }
        public bool IsValid { get; }

        public S2C_0x023_ItemTradeList(ReadOnlySpan<byte> payload)
        {
            if (payload.Length < 34)
            {
                Count = 0;
                TradeCounter = 0;
                ItemId = 0;
                ItemFreeSpaceNum = 0;
                TradeIndex = 0;
                ExtData = ReadOnlySpan<byte>.Empty;
                IsValid = false;
                return;
            }

            Count = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(0, 4));
            TradeCounter = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(4, 2));
            ItemId = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(6, 2));
            ItemFreeSpaceNum = payload[8];
            TradeIndex = payload[9];
            ExtData = payload.Slice(10, 24);
            IsValid = true;
        }
    }

    /// <summary>
    /// S2C 0x025: Trade item update for local player's offer.
    /// Protocol specification referenced from LandSandBoat (https://github.com/LandSandBoat/server/blob/base/src/map/packets/s2c/0x025_item_trade_mylist.h).
    /// </summary>
    public readonly ref struct S2C_0x025_ItemTradeMyList
    {
        public const ushort PacketId = 0x025;

        public uint Count { get; }
        public ushort ItemId { get; }
        public byte TradeIndex { get; }
        public byte Slot { get; }
        public bool IsValid { get; }

        public S2C_0x025_ItemTradeMyList(ReadOnlySpan<byte> payload)
        {
            if (payload.Length < 8)
            {
                Count = 0;
                ItemId = 0;
                TradeIndex = 0;
                Slot = 0;
                IsValid = false;
                return;
            }

            Count = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(0, 4));
            ItemId = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(4, 2));
            TradeIndex = payload[6];
            Slot = payload[7];
            IsValid = true;
        }
    }

    /// <summary>
    /// S2C 0x026: Subcontainer and mannequin equipment display info.
    /// Protocol specification referenced from LandSandBoat (https://github.com/LandSandBoat/server/blob/base/src/map/packets/s2c/0x026_item_subcontainer.h).
    /// </summary>
    public readonly ref struct S2C_0x026_ItemSubcontainer
    {
        public const ushort PacketId = 0x026;

        public bool IsUsed { get; }
        public ContainerId Container { get; }
        public byte Slot { get; }
        public ushort ModelIdHead { get; }
        public ushort ModelIdBody { get; }
        public ushort ModelIdHands { get; }
        public ushort ModelIdLegs { get; }
        public ushort ModelIdFeet { get; }
        public ushort ModelIdMain { get; }
        public ushort ModelIdSub { get; }
        public ushort ModelIdRange { get; }
        public byte Race { get; }
        public byte Pose { get; }
        public bool IsValid { get; }

        public S2C_0x026_ItemSubcontainer(ReadOnlySpan<byte> payload)
        {
            if (payload.Length < 24)
            {
                IsUsed = false;
                Container = ContainerId.Inventory;
                Slot = 0;
                ModelIdHead = 0;
                ModelIdBody = 0;
                ModelIdHands = 0;
                ModelIdLegs = 0;
                ModelIdFeet = 0;
                ModelIdMain = 0;
                ModelIdSub = 0;
                ModelIdRange = 0;
                Race = 0;
                Pose = 0;
                IsValid = false;
                return;
            }

            IsUsed = payload[0] != 0;
            Container = (ContainerId)payload[1];
            Slot = payload[2];
            ModelIdHead = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(6, 2));
            ModelIdBody = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(8, 2));
            ModelIdHands = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(10, 2));
            ModelIdLegs = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(12, 2));
            ModelIdFeet = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(14, 2));
            ModelIdMain = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(16, 2));
            ModelIdSub = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(18, 2));
            ModelIdRange = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(20, 2));
            Race = payload[22];
            Pose = payload[23];
            IsValid = true;
        }
    }

    /// <summary>
    /// S2C 0x03C: Shop item list.
    /// Protocol specification referenced from LandSandBoat (https://github.com/LandSandBoat/server/blob/base/src/map/packets/s2c/0x03c_shop_list.h).
    /// </summary>
    public readonly ref struct S2C_0x03C_ShopList
    {
        public const ushort PacketId = 0x03C;

        public ushort ShopItemOffsetIndex { get; }
        public byte Flags { get; }
        public int ItemCount { get; }
        public ReadOnlySpan<byte> ItemsPayload { get; }
        public bool IsValid { get; }

        public S2C_0x03C_ShopList(ReadOnlySpan<byte> payload)
        {
            if (payload.Length < 4)
            {
                ShopItemOffsetIndex = 0;
                Flags = 0;
                ItemCount = 0;
                ItemsPayload = ReadOnlySpan<byte>.Empty;
                IsValid = false;
                return;
            }

            ShopItemOffsetIndex = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(0, 2));
            Flags = payload[2];
            ItemsPayload = payload.Slice(4);
            ItemCount = Math.Min(ItemsPayload.Length / 12, 19);
            IsValid = true;
        }

        public (uint Price, ushort ItemId, byte ShopIndex, ushort Skill, ushort GuildInfo) GetItem(int index)
        {
            if (index < 0 || index >= ItemCount) return (0, 0, 0, 0, 0);
            var slice = ItemsPayload.Slice(index * 12, 12);
            uint price = BinaryPrimitives.ReadUInt32LittleEndian(slice.Slice(0, 4));
            ushort itemId = BinaryPrimitives.ReadUInt16LittleEndian(slice.Slice(4, 2));
            byte shopIndex = slice[6];
            ushort skill = BinaryPrimitives.ReadUInt16LittleEndian(slice.Slice(8, 2));
            ushort guildInfo = BinaryPrimitives.ReadUInt16LittleEndian(slice.Slice(10, 2));
            return (price, itemId, shopIndex, skill, guildInfo);
        }
    }

    /// <summary>
    /// S2C 0x03D: Shop item appraisal price or completed sale result.
    /// Protocol specification referenced from LandSandBoat (https://github.com/LandSandBoat/server/blob/base/src/map/packets/s2c/0x03d_shop_sell.h).
    /// </summary>
    public readonly ref struct S2C_0x03D_ShopSell
    {
        public const ushort PacketId = 0x03D;

        public uint Price { get; }
        public byte Slot { get; }
        public byte Type { get; }
        public uint Count { get; }
        public bool IsValid { get; }

        public S2C_0x03D_ShopSell(ReadOnlySpan<byte> payload)
        {
            if (payload.Length < 12)
            {
                Price = 0;
                Slot = 0;
                Type = 0;
                Count = 0;
                IsValid = false;
                return;
            }

            Price = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(0, 4));
            Slot = payload[4];
            Type = payload[5];
            Count = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(8, 4));
            IsValid = true;
        }
    }

    /// <summary>
    /// S2C 0x03E: Open shop window notice.
    /// Protocol specification referenced from LandSandBoat (https://github.com/LandSandBoat/server/blob/base/src/map/packets/s2c/0x03e_shop_open.h).
    /// </summary>
    public readonly ref struct S2C_0x03E_ShopOpen
    {
        public const ushort PacketId = 0x03E;

        public ushort ShopListNum { get; }
        public bool IsValid { get; }

        public S2C_0x03E_ShopOpen(ReadOnlySpan<byte> payload)
        {
            if (payload.Length < 2)
            {
                ShopListNum = 0;
                IsValid = false;
                return;
            }

            ShopListNum = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(0, 2));
            IsValid = true;
        }
    }

    /// <summary>
    /// S2C 0x03F: Shop purchase confirmation.
    /// Protocol specification referenced from LandSandBoat (https://github.com/LandSandBoat/server/blob/base/src/map/packets/s2c/0x03f_shop_buy.h).
    /// </summary>
    public readonly ref struct S2C_0x03F_ShopBuy
    {
        public const ushort PacketId = 0x03F;

        public ushort ShopItemIndex { get; }
        public byte BuyState { get; }
        public uint Count { get; }
        public bool IsValid { get; }

        public S2C_0x03F_ShopBuy(ReadOnlySpan<byte> payload)
        {
            if (payload.Length < 8)
            {
                ShopItemIndex = 0;
                BuyState = 0;
                Count = 0;
                IsValid = false;
                return;
            }

            ShopItemIndex = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(0, 2));
            BuyState = payload[2];
            Count = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(4, 4));
            IsValid = true;
        }
    }

    /// <summary>
    /// S2C 0x04C: Auction house message and search results.
    /// Protocol specification referenced from LandSandBoat (https://github.com/LandSandBoat/server/blob/base/src/map/packets/s2c/0x04c_auc.h).
    /// </summary>
    public readonly ref struct S2C_0x04C_Auc
    {
        public const ushort PacketId = 0x04C;

        public AuctionCommand Command { get; }
        public sbyte AucWorkIndex { get; }
        public sbyte Result { get; }
        public sbyte ResultStatus { get; }
        public ushort ItemId { get; }
        public uint Price { get; }
        public uint Count { get; }
        public string SellerName { get; }
        public bool IsValid { get; }

        public S2C_0x04C_Auc(ReadOnlySpan<byte> payload)
        {
            if (payload.Length < 20)
            {
                Command = AuctionCommand.Open;
                AucWorkIndex = 0;
                Result = 0;
                ResultStatus = 0;
                ItemId = 0;
                Price = 0;
                Count = 0;
                SellerName = string.Empty;
                IsValid = false;
                return;
            }

            Command = (AuctionCommand)payload[0];
            AucWorkIndex = (sbyte)payload[1];
            Result = (sbyte)payload[2];
            ResultStatus = (sbyte)payload[3];

            if (payload.Length >= 48)
            {
                var parcel = payload.Slice(16);
                var nameBytes = parcel.Slice(4, 16);
                int nullIdx = nameBytes.IndexOf((byte)0);
                SellerName = nullIdx >= 0 ? Encoding.ASCII.GetString(nameBytes.Slice(0, nullIdx)) : Encoding.ASCII.GetString(nameBytes);
                ItemId = BinaryPrimitives.ReadUInt16LittleEndian(parcel.Slice(20, 2));
                Count = parcel[22];
                Price = BinaryPrimitives.ReadUInt32LittleEndian(parcel.Slice(24, 4));
            }
            else
            {
                SellerName = string.Empty;
                ItemId = 0;
                Count = 0;
                Price = 0;
            }

            IsValid = true;
        }
    }

    /// <summary>
    /// S2C 0x050: Equipped gear slot change update.
    /// Protocol specification referenced from LandSandBoat (https://github.com/LandSandBoat/server/blob/base/src/map/packets/s2c/0x050_equip_list.h).
    /// </summary>
    public readonly ref struct S2C_0x050_EquipList
    {
        public const ushort PacketId = 0x050;

        public byte Slot { get; }
        public EquipSlotId EquipSlot { get; }
        public ContainerId Container { get; }
        public bool IsValid { get; }

        public S2C_0x050_EquipList(ReadOnlySpan<byte> payload)
        {
            if (payload.Length < 3)
            {
                Slot = 0;
                EquipSlot = EquipSlotId.Main;
                Container = ContainerId.Inventory;
                IsValid = false;
                return;
            }

            Slot = payload[0];
            EquipSlot = (EquipSlotId)payload[1];
            Container = (ContainerId)payload[2];
            IsValid = true;
        }
    }

    /// <summary>
    /// S2C 0x082: Guild shop purchase confirmation.
    /// Protocol specification referenced from LandSandBoat (https://github.com/LandSandBoat/server/blob/base/src/map/packets/s2c/0x082_guild_buy.h).
    /// </summary>
    public readonly ref struct S2C_0x082_GuildBuy
    {
        public const ushort PacketId = 0x082;

        public ushort ItemId { get; }
        public byte Count { get; }
        public sbyte Trade { get; }
        public bool IsValid { get; }

        public S2C_0x082_GuildBuy(ReadOnlySpan<byte> payload)
        {
            if (payload.Length < 4)
            {
                ItemId = 0;
                Count = 0;
                Trade = 0;
                IsValid = false;
                return;
            }

            ItemId = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(0, 2));
            Count = payload[2];
            Trade = (sbyte)payload[3];
            IsValid = true;
        }
    }

    /// <summary>
    /// S2C 0x083: Guild shop items available for purchase.
    /// Protocol specification referenced from LandSandBoat (https://github.com/LandSandBoat/server/blob/base/src/map/packets/s2c/0x083_guild_buylist.h).
    /// </summary>
    public readonly ref struct S2C_0x083_GuildBuyList
    {
        public const ushort PacketId = 0x083;

        public byte Count { get; }
        public byte Stat { get; }
        public ReadOnlySpan<byte> ItemsPayload { get; }
        public bool IsValid { get; }

        public S2C_0x083_GuildBuyList(ReadOnlySpan<byte> payload)
        {
            if (payload.Length < 242)
            {
                Count = 0;
                Stat = 0;
                ItemsPayload = ReadOnlySpan<byte>.Empty;
                IsValid = false;
                return;
            }

            ItemsPayload = payload.Slice(0, 240);
            Count = payload[240];
            Stat = payload[241];
            IsValid = true;
        }

        public (ushort ItemId, byte Stock, byte Max, int Price) GetItem(int index)
        {
            if (index < 0 || index >= Count || index >= 30) return (0, 0, 0, 0);
            var slice = ItemsPayload.Slice(index * 8, 8);
            ushort itemId = BinaryPrimitives.ReadUInt16LittleEndian(slice.Slice(0, 2));
            byte stock = slice[2];
            byte max = slice[3];
            int price = BinaryPrimitives.ReadInt32LittleEndian(slice.Slice(4, 4));
            return (itemId, stock, max, price);
        }
    }

    /// <summary>
    /// S2C 0x084: Guild shop sale confirmation.
    /// Protocol specification referenced from LandSandBoat (https://github.com/LandSandBoat/server/blob/base/src/map/packets/s2c/0x084_guild_sell.h).
    /// </summary>
    public readonly ref struct S2C_0x084_GuildSell
    {
        public const ushort PacketId = 0x084;

        public ushort ItemId { get; }
        public byte Count { get; }
        public sbyte Trade { get; }
        public bool IsValid { get; }

        public S2C_0x084_GuildSell(ReadOnlySpan<byte> payload)
        {
            if (payload.Length < 4)
            {
                ItemId = 0;
                Count = 0;
                Trade = 0;
                IsValid = false;
                return;
            }

            ItemId = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(0, 2));
            Count = payload[2];
            Trade = (sbyte)payload[3];
            IsValid = true;
        }
    }

    /// <summary>
    /// S2C 0x085: Guild shop items accepted for sale.
    /// Protocol specification referenced from LandSandBoat (https://github.com/LandSandBoat/server/blob/base/src/map/packets/s2c/0x085_guild_selllist.h).
    /// </summary>
    public readonly ref struct S2C_0x085_GuildSellList
    {
        public const ushort PacketId = 0x085;

        public byte Count { get; }
        public byte Stat { get; }
        public ReadOnlySpan<byte> ItemsPayload { get; }
        public bool IsValid { get; }

        public S2C_0x085_GuildSellList(ReadOnlySpan<byte> payload)
        {
            if (payload.Length < 242)
            {
                Count = 0;
                Stat = 0;
                ItemsPayload = ReadOnlySpan<byte>.Empty;
                IsValid = false;
                return;
            }

            ItemsPayload = payload.Slice(0, 240);
            Count = payload[240];
            Stat = payload[241];
            IsValid = true;
        }

        public (ushort ItemId, byte Stock, byte Max, int Price) GetItem(int index)
        {
            if (index < 0 || index >= Count || index >= 30) return (0, 0, 0, 0);
            var slice = ItemsPayload.Slice(index * 8, 8);
            ushort itemId = BinaryPrimitives.ReadUInt16LittleEndian(slice.Slice(0, 2));
            byte stock = slice[2];
            byte max = slice[3];
            int price = BinaryPrimitives.ReadInt32LittleEndian(slice.Slice(4, 4));
            return (itemId, stock, max, price);
        }
    }

    /// <summary>
    /// S2C 0x086: Guild operating status and closing time.
    /// Protocol specification referenced from LandSandBoat (https://github.com/LandSandBoat/server/blob/base/src/map/packets/s2c/0x086_guild_open.h).
    /// </summary>
    public readonly ref struct S2C_0x086_GuildOpen
    {
        public const ushort PacketId = 0x086;

        public ShopOpenStatus Status { get; }
        public uint Time { get; }
        public bool IsValid { get; }

        public S2C_0x086_GuildOpen(ReadOnlySpan<byte> payload)
        {
            if (payload.Length < 8)
            {
                Status = ShopOpenStatus.Close;
                Time = 0;
                IsValid = false;
                return;
            }

            Status = (ShopOpenStatus)payload[0];
            Time = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(4, 4));
            IsValid = true;
        }
    }

    /// <summary>
    /// S2C 0x105: Target player's bazaar item listing.
    /// Protocol specification referenced from LandSandBoat (https://github.com/LandSandBoat/server/blob/base/src/map/packets/s2c/0x105_bazaar_list.h).
    /// </summary>
    public readonly ref struct S2C_0x105_BazaarList
    {
        public const ushort PacketId = 0x105;

        public uint Price { get; }
        public uint Count { get; }
        public ushort TaxRate { get; }
        public ushort ItemId { get; }
        public byte ItemIndex { get; }
        public ReadOnlySpan<byte> ExtData { get; }
        public bool IsValid { get; }

        public S2C_0x105_BazaarList(ReadOnlySpan<byte> payload)
        {
            if (payload.Length < 38)
            {
                Price = 0;
                Count = 0;
                TaxRate = 0;
                ItemId = 0;
                ItemIndex = 0;
                ExtData = ReadOnlySpan<byte>.Empty;
                IsValid = false;
                return;
            }

            Price = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(0, 4));
            Count = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(4, 4));
            TaxRate = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(8, 2));
            ItemId = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(10, 2));
            ItemIndex = payload[12];
            ExtData = payload.Slice(13, 24);
            IsValid = true;
        }
    }

    /// <summary>
    /// S2C 0x106: Bazaar purchase status.
    /// Protocol specification referenced from LandSandBoat (https://github.com/LandSandBoat/server/blob/base/src/map/packets/s2c/0x106_bazaar_buy.h).
    /// </summary>
    public readonly ref struct S2C_0x106_BazaarBuy
    {
        public const ushort PacketId = 0x106;

        public BazaarBuyState State { get; }
        public string TargetName { get; }
        public bool IsValid { get; }

        public S2C_0x106_BazaarBuy(ReadOnlySpan<byte> payload)
        {
            if (payload.Length < 20)
            {
                State = BazaarBuyState.Err;
                TargetName = string.Empty;
                IsValid = false;
                return;
            }

            State = (BazaarBuyState)BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(0, 4));
            var nameSlice = payload.Slice(4, 16);
            int nullIdx = nameSlice.IndexOf((byte)0);
            TargetName = nullIdx >= 0 ? Encoding.ASCII.GetString(nameSlice.Slice(0, nullIdx)) : Encoding.ASCII.GetString(nameSlice);
            IsValid = true;
        }
    }

    /// <summary>
    /// S2C 0x107: Bazaar closed while viewing.
    /// Protocol specification referenced from LandSandBoat (https://github.com/LandSandBoat/server/blob/base/src/map/packets/s2c/0x107_bazaar_close.h).
    /// </summary>
    public readonly ref struct S2C_0x107_BazaarClose
    {
        public const ushort PacketId = 0x107;

        public string SellerName { get; }
        public bool IsValid { get; }

        public S2C_0x107_BazaarClose(ReadOnlySpan<byte> payload)
        {
            if (payload.Length < 16)
            {
                SellerName = string.Empty;
                IsValid = false;
                return;
            }

            var nameSlice = payload.Slice(0, 16);
            int nullIdx = nameSlice.IndexOf((byte)0);
            SellerName = nullIdx >= 0 ? Encoding.ASCII.GetString(nameSlice.Slice(0, nullIdx)) : Encoding.ASCII.GetString(nameSlice);
            IsValid = true;
        }
    }

    /// <summary>
    /// S2C 0x108: Player entered or left local client's bazaar.
    /// Protocol specification referenced from LandSandBoat (https://github.com/LandSandBoat/server/blob/base/src/map/packets/s2c/0x108_bazaar_shopping.h).
    /// </summary>
    public readonly ref struct S2C_0x108_BazaarShopping
    {
        public const ushort PacketId = 0x108;

        public uint UniqueNo { get; }
        public BazaarShoppingState State { get; }
        public byte HideLevel { get; }
        public ushort ActIndex { get; }
        public string BuyerName { get; }
        public bool IsValid { get; }

        public S2C_0x108_BazaarShopping(ReadOnlySpan<byte> payload)
        {
            if (payload.Length < 28)
            {
                UniqueNo = 0;
                State = BazaarShoppingState.End;
                HideLevel = 0;
                ActIndex = 0;
                BuyerName = string.Empty;
                IsValid = false;
                return;
            }

            UniqueNo = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(0, 4));
            State = (BazaarShoppingState)BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(4, 4));
            HideLevel = payload[8];
            ActIndex = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(10, 2));
            var nameSlice = payload.Slice(12, 16);
            int nullIdx = nameSlice.IndexOf((byte)0);
            BuyerName = nullIdx >= 0 ? Encoding.ASCII.GetString(nameSlice.Slice(0, nullIdx)) : Encoding.ASCII.GetString(nameSlice);
            IsValid = true;
        }
    }

    /// <summary>
    /// S2C 0x109: Another player purchased an item from local bazaar.
    /// Protocol specification referenced from LandSandBoat (https://github.com/LandSandBoat/server/blob/base/src/map/packets/s2c/0x109_bazaar_sell.h).
    /// </summary>
    public readonly ref struct S2C_0x109_BazaarSell
    {
        public const ushort PacketId = 0x109;

        public uint UniqueNo { get; }
        public uint Count { get; }
        public ushort ActIndex { get; }
        public ushort BazaarActIndex { get; }
        public string BuyerName { get; }
        public byte Slot { get; }
        public bool IsValid { get; }

        public S2C_0x109_BazaarSell(ReadOnlySpan<byte> payload)
        {
            if (payload.Length < 32)
            {
                UniqueNo = 0;
                Count = 0;
                ActIndex = 0;
                BazaarActIndex = 0;
                BuyerName = string.Empty;
                Slot = 0;
                IsValid = false;
                return;
            }

            UniqueNo = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(0, 4));
            Count = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(4, 4));
            ActIndex = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(8, 2));
            BazaarActIndex = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(10, 2));
            var nameSlice = payload.Slice(12, 16);
            int nullIdx = nameSlice.IndexOf((byte)0);
            BuyerName = nullIdx >= 0 ? Encoding.ASCII.GetString(nameSlice.Slice(0, nullIdx)) : Encoding.ASCII.GetString(nameSlice);
            Slot = payload[28];
            IsValid = true;
        }
    }

    /// <summary>
    /// S2C 0x10A: Item successfully sold from personal bazaar notification.
    /// Protocol specification referenced from LandSandBoat (https://github.com/LandSandBoat/server/blob/base/src/map/packets/s2c/0x10a_bazaar_sale.h).
    /// </summary>
    public readonly ref struct S2C_0x10A_BazaarSale
    {
        public const ushort PacketId = 0x10A;

        public uint Count { get; }
        public ushort ItemId { get; }
        public string BuyerName { get; }
        public bool IsValid { get; }

        public S2C_0x10A_BazaarSale(ReadOnlySpan<byte> payload)
        {
            if (payload.Length < 24)
            {
                Count = 0;
                ItemId = 0;
                BuyerName = string.Empty;
                IsValid = false;
                return;
            }

            Count = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(0, 4));
            ItemId = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(4, 2));
            var nameSlice = payload.Slice(6, 16);
            int nullIdx = nameSlice.IndexOf((byte)0);
            BuyerName = nullIdx >= 0 ? Encoding.ASCII.GetString(nameSlice.Slice(0, nullIdx)) : Encoding.ASCII.GetString(nameSlice);
            IsValid = true;
        }
    }

    /// <summary>
    /// S2C 0x113: Player currency update 1 (Sparks, Accolades, Beastcoins, Fewell, Guild Points, Seals, etc.).
    /// Protocol specification referenced from LandSandBoat (https://github.com/LandSandBoat/server/blob/base/src/map/packets/s2c/0x113_currencies_1.h).
    /// </summary>
    public readonly ref struct S2C_0x113_Currencies1
    {
        public const ushort PacketId = 0x113;

        public int ConquestSandoria { get; }
        public int ConquestBastok { get; }
        public int ConquestWindurst { get; }
        public ushort BeastmansSeals { get; }
        public ushort KindredsSeals { get; }
        public ushort KindredsCrests { get; }
        public ushort HighKindredsCrests { get; }
        public ushort SacredKindredsCrests { get; }
        public ushort AncientBeastcoins { get; }
        public ushort ValorPoints { get; }
        public ushort Scylds { get; }
        public int SparksOfEminence { get; }
        public int ImperialStanding { get; }
        public int AlliedNotes { get; }
        public ushort LoginPoints { get; }
        public int Cruor { get; }
        public int UnityAccolades { get; }
        public ushort Deeds { get; }
        public bool IsValid { get; }

        public S2C_0x113_Currencies1(ReadOnlySpan<byte> payload)
        {
            if (payload.Length < 28)
            {
                ConquestSandoria = 0;
                ConquestBastok = 0;
                ConquestWindurst = 0;
                BeastmansSeals = 0;
                KindredsSeals = 0;
                KindredsCrests = 0;
                HighKindredsCrests = 0;
                SacredKindredsCrests = 0;
                AncientBeastcoins = 0;
                ValorPoints = 0;
                Scylds = 0;
                SparksOfEminence = 0;
                ImperialStanding = 0;
                AlliedNotes = 0;
                LoginPoints = 0;
                Cruor = 0;
                UnityAccolades = 0;
                Deeds = 0;
                IsValid = false;
                return;
            }

            ConquestSandoria = payload.Length >= 4 ? BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(0, 4)) : 0;
            ConquestBastok = payload.Length >= 8 ? BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(4, 4)) : 0;
            ConquestWindurst = payload.Length >= 12 ? BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(8, 4)) : 0;
            BeastmansSeals = payload.Length >= 14 ? BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(12, 2)) : (ushort)0;
            KindredsSeals = payload.Length >= 16 ? BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(14, 2)) : (ushort)0;
            KindredsCrests = payload.Length >= 18 ? BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(16, 2)) : (ushort)0;
            HighKindredsCrests = payload.Length >= 20 ? BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(18, 2)) : (ushort)0;
            SacredKindredsCrests = payload.Length >= 22 ? BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(20, 2)) : (ushort)0;
            AncientBeastcoins = payload.Length >= 24 ? BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(22, 2)) : (ushort)0;
            ValorPoints = payload.Length >= 26 ? BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(24, 2)) : (ushort)0;
            Scylds = payload.Length >= 28 ? BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(26, 2)) : (ushort)0;
            SparksOfEminence = payload.Length >= 116 ? BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(112, 4)) : 0;
            ImperialStanding = payload.Length >= 124 ? BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(120, 4)) : 0;
            AlliedNotes = payload.Length >= 164 ? BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(160, 4)) : 0;
            LoginPoints = payload.Length >= 168 ? BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(166, 2)) : (ushort)0;
            Cruor = payload.Length >= 172 ? BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(168, 4)) : 0;
            UnityAccolades = payload.Length >= 228 ? BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(224, 4)) : 0;
            Deeds = payload.Length >= 246 ? BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(244, 2)) : (ushort)0;
            IsValid = true;
        }
    }

    /// <summary>
    /// S2C 0x116: Equipment set item validation confirmation.
    /// Protocol specification referenced from LandSandBoat (https://github.com/LandSandBoat/server/blob/base/src/map/packets/s2c/0x116_equipset_valid.h).
    /// </summary>
    public readonly ref struct S2C_0x116_EquipsetValid
    {
        public const ushort PacketId = 0x116;

        public ReadOnlySpan<byte> ItemsPayload { get; }
        public bool IsValid { get; }

        public S2C_0x116_EquipsetValid(ReadOnlySpan<byte> payload)
        {
            if (payload.Length < 68)
            {
                ItemsPayload = ReadOnlySpan<byte>.Empty;
                IsValid = false;
                return;
            }

            ItemsPayload = payload.Slice(0, 68);
            IsValid = true;
        }

        public (bool HasItem, bool RemoveItem, ContainerId Container, byte ItemIndex, ushort ItemId) GetItem(int index)
        {
            if (index < 0 || index >= 17 || !IsValid) return (false, false, ContainerId.Inventory, 0, 0);
            var slice = ItemsPayload.Slice(index * 4, 4);
            byte flags = slice[0];
            bool hasItem = (flags & 0x01) != 0;
            bool removeItem = (flags & 0x02) != 0;
            ContainerId container = (ContainerId)((flags >> 2) & 0x3F);
            byte itemIndex = slice[1];
            ushort itemId = BinaryPrimitives.ReadUInt16LittleEndian(slice.Slice(2, 2));
            return (hasItem, removeItem, container, itemIndex, itemId);
        }
    }

    /// <summary>
    /// S2C 0x117: Equipment set change result.
    /// Protocol specification referenced from LandSandBoat (https://github.com/LandSandBoat/server/blob/base/src/map/packets/s2c/0x117_equipset_res.h).
    /// </summary>
    public readonly ref struct S2C_0x117_EquipsetRes
    {
        public const ushort PacketId = 0x117;

        public byte Count { get; }
        public bool IsValid { get; }

        public S2C_0x117_EquipsetRes(ReadOnlySpan<byte> payload)
        {
            if (payload.Length < 4)
            {
                Count = 0;
                IsValid = false;
                return;
            }

            Count = payload[0];
            IsValid = true;
        }
    }

    /// <summary>
    /// S2C 0x118: Player currency update 2 (Bayld, Kinetic units, Silt, Beads, Hallmarks, Stones, etc.).
    /// Protocol specification referenced from LandSandBoat (https://github.com/LandSandBoat/server/blob/base/src/map/packets/s2c/0x118_currencies_2.h).
    /// </summary>
    public readonly ref struct S2C_0x118_Currencies2
    {
        public const ushort PacketId = 0x118;

        public int Bayld { get; }
        public ushort KineticUnits { get; }
        public byte CoalitionImprimaturs { get; }
        public int ObsidianFragments { get; }
        public int MweyaPlasm { get; }
        public ushort EschaBeads { get; }
        public int EschaSilt { get; }
        public int Potpourri { get; }
        public int Hallmarks { get; }
        public int TotalHallmarks { get; }
        public int BadgesOfGallantry { get; }
        public int DomainPoints { get; }
        public int MogSegments { get; }
        public int Gallimaufry { get; }
        public bool IsValid { get; }

        public S2C_0x118_Currencies2(ReadOnlySpan<byte> payload)
        {
            if (payload.Length < 20)
            {
                Bayld = 0;
                KineticUnits = 0;
                CoalitionImprimaturs = 0;
                ObsidianFragments = 0;
                MweyaPlasm = 0;
                EschaBeads = 0;
                EschaSilt = 0;
                Potpourri = 0;
                Hallmarks = 0;
                TotalHallmarks = 0;
                BadgesOfGallantry = 0;
                DomainPoints = 0;
                MogSegments = 0;
                Gallimaufry = 0;
                IsValid = false;
                return;
            }

            Bayld = payload.Length >= 4 ? BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(0, 4)) : 0;
            KineticUnits = payload.Length >= 6 ? BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(4, 2)) : (ushort)0;
            CoalitionImprimaturs = payload.Length >= 7 ? payload[6] : (byte)0;
            ObsidianFragments = payload.Length >= 12 ? BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(8, 4)) : 0;
            MweyaPlasm = payload.Length >= 20 ? BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(16, 4)) : 0;
            EschaBeads = payload.Length >= 72 ? BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(70, 2)) : (ushort)0;
            EschaSilt = payload.Length >= 76 ? BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(72, 4)) : 0;
            Potpourri = payload.Length >= 80 ? BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(76, 4)) : 0;
            Hallmarks = payload.Length >= 84 ? BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(80, 4)) : 0;
            TotalHallmarks = payload.Length >= 88 ? BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(84, 4)) : 0;
            BadgesOfGallantry = payload.Length >= 92 ? BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(88, 4)) : 0;
            DomainPoints = payload.Length >= 132 ? BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(128, 4)) : 0;
            MogSegments = payload.Length >= 140 ? BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(136, 4)) : 0;
            Gallimaufry = payload.Length >= 144 ? BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(140, 4)) : 0;
            IsValid = true;
        }
    }

    #endregion

    #region Outbound C2S Builders

    /// <summary>
    /// Outbound C2S builder methods for Inventory & Economy actions.
    /// Operates completely zero-allocation directly into caller-provided spans.
    /// </summary>
    public static class InventoryPacketBuilders
    {
        /// <summary>
        /// C2S 0x028: Drops an item from inventory.
        /// Protocol specification referenced from LandSandBoat (https://github.com/LandSandBoat/server/blob/base/src/map/packets/c2s/0x028_item_dump.h).
        /// </summary>
        public static int BuildItemDump(Span<byte> destination, ushort sequenceId, uint count, ContainerId container, byte slot)
        {
            PacketHeader.Write(destination, 0x028, 3, sequenceId);
            var payload = destination.Slice(4, 8);
            payload.Clear();
            BinaryPrimitives.WriteUInt32LittleEndian(payload.Slice(0, 4), count);
            payload[4] = (byte)container;
            payload[5] = slot;
            return 12;
        }

        /// <summary>
        /// C2S 0x029: Moves an item between containers/slots.
        /// Protocol specification referenced from LandSandBoat (https://github.com/LandSandBoat/server/blob/base/src/map/packets/c2s/0x029_item_move.h).
        /// </summary>
        public static int BuildItemMove(Span<byte> destination, ushort sequenceId, uint count, ContainerId srcCont, byte srcSlot, ContainerId dstCont, byte dstSlot)
        {
            PacketHeader.Write(destination, 0x029, 3, sequenceId);
            var payload = destination.Slice(4, 8);
            payload.Clear();
            BinaryPrimitives.WriteUInt32LittleEndian(payload.Slice(0, 4), count);
            payload[4] = (byte)srcCont;
            payload[5] = (byte)dstCont;
            payload[6] = srcSlot;
            payload[7] = dstSlot;
            return 12;
        }

        /// <summary>
        /// C2S 0x032: Initiates a trade request with a target entity.
        /// Protocol specification referenced from LandSandBoat (https://github.com/LandSandBoat/server/blob/base/src/map/packets/c2s/0x032_trade_req.h).
        /// </summary>
        public static int BuildTradeReq(Span<byte> destination, ushort sequenceId, uint targetServerId, ushort targetIndex)
        {
            PacketHeader.Write(destination, 0x032, 3, sequenceId);
            var payload = destination.Slice(4, 8);
            payload.Clear();
            BinaryPrimitives.WriteUInt32LittleEndian(payload.Slice(0, 4), targetServerId);
            BinaryPrimitives.WriteUInt16LittleEndian(payload.Slice(4, 2), targetIndex);
            return 12;
        }

        /// <summary>
        /// C2S 0x033: Responds to a trade request or alters trade progress.
        /// Protocol specification referenced from LandSandBoat (https://github.com/LandSandBoat/server/blob/base/src/map/packets/c2s/0x033_trade_res.h).
        /// </summary>
        public static int BuildTradeRes(Span<byte> destination, ushort sequenceId, TradeClientKind kind, ushort tradeCounter)
        {
            PacketHeader.Write(destination, 0x033, 3, sequenceId);
            var payload = destination.Slice(4, 8);
            payload.Clear();
            BinaryPrimitives.WriteUInt32LittleEndian(payload.Slice(0, 4), (uint)kind);
            BinaryPrimitives.WriteUInt16LittleEndian(payload.Slice(4, 2), tradeCounter);
            return 12;
        }

        /// <summary>
        /// C2S 0x034: Sets an item into the trade window.
        /// Protocol specification referenced from LandSandBoat (https://github.com/LandSandBoat/server/blob/base/src/map/packets/c2s/0x034_trade_list.h).
        /// </summary>
        public static int BuildTradeList(Span<byte> destination, ushort sequenceId, uint count, ushort itemId, byte slot, byte tradeIndex)
        {
            PacketHeader.Write(destination, 0x034, 3, sequenceId);
            var payload = destination.Slice(4, 8);
            payload.Clear();
            BinaryPrimitives.WriteUInt32LittleEndian(payload.Slice(0, 4), count);
            BinaryPrimitives.WriteUInt16LittleEndian(payload.Slice(4, 2), itemId);
            payload[6] = slot;
            payload[7] = tradeIndex;
            return 12;
        }

        /// <summary>
        /// C2S 0x036: Completes a trade with an NPC (e.g. quest or delivery).
        /// Protocol specification referenced from LandSandBoat (https://github.com/LandSandBoat/server/blob/base/src/map/packets/c2s/0x036_item_transfer.h).
        /// </summary>
        public static int BuildItemTransfer(Span<byte> destination, ushort sequenceId, uint targetServerId, ushort targetIndex, ReadOnlySpan<(byte Slot, uint Count)> items)
        {
            PacketHeader.Write(destination, 0x036, 16, sequenceId);
            var payload = destination.Slice(4, 60);
            payload.Clear();

            BinaryPrimitives.WriteUInt32LittleEndian(payload.Slice(0, 4), targetServerId);
            int count = Math.Min(items.Length, 10);
            for (int i = 0; i < count; i++)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(payload.Slice(4 + (i * 4), 4), items[i].Count);
                payload[44 + i] = items[i].Slot;
            }

            BinaryPrimitives.WriteUInt16LittleEndian(payload.Slice(54, 2), targetIndex);
            payload[56] = (byte)count;
            return 64;
        }

        /// <summary>
        /// C2S 0x037: Uses an item.
        /// Protocol specification referenced from LandSandBoat (https://github.com/LandSandBoat/server/blob/base/src/map/packets/c2s/0x037_item_use.h).
        /// </summary>
        public static int BuildItemUse(Span<byte> destination, ushort sequenceId, uint targetServerId, ushort targetIndex, uint count, byte slot, ContainerId container)
        {
            PacketHeader.Write(destination, 0x037, 5, sequenceId);
            var payload = destination.Slice(4, 16);
            payload.Clear();

            BinaryPrimitives.WriteUInt32LittleEndian(payload.Slice(0, 4), targetServerId);
            BinaryPrimitives.WriteUInt32LittleEndian(payload.Slice(4, 4), count);
            BinaryPrimitives.WriteUInt16LittleEndian(payload.Slice(8, 2), targetIndex);
            payload[10] = slot;
            BinaryPrimitives.WriteUInt32LittleEndian(payload.Slice(12, 4), (uint)container);
            return 20;
        }

        /// <summary>
        /// C2S 0x03A: Requests sorting / auto-stacking of a container.
        /// Protocol specification referenced from LandSandBoat (https://github.com/LandSandBoat/server/blob/base/src/map/packets/c2s/0x03a_item_stack.h).
        /// </summary>
        public static int BuildItemStack(Span<byte> destination, ushort sequenceId, ContainerId container)
        {
            PacketHeader.Write(destination, 0x03A, 2, sequenceId);
            var payload = destination.Slice(4, 4);
            payload.Clear();
            BinaryPrimitives.WriteUInt32LittleEndian(payload.Slice(0, 4), (uint)container);
            return 8;
        }

        /// <summary>
        /// C2S 0x03B: Interacts with a subcontainer item (e.g. mannequin).
        /// Protocol specification referenced from LandSandBoat (https://github.com/LandSandBoat/server/blob/base/src/map/packets/c2s/0x03b_subcontainer.h).
        /// </summary>
        public static int BuildSubcontainer(
            Span<byte> destination,
            ushort sequenceId,
            SubcontainerKind kind,
            ContainerId mannequinCont,
            byte mannequinSlot,
            SubcontainerSlotIndex slotIndex,
            ContainerId equipCont,
            byte equipSlot)
        {
            PacketHeader.Write(destination, 0x03B, 8, sequenceId);
            var payload = destination.Slice(4, 28);
            payload.Clear();

            BinaryPrimitives.WriteUInt32LittleEndian(payload.Slice(0, 4), (uint)kind);
            BinaryPrimitives.WriteUInt32LittleEndian(payload.Slice(4, 4), (uint)mannequinCont);
            payload[8] = mannequinSlot;
            payload[9] = (byte)slotIndex;
            BinaryPrimitives.WriteUInt32LittleEndian(payload.Slice(12, 4), (uint)equipCont);
            payload[16] = equipSlot;
            return 32;
        }

        /// <summary>
        /// C2S 0x04E: Interacts with the Auction House.
        /// Protocol specification referenced from LandSandBoat (https://github.com/LandSandBoat/server/blob/base/src/map/packets/c2s/0x04e_auc.h).
        /// </summary>
        public static int BuildAuctionRequest(
            Span<byte> destination,
            ushort sequenceId,
            AuctionCommand command,
            sbyte aucWorkIndex,
            ushort itemId = 0,
            uint price = 0,
            uint count = 0)
        {
            PacketHeader.Write(destination, 0x04E, 13, sequenceId);
            var payload = destination.Slice(4, 48);
            payload.Clear();

            payload[0] = (byte)command;
            payload[1] = (byte)aucWorkIndex;

            if (command == AuctionCommand.Bid)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(payload.Slice(4, 4), price);
                BinaryPrimitives.WriteUInt16LittleEndian(payload.Slice(8, 2), itemId);
                BinaryPrimitives.WriteUInt32LittleEndian(payload.Slice(12, 4), count);
            }
            return 52;
        }

        /// <summary>
        /// C2S 0x050: Equips a single item into an equipment slot.
        /// Protocol specification referenced from LandSandBoat (https://github.com/LandSandBoat/server/blob/base/src/map/packets/c2s/0x050_equip_set.h).
        /// </summary>
        public static int BuildEquipSet(Span<byte> destination, ushort sequenceId, byte slot, EquipSlotId equipSlot, ContainerId container)
        {
            PacketHeader.Write(destination, 0x050, 2, sequenceId);
            var payload = destination.Slice(4, 4);
            payload.Clear();
            payload[0] = slot;
            payload[1] = (byte)equipSlot;
            payload[2] = (byte)container;
            return 8;
        }

        /// <summary>
        /// C2S 0x051: Equips an entire equipment set.
        /// Protocol specification referenced from LandSandBoat (https://github.com/LandSandBoat/server/blob/base/src/map/packets/c2s/0x051_equipset_set.h).
        /// </summary>
        public static int BuildEquipsetSet(Span<byte> destination, ushort sequenceId, ReadOnlySpan<(byte Slot, EquipSlotId EquipSlot, ContainerId Container)> items)
        {
            PacketHeader.Write(destination, 0x051, 18, sequenceId);
            var payload = destination.Slice(4, 68);
            payload.Clear();

            int count = Math.Min(items.Length, 16);
            payload[0] = (byte)count;
            for (int i = 0; i < count; i++)
            {
                int offset = 4 + (i * 4);
                payload[offset] = items[i].Slot;
                payload[offset + 1] = (byte)items[i].EquipSlot;
                payload[offset + 2] = (byte)items[i].Container;
            }
            return 72;
        }

        /// <summary>
        /// C2S 0x052: Validates changes to an equipment set.
        /// Protocol specification referenced from LandSandBoat (https://github.com/LandSandBoat/server/blob/base/src/map/packets/c2s/0x052_equipset_check.h).
        /// </summary>
        public static int BuildEquipsetCheck(Span<byte> destination, ushort sequenceId, EquipSlotId equipSlot, byte slot, ContainerId container, ushort itemId)
        {
            PacketHeader.Write(destination, 0x052, 19, sequenceId);
            var payload = destination.Slice(4, 72);
            payload.Clear();

            payload[0] = (byte)equipSlot;
            byte flags = (byte)(((byte)container & 0x3F) << 2 | 0x01);
            payload[4] = flags;
            payload[5] = slot;
            BinaryPrimitives.WriteUInt16LittleEndian(payload.Slice(6, 2), itemId);
            return 76;
        }

        /// <summary>
        /// C2S 0x053: Configures lockstyle appearance.
        /// Protocol specification referenced from LandSandBoat (https://github.com/LandSandBoat/server/blob/base/src/map/packets/c2s/0x053_lockstyle.h).
        /// </summary>
        public static int BuildLockstyle(Span<byte> destination, ushort sequenceId, LockstyleMode mode, ReadOnlySpan<(byte Slot, EquipSlotId EquipSlot, ContainerId Container, ushort ItemId)> items)
        {
            PacketHeader.Write(destination, 0x053, 34, sequenceId);
            var payload = destination.Slice(4, 132);
            payload.Clear();

            int count = Math.Min(items.Length, 16);
            payload[0] = (byte)count;
            payload[1] = (byte)mode;

            for (int i = 0; i < count; i++)
            {
                int offset = 4 + (i * 8);
                payload[offset] = items[i].Slot;
                payload[offset + 1] = (byte)items[i].EquipSlot;
                payload[offset + 2] = (byte)items[i].Container;
                BinaryPrimitives.WriteUInt16LittleEndian(payload.Slice(offset + 4, 2), items[i].ItemId);
            }
            return 136;
        }

        /// <summary>
        /// C2S 0x083: Purchases items from an NPC shop.
        /// Protocol specification referenced from LandSandBoat (https://github.com/LandSandBoat/server/blob/base/src/map/packets/c2s/0x083_shop_buy.h).
        /// </summary>
        public static int BuildShopBuy(Span<byte> destination, ushort sequenceId, uint count, ushort shopNo, ushort shopItemIndex, byte propertyItemIndex)
        {
            PacketHeader.Write(destination, 0x083, 4, sequenceId);
            var payload = destination.Slice(4, 12);
            payload.Clear();

            BinaryPrimitives.WriteUInt32LittleEndian(payload.Slice(0, 4), count);
            BinaryPrimitives.WriteUInt16LittleEndian(payload.Slice(4, 2), shopNo);
            BinaryPrimitives.WriteUInt16LittleEndian(payload.Slice(6, 2), shopItemIndex);
            payload[8] = propertyItemIndex;
            return 16;
        }

        /// <summary>
        /// C2S 0x084: Requests an appraisal to sell an item to a shop.
        /// Protocol specification referenced from LandSandBoat (https://github.com/LandSandBoat/server/blob/base/src/map/packets/c2s/0x084_shop_sell_req.h).
        /// </summary>
        public static int BuildShopSellReq(Span<byte> destination, ushort sequenceId, uint count, ushort itemId, byte slot)
        {
            PacketHeader.Write(destination, 0x084, 3, sequenceId);
            var payload = destination.Slice(4, 8);
            payload.Clear();

            BinaryPrimitives.WriteUInt32LittleEndian(payload.Slice(0, 4), count);
            BinaryPrimitives.WriteUInt16LittleEndian(payload.Slice(4, 2), itemId);
            payload[6] = slot;
            return 12;
        }

        /// <summary>
        /// C2S 0x085: Confirms selling an appraised item to a shop.
        /// Protocol specification referenced from LandSandBoat (https://github.com/LandSandBoat/server/blob/base/src/map/packets/c2s/0x085_shop_sell_set.h).
        /// </summary>
        public static int BuildShopSellSet(Span<byte> destination, ushort sequenceId, ushort sellFlag = 1)
        {
            PacketHeader.Write(destination, 0x085, 2, sequenceId);
            var payload = destination.Slice(4, 4);
            payload.Clear();
            BinaryPrimitives.WriteUInt16LittleEndian(payload.Slice(0, 2), sellFlag);
            return 8;
        }

        /// <summary>
        /// C2S 0x104: Exits browsing a player's bazaar.
        /// Protocol specification referenced from LandSandBoat (https://github.com/LandSandBoat/server/blob/base/src/map/packets/c2s/0x104_bazaar_exit.h).
        /// </summary>
        public static int BuildBazaarExit(Span<byte> destination, ushort sequenceId)
        {
            PacketHeader.Write(destination, 0x104, 1, sequenceId);
            return 4;
        }

        /// <summary>
        /// C2S 0x105: Requests to view another player's bazaar.
        /// Protocol specification referenced from LandSandBoat (https://github.com/LandSandBoat/server/blob/base/src/map/packets/c2s/0x105_bazaar_list.h).
        /// </summary>
        public static int BuildBazaarList(Span<byte> destination, ushort sequenceId, uint targetServerId, ushort targetIndex)
        {
            PacketHeader.Write(destination, 0x105, 3, sequenceId);
            var payload = destination.Slice(4, 8);
            payload.Clear();
            BinaryPrimitives.WriteUInt32LittleEndian(payload.Slice(0, 4), targetServerId);
            BinaryPrimitives.WriteUInt16LittleEndian(payload.Slice(4, 2), targetIndex);
            return 12;
        }

        /// <summary>
        /// C2S 0x106: Purchases an item from another player's bazaar.
        /// Protocol specification referenced from LandSandBoat (https://github.com/LandSandBoat/server/blob/base/src/map/packets/c2s/0x106_bazaar_buy.h).
        /// </summary>
        public static int BuildBazaarBuy(Span<byte> destination, ushort sequenceId, byte bazaarItemIndex, uint count)
        {
            PacketHeader.Write(destination, 0x106, 3, sequenceId);
            var payload = destination.Slice(4, 8);
            payload.Clear();
            payload[0] = bazaarItemIndex;
            BinaryPrimitives.WriteUInt32LittleEndian(payload.Slice(4, 4), count);
            return 12;
        }

        /// <summary>
        /// C2S 0x109: Opens local player's bazaar.
        /// Protocol specification referenced from LandSandBoat (https://github.com/LandSandBoat/server/blob/base/src/map/packets/c2s/0x109_bazaar_open.h).
        /// </summary>
        public static int BuildBazaarOpen(Span<byte> destination, ushort sequenceId)
        {
            PacketHeader.Write(destination, 0x109, 1, sequenceId);
            return 4;
        }

        /// <summary>
        /// C2S 0x10A: Sets price on an item in local player's personal bazaar.
        /// Protocol specification referenced from LandSandBoat (https://github.com/LandSandBoat/server/blob/base/src/map/packets/c2s/0x10a_bazaar_itemset.h).
        /// </summary>
        public static int BuildBazaarItemSet(Span<byte> destination, ushort sequenceId, byte slot, uint price)
        {
            PacketHeader.Write(destination, 0x10A, 3, sequenceId);
            var payload = destination.Slice(4, 8);
            payload.Clear();
            payload[0] = slot;
            BinaryPrimitives.WriteUInt32LittleEndian(payload.Slice(4, 4), price);
            return 12;
        }

        /// <summary>
        /// C2S 0x10B: Closes local bazaar or clears listing.
        /// Protocol specification referenced from LandSandBoat (https://github.com/LandSandBoat/server/blob/base/src/map/packets/c2s/0x10b_bazaar_close.h).
        /// </summary>
        public static int BuildBazaarClose(Span<byte> destination, ushort sequenceId, uint allListClearFlag = 0)
        {
            PacketHeader.Write(destination, 0x10B, 2, sequenceId);
            var payload = destination.Slice(4, 4);
            payload.Clear();
            BinaryPrimitives.WriteUInt32LittleEndian(payload.Slice(0, 4), allListClearFlag);
            return 8;
        }

        /// <summary>
        /// C2S 0x10F: Requests Currencies 1 update.
        /// Protocol specification referenced from LandSandBoat (https://github.com/LandSandBoat/server/blob/base/src/map/packets/c2s/0x10f_currencies_1.h).
        /// </summary>
        public static int BuildCurrencies1Request(Span<byte> destination, ushort sequenceId)
        {
            PacketHeader.Write(destination, 0x10F, 1, sequenceId);
            return 4;
        }

        /// <summary>
        /// C2S 0x115: Requests Currencies 2 update.
        /// Protocol specification referenced from LandSandBoat (https://github.com/LandSandBoat/server/blob/base/src/map/packets/c2s/0x115_currencies_2.h).
        /// </summary>
        public static int BuildCurrencies2Request(Span<byte> destination, ushort sequenceId)
        {
            PacketHeader.Write(destination, 0x115, 1, sequenceId);
            return 4;
        }
    }

    #endregion
}
