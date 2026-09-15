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
            dispatcher.Register(S2C_0x0E0_GroupComlink.PacketId, HandleGroupComlink);
            dispatcher.Register(S2C_0x0E2_GroupList2.PacketId, HandleGroupList2);
            dispatcher.Register(S2C_0x11D_PartyReq.PacketId, HandlePartyReq);
        }

        public void Unregister(IPacketDispatcher dispatcher)
        {
            ArgumentNullException.ThrowIfNull(dispatcher);

            dispatcher.Unregister(S2C_0x0DC_GroupSolicitReq.PacketId);
            dispatcher.Unregister(S2C_0x0DE_GroupSolicitNo.PacketId);
            dispatcher.Unregister(S2C_0x0C8_GroupTbl.PacketId);
            dispatcher.Unregister(S2C_0x0DD_GroupList.PacketId);
            dispatcher.Unregister(S2C_0x0E0_GroupComlink.PacketId);
            dispatcher.Unregister(S2C_0x0E2_GroupList2.PacketId);
            dispatcher.Unregister(S2C_0x11D_PartyReq.PacketId);
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

        /// <summary>
        /// Requests updated party member list (C2S 0x076).
        /// </summary>
        public async Task SendGroupListReqAsync(byte kind = 0)
        {
            ushort seq = ++_sequenceNumber;
            byte[] packet = PartyPacketBuilder.BuildGroupListReq(kind, seq);
            if (LogOutboundOnRoute)
            {
                _logPacketCallback?.Invoke(PacketDirection.Outbound, 0x076, seq, packet);
            }
            await _sendChunkCallback(packet, true).ConfigureAwait(false);
            GordianLog.Debug("PARTY", $"Sent GroupListReq (Kind={kind})");
        }

        /// <summary>
        /// Changes group settings, e.g. set party leader or level sync (C2S 0x077).
        /// </summary>
        public async Task SendGroupChange2Async(string name, byte kind, byte changeKind)
        {
            ushort seq = ++_sequenceNumber;
            byte[] packet = PartyPacketBuilder.BuildGroupChange2(name, kind, changeKind, seq);
            if (LogOutboundOnRoute)
            {
                _logPacketCallback?.Invoke(PacketDirection.Outbound, 0x077, seq, packet);
            }
            await _sendChunkCallback(packet, true).ConfigureAwait(false);
            GordianLog.Info("PARTY", $"Sent GroupChange2: Name='{name}', Kind={kind}, ChangeKind={changeKind}");
        }

        /// <summary>
        /// Requests to join target player's party (/partyrequestcmd) (C2S 0x11C).
        /// </summary>
        public async Task SendPartyRequestAsync(uint targetServerId, ushort targetIndex, byte kind = 0)
        {
            ushort seq = ++_sequenceNumber;
            byte[] packet = PartyPacketBuilder.BuildPartyRequest(targetServerId, targetIndex, kind, seq);
            if (LogOutboundOnRoute)
            {
                _logPacketCallback?.Invoke(PacketDirection.Outbound, 0x11C, seq, packet);
            }
            await _sendChunkCallback(packet, true).ConfigureAwait(false);
            GordianLog.Info("PARTY", $"Sent PartyRequest: TargetServerId=0x{targetServerId:X8}, Kind={kind}");
        }

        private void HandleGroupComlink(PacketHeader header, ReadOnlySpan<byte> payload)
        {
            var comlink = new S2C_0x0E0_GroupComlink(payload);
            if (!comlink.IsValid) return;

            GordianLog.Debug("PARTY", $"Group Comlink update: LinkshellNum={comlink.LinkshellNum}, ItemIndex={comlink.ItemIndex}");
        }

        private void HandleGroupList2(PacketHeader header, ReadOnlySpan<byte> payload)
        {
            var list2 = new S2C_0x0E2_GroupList2(payload);
            if (!list2.IsValid) return;

            string name = list2.GetName();
            var member = new PartyMember
            {
                ServerId = list2.UniqueNo,
                TargetIndex = list2.ActIndex,
                Name = name,
                Hp = list2.Hp,
                Mp = list2.Mp,
                Tp = list2.Tp,
                Hpp = list2.Hpp,
                Mpp = list2.Mpp,
                ZoneId = list2.ZoneNo,
                MainJob = list2.MainJob,
                MainJobLevel = list2.MainJobLevel,
                SubJob = list2.SubJob,
                SubJobLevel = list2.SubJobLevel,
                IsLeader = (list2.GAttr & 0x04) != 0,
                MemberNumber = list2.MemberNumber
            };

            GordianLog.Debug("PARTY", $"Group member (List2) update: '{name}' (Job: {list2.MainJob}{list2.MainJobLevel})");
            _partyState.UpsertMember(member);
        }

        private void HandlePartyReq(PacketHeader header, ReadOnlySpan<byte> payload)
        {
            var req = new S2C_0x11D_PartyReq(payload);
            if (!req.IsValid) return;

            GordianLog.Info("PARTY", $"PartyReq notification received: ServerId=0x{req.UniqueNo:X8}, Result={req.Result}");
        }
    }
}
