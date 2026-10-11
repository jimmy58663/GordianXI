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
            // Windower display order: internal (10, height 5, 20) reads X=10, Y=20, Z=5.
            Assert.Contains("X=10.00, Y=20.00, Z=5.00", res.Message);
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
        public async Task ExecuteCommand_Attack_WhileEngagedOnAnother_SendsChangeTarget()
        {
            _world.UpsertEntity(new WorldEntity(0x02020203, 26, EntityType.Monster) { Name = "Wild_Rabbit", Hpp = 100 });
            await _actionService.ExecuteCommandAsync("/attack Forest_Hare");
            Assert.Equal((ushort)CliActionId.Attack, ActionIdOf(_sentChunks[^1]));

            var res = await _actionService.ExecuteCommandAsync("/attack Wild_Rabbit");

            Assert.True(res.Success);
            byte[] packet = _sentChunks[^1];
            Assert.Equal((ushort)CliActionId.ChangeTarget, ActionIdOf(packet));
            Assert.Equal(0x02020203u, System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(4, 4)));
            Assert.Equal(26, System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(8, 2)));
            Assert.True(_combatState.IsEngaged);
            Assert.Equal(0x02020203u, _combatState.TargetServerId);

            // Attacking the engaged target again is a plain Attack.
            await _actionService.ExecuteCommandAsync("/attack Wild_Rabbit");
            Assert.Equal((ushort)CliActionId.Attack, ActionIdOf(_sentChunks[^1]));
        }

        [Fact]
        public async Task ExecuteCommand_Refa_ReleasesOneOrAllOwnTrusts()
        {
            _world.UpsertEntity(new WorldEntity(0x01A00700, 1792, EntityType.Trust) { Name = "Shantotto" });
            _world.UpsertEntity(new WorldEntity(0x01A00701, 1793, EntityType.Trust) { Name = "Kupipi" });
            _partyState.UpsertMember(new PartyMember { ServerId = 0x01020304, TargetIndex = 10, Name = "TestPlayer" });
            _partyState.UpsertMember(new PartyMember { ServerId = 0x01A00700, TargetIndex = 1792, Name = "Shantotto" });
            _partyState.UpsertMember(new PartyMember { ServerId = 0x01A00701, TargetIndex = 1793, Name = "Kupipi" });

            var res = await _actionService.ExecuteCommandAsync("/refa kupipi");
            Assert.True(res.Success);
            byte[] one = Assert.Single(_sentChunks);
            Assert.Equal((ushort)CliActionId.Talk, ActionIdOf(one));
            Assert.Equal(0x01A00701u, System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(one.AsSpan(4, 4)));
            Assert.Equal(1u, System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(one.AsSpan(12, 4)));

            _sentChunks.Clear();
            res = await _actionService.ExecuteCommandAsync("/refa all");
            Assert.True(res.Success);
            Assert.Equal(2, _sentChunks.Count);
            Assert.Equal(0x01A00700u, System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(_sentChunks[0].AsSpan(4, 4)));
            Assert.Equal(0u, System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(_sentChunks[0].AsSpan(12, 4)));
            Assert.Equal(0x01A00701u, System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(_sentChunks[1].AsSpan(4, 4)));
            Assert.Equal(1u, System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(_sentChunks[1].AsSpan(12, 4)));

            _sentChunks.Clear();
            res = await _actionService.ExecuteCommandAsync("/refa Prishe");
            Assert.False(res.Success);
            Assert.Empty(_sentChunks);
        }

        [Theory]
        [InlineData("/fish", CliActionId.Fish, 0u)]
        [InlineData("/sprint", CliActionId.Sprint, 0u)]
        [InlineData("/dig", CliActionId.ChocoboDig, 0u)]
        [InlineData("/blockaid on", CliActionId.Blockaid, 1u)]
        public async Task ExecuteCommand_SelfActions_TargetThePlayer(string input, CliActionId action, uint param)
        {
            var res = await _actionService.ExecuteCommandAsync(input);

            Assert.True(res.Success);
            byte[] packet = Assert.Single(_sentChunks);
            Assert.Equal((ushort)action, ActionIdOf(packet));
            Assert.Equal(0x01020304u, System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(4, 4)));
            Assert.Equal(10, System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(8, 2)));
            Assert.Equal(param, System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(12, 4)));
        }

        private static ushort ActionIdOf(byte[] packet) => System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(10, 2));

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
            _profile.FeatureRestrictions = FeatureRestrictions.None;
            var res = await _actionService.ExecuteCommandAsync("/moveto 55.5 12.3 88.0");

            Assert.True(res.Success);
            Assert.Contains("Locomotion updated", res.Message);
            Assert.NotEmpty(_sentChunks);
            // Pos packet (0x015) is 32 bytes
            Assert.Equal(32, _sentChunks[0].Length);
        }

        [Fact]
        public async Task ExecuteCommand_MoveTo_TakesWindowerOrderWithZAsHeight()
        {
            _profile.FeatureRestrictions = FeatureRestrictions.None;
            await _actionService.ExecuteCommandAsync("/moveto 124.12 -588.277 -5.69");

            Assert.True(_world.TryGetByServerId(0x01020304, out var player));
            // Internal Y is height, internal Z is the display Y.
            Assert.Equal(new Vector3(124.12f, -5.69f, -588.277f), player!.Position);
        }

        [Fact]
        public async Task ExecuteCommand_MoveTo_WithoutHeightKeepsTheCurrentHeight()
        {
            _profile.FeatureRestrictions = FeatureRestrictions.None;
            await _actionService.ExecuteCommandAsync("/moveto 30 40");

            Assert.True(_world.TryGetByServerId(0x01020304, out var player));
            Assert.Equal(new Vector3(30f, 5f, 40f), player!.Position);
        }

        [Fact]
        public async Task ExecuteCommand_MoveTo_MovementRestricted_BlocksSyntheticMovement()
        {
            _profile.FeatureRestrictions = FeatureRestrictions.Movement;
            var res = await _actionService.ExecuteCommandAsync("/moveto 55.5 12.3 88.0");

            Assert.False(res.Success);
            Assert.Contains("blocked by server feature restrictions (Movement)", res.Message);
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

        [Fact]
        public async Task ExecuteCommand_Help_ListsCommands_FilteredByPolicy()
        {
            _profile.FeatureRestrictions = FeatureRestrictions.None;
            var resAllow = await _actionService.ExecuteCommandAsync("/help");
            Assert.True(resAllow.Success);
            Assert.Contains("[Restrictions: None]", resAllow.Message);
            Assert.Contains("/pos", resAllow.Message);
            Assert.Contains("/moveto", resAllow.Message);

            _profile.FeatureRestrictions = FeatureRestrictions.Movement;
            var resVanilla = await _actionService.ExecuteCommandAsync("/commands");
            Assert.True(resVanilla.Success);
            Assert.Contains("[Restrictions: Movement]", resVanilla.Message);
            Assert.Contains("/pos", resVanilla.Message);
            Assert.DoesNotContain("/moveto", resVanilla.Message);
        }

        [Fact]
        public async Task ExecuteCommand_Help_SpecificCommand_ReflectsPolicy()
        {
            _profile.FeatureRestrictions = FeatureRestrictions.None;
            var resAllow = await _actionService.ExecuteCommandAsync("/help moveto");
            Assert.True(resAllow.Success);
            Assert.Contains("Usage: /moveto", resAllow.Message);

            _profile.FeatureRestrictions = FeatureRestrictions.Movement;
            var resVanilla = await _actionService.ExecuteCommandAsync("/help moveto");
            Assert.True(resVanilla.Success);
            Assert.Contains("blocked by server feature restrictions (Movement)", resVanilla.Message);
        }

        [Fact]
        public async Task ExecuteCommand_GmHelp_NonGm_ReturnsYouAreNotAGm()
        {
            _localPlayer.GmLevel = 0;
            var res = await _actionService.ExecuteCommandAsync("/gmhelp");

            Assert.False(res.Success); // Warn result
            Assert.Equal("You are not a GM.", res.Message);

            var resCmds = await _actionService.ExecuteCommandAsync("/gmcommands");
            Assert.Equal("You are not a GM.", resCmds.Message);

            var resHelpGm = await _actionService.ExecuteCommandAsync("/help gm");
            Assert.Equal("You are not a GM.", resHelpGm.Message);
        }

        [Fact]
        public async Task ExecuteCommand_GmHelp_GmPlayer_ListsGmCommands()
        {
            _localPlayer.GmLevel = 1;
            var res = await _actionService.ExecuteCommandAsync("/gmhelp");

            Assert.True(res.Success);
            Assert.Contains("--- Game Master (GM) Commands", res.Message);
            Assert.Contains("!pos", res.Message);
            Assert.Contains("!goto", res.Message);
            Assert.Contains("!zone", res.Message);
            Assert.Contains("!god", res.Message);
        }

        [Theory]
        [InlineData("/moveto 55.5 12.3 88.0")]
        [InlineData("/moveto 55.5, 12.3, 88.0")]
        [InlineData("/moveto (55.5, 12.3, 88.0)")]
        [InlineData("/moveto Pos: (55.5, 12.3, 88.0)")]
        [InlineData("/moveto X=55.5, Y=12.3, Z=88.0")]
        public async Task ExecuteCommand_MoveTo_VariousCoordinateFormats_ParsesSuccessfully(string cmd)
        {
            _profile.FeatureRestrictions = FeatureRestrictions.None;
            var res = await _actionService.ExecuteCommandAsync(cmd);

            Assert.True(res.Success);
            Assert.Contains("X=55.50, Y=12.30, Z=88.00", res.Message);
        }
    

        [Theory]
        [InlineData("/lockstyle", LockstyleMode.Query)]
        [InlineData("/lockstyle on", LockstyleMode.Enable)]
        [InlineData("/lockstyle OFF", LockstyleMode.Disable)]
        [InlineData("/lockstyleset", LockstyleMode.Enable)]
        public async Task ExecuteCommand_Lockstyle_Sends0x053WithRetailMode(string input, LockstyleMode mode)
        {
            _actionService.InventoryModule = new InventoryPacketModule(new InventoryState(), _localPlayer, (mem, _) =>
            {
                _sentChunks.Add(mem.ToArray());
                return Task.CompletedTask;
            });

            var result = await _actionService.ExecuteCommandAsync(input);

            Assert.True(result.Success);
            byte[] packet = Assert.Single(_sentChunks);
            Assert.True(PacketHeader.TryParse(packet, out var header));
            Assert.Equal(0x053, header.PacketId);
            Assert.Equal(0, packet[4]); // no items
            Assert.Equal((byte)mode, packet[5]);
        }

        [Theory]
        [InlineData("/lockstyle maybe")]
        [InlineData("/lockstyleset 3")]
        public async Task ExecuteCommand_Lockstyle_UnsupportedArgumentsSendNothing(string input)
        {
            _actionService.InventoryModule = new InventoryPacketModule(new InventoryState(), _localPlayer, (mem, _) =>
            {
                _sentChunks.Add(mem.ToArray());
                return Task.CompletedTask;
            });

            var result = await _actionService.ExecuteCommandAsync(input);

            Assert.Equal(PlayerActionResultKind.Warning, result.Kind);
            Assert.Empty(_sentChunks);
        }

        [Fact]
        public async Task MarkKeyItemSeen_UsesLocalPlayerIndex()
        {
            var progression = new ProgressionState();
            var acquired = new uint[16];
            acquired[0] = 1u << 5;
            progression.UpdateKeyItems(0, acquired, new uint[16]);
            _actionService.ProgressionModule = new ProgressionPacketModule(progression, _localPlayer, (mem, _) =>
            {
                _sentChunks.Add(mem.ToArray());
                return Task.CompletedTask;
            });

            Assert.True(await _actionService.MarkKeyItemSeenAsync(5));

            byte[] packet = Assert.Single(_sentChunks);
            Assert.Equal(0x064, PacketHeader.TryParse(packet, out var header) ? header.PacketId : 0);
            Assert.Equal(10, BitConverter.ToUInt16(packet, 72)); // TestPlayer's target index
        }

        private void EngageHare()
        {
            _actionService.SetTargetByServerId(0x02020202);
            _combatState.Engage(0x02020202, 25);
        }

        private void SendServerStatus(byte status)
        {
            var payload = new byte[0x60];
            payload[44] = status;
            _localPlayer.UpdateFromCharStatus(new S2C_0x037_CharStatus(payload));
        }

        [Fact]
        public void EngagedTargetDies_EndsEngagementAndDropsTarget()
        {
            EngageHare();
            Assert.True(_world.TryGetByServerId(0x02020202, out var hare));

            hare!.Hpp = 0;
            hare.AnimationState = 3;
            _world.UpsertEntity(hare);

            Assert.False(_combatState.IsEngaged);
            Assert.Null(_actionService.CurrentTarget);
            Assert.Empty(_sentChunks); // the server already ended it: no disengage request
        }

        [Fact]
        public void EngagedTargetDespawns_EndsEngagementAndDropsTarget()
        {
            EngageHare();

            _world.RemoveEntity(0x02020202);

            Assert.False(_combatState.IsEngaged);
            Assert.Null(_actionService.CurrentTarget);
        }

        [Fact]
        public void ServerEndsBattleStatus_EndsEngagementAndDropsTarget()
        {
            EngageHare();
            SendServerStatus(1);
            Assert.True(_combatState.IsEngaged);

            SendServerStatus(0);

            Assert.False(_combatState.IsEngaged);
            Assert.Null(_actionService.CurrentTarget);
        }

        [Fact]
        public void StatusWithoutPriorBattle_DoesNotEndAFreshEngagement()
        {
            // Engaging sets the local state before the server's 0x037 says so; a stale idle status must not undo it.
            EngageHare();

            SendServerStatus(0);

            Assert.True(_combatState.IsEngaged);
            Assert.NotNull(_actionService.CurrentTarget);
        }

        [Fact]
        public void SelectedTargetDiesWhileNotEngaged_KeepsTarget_ButDespawnDropsIt()
        {
            _actionService.SetTargetByServerId(0x02020202);
            Assert.True(_world.TryGetByServerId(0x02020202, out var hare));

            hare!.Hpp = 0;
            _world.UpsertEntity(hare);
            Assert.NotNull(_actionService.CurrentTarget);

            _world.RemoveEntity(0x02020202);
            Assert.Null(_actionService.CurrentTarget);
        }

        /// <summary>
        /// #327: a selected monster entering the death status (server status 3, as LandSandBoat's CDeathState sends it) is
        /// dropped as the target, as retail does; so is one the server turns TargetOff (#334).
        /// </summary>
        [Fact]
        public void SelectedMonsterDiesOrTurnsTargetOff_DropsTarget()
        {
            _actionService.SetTargetByServerId(0x02020202);
            Assert.True(_world.TryGetByServerId(0x02020202, out var hare));

            hare!.Hpp = 0;
            hare.AnimationState = WorldEntity.StatusDead;
            _world.UpsertEntity(hare);
            Assert.Null(_actionService.CurrentTarget);

            hare.Hpp = 100;
            hare.AnimationState = 0;
            _actionService.SetTargetByServerId(0x02020202);
            Assert.NotNull(_actionService.CurrentTarget);
            hare.IsTargetOff = true;
            _world.UpsertEntity(hare);
            Assert.Null(_actionService.CurrentTarget);
        }

        [Fact]
        public void AnchorCommand_TogglesAndReportsServerLock()
        {
            Assert.False(_actionService.Knockback.AnchorRequested);

            var on = _actionService.ApplyAnchorCommand("on");
            Assert.True(_actionService.Knockback.IsAnchored(_profile));
            Assert.Contains("on", on.Message);

            _actionService.ApplyAnchorCommand("");
            Assert.False(_actionService.Knockback.AnchorRequested);

            _profile.FeatureRestrictions = FeatureRestrictions.KnockbackOverride;
            var locked = _actionService.ApplyAnchorCommand("on");
            Assert.False(_actionService.Knockback.IsAnchored(_profile));
            Assert.Contains("server", locked.Message);

            Assert.Contains("Usage", _actionService.ApplyAnchorCommand("sideways").Message);
        }
        [Fact]
        public void SetTargetByPartySlot_CountsTheOtherMembersInPartyOrder()
        {
            _world.UpsertEntity(new WorldEntity(0x0A, 0x0A, EntityType.Player) { Name = "Second", IsSpawned = true });
            _world.UpsertEntity(new WorldEntity(0x0B, 0x0B, EntityType.Player) { Name = "Third", IsSpawned = true });
            _partyState.UpsertMember(new PartyMember { ServerId = 0x0B, Name = "Third", MemberNumber = 2 });
            _partyState.UpsertMember(new PartyMember { ServerId = 0x01020304, Name = "TestPlayer", MemberNumber = 0 });
            _partyState.UpsertMember(new PartyMember { ServerId = 0x0A, Name = "Second", MemberNumber = 1 });
            _partyState.UpsertMember(new PartyMember { ServerId = 0x0C, Name = "OtherParty", MemberNumber = 0, PartyNumber = 1 });

            Assert.True(_actionService.SetTargetByPartySlot(1));
            Assert.Equal(0x0Au, _actionService.CurrentTarget?.ServerId);
            Assert.True(_actionService.SetTargetByPartySlot(2));
            Assert.Equal(0x0Bu, _actionService.CurrentTarget?.ServerId);
            Assert.False(_actionService.SetTargetByPartySlot(3));
        }
    }
}
