// tests/Gordian.App.Tests/ViewModels/ChatViewModelTests.cs
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using Gordian.App.ViewModels;
using Gordian.Core.Network;
using Gordian.Core.Network.Packets;
using Gordian.Core.World;
using Xunit;

namespace Gordian.App.Tests.ViewModels
{
    public sealed class ChatViewModelTests : IDisposable
    {
        private readonly SessionRegistry _registry;
        private readonly ChatViewModel _vm;

        public ChatViewModelTests()
        {
            ChatViewModel.UiDispatcher = a => a();
            _registry = new SessionRegistry();
            _vm = new ChatViewModel(_registry);
        }

        public void Dispose()
        {
            _vm.Dispose();
            ChatViewModel.UiDispatcher = null;
        }

        [Fact]
        public void InitialState_WithoutSession_HasSafeDefaults()
        {
            Assert.False(_vm.HasActiveSession);
            Assert.Null(_vm.SelectedSession);
            Assert.Equal("Say", _vm.SelectedChannel);
            Assert.False(_vm.IsTellChannel);
            Assert.Empty(_vm.AllMessages);
            Assert.Empty(_vm.FilteredMessages);
            Assert.False(_vm.CanSendMessage());
        }

        [Fact]
        public void RegisterSession_AutoSelectsSession()
        {
            var netManager = new SessionNetworkManager("127.0.0.1", 54230);
            var session = new CharacterSession("Cybin", 12345, "user1", netManager);

            _registry.RegisterSession(session);

            Assert.True(_vm.HasActiveSession);
            Assert.Same(session, _vm.SelectedSession);
            Assert.Contains("Cybin", _vm.SessionHeaderTitle);
        }

        [Fact]
        public void InboundChatMessage_AddsToLogAndFilters()
        {
            var sentPackets = new List<byte[]>();
            var netManager = new SessionNetworkManager("127.0.0.1", 54230);
            var session = new CharacterSession("Cybin", 12345, "user1", netManager);
            _registry.RegisterSession(session);

            // Directly invoke inbound chat packet on session's dispatcher
            byte[] chatPayload = new byte[40];
            chatPayload[0] = (byte)ChatMessageType.Say;
            chatPayload[1] = 0; // Not GM
            BinaryPrimitives.WriteUInt16LittleEndian(chatPayload.AsSpan(2, 2), 240);
            Encoding.ASCII.GetBytes("Adventurer").CopyTo(chatPayload.AsSpan(4));
            Encoding.ASCII.GetBytes("Greetings!").CopyTo(chatPayload.AsSpan(19));

            session.NetworkManager.Parser.Dispatcher.Dispatch(
                new PacketHeader(0x017, chatPayload.Length + 4, 1),
                chatPayload
            );

            Assert.Single(_vm.AllMessages);
            Assert.Single(_vm.FilteredMessages);

            var item = _vm.FilteredMessages[0];
            Assert.Equal("[Say]", item.BadgeText);
            Assert.Equal("Adventurer", item.Sender);
            Assert.Equal("Greetings!", item.Message);
        }

        [Fact]
        public void InboundSystemMessage_AddsToLog()
        {
            var netManager = new SessionNetworkManager("127.0.0.1", 54230);
            var session = new CharacterSession("Cybin", 12345, "user1", netManager);
            _registry.RegisterSession(session);

            byte[] sysPayload = new byte[30];
            BinaryPrimitives.WriteUInt32LittleEndian(sysPayload.AsSpan(0, 4), 100);
            BinaryPrimitives.WriteUInt16LittleEndian(sysPayload.AsSpan(4, 2), 5);
            BinaryPrimitives.WriteUInt16LittleEndian(sysPayload.AsSpan(6, 2), 12);
            sysPayload[8] = 0;
            Encoding.ASCII.GetBytes("Para0 50").CopyTo(sysPayload.AsSpan(9));

            session.NetworkManager.Parser.Dispatcher.Dispatch(
                new PacketHeader(0x009, sysPayload.Length + 4, 2),
                sysPayload
            );

            Assert.Single(_vm.AllMessages);
            var item = _vm.AllMessages[0];
            Assert.Equal("[System]", item.BadgeText);
            Assert.Contains("That person is a party member", item.Message);
        }

        [Fact]
        public void ChannelFilter_FiltersMessagesProperly()
        {
            var netManager = new SessionNetworkManager("127.0.0.1", 54230);
            var session = new CharacterSession("Cybin", 12345, "user1", netManager);
            _registry.RegisterSession(session);

            // Add Say
            _vm.AddMessageItem(new ChatItemViewModel("[Say]", "#FFF", "Alice", "#FFF", "Hello", "#FFF"));
            // Add Tell
            _vm.AddMessageItem(new ChatItemViewModel("[Tell]", "#FFF", "Bob", "#FFF", "Secret", "#FFF"));
            // Add Party
            _vm.AddMessageItem(new ChatItemViewModel("[Party]", "#FFF", "Charlie", "#FFF", "Ready", "#FFF"));

            Assert.Equal(3, _vm.AllMessages.Count);
            Assert.Equal(3, _vm.FilteredMessages.Count);

            // Filter to Tell
            _vm.ChannelFilter = "Tell";
            Assert.Single(_vm.FilteredMessages);
            Assert.Equal("Bob", _vm.FilteredMessages[0].Sender);

            // Filter to Party
            _vm.ChannelFilter = "Party";
            Assert.Single(_vm.FilteredMessages);
            Assert.Equal("Charlie", _vm.FilteredMessages[0].Sender);

            // Reset to All
            _vm.ChannelFilter = "All";
            Assert.Equal(3, _vm.FilteredMessages.Count);
        }

        [Fact]
        public async Task ExecuteSendMessageAsync_SayChannel_TransmitsChatStd()
        {
            var sentChunks = new List<byte[]>();
            var netManager = new SessionNetworkManager("127.0.0.1", 54230)
            {
                OutboundChunkOverride = (data, prio) =>
                {
                    sentChunks.Add(data.ToArray());
                    return Task.CompletedTask;
                }
            };

            var session = new CharacterSession("Cybin", 12345, "user1", netManager);
            _registry.RegisterSession(session);

            _vm.SelectedChannel = "Say";
            _vm.OutgoingMessage = "Testing say message";

            Assert.True(_vm.CanSendMessage());
            await _vm.ExecuteSendMessageAsync();

            Assert.Single(sentChunks);
            ushort packetId = (ushort)(BinaryPrimitives.ReadUInt16LittleEndian(sentChunks[0].AsSpan(0, 2)) & 0x1FF);
            Assert.Equal(0x0B5, packetId);

            Assert.Empty(_vm.OutgoingMessage);
            Assert.Single(_vm.AllMessages);
            Assert.Equal("[Say]", _vm.AllMessages[0].BadgeText);
            Assert.Equal("Testing say message", _vm.AllMessages[0].Message);
        }

        [Fact]
        public async Task ExecuteSendMessageAsync_TellChannel_TransmitsTell()
        {
            var sentChunks = new List<byte[]>();
            var netManager = new SessionNetworkManager("127.0.0.1", 54230)
            {
                OutboundChunkOverride = (data, prio) =>
                {
                    sentChunks.Add(data.ToArray());
                    return Task.CompletedTask;
                }
            };

            var session = new CharacterSession("Cybin", 12345, "user1", netManager);
            _registry.RegisterSession(session);

            _vm.SelectedChannel = "Tell";
            Assert.True(_vm.IsTellChannel);

            _vm.TellRecipient = "Friend";
            _vm.OutgoingMessage = "Let's party!";

            Assert.True(_vm.CanSendMessage());
            await _vm.ExecuteSendMessageAsync();

            Assert.Single(sentChunks);
            ushort packetId = (ushort)(BinaryPrimitives.ReadUInt16LittleEndian(sentChunks[0].AsSpan(0, 2)) & 0x1FF);
            Assert.Equal(0x0B6, packetId);

            Assert.Empty(_vm.OutgoingMessage);
            Assert.Single(_vm.AllMessages);
            Assert.Equal("[Tell]", _vm.AllMessages[0].BadgeText);
            Assert.Equal("Let's party!", _vm.AllMessages[0].Message);
        }

        [Fact]
        public void ClearLog_ClearsAllMessages()
        {
            _vm.AddMessageItem(new ChatItemViewModel("[Say]", "#FFF", "Alice", "#FFF", "Hello", "#FFF"));
            _vm.AddMessageItem(new ChatItemViewModel("[Say]", "#FFF", "Bob", "#FFF", "Hi", "#FFF"));

            Assert.Equal(2, _vm.AllMessages.Count);
            _vm.ExecuteClearLog();

            Assert.Empty(_vm.AllMessages);
            Assert.Empty(_vm.FilteredMessages);
            Assert.Equal("Chat log cleared.", _vm.StatusText);
        }

        [Fact]
        public void InboundPartyInvite_AddsPartyNotification()
        {
            var netManager = new SessionNetworkManager("127.0.0.1", 54230);
            var session = new CharacterSession("Cybin", 12345, "user1", netManager);
            _registry.RegisterSession(session);

            byte[] invitePayload = new byte[32];
            BinaryPrimitives.WriteUInt32LittleEndian(invitePayload.AsSpan(0, 4), 9999);
            BinaryPrimitives.WriteUInt16LittleEndian(invitePayload.AsSpan(4, 2), 44);
            invitePayload[7] = (byte)PartyKind.Party;
            Encoding.ASCII.GetBytes("Tarudrake").CopyTo(invitePayload.AsSpan(8));

            session.NetworkManager.Parser.Dispatcher.Dispatch(
                new PacketHeader(0x0DC, invitePayload.Length + 4, 1),
                invitePayload
            );

            Assert.Single(_vm.AllMessages);
            var item = _vm.AllMessages[0];
            Assert.Equal("[Party]", item.BadgeText);
            Assert.Contains("Tarudrake invited you to join a party", item.Message);
            Assert.Contains("/join", item.Message);
        }

        [Fact]
        public async Task SlashCommand_Join_AcceptsPartyInvite()
        {
            var sentChunks = new List<byte[]>();
            var netManager = new SessionNetworkManager("127.0.0.1", 54230)
            {
                OutboundChunkOverride = (data, _) =>
                {
                    sentChunks.Add(data.ToArray());
                    return Task.CompletedTask;
                }
            };
            var session = new CharacterSession("Cybin", 12345, "user1", netManager);
            _registry.RegisterSession(session);

            // Set pending invite
            session.Party.SetPendingInvite(new PartyInvite(9999, 44, "Tarudrake", PartyKind.Party, DateTime.UtcNow));

            _vm.OutgoingMessage = "/join";
            Assert.True(_vm.CanSendMessage());
            await _vm.ExecuteSendMessageAsync();

            Assert.Single(sentChunks);
            ushort packetId = (ushort)(BinaryPrimitives.ReadUInt16LittleEndian(sentChunks[0].AsSpan(0, 2)) & 0x1FF);
            Assert.Equal(0x074, packetId);
            Assert.Equal(1, sentChunks[0][4]); // Res = 1 (Accept)

            var notice = _vm.AllMessages.Last();
            Assert.Equal("[Party]", notice.BadgeText);
            Assert.Contains("Accepted party invite from Tarudrake", notice.Message);
        }

        [Fact]
        public async Task SlashCommand_PcmdAdd_SendsInviteToTargetInWorld()
        {
            var sentChunks = new List<byte[]>();
            var netManager = new SessionNetworkManager("127.0.0.1", 54230)
            {
                OutboundChunkOverride = (data, _) =>
                {
                    sentChunks.Add(data.ToArray());
                    return Task.CompletedTask;
                }
            };
            var session = new CharacterSession("Cybin", 12345, "user1", netManager);
            _registry.RegisterSession(session);

            // Add Tarudrake to session's world
            session.World.UpsertEntity(new WorldEntity(9999, 44, EntityType.Player)
            {
                Name = "Tarudrake",
                Position = Vector3.Zero
            });

            _vm.OutgoingMessage = "/pcmd add Tarudrake";
            Assert.True(_vm.CanSendMessage());
            await _vm.ExecuteSendMessageAsync();

            Assert.Single(sentChunks);
            ushort packetId = (ushort)(BinaryPrimitives.ReadUInt16LittleEndian(sentChunks[0].AsSpan(0, 2)) & 0x1FF);
            Assert.Equal(0x06E, packetId);
            uint targetId = BinaryPrimitives.ReadUInt32LittleEndian(sentChunks[0].AsSpan(4, 4));
            ushort targetIndex = BinaryPrimitives.ReadUInt16LittleEndian(sentChunks[0].AsSpan(8, 2));
            Assert.Equal(9999u, targetId);
            Assert.Equal(44, targetIndex);

            var notice = _vm.AllMessages.Last();
            Assert.Equal("[Party]", notice.BadgeText);
            Assert.Contains("Invited Tarudrake to join the party", notice.Message);
        }

        [Fact]
        public async Task SlashCommand_Echo_AddsLocalEcho()
        {
            var netManager = new SessionNetworkManager("127.0.0.1", 54230);
            var session = new CharacterSession("Cybin", 12345, "user1", netManager);
            _registry.RegisterSession(session);

            _vm.OutgoingMessage = "/echo Hello local console!";
            await _vm.ExecuteSendMessageAsync();

            Assert.Single(_vm.AllMessages);
            var item = _vm.AllMessages[0];
            Assert.Equal("[Echo]", item.BadgeText);
            Assert.Equal("Hello local console!", item.Message);
        }

        [Fact]
        public async Task ServerCommand_ExclamationPos_SendsSayChatPacket()
        {
            var sentChunks = new List<byte[]>();
            var netManager = new SessionNetworkManager("127.0.0.1", 54230)
            {
                OutboundChunkOverride = (data, _) =>
                {
                    sentChunks.Add(data.ToArray());
                    return Task.CompletedTask;
                }
            };
            var session = new CharacterSession("Cybin", 12345, "user1", netManager);
            _registry.RegisterSession(session);

            _vm.OutgoingMessage = "!pos";
            await _vm.ExecuteSendMessageAsync();

            Assert.Single(sentChunks);
            ushort packetId = (ushort)(BinaryPrimitives.ReadUInt16LittleEndian(sentChunks[0].AsSpan(0, 2)) & 0x1FF);
            Assert.Equal(0x0B5, packetId);
            Assert.Equal((byte)ChatSendKind.Say, sentChunks[0][4]);
        }
    }
}
