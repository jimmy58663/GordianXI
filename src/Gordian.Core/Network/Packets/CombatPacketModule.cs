// src/Gordian.Core/Network/Packets/CombatPacketModule.cs
// Protocol specification referenced from LandSandBoat (https://github.com/LandSandBoat/server)
// and Atom0s XiPackets research (https://github.com/atom0s/XiPackets).

using System;
using System.Numerics;
using System.Threading.Tasks;
using Gordian.Core.Diagnostics;
using Gordian.Core.World;

namespace Gordian.Core.Network.Packets
{
    /// <summary>
    /// Packet domain module managing combat actions, auto-attack engagement, spell casting,
    /// abilities, weaponskills, emotes, status effects, and recast timers.
    /// Operates zero-allocation on the inbound socket pipeline.
    /// </summary>
    public sealed class CombatPacketModule
    {
        private readonly CombatState _combatState;
        private readonly LocalPlayerState _localPlayerState;
        private readonly Func<ReadOnlyMemory<byte>, bool, Task> _sendChunkCallback;
        private readonly Action<PacketDirection, ushort, ushort, ReadOnlySpan<byte>>? _logPacketCallback;
        private ushort _sequenceNumber;

        public CombatState State => _combatState;
        public bool LogOutboundOnRoute { get; set; } = true;

        public CombatPacketModule(
            CombatState combatState,
            LocalPlayerState localPlayerState,
            Func<ReadOnlyMemory<byte>, bool, Task> sendChunkCallback,
            Action<PacketDirection, ushort, ushort, ReadOnlySpan<byte>>? logPacketCallback = null)
        {
            _combatState = combatState ?? throw new ArgumentNullException(nameof(combatState));
            _localPlayerState = localPlayerState ?? throw new ArgumentNullException(nameof(localPlayerState));
            _sendChunkCallback = sendChunkCallback ?? throw new ArgumentNullException(nameof(sendChunkCallback));
            _logPacketCallback = logPacketCallback;
        }

        public void Register(IPacketDispatcher dispatcher)
        {
            ArgumentNullException.ThrowIfNull(dispatcher);

            dispatcher.Register(S2C_0x028_CombatAction.PacketId, HandleCombatAction);
            dispatcher.Register(S2C_0x029_BattleMessage.PacketId, HandleBattleMessage);
            dispatcher.Register(S2C_0x02D_BattleMessage2.PacketId, HandleBattleMessage2);
            dispatcher.Register(S2C_0x030_Effect.PacketId, HandleEffect);
            dispatcher.Register(S2C_0x0AA_MagicData.PacketId, HandleMagicData);
            dispatcher.Register(S2C_0x0AC_CommandData.PacketId, HandleCommandData);
            dispatcher.Register(S2C_0x119_AbilRecast.PacketId, HandleAbilRecast);
        }

        public void Unregister(IPacketDispatcher dispatcher)
        {
            ArgumentNullException.ThrowIfNull(dispatcher);

            dispatcher.Unregister(S2C_0x028_CombatAction.PacketId);
            dispatcher.Unregister(S2C_0x029_BattleMessage.PacketId);
            dispatcher.Unregister(S2C_0x02D_BattleMessage2.PacketId);
            dispatcher.Unregister(S2C_0x030_Effect.PacketId);
            dispatcher.Unregister(S2C_0x0AA_MagicData.PacketId);
            dispatcher.Unregister(S2C_0x0AC_CommandData.PacketId);
            dispatcher.Unregister(S2C_0x119_AbilRecast.PacketId);
        }

        #region Inbound Packet Handlers

        private void HandleCombatAction(PacketHeader header, ReadOnlySpan<byte> payload)
        {
            var action = new S2C_0x028_CombatAction(payload);
            if (!action.IsValid) return;

            GordianLog.Debug("COMBAT", $"Action 0x028: Actor={action.ActorId:X8}, Category={action.Category}, ActionId={action.ActionId}, Targets={action.TargetCount}");

            // Update casting state if local player finishes or starts a spell
            if (_localPlayerState.ServerId != 0 && action.ActorId == _localPlayerState.ServerId)
            {
                if (action.Category == ActionCategory.MagicStart)
                {
                    _combatState.StartCasting((ushort)action.ActionId);
                }
                else if (action.Category == ActionCategory.MagicFinish)
                {
                    _combatState.FinishCasting();
                }
            }

            var record = action.ToRecord();
            _combatState.RecordAction(record);
        }

        private void HandleBattleMessage(PacketHeader header, ReadOnlySpan<byte> payload)
        {
            var msg = new S2C_0x029_BattleMessage(payload);
            if (!msg.IsValid) return;

            GordianLog.Debug("COMBAT", $"BattleMessage 0x029: Caster={msg.UniqueNoCas:X8}, Target={msg.UniqueNoTar:X8}, MsgId={msg.MessageNum}, Data={msg.Data}");

            var record = new CombatMessageRecord
            {
                CasterId = msg.UniqueNoCas,
                TargetId = msg.UniqueNoTar,
                CasterIndex = msg.ActIndexCas,
                TargetIndex = msg.ActIndexTar,
                Param = msg.Data,
                Value = msg.Data2,
                MessageId = msg.MessageNum,
                MessageType = msg.Type,
                IsEndOfCombat = false
            };

            _combatState.RecordBattleMessage(record);
        }

        private void HandleBattleMessage2(PacketHeader header, ReadOnlySpan<byte> payload)
        {
            var msg = new S2C_0x02D_BattleMessage2(payload);
            if (!msg.IsValid) return;

            GordianLog.Debug("COMBAT", $"BattleMessage2 0x02D: Caster={msg.UniqueNoCas:X8}, Target={msg.UniqueNoTar:X8}, MsgId={msg.MessageNum}, Data={msg.Data}");

            var record = new CombatMessageRecord
            {
                CasterId = msg.UniqueNoCas,
                TargetId = msg.UniqueNoTar,
                CasterIndex = msg.ActIndexCas,
                TargetIndex = msg.ActIndexTar,
                Param = msg.Data,
                Value = msg.Data2,
                MessageId = msg.MessageNum,
                MessageType = msg.Type,
                IsEndOfCombat = true
            };

            _combatState.RecordBattleMessage(record);
        }

        private void HandleEffect(PacketHeader header, ReadOnlySpan<byte> payload)
        {
            var effect = new S2C_0x030_Effect(payload);
            if (!effect.IsValid) return;

            GordianLog.Debug("EFFECT", $"Effect 0x030: Target={effect.UniqueNo:X8}, EffectNum={effect.EffectNum}, Type={effect.Type}");
        }

        private void HandleMagicData(PacketHeader header, ReadOnlySpan<byte> payload)
        {
            var magic = new S2C_0x0AA_MagicData(payload);
            if (!magic.IsValid) return;

            GordianLog.Info("COMBAT", "Received 0x0AA Learned Magic Spell List update.");
            _localPlayerState.UpdateFromMagicData(magic);
        }

        private void HandleCommandData(PacketHeader header, ReadOnlySpan<byte> payload)
        {
            var cmd = new S2C_0x0AC_CommandData(payload);
            if (!cmd.IsValid) return;

            GordianLog.Info("COMBAT", "Received 0x0AC Command Data (Weaponskills, Abilities, Traits) update.");
            _localPlayerState.UpdateFromCommandData(cmd);
        }

        private void HandleAbilRecast(PacketHeader header, ReadOnlySpan<byte> payload)
        {
            var recast = new S2C_0x119_AbilRecast(payload);
            if (!recast.IsValid) return;

            GordianLog.Debug("COMBAT", $"Received 0x119 Ability Recasts update (MountRecast={recast.MountRecast}s).");
            _localPlayerState.UpdateFromAbilRecast(recast);
            _combatState.UpdateRecasts(recast);
        }

        #endregion

        #region Outbound Action Methods

        public async Task RequestAttackAsync(uint targetId, ushort targetIndex)
        {
            byte[] buffer = new byte[28];
            ushort seq = ++_sequenceNumber;
            int length = CombatPacketBuilder.BuildAttackRequest(buffer, seq, targetId, targetIndex);

            _combatState.Engage(targetId, targetIndex);
            LogOutbound(0x01A, seq, buffer.AsSpan(4, length - 4));
            await _sendChunkCallback(buffer.AsMemory(0, length), false).ConfigureAwait(false);
        }

        public async Task RequestAttackOffAsync(uint targetId, ushort targetIndex)
        {
            byte[] buffer = new byte[28];
            ushort seq = ++_sequenceNumber;
            int length = CombatPacketBuilder.BuildAttackOffRequest(buffer, seq, targetId, targetIndex);

            _combatState.Disengage();
            LogOutbound(0x01A, seq, buffer.AsSpan(4, length - 4));
            await _sendChunkCallback(buffer.AsMemory(0, length), false).ConfigureAwait(false);
        }

        public async Task RequestCastMagicAsync(uint targetId, ushort targetIndex, ushort spellId, Vector3 targetPos = default)
        {
            byte[] buffer = new byte[28];
            ushort seq = ++_sequenceNumber;
            int length = CombatPacketBuilder.BuildCastMagicRequest(buffer, seq, targetId, targetIndex, spellId, targetPos);

            _combatState.StartCasting(spellId);
            LogOutbound(0x01A, seq, buffer.AsSpan(4, length - 4));
            await _sendChunkCallback(buffer.AsMemory(0, length), false).ConfigureAwait(false);
        }

        public async Task RequestWeaponskillAsync(uint targetId, ushort targetIndex, ushort wsId)
        {
            byte[] buffer = new byte[28];
            ushort seq = ++_sequenceNumber;
            int length = CombatPacketBuilder.BuildWeaponskillRequest(buffer, seq, targetId, targetIndex, wsId);

            LogOutbound(0x01A, seq, buffer.AsSpan(4, length - 4));
            await _sendChunkCallback(buffer.AsMemory(0, length), false).ConfigureAwait(false);
        }

        public async Task RequestJobAbilityAsync(uint targetId, ushort targetIndex, ushort abilityId)
        {
            byte[] buffer = new byte[28];
            ushort seq = ++_sequenceNumber;
            int length = CombatPacketBuilder.BuildJobAbilityRequest(buffer, seq, targetId, targetIndex, abilityId);

            LogOutbound(0x01A, seq, buffer.AsSpan(4, length - 4));
            await _sendChunkCallback(buffer.AsMemory(0, length), false).ConfigureAwait(false);
        }

        public async Task RequestShootAsync(uint targetId, ushort targetIndex)
        {
            byte[] buffer = new byte[28];
            ushort seq = ++_sequenceNumber;
            int length = CombatPacketBuilder.BuildShootRequest(buffer, seq, targetId, targetIndex);

            LogOutbound(0x01A, seq, buffer.AsSpan(4, length - 4));
            await _sendChunkCallback(buffer.AsMemory(0, length), false).ConfigureAwait(false);
        }

        public async Task RequestAssistAsync(uint targetId, ushort targetIndex)
        {
            byte[] buffer = new byte[28];
            ushort seq = ++_sequenceNumber;
            int length = CombatPacketBuilder.BuildAssistRequest(buffer, seq, targetId, targetIndex);

            LogOutbound(0x01A, seq, buffer.AsSpan(4, length - 4));
            await _sendChunkCallback(buffer.AsMemory(0, length), false).ConfigureAwait(false);
        }

        public async Task RequestMountAsync(uint mountId)
        {
            byte[] buffer = new byte[28];
            ushort seq = ++_sequenceNumber;
            int length = CombatPacketBuilder.BuildMountRequest(buffer, seq, mountId);

            LogOutbound(0x01A, seq, buffer.AsSpan(4, length - 4));
            await _sendChunkCallback(buffer.AsMemory(0, length), false).ConfigureAwait(false);
        }

        public async Task RequestDismountAsync()
        {
            byte[] buffer = new byte[28];
            ushort seq = ++_sequenceNumber;
            int length = CombatPacketBuilder.BuildDismountRequest(buffer, seq);

            LogOutbound(0x01A, seq, buffer.AsSpan(4, length - 4));
            await _sendChunkCallback(buffer.AsMemory(0, length), false).ConfigureAwait(false);
        }

        public async Task RequestEmoteAsync(uint targetId, ushort targetIndex, EmoteId emoteId, byte mode = 0, ushort param = 0)
        {
            byte[] buffer = new byte[16];
            ushort seq = ++_sequenceNumber;
            int length = CombatPacketBuilder.BuildEmoteRequest(buffer, seq, targetId, targetIndex, emoteId, mode, param);

            LogOutbound(0x05D, seq, buffer.AsSpan(4, length - 4));
            await _sendChunkCallback(buffer.AsMemory(0, length), false).ConfigureAwait(false);
        }

        public async Task RequestBuffCancelAsync(ushort buffId)
        {
            byte[] buffer = new byte[8];
            ushort seq = ++_sequenceNumber;
            int length = CombatPacketBuilder.BuildBuffCancelRequest(buffer, seq, buffId);

            LogOutbound(0x0F1, seq, buffer.AsSpan(4, length - 4));
            await _sendChunkCallback(buffer.AsMemory(0, length), false).ConfigureAwait(false);
        }

        public async Task RequestJumpAsync()
        {
            byte[] buffer = new byte[12];
            ushort seq = ++_sequenceNumber;
            uint playerId = _localPlayerState.ServerId;
            ushort playerIndex = 0; // zone target index
            int length = CombatPacketBuilder.BuildJumpRequest(buffer, seq, playerId, playerIndex);

            LogOutbound(0x11D, seq, buffer.AsSpan(4, length - 4));
            await _sendChunkCallback(buffer.AsMemory(0, length), false).ConfigureAwait(false);
        }

        private void LogOutbound(ushort packetId, ushort sequenceId, ReadOnlySpan<byte> payload)
        {
            if (LogOutboundOnRoute)
            {
                _logPacketCallback?.Invoke(PacketDirection.Outbound, packetId, sequenceId, payload);
            }
        }

        #endregion
    }
}
