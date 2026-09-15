// src/Gordian.Core/World/PartyState.cs
using System;
using System.Collections.Generic;
using System.Linq;
using Gordian.Core.Network.Packets;

namespace Gordian.Core.World
{
    /// <summary>
    /// Details of an incoming pending Party or Alliance invite.
    /// </summary>
    public sealed record PartyInvite(
        uint InviterId,
        ushort InviterTargetIndex,
        string InviterName,
        PartyKind Kind,
        DateTime Timestamp
    );

    /// <summary>
    /// Represents an active Party Member in the player's current party or alliance.
    /// </summary>
    public sealed class PartyMember
    {
        public uint ServerId { get; set; }
        public ushort TargetIndex { get; set; }
        public string Name { get; set; } = string.Empty;
        public uint Hp { get; set; }
        public uint Mp { get; set; }
        public uint Tp { get; set; }
        public byte Hpp { get; set; }
        public byte Mpp { get; set; }
        public ushort ZoneId { get; set; }
        public JobId MainJob { get; set; } = JobId.None;
        public byte MainJobLevel { get; set; }
        public JobId SubJob { get; set; } = JobId.None;
        public byte SubJobLevel { get; set; }
        public bool IsLeader { get; set; }
        public byte MemberNumber { get; set; }

        public PartyMember Clone()
        {
            return new PartyMember
            {
                ServerId = ServerId,
                TargetIndex = TargetIndex,
                Name = Name,
                Hp = Hp,
                Mp = Mp,
                Tp = Tp,
                Hpp = Hpp,
                Mpp = Mpp,
                ZoneId = ZoneId,
                MainJob = MainJob,
                MainJobLevel = MainJobLevel,
                SubJob = SubJob,
                SubJobLevel = SubJobLevel,
                IsLeader = IsLeader,
                MemberNumber = MemberNumber
            };
        }
    }

    /// <summary>
    /// Thread-safe model maintaining active party composition, pending invites, and member vitals.
    /// </summary>
    public sealed class PartyState
    {
        private readonly object _lock = new object();
        private readonly List<PartyMember> _members = new List<PartyMember>();
        private PartyInvite? _pendingInvite;

        public PartyInvite? PendingInvite
        {
            get
            {
                lock (_lock) return _pendingInvite;
            }
            private set
            {
                lock (_lock) _pendingInvite = value;
            }
        }

        public bool HasPendingInvite => PendingInvite != null;

        public bool IsInParty
        {
            get
            {
                lock (_lock) return _members.Count > 1;
            }
        }

        public bool IsLeader
        {
            get
            {
                lock (_lock)
                {
                    var leader = _members.FirstOrDefault(m => m.IsLeader);
                    return leader != null && leader.MemberNumber == 0;
                }
            }
        }

        public IReadOnlyList<PartyMember> Members
        {
            get
            {
                lock (_lock)
                {
                    return _members.Select(m => m.Clone()).ToList();
                }
            }
        }

        public event Action<PartyInvite>? InviteReceived;
        public event Action? InviteCleared;
        public event Action<PartyMember>? MemberJoined;
        public event Action<PartyMember>? MemberUpdated;
        public event Action<uint>? MemberLeft;
        public event Action? PartyDisbanded;

        public void SetPendingInvite(PartyInvite invite)
        {
            ArgumentNullException.ThrowIfNull(invite);
            PendingInvite = invite;
            InviteReceived?.Invoke(invite);
        }

        public void ClearPendingInvite()
        {
            bool hadInvite;
            lock (_lock)
            {
                hadInvite = _pendingInvite != null;
                _pendingInvite = null;
            }

            if (hadInvite)
            {
                InviteCleared?.Invoke();
            }
        }

        public void UpsertMember(PartyMember member)
        {
            ArgumentNullException.ThrowIfNull(member);
            bool isNew = false;
            PartyMember? notifyMember = null;

            lock (_lock)
            {
                var existing = _members.FirstOrDefault(m => m.ServerId == member.ServerId || (m.Name.Equals(member.Name, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(member.Name)));
                if (existing != null)
                {
                    existing.ServerId = member.ServerId != 0 ? member.ServerId : existing.ServerId;
                    existing.TargetIndex = member.TargetIndex != 0 ? member.TargetIndex : existing.TargetIndex;
                    if (!string.IsNullOrEmpty(member.Name)) existing.Name = member.Name;
                    existing.Hp = member.Hp;
                    existing.Mp = member.Mp;
                    existing.Tp = member.Tp;
                    existing.Hpp = member.Hpp;
                    existing.Mpp = member.Mpp;
                    existing.ZoneId = member.ZoneId != 0 ? member.ZoneId : existing.ZoneId;
                    if (member.MainJob != JobId.None) existing.MainJob = member.MainJob;
                    if (member.MainJobLevel > 0) existing.MainJobLevel = member.MainJobLevel;
                    if (member.SubJob != JobId.None) existing.SubJob = member.SubJob;
                    if (member.SubJobLevel > 0) existing.SubJobLevel = member.SubJobLevel;
                    existing.IsLeader = member.IsLeader;
                    existing.MemberNumber = member.MemberNumber;
                    notifyMember = existing.Clone();
                }
                else
                {
                    var clone = member.Clone();
                    _members.Add(clone);
                    isNew = true;
                    notifyMember = clone;
                }
            }

            if (isNew && notifyMember != null)
            {
                MemberJoined?.Invoke(notifyMember);
            }
            else if (notifyMember != null)
            {
                MemberUpdated?.Invoke(notifyMember);
            }
        }

        public void RemoveMember(uint serverId)
        {
            bool removed = false;
            lock (_lock)
            {
                int index = _members.FindIndex(m => m.ServerId == serverId);
                if (index >= 0)
                {
                    _members.RemoveAt(index);
                    removed = true;
                }
            }

            if (removed)
            {
                MemberLeft?.Invoke(serverId);
            }
        }

        public void ClearParty()
        {
            bool hadMembers;
            lock (_lock)
            {
                hadMembers = _members.Count > 0;
                _members.Clear();
            }

            if (hadMembers)
            {
                PartyDisbanded?.Invoke();
            }
        }
    }
}
