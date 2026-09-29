// tests/Gordian.Core.Tests/Network/TreasurePacketTests.cs
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Gordian.Core.Network;
using Gordian.Core.Network.Packets;
using Gordian.Core.Resources.Models;
using Gordian.Core.Ui;
using Gordian.Core.World;
using Xunit;

namespace Gordian.Core.Tests.Network
{
    public class TreasurePacketTests
    {
        private const uint LocalId = 0x01000001;
        private const uint MobId = 0x01000200;

        private static byte[] TrophyList(byte slot, ushort itemId, ushort gold = 0, byte entry = 0, bool container = false,
            bool named = false, ushort localLot = 0, string leader = "", ushort leaderLot = 0, uint leaderId = 0)
        {
            var p = new byte[56];
            BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(0, 4), 1);
            BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(4, 4), MobId);
            BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(8, 2), gold);
            BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(12, 2), itemId);
            BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(14, 2), 0x200);
            p[16] = slot;
            p[17] = entry;
            p[18] = (byte)(container ? 1 : 0);
            BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(20, 4), 123456);
            BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(24, 2), (ushort)(localLot > 0 ? 1 : 0));
            BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(26, 2), localLot);
            BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(28, 4), leaderId);
            BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(34, 2), leaderLot);
            Encoding.ASCII.GetBytes(leader).CopyTo(p.AsSpan(36));
            p[52] = (byte)(named ? 1 : 0);
            return p;
        }

        private static byte[] Solution(byte slot, byte judge, uint leaderId = 0, string leader = "", short leaderLot = 0,
            uint entryId = 0, string entry = "", bool isLot = false, short entryLot = 0)
        {
            var p = new byte[56];
            BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(0, 4), leaderId);
            BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(4, 4), entryId);
            BinaryPrimitives.WriteInt16LittleEndian(p.AsSpan(10, 2), leaderLot);
            BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(12, 2), (ushort)(0x0345 | (isLot ? 0x8000 : 0)));
            BinaryPrimitives.WriteInt16LittleEndian(p.AsSpan(14, 2), entryLot);
            p[16] = slot;
            p[17] = judge;
            Encoding.ASCII.GetBytes(leader).CopyTo(p.AsSpan(18));
            Encoding.ASCII.GetBytes(entry).CopyTo(p.AsSpan(34));
            return p;
        }

        private static (TreasurePoolState, PacketDispatcher, List<byte[]>) Create()
        {
            var pool = new TreasurePoolState();
            var player = new LocalPlayerState { ServerId = LocalId };
            var sent = new List<byte[]>();
            var module = new TreasurePacketModule(pool, player, null, (data, _) =>
            {
                sent.Add(data.ToArray());
                return Task.CompletedTask;
            });
            var dispatcher = new PacketDispatcher();
            module.Register(dispatcher);
            return (pool, dispatcher, sent);
        }

        private static StockUiTreasure Log() => new(
            id => new ItemRecord { ItemId = id, Name = "Fire Crystal", LogName = "fire crystal", StackSize = 12 },
            id => id == MobId ? "Goblin" : id == LocalId ? "Me" : null,
            () => LocalId);

        [Fact]
        public void TrophyList_DecodesEveryField()
        {
            var d = new S2C_0x0D2_TrophyList(TrophyList(3, 4096, gold: 25, entry: 2, container: true, named: true, localLot: 500,
                leader: "Ayame", leaderLot: 700, leaderId: 0x1234));

            Assert.True(d.IsValid);
            Assert.Equal(3, d.Slot);
            Assert.Equal(4096, d.ItemId);
            Assert.Equal(25, d.Gold);
            Assert.Equal(MobId, d.DropperId);
            Assert.Equal(TreasureEntryKind.Lot, d.Entry);
            Assert.True(d.IsContainer);
            Assert.True(d.Named);
            Assert.True(d.IsLocallyLotted);
            Assert.Equal(500, d.LocalLot);
            Assert.Equal("Ayame", d.LeaderName);
            Assert.Equal(700, d.LeaderLot);
            Assert.Equal(123456u, d.StartTime);
        }

        [Fact]
        public void TrophyList_ShortPayloadIsInvalid()
        {
            Assert.False(new S2C_0x0D2_TrophyList(new byte[52]).IsValid);
            Assert.False(new S2C_0x0D3_TrophySolution(new byte[49]).IsValid);
        }

        [Fact]
        public void TrophySolution_DecodesLotAndPass()
        {
            var d = new S2C_0x0D3_TrophySolution(Solution(4, 0, 0x99, "Ayame", 800, LocalId, "Me", isLot: true, entryLot: 512));

            Assert.True(d.IsValid);
            Assert.Equal(4, d.Slot);
            Assert.Equal(TreasureJudge.Progress, d.Judge);
            Assert.Equal(0x99u, d.LeaderId);
            Assert.Equal("Ayame", d.LeaderName);
            Assert.Equal(800, d.LeaderLot);
            Assert.Equal(0x0345, d.EntryIndex);
            Assert.True(d.EntryIsLot);
            Assert.Equal(512, d.EntryLot);
            Assert.Equal("Me", d.EntryName);
        }

        [Fact]
        public void Dispatcher_FillsTheSlotAndTracksTheLocalLot()
        {
            var (pool, dispatcher, _) = Create();

            Assert.True(dispatcher.Dispatch(new PacketHeader(0x0D2, 60, 1), TrophyList(2, 4096)));
            var slot = pool.GetSlot(2)!;
            Assert.Equal(4096, slot.ItemId);
            Assert.Equal(TreasureEntryKind.None, slot.Entry);
            Assert.Equal(1, pool.Count);

            // Another player lots: the leader changes, the local entry does not.
            dispatcher.Dispatch(new PacketHeader(0x0D3, 60, 2), Solution(2, 0, 0x99, "Ayame", 800, 0x99, "Ayame", true, 800));
            slot = pool.GetSlot(2)!;
            Assert.Equal("Ayame", slot.LeaderName);
            Assert.Equal(800, slot.LeaderLot);
            Assert.Equal(TreasureEntryKind.None, slot.Entry);

            // The local player lots.
            dispatcher.Dispatch(new PacketHeader(0x0D3, 60, 3), Solution(2, 0, 0x99, "Ayame", 800, LocalId, "Me", true, 512));
            slot = pool.GetSlot(2)!;
            Assert.Equal(TreasureEntryKind.Lot, slot.Entry);
            Assert.Equal(512, slot.LocalLot);
        }

        [Fact]
        public void Dispatcher_PassIsRecordedAndJudgementEmptiesTheSlot()
        {
            var (pool, dispatcher, _) = Create();
            dispatcher.Dispatch(new PacketHeader(0x0D2, 60, 1), TrophyList(0, 4096));

            dispatcher.Dispatch(new PacketHeader(0x0D3, 60, 2), Solution(0, 0, entryId: LocalId, entry: "Me", isLot: false));
            Assert.Equal(TreasureEntryKind.Pass, pool.GetSlot(0)!.Entry);

            TreasureSolution? seen = null;
            pool.Solved += s => seen = s;
            dispatcher.Dispatch(new PacketHeader(0x0D3, 60, 3), Solution(0, 1, 0x99, "Ayame"));

            Assert.Null(pool.GetSlot(0));
            Assert.Equal(0, pool.Count);
            Assert.Equal(4096, seen!.ItemId); // read before the slot was cleared
        }

        [Fact]
        public void Dispatcher_ReenteringAZoneRestoresTheLocalLot()
        {
            var (pool, dispatcher, _) = Create();
            dispatcher.Dispatch(new PacketHeader(0x0D2, 60, 1), TrophyList(1, 4096, entry: 2, localLot: 640, leader: "Me", leaderLot: 640, leaderId: LocalId));

            var slot = pool.GetSlot(1)!;
            Assert.Equal(TreasureEntryKind.Lot, slot.Entry);
            Assert.Equal(640, slot.LocalLot);
            Assert.Equal(640, slot.LeaderLot);
        }

        [Fact]
        public void Dispatcher_GoldOnlyPacketTakesNoSlotButIsReported()
        {
            var (pool, dispatcher, _) = Create();
            TreasureFound? found = null;
            pool.Found += f => found = f;

            dispatcher.Dispatch(new PacketHeader(0x0D2, 60, 1), TrophyList(0, 0, gold: 100));

            Assert.Equal(0, pool.Count);
            Assert.Equal(100u, found!.Gold);
        }

        [Fact]
        public void Dispatcher_IgnoresAnOutOfRangeSlot()
        {
            var (pool, dispatcher, _) = Create();
            dispatcher.Dispatch(new PacketHeader(0x0D2, 60, 1), TrophyList(10, 4096));
            Assert.Equal(0, pool.Count);
        }

        [Fact]
        public void Clear_EmptiesThePool()
        {
            var (pool, dispatcher, _) = Create();
            dispatcher.Dispatch(new PacketHeader(0x0D2, 60, 1), TrophyList(0, 4096));
            pool.Clear();
            Assert.Equal(0, pool.Count);
        }

        [Fact]
        public void Builders_ProduceTheClientPackets()
        {
            byte[] lot = TreasurePacketBuilder.BuildLot(7, 12, 0x0102);
            Assert.Equal(new byte[] { 0x41, 0x04, 0x02, 0x01, 7, 12, 0, 0 }, lot);

            byte[] pass = TreasurePacketBuilder.BuildPass(3, 0x0102);
            Assert.Equal(new byte[] { 0x42, 0x04, 0x02, 0x01, 3, 0, 0, 0 }, pass);
        }

        [Fact]
        public async Task Module_SendsLotAndPass()
        {
            var sent = new List<byte[]>();
            var module = new TreasurePacketModule(new TreasurePoolState(), new LocalPlayerState(), null, (data, _) =>
            {
                sent.Add(data.ToArray());
                return Task.CompletedTask;
            });
            await module.SendLotAsync(5);
            await module.SendPassAsync(6);

            Assert.Equal(2, sent.Count);
            Assert.Equal(0x041, BinaryPrimitives.ReadUInt16LittleEndian(sent[0]) & 0x1FF);
            Assert.Equal(5, sent[0][4]);
            Assert.Equal(0x042, BinaryPrimitives.ReadUInt16LittleEndian(sent[1]) & 0x1FF);
            Assert.Equal(6, sent[1][4]);
        }

        [Fact]
        public void Log_FoundMessages()
        {
            var log = Log();
            Assert.Equal(new[] { "You find a fire crystal on the Goblin." },
                log.FormatFound(new TreasureFound(0, 4096, 1, MobId, 0x200, false, false)));
            Assert.Equal(new[] { "You find a fire crystal in the Goblin." },
                log.FormatFound(new TreasureFound(0, 4096, 1, MobId, 0x200, true, false)));
            Assert.Equal(new[] { "You find a fire crystal on Goblin." },
                log.FormatFound(new TreasureFound(0, 4096, 1, MobId, 0x200, false, true)));
            Assert.Equal(new[] { "You find 1,250 gil on the Goblin.", "You find a fire crystal on the Goblin." },
                log.FormatFound(new TreasureFound(1250, 4096, 1, MobId, 0x200, false, false)));
        }

        [Fact]
        public void Log_LotWinAndLossMessages()
        {
            var log = Log();
            Assert.Equal(new[] { "Ayame's lot for the fire crystal: 512 points." },
                log.FormatSolution(new TreasureSolution(0, 4096, TreasureJudge.Progress, 0x99, "Ayame", 0x99, "Ayame", true, 512)));
            Assert.Empty(log.FormatSolution(new TreasureSolution(0, 4096, TreasureJudge.Progress, 0x99, "Ayame", 0x99, "Ayame", false, 0)));

            Assert.Equal(new[] { "You obtain a fire crystal." },
                log.FormatSolution(new TreasureSolution(0, 4096, TreasureJudge.Win, LocalId, "Me", 0, "", false, 0)));
            Assert.Equal(new[] { "Ayame obtains a fire crystal." },
                log.FormatSolution(new TreasureSolution(0, 4096, TreasureJudge.Win, 0x99, "Ayame", 0, "", false, 0)));

            Assert.Equal(new[] { "Ayame does not meet the necessary requirements to obtain the fire crystal.", "Fire crystal lost." },
                log.FormatSolution(new TreasureSolution(0, 4096, TreasureJudge.WinError, 0x99, "Ayame", 0, "", false, 0)));
            Assert.Empty(log.FormatSolution(new TreasureSolution(0, 4096, TreasureJudge.Lost, 0, "", 0, "", false, 0)));
        }

        [Fact]
        public void Router_ParsesLotAndPass()
        {
            var world = new WorldState();
            var lot = ChatCommandRouter.Parse("/lot 3", ChatSendKind.Say, world);
            Assert.Equal(ChatCommandResultKind.TreasureLot, lot.Kind);
            Assert.Equal("3", lot.Message);
            Assert.Equal(ChatCommandResultKind.TreasurePass, ChatCommandRouter.Parse("/pass", ChatSendKind.Say, world).Kind);
        }
    }
}
