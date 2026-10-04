// src/Gordian.Core/Network/Packets/PlayerCommandPacketModule.cs
using System;
using System.Buffers.Binary;
using System.Numerics;
using System.Threading.Tasks;
using Gordian.Core.Diagnostics;
using Gordian.Core.World;

namespace Gordian.Core.Network.Packets
{
    /// <summary>
    /// Packet domain module for the everyday commands: <c>/heal</c> (C2S 0x0E8), <c>/sit</c> (0x0EA), <c>/sitchair</c>
    /// (0x113), <c>/random</c> (0x0A2), proposals and votes (0x0A0 / 0x0A1, answered by S2C 0x078 / 0x079), wide scan
    /// (0x0F4-0x0F6, answered by S2C 0x0F4-0x0F6), the emote list (0x119, answered by 0x11A), the synthesis effect end
    /// (0x059) and other players' <c>/jump</c> (S2C 0x11E). Decoded data goes to <see cref="PlayerCommandState"/>.
    /// </summary>
    public sealed class PlayerCommandPacketModule
    {
        private readonly PlayerCommandState _state;
        private readonly WorldState _world;
        private readonly Func<ReadOnlyMemory<byte>, bool, Task> _sendChunkCallback;
        private readonly Action<PacketDirection, ushort, ushort, ReadOnlySpan<byte>>? _logPacketCallback;
        private ushort _sequenceNumber;

        public PlayerCommandState State => _state;
        public bool LogOutboundOnRoute { get; set; } = true;

        public PlayerCommandPacketModule(
            PlayerCommandState state,
            WorldState world,
            Func<ReadOnlyMemory<byte>, bool, Task> sendChunkCallback,
            Action<PacketDirection, ushort, ushort, ReadOnlySpan<byte>>? logPacketCallback = null)
        {
            _state = state ?? throw new ArgumentNullException(nameof(state));
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _sendChunkCallback = sendChunkCallback ?? throw new ArgumentNullException(nameof(sendChunkCallback));
            _logPacketCallback = logPacketCallback;
        }

        public void Register(IPacketDispatcher dispatcher)
        {
            ArgumentNullException.ThrowIfNull(dispatcher);
            dispatcher.Register(S2C_0x078_SwitchStart.PacketId, HandleSwitchStart);
            dispatcher.Register(S2C_0x079_SwitchProc.PacketId, HandleSwitchProc);
            dispatcher.Register(S2C_0x0F4_TrackingList.PacketId, HandleTrackingList);
            dispatcher.Register(S2C_0x0F5_TrackingPos.PacketId, HandleTrackingPos);
            dispatcher.Register(S2C_0x0F6_TrackingState.PacketId, HandleTrackingState);
            dispatcher.Register(S2C_0x11A_EmoteList.PacketId, HandleEmoteList);
            dispatcher.Register(S2C_0x11E_Jump.PacketId, HandleJump);
        }

        public void Unregister(IPacketDispatcher dispatcher)
        {
            ArgumentNullException.ThrowIfNull(dispatcher);
            dispatcher.Unregister(S2C_0x078_SwitchStart.PacketId);
            dispatcher.Unregister(S2C_0x079_SwitchProc.PacketId);
            dispatcher.Unregister(S2C_0x0F4_TrackingList.PacketId);
            dispatcher.Unregister(S2C_0x0F5_TrackingPos.PacketId);
            dispatcher.Unregister(S2C_0x0F6_TrackingState.PacketId);
            dispatcher.Unregister(S2C_0x11A_EmoteList.PacketId);
            dispatcher.Unregister(S2C_0x11E_Jump.PacketId);
        }

        #region Inbound

        private void HandleSwitchStart(PacketHeader header, ReadOnlySpan<byte> payload)
        {
            var start = new S2C_0x078_SwitchStart(payload);
            if (!start.IsValid) return;
            GordianLog.Debug("VOTE", $"Proposal 0x078: proposer='{start.ProposerName}' kind={start.Kind} text='{start.Text.Replace('\n', '|')}'");
            _state.Votes.ApplyStart(start.ProposerName, start.Kind, start.Text);
        }

        private void HandleSwitchProc(PacketHeader header, ReadOnlySpan<byte> payload)
        {
            var proc = new S2C_0x079_SwitchProc(payload);
            if (!proc.IsValid) return;

            Span<int> votes = stackalloc int[S2C_0x079_SwitchProc.VoteSlots];
            for (int i = 1; i < S2C_0x079_SwitchProc.VoteSlots; i++) votes[i] = proc.GetVotes(i);
            GordianLog.Debug("VOTE", $"Tally 0x079: proposer='{proc.ProposerName}' closed={proc.Closed} options={Math.Max(0, proc.QuestionNum - 1)} " +
                $"votes=[{votes[1]},{votes[2]},{votes[3]},{votes[4]},{votes[5]},{votes[6]},{votes[7]},{votes[8]}]");
            _state.Votes.ApplyProc(proc.ProposerName, proc.Kind, proc.Closed, Math.Max(0, proc.QuestionNum - 1), votes, proc.Text);
        }

        private void HandleTrackingList(PacketHeader header, ReadOnlySpan<byte> payload)
        {
            var entry = new S2C_0x0F4_TrackingList(payload);
            if (!entry.IsValid) return;
            _state.WideScan.AddEntry(new WideScanEntry(entry.ActIndex, entry.Level, entry.Type, entry.DeltaX, entry.DeltaZ, entry.Name));
        }

        private void HandleTrackingPos(PacketHeader header, ReadOnlySpan<byte> payload)
        {
            var pos = new S2C_0x0F5_TrackingPos(payload);
            if (!pos.IsValid) return;
            GordianLog.Debug("WIDESCAN", $"Tracking 0x0F5: index={pos.ActIndex} state={pos.State} level={pos.Level} pos=({pos.X:F1},{pos.Y:F1},{pos.Z:F1})");
            _state.WideScan.ApplyTrack(new WideScanTrack(pos.ActIndex, pos.Level, new Vector3(pos.X, pos.Y, pos.Z), pos.State));
        }

        private void HandleTrackingState(PacketHeader header, ReadOnlySpan<byte> payload)
        {
            var state = new S2C_0x0F6_TrackingState(payload);
            if (!state.IsValid) return;
            GordianLog.Debug("WIDESCAN", $"List state 0x0F6: {state.State} ({_state.WideScan.Snapshot().Count} entries so far)");
            _state.WideScan.ApplyListState(state.State);
        }

        private void HandleEmoteList(PacketHeader header, ReadOnlySpan<byte> payload)
        {
            var list = new S2C_0x11A_EmoteList(payload);
            if (!list.IsValid) return;
            GordianLog.Debug("EMOTE", $"Emote list 0x11A: jobEmotes=0x{list.JobEmotes:X8} chairs=0x{list.Chairs:X4}");
            _state.Emotes.Apply(list.JobEmotes, list.Chairs);
        }

        private void HandleJump(PacketHeader header, ReadOnlySpan<byte> payload)
        {
            var jump = new S2C_0x11E_Jump(payload);
            if (!jump.IsValid) return;
            if (_world.TryGetByTargetIndex(jump.ActIndex, out var entity) && entity != null)
            {
                entity.NotifyJump();
                GordianLog.Debug("ENTITY", $"Jump 0x11E: '{entity.Name}' (index {jump.ActIndex}) jumped (count {entity.JumpCount}).");
            }
            else
            {
                GordianLog.Debug("ENTITY", $"Jump 0x11E: unknown entity index {jump.ActIndex}.");
            }
        }

        #endregion

        #region Outbound

        private ushort NextSequence() => unchecked(++_sequenceNumber);

        private Task Send(ushort opcode, byte[] packet)
        {
            LogOutbound(opcode, packet);
            return _sendChunkCallback(packet, true);
        }

        /// <summary>Sends C2S 0x0E8: <c>/heal</c>. The server answers with the Healing status (S2C 0x037 / 0x063).</summary>
        public Task SendHealAsync(RestMode mode = RestMode.Toggle) => Send(0x0E8, PlayerCommandPacketBuilder.BuildCamp(mode, NextSequence()));

        /// <summary>Sends C2S 0x0EA: <c>/sit</c>. The server answers with an updated animation (S2C 0x037 / 0x00D).</summary>
        public Task SendSitAsync(RestMode mode = RestMode.Toggle) => Send(0x0EA, PlayerCommandPacketBuilder.BuildSit(mode, NextSequence()));

        /// <summary>Sends C2S 0x113: <c>/sitchair</c> (chair 0 is the plain chair).</summary>
        public Task SendSitChairAsync(uint chairId, RestMode mode = RestMode.Toggle) => Send(0x113, PlayerCommandPacketBuilder.BuildSitChair(mode, chairId, NextSequence()));

        /// <summary>Sends C2S 0x0A2: <c>/random</c>. The server answers with S2C 0x009 message 88 to you and everyone near.</summary>
        public Task SendRandomAsync(uint typedNumber = 0) => Send(0x0A2, PlayerCommandPacketBuilder.BuildDice(typedNumber, NextSequence()));

        /// <summary>Sends C2S 0x0A0: a proposal (the raw question and options text); empty text cancels the sender's live one.</summary>
        public Task SendProposalAsync(ProposalKind kind, string text) => Send(0x0A0, PlayerCommandPacketBuilder.BuildProposal(kind, text, NextSequence()));

        /// <summary>Sends C2S 0x0A1: a vote for option <paramref name="optionIndex"/> (1 to 8) in the proposal of <paramref name="proposerName"/>.</summary>
        public Task SendVoteAsync(byte optionIndex, string proposerName) => Send(0x0A1, PlayerCommandPacketBuilder.BuildVote(optionIndex, proposerName, NextSequence()));

        /// <summary>Sends C2S 0x0F4: asks for the wide scan list (S2C 0x0F6, 0x0F4 entries, 0x0F6).</summary>
        public Task SendWideScanAsync() => Send(0x0F4, PlayerCommandPacketBuilder.BuildTrackingList(NextSequence()));

        /// <summary>Sends C2S 0x0F5: tracks the entity at <paramref name="actIndex"/> (answered by S2C 0x0F5 updates).</summary>
        public Task SendTrackingStartAsync(ushort actIndex) => Send(0x0F5, PlayerCommandPacketBuilder.BuildTrackingStart(actIndex, NextSequence()));

        /// <summary>Sends C2S 0x0F6: stops tracking.</summary>
        public Task SendTrackingEndAsync() => Send(0x0F6, PlayerCommandPacketBuilder.BuildTrackingEnd(NextSequence()));

        /// <summary>Sends C2S 0x119: asks for the unlocked job emotes and chairs (S2C 0x11A).</summary>
        public Task SendEmoteListRequestAsync() => Send(0x119, PlayerCommandPacketBuilder.BuildEmoteListRequest(NextSequence()));

        /// <summary>Sends C2S 0x059: the synthesis effect finished (0) or was abandoned (1). Nothing sends it until synthesis plays animations (#112).</summary>
        public Task SendEffectEndAsync(uint effectPara = 0) => Send(0x059, PlayerCommandPacketBuilder.BuildEffectEnd(effectPara, NextSequence()));

        private void LogOutbound(ushort packetId, ReadOnlySpan<byte> packet)
        {
            if (LogOutboundOnRoute && _logPacketCallback != null)
            {
                ushort seq = packet.Length >= 4 ? BinaryPrimitives.ReadUInt16LittleEndian(packet.Slice(2, 2)) : (ushort)0;
                var payload = packet.Length >= 4 ? packet.Slice(4) : ReadOnlySpan<byte>.Empty;
                _logPacketCallback(PacketDirection.Outbound, packetId, seq, payload);
            }
        }

        #endregion
    }
}
