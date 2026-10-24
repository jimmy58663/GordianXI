// tests/Gordian.Core.Tests/Macros/MacroCommandTests.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Gordian.Engine.Actions;
using Gordian.Core.Config;
using Gordian.Engine.Macros;
using Gordian.Core.Network;
using Gordian.Core.Network.Packets;
using Gordian.Engine.Ui;
using Gordian.Core.World;
using Xunit;

namespace Gordian.Core.Tests.Macros
{
    /// <summary>The macro commands and chat placeholders of #362.</summary>
    public class MacroCommandTests
    {
        private readonly WorldState _world = new();
        private readonly LocalPlayerState _player = new() { ServerId = 0x01020304 };
        private readonly List<byte[]> _sent = new();
        private readonly PlayerActionService _service;

        public MacroCommandTests()
        {
            var profile = new SessionProfile();
            Task Capture(ReadOnlyMemory<byte> mem, bool urgent)
            {
                lock (_sent) _sent.Add(mem.ToArray());
                return Task.CompletedTask;
            }
            _service = new PlayerActionService(profile, _world, _player,
                new CombatPacketModule(new CombatState(), _player, Capture), new ChatPacketModule(Capture),
                new PartyPacketModule(new PartyState(), Capture), new EntityPacketModule(_world, _player, Capture),
                new LifecyclePacketModule(profile, Capture), Capture);
            _world.UpsertEntity(new WorldEntity(0x01020304, 10, EntityType.Player) { Name = "Tester", Position = new Vector3(0, 0, 0) });
            _world.UpsertEntity(new WorldEntity(0x01000020, 32, EntityType.Npc) { Name = "Far Npc", Position = new Vector3(20, 0, 0) });
            _world.UpsertEntity(new WorldEntity(0x01000021, 33, EntityType.Npc) { Name = "Near Npc", Position = new Vector3(3, 0, 0) });
            _world.UpsertEntity(new WorldEntity(0x01000022, 34, EntityType.Monster) { Name = "Wild Rabbit", Position = new Vector3(6, 0, 0), Hpp = 100 });
        }

        [Theory]
        [InlineData("/macro book 3", ChatCommandResultKind.Macro)]
        [InlineData("/wait 2", ChatCommandResultKind.Wait)]
        [InlineData("/recast \"Provoke\"", ChatCommandResultKind.Recast)]
        [InlineData("/targetnpc", ChatCommandResultKind.TargetNpc)]
        [InlineData("/targetbnpc", ChatCommandResultKind.TargetBattleNpc)]
        [InlineData("/lastsynth", ChatCommandResultKind.LastSynth)]
        [InlineData("/acmd leader Tester", ChatCommandResultKind.AllianceCommand)]
        public void Router_KnowsTheMacroCommands(string line, ChatCommandResultKind kind)
        {
            Assert.Equal(kind, ChatCommandRouter.Parse(line).Kind);
            Assert.Equal("Provoke", ChatCommandRouter.Parse("/recast \"Provoke\"").Message);
        }

        [Fact]
        public async Task Macro_BookAndSetChangeTheCurrentSet()
        {
            var library = new MacroLibrary();
            _service.Macros = library;
            Assert.True((await _service.ExecuteCommandAsync("/macro book 12")).Success);
            Assert.True((await _service.ExecuteCommandAsync("/macro set 10")).Success);
            Assert.Equal((11, 9), (library.CurrentBook, library.CurrentSet));
            Assert.False((await _service.ExecuteCommandAsync("/macro book 41")).Success);
            Assert.False((await _service.ExecuteCommandAsync("/macro page 1")).Success);
        }

        [Fact]
        public async Task TargetNpc_AndTargetBnpc_PickTheClosest()
        {
            Assert.True((await _service.ExecuteCommandAsync("/targetnpc")).Success);
            Assert.Equal("Near Npc", _service.CurrentTarget?.Name);
            Assert.True((await _service.ExecuteCommandAsync("/targetbnpc")).Success);
            Assert.Equal("Wild Rabbit", _service.CurrentTarget?.Name);
        }

        [Fact]
        public async Task PartyLeader_SendsGroupChange2KindParty()
        {
            Assert.Equal(ChatCommandResultKind.PartyLeader, ChatCommandRouter.Parse("/pcmd leader Tester").Kind);
            var result = await _service.ExecuteCommandAsync("/pcmd leader Tester");
            Assert.True(result.Success);
            var packet = _sent[^1];
            Assert.Equal(0x77, packet[0] | ((packet[1] & 0x01) << 8));
            Assert.Contains("Usage", (await _service.ExecuteCommandAsync("/pcmd leader")).Message);
        }

        [Fact]
        public async Task Wait_TypedDoesNothing()
        {
            var result = await _service.ExecuteCommandAsync("/wait 3");
            Assert.True(result.Success);
            Assert.Equal(string.Empty, result.Message);
        }

        [Fact]
        public async Task AllianceLeader_SendsGroupChange2KindAlliance()
        {
            var result = await _service.ExecuteCommandAsync("/acmd leader Tester");
            Assert.True(result.Success);
            var packet = _sent[^1];
            Assert.Equal(0x77, packet[0] | ((packet[1] & 0x01) << 8));
        }

        [Fact]
        public async Task LastSynth_WithoutASynthesisWarns()
        {
            var result = await _service.ExecuteCommandAsync("/lastsynth");
            Assert.False(result.Success);
        }

        [Fact]
        public void Tokens_FillVitalsAndTargetNames()
        {
            _player.UpdateVitals(1234, 56, 1000);
            _service.SetTargetByServerId(0x01000022);
            Assert.Equal("I have 1234/0 HP, TP 1000 on Wild Rabbit; me Tester", _service.ExpandChatTokens("I have <hp> HP, TP <tp> on <t>; me <me>"));
            // Unknown placeholders and the sub-target cursor stay as typed; <wait n> goes.
            Assert.Equal("<foo> <st> done", _service.ExpandChatTokens("<foo> <st> done <wait 2>"));
            // No pet: <pethpp> stays.
            Assert.Equal("<pethpp>", _service.ExpandChatTokens("<pethpp>"));
            _service.PetSnapshot = () => new LocalPet(1, 2, 3, 75, 40, 1500, 0, "Carbuncle");
            Assert.Equal("75% 40% 1500", _service.ExpandChatTokens("<pethpp> <petmpp> <pettp>"));
        }

        [Fact]
        public void TokenFormats()
        {
            Assert.Equal("WAR75/NIN37", ChatTextTokens.Jobs("WAR", 75, "NIN", 37));
            Assert.Equal("WAR75", ChatTextTokens.Jobs("WAR", 75, null, 0));
            Assert.Equal("1:05", ChatTextTokens.Recast(TimeSpan.FromSeconds(64.2)));
            Assert.Equal("0:00", ChatTextTokens.Recast(TimeSpan.FromSeconds(-1)));
            Assert.Equal("recast=Cure IV", ChatTextTokens.NormalizeWord("Recast=\"Cure IV\""));
            Assert.Equal("a <B> c", ChatTextTokens.Expand("a <B> c", w => w == "x" ? "y" : null));
            Assert.Equal("a y c", ChatTextTokens.Expand("a <X> c", w => w == "x" ? "y" : null));
        }

        [Fact]
        public async Task Chat_ExpandsPlaceholdersBeforeSendingAndEchoing()
        {
            var chat = new StockUiChat { CharacterName = () => "Tester", ExpandTokens = _service.ExpandChatTokens };
            chat.Execute = (line, kind) => _service.ExecuteCommandAsync(line, kind);
            _service.SetTargetByServerId(0x01000021);
            await chat.SubmitAsync("/p Healing <t>", ChatInputMode.Say);
            var lines = new List<ChatLogLine>();
            chat.Log.CopyVisible(1, 10, lines);
            Assert.Contains(lines, l => l.Text == "(Tester) Healing Near Npc");

            // /echo is drawn in retail's echo colour (maintainer retail capture 2026-10-13).
            await chat.SubmitAsync("/echo hello", ChatInputMode.Say);
            lines.Clear();
            chat.Log.CopyVisible(1, 10, lines);
            Assert.Equal(ChatLogChannel.Echo, lines.Single(l => l.Text == "hello").Channel);
        }
    }
}
