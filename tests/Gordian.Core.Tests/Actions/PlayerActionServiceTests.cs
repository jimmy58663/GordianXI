// tests/Gordian.Core.Tests/Actions/PlayerActionServiceTests.cs
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;
using Gordian.Core.Actions;
using Gordian.Core.Config;
using Gordian.Core.Network;
using Gordian.Core.Network.Packets;
using Gordian.Core.World;
using Xunit;

namespace Gordian.Core.Tests.Actions
{
    public class PlayerActionServiceTests
    {
        private readonly SessionProfile _profile;
        private readonly WorldState _world;
        private readonly LocalPlayerState _localPlayer;
        private readonly CombatState _combatState;
        private readonly PartyState _partyState;
        private readonly CombatPacketModule _combatModule;
        private readonly ChatPacketModule _chatModule;
        private readonly PartyPacketModule _partyModule;
        private readonly EntityPacketModule _entityModule;
        private readonly LifecyclePacketModule _lifecycleModule;
        private readonly List<byte[]> _sentChunks = new List<byte[]>();
        private readonly PlayerActionService _actionService;

        public PlayerActionServiceTests()
        {
            _profile = new SessionProfile();
            _world = new WorldState();
            _localPlayer = new LocalPlayerState { ServerId = 0x01020304 };
            _combatState = new CombatState();
            _partyState = new PartyState();

            Task CaptureChunk(ReadOnlyMemory<byte> mem, bool urgent)
            {
                _sentChunks.Add(mem.ToArray());
                return Task.CompletedTask;
            }

            _combatModule = new CombatPacketModule(_combatState, _localPlayer, CaptureChunk);
            _chatModule = new ChatPacketModule(CaptureChunk);
            _partyModule = new PartyPacketModule(_partyState, CaptureChunk);
            _entityModule = new EntityPacketModule(_world, _localPlayer, CaptureChunk);
            _lifecycleModule = new LifecyclePacketModule(_profile, CaptureChunk);

            _actionService = new PlayerActionService(
                _profile,
                _world,
                _localPlayer,
                _combatModule,
                _chatModule,
                _partyModule,
                _entityModule,
                _lifecycleModule,
                CaptureChunk);

            // Add local player entity to world
            _world.UpsertEntity(new WorldEntity(0x01020304, 10, EntityType.Player)
            {
                Name = "TestPlayer",
                Position = new Vector3(10.0f, 5.0f, 20.0f),
                Direction = 64
            });

            // Add target monster to world
            _world.UpsertEntity(new WorldEntity(0x02020202, 25, EntityType.Monster)
            {
                Name = "Forest_Hare",
                Position = new Vector3(15.0f, 5.0f, 22.0f),
                Hpp = 100
            });
        }

        [Fact]
        public async Task ExecuteCommand_Pos_ReturnsFormattedCoordinates()
        {
            var res = await _actionService.ExecuteCommandAsync("/pos");

            Assert.True(res.Success);
            Assert.Contains("Position", res.Message);
            Assert.Contains("X=10.00", res.Message);
            Assert.Contains("Y=5.00", res.Message);
            Assert.Contains("Z=20.00", res.Message);
        }

        [Fact]
        public async Task ExecuteCommand_Vitals_ReturnsCurrentHpMp()
        {
            var res = await _actionService.ExecuteCommandAsync("/vitals");

            Assert.True(res.Success);
            Assert.Contains("Vitals", res.Message);
            Assert.Contains("HP:", res.Message);
        }

        [Fact]
        public async Task ExecuteCommand_Nearby_ReturnsEntitiesWithinRadius()
        {
            var res = await _actionService.ExecuteCommandAsync("/nearby 50");

            Assert.True(res.Success);
            Assert.Contains("Forest_Hare", res.Message);
            Assert.Contains("Nearby Entities", res.Message);
        }

        [Fact]
        public async Task ExecuteCommand_Target_ByName_SelectsEntity()
        {
            var res = await _actionService.ExecuteCommandAsync("/target Forest_Hare");

            Assert.True(res.Success);
            Assert.NotNull(_actionService.CurrentTarget);
            Assert.Equal("Forest_Hare", _actionService.CurrentTarget!.Name);
            Assert.Equal(0x02020202u, _actionService.CurrentTarget.ServerId);
        }

        [Fact]
        public async Task ExecuteCommand_Target_ByIndex_SelectsEntity()
        {
            var res = await _actionService.ExecuteCommandAsync("/target 25");

            Assert.True(res.Success);
            Assert.NotNull(_actionService.CurrentTarget);
            Assert.Equal("Forest_Hare", _actionService.CurrentTarget!.Name);
        }

        [Fact]
        public async Task ExecuteCommand_TargetInfo_ReturnsDetails()
        {
            _actionService.SetTargetByName("Forest_Hare");
            var res = await _actionService.ExecuteCommandAsync("/targetinfo");

            Assert.True(res.Success);
            Assert.Contains("Forest_Hare", res.Message);
            Assert.Contains("Dist:", res.Message);
        }

        [Fact]
        public async Task ExecuteCommand_Attack_WithTarget_Sends0x01APacket()
        {
            _actionService.SetTargetByName("Forest_Hare");
            var res = await _actionService.ExecuteCommandAsync("/attack");

            Assert.True(res.Success);
            Assert.NotEmpty(_sentChunks);
            // Verify packet length is 28 bytes (0x01A action request)
            Assert.Equal(28, _sentChunks[0].Length);
        }

        [Fact]
        public async Task ExecuteCommand_Attack_WithoutTarget_ReturnsWarning()
        {
            _actionService.ClearTarget();
            var res = await _actionService.ExecuteCommandAsync("/attack");

            Assert.False(res.Success);
            Assert.Contains("No target selected", res.Message);
            Assert.Empty(_sentChunks);
        }

        [Fact]
        public async Task ExecuteCommand_Disengage_SendsAttackOff()
        {
            var res = await _actionService.ExecuteCommandAsync("/attackoff");

            Assert.True(res.Success);
            Assert.NotEmpty(_sentChunks);
            Assert.Equal(28, _sentChunks[0].Length);
        }

        [Fact]
        public async Task ExecuteCommand_Cast_SendsMagicRequest()
        {
            _actionService.SetTargetByName("Forest_Hare");
            var res = await _actionService.ExecuteCommandAsync("/magic 1");

            Assert.True(res.Success);
            Assert.NotEmpty(_sentChunks);
            Assert.Equal(28, _sentChunks[0].Length);
        }

        [Fact]
        public async Task ExecuteCommand_MoveTo_AllowAll_UpdatesPositionAndSendsPacket()
        {
            _profile.AutomationPolicy = ServerAutomationPolicy.AllowAll;
            var res = await _actionService.ExecuteCommandAsync("/moveto 55.5 12.3 88.0");

            Assert.True(res.Success);
            Assert.Contains("Locomotion updated", res.Message);
            Assert.NotEmpty(_sentChunks);
            // Pos packet (0x015) is 32 bytes
            Assert.Equal(32, _sentChunks[0].Length);
        }

        [Fact]
        public async Task ExecuteCommand_MoveTo_StrictVanilla_BlocksSyntheticMovement()
        {
            _profile.AutomationPolicy = ServerAutomationPolicy.StrictVanilla;
            var res = await _actionService.ExecuteCommandAsync("/moveto 55.5 12.3 88.0");

            Assert.False(res.Success);
            Assert.Contains("blocked by server automation policy (StrictVanilla)", res.Message);
            Assert.Empty(_sentChunks);
        }

        [Fact]
        public async Task ExecuteCommand_ServerCommand_SendsSayPacket()
        {
            var res = await _actionService.ExecuteCommandAsync("!pos");

            Assert.True(res.Success);
            Assert.Contains("Sent server command", res.Message);
            Assert.NotEmpty(_sentChunks);
        }

        [Fact]
        public async Task ExecuteCommand_Jump_SendsJumpPacket()
        {
            var res = await _actionService.ExecuteCommandAsync("/jump");

            Assert.True(res.Success);
            Assert.NotEmpty(_sentChunks);
            // 0x11D Jump is 12 bytes
            Assert.Equal(12, _sentChunks[0].Length);
        }

        [Fact]
        public async Task MoveToAsync_FiresLocalPlayerMovedEventAndUpdatesWorldState()
        {
            Vector3? movedPos = null;
            byte? movedDir = null;
            _actionService.LocalPlayerMoved += (pos, dir) =>
            {
                movedPos = pos;
                movedDir = dir;
            };

            var targetPos = new Vector3(12.5f, 3.4f, 56.7f);
            var res = await _actionService.MoveToAsync(targetPos);

            Assert.True(res.Success);
            Assert.NotNull(movedPos);
            Assert.Equal(targetPos, movedPos.Value);
            Assert.True(_world.TryGetByServerId(_localPlayer.ServerId, out var localEnt));
            Assert.Equal(targetPos, localEnt!.Position);
        }

        [Fact]
        public async Task ExecuteCommand_TargetHexServerId_TargetsEntity()
        {
            var target = new WorldEntity(0x01020304, 88, EntityType.Monster)
            {
                Name = "Goblin_Smithy"
            };
            _world.UpsertEntity(target);

            var res = await _actionService.ExecuteCommandAsync("/target 0x01020304");

            Assert.True(res.Success);
            Assert.NotNull(_actionService.CurrentTarget);
            Assert.Equal(0x01020304u, _actionService.CurrentTarget!.ServerId);
        }

        [Fact]
        public void NearbySummary_DisplaysUnknown_WhenNameIsMissing()
        {
            var nameless = new WorldEntity(0x5555, 12, EntityType.Player)
            {
                Name = "",
                Position = new Vector3(5, 0, 5),
                Hpp = 100
            };
            _world.UpsertEntity(nameless);

            string summary = _actionService.GetNearbySummary(50f);
            Assert.Contains("<Unknown>", summary);
            Assert.Contains("HP: 100%", summary);
        }
    }
}
