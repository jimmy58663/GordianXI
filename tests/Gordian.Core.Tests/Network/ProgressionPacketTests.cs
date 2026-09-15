// tests/Gordian.Core.Tests/Network/ProgressionPacketTests.cs
using System;
using System.Buffers.Binary;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using Gordian.Core.Network.Packets;
using Gordian.Core.World;
using Xunit;

namespace Gordian.Core.Tests.Network
{
    public class ProgressionPacketTests
    {
        [Fact]
        public void S2C_0x032_Event_DecodesCutsceneStart()
        {
            byte[] payload = new byte[16];
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(0, 4), 0x12345678); // UniqueNo
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(4, 2), 0x00A1);     // ActIndex
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(6, 2), 105);        // EventNum
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(8, 2), 1);          // EventPara
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(10, 2), 2);         // Mode

            var evt = new S2C_0x032_Event(payload);

            Assert.True(evt.IsValid);
            Assert.Equal(0x12345678u, evt.UniqueNo);
            Assert.Equal(0x00A1, evt.ActIndex);
            Assert.Equal(105, evt.EventNum);
            Assert.Equal(1, evt.EventPara);
            Assert.Equal(2, evt.Mode);
        }

        [Fact]
        public void S2C_0x033_EventStr_DecodesStringsAndData()
        {
            byte[] payload = new byte[120];
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(0, 4), 0x9999);
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(4, 2), 42);
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(6, 2), 500);

            // String 0: "Ayame"
            Encoding.ASCII.GetBytes("Ayame").CopyTo(payload.AsSpan(12));
            // Data 0: 0xCAFE
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(12 + 64, 4), 0xCAFE);

            var evt = new S2C_0x033_EventStr(payload);

            Assert.True(evt.IsValid);
            Assert.Equal(500, evt.EventNum);
            Assert.Equal("Ayame", evt.GetStringParam(0));
            Assert.Equal(0xCAFEu, evt.GetDataParam(0));
        }

        [Fact]
        public void S2C_0x034_EventNum_DecodesNumericParams()
        {
            byte[] payload = new byte[48];
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(0, 4), 0x5555);

            // 8 numeric params
            for (int i = 0; i < 8; i++)
            {
                BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(4 + (i * 4), 4), (i + 1) * 10);
            }

            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(36, 2), 15); // ActIndex
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(38, 2), 77); // EventNum

            var evt = new S2C_0x034_EventNum(payload);

            Assert.True(evt.IsValid);
            Assert.Equal(15, evt.ActIndex);
            Assert.Equal(77, evt.EventNum);
            Assert.Equal(10, evt.GetNumericParam(0));
            Assert.Equal(50, evt.GetNumericParam(4));
            Assert.Equal(80, evt.GetNumericParam(7));
        }

        [Fact]
        public void S2C_0x036_TalkNum_DecodesMessageIdAndType()
        {
            byte[] payload = new byte[10];
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(0, 4), 0x1111);
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(4, 2), 22);
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(6, 2), 7001); // MesNum
            payload[8] = 3; // Type

            var talk = new S2C_0x036_TalkNum(payload);

            Assert.True(talk.IsValid);
            Assert.Equal(7001, talk.MessageId);
            Assert.Equal(3, talk.Type);
        }

        [Fact]
        public void S2C_0x052_EventUcOff_DecodesMode()
        {
            byte[] payload = new byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(0, 4), (uint)EventUcOffMode.CancelEvent);

            var ucoff = new S2C_0x052_EventUcOff(payload);

            Assert.True(ucoff.IsValid);
            Assert.Equal(EventUcOffMode.CancelEvent, ucoff.Mode);
        }

        [Fact]
        public void C2S_0x05B_EventEnd_BuildsValidWireFormat()
        {
            byte[] packet = ProgressionPacketBuilder.BuildEventEnd(
                uniqueNo: 0x12345678,
                endPara: 2,
                actIndex: 12,
                mode: 0,
                eventNum: 105,
                eventPara: 1,
                sequenceId: 44
            );

            Assert.Equal(20, packet.Length);
            ushort headerWord = BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(0, 2));
            Assert.Equal(0x05B, headerWord & 0x1FF);
            Assert.Equal(5, headerWord >> 9); // 5 dwords = 20 bytes
            Assert.Equal(44, BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(2, 2)));
            Assert.Equal(0x12345678u, BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(4, 4)));
            Assert.Equal(2u, BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(8, 4)));
        }

        [Fact]
        public void C2S_0x05C_EventEndXzy_BuildsValidWireFormatWithCoordinates()
        {
            var pos = new Vector3(10.5f, -2.25f, 100.0f);
            byte[] packet = ProgressionPacketBuilder.BuildEventEndXzy(
                position: pos,
                uniqueNo: 0x999,
                endPara: 0,
                eventNum: 200,
                eventPara: 3,
                actIndex: 1,
                mode: 1,
                dir: -64,
                sequenceId: 10
            );

            Assert.Equal(32, packet.Length);
            ushort headerWord = BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(0, 2));
            Assert.Equal(0x05C, headerWord & 0x1FF);
            Assert.Equal(8, headerWord >> 9); // 8 dwords = 32 bytes

            float x = BinaryPrimitives.ReadSingleLittleEndian(packet.AsSpan(4, 4));
            float y = BinaryPrimitives.ReadSingleLittleEndian(packet.AsSpan(8, 4));
            float z = BinaryPrimitives.ReadSingleLittleEndian(packet.AsSpan(12, 4));

            Assert.Equal(10.5f, x);
            Assert.Equal(-2.25f, y);
            Assert.Equal(100.0f, z);
            Assert.Equal((sbyte)-64, (sbyte)packet[31]);
        }

        [Fact]
        public void S2C_0x055_ScenarioItem_DecodesKeyItemsBitmask()
        {
            byte[] payload = new byte[132];
            // TableIndex at byte 128
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(128, 2), 0);
            // Acquired: bit 5 of dword 0 (key item 5)
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(0, 4), 1u << 5);
            // Seen: bit 5 of dword 0 (key item 5 seen)
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(64, 4), 1u << 5);

            var scenario = new S2C_0x055_ScenarioItem(payload);

            Assert.True(scenario.IsValid);
            Assert.Equal(0, scenario.TableIndex);
            Assert.Equal(32u, scenario.GetAcquiredFlag(0));
            Assert.Equal(32u, scenario.GetSeenFlag(0));
        }

        [Fact]
        public void C2S_0x064_ScenarioItem_BuildsReadConfirmation()
        {
            uint[] flags = new uint[16];
            flags[0] = 0x00000001;

            byte[] packet = ProgressionPacketBuilder.BuildScenarioItemRead(0x1234, 10, 0, flags, 99);

            Assert.Equal(76, packet.Length);
            ushort headerWord = BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(0, 2));
            Assert.Equal(0x064, headerWord & 0x1FF);
            Assert.Equal(19, headerWord >> 9); // 19 dwords = 76 bytes
            Assert.Equal(0x1234u, BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(4, 4)));
            Assert.Equal(1u, BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(8, 4)));
            Assert.Equal(10, BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(72, 2)));
        }

        [Fact]
        public void S2C_0x056_Mission_DecodesMainStoryExpansions()
        {
            byte[] payload = new byte[36];
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(0, 4), 1);   // Nation (Bastok)
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(4, 4), 15);  // NationMission
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(8, 4), 22);  // RotZ
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(12, 4), 30); // CoP
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(32, 2), 0xFFFF); // Main port

            var mission = new S2C_0x056_Mission(payload);

            Assert.True(mission.IsValid);
            Assert.True(mission.IsMainPort);
            Assert.Equal(1u, mission.Nation);
            Assert.Equal(15u, mission.NationMission);
            Assert.Equal(22u, mission.ExpansionRotZ);
            Assert.Equal(30u, mission.ExpansionCoP);
        }

        [Fact]
        public void S2C_0x02E_0x096_0x0FA_MogHouse_DecodesCorrectly()
        {
            // 0x02E OpenMogMenu
            var mogMenu = new S2C_0x02E_OpenMogMenu(ReadOnlySpan<byte>.Empty);
            Assert.True(mogMenu.IsValid);

            // 0x096 MyRoomEnter
            byte[] enterPayload = new byte[4] { 1, 0, 0, 0 };
            var enter = new S2C_0x096_MyRoomEnter(enterPayload);
            Assert.True(enter.IsValid);
            Assert.Equal(1, enter.Result);

            // 0x0FA MyRoomOperation
            byte[] opPayload = new byte[8];
            BinaryPrimitives.WriteUInt16LittleEndian(opPayload.AsSpan(0, 2), 1234); // ItemNo
            opPayload[2] = (byte)MyRoomOperationResult.Layout;
            opPayload[5] = 3; // Index
            opPayload[6] = 2; // Category

            var op = new S2C_0x0FA_MyRoomOperation(opPayload);
            Assert.True(op.IsValid);
            Assert.Equal(1234, op.MyroomItemNo);
            Assert.Equal(MyRoomOperationResult.Layout, op.Result);
            Assert.Equal(3, op.MyroomItemIndex);
        }

        [Fact]
        public void C2S_0x100_MyRoomJob_BuildsJobChangePacket()
        {
            byte[] packet = ProgressionPacketBuilder.BuildMyRoomJob(mainJob: 1, subJob: 4, sequenceId: 7);

            Assert.Equal(8, packet.Length);
            ushort headerWord = BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(0, 2));
            Assert.Equal(0x100, headerWord & 0x1FF);
            Assert.Equal(2, headerWord >> 9);
            Assert.Equal(1, packet[4]); // MainJob
            Assert.Equal(4, packet[5]); // SubJob
        }

        [Fact]
        public void S2C_0x08C_Merit_DecodesTotalAndEntries()
        {
            byte[] payload = new byte[12];
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(0, 2), 45); // MeritCount

            // Entry 0
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(4, 2), 10); // Index
            payload[6] = 2; // Next
            payload[7] = 5; // Count (Level)

            var merit = new S2C_0x08C_Merit(payload);

            Assert.True(merit.IsValid);
            Assert.Equal(45, merit.MeritCount);
            var entry = merit.GetMeritEntry(0);
            Assert.Equal(10, entry.Index);
            Assert.Equal(2, entry.Next);
            Assert.Equal(5, entry.Count);
        }

        [Fact]
        public void S2C_0x08D_JobPoints_DecodesEntries()
        {
            byte[] payload = new byte[256];

            // Entry 0: Index=3, JobNo=5, Next=10, Level=7
            // word1: Index (5 bits) | (JobNo << 5) = 3 | (5 << 5) = 3 | 160 = 163
            // word2: Next (10 bits) | (Level << 10) = 10 | (7 << 10) = 10 | 7168 = 7178
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(0, 2), (ushort)(3 | (5 << 5)));
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(2, 2), (ushort)(10 | (7 << 10)));

            var jp = new S2C_0x08D_JobPoints(payload);

            Assert.True(jp.IsValid);
            var entry = jp.GetJobPoint(0);
            Assert.Equal(3, entry.Index);
            Assert.Equal(5, entry.JobNo);
            Assert.Equal(10, entry.Next);
            Assert.Equal(7, entry.Level);
        }

        [Fact]
        public void S2C_0x111_0x112_Roe_DecodesActiveAndCompletedLogs()
        {
            // 0x111 active log
            byte[] activePayload = new byte[256];
            // ObjectiveId: 25, Progress: 100
            uint raw = 25u | (100u << 12);
            BinaryPrimitives.WriteUInt32LittleEndian(activePayload.AsSpan(0, 4), raw);

            var active = new S2C_0x111_RoeActiveLog(activePayload);
            Assert.True(active.IsValid);
            var obj = active.GetActiveObjective(0);
            Assert.Equal(25, obj.ObjectiveId);
            Assert.Equal(100u, obj.Progress);

            // 0x112 completed log
            byte[] logPayload = new byte[132];
            logPayload[0] = 0x01; // First bit set = objective 0 complete
            BinaryPrimitives.WriteUInt16LittleEndian(logPayload.AsSpan(128, 2), 0); // Offset

            var logChunk = new S2C_0x112_RoeLog(logPayload);
            Assert.True(logChunk.IsValid);
            Assert.Equal(0, logChunk.Offset);
            Assert.Equal(0x01, logChunk.GetData()[0]);
        }

        [Fact]
        public void S2C_0x05E_0x115_0x073_0x110_DecodesCorrectly()
        {
            // 0x05E Conquest
            byte[] conqPayload = new byte[184];
            conqPayload[0] = 1; // Balance
            conqPayload[1] = 2; // Alliance
            BinaryPrimitives.WriteUInt32LittleEndian(conqPayload.AsSpan(144, 4), 50000); // CP

            var conq = new S2C_0x05E_Conquest(conqPayload);
            Assert.True(conq.IsValid);
            Assert.Equal(1, conq.Balance);
            Assert.Equal(50000u, conq.ConquestPoints);

            // 0x115 Fish
            byte[] fishPayload = new byte[20];
            BinaryPrimitives.WriteUInt16LittleEndian(fishPayload.AsSpan(0, 2), 1000); // Stamina
            BinaryPrimitives.WriteUInt16LittleEndian(fishPayload.AsSpan(12, 2), 30);   // Time

            var fish = new S2C_0x115_Fish(fishPayload);
            Assert.True(fish.IsValid);
            Assert.Equal(1000, fish.Stamina);
            Assert.Equal(30, fish.Time);

            // 0x073 Toteboard
            byte[] totePayload = new byte[68];
            BinaryPrimitives.WriteUInt32LittleEndian(totePayload.AsSpan(0, 4), 1); // SlotIndex
            BinaryPrimitives.WriteUInt16LittleEndian(totePayload.AsSpan(12, 2), 250); // Odds pair 0 = 25.0

            var tote = new S2C_0x073_ChocoboToteboard(totePayload);
            Assert.True(tote.IsValid);
            Assert.Equal(1u, tote.SlotIndex);
            Assert.Equal(250, tote.GetOdds(0));

            // 0x110 Unity
            byte[] unityPayload = new byte[10];
            BinaryPrimitives.WriteUInt32LittleEndian(unityPayload.AsSpan(0, 4), 12345); // Sparks
            BinaryPrimitives.WriteUInt16LittleEndian(unityPayload.AsSpan(4, 2), 50);    // Deeds

            var unity = new S2C_0x110_Unity(unityPayload);
            Assert.True(unity.IsValid);
            Assert.Equal(12345u, unity.Sparks);
            Assert.Equal(50, unity.Deeds);
        }

        [Fact]
        public void S2C_0x0E0_0x0E2_0x11D_PartyPackets_DecodeCorrectly()
        {
            // 0x0E0 GroupComlink
            byte[] comlinkPayload = new byte[4] { 1, 5, 2, 0 };
            var comlink = new S2C_0x0E0_GroupComlink(comlinkPayload);
            Assert.True(comlink.IsValid);
            Assert.Equal(1, comlink.LinkshellNum);
            Assert.Equal(5, comlink.ItemIndex);

            // 0x0E2 GroupList2
            byte[] list2Payload = new byte[52];
            BinaryPrimitives.WriteUInt32LittleEndian(list2Payload.AsSpan(0, 4), 999);
            BinaryPrimitives.WriteUInt32LittleEndian(list2Payload.AsSpan(4, 4), 800); // HP
            BinaryPrimitives.WriteUInt16LittleEndian(list2Payload.AsSpan(20, 2), 15);  // ActIndex
            Encoding.ASCII.GetBytes("Kupopo").CopyTo(list2Payload.AsSpan(36));

            var list2 = new S2C_0x0E2_GroupList2(list2Payload);
            Assert.True(list2.IsValid);
            Assert.Equal(999u, list2.UniqueNo);
            Assert.Equal(800u, list2.Hp);
            Assert.Equal("Kupopo", list2.GetName());

            // 0x11D PartyReq
            byte[] reqPayload = new byte[8];
            BinaryPrimitives.WriteUInt32LittleEndian(reqPayload.AsSpan(0, 4), 555);
            BinaryPrimitives.WriteUInt16LittleEndian(reqPayload.AsSpan(4, 2), 7);
            reqPayload[6] = 1; // Result

            var partyReq = new S2C_0x11D_PartyReq(reqPayload);
            Assert.True(partyReq.IsValid);
            Assert.Equal(555u, partyReq.UniqueNo);
            Assert.Equal(1, partyReq.Result);
        }

        [Fact]
        public void ProgressionState_ManagesKeyItemsAndEvents()
        {
            var state = new ProgressionState();
            bool eventFired = false;
            state.EventStarted += info =>
            {
                eventFired = true;
                Assert.Equal(100, info.EventNum);
            };

            state.StartEvent(1, 2, 100, 0, 1);
            Assert.True(state.IsInEvent);
            Assert.True(eventFired);

            state.EndEvent();
            Assert.False(state.IsInEvent);

            // Key Items
            uint[] acq = new uint[16];
            uint[] seen = new uint[16];
            acq[0] = 1u << 3; // Key item 3
            seen[0] = 1u << 3;

            state.UpdateKeyItems(0, acq, seen);
            Assert.True(state.HasKeyItem(3));
            Assert.True(state.IsKeyItemSeen(3));
            Assert.False(state.HasKeyItem(4));
        }

        [Fact]
        public async Task ProgressionPacketModule_RegistersAndDispatchesPackets()
        {
            var state = new ProgressionState();
            var player = new LocalPlayerState();
            byte[]? sentChunk = null;

            var module = new ProgressionPacketModule(
                state,
                player,
                (data, immediate) =>
                {
                    sentChunk = data.ToArray();
                    return Task.CompletedTask;
                }
            );

            var dispatcher = new PacketDispatcher();
            module.Register(dispatcher);

            // Dispatch 0x032 Event
            byte[] eventPayload = new byte[16];
            BinaryPrimitives.WriteUInt16LittleEndian(eventPayload.AsSpan(6, 2), 222); // EventNum
            var header = new PacketHeader(0x032, 20, 1);

            bool handled = dispatcher.Dispatch(header, eventPayload);
            Assert.True(handled);
            Assert.True(state.IsInEvent);
            Assert.Equal(222, state.ActiveEvent!.EventNum);

            // Send EventEnd
            await module.SendEventEndAsync(1, 0, 10, 0, 222, 0);
            Assert.NotNull(sentChunk);
            Assert.False(state.IsInEvent);
        }
    }
}
