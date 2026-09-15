// tests/Gordian.Core.Tests/Network/InventoryPacketTests.cs
using System;
using System.Buffers.Binary;
using System.Text;
using System.Threading.Tasks;
using Gordian.Core.Network.Packets;
using Gordian.Core.World;
using Xunit;

namespace Gordian.Core.Tests.Network
{
    public class InventoryPacketTests
    {
        #region Inbound S2C Decoders

        [Fact]
        public void S2C_0x01C_ItemMax_DecodesContainerSizes()
        {
            byte[] payload = new byte[68];
            // Container 0 (Inventory) has max size 80 (encoded as 81)
            payload[0] = 81;
            // Container 1 (MogSafe) has max size 50 (encoded as 51)
            payload[1] = 51;

            // Usable size at offset 32: Container 0 has usable 80 (encoded as 81)
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(32, 2), 81);
            // Container 8 (Wardrobe) at offset 32 + 8*2 = 48: usable 60 (encoded as 61)
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(48, 2), 61);

            var p = new S2C_0x01C_ItemMax(payload);

            Assert.True(p.IsValid);
            Assert.Equal(80, p.GetMaxSize(ContainerId.Inventory));
            Assert.Equal(50, p.GetMaxSize(ContainerId.MogSafe));
            Assert.Equal(80, p.GetUsableSize(ContainerId.Inventory));
            Assert.Equal(60, p.GetUsableSize(ContainerId.Wardrobe));
        }

        [Fact]
        public void S2C_0x01D_ItemSame_DecodesSyncState()
        {
            byte[] payload = new byte[8];
            payload[0] = (byte)ItemSameState.AllLoaded;
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(4, 4), 0x00000001);

            var p = new S2C_0x01D_ItemSame(payload);

            Assert.True(p.IsValid);
            Assert.Equal(ItemSameState.AllLoaded, p.State);
            Assert.Equal(1u, p.Flags);
        }

        [Fact]
        public void S2C_0x01E_ItemNum_DecodesCountUpdate()
        {
            byte[] payload = new byte[8];
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(0, 4), 12);
            payload[4] = (byte)ContainerId.MogSatchel;
            payload[5] = 7;
            payload[6] = (byte)ItemLockFlag.NoDrop;

            var p = new S2C_0x01E_ItemNum(payload);

            Assert.True(p.IsValid);
            Assert.Equal(12u, p.Count);
            Assert.Equal(ContainerId.MogSatchel, p.Container);
            Assert.Equal(7, p.Slot);
            Assert.Equal(ItemLockFlag.NoDrop, p.LockFlag);
        }

        [Fact]
        public void S2C_0x01F_ItemList_DecodesItemEntry()
        {
            byte[] payload = new byte[12];
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(0, 4), 99);
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(4, 2), 4096); // Fire Crystal
            payload[6] = (byte)ContainerId.Inventory;
            payload[7] = 3;
            payload[8] = (byte)ItemLockFlag.Normal;

            var p = new S2C_0x01F_ItemList(payload);

            Assert.True(p.IsValid);
            Assert.Equal(99u, p.Count);
            Assert.Equal(4096, p.ItemId);
            Assert.Equal(ContainerId.Inventory, p.Container);
            Assert.Equal(3, p.Slot);
            Assert.Equal(ItemLockFlag.Normal, p.LockFlag);
        }

        [Fact]
        public void S2C_0x020_ItemAttr_DecodesFullItemDetails()
        {
            byte[] payload = new byte[36];
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(0, 4), 1);
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(4, 4), 500000); // 500k bazaar price
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(8, 2), 16420);  // Kraken Club
            payload[10] = (byte)ContainerId.Wardrobe;
            payload[11] = 15;
            payload[12] = (byte)ItemLockFlag.Normal;
            payload[13] = 0xAA; // ExtData byte

            var p = new S2C_0x020_ItemAttr(payload);

            Assert.True(p.IsValid);
            Assert.Equal(1u, p.Count);
            Assert.Equal(500000u, p.Price);
            Assert.Equal(16420, p.ItemId);
            Assert.Equal(ContainerId.Wardrobe, p.Container);
            Assert.Equal(15, p.Slot);
            Assert.Equal(0xAA, p.ExtData[0]);
        }

        [Fact]
        public void S2C_0x021_ItemTradeReq_DecodesPartnerInfo()
        {
            byte[] payload = new byte[8];
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(0, 4), 0x01020304);
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(4, 2), 42);

            var p = new S2C_0x021_ItemTradeReq(payload);

            Assert.True(p.IsValid);
            Assert.Equal(0x01020304u, p.UniqueNo);
            Assert.Equal(42, p.ActIndex);
        }

        [Fact]
        public void S2C_0x022_ItemTradeRes_DecodesResultStatus()
        {
            byte[] payload = new byte[10];
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(0, 4), 0x01020304);
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(4, 4), (uint)TradeResultKind.Make);
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(8, 2), 42);

            var p = new S2C_0x022_ItemTradeRes(payload);

            Assert.True(p.IsValid);
            Assert.Equal(TradeResultKind.Make, p.Kind);
            Assert.Equal(42, p.ActIndex);
        }

        [Fact]
        public void S2C_0x023_ItemTradeList_DecodesPartnerOffer()
        {
            byte[] payload = new byte[34];
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(0, 4), 5);
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(4, 2), 1);
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(6, 2), 1234);
            payload[8] = 20; // free space
            payload[9] = 2;  // trade index

            var p = new S2C_0x023_ItemTradeList(payload);

            Assert.True(p.IsValid);
            Assert.Equal(5u, p.Count);
            Assert.Equal(1, p.TradeCounter);
            Assert.Equal(1234, p.ItemId);
            Assert.Equal(20, p.ItemFreeSpaceNum);
            Assert.Equal(2, p.TradeIndex);
        }

        [Fact]
        public void S2C_0x025_ItemTradeMyList_DecodesMyOffer()
        {
            byte[] payload = new byte[8];
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(0, 4), 10);
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(4, 2), 4321);
            payload[6] = 1; // trade index
            payload[7] = 5; // slot

            var p = new S2C_0x025_ItemTradeMyList(payload);

            Assert.True(p.IsValid);
            Assert.Equal(10u, p.Count);
            Assert.Equal(4321, p.ItemId);
            Assert.Equal(1, p.TradeIndex);
            Assert.Equal(5, p.Slot);
        }

        [Fact]
        public void S2C_0x026_ItemSubcontainer_DecodesMannequin()
        {
            byte[] payload = new byte[24];
            payload[0] = 1; // is used
            payload[1] = (byte)ContainerId.MogSafe;
            payload[2] = 4; // slot
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(6, 2), 100); // Head
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(8, 2), 200); // Body
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(16, 2), 300); // Main
            payload[22] = 1; // Hume M
            payload[23] = 2; // Pose

            var p = new S2C_0x026_ItemSubcontainer(payload);

            Assert.True(p.IsValid);
            Assert.True(p.IsUsed);
            Assert.Equal(ContainerId.MogSafe, p.Container);
            Assert.Equal(4, p.Slot);
            Assert.Equal(100, p.ModelIdHead);
            Assert.Equal(200, p.ModelIdBody);
            Assert.Equal(300, p.ModelIdMain);
            Assert.Equal(1, p.Race);
            Assert.Equal(2, p.Pose);
        }

        [Fact]
        public void S2C_0x03C_ShopList_DecodesItems()
        {
            byte[] payload = new byte[4 + (12 * 2)];
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(0, 2), 0);
            payload[2] = 1;

            // Item 0: Potion (ItemId 4112, Price 100)
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(4, 4), 100);
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(8, 2), 4112);
            payload[10] = 0; // ShopIndex

            // Item 1: Hi-Potion (ItemId 4113, Price 500)
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(16, 4), 500);
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(20, 2), 4113);
            payload[22] = 1; // ShopIndex

            var p = new S2C_0x03C_ShopList(payload);

            Assert.True(p.IsValid);
            Assert.Equal(2, p.ItemCount);

            var item0 = p.GetItem(0);
            Assert.Equal(100u, item0.Price);
            Assert.Equal(4112, item0.ItemId);

            var item1 = p.GetItem(1);
            Assert.Equal(500u, item1.Price);
            Assert.Equal(4113, item1.ItemId);
        }

        [Fact]
        public void S2C_0x03D_ShopSell_DecodesAppraisal()
        {
            byte[] payload = new byte[12];
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(0, 4), 1250);
            payload[4] = 3; // Slot
            payload[5] = 0; // Type
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(8, 4), 1);

            var p = new S2C_0x03D_ShopSell(payload);

            Assert.True(p.IsValid);
            Assert.Equal(1250u, p.Price);
            Assert.Equal(3, p.Slot);
        }

        [Fact]
        public void S2C_0x03E_ShopOpen_DecodesOpenSignal()
        {
            byte[] payload = new byte[4];
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(0, 2), 15);

            var p = new S2C_0x03E_ShopOpen(payload);

            Assert.True(p.IsValid);
            Assert.Equal(15, p.ShopListNum);
        }

        [Fact]
        public void S2C_0x03F_ShopBuy_DecodesBuyConfirmation()
        {
            byte[] payload = new byte[8];
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(0, 2), 5);
            payload[2] = 0; // success
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(4, 4), 2);

            var p = new S2C_0x03F_ShopBuy(payload);

            Assert.True(p.IsValid);
            Assert.Equal(5, p.ShopItemIndex);
            Assert.Equal(0, p.BuyState);
            Assert.Equal(2u, p.Count);
        }

        [Fact]
        public void S2C_0x04C_Auc_DecodesAuctionResponse()
        {
            byte[] payload = new byte[48];
            payload[0] = (byte)AuctionCommand.Info;
            payload[1] = 1; // work index
            payload[2] = 0; // result ok

            // Parcel: Seller Name at offset 16 + 4 = 20
            Encoding.ASCII.GetBytes("Tarutaru").CopyTo(payload.AsSpan(20));
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(36, 2), 17000); // ItemId
            payload[38] = 1; // Count
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(40, 4), 75000); // Price

            var p = new S2C_0x04C_Auc(payload);

            Assert.True(p.IsValid);
            Assert.Equal(AuctionCommand.Info, p.Command);
            Assert.Equal("Tarutaru", p.SellerName);
            Assert.Equal(17000, p.ItemId);
            Assert.Equal(75000u, p.Price);
        }

        [Fact]
        public void S2C_0x050_EquipList_DecodesEquippedSlot()
        {
            byte[] payload = new byte[4];
            payload[0] = 12; // slot
            payload[1] = (byte)EquipSlotId.Main;
            payload[2] = (byte)ContainerId.Wardrobe;

            var p = new S2C_0x050_EquipList(payload);

            Assert.True(p.IsValid);
            Assert.Equal(12, p.Slot);
            Assert.Equal(EquipSlotId.Main, p.EquipSlot);
            Assert.Equal(ContainerId.Wardrobe, p.Container);
        }

        [Fact]
        public void S2C_0x082_GuildBuy_DecodesGuildPurchase()
        {
            byte[] payload = new byte[4];
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(0, 2), 640);
            payload[2] = 12;
            payload[3] = 1;

            var p = new S2C_0x082_GuildBuy(payload);

            Assert.True(p.IsValid);
            Assert.Equal(640, p.ItemId);
            Assert.Equal(12, p.Count);
        }

        [Fact]
        public void S2C_0x083_GuildBuyList_DecodesGuildStock()
        {
            byte[] payload = new byte[242];
            // Item 0: ItemId 500, stock 10, max 20, price 1000
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(0, 2), 500);
            payload[2] = 10;
            payload[3] = 20;
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(4, 4), 1000);

            payload[240] = 1; // Count
            payload[241] = 0; // Stat

            var p = new S2C_0x083_GuildBuyList(payload);

            Assert.True(p.IsValid);
            Assert.Equal(1, p.Count);

            var item = p.GetItem(0);
            Assert.Equal(500, item.ItemId);
            Assert.Equal(10, item.Stock);
            Assert.Equal(20, item.Max);
            Assert.Equal(1000, item.Price);
        }

        [Fact]
        public void S2C_0x086_GuildOpen_DecodesOperatingStatus()
        {
            byte[] payload = new byte[8];
            payload[0] = (byte)ShopOpenStatus.Open;
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(4, 4), 1800); // 18:00 close

            var p = new S2C_0x086_GuildOpen(payload);

            Assert.True(p.IsValid);
            Assert.Equal(ShopOpenStatus.Open, p.Status);
            Assert.Equal(1800u, p.Time);
        }

        [Fact]
        public void S2C_0x105_BazaarList_DecodesItem()
        {
            byte[] payload = new byte[38];
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(0, 4), 25000); // price
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(4, 4), 1);     // count
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(8, 2), 50);    // tax
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(10, 2), 17001);// item
            payload[12] = 2; // slot index

            var p = new S2C_0x105_BazaarList(payload);

            Assert.True(p.IsValid);
            Assert.Equal(25000u, p.Price);
            Assert.Equal(1u, p.Count);
            Assert.Equal(50, p.TaxRate);
            Assert.Equal(17001, p.ItemId);
            Assert.Equal(2, p.ItemIndex);
        }

        [Fact]
        public void S2C_0x106_BazaarBuy_DecodesBuyResult()
        {
            byte[] payload = new byte[20];
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(0, 4), (uint)BazaarBuyState.Ok);
            Encoding.ASCII.GetBytes("Mithra").CopyTo(payload.AsSpan(4));

            var p = new S2C_0x106_BazaarBuy(payload);

            Assert.True(p.IsValid);
            Assert.Equal(BazaarBuyState.Ok, p.State);
            Assert.Equal("Mithra", p.TargetName);
        }

        [Fact]
        public void S2C_0x107_BazaarClose_DecodesClosedNotification()
        {
            byte[] payload = new byte[16];
            Encoding.ASCII.GetBytes("Elvaan").CopyTo(payload.AsSpan(0));

            var p = new S2C_0x107_BazaarClose(payload);

            Assert.True(p.IsValid);
            Assert.Equal("Elvaan", p.SellerName);
        }

        [Fact]
        public void S2C_0x108_BazaarShopping_DecodesVisitor()
        {
            byte[] payload = new byte[28];
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(0, 4), 0x999);
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(4, 4), (uint)BazaarShoppingState.Enter);
            Encoding.ASCII.GetBytes("Galka").CopyTo(payload.AsSpan(12));

            var p = new S2C_0x108_BazaarShopping(payload);

            Assert.True(p.IsValid);
            Assert.Equal(BazaarShoppingState.Enter, p.State);
            Assert.Equal("Galka", p.BuyerName);
        }

        [Fact]
        public void S2C_0x113_Currencies1_DecodesSparksAndAccolades()
        {
            byte[] payload = new byte[248];
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(0, 4), 15000);  // Sandy CP
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(112, 4), 99999); // Sparks
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(224, 4), 45000); // Unity Accolades

            var p = new S2C_0x113_Currencies1(payload);

            Assert.True(p.IsValid);
            Assert.Equal(15000, p.ConquestSandoria);
            Assert.Equal(99999, p.SparksOfEminence);
            Assert.Equal(45000, p.UnityAccolades);
        }

        [Fact]
        public void S2C_0x118_Currencies2_DecodesBayldAndSilt()
        {
            byte[] payload = new byte[156];
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(0, 4), 50000); // Bayld
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(72, 4), 250000); // Silt
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(140, 4), 12000); // Gallimaufry

            var p = new S2C_0x118_Currencies2(payload);

            Assert.True(p.IsValid);
            Assert.Equal(50000, p.Bayld);
            Assert.Equal(250000, p.EschaSilt);
            Assert.Equal(12000, p.Gallimaufry);
        }

        #endregion

        #region Outbound C2S Builders

        [Fact]
        public void BuildItemDump_EncodesCorrectPayload()
        {
            byte[] buf = new byte[12];
            int len = InventoryPacketBuilders.BuildItemDump(buf, 0x1234, 12, ContainerId.MogSafe, 5);

            Assert.Equal(12, len);
            Assert.True(PacketHeader.TryParse(buf, out var header));
            Assert.Equal(0x028, header.PacketId);
            Assert.Equal(0x1234, header.SequenceId);
            Assert.Equal(12, header.TotalSize);

            uint count = BinaryPrimitives.ReadUInt32LittleEndian(buf.AsSpan(4, 4));
            Assert.Equal(12u, count);
            Assert.Equal((byte)ContainerId.MogSafe, buf[8]);
            Assert.Equal(5, buf[9]);
        }

        [Fact]
        public void BuildItemMove_EncodesCorrectPayload()
        {
            byte[] buf = new byte[12];
            int len = InventoryPacketBuilders.BuildItemMove(buf, 0x0001, 1, ContainerId.Inventory, 3, ContainerId.MogSatchel, 10);

            Assert.Equal(12, len);
            Assert.True(PacketHeader.TryParse(buf, out var header));
            Assert.Equal(0x029, header.PacketId);
            Assert.Equal(12, header.TotalSize);

            uint count = BinaryPrimitives.ReadUInt32LittleEndian(buf.AsSpan(4, 4));
            Assert.Equal(1u, count);
            Assert.Equal((byte)ContainerId.Inventory, buf[8]);
            Assert.Equal((byte)ContainerId.MogSatchel, buf[9]);
            Assert.Equal(3, buf[10]);
            Assert.Equal(10, buf[11]);
        }

        [Fact]
        public void BuildTradeReq_EncodesTarget()
        {
            byte[] buf = new byte[12];
            int len = InventoryPacketBuilders.BuildTradeReq(buf, 0x0002, 0x10002000, 105);

            Assert.Equal(12, len);
            Assert.True(PacketHeader.TryParse(buf, out var header));
            Assert.Equal(0x032, header.PacketId);

            uint targetId = BinaryPrimitives.ReadUInt32LittleEndian(buf.AsSpan(4, 4));
            ushort targetIndex = BinaryPrimitives.ReadUInt16LittleEndian(buf.AsSpan(8, 2));
            Assert.Equal(0x10002000u, targetId);
            Assert.Equal(105, targetIndex);
        }

        [Fact]
        public void BuildTradeRes_EncodesResponse()
        {
            byte[] buf = new byte[12];
            int len = InventoryPacketBuilders.BuildTradeRes(buf, 0x0003, TradeClientKind.Make, 1);

            Assert.Equal(12, len);
            Assert.True(PacketHeader.TryParse(buf, out var header));
            Assert.Equal(0x033, header.PacketId);

            uint kind = BinaryPrimitives.ReadUInt32LittleEndian(buf.AsSpan(4, 4));
            ushort counter = BinaryPrimitives.ReadUInt16LittleEndian(buf.AsSpan(8, 2));
            Assert.Equal((uint)TradeClientKind.Make, kind);
            Assert.Equal(1, counter);
        }

        [Fact]
        public void BuildTradeList_EncodesItemOffer()
        {
            byte[] buf = new byte[12];
            int len = InventoryPacketBuilders.BuildTradeList(buf, 0x0004, 5, 4096, 2, 0);

            Assert.Equal(12, len);
            Assert.True(PacketHeader.TryParse(buf, out var header));
            Assert.Equal(0x034, header.PacketId);

            uint count = BinaryPrimitives.ReadUInt32LittleEndian(buf.AsSpan(4, 4));
            ushort itemId = BinaryPrimitives.ReadUInt16LittleEndian(buf.AsSpan(8, 2));
            Assert.Equal(5u, count);
            Assert.Equal(4096, itemId);
            Assert.Equal(2, buf[10]);
            Assert.Equal(0, buf[11]);
        }

        [Fact]
        public void BuildItemTransfer_EncodesNpcTrade()
        {
            byte[] buf = new byte[64];
            var items = new (byte Slot, uint Count)[] { (1, 10), (2, 5) };
            int len = InventoryPacketBuilders.BuildItemTransfer(buf, 0x0005, 0x12345678, 50, items);

            Assert.Equal(64, len);
            Assert.True(PacketHeader.TryParse(buf, out var header));
            Assert.Equal(0x036, header.PacketId);

            uint targetId = BinaryPrimitives.ReadUInt32LittleEndian(buf.AsSpan(4, 4));
            Assert.Equal(0x12345678u, targetId);
            Assert.Equal(10u, BinaryPrimitives.ReadUInt32LittleEndian(buf.AsSpan(8, 4)));
            Assert.Equal(5u, BinaryPrimitives.ReadUInt32LittleEndian(buf.AsSpan(12, 4)));
            Assert.Equal(1, buf[48]);
            Assert.Equal(2, buf[49]);
            Assert.Equal(2, buf[60]); // count
        }

        [Fact]
        public void BuildItemUse_EncodesTargetAndSlot()
        {
            byte[] buf = new byte[20];
            int len = InventoryPacketBuilders.BuildItemUse(buf, 0x0006, 0x55555555, 12, 1, 4, ContainerId.Inventory);

            Assert.Equal(20, len);
            Assert.True(PacketHeader.TryParse(buf, out var header));
            Assert.Equal(0x037, header.PacketId);

            uint targetId = BinaryPrimitives.ReadUInt32LittleEndian(buf.AsSpan(4, 4));
            uint count = BinaryPrimitives.ReadUInt32LittleEndian(buf.AsSpan(8, 4));
            ushort targetIndex = BinaryPrimitives.ReadUInt16LittleEndian(buf.AsSpan(12, 2));
            Assert.Equal(0x55555555u, targetId);
            Assert.Equal(1u, count);
            Assert.Equal(12, targetIndex);
            Assert.Equal(4, buf[14]);
            Assert.Equal((uint)ContainerId.Inventory, BinaryPrimitives.ReadUInt32LittleEndian(buf.AsSpan(16, 4)));
        }

        [Fact]
        public void BuildEquipSet_EncodesSingleEquip()
        {
            byte[] buf = new byte[8];
            int len = InventoryPacketBuilders.BuildEquipSet(buf, 0x0007, 10, EquipSlotId.Head, ContainerId.Wardrobe);

            Assert.Equal(8, len);
            Assert.True(PacketHeader.TryParse(buf, out var header));
            Assert.Equal(0x050, header.PacketId);

            Assert.Equal(10, buf[4]);
            Assert.Equal((byte)EquipSlotId.Head, buf[5]);
            Assert.Equal((byte)ContainerId.Wardrobe, buf[6]);
        }

        [Fact]
        public void BuildBazaarItemSet_EncodesPrice()
        {
            byte[] buf = new byte[12];
            int len = InventoryPacketBuilders.BuildBazaarItemSet(buf, 0x0008, 14, 150000);

            Assert.Equal(12, len);
            Assert.True(PacketHeader.TryParse(buf, out var header));
            Assert.Equal(0x10A, header.PacketId);

            Assert.Equal(14, buf[4]);
            Assert.Equal(150000u, BinaryPrimitives.ReadUInt32LittleEndian(buf.AsSpan(8, 4)));
        }

        #endregion

        #region InventoryState & Dispatch Integration

        [Fact]
        public void InventoryState_TracksItemsAcrossContainers()
        {
            var state = new InventoryState();

            // Set size
            state.SetContainerSizes(ContainerId.Inventory, 80, 80);
            Assert.Equal(80, state.GetContainer(ContainerId.Inventory).MaxSize);

            // Add item
            state.SetItem(ContainerId.Inventory, 1, 4096, 12, ItemLockFlag.Normal);
            Assert.True(state.GetContainer(ContainerId.Inventory).TryGetItem(1, out var item));
            Assert.Equal(4096, item.ItemId);
            Assert.Equal(12u, item.Count);

            // Move item to Wardrobe slot 5
            state.MoveItem(ContainerId.Inventory, 1, ContainerId.Wardrobe, 5, 12);
            Assert.False(state.GetContainer(ContainerId.Inventory).TryGetItem(1, out _));
            Assert.True(state.GetContainer(ContainerId.Wardrobe).TryGetItem(5, out var moved));
            Assert.Equal(4096, moved.ItemId);
            Assert.Equal(12u, moved.Count);
            Assert.Equal(ContainerId.Wardrobe, moved.Container);

            // Equip item
            state.SetEquip(EquipSlotId.Main, ContainerId.Wardrobe, 5);
            var equipped = state.GetEquipped(EquipSlotId.Main);
            Assert.Equal(ContainerId.Wardrobe, equipped.Container);
            Assert.Equal(5, equipped.Slot);
        }

        [Fact]
        public async Task InventoryPacketModule_DispatchesAndUpdatesState()
        {
            var inventoryState = new InventoryState();
            var localPlayerState = new LocalPlayerState();
            var dispatcher = new PacketDispatcher();

            var module = new InventoryPacketModule(
                inventoryState,
                localPlayerState,
                (mem, _) => Task.CompletedTask
            );
            module.Register(dispatcher);

            // 1. Simulate 0x01C container sizes
            byte[] p01C = new byte[68];
            p01C[0] = 81; // 80 max
            BinaryPrimitives.WriteUInt16LittleEndian(p01C.AsSpan(32, 2), 81); // 80 usable
            dispatcher.Dispatch(new PacketHeader(0x01C, 72, 1), p01C);

            Assert.Equal(80, inventoryState.GetContainer(ContainerId.Inventory).MaxSize);
            Assert.Equal(80, inventoryState.GetContainer(ContainerId.Inventory).UsableSize);

            // 2. Simulate 0x01F item list
            byte[] p01F = new byte[12];
            BinaryPrimitives.WriteUInt32LittleEndian(p01F.AsSpan(0, 4), 1);
            BinaryPrimitives.WriteUInt16LittleEndian(p01F.AsSpan(4, 2), 16420); // Kraken Club
            p01F[6] = (byte)ContainerId.Inventory;
            p01F[7] = 2; // Slot 2
            dispatcher.Dispatch(new PacketHeader(0x01F, 16, 2), p01F);

            Assert.True(inventoryState.GetContainer(ContainerId.Inventory).TryGetItem(2, out var item));
            Assert.Equal(16420, item.ItemId);

            // 3. Simulate 0x050 equip change
            byte[] p050 = new byte[4];
            p050[0] = 2;
            p050[1] = (byte)EquipSlotId.Main;
            p050[2] = (byte)ContainerId.Inventory;
            dispatcher.Dispatch(new PacketHeader(0x050, 8, 3), p050);

            var eq = inventoryState.GetEquipped(EquipSlotId.Main);
            Assert.Equal(ContainerId.Inventory, eq.Container);
            Assert.Equal(2, eq.Slot);

            // 4. Test outbound methods
            await module.DropItemAsync(ContainerId.Inventory, 2, 1);
            await module.EquipItemAsync(2, EquipSlotId.Main, ContainerId.Inventory);
            await module.SortContainerAsync(ContainerId.Inventory);

            module.Unregister(dispatcher);
        }

        [Fact]
        public void S2C_0x116_EquipsetValid_DecodesValidationResults()
        {
            byte[] payload = new byte[68];
            // Slot 0: HasItem = 1, Container = MogCase (7), ItemIndex = 5, ItemNo = 12500
            byte flags = (byte)((7 << 2) | 0x01);
            payload[0] = flags;
            payload[1] = 5;
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(2, 2), 12500);

            var p = new S2C_0x116_EquipsetValid(payload);

            Assert.True(p.IsValid);
            var item = p.GetItem(0);
            Assert.True(item.HasItem);
            Assert.False(item.RemoveItem);
            Assert.Equal(ContainerId.MogCase, item.Container);
            Assert.Equal(5, item.ItemIndex);
            Assert.Equal(12500, item.ItemId);
        }

        [Fact]
        public void S2C_0x117_EquipsetRes_DecodesResult()
        {
            byte[] payload = new byte[4];
            payload[0] = 16;

            var p = new S2C_0x117_EquipsetRes(payload);

            Assert.True(p.IsValid);
            Assert.Equal(16, p.Count);
        }

        [Fact]
        public void S2C_0x084_GuildSell_DecodesConfirmation()
        {
            byte[] payload = new byte[4];
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(0, 2), 800);
            payload[2] = 2;
            payload[3] = 1;

            var p = new S2C_0x084_GuildSell(payload);

            Assert.True(p.IsValid);
            Assert.Equal(800, p.ItemId);
            Assert.Equal(2, p.Count);
            Assert.Equal(1, p.Trade);
        }

        [Fact]
        public void S2C_0x085_GuildSellList_DecodesAcceptedItems()
        {
            byte[] payload = new byte[242];
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(0, 2), 999);
            payload[2] = 5;
            payload[3] = 10;
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(4, 4), 3500);

            payload[240] = 1;

            var p = new S2C_0x085_GuildSellList(payload);

            Assert.True(p.IsValid);
            Assert.Equal(1, p.Count);
            var item = p.GetItem(0);
            Assert.Equal(999, item.ItemId);
            Assert.Equal(5, item.Stock);
            Assert.Equal(10, item.Max);
            Assert.Equal(3500, item.Price);
        }

        [Fact]
        public void S2C_0x109_BazaarSell_DecodesPurchaseFromPlayer()
        {
            byte[] payload = new byte[32];
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(0, 4), 0xABCD);
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(4, 4), 3);
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(8, 2), 100);
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(10, 2), 200);
            Encoding.ASCII.GetBytes("Buyer").CopyTo(payload.AsSpan(12));
            payload[28] = 4; // slot

            var p = new S2C_0x109_BazaarSell(payload);

            Assert.True(p.IsValid);
            Assert.Equal(3u, p.Count);
            Assert.Equal("Buyer", p.BuyerName);
            Assert.Equal(4, p.Slot);
        }

        [Fact]
        public void S2C_0x10A_BazaarSale_DecodesSaleEvent()
        {
            byte[] payload = new byte[24];
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(0, 4), 1);
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(4, 2), 16420);
            Encoding.ASCII.GetBytes("Shopper").CopyTo(payload.AsSpan(6));

            var p = new S2C_0x10A_BazaarSale(payload);

            Assert.True(p.IsValid);
            Assert.Equal(1u, p.Count);
            Assert.Equal(16420, p.ItemId);
            Assert.Equal("Shopper", p.BuyerName);
        }

        [Fact]
        public void BuildAdditionalBuilders_EncodesCorrectWireFormat()
        {
            byte[] buf = new byte[150];

            // 1. Auction Request (Bid)
            int len = InventoryPacketBuilders.BuildAuctionRequest(buf, 1, AuctionCommand.Bid, 0, 4096, 5000, 1);
            Assert.Equal(52, len);
            Assert.True(PacketHeader.TryParse(buf, out var h1));
            Assert.Equal(0x04E, h1.PacketId);
            Assert.Equal((byte)AuctionCommand.Bid, buf[4]);

            // 2. Equipset Set
            var equipItems = new (byte Slot, EquipSlotId EquipSlot, ContainerId Container)[]
            {
                (1, EquipSlotId.Head, ContainerId.Inventory),
                (2, EquipSlotId.Body, ContainerId.Wardrobe)
            };
            len = InventoryPacketBuilders.BuildEquipsetSet(buf, 2, equipItems);
            Assert.Equal(72, len);
            Assert.True(PacketHeader.TryParse(buf, out var h2));
            Assert.Equal(0x051, h2.PacketId);
            Assert.Equal(2, buf[4]); // count

            // 3. Equipset Check
            len = InventoryPacketBuilders.BuildEquipsetCheck(buf, 3, EquipSlotId.Hands, 5, ContainerId.Wardrobe, 14000);
            Assert.Equal(76, len);
            Assert.True(PacketHeader.TryParse(buf, out var h3));
            Assert.Equal(0x052, h3.PacketId);
            Assert.Equal((byte)EquipSlotId.Hands, buf[4]);

            // 4. Lockstyle
            var lockItems = new (byte Slot, EquipSlotId EquipSlot, ContainerId Container, ushort ItemId)[]
            {
                (1, EquipSlotId.Head, ContainerId.Inventory, 12000)
            };
            len = InventoryPacketBuilders.BuildLockstyle(buf, 4, LockstyleMode.Enable, lockItems);
            Assert.Equal(136, len);
            Assert.True(PacketHeader.TryParse(buf, out var h4));
            Assert.Equal(0x053, h4.PacketId);
            Assert.Equal(1, buf[4]); // count
            Assert.Equal((byte)LockstyleMode.Enable, buf[5]);

            // 5. Shop Buy
            len = InventoryPacketBuilders.BuildShopBuy(buf, 5, 2, 10, 3, 0);
            Assert.Equal(16, len);
            Assert.True(PacketHeader.TryParse(buf, out var h5));
            Assert.Equal(0x083, h5.PacketId);

            // 6. Shop Sell Req & Set
            len = InventoryPacketBuilders.BuildShopSellReq(buf, 6, 1, 4112, 3);
            Assert.Equal(12, len);
            Assert.True(PacketHeader.TryParse(buf, out var h6));
            Assert.Equal(0x084, h6.PacketId);

            len = InventoryPacketBuilders.BuildShopSellSet(buf, 7, 1);
            Assert.Equal(8, len);
            Assert.True(PacketHeader.TryParse(buf, out var h7));
            Assert.Equal(0x085, h7.PacketId);

            // 7. Bazaar exit, open, close, buy, list
            len = InventoryPacketBuilders.BuildBazaarExit(buf, 8);
            Assert.Equal(4, len);
            Assert.True(PacketHeader.TryParse(buf, out var h8));
            Assert.Equal(0x104, h8.PacketId);

            len = InventoryPacketBuilders.BuildBazaarOpen(buf, 9);
            Assert.Equal(4, len);
            Assert.True(PacketHeader.TryParse(buf, out var h9));
            Assert.Equal(0x109, h9.PacketId);

            len = InventoryPacketBuilders.BuildBazaarClose(buf, 10, 1);
            Assert.Equal(8, len);
            Assert.True(PacketHeader.TryParse(buf, out var h10));
            Assert.Equal(0x10B, h10.PacketId);

            len = InventoryPacketBuilders.BuildBazaarBuy(buf, 11, 2, 1);
            Assert.Equal(12, len);
            Assert.True(PacketHeader.TryParse(buf, out var h11));
            Assert.Equal(0x106, h11.PacketId);

            len = InventoryPacketBuilders.BuildBazaarList(buf, 12, 0x11112222, 55);
            Assert.Equal(12, len);
            Assert.True(PacketHeader.TryParse(buf, out var h12));
            Assert.Equal(0x105, h12.PacketId);

            // 8. Currencies 1 & 2 Requests
            len = InventoryPacketBuilders.BuildCurrencies1Request(buf, 13);
            Assert.Equal(4, len);
            Assert.True(PacketHeader.TryParse(buf, out var h13));
            Assert.Equal(0x10F, h13.PacketId);

            len = InventoryPacketBuilders.BuildCurrencies2Request(buf, 14);
            Assert.Equal(4, len);
            Assert.True(PacketHeader.TryParse(buf, out var h14));
            Assert.Equal(0x115, h14.PacketId);

            // 9. Subcontainer
            len = InventoryPacketBuilders.BuildSubcontainer(buf, 15, SubcontainerKind.Equip, ContainerId.MogSafe, 2, SubcontainerSlotIndex.Head, ContainerId.Wardrobe, 10);
            Assert.Equal(32, len);
            Assert.True(PacketHeader.TryParse(buf, out var h15));
            Assert.Equal(0x03B, h15.PacketId);
        }

        [Fact]
        public void InventoryState_TradeShopBazaarLifecycles()
        {
            var state = new InventoryState();

            // Trade lifecycle
            state.StartTrade(0x1234, 10);
            Assert.True(state.IsTrading);
            Assert.Equal(0x1234u, state.TradePartnerServerId);
            Assert.Equal(10, state.TradePartnerIndex);

            state.SetPartnerTradeItem(0, 4096, 12, ReadOnlySpan<byte>.Empty);
            Assert.True(state.PartnerTradeItems.TryGetValue(0, out var partnerItem));
            Assert.Equal(4096, partnerItem.ItemId);
            Assert.Equal(12u, partnerItem.Count);

            state.SetMyTradeItem(0, 4112, 1, 3);
            Assert.True(state.MyTradeItems.TryGetValue(0, out var myItem));
            Assert.Equal(4112, myItem.ItemId);
            Assert.Equal(3, myItem.Slot);

            state.SetTradeStatus(TradeResultKind.End);
            Assert.False(state.IsTrading);
            Assert.Empty(state.PartnerTradeItems);
            Assert.Empty(state.MyTradeItems);

            // Shop lifecycle
            state.OpenShop(5);
            Assert.True(state.IsShopOpen);
            Assert.Equal(5, state.ShopListNum);
            state.SetAppraisal(2, 4500);
            Assert.Equal(4500u, state.AppraisedSellPrice);
            state.CloseShop();
            Assert.False(state.IsShopOpen);

            // Bazaar lifecycle
            state.StartViewingBazaar("Merchant");
            Assert.True(state.IsViewingBazaar);
            Assert.Equal("Merchant", state.ViewedBazaarPlayer);
            state.AddBazaarItem(new BazaarItemEntry(1000, 1, 0, 4096, 0, Array.Empty<byte>()));
            Assert.Single(state.BrowsedBazaarItems);
            state.StopViewingBazaar();
            Assert.False(state.IsViewingBazaar);

            state.SetPersonalBazaarPrice(1, 20000);
            Assert.Equal(20000u, state.PersonalBazaarPrices[1]);
            state.ClearPersonalBazaar();
            Assert.Empty(state.PersonalBazaarPrices);
        }

        #endregion
    }
}
