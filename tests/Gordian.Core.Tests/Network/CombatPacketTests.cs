// tests/Gordian.Core.Tests/Network/CombatPacketTests.cs
using System;
using System.Buffers.Binary;
using System.Numerics;
using System.Threading.Tasks;
using Gordian.Core.Network;
using Gordian.Core.Network.Packets;
using Gordian.Core.World;
using Xunit;

namespace Gordian.Core.Tests.Network
{
    public class CombatPacketTests
    {
        [Fact]
        public void S2C_0x028_CombatAction_DecodesBasicAttack()
        {
            byte[] payload = new byte[64];
            payload[0] = 30; // workSize

            var writer = new BitStreamWriter(payload.AsSpan(1));
            writer.WriteUInt32(0x10001111, 32);                         // ActorId
            writer.WriteByte(1, 6);                                     // TargetCount = 1
            writer.WriteByte(0, 4);                                     // ResSum = 0
            writer.WriteByte((byte)ActionCategory.BasicAttack, 4);      // Category = 1
            writer.WriteUInt32(0, 32);                                  // ActionId = 0
            writer.WriteUInt32(0, 32);                                  // Recast = 0

            // Target 0
            writer.WriteUInt32(0x20002222, 32);                         // TargetId
            writer.WriteByte(1, 4);                                     // ResultCount = 1

            // Result 0
            writer.WriteByte((byte)ActionResolution.Hit, 3);            // Resolution = Hit
            writer.WriteByte(0, 2);                                     // Kind = 0
            writer.WriteUInt16(100, 12);                                // Animation = 100
            writer.WriteByte(0, 5);                                     // Info = 0
            writer.WriteByte(2, 5);                                     // Scale = 2
            writer.WriteUInt32(145, 17);                                // Param (Damage) = 145
            writer.WriteUInt16(1, 10);                                  // MessageId = 1
            writer.WriteUInt32(0, 31);                                  // Modifier = 0
            writer.WriteBool(false);                                    // HasProc = false
            writer.WriteBool(false);                                    // HasReact = false

            var action = new S2C_0x028_CombatAction(payload);

            Assert.True(action.IsValid);
            Assert.Equal(0x10001111u, action.ActorId);
            Assert.Equal(1, action.TargetCount);
            Assert.Equal(ActionCategory.BasicAttack, action.Category);
            Assert.Equal(0u, action.ActionId);

            Span<CombatActionResult> results = stackalloc CombatActionResult[4];
            bool targetFound = action.TryGetTarget(0, out uint targetId, results, out int resultsCount);

            Assert.True(targetFound);
            Assert.Equal(0x20002222u, targetId);
            Assert.Equal(1, resultsCount);
            Assert.Equal(ActionResolution.Hit, results[0].Resolution);
            Assert.Equal(145, results[0].Param);
            Assert.Equal(1, results[0].MessageId);
            Assert.False(results[0].HasProc);
            Assert.False(results[0].HasReaction);

            // Verify ToRecord snapshot
            var record = action.ToRecord();
            Assert.NotNull(record);
            Assert.Single(record.Targets);
            Assert.Equal(0x20002222u, record.Targets[0].TargetId);
            Assert.Single(record.Targets[0].Results);
            Assert.Equal(145, record.Targets[0].Results[0].Param);
        }

        [Fact]
        public void S2C_0x028_CombatAction_DecodesMultiTargetWithProcAndReaction()
        {
            byte[] payload = new byte[128];
            payload[0] = 50;

            var writer = new BitStreamWriter(payload.AsSpan(1));
            writer.WriteUInt32(0x10005555, 32);
            writer.WriteByte(2, 6); // 2 targets
            writer.WriteByte(0, 4);
            writer.WriteByte((byte)ActionCategory.SkillFinish, 4); // WeaponSkill finish
            writer.WriteUInt32(42, 32); // ActionId = 42
            writer.WriteUInt32(0, 32);

            // Target 0: Has additional effect proc (Fire damage)
            writer.WriteUInt32(0x20000001, 32);
            writer.WriteByte(1, 4);
            writer.WriteByte((byte)ActionResolution.Hit, 3);
            writer.WriteByte(1, 2);
            writer.WriteUInt16(200, 12);
            writer.WriteByte(0, 5);
            writer.WriteByte(0, 5);
            writer.WriteUInt32(500, 17); // 500 damage
            writer.WriteUInt16(185, 10);
            writer.WriteUInt32(0, 31);
            writer.WriteBool(true); // HasProc = true
            writer.WriteByte((byte)ActionProcAddEffect.FireDamage, 6);
            writer.WriteByte(1, 4);
            writer.WriteUInt32(35, 17); // 35 additional fire damage
            writer.WriteUInt16(163, 10);
            writer.WriteBool(false); // HasReact = false

            // Target 1: Has spikes reaction
            writer.WriteUInt32(0x20000002, 32);
            writer.WriteByte(1, 4);
            writer.WriteByte((byte)ActionResolution.Hit, 3);
            writer.WriteByte(0, 2);
            writer.WriteUInt16(200, 12);
            writer.WriteByte(0, 5);
            writer.WriteByte(0, 5);
            writer.WriteUInt32(480, 17);
            writer.WriteUInt16(185, 10);
            writer.WriteUInt32(0, 31);
            writer.WriteBool(false); // HasProc = false
            writer.WriteBool(true);  // HasReact = true
            writer.WriteByte((byte)ActionReactKind.BlazeSpikes, 6);
            writer.WriteByte(0, 4);
            writer.WriteUInt32(12, 14); // 12 spikes damage
            writer.WriteUInt16(44, 10);

            var action = new S2C_0x028_CombatAction(payload);
            Assert.True(action.IsValid);
            Assert.Equal(2, action.TargetCount);
            Assert.Equal(ActionCategory.SkillFinish, action.Category);
            Assert.Equal(42u, action.ActionId);

            var record = action.ToRecord();
            Assert.Equal(2, record.Targets.Count);

            // Verify Target 0 proc
            var res0 = record.Targets[0].Results[0];
            Assert.Equal(500, res0.Param);
            Assert.True(res0.HasProc);
            Assert.Equal(ActionProcAddEffect.FireDamage, res0.ProcKind);
            Assert.Equal(35, res0.ProcParam);
            Assert.False(res0.HasReaction);

            // Verify Target 1 reaction
            var res1 = record.Targets[1].Results[0];
            Assert.Equal(480, res1.Param);
            Assert.False(res1.HasProc);
            Assert.True(res1.HasReaction);
            Assert.Equal(ActionReactKind.BlazeSpikes, res1.ReactionKind);
            Assert.Equal(12, res1.ReactionParam);
        }

        [Fact]
        public void S2C_0x029_BattleMessage_DecodesStandardMessage()
        {
            byte[] payload = new byte[24];
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(0, 4), 0x11112222); // Caster
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(4, 4), 0x33334444); // Target
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(8, 4), 150);        // Data
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(12, 4), 250);       // Data2
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(16, 2), 10);        // Caster Index
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(18, 2), 20);        // Target Index
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(20, 2), 67);        // MessageNum
            payload[22] = 1;                                                            // Type

            var msg = new S2C_0x029_BattleMessage(payload);

            Assert.True(msg.IsValid);
            Assert.Equal(0x11112222u, msg.UniqueNoCas);
            Assert.Equal(0x33334444u, msg.UniqueNoTar);
            Assert.Equal(150u, msg.Data);
            Assert.Equal(250u, msg.Data2);
            Assert.Equal(10, msg.ActIndexCas);
            Assert.Equal(20, msg.ActIndexTar);
            Assert.Equal(67, msg.MessageNum);
            Assert.Equal(1, msg.Type);
        }

        [Fact]
        public void S2C_0x02D_BattleMessage2_DecodesProgressionMessage()
        {
            byte[] payload = new byte[24];
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(0, 4), 0x55556666); // Caster
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(4, 4), 0x77778888); // Target
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(8, 2), 15);         // Caster Index
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(10, 2), 25);        // Target Index
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(12, 4), 1200);      // Data (e.g. Exp)
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(16, 4), 3);         // Data2 (e.g. Chain)
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(20, 2), 371);       // MessageNum
            payload[22] = 2;                                                            // Type

            var msg = new S2C_0x02D_BattleMessage2(payload);

            Assert.True(msg.IsValid);
            Assert.Equal(0x55556666u, msg.UniqueNoCas);
            Assert.Equal(0x77778888u, msg.UniqueNoTar);
            Assert.Equal(15, msg.ActIndexCas);
            Assert.Equal(25, msg.ActIndexTar);
            Assert.Equal(1200u, msg.Data);
            Assert.Equal(3u, msg.Data2);
            Assert.Equal(371, msg.MessageNum);
            Assert.Equal(2, msg.Type);
        }

        [Fact]
        public void S2C_0x030_Effect_DecodesStatusEffect()
        {
            byte[] payload = new byte[12];
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(0, 4), 0x99887766);
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(4, 2), 42);
            BinaryPrimitives.WriteInt16LittleEndian(payload.AsSpan(6, 2), (short)SynthesisEffect.SynthesisSuccess);
            payload[8] = 5;
            payload[9] = 1;
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(10, 2), 30);

            var effect = new S2C_0x030_Effect(payload);

            Assert.True(effect.IsValid);
            Assert.Equal(0x99887766u, effect.UniqueNo);
            Assert.Equal(42, effect.ActIndex);
            Assert.Equal(SynthesisEffect.SynthesisSuccess, effect.EffectNum);
            Assert.Equal(5, effect.Type);
            Assert.Equal(1, effect.Status);
            Assert.Equal(30, effect.Timer);
        }

        [Fact]
        public void S2C_0x0AA_MagicData_DecodesLearnedSpells()
        {
            byte[] payload = new byte[128];
            // Set bit for Spell ID 1 (Cure): byte 0, bit 1
            payload[0] |= (1 << 1);
            // Set bit for Spell ID 57 (Protect): byte 7 (57 / 8), bit 1 (57 % 8)
            payload[7] |= (1 << 1);
            // Set bit for Spell ID 200: byte 25 (200 / 8), bit 0 (200 % 8)
            payload[25] |= (1 << 0);

            var magic = new S2C_0x0AA_MagicData(payload);

            Assert.True(magic.IsValid);
            Assert.True(magic.IsSpellLearned(1));
            Assert.False(magic.IsSpellLearned(2));
            Assert.True(magic.IsSpellLearned(57));
            Assert.False(magic.IsSpellLearned(58));
            Assert.True(magic.IsSpellLearned(200));

            // Test LocalPlayerState update
            var player = new LocalPlayerState();
            bool eventFired = false;
            player.MagicLearnedUpdated += () => eventFired = true;

            player.UpdateFromMagicData(magic);

            Assert.True(eventFired);
            Assert.True(player.HasSpell(1));
            Assert.False(player.HasSpell(2));
            Assert.True(player.HasSpell(57));
            Assert.True(player.HasSpell(200));
        }

        [Fact]
        public void S2C_0x0AC_CommandData_DecodesCommandsAndTraits()
        {
            byte[] payload = new byte[224];
            // WS 1 (Fast Blade): byte 0, bit 1
            payload[0] |= (1 << 1);
            // JA 5 (Provoke): byte 64 + (5 / 8), bit (5 % 8)
            payload[64] |= (1 << 5);
            // Pet 3: byte 128 + 0, bit 3
            payload[128] |= (1 << 3);
            // Trait 10: byte 192 + (10 / 8), bit (10 % 8) = byte 193, bit 2
            payload[193] |= (1 << 2);

            var cmd = new S2C_0x0AC_CommandData(payload);

            Assert.True(cmd.IsValid);
            Assert.True(cmd.HasWeaponSkill(1));
            Assert.False(cmd.HasWeaponSkill(2));
            Assert.True(cmd.HasJobAbility(5));
            Assert.False(cmd.HasJobAbility(6));
            Assert.True(cmd.HasPetAbility(3));
            Assert.True(cmd.HasTrait(10));

            // Test LocalPlayerState update
            var player = new LocalPlayerState();
            bool eventFired = false;
            player.CommandsUpdated += () => eventFired = true;

            player.UpdateFromCommandData(cmd);

            Assert.True(eventFired);
            Assert.True(player.HasWeaponSkill(1));
            Assert.True(player.HasJobAbility(5));
            Assert.True(player.HasPetAbility(3));
            Assert.True(player.HasTrait(10));
        }

        [Fact]
        public void S2C_0x119_AbilRecast_DecodesRecasts()
        {
            byte[] payload = new byte[256];
            // Recast 1: TimerId 5 (Provoke), 30 seconds
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(8, 2), 30);
            payload[11] = 5; // TimerId

            // Mount recast: 60 seconds, Id 1
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(248, 4), 60);
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(252, 4), 1);

            var recast = new S2C_0x119_AbilRecast(payload);

            Assert.True(recast.IsValid);
            var timer1 = recast.GetTimer(1);
            Assert.Equal(30, timer1.TimerSeconds);
            Assert.Equal(5, timer1.TimerId);
            Assert.Equal(60u, recast.MountRecast);
            Assert.Equal(1u, recast.MountRecastId);

            var combatState = new CombatState();
            combatState.UpdateRecasts(recast);

            Assert.Equal(30, combatState.GetAbilityRecast(5));
            Assert.Equal(60u, combatState.MountRecastSeconds);
        }

        [Fact]
        public void OutboundBuilders_BuildValidC2SPackets()
        {
            byte[] buffer = new byte[64];

            // 1. Attack request (0x01A)
            int len = CombatPacketBuilder.BuildAttackRequest(buffer, 10, 0x12345678, 42);
            Assert.Equal(28, len);
            Assert.True(PacketHeader.TryParse(buffer.AsSpan(0, 4), out var hdr));
            Assert.Equal(0x01A, hdr.PacketId);
            Assert.Equal(10, hdr.SequenceId);
            Assert.Equal(0x12345678u, BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(4, 4)));
            Assert.Equal(42, BinaryPrimitives.ReadUInt16LittleEndian(buffer.AsSpan(8, 2)));
            Assert.Equal((ushort)CliActionId.Attack, BinaryPrimitives.ReadUInt16LittleEndian(buffer.AsSpan(10, 2)));

            // 2. Cast magic request (0x01A)
            len = CombatPacketBuilder.BuildCastMagicRequest(buffer, 11, 0x12345678, 42, 1, new Vector3(10.5f, 20.5f, 30.5f));
            Assert.Equal(28, len);
            Assert.Equal((ushort)CliActionId.CastMagic, BinaryPrimitives.ReadUInt16LittleEndian(buffer.AsSpan(10, 2)));
            Assert.Equal(1u, BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(12, 4))); // SpellId
            Assert.Equal(10.5f, BinaryPrimitives.ReadSingleLittleEndian(buffer.AsSpan(16, 4))); // PosX
            Assert.Equal(30.5f, BinaryPrimitives.ReadSingleLittleEndian(buffer.AsSpan(20, 4))); // PosZ
            Assert.Equal(20.5f, BinaryPrimitives.ReadSingleLittleEndian(buffer.AsSpan(24, 4))); // PosY

            // 3. Emote request (0x05D)
            len = CombatPacketBuilder.BuildEmoteRequest(buffer, 12, 0x12345678, 42, EmoteId.Bow);
            Assert.Equal(16, len);
            Assert.True(PacketHeader.TryParse(buffer.AsSpan(0, 4), out hdr));
            Assert.Equal(0x05D, hdr.PacketId);
            Assert.Equal((byte)EmoteId.Bow, buffer[10]);

            // 4. Buff cancel request (0x0F1)
            len = CombatPacketBuilder.BuildBuffCancelRequest(buffer, 13, 43); // Protect
            Assert.Equal(8, len);
            Assert.True(PacketHeader.TryParse(buffer.AsSpan(0, 4), out hdr));
            Assert.Equal(0x0F1, hdr.PacketId);
            Assert.Equal(43, BinaryPrimitives.ReadUInt16LittleEndian(buffer.AsSpan(4, 2)));

            // 5. Jump request (0x11D)
            len = CombatPacketBuilder.BuildJumpRequest(buffer, 14, 0x11112222, 0);
            Assert.Equal(12, len);
            Assert.True(PacketHeader.TryParse(buffer.AsSpan(0, 4), out hdr));
            Assert.Equal(0x11D, hdr.PacketId);
            Assert.Equal(0x11112222u, BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(4, 4)));
        }

        [Fact]
        public async Task CombatPacketModule_OutboundDispatch_SendsAndUpdatesCombatState()
        {
            var combatState = new CombatState();
            var playerState = new LocalPlayerState { ServerId = 0x10000001 };
            ReadOnlyMemory<byte> lastSent = default;

            var module = new CombatPacketModule(
                combatState,
                playerState,
                (chunk, urgent) =>
                {
                    lastSent = chunk;
                    return Task.CompletedTask;
                });

            // Request attack
            await module.RequestAttackAsync(0x20000001, 100);
            Assert.True(combatState.IsEngaged);
            Assert.Equal(0x20000001u, combatState.TargetServerId);
            Assert.Equal(100, combatState.TargetIndex);
            Assert.False(lastSent.IsEmpty);

            // Request attack off
            await module.RequestAttackOffAsync(0x20000001, 100);
            Assert.False(combatState.IsEngaged);

            // Request cast
            await module.RequestCastMagicAsync(0x20000001, 100, 1);
            Assert.True(combatState.IsCasting);
            Assert.Equal(1, combatState.CastingSpellId);

            // Request jump
            await module.RequestJumpAsync();
            Assert.True(PacketHeader.TryParse(lastSent.Span.Slice(0, 4), out var hdr));
            Assert.Equal(0x11D, hdr.PacketId);
        }

        [Fact]
        public void ChatCommandRouter_ParsesCombatAndActionSlashCommands()
        {
            var world = new WorldState();
            var entity = new WorldEntity(0x20001234, 10, EntityType.Monster) { Name = "Goblin" };
            world.UpsertEntity(entity);

            // 1. /attack Goblin
            var res = ChatCommandRouter.Parse("/attack Goblin", ChatSendKind.Say, world);
            Assert.Equal(ChatCommandResultKind.CombatAttack, res.Kind);
            Assert.Equal(0x20001234u, res.TargetServerId);
            Assert.Equal(10, res.TargetIndex);

            // 2. /aoff
            res = ChatCommandRouter.Parse("/aoff");
            Assert.Equal(ChatCommandResultKind.CombatAttackOff, res.Kind);

            // 3. /magic "Cure IV" Goblin
            res = ChatCommandRouter.Parse("/magic \"Cure IV\" Goblin", ChatSendKind.Say, world);
            Assert.Equal(ChatCommandResultKind.CombatCast, res.Kind);
            Assert.Equal("Cure IV", res.Message);
            Assert.Equal(0x20001234u, res.TargetServerId);

            // 4. /ws "Fast Blade"
            res = ChatCommandRouter.Parse("/ws \"Fast Blade\"");
            Assert.Equal(ChatCommandResultKind.CombatWeaponskill, res.Kind);
            Assert.Equal("Fast Blade", res.Message);

            // 5. /ja "Provoke" Goblin
            res = ChatCommandRouter.Parse("/ja \"Provoke\" Goblin", ChatSendKind.Say, world);
            Assert.Equal(ChatCommandResultKind.CombatJobAbility, res.Kind);
            Assert.Equal("Provoke", res.Message);
            Assert.Equal(0x20001234u, res.TargetServerId);

            // 6. /cancel 43
            res = ChatCommandRouter.Parse("/cancel 43");
            Assert.Equal(ChatCommandResultKind.CombatBuffCancel, res.Kind);
            Assert.Equal(43, res.ActionParam);

            // 7. /jump
            res = ChatCommandRouter.Parse("/jump");
            Assert.Equal(ChatCommandResultKind.CombatJump, res.Kind);

            // 8. /bow Goblin
            res = ChatCommandRouter.Parse("/bow Goblin", ChatSendKind.Say, world);
            Assert.Equal(ChatCommandResultKind.Emote, res.Kind);
            Assert.Equal(EmoteId.Bow, res.Emote);
            Assert.Equal(0x20001234u, res.TargetServerId);
        }
    }
}
