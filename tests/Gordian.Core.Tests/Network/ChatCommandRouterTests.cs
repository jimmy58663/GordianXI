// tests/Gordian.Core.Tests/Network/ChatCommandRouterTests.cs
using System;
using System.Numerics;
using Gordian.Core.Network;
using Gordian.Core.Network.Packets;
using Gordian.Core.World;
using Xunit;

namespace Gordian.Core.Tests.Network
{
    public class ChatCommandRouterTests
    {
        private readonly WorldState _world;

        public ChatCommandRouterTests()
        {
            _world = new WorldState();
            _world.UpsertEntity(new WorldEntity(12345, 42, EntityType.Player)
            {
                Name = "Tarudrake",
                Position = Vector3.Zero
            });
        }

        [Theory]
        [InlineData("/join")]
        [InlineData("/accept")]
        [InlineData("/pcmd accept")]
        public void Parse_JoinCommands_ReturnsPartyAccept(string cmd)
        {
            var result = ChatCommandRouter.Parse(cmd, ChatSendKind.Say, _world);
            Assert.Equal(ChatCommandResultKind.PartyAccept, result.Kind);
        }

        [Theory]
        [InlineData("/decline")]
        [InlineData("/pcmd decline")]
        public void Parse_DeclineCommands_ReturnsPartyDecline(string cmd)
        {
            var result = ChatCommandRouter.Parse(cmd, ChatSendKind.Say, _world);
            Assert.Equal(ChatCommandResultKind.PartyDecline, result.Kind);
        }

        [Theory]
        [InlineData("/pcmd add Tarudrake")]
        [InlineData("/pcmd invite Tarudrake")]
        [InlineData("/invite Tarudrake")]
        public void Parse_InviteCommands_ResolvesTargetFromWorldState(string cmd)
        {
            var result = ChatCommandRouter.Parse(cmd, ChatSendKind.Say, _world);
            Assert.Equal(ChatCommandResultKind.PartyInvite, result.Kind);
            Assert.Equal(12345u, result.TargetServerId);
            Assert.Equal(42, result.TargetIndex);
            Assert.Equal("Tarudrake", result.TargetName);
        }

        [Fact]
        public void Parse_InviteCommands_TargetNotInWorld_ReturnsZeroIds()
        {
            var result = ChatCommandRouter.Parse("/pcmd add UnknownPlayer", ChatSendKind.Say, _world);
            Assert.Equal(ChatCommandResultKind.PartyInvite, result.Kind);
            Assert.Equal(0u, result.TargetServerId);
            Assert.Equal(0, result.TargetIndex);
            Assert.Equal("UnknownPlayer", result.TargetName);
        }

        [Theory]
        [InlineData("/leave")]
        [InlineData("/break")]
        [InlineData("/pcmd leave")]
        public void Parse_LeaveCommands_ReturnsPartyLeave(string cmd)
        {
            var result = ChatCommandRouter.Parse(cmd, ChatSendKind.Say, _world);
            Assert.Equal(ChatCommandResultKind.PartyLeave, result.Kind);
        }

        [Theory]
        [InlineData("/disband")]
        [InlineData("/breakup")]
        [InlineData("/pcmd disband")]
        [InlineData("/pcmd breakup")]
        public void Parse_DisbandCommands_ReturnsPartyDisband(string cmd)
        {
            var result = ChatCommandRouter.Parse(cmd, ChatSendKind.Say, _world);
            Assert.Equal(ChatCommandResultKind.PartyDisband, result.Kind);
        }

        [Theory]
        [InlineData("/tell Tarudrake Hello there!", "Tarudrake", "Hello there!")]
        [InlineData("/t Tarudrake Hello there!", "Tarudrake", "Hello there!")]
        [InlineData("/w Tarudrake Hello there!", "Tarudrake", "Hello there!")]
        public void Parse_TellCommands_ExtractsRecipientAndMessage(string cmd, string expectedRecipient, string expectedMsg)
        {
            var result = ChatCommandRouter.Parse(cmd, ChatSendKind.Say, _world);
            Assert.Equal(ChatCommandResultKind.SendTell, result.Kind);
            Assert.Equal(expectedRecipient, result.Recipient);
            Assert.Equal(expectedMsg, result.Message);
        }

        [Fact]
        public void Parse_PartyChatCommand_RoutesToPartyChannel()
        {
            var result = ChatCommandRouter.Parse("/p Ready for pull!", ChatSendKind.Say, _world);
            Assert.Equal(ChatCommandResultKind.SendChat, result.Kind);
            Assert.Equal(ChatSendKind.Party, result.SpeechKind);
            Assert.Equal("Ready for pull!", result.Message);
        }

        [Fact]
        public void Parse_EchoCommand_ReturnsLocalEcho()
        {
            var result = ChatCommandRouter.Parse("/echo This is a local reminder", ChatSendKind.Say, _world);
            Assert.Equal(ChatCommandResultKind.LocalEcho, result.Kind);
            Assert.Equal("This is a local reminder", result.Message);
        }

        [Theory]
        [InlineData("!pos")]
        [InlineData("!zone 240")]
        [InlineData("!heal")]
        public void Parse_ServerExclamationCommands_ReturnsServerCommand(string cmd)
        {
            var result = ChatCommandRouter.Parse(cmd, ChatSendKind.Say, _world);
            Assert.Equal(ChatCommandResultKind.ServerCommand, result.Kind);
            Assert.Equal(ChatSendKind.Say, result.SpeechKind);
            Assert.Equal(cmd, result.Message);
        }

        [Fact]
        public void Parse_PlainChat_UsesDefaultChannel()
        {
            var result = ChatCommandRouter.Parse("Hello everybody", ChatSendKind.Shout, _world);
            Assert.Equal(ChatCommandResultKind.SendChat, result.Kind);
            Assert.Equal(ChatSendKind.Shout, result.SpeechKind);
            Assert.Equal("Hello everybody", result.Message);
        }

        [Fact]
        public void Parse_UnrecognizedSlashCommand_ReturnsUnrecognized()
        {
            var result = ChatCommandRouter.Parse("/nonexistentcommand 123", ChatSendKind.Say, _world);
            Assert.Equal(ChatCommandResultKind.Unrecognized, result.Kind);
        }

        [Fact]
        public void StandardMessages_ResolvesKnownMessageIds()
        {
            Assert.True(StandardMessages.TryGetMessage(11, out string msg11));
            Assert.Equal("Your invitation was declined.", msg11);

            Assert.True(StandardMessages.TryGetMessage(23, out string msg23));
            Assert.Equal("You cannot invite that person at this time.", msg23);

            var sysMsg = new SystemMessage(1001, 10, 11, 0, string.Empty, DateTime.UtcNow);
            string formatted = StandardMessages.FormatMessage(sysMsg);
            Assert.Equal("Your invitation was declined.", formatted);
        }
    }
}
