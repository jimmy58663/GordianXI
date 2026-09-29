// tests/Gordian.Core.Tests/Network/DecodedStateCacheTests.cs
using System;
using System.Buffers.Binary;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Gordian.Core.Network;
using Gordian.Core.Network.Packets;
using Gordian.Core.World;
using Xunit;

namespace Gordian.Core.Tests.Network
{
    /// <summary>
    /// S2C packets that used to be decoded and only logged now land in a World state cache (issue #5):
    /// each test dispatches the packet through its module and reads the cache back.
    /// </summary>
    public class DecodedStateCacheTests
    {
        private static readonly Func<ReadOnlyMemory<byte>, bool, Task> NoSend = (_, _) => Task.CompletedTask;

        private static (PacketDispatcher Dispatcher, InventoryState State) Inventory()
        {
            var state = new InventoryState();
            var dispatcher = new PacketDispatcher();
            new InventoryPacketModule(state, new LocalPlayerState(), NoSend).Register(dispatcher);
            return (dispatcher, state);
        }

        private static void WriteName(byte[] payload, int offset, string name) =>
            Encoding.ASCII.GetBytes(name).CopyTo(payload.AsSpan(offset));

        [Fact]
        public void Effect0x030_StoresCraftEffectPerEntity()
        {
            var state = new CombatState();
            var dispatcher = new PacketDispatcher();
            new CombatPacketModule(state, new LocalPlayerState(), NoSend).Register(dispatcher);

            byte[] payload = new byte[12];
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(0, 4), 0x01000042);
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(4, 2), 0x42);
            BinaryPrimitives.WriteInt16LittleEndian(payload.AsSpan(6, 2), (short)SynthesisEffect.SynthesisFailure);
            payload[8] = 3;
            payload[9] = 1;
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(10, 2), 25);
            dispatcher.Dispatch(new PacketHeader(0x030, 16, 1), payload);

            Assert.True(state.TryGetCraftEffect(0x01000042, out var effect));
            Assert.Equal(SynthesisEffect.SynthesisFailure, effect.Effect);
            Assert.Equal(3, effect.Param);
            Assert.Equal(1, effect.ServerStatus);
            Assert.Equal(25, effect.Timer);
            Assert.False(state.TryGetCraftEffect(0x01000043, out _));
        }

        [Fact]
        public void Auc0x04C_StoresLastResponseAndSaleSlot()
        {
            var (dispatcher, state) = Inventory();

            byte[] payload = new byte[56];
            payload[0] = (byte)AuctionCommand.LotCheck;
            payload[1] = 2; // AucWorkIndex
            payload[2] = 1; // Result
            payload[16] = 3; // Parcel Stat
            payload[18] = 9; // Parcel ItemIndex
            WriteName(payload, 20, "Seller");
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(36, 2), 4096);
            payload[38] = 12;
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(40, 4), 5000);
            dispatcher.Dispatch(new PacketHeader(0x04C, 60, 1), payload);

            var last = state.LastAuctionResponse;
            Assert.NotNull(last);
            Assert.Equal(AuctionCommand.LotCheck, last!.Command);
            Assert.Equal(3, last.ParcelStat);
            Assert.Equal(9, last.ItemIndex);
            Assert.Equal(4096, last.ItemId);
            Assert.Equal(12u, last.Count);
            Assert.Equal(5000u, last.Price);
            Assert.Equal("Seller", last.SellerName);
            Assert.Same(last, state.SnapshotAuctionSlots()[2]);

            // An answer without a sale slot (work index -1) is kept as the last answer but fills no slot.
            byte[] open = new byte[20];
            open[0] = (byte)AuctionCommand.Open;
            open[1] = 0xFF;
            dispatcher.Dispatch(new PacketHeader(0x04C, 24, 2), open);
            Assert.Equal(AuctionCommand.Open, state.LastAuctionResponse!.Command);
            Assert.Single(state.SnapshotAuctionSlots());
        }

        [Fact]
        public void Guild0x082And0x084_StoreTransactionResult()
        {
            var (dispatcher, state) = Inventory();

            byte[] buy = new byte[4];
            BinaryPrimitives.WriteUInt16LittleEndian(buy.AsSpan(0, 2), 640);
            buy[2] = 3;
            dispatcher.Dispatch(new PacketHeader(0x082, 8, 1), buy);
            Assert.Equal(new GuildTransaction(true, 640, 3, 0), state.LastGuildTransaction);
            Assert.True(state.LastGuildTransaction!.Succeeded);

            byte[] sellFailed = new byte[4];
            sellFailed[3] = 2; // ItemNo 0: failed, Trade says why
            dispatcher.Dispatch(new PacketHeader(0x084, 8, 2), sellFailed);
            Assert.Equal(new GuildTransaction(false, 0, 0, 2), state.LastGuildTransaction);
            Assert.False(state.LastGuildTransaction!.Succeeded);
        }

        [Fact]
        public void GuildSellList0x085_FirstPacketStartsANewList()
        {
            var (dispatcher, state) = Inventory();

            static byte[] Packet(byte stat, params ushort[] itemIds)
            {
                byte[] payload = new byte[244];
                for (int i = 0; i < itemIds.Length; i++)
                {
                    BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(i * 8, 2), itemIds[i]);
                    payload[i * 8 + 2] = 5;
                    payload[i * 8 + 3] = 20;
                    BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(i * 8 + 4, 4), 100 + i);
                }
                payload[240] = (byte)itemIds.Length;
                payload[241] = stat;
                return payload;
            }

            dispatcher.Dispatch(new PacketHeader(0x085, 248, 1), Packet(0x40, 1, 2));
            dispatcher.Dispatch(new PacketHeader(0x085, 248, 2), Packet(0x81, 3));
            Assert.Equal(new ushort[] { 1, 2, 3 }, state.SnapshotGuildSellList().Select(i => i.ItemId));
            Assert.Equal(new GuildItemEntry(1, 5, 20, 100), state.SnapshotGuildSellList()[0]);

            // A single-packet list (Stat 0x80) replaces the old one.
            dispatcher.Dispatch(new PacketHeader(0x085, 248, 3), Packet(0x80, 7));
            Assert.Equal(new ushort[] { 7 }, state.SnapshotGuildSellList().Select(i => i.ItemId));
        }

        [Fact]
        public void Bazaar0x106To0x10A_StorePurchaseVisitorsAndSales()
        {
            var (dispatcher, state) = Inventory();

            byte[] buy = new byte[20];
            BinaryPrimitives.WriteUInt32LittleEndian(buy.AsSpan(0, 4), (uint)BazaarBuyState.Err);
            WriteName(buy, 4, "Merchant");
            dispatcher.Dispatch(new PacketHeader(0x106, 24, 1), buy);
            Assert.Equal(new BazaarPurchaseResult(BazaarBuyState.Err, "Merchant"), state.LastBazaarPurchase);

            byte[] Visitor(BazaarShoppingState s)
            {
                byte[] payload = new byte[28];
                BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(0, 4), 0x0100AAAA);
                BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(4, 4), (uint)s);
                BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(10, 2), 0x0AA);
                WriteName(payload, 12, "Browser");
                return payload;
            }
            dispatcher.Dispatch(new PacketHeader(0x108, 32, 2), Visitor(BazaarShoppingState.Enter));
            Assert.Equal(new BazaarVisitor(0x0100AAAA, 0x0AA, "Browser"), Assert.Single(state.SnapshotBazaarVisitors()));
            dispatcher.Dispatch(new PacketHeader(0x108, 32, 3), Visitor(BazaarShoppingState.Exit));
            Assert.Empty(state.SnapshotBazaarVisitors());

            byte[] sold = new byte[32];
            BinaryPrimitives.WriteUInt32LittleEndian(sold.AsSpan(0, 4), 0x0100BBBB);
            BinaryPrimitives.WriteUInt32LittleEndian(sold.AsSpan(4, 4), 2);
            BinaryPrimitives.WriteUInt16LittleEndian(sold.AsSpan(8, 2), 0x0BB);
            WriteName(sold, 12, "Buyer");
            sold[28] = 4;
            dispatcher.Dispatch(new PacketHeader(0x109, 36, 4), sold);
            Assert.Equal(new BazaarSlotSold(0x0100BBBB, 0x0BB, "Buyer", 4, 2), state.LastBazaarSlotSold);

            byte[] sale = new byte[24];
            BinaryPrimitives.WriteUInt32LittleEndian(sale.AsSpan(0, 4), 2);
            BinaryPrimitives.WriteUInt16LittleEndian(sale.AsSpan(4, 2), 4509);
            WriteName(sale, 6, "Buyer");
            dispatcher.Dispatch(new PacketHeader(0x10A, 28, 5), sale);
            Assert.Equal(new BazaarSale(4509, 2, "Buyer"), Assert.Single(state.SnapshotBazaarSales()));
        }

        [Fact]
        public void BazaarSales_KeepOnlyTheMostRecent()
        {
            var state = new InventoryState();
            for (int i = 0; i < InventoryState.MaxBazaarSales + 5; i++) state.RecordBazaarSale(new BazaarSale((ushort)i, 1, "B"));

            var sales = state.SnapshotBazaarSales();
            Assert.Equal(InventoryState.MaxBazaarSales, sales.Length);
            Assert.Equal(5, sales[0].ItemId);
        }

        [Fact]
        public void Equipset0x116_StoresValidatedSlots()
        {
            var (dispatcher, state) = Inventory();

            byte[] payload = new byte[68];
            payload[0] = (byte)(((int)ContainerId.Wardrobe << 2) | 0x01);
            payload[1] = 6;
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(2, 2), 12500);
            payload[4] = 0x02; // entry 1: remove piece
            dispatcher.Dispatch(new PacketHeader(0x116, 72, 1), payload);

            var entries = state.SnapshotEquipsetValidation();
            Assert.Equal(17, entries.Length);
            Assert.Equal(new EquipsetSlotEntry(true, false, ContainerId.Wardrobe, 6, 12500), entries[0]);
            Assert.True(entries[1].RemoveItem);
        }

        [Fact]
        public void Equipset0x117_StoresResultAndReportsFailedPieces()
        {
            var (dispatcher, state) = Inventory();

            byte[] payload = new byte[132];
            payload[0] = 2; // two pieces changed
            // Changed: Head from inventory index 5, Body from wardrobe index 7.
            payload[4] = 5; payload[5] = (byte)EquipSlotId.Head; payload[6] = (byte)ContainerId.Inventory;
            payload[8] = 7; payload[9] = (byte)EquipSlotId.Body; payload[10] = (byte)ContainerId.Wardrobe;
            // Equipped: only the head piece went on.
            int equipped = 4 + 16 * 4;
            payload[equipped + 4 * 4] = 5; payload[equipped + 4 * 4 + 1] = (byte)EquipSlotId.Head; payload[equipped + 4 * 4 + 2] = (byte)ContainerId.Inventory;
            dispatcher.Dispatch(new PacketHeader(0x117, 136, 1), payload);

            var result = state.LastEquipsetResult;
            Assert.NotNull(result);
            Assert.Equal(2, result!.Changed.Count);
            Assert.Equal(16, result.Equipped.Count);
            var failed = Assert.Single(result.FailedItems);
            Assert.Equal(EquipSlotId.Body, failed.EquipSlot);
        }

        [Fact]
        public void Comlink0x0E0_StoresLinkshellItemLocation()
        {
            var state = new PartyState();
            var dispatcher = new PacketDispatcher();
            new PartyPacketModule(state, NoSend).Register(dispatcher);

            dispatcher.Dispatch(new PacketHeader(0x0E0, 8, 1), new byte[] { 2, 14, (byte)ContainerId.Inventory, 0 });
            Assert.True(state.HasLinkshell(2));
            Assert.Equal(new LinkshellItemLocation(14, ContainerId.Inventory), state.GetLinkshellItem(2));
            Assert.Null(state.GetLinkshellItem(1));

            dispatcher.Dispatch(new PacketHeader(0x0E0, 8, 2), new byte[] { 2, 0, 0, 0 });
            Assert.False(state.HasLinkshell(2));
            Assert.Null(state.GetLinkshellItem(2));
        }

        [Fact]
        public void PartyReq0x11D_TracksJoinRequests()
        {
            var state = new PartyState();
            var dispatcher = new PacketDispatcher();
            new PartyPacketModule(state, NoSend).Register(dispatcher);

            byte[] Request(byte status)
            {
                byte[] payload = new byte[28];
                BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(0, 4), 0x0100CCCC);
                BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(4, 2), 0x0CC);
                payload[7] = status;
                WriteName(payload, 8, "Joiner");
                BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(24, 2), 5);
                return payload;
            }

            int changes = 0;
            state.JoinRequestsChanged += () => changes++;

            dispatcher.Dispatch(new PacketHeader(0x11D, 32, 1), Request(0));
            var request = Assert.Single(state.SnapshotJoinRequests());
            Assert.Equal(0x0100CCCCu, request.ServerId);
            Assert.Equal("Joiner", request.Name);
            Assert.Equal(5, request.Race);

            dispatcher.Dispatch(new PacketHeader(0x11D, 32, 2), Request(1));
            Assert.Empty(state.SnapshotJoinRequests());
            Assert.Equal(2, changes);
        }

        [Fact]
        public void TalkNum0x036AndMyRoom0x0FA_StoreLastResult()
        {
            var state = new ProgressionState();
            var dispatcher = new PacketDispatcher();
            new ProgressionPacketModule(state, new LocalPlayerState(), NoSend).Register(dispatcher);

            byte[] talk = new byte[12];
            BinaryPrimitives.WriteUInt32LittleEndian(talk.AsSpan(0, 4), 0x01000010);
            BinaryPrimitives.WriteUInt16LittleEndian(talk.AsSpan(4, 2), 0x10);
            BinaryPrimitives.WriteUInt16LittleEndian(talk.AsSpan(6, 2), 0x8000 | 7000);
            dispatcher.Dispatch(new PacketHeader(0x036, 16, 1), talk);
            Assert.Equal(7000, state.LastDialogMessage!.MessageId);
            Assert.True(state.LastDialogMessage.HideName);

            byte[] op = new byte[8];
            BinaryPrimitives.WriteUInt16LittleEndian(op.AsSpan(0, 2), 3);
            op[2] = (byte)MyRoomOperationResult.PlantCheck;
            op[5] = 11;
            op[6] = (byte)ContainerId.MogSafe;
            dispatcher.Dispatch(new PacketHeader(0x0FA, 12, 2), op);
            Assert.Equal(new MyRoomOperationInfo(3, MyRoomOperationResult.PlantCheck, 11, ContainerId.MogSafe), state.LastMyRoomOperation);
        }
    }
}
