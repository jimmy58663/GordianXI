// src/Gordian.Core/Network/Packets/PartyPacketModule.cs
using System;
using System.Threading.Tasks;
using Gordian.Core.Diagnostics;
using Gordian.Core.World;

namespace Gordian.Core.Network.Packets
{
    /// <summary>
    /// Packet domain module managing Party &amp; Alliance networking, invites, member lists, and commands.
    /// Operates zero-allocation in the packet handling loop.
    /// </summary>
    public sealed class PartyPacketModule
    {
        private readonly PartyState _partyState;
        private readonly Func<ReadOnlyMemory<byte>, bool, Task> _sendChunkCallback;
        private readonly Action<PacketDirection, ushort, ushort, ReadOnlySpan<byte>>? _logPacketCallback;
        private ushort _sequenceNumber;

        public PartyState State => _partyState;
        public bool LogOutboundOnRoute { get; set; } = true;

        public PartyPacketModule(
            PartyState partyState,
            Func<ReadOnlyMemory<byte>, bool, Task> sendChunkCallback,
            Action<PacketDirection, ushort, ushort, ReadOnlySpan<byte>>? logPacketCallback = null)
        {
            _partyState = partyState ?? throw new ArgumentNullException(nameof(partyState));
            _sendChunkCallback = sendChunkCallback ?? throw new ArgumentNullException(nameof(sendChunkCallback));
            _logPacketCallback = logPacketCallback;
        }

        public void Register(IPacketDispatcher dispatcher)
        {
            ArgumentNullException.ThrowIfNull(dispatcher);

            dispatcher.Register(S2C_0x0DC_GroupSolicitReq.PacketId, HandleGroupSolicitReq);
            dispatcher.Register(S2C_0x0DE_GroupSolicitNo.PacketId, HandleGroupSolicitNo);
            dispatcher.Register(S2C_0x0C8_GroupTbl.PacketId, HandleGroupTbl);
            dispatcher.Register(S2C_0x0DD_GroupList.PacketId, HandleGroupList);
        }

        public void Unregister(IPacketDispatcher dispatcher)
        {
            ArgumentNullException.ThrowIfNull(dispatcher);

            dispatcher.Unregister(S2C_0x0DC_GroupSolicitReq.PacketId);
            dispatcher.Unregister(S2C_0x0DE_GroupSolicitNo.PacketId);
            dispatcher.Unregister(S2C_0x0C8_GroupTbl.PacketId);
            dispatcher.Unregister(S2C_0x0DD_GroupList.PacketId);
        }

        private void HandleGroupSolicitReq(PacketHeader header, ReadOnlySpan<byte> payload)
        {
            var req = new S2C_0x0DC_GroupSolicitReq(payload);
            if (!req.IsValid) return;

            string inviterName = req.GetInviterName();
            var invite = new PartyInvite(
                InviterId: req.UniqueNo,
                InviterTargetIndex: req.ActIndex,
                InviterName: inviterName,
                Kind: req.Kind,
                Timestamp: DateTime.UtcNow
            );

            GordianLog.Info("PARTY", $"Received party invite from '{inviterName}' (ID: 0x{req.UniqueNo:X8}, TargIdx: {req.ActIndex}, Kind: {req.Kind})");
            _partyState.SetPendingInvite(invite);
        }

        private void HandleGroupSolicitNo(PacketHeader header, ReadOnlySpan<byte> payload)
        {
            var no = new S2C_0x0DE_GroupSolicitNo(payload);
            GordianLog.Info("PARTY", $"Party invite cleared/reset (Reason: {no.Reason})");
            _partyState.ClearPendingInvite();
        }

        private void HandleGroupTbl(PacketHeader header, ReadOnlySpan<byte> payload)
        {
            var tbl = new S2C_0x0C8_GroupTbl(payload);
            if (!tbl.IsValid) return;

            GordianLog.Debug("PARTY", $"Group table received with {tbl.EntryCount} members (Kind: {tbl.Kind})");
            if (tbl.EntryCount == 0)
            {
                _partyState.ClearParty();
                return;
            }

            for (int i = 0; i < tbl.EntryCount; i++)
            {
                uint id = tbl.GetEntryUniqueNo(i);
                if (id == 0) continue;

                ushort targetIndex = tbl.GetEntryActIndex(i);
                bool isLeader = tbl.IsEntryLeader(i);
                ushort zoneId = tbl.GetEntryZoneNo(i);

                _partyState.UpsertMember(new PartyMember
                {
                    ServerId = id,
                    TargetIndex = targetIndex,
                    IsLeader = isLeader,
                    ZoneId = zoneId,
                    MemberNumber = (byte)i
                });
            }
        }

        private void HandleGroupList(PacketHeader header, ReadOnlySpan<byte> payload)
        {
            var list = new S2C_0x0DD_GroupList(payload);
            if (!list.IsValid) return;

            string name = list.GetName();
            var member = new PartyMember
            {
                ServerId = list.UniqueNo,
                TargetIndex = list.ActIndex,
                Name = name,
                Hp = list.Hp,
                Mp = list.Mp,
                Tp = list.Tp,
                Hpp = list.Hpp,
                Mpp = list.Mpp,
                ZoneId = list.ZoneNo,
                MainJob = list.MainJob,
                MainJobLevel = list.MainJobLevel,
                SubJob = list.SubJob,
                SubJobLevel = list.SubJobLevel,
                IsLeader = list.IsPartyLeader,
                MemberNumber = list.MemberNumber
            };

            GordianLog.Debug("PARTY", $"Group member update: '{name}' (HP: {list.Hp}, MP: {list.Mp}, TP: {list.Tp}, Job: {list.MainJob}{list.MainJobLevel})");
            _partyState.UpsertMember(member);
        }

        /// <summary>
        /// Sends an outbound party or alliance invite (C2S 0x06E).
        /// </summary>
        public async Task SendInviteAsync(uint targetServerId, ushort targetIndex, PartyKind kind = PartyKind.Party)
        {
            ushort seq = ++_sequenceNumber;
            byte[] packet = PartyPacketBuilder.BuildGroupSolicitReq(targetServerId, targetIndex, kind, seq);
            if (LogOutboundOnRoute)
            {
                _logPacketCallback?.Invoke(PacketDirection.Outbound, 0x06E, seq, packet);
            }
            await _sendChunkCallback(packet, true).ConfigureAwait(false);
            GordianLog.Info("PARTY", $"Sent party invite to ServerId: 0x{targetServerId:X8}, Index: {targetIndex}");
        }

        /// <summary>
        /// Responds to a pending party or alliance invite by accepting (C2S 0x074).
        /// </summary>
        public async Task AcceptInviteAsync()
        {
            ushort seq = ++_sequenceNumber;
            byte[] packet = PartyPacketBuilder.BuildGroupSolicitRes(accept: true, seq);
            if (LogOutboundOnRoute)
            {
                _logPacketCallback?.Invoke(PacketDirection.Outbound, 0x074, seq, packet);
            }
            await _sendChunkCallback(packet, true).ConfigureAwait(false);
            _partyState.ClearPendingInvite();
            GordianLog.Info("PARTY", "Accepted party invitation.");
        }

        /// <summary>
        /// Responds to a pending party or alliance invite by declining (C2S 0x074).
        /// </summary>
        public async Task DeclineInviteAsync()
        {
            ushort seq = ++_sequenceNumber;
            byte[] packet = PartyPacketBuilder.BuildGroupSolicitRes(accept: false, seq);
            if (LogOutboundOnRoute)
            {
                _logPacketCallback?.Invoke(PacketDirection.Outbound, 0x074, seq, packet);
            }
            await _sendChunkCallback(packet, true).ConfigureAwait(false);
            _partyState.ClearPendingInvite();
            GordianLog.Info("PARTY", "Declined party invitation.");
        }

        /// <summary>
        /// Leaves the current party or alliance (C2S 0x06F).
        /// </summary>
        public async Task LeavePartyAsync(PartyKind kind = PartyKind.Party)
        {
            ushort seq = ++_sequenceNumber;
            byte[] packet = PartyPacketBuilder.BuildGroupLeave(kind, seq);
            if (LogOutboundOnRoute)
            {
                _logPacketCallback?.Invoke(PacketDirection.Outbound, 0x06F, seq, packet);
            }
            await _sendChunkCallback(packet, true).ConfigureAwait(false);
            _partyState.ClearParty();
            GordianLog.Info("PARTY", "Left party.");
        }

        /// <summary>
        /// Disbands the current party or alliance (C2S 0x070).
        /// </summary>
        public async Task DisbandPartyAsync(PartyKind kind = PartyKind.Party)
        {
            ushort seq = ++_sequenceNumber;
            byte[] packet = PartyPacketBuilder.BuildGroupBreakup(kind, seq);
            if (LogOutboundOnRoute)
            {
                _logPacketCallback?.Invoke(PacketDirection.Outbound, 0x070, seq, packet);
            }
            await _sendChunkCallback(packet, true).ConfigureAwait(false);
            _partyState.ClearParty();
            GordianLog.Info("PARTY", "Disbanded party.");
        }

        /// <summary>
        /// Kicks / strikes a member from the party or alliance (C2S 0x071).
        /// </summary>
        public async Task KickMemberAsync(uint targetServerId, ushort targetIndex, string name, byte kind = 0)
        {
            ushort seq = ++_sequenceNumber;
            byte[] packet = PartyPacketBuilder.BuildGroupStrike(targetServerId, targetIndex, name, kind, seq);
            if (LogOutboundOnRoute)
            {
                _logPacketCallback?.Invoke(PacketDirection.Outbound, 0x071, seq, packet);
            }
            await _sendChunkCallback(packet, true).ConfigureAwait(false);
            _partyState.RemoveMember(targetServerId);
            GordianLog.Info("PARTY", $"Kicked member '{name}' (ID: 0x{targetServerId:X8}) from party.");
        }
    }
}
