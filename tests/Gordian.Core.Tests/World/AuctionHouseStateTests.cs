// tests/Gordian.Core.Tests/World/AuctionHouseStateTests.cs
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Gordian.Core.Network;
using Gordian.Core.Network.Packets;
using Gordian.Core.World;
using Xunit;

namespace Gordian.Core.Tests.World
{
    /// <summary>
    /// The Auction House cache (#90) fed by S2C 0x04C, with the answers LandSandBoat sends (utils/auctionutils.cpp,
    /// packets/s2c/0x04c_auc.cpp): MOPEN, the Info reset, the sale-slot replies, a cancel emptying its slot, the AskCommit fee.
    /// </summary>
    public class AuctionHouseStateTests
    {
        private static Task NoSend(ReadOnlyMemory<byte> data, bool immediate) => Task.CompletedTask;

        private static (PacketDispatcher Dispatcher, InventoryState State) Inventory()
        {
            var state = new InventoryState();
            var dispatcher = new PacketDispatcher();
            new InventoryPacketModule(state, new LocalPlayerState(), NoSend).Register(dispatcher);
            return (dispatcher, state);
        }

        private static ushort _sequence;

        private static void Send(PacketDispatcher dispatcher, AuctionCommand command, sbyte slot, byte result,
            ushort itemId = 0, uint price = 0, byte quantity = 0, byte stat = 0, string seller = "", uint param0 = 0, uint param1 = 0, uint param2 = 0)
        {
            byte[] payload = new byte[56];
            payload[0] = (byte)command;
            payload[1] = (byte)slot;
            payload[2] = result;
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(4), param0);
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(8), param1);
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(12), param2);
            if (itemId != 0)
            {
                payload[16] = stat;
                Encoding.ASCII.GetBytes(seller).CopyTo(payload.AsSpan(20));
                BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(36), itemId);
                payload[38] = quantity;
                BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(40), price);
            }
            dispatcher.Dispatch(new PacketHeader(0x04C, 60, ++_sequence), payload);
        }

        [Fact]
        public void Mopen_OpensTheWindow()
        {
            var (dispatcher, state) = Inventory();
            int opened = 0;
            state.AuctionHouse.Opened += () => opened++;

            Send(dispatcher, AuctionCommand.Open, -1, 1);

            Assert.True(state.AuctionHouse.IsOpen);
            Assert.Equal(1, opened);
            state.AuctionHouse.Close();
            Assert.False(state.AuctionHouse.IsOpen);
        }

        [Fact]
        public void Mopen_ClosedForTrading_DoesNotOpen()
        {
            var (dispatcher, state) = Inventory();
            AuctionResponse? seen = null;
            state.AuctionHouse.ResponseReceived += r => seen = r;

            Send(dispatcher, AuctionCommand.Open, -1, 0xFF);

            Assert.False(state.AuctionHouse.IsOpen);
            Assert.Equal(AuctionResultCode.OnAuction, seen!.ResultCode);
        }

        [Fact]
        public void SaleSlotReplies_FillTheSlots_StackFromQuantityZero()
        {
            var (dispatcher, state) = Inventory();

            // LandSandBoat's slot reply: command 0x0C, Stat 3 (LotInDone), ItemQuantity 1 - stack.
            Send(dispatcher, AuctionCommand.LotCancel, 0, 1, itemId: 4096, price: 900, quantity: 0, stat: 3);
            Send(dispatcher, AuctionCommand.LotCheck, 2, 1, itemId: 640, price: 50, quantity: 1, stat: 3, seller: "Seller");

            var sales = state.AuctionHouse.SnapshotSales();
            Assert.Equal(AuctionHouseState.SlotCount, sales.Length);
            Assert.Equal(new AuctionSale(0, 4096, true, 900, AuctionParcelStat.LotInDone, string.Empty), sales[0]);
            Assert.Equal(new AuctionSale(2, 640, false, 50, AuctionParcelStat.LotInDone, "Seller"), sales[2]);
            Assert.Null(sales[1]);
            Assert.Equal(1, state.AuctionHouse.NextFreeSlot);
        }

        [Fact]
        public void CancelWithoutParcel_EmptiesTheSlot_InfoResetsTheList()
        {
            var (dispatcher, state) = Inventory();
            Send(dispatcher, AuctionCommand.LotCancel, 0, 1, itemId: 4096, price: 900, quantity: 1, stat: 3);
            Send(dispatcher, AuctionCommand.LotCancel, 1, 1, itemId: 4097, price: 100, quantity: 1, stat: 3);

            // A successful cancel (LandSandBoat: result 0, no parcel).
            Send(dispatcher, AuctionCommand.LotCancel, 0, 0);
            Assert.Null(state.AuctionHouse.SnapshotSales()[0]);
            Assert.NotNull(state.AuctionHouse.SnapshotSales()[1]);

            int changes = 0;
            state.AuctionHouse.SalesChanged += () => changes++;
            Send(dispatcher, AuctionCommand.Info, -1, 1);
            Assert.All(state.AuctionHouse.SnapshotSales(), Assert.Null);
            Assert.Equal(1, changes);

            // "Please try again in a little while." (0xF6) keeps the list.
            Send(dispatcher, AuctionCommand.LotCancel, 3, 1, itemId: 4096, price: 900, quantity: 1, stat: 3);
            Send(dispatcher, AuctionCommand.Info, -1, 0xF6);
            Assert.NotNull(state.AuctionHouse.SnapshotSales()[3]);
        }

        [Fact]
        public void AskCommitReply_QuotesTheFee()
        {
            var (dispatcher, state) = Inventory();

            // Param.AskCommit: Commission (the fee), ItemWorkIndex | ItemNo << 16, ItemStacks.
            Send(dispatcher, AuctionCommand.AskCommit, -1, 1, param0: 15, param1: 9u | (4096u << 16), param2: 0);

            Assert.Equal(new AuctionListingQuote(4096, 9, true, 15), state.AuctionHouse.LastQuote);

            Send(dispatcher, AuctionCommand.AskCommit, -1, 0xC5);
            Assert.Null(state.AuctionHouse.LastQuote);
        }

        [Fact]
        public void AllSlotsUsed_NoFreeSlot()
        {
            var state = new AuctionHouseState();
            for (sbyte i = 0; i < AuctionHouseState.SlotCount; i++)
            {
                state.Apply(new AuctionResponse(AuctionCommand.LotCheck, i, 1, 0, 3, 1, 4096, 1, 10, string.Empty));
            }
            Assert.Equal(-1, state.NextFreeSlot);
        }
    }
}
