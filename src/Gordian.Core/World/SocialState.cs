// src/Gordian.Core/World/SocialState.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Gordian.Core.Network.Packets;

namespace Gordian.Core.World
{
    /// <summary>
    /// One slot of the delivery box (S2C 0x04B <c>GP_POST_BOX_STATE</c>): the item, the other party and the slot's state.
    /// </summary>
    /// <param name="Box">The box the slot is in.</param>
    /// <param name="Slot">The slot, 0-7.</param>
    /// <param name="Stat">The slot's state.</param>
    /// <param name="Name">The sender of an incoming item or the recipient of an outgoing one.</param>
    /// <param name="ItemId">The item.</param>
    /// <param name="Quantity">The quantity.</param>
    /// <param name="Kind">The item kind word.</param>
    /// <param name="RequestId">The request id of the box state.</param>
    /// <param name="RequestTime">The request time of the box state.</param>
    /// <param name="ExtData">The item's 28 bytes of extra data.</param>
    public sealed record DeliverySlot(DeliveryBox Box, int Slot, DeliveryItemStat Stat, string Name, ushort ItemId, uint Quantity,
        int Kind, uint RequestId, uint RequestTime, byte[] ExtData)
    {
        /// <summary>The retail client disables Return for items from the Auction House, whose sender starts with "AH".</summary>
        public bool CanReturn => !Name.StartsWith("AH", StringComparison.Ordinal);
    }

    /// <summary>The last answer of the delivery box (S2C 0x04B), with its raw codes.</summary>
    public sealed record DeliveryResponse(DeliveryCommand Command, DeliveryBox Box, int Slot, int ItemWorkNo, DeliveryResultCode Result,
        int ResParam1, int ResParam2, int ResParam3)
    {
        public bool Succeeded => Result == DeliveryResultCode.Success;
    }

    /// <summary>
    /// The delivery box: eight slots of the incoming box and eight of the outgoing box as the server last described them
    /// (S2C 0x04B), the item counts of a check, the last answer, and whether a recipient name was found. LandSandBoat
    /// answers every command, so the state follows the answers, not the requests.
    /// </summary>
    public sealed class DeliveryBoxState
    {
        public const int SlotsPerBox = SocialPacketBuilder.DeliverySlotCount;

        private readonly object _lock = new();
        private readonly DeliverySlot?[] _incoming = new DeliverySlot?[SlotsPerBox];
        private readonly DeliverySlot?[] _outgoing = new DeliverySlot?[SlotsPerBox];

        /// <summary>Raised whenever an answer changed the state.</summary>
        public event Action? Changed;

        /// <summary>The last answer of the server, or null.</summary>
        public DeliveryResponse? LastResponse { get; private set; }

        /// <summary>The number of items in the incoming box from the last check (-1 before one).</summary>
        public int IncomingCount { get; private set; } = -1;

        /// <summary>The number of items in the outgoing box from the last check (-1 before one).</summary>
        public int OutgoingCount { get; private set; } = -1;

        /// <summary>
        /// The mode the server confirmed: the outgoing box after DeliOpen, the incoming box after PostOpen, None after
        /// PostalClose or before any.
        /// </summary>
        public DeliveryBox OpenBox { get; private set; } = DeliveryBox.None;

        /// <summary>The last recipient name the server confirmed with a query, or null.</summary>
        public string? LastQueriedName { get; private set; }

        /// <summary>The result of the last recipient query: true when the name exists. Null before a query.</summary>
        public bool? LastQueryFound { get; private set; }

        /// <summary>The slot of a box (0-7), or null when it is empty or unknown.</summary>
        public DeliverySlot? GetSlot(DeliveryBox box, int slot)
        {
            if (slot < 0 || slot >= SlotsPerBox) return null;
            lock (_lock) return BoxSlots(box)?[slot];
        }

        /// <summary>A copy of the filled slots of a box.</summary>
        public IReadOnlyList<DeliverySlot> SnapshotSlots(DeliveryBox box)
        {
            lock (_lock) return BoxSlots(box)?.Where(s => s != null).Select(s => s!).ToArray() ?? System.Array.Empty<DeliverySlot>();
        }

        private DeliverySlot?[]? BoxSlots(DeliveryBox box) => box switch
        {
            DeliveryBox.Incoming => _incoming,
            DeliveryBox.Outgoing => _outgoing,
            _ => null
        };

        /// <summary>Applies an S2C 0x04B answer.</summary>
        public void Apply(in S2C_0x04B_PbxResult result)
        {
            if (!result.IsValid) return;
            var box = (DeliveryBox)result.BoxNo;
            var code = result.ResultCode;
            bool ok = code is DeliveryResultCode.Success or DeliveryResultCode.Interim;

            lock (_lock)
            {
                LastResponse = new DeliveryResponse(result.Command, box, result.PostWorkNo, result.ItemWorkNo, code,
                    result.ResParam1, result.ResParam2, result.ResParam3);

                switch (result.Command)
                {
                    case DeliveryCommand.Check when ok:
                        if (box == DeliveryBox.Incoming) IncomingCount = result.ResParam2;
                        else if (box == DeliveryBox.Outgoing) OutgoingCount = result.ResParam3;
                        break;
                    case DeliveryCommand.DeliOpen when ok:
                        OpenBox = DeliveryBox.Outgoing;
                        break;
                    case DeliveryCommand.PostOpen when ok:
                        OpenBox = DeliveryBox.Incoming;
                        break;
                    case DeliveryCommand.PostalClose when ok:
                        OpenBox = DeliveryBox.None;
                        break;
                    case DeliveryCommand.Query:
                        LastQueryFound = code == DeliveryResultCode.Success;
                        break;
                }

                var slots = BoxSlots(box);
                if (slots != null && result.PostWorkNo >= 0 && result.PostWorkNo < SlotsPerBox && result.HasItemState && ok)
                {
                    // An empty state is an empty slot, also after a finished Get / Clear / Reject: the server resends the item
                    // with the state cleared.
                    var stat = result.Stat;
                    slots[result.PostWorkNo] = stat == DeliveryItemStat.None
                        ? null
                        : new DeliverySlot(box, result.PostWorkNo, stat, result.GetName(), result.ItemId, result.Quantity, result.Kind,
                            result.RequestId, result.RequestTime, result.ExtData.ToArray());
                }
            }

            Changed?.Invoke();
        }

        /// <summary>Records the name of a recipient query (the packet that answers carries no name).</summary>
        public void NoteQuery(string name)
        {
            lock (_lock)
            {
                LastQueriedName = name;
                LastQueryFound = null;
            }
        }

        /// <summary>Forgets everything (zone change or logout).</summary>
        public void Clear()
        {
            lock (_lock)
            {
                System.Array.Clear(_incoming);
                System.Array.Clear(_outgoing);
                IncomingCount = -1;
                OutgoingCount = -1;
                OpenBox = DeliveryBox.None;
                LastResponse = null;
                LastQueriedName = null;
                LastQueryFound = null;
            }
            Changed?.Invoke();
        }
    }

    /// <summary>
    /// The character's blacklist (S2C 0x041 pages and 0x042 edits). LandSandBoat sends the list at every login; retail
    /// drops a player's message (S2C 0x009 with <c>Attr</c> 0x10) when the sender is on it.
    /// </summary>
    public sealed class BlacklistState
    {
        private readonly object _lock = new();
        private readonly Dictionary<uint, string> _entries = new();

        /// <summary>Raised when the list changed.</summary>
        public event Action? Changed;

        /// <summary>Raised when the server refused an edit (S2C 0x042 with the error mode).</summary>
        public event Action? EditFailed;

        /// <summary>True once the last page of the list arrived.</summary>
        public bool IsComplete { get; private set; }

        /// <summary>The number of names on the list.</summary>
        public int Count
        {
            get { lock (_lock) return _entries.Count; }
        }

        /// <summary>Whether the character with this id is blacklisted.</summary>
        public bool IsBlacklisted(uint characterId)
        {
            lock (_lock) return _entries.ContainsKey(characterId);
        }

        /// <summary>Whether a character of this name (case-insensitive) is blacklisted.</summary>
        public bool IsBlacklisted(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            lock (_lock) return _entries.Values.Any(n => n.Equals(name, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>A copy of the entries, sorted by name.</summary>
        public IReadOnlyList<(uint Id, string Name)> Snapshot()
        {
            lock (_lock) return _entries.Select(e => (e.Key, e.Value)).OrderBy(e => e.Value, StringComparer.OrdinalIgnoreCase).ToArray();
        }

        /// <summary>Applies an S2C 0x041 page.</summary>
        public void Apply(in S2C_0x041_BlackList page)
        {
            if (!page.IsValid) return;
            lock (_lock)
            {
                if (page.ResetsList)
                {
                    _entries.Clear();
                    IsComplete = false;
                }
                for (int i = 0; i < page.Count; i++)
                {
                    uint id = page.GetId(i);
                    if (id != 0) _entries[id] = page.GetName(i);
                }
                if (page.IsLastPage) IsComplete = true;
            }
            Changed?.Invoke();
        }

        /// <summary>Applies an S2C 0x042 edit.</summary>
        public void Apply(in S2C_0x042_BlackEdit edit)
        {
            if (!edit.IsValid) return;
            if (edit.Mode == BlacklistEditMode.Error)
            {
                EditFailed?.Invoke();
                return;
            }

            lock (_lock)
            {
                if (edit.Mode == BlacklistEditMode.Add) _entries[edit.Id] = edit.GetName();
                else _entries.Remove(edit.Id);
            }
            Changed?.Invoke();
        }

        /// <summary>Forgets the list (logout).</summary>
        public void Clear()
        {
            lock (_lock)
            {
                _entries.Clear();
                IsComplete = false;
            }
            Changed?.Invoke();
        }
    }

    /// <summary>The last world pass answer (S2C 0x059).</summary>
    public sealed record FriendPassInfo(int UsesLeft, int HoursLeft, int Price, string Pass, byte Type);

    /// <summary>The last <c>/itemsearch</c> answer (S2C 0x049) and where the client found the item.</summary>
    /// <param name="ItemId">The item the server found, 0 for none (ignored when <paramref name="IsAsync"/>).</param>
    /// <param name="ItemName">The echoed search text.</param>
    /// <param name="IsAsync">The server asked for the search by name over several frames.</param>
    /// <param name="Containers">The containers that hold <paramref name="ItemId"/>, with the counts, from <see cref="InventoryState"/>.</param>
    public sealed record ItemSearchResult(ushort ItemId, string ItemName, bool IsAsync, IReadOnlyList<(ContainerId Container, uint Count)> Containers);

    /// <summary>A party or alliance member's position on the map (S2C 0x0A0).</summary>
    public sealed record MapGroupPosition(uint ServerId, short ZoneId, Vector3 Position, DateTime ReceivedUtc);

    /// <summary>
    /// The state the social packets fill that is not the delivery box or the blacklist: the world pass vendor, the last
    /// <c>/itemsearch</c>, the party's server-side group id (S2C 0x0E1) and the party map positions (S2C 0x0A0), and the
    /// linkshell concierge (S2C 0x048).
    /// </summary>
    public sealed class SocialState
    {
        private readonly object _lock = new();
        private readonly Dictionary<uint, MapGroupPosition> _mapGroup = new();
        private readonly ConciergeLinkshell?[] _concierge = new ConciergeLinkshell?[16];

        public event Action<FriendPassInfo>? FriendPassReceived;
        public event Action<ItemSearchResult>? ItemSearchReceived;
        public event Action<uint>? GroupIdReceived;
        public event Action<MapGroupPosition>? MapGroupReceived;
        public event Action? ConciergeChanged;

        /// <summary>The last world pass answer, or null.</summary>
        public FriendPassInfo? LastFriendPass { get; private set; }

        /// <summary>The last <c>/itemsearch</c> answer, or null.</summary>
        public ItemSearchResult? LastItemSearch { get; private set; }

        /// <summary>The party's server-side group id from the last S2C 0x0E1 (0 outside a party); null before an answer.</summary>
        public uint? GroupId { get; private set; }

        /// <summary>The concierge slot of the character's own listing (S2C 0x048 header), or -1.</summary>
        public int ConciergeOwnSlot { get; private set; } = -1;

        /// <summary>Days since the character's listing was posted (S2C 0x048 header).</summary>
        public int ConciergePostedDays { get; private set; }

        public void SetFriendPass(FriendPassInfo info)
        {
            lock (_lock) LastFriendPass = info;
            FriendPassReceived?.Invoke(info);
        }

        public void SetItemSearch(ItemSearchResult result)
        {
            lock (_lock) LastItemSearch = result;
            ItemSearchReceived?.Invoke(result);
        }

        public void SetGroupId(uint groupId)
        {
            lock (_lock) GroupId = groupId;
            GroupIdReceived?.Invoke(groupId);
        }

        public void SetMapGroup(MapGroupPosition position)
        {
            lock (_lock) _mapGroup[position.ServerId] = position;
            MapGroupReceived?.Invoke(position);
        }

        /// <summary>The map positions of the party and alliance members the server last sent.</summary>
        public IReadOnlyList<MapGroupPosition> SnapshotMapGroup()
        {
            lock (_lock) return _mapGroup.Values.ToArray();
        }

        /// <summary>Forgets the party map positions (a new zone; the server sends them again on request).</summary>
        public void ClearMapGroup()
        {
            lock (_lock) _mapGroup.Clear();
        }

        /// <summary>The concierge listing in slot <paramref name="slot"/> (0-15), or null.</summary>
        public ConciergeLinkshell? GetConciergeLinkshell(int slot)
        {
            if (slot < 0 || slot >= _concierge.Length) return null;
            lock (_lock) return _concierge[slot];
        }

        /// <summary>Applies an S2C 0x048 packet of either form.</summary>
        public void ApplyConcierge(in S2C_0x048_LinkConcierge packet)
        {
            if (!packet.IsValid) return;
            lock (_lock)
            {
                if (packet.IsHeader)
                {
                    ConciergeOwnSlot = packet.Registered ? packet.SlotIndex : -1;
                    ConciergePostedDays = packet.PostedDays;
                }
                else
                {
                    for (int i = 0; i < S2C_0x048_LinkConcierge.RecordSlots; i++)
                    {
                        if (packet.TryGetLinkshell(i, out var linkshell) && linkshell.SlotIndex < _concierge.Length)
                        {
                            _concierge[linkshell.SlotIndex] = linkshell;
                        }
                    }
                }
            }
            ConciergeChanged?.Invoke();
        }
    }
}
