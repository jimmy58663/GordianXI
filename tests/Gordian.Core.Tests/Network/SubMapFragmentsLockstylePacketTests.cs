// tests/Gordian.Core.Tests/Network/SubMapFragmentsLockstylePacketTests.cs
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Gordian.Core.Config;
using Gordian.Core.Network;
using Gordian.Core.Network.Packets;
using Gordian.Core.Resources.Models;
using Gordian.Core.Ui;
using Xunit;

namespace Gordian.Core.Tests.Network
{
    /// <summary>
    /// #117: sub-map packets (S2C 0x10E / 0x10F, C2S 0x0EB / 0x0F2), session control (S2C 0x005 / 0x006), the server
    /// message fragments (S2C 0x04D, C2S 0x04B) and the style lock error (S2C 0x11C).
    /// </summary>
    public class SubMapFragmentsLockstylePacketTests
    {
        private static byte[] U32(uint value)
        {
            var b = new byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(b, value);
            return b;
        }

        private static (PacketParser Parser, PacketDispatcher Dispatcher, List<byte[]> Sent) MakeParser()
        {
            var sent = new List<byte[]>();
            var dispatcher = new PacketDispatcher();
            var parser = new PacketParser(new SessionProfile(), (data, _) =>
            {
                sent.Add(data.ToArray());
                return Task.CompletedTask;
            }, dispatcher: dispatcher);
            return (parser, dispatcher, sent);
        }

        private static void Dispatch(PacketDispatcher dispatcher, ushort id, byte[] payload) =>
            dispatcher.Dispatch(new PacketHeader(id, payload.Length + 4, 1), payload);

        #region Sub-map

        [Fact]
        public void S2C_0x10E_DecodesMapNum()
        {
            var p = new S2C_0x10E_ReqSubMapNum(U32(3));
            Assert.True(p.IsValid);
            Assert.Equal(3u, p.MapNum);
            Assert.False(new S2C_0x10E_ReqSubMapNum(new byte[3]).IsValid);
        }

        [Fact]
        public void S2C_0x10F_And_0x005_DecodeTheirWord()
        {
            Assert.Equal(2u, new S2C_0x10F_ReqLogoutInfo(U32(2)).Mode);
            var control = new S2C_0x005_PacketControl(U32(800).Concat(new byte[20]).ToArray());
            Assert.True(control.IsValid);
            Assert.Equal(800u, control.PacketCount);
            Assert.False(new S2C_0x005_PacketControl(Array.Empty<byte>()).IsValid);
            Assert.True(new S2C_0x006_Naraku(Array.Empty<byte>()).IsValid);
        }

        [Fact]
        public void C2S_0x0EB_IsTheFourByteHeader()
        {
            byte[] packet = SubMapOutboundPackets.BuildReqSubMapNum(sequenceId: 0x1234);
            Assert.Equal(4, packet.Length);
            ushort header = BinaryPrimitives.ReadUInt16LittleEndian(packet);
            Assert.Equal(0x0EB, header & 0x1FF);
            Assert.Equal(1, header >> 9); // size in 4-byte words: LSB's struct is the header alone
            Assert.Equal(0x1234, BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(2)));
        }

        [Fact]
        public void C2S_0x0F2_CarriesStateAndSubMap()
        {
            byte[] packet = SubMapOutboundPackets.BuildSubMapChange(SubMapChangeState.Event, 9);
            Assert.Equal(8, packet.Length);
            ushort header = BinaryPrimitives.ReadUInt16LittleEndian(packet);
            Assert.Equal(0x0F2, header & 0x1FF);
            Assert.Equal(2, header >> 9);
            Assert.Equal(2, BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(4)));
            Assert.Equal(9, BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(6)));
        }

        [Fact]
        public async Task Parser_TracksTheSubMapFromRequestAnswerAndChange()
        {
            var (parser, dispatcher, sent) = MakeParser();
            var subMap = parser.World.SubMap;

            await parser.LifecycleModule.RequestSubMapNumberAsync();
            Assert.True(subMap.RequestPending);
            Assert.Equal(0x0EB, BinaryPrimitives.ReadUInt16LittleEndian(sent[^1]) & 0x1FF);

            Dispatch(dispatcher, S2C_0x10E_ReqSubMapNum.PacketId, U32(5));
            Assert.False(subMap.RequestPending);
            Assert.Equal(5, subMap.SubMapNumber);

            await parser.LifecycleModule.SendSubMapChangeAsync(SubMapChangeState.Event, 2);
            Assert.Equal(2, subMap.SubMapNumber);
            Assert.Equal(0x0F2, BinaryPrimitives.ReadUInt16LittleEndian(sent[^1]) & 0x1FF);
        }

        [Fact]
        public void Parser_StoresPacketControl_AndAcceptsNarakuAndLogoutInfo()
        {
            var (parser, dispatcher, _) = MakeParser();
            Assert.Equal(400u, parser.World.PacketControlCount);
            Dispatch(dispatcher, S2C_0x005_PacketControl.PacketId, U32(600).Concat(new byte[20]).ToArray());
            Assert.Equal(600u, parser.World.PacketControlCount);

            Assert.True(dispatcher.HasHandler(S2C_0x006_Naraku.PacketId));
            Assert.True(dispatcher.HasHandler(S2C_0x10F_ReqLogoutInfo.PacketId));
            Dispatch(dispatcher, S2C_0x006_Naraku.PacketId, Array.Empty<byte>());
            Dispatch(dispatcher, S2C_0x10F_ReqLogoutInfo.PacketId, U32(1));
        }

        #endregion

        #region Fragments (server message)

        private static byte[] Fragment(byte command, FragmentsKind kind, int timestamp, int sizeTotal, int offset, ReadOnlySpan<byte> data, byte value2 = 2)
        {
            var payload = new byte[20 + ((data.Length + 3) & ~3)];
            payload[0] = command;
            payload[1] = 1; // Result
            payload[2] = (byte)kind;
            payload[3] = value2;
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(4), timestamp);
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(8), sizeTotal);
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(12), offset);
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(16), data.Length);
            data.CopyTo(payload.AsSpan(20));
            return payload;
        }

        [Fact]
        public void S2C_0x04D_DecodesTheFixedFieldsAndData()
        {
            byte[] text = Encoding.ASCII.GetBytes("Hello\0");
            var f = new S2C_0x04D_Fragments(Fragment(1, FragmentsKind.ServerMessage, 1000, text.Length, 0, text));
            Assert.True(f.IsValid);
            Assert.Equal(1, f.Command);
            Assert.Equal(1, f.Result);
            Assert.Equal(FragmentsKind.ServerMessage, f.Kind);
            Assert.Equal(2, f.Value2);
            Assert.Equal(1000, f.Timestamp);
            Assert.Equal(6, f.SizeTotal);
            Assert.Equal(0, f.Offset);
            Assert.Equal(6, f.DataSize);
            Assert.Equal(text, f.Data.ToArray());
            Assert.True(f.IsLast);
            Assert.False(new S2C_0x04D_Fragments(new byte[19]).IsValid);
        }

        [Fact]
        public void S2C_0x04D_ClampsDataToThePacket()
        {
            byte[] payload = Fragment(1, FragmentsKind.ServerMessage, 1, 500, 0, new byte[8]);
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(16), 400); // claims more than it holds
            Assert.Equal(8, new S2C_0x04D_Fragments(payload).Data.Length);
        }

        [Fact]
        public void C2S_0x04B_ServerMessageRequest_MatchesLsbSize()
        {
            byte[] first = FragmentsOutboundPackets.BuildServerMessageRequest();
            Assert.Equal(24, first.Length);
            ushort header = BinaryPrimitives.ReadUInt16LittleEndian(first);
            Assert.Equal(0x04B, header & 0x1FF);
            Assert.Equal(6, header >> 9);
            Assert.Equal(1, first[4]); // Command 1: the first fragment
            Assert.Equal(1, first[6]); // value1: server message
            Assert.Equal(2, first[7]); // value2: English
            Assert.Equal(0, BinaryPrimitives.ReadInt32LittleEndian(first.AsSpan(16)));

            byte[] next = FragmentsOutboundPackets.BuildServerMessageRequest(offset: 236, timestamp: 77, sizeTotal: 300);
            Assert.Equal(2, next[4]); // Command 2: a later fragment
            Assert.Equal(77, BinaryPrimitives.ReadInt32LittleEndian(next.AsSpan(8)));
            Assert.Equal(300, BinaryPrimitives.ReadInt32LittleEndian(next.AsSpan(12)));
            Assert.Equal(236, BinaryPrimitives.ReadInt32LittleEndian(next.AsSpan(16)));
        }

        [Fact]
        public async Task ChatModule_RebuildsAFragmentedServerMessage()
        {
            var sent = new List<byte[]>();
            var dispatcher = new PacketDispatcher();
            var module = new ChatPacketModule((data, _) =>
            {
                sent.Add(data.ToArray());
                return Task.CompletedTask;
            });
            module.Register(dispatcher);
            var messages = new List<string>();
            module.ServerMessageReceived += messages.Add;

            await module.RequestServerMessageAsync();
            Assert.Equal(0x04B, BinaryPrimitives.ReadUInt16LittleEndian(sent[^1]) & 0x1FF);

            string text = "Welcome to the server! " + new string('x', 280);
            byte[] bytes = Encoding.ASCII.GetBytes(text + "\0");
            int total = bytes.Length;

            Dispatch(dispatcher, S2C_0x04D_Fragments.PacketId, Fragment(1, FragmentsKind.ServerMessage, 4242, total, 0, bytes.AsSpan(0, 236)));
            Assert.Empty(messages);
            byte[] request = sent[^1];
            Assert.Equal(2, sent.Count);
            Assert.Equal(2, request[4]);
            Assert.Equal(4242, BinaryPrimitives.ReadInt32LittleEndian(request.AsSpan(8)));
            Assert.Equal(236, BinaryPrimitives.ReadInt32LittleEndian(request.AsSpan(16)));

            Dispatch(dispatcher, S2C_0x04D_Fragments.PacketId, Fragment(2, FragmentsKind.ServerMessage, 4242, total, 236, bytes.AsSpan(236)));
            Assert.Equal(text, Assert.Single(messages));
            Assert.Equal(2, sent.Count); // no further request
        }

        [Fact]
        public void ChatModule_IgnoresEmptyMessages_AndRankingFragments()
        {
            var dispatcher = new PacketDispatcher();
            var module = new ChatPacketModule((_, _) => Task.CompletedTask);
            module.Register(dispatcher);
            var messages = new List<string>();
            module.ServerMessageReceived += messages.Add;

            // LandSandBoat with no server message: sizes 0 in a full-size packet.
            Dispatch(dispatcher, S2C_0x04D_Fragments.PacketId, Fragment(1, FragmentsKind.ServerMessage, 1, 0, 0, new byte[236]).Take(256).ToArray());
            Dispatch(dispatcher, S2C_0x04D_Fragments.PacketId, Fragment(0x0D, FragmentsKind.Ranking, 1, 216, 0, new byte[40], value2: 4));
            Assert.Empty(messages);
        }

        [Fact]
        public void Assembler_DropsStaleFragments()
        {
            var assembler = new ServerMessageAssembler();
            byte[] part = new byte[236];
            Assert.Equal(ServerMessageFragmentResult.NeedMore,
                assembler.Add(new S2C_0x04D_Fragments(Fragment(1, FragmentsKind.ServerMessage, 1, 300, 0, part)), out _));
            Assert.Equal(ServerMessageFragmentResult.Ignored,
                assembler.Add(new S2C_0x04D_Fragments(Fragment(2, FragmentsKind.ServerMessage, 2, 300, 236, new byte[64])), out _));
            Assert.Equal(236, assembler.NextOffset);
        }

        [Fact]
        public void SplitServerMessage_SplitsLinesAndDropsTrailingBlanks()
        {
            Assert.Equal(new[] { "Line one", "Line two", "", "Line four" }, StockUiChat.SplitServerMessage("Line one\r\nLine two\n\nLine four\n\n"));
            Assert.Empty(StockUiChat.SplitServerMessage(string.Empty));
        }

        #endregion

        #region Lockstyle error

        [Fact]
        public void S2C_0x11C_DecodesTheFailedItems()
        {
            var payload = new byte[4 + 32];
            payload[0] = 2;
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(4), 12345);
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(6), 16384);
            var p = new S2C_0x11C_LockstyleError(payload);
            Assert.True(p.IsValid);
            Assert.Equal(2, p.ItemCount);
            Assert.Equal(12345, p.GetItemId(0));
            Assert.Equal(16384, p.GetItemId(1));
            Assert.Equal(0, p.GetItemId(2));
        }

        [Fact]
        public void S2C_0x11C_ClampsTheCountToThePacket()
        {
            var payload = new byte[4 + 4];
            payload[0] = 16;
            Assert.Equal(2, new S2C_0x11C_LockstyleError(payload).ItemCount);
            Assert.False(new S2C_0x11C_LockstyleError(new byte[3]).IsValid);
        }

        [Fact]
        public void InventoryModule_RaisesLockstyleFailed()
        {
            var (parser, dispatcher, _) = MakeParser();
            ushort[]? failed = null;
            parser.InventoryModule.LockstyleFailed += items => failed = items;
            var payload = new byte[36];
            payload[0] = 1;
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(4), 12552);
            Dispatch(dispatcher, S2C_0x11C_LockstyleError.PacketId, payload);
            Assert.Equal(new ushort[] { 12552 }, failed);
        }

        [Fact]
        public void LockstyleLog_NamesEachItem()
        {
            var lines = LockstyleLog.FormatErrors(new ushort[] { 12552, 0 },
                id => new ItemRecord { ItemId = id, Name = "Chocobo Shirt", LogName = "chocobo shirt" }).ToList();
            Assert.Equal("Unable to use chocobo shirt for style lock.", Assert.Single(lines));
        }

        #endregion
    }
}
