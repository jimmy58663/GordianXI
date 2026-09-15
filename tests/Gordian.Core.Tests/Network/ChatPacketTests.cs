// tests/Gordian.Core.Tests/Network/ChatPacketTests.cs
using System;
using System.Buffers.Binary;
using System.Text;
using System.Threading.Tasks;
using Gordian.Core.Network;
using Gordian.Core.Network.Packets;
using Xunit;

namespace Gordian.Core.Tests.Network
{
    public class ChatPacketTests
    {
        [Fact]
        public void S2C_0x017_ChatStd_DecodesSayMessage()
        {
            byte[] payload = new byte[40];
            payload[0] = (byte)ChatMessageType.Say;
            payload[1] = 0x00; // Normal player (not GM)
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(2, 2), 240); // Zone 240 (Port Jeuno)

            byte[] nameBytes = Encoding.ASCII.GetBytes("PlayerOne");
            nameBytes.CopyTo(payload.AsSpan(4));

            byte[] msgBytes = Encoding.ASCII.GetBytes("Hello Vana'diel!");
            msgBytes.CopyTo(payload.AsSpan(19));

            var chat = new S2C_0x017_ChatStd(payload);

            Assert.True(chat.IsValid);
            Assert.Equal(ChatMessageType.Say, chat.Kind);
            Assert.False(chat.IsGm);
            Assert.Equal(240, chat.ZoneId);
            Assert.Equal("PlayerOne", chat.GetSenderName());
            Assert.Equal("Hello Vana'diel!", chat.GetMessage());
            Assert.False(chat.HasAutoTranslate());
        }

        [Fact]
        public void S2C_0x017_ChatStd_DecodesGmShoutMessage()
        {
            byte[] payload = new byte[64];
            payload[0] = (byte)ChatMessageType.Shout;
            payload[1] = 0x01; // GM flag set
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(2, 2), 100);

            byte[] nameBytes = Encoding.ASCII.GetBytes("GM_Guide");
            nameBytes.CopyTo(payload.AsSpan(4));

            byte[] msgBytes = Encoding.ASCII.GetBytes("Server maintenance in 15 minutes.");
            msgBytes.CopyTo(payload.AsSpan(19));

            var chat = new S2C_0x017_ChatStd(payload);

            Assert.True(chat.IsValid);
            Assert.Equal(ChatMessageType.Shout, chat.Kind);
            Assert.True(chat.IsGm);
            Assert.Equal(100, chat.ZoneId);
            Assert.Equal("GM_Guide", chat.GetSenderName());
            Assert.Equal("Server maintenance in 15 minutes.", chat.GetMessage());
        }

        [Fact]
        public void S2C_0x017_ChatStd_DecodesAssistMessageRanks()
        {
            byte[] payload = new byte[35];
            payload[0] = (byte)ChatMessageType.NaAssist;
            payload[1] = 0x00;
            // Data field holds: low byte = mastery rank (15), high byte = mentor rank (2)
            ushort assistData = (ushort)(15 | (2 << 8));
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(2, 2), assistData);

            byte[] nameBytes = Encoding.ASCII.GetBytes("MentorSam");
            nameBytes.CopyTo(payload.AsSpan(4));

            byte[] msgBytes = Encoding.ASCII.GetBytes("Need help?");
            msgBytes.CopyTo(payload.AsSpan(19));

            var chat = new S2C_0x017_ChatStd(payload);

            Assert.True(chat.IsValid);
            Assert.Equal(ChatMessageType.NaAssist, chat.Kind);
            Assert.Equal(15, chat.MasteryRank);
            Assert.Equal(2, chat.MentorRank);
            Assert.Equal("MentorSam", chat.GetSenderName());
            Assert.Equal("Need help?", chat.GetMessage());
        }

        [Fact]
        public void S2C_0x017_ChatStd_DetectsAutoTranslateMarker()
        {
            byte[] payload = new byte[30];
            payload[0] = (byte)ChatMessageType.Party;
            payload[1] = 0x00;
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(2, 2), 101);

            Encoding.ASCII.GetBytes("Member").CopyTo(payload.AsSpan(4));

            // Auto-translate string: 0xFD 0x02 0x02 0x01 0x05 0xFD
            byte[] autoTransBytes = new byte[] { 0xFD, 0x02, 0x02, 0x01, 0x05, 0xFD, 0x00 };
            autoTransBytes.CopyTo(payload.AsSpan(19));

            var chat = new S2C_0x017_ChatStd(payload);

            Assert.True(chat.IsValid);
            Assert.True(chat.HasAutoTranslate());
        }

        [Fact]
        public void S2C_0x017_ChatStd_HandlesInvalidShortPayload()
        {
            byte[] shortPayload = new byte[10];
            var chat = new S2C_0x017_ChatStd(shortPayload);

            Assert.False(chat.IsValid);
            Assert.Equal(string.Empty, chat.GetSenderName());
            Assert.Equal(string.Empty, chat.GetMessage());
            Assert.False(chat.HasAutoTranslate());
        }

        [Fact]
        public void S2C_0x009_SysMessage_DecodesValidPayload()
        {
            byte[] payload = new byte[40];
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(0, 4), 0x01020304);
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(4, 2), 42);
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(6, 2), 501);
            payload[8] = 0x10;

            byte[] paramBytes = Encoding.ASCII.GetBytes("Para0 100 Para1 200");
            paramBytes.CopyTo(payload.AsSpan(9));

            var sysMsg = new S2C_0x009_SysMessage(payload);

            Assert.True(sysMsg.IsValid);
            Assert.Equal(0x01020304u, sysMsg.UniqueNo);
            Assert.Equal(42, sysMsg.ActorIndex);
            Assert.Equal(501, sysMsg.MessageId);
            Assert.Equal(0x10, sysMsg.Attr);
            Assert.Equal("Para0 100 Para1 200", sysMsg.GetData());
        }

        [Fact]
        public void S2C_0x009_SysMessage_HandlesShortPayload()
        {
            byte[] shortPayload = new byte[5];
            var sysMsg = new S2C_0x009_SysMessage(shortPayload);

            Assert.False(sysMsg.IsValid);
            Assert.Equal(string.Empty, sysMsg.GetData());
        }

        [Fact]
        public void S2C_0x047_Translate_DecodesTranslation()
        {
            byte[] payload = new byte[132];
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(0, 2), 777);
            payload[2] = (byte)TranslateLanguage.Japanese;
            payload[3] = (byte)TranslateLanguage.English;

            Encoding.ASCII.GetBytes("Arigatou").CopyTo(payload.AsSpan(4));
            Encoding.ASCII.GetBytes("Thank you").CopyTo(payload.AsSpan(68));

            var tr = new S2C_0x047_Translate(payload);

            Assert.True(tr.IsValid);
            Assert.Equal(777, tr.ItemNo);
            Assert.Equal(TranslateLanguage.Japanese, tr.FromIndex);
            Assert.Equal(TranslateLanguage.English, tr.ToIndex);
            Assert.Equal("Arigatou", tr.GetFromString());
            Assert.Equal("Thank you", tr.GetToString());
        }

        [Fact]
        public void S2C_0x0CC_LinkshellMessage_DecodesMotdAndPermissions()
        {
            byte[] payload = new byte[172];
            // Stat: 3, Attr: 2
            payload[0] = (byte)(3 | (2 << 4));

            // ReadLevel: 0, WriteLevel: 1, PubEditLevel: 2, Slot: LS2 (1)
            // bits 0..1 = 0, bits 2..3 = 1, bits 4..5 = 2, bits 6..7 = 1
            payload[1] = (byte)((0 & 0x03) | ((1 & 0x03) << 2) | ((2 & 0x03) << 4) | ((1 & 0x03) << 6));

            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(2, 2), 99); // SeqId

            Encoding.ASCII.GetBytes("Event tonight at 8 PM EST").CopyTo(payload.AsSpan(4));
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(132, 4), 1700000000); // UpdateTime
            Encoding.ASCII.GetBytes("GuildMaster").CopyTo(payload.AsSpan(136));
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(152, 2), 1); // OpType (Post)
            Encoding.ASCII.GetBytes("Dragonslayer").CopyTo(payload.AsSpan(156));

            var ls = new S2C_0x0CC_LinkshellMessage(payload);

            Assert.True(ls.IsValid);
            Assert.Equal(3, ls.Stat);
            Assert.Equal(2, ls.Attr);
            Assert.Equal(0, ls.ReadLevel);
            Assert.Equal(1, ls.WriteLevel);
            Assert.Equal(2, ls.PubEditLevel);
            Assert.Equal(LinkshellSlot.LS2, ls.Slot);
            Assert.Equal(99, ls.SequenceId);
            Assert.Equal("Event tonight at 8 PM EST", ls.GetMessage());
            Assert.Equal(1700000000u, ls.UpdateTime);
            Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1700000000), ls.UpdateDateTime);
            Assert.Equal("GuildMaster", ls.GetModifier());
            Assert.Equal(1, ls.OpType);
            Assert.Equal("Dragonslayer", ls.GetLinkshellName());
        }

        [Fact]
        public void C2S_0x0B5_ChatStd_BuildsValidWirePacket()
        {
            byte[] packet = ChatOutboundPackets.BuildChatStd(ChatSendKind.Party, "Ready to pull!", 1234);

            Assert.True(packet.Length >= 8);
            Assert.Equal(0, packet.Length % 4);

            ushort headerWord = BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(0, 2));
            ushort packetId = (ushort)(headerWord & 0x1FF);
            int words = headerWord >> 9;
            Assert.Equal(0x0B5, packetId);
            Assert.Equal(packet.Length / 4, words);

            ushort seq = BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(2, 2));
            Assert.Equal(1234, seq);

            Assert.Equal((byte)ChatSendKind.Party, packet[4]);
            Assert.Equal(0x00, packet[5]);

            string msg = Encoding.ASCII.GetString(packet.AsSpan(6, 14));
            Assert.Equal("Ready to pull!", msg);
        }

        [Fact]
        public void C2S_0x0B6_ChatTell_BuildsValidWirePacket()
        {
            byte[] packet = ChatOutboundPackets.BuildChatTell("Recipient", "Private whisper", 5678);

            Assert.True(packet.Length >= 24);
            Assert.Equal(0, packet.Length % 4);

            ushort headerWord = BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(0, 2));
            ushort packetId = (ushort)(headerWord & 0x1FF);
            Assert.Equal(0x0B6, packetId);

            ushort seq = BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(2, 2));
            Assert.Equal(5678, seq);

            Assert.Equal(0x03, packet[4]); // Mandatory 0x03 marker
            Assert.Equal(0x00, packet[5]);

            string name = Encoding.ASCII.GetString(packet.AsSpan(6, 9));
            Assert.Equal("Recipient", name);

            string msg = Encoding.ASCII.GetString(packet.AsSpan(21, 15));
            Assert.Equal("Private whisper", msg);
        }

        [Fact]
        public void C2S_0x0B7_AssistChannel_BuildsValidWirePacket()
        {
            byte[] packet = ChatOutboundPackets.BuildAssistChannel(AssistActionKind.GiveThumbsUp, "HelpfulPlayer", 42);

            Assert.Equal(24, packet.Length);

            ushort headerWord = BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(0, 2));
            Assert.Equal(0x0B7, headerWord & 0x1FF);
            Assert.Equal(6, headerWord >> 9); // 24 / 4 = 6 words

            Assert.Equal(42, BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(2, 2)));
            Assert.Equal((byte)AssistActionKind.GiveThumbsUp, packet[4]);
            Assert.Equal(0x00, packet[5]);

            string name = Encoding.ASCII.GetString(packet.AsSpan(6, 13));
            Assert.Equal("HelpfulPlayer", name);

            Assert.Equal((byte)' ', packet[21]); // Single space per protocol
        }

        [Fact]
        public void C2S_0x02B_TranslateRequest_BuildsValidWirePacket()
        {
            byte[] packet = ChatOutboundPackets.BuildTranslateRequest(
                TranslateLanguage.English,
                TranslateLanguage.Japanese,
                "Good evening",
                77);

            Assert.True(packet.Length >= 12);
            Assert.Equal(0, packet.Length % 4);

            ushort headerWord = BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(0, 2));
            Assert.Equal(0x02B, headerWord & 0x1FF);
            Assert.Equal(packet.Length / 4, headerWord >> 9);

            Assert.Equal(77, BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(2, 2)));
            Assert.Equal((byte)TranslateLanguage.English, packet[4]);
            Assert.Equal((byte)TranslateLanguage.Japanese, packet[5]);

            string term = Encoding.ASCII.GetString(packet.AsSpan(8, 12));
            Assert.Equal("Good evening", term);
        }

        [Fact]
        public void C2S_0x0E0_SetUserMsg_BuildsValidWirePacket()
        {
            byte[] packet = ChatOutboundPackets.BuildSetUserMsg(
                "Need EXP party, Level 75 WAR/NIN",
                SearchMessageType.EXPPartySeekParty,
                installTime: 123456,
                srvExCode: 1,
                cliExCode: 2,
                sequenceId: 101);

            Assert.Equal(152, packet.Length);

            ushort headerWord = BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(0, 2));
            Assert.Equal(0x0E0, headerWord & 0x1FF);
            Assert.Equal(38, headerWord >> 9); // 152 / 4 = 38 words

            Assert.Equal(101, BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(2, 2)));

            string msg = Encoding.ASCII.GetString(packet.AsSpan(4, 32));
            Assert.Equal("Need EXP party, Level 75 WAR/NIN", msg);

            Assert.Equal(123456u, BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(132, 4)));
            Assert.Equal((byte)'W', packet[136]);
            Assert.Equal((byte)'I', packet[137]);
            Assert.Equal((byte)'N', packet[138]);
            Assert.Equal((uint)SearchMessageType.EXPPartySeekParty, BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(148, 4)));
        }

        [Fact]
        public void C2S_0x0E1_GetLsMsg_BuildsValidWirePacket()
        {
            byte[] packet = ChatOutboundPackets.BuildGetLsMsg(LinkshellSlot.LS2, 202);

            Assert.Equal(148, packet.Length);

            ushort headerWord = BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(0, 2));
            Assert.Equal(0x0E1, headerWord & 0x1FF);
            Assert.Equal(37, headerWord >> 9); // 148 / 4 = 37 words

            Assert.Equal(202, BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(2, 2)));
            Assert.Equal((byte)(1 << 6), packet[5]); // LS2 in bits 6..7
        }

        [Fact]
        public void C2S_0x0E2_SetLsMsg_BuildsValidWirePacket()
        {
            byte[] packet = ChatOutboundPackets.BuildSetLsMsg(
                LinkshellSlot.LS1,
                "New linkshell rule: be kind",
                LinkshellWriteLevel.Pearlsack,
                303);

            Assert.Equal(148, packet.Length);

            ushort headerWord = BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(0, 2));
            Assert.Equal(0x0E2, headerWord & 0x1FF);
            Assert.Equal(37, headerWord >> 9);

            Assert.Equal(303, BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(2, 2)));
            // LS1 (0 << 6) | Pearlsack (1 << 2) = 0x04
            Assert.Equal(0x04, packet[5]);

            string msg = Encoding.ASCII.GetString(packet.AsSpan(16, 27));
            Assert.Equal("New linkshell rule: be kind", msg);
        }

        [Fact]
        public void C2S_0x0E4_GetLsPriv_BuildsValidWirePacket()
        {
            byte[] packet = ChatOutboundPackets.BuildGetLsPriv(LinkshellSlot.LS1, 404);

            Assert.Equal(148, packet.Length);

            ushort headerWord = BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(0, 2));
            Assert.Equal(0x0E4, headerWord & 0x1FF);
            Assert.Equal(37, headerWord >> 9);

            Assert.Equal(404, BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(2, 2)));
            Assert.Equal(0x00, packet[5]); // LS1 in bits 6..7
        }

        [Fact]
        public void ChatPacketModule_RegistersAndDispatchesInboundEvents()
        {
            var dispatcher = new PacketDispatcher();
            var module = new ChatPacketModule((data, reliable) => Task.CompletedTask);
            module.Register(dispatcher);

            ChatMessage? receivedChat = null;
            SystemMessage? receivedSys = null;
            TranslateMessage? receivedTrans = null;
            LinkshellMessage? receivedLs = null;

            module.ChatMessageReceived += (msg) => receivedChat = msg;
            module.SystemMessageReceived += (msg) => receivedSys = msg;
            module.TranslateReceived += (msg) => receivedTrans = msg;
            module.LinkshellMessageReceived += (msg) => receivedLs = msg;

            // 1. Dispatch 0x017
            byte[] chatPayload = new byte[35];
            chatPayload[0] = (byte)ChatMessageType.Tell;
            chatPayload[1] = 0;
            BinaryPrimitives.WriteUInt16LittleEndian(chatPayload.AsSpan(2, 2), 241);
            Encoding.ASCII.GetBytes("Sender").CopyTo(chatPayload.AsSpan(4));
            Encoding.ASCII.GetBytes("Testing!").CopyTo(chatPayload.AsSpan(19));

            dispatcher.Dispatch(new PacketHeader(0x017, chatPayload.Length + 4, 1), chatPayload);

            Assert.NotNull(receivedChat);
            Assert.Equal(ChatMessageType.Tell, receivedChat.Type);
            Assert.Equal("Sender", receivedChat.Sender);
            Assert.Equal("Testing!", receivedChat.Message);
            Assert.Equal(241, receivedChat.ZoneId);

            // 2. Dispatch 0x009
            byte[] sysPayload = new byte[25];
            BinaryPrimitives.WriteUInt32LittleEndian(sysPayload.AsSpan(0, 4), 9999);
            BinaryPrimitives.WriteUInt16LittleEndian(sysPayload.AsSpan(4, 2), 11);
            BinaryPrimitives.WriteUInt16LittleEndian(sysPayload.AsSpan(6, 2), 404);
            sysPayload[8] = 1;
            Encoding.ASCII.GetBytes("StringData").CopyTo(sysPayload.AsSpan(9));

            dispatcher.Dispatch(new PacketHeader(0x009, sysPayload.Length + 4, 2), sysPayload);

            Assert.NotNull(receivedSys);
            Assert.Equal(9999u, receivedSys.UniqueNo);
            Assert.Equal(11, receivedSys.ActorIndex);
            Assert.Equal(404, receivedSys.MessageId);
            Assert.Equal("StringData", receivedSys.Parameters);

            // 3. Dispatch 0x047
            byte[] trPayload = new byte[132];
            BinaryPrimitives.WriteUInt16LittleEndian(trPayload.AsSpan(0, 2), 55);
            trPayload[2] = (byte)TranslateLanguage.Japanese;
            trPayload[3] = (byte)TranslateLanguage.English;
            Encoding.ASCII.GetBytes("Hai").CopyTo(trPayload.AsSpan(4));
            Encoding.ASCII.GetBytes("Yes").CopyTo(trPayload.AsSpan(68));

            dispatcher.Dispatch(new PacketHeader(0x047, trPayload.Length + 4, 3), trPayload);

            Assert.NotNull(receivedTrans);
            Assert.Equal(55, receivedTrans.ItemNo);
            Assert.Equal("Hai", receivedTrans.SourcePhrase);
            Assert.Equal("Yes", receivedTrans.TranslatedPhrase);

            // 4. Dispatch 0x0CC
            byte[] lsPayload = new byte[172];
            lsPayload[0] = 0x11;
            lsPayload[1] = 0; // Slot LS1
            BinaryPrimitives.WriteUInt16LittleEndian(lsPayload.AsSpan(2, 2), 10);
            Encoding.ASCII.GetBytes("Welcome!").CopyTo(lsPayload.AsSpan(4));
            BinaryPrimitives.WriteUInt32LittleEndian(lsPayload.AsSpan(132, 4), 1600000000);
            Encoding.ASCII.GetBytes("Admin").CopyTo(lsPayload.AsSpan(136));
            Encoding.ASCII.GetBytes("LSGroup").CopyTo(lsPayload.AsSpan(156));

            dispatcher.Dispatch(new PacketHeader(0x0CC, lsPayload.Length + 4, 4), lsPayload);

            Assert.NotNull(receivedLs);
            Assert.Equal(LinkshellSlot.LS1, receivedLs.Slot);
            Assert.Equal("Welcome!", receivedLs.Message);
            Assert.Equal("Admin", receivedLs.Modifier);
            Assert.Equal("LSGroup", receivedLs.LinkshellName);
        }

        [Fact]
        public async Task ChatPacketModule_TransmitsOutboundPacketsCorrectly()
        {
            ReadOnlyMemory<byte> lastSent = default;
            int sendCount = 0;

            var module = new ChatPacketModule((data, reliable) =>
            {
                lastSent = data;
                sendCount++;
                return Task.CompletedTask;
            });

            // 1. SendChatAsync
            await module.SendChatAsync(ChatSendKind.Shout, "Looking for tank!");
            Assert.Equal(1, sendCount);
            Assert.Equal(0x0B5, BinaryPrimitives.ReadUInt16LittleEndian(lastSent.Span.Slice(0, 2)) & 0x1FF);

            // 2. SendTellAsync
            await module.SendTellAsync("TargetGuy", "Can you tank?");
            Assert.Equal(2, sendCount);
            Assert.Equal(0x0B6, BinaryPrimitives.ReadUInt16LittleEndian(lastSent.Span.Slice(0, 2)) & 0x1FF);

            // 3. SendAssistActionAsync
            await module.SendAssistActionAsync(AssistActionKind.IssueWarning, "BadActor");
            Assert.Equal(3, sendCount);
            Assert.Equal(0x0B7, BinaryPrimitives.ReadUInt16LittleEndian(lastSent.Span.Slice(0, 2)) & 0x1FF);

            // 4. RequestTranslateAsync
            await module.RequestTranslateAsync(TranslateLanguage.English, TranslateLanguage.Japanese, "Help");
            Assert.Equal(4, sendCount);
            Assert.Equal(0x02B, BinaryPrimitives.ReadUInt16LittleEndian(lastSent.Span.Slice(0, 2)) & 0x1FF);

            // 5. SetSearchMessageAsync
            await module.SetSearchMessageAsync("LFG", SearchMessageType.EXPPartySeekParty);
            Assert.Equal(5, sendCount);
            Assert.Equal(0x0E0, BinaryPrimitives.ReadUInt16LittleEndian(lastSent.Span.Slice(0, 2)) & 0x1FF);

            // 6. RequestLinkshellMessageAsync
            await module.RequestLinkshellMessageAsync(LinkshellSlot.LS2);
            Assert.Equal(6, sendCount);
            Assert.Equal(0x0E1, BinaryPrimitives.ReadUInt16LittleEndian(lastSent.Span.Slice(0, 2)) & 0x1FF);

            // 7. SetLinkshellMessageAsync
            await module.SetLinkshellMessageAsync(LinkshellSlot.LS1, "Updated MOTD");
            Assert.Equal(7, sendCount);
            Assert.Equal(0x0E2, BinaryPrimitives.ReadUInt16LittleEndian(lastSent.Span.Slice(0, 2)) & 0x1FF);

            // 8. RequestLinkshellPrivilegesAsync
            await module.RequestLinkshellPrivilegesAsync(LinkshellSlot.LS1);
            Assert.Equal(8, sendCount);
            Assert.Equal(0x0E4, BinaryPrimitives.ReadUInt16LittleEndian(lastSent.Span.Slice(0, 2)) & 0x1FF);
        }

        [Fact]
        public void PacketParser_ExposesChatModuleAndDispatchesChatEvents()
        {
            var profile = new Gordian.Core.Config.SessionProfile();
            var parser = new PacketParser(profile, (data, prio) => Task.CompletedTask);

            Assert.NotNull(parser.ChatModule);

            ChatMessage? receivedChat = null;
            parser.ChatModule.ChatMessageReceived += (msg) => receivedChat = msg;

            // Direct-dispatch a 0x017 packet into parser's dispatcher
            byte[] chatPayload = new byte[35];
            chatPayload[0] = (byte)ChatMessageType.Linkshell;
            chatPayload[1] = 0;
            BinaryPrimitives.WriteUInt16LittleEndian(chatPayload.AsSpan(2, 2), 245);
            Encoding.ASCII.GetBytes("Linkmate").CopyTo(chatPayload.AsSpan(4));
            Encoding.ASCII.GetBytes("Good morning!").CopyTo(chatPayload.AsSpan(19));

            parser.Dispatcher.Dispatch(new PacketHeader(0x017, chatPayload.Length + 4, 1), chatPayload);

            Assert.NotNull(receivedChat);
            Assert.Equal(ChatMessageType.Linkshell, receivedChat.Type);
            Assert.Equal("Linkmate", receivedChat.Sender);
            Assert.Equal("Good morning!", receivedChat.Message);
            Assert.Equal(245, receivedChat.ZoneId);
        }
    }
}
