// src/Gordian.Core/World/TreasurePoolState.cs
using System;
using System.Collections.Generic;
using Gordian.Core.Network.Packets;

namespace Gordian.Core.World
{
    /// <summary>One occupied treasure pool slot: the item, where it dropped, and the lot standings.</summary>
    public sealed record TreasureSlot(
        byte Slot,
        ushort ItemId,
        uint Count,
        uint DropperId,
        ushort DropperIndex,
        bool IsContainer,
        bool DropperNamed,
        /// <summary>Server clock (ms) when the item entered the pool.</summary>
        uint StartTime,
        /// <summary>What the local player did: nothing, pass or lot.</summary>
        TreasureEntryKind Entry,
        /// <summary>The local player's lot; 0 until they lot.</summary>
        ushort LocalLot,
        uint LeaderId,
        ushort LeaderIndex,
        string LeaderName,
        /// <summary>The highest lot so far; 0 when nobody has lotted.</summary>
        ushort LeaderLot);

    /// <summary>An item or gil found (S2C 0x0D2), for the message log.</summary>
    public sealed record TreasureFound(uint Gold, ushort ItemId, uint ItemCount, uint DropperId, ushort DropperIndex,
        bool IsContainer, bool DropperNamed);

    /// <summary>
    /// A lot, pass or judgement (S2C 0x0D3), for the message log. <see cref="ItemId"/> is the slot's item read
    /// before a judgement cleared it (0 when the slot was empty).
    /// </summary>
    public sealed record TreasureSolution(byte Slot, ushort ItemId, TreasureJudge Judge, uint LeaderId, string LeaderName,
        uint EntryId, string EntryName, bool EntryIsLot, short EntryLot);

    /// <summary>A party or alliance member's entry on a pool item: a lot (its value) or a pass.</summary>
    public readonly record struct TreasureMemberEntry(bool Passed, ushort Lot);

    /// <summary>
    /// The treasure pool: 10 slots filled by S2C 0x0D2 and updated by 0x0D3 (a lot or pass) until a judgement
    /// (win, loss) empties the slot. Read by the render thread while the network thread writes, so every access takes
    /// the lock.
    /// </summary>
    public sealed class TreasurePoolState
    {
        public const int SlotCount = TreasurePacketBuilder.SlotCount;

        private readonly object _sync = new();
        private readonly TreasureSlot?[] _slots = new TreasureSlot?[SlotCount];

        // Every member's lot or pass per slot, from the 0x0D3 progress packets (and the leader a 0x0D2 names), for the
        // party window's lot column (#143): character id -> entry.
        private readonly Dictionary<uint, TreasureMemberEntry>?[] _entries = new Dictionary<uint, TreasureMemberEntry>?[SlotCount];

        /// <summary>
        /// How long an item stays in the pool before it goes to the highest lot (or is lost): five minutes. LandSandBoat
        /// <c>treasure_pool.cpp</c> (<c>treasure_livetime = 5min</c>, the slot's time stamp set 3 s early).
        /// </summary>
        public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);

        /// <summary>The local clock, in milliseconds (tests replace it).</summary>
        internal Func<long> LocalMilliseconds { get; set; } = () => Environment.TickCount64;

        // The server clock's lead over the local clock (ms), estimated as the largest StartTime - local time seen: a newly
        // found item's StartTime is the server's "now" (minus LandSandBoat's 3 s), an older one's is behind it.
        private long? _serverLeadMs;

        /// <summary>Raised after an item or gil was found, on the network thread.</summary>
        public event Action<TreasureFound>? Found;

        /// <summary>Raised after a lot, pass or judgement was applied, on the network thread.</summary>
        public event Action<TreasureSolution>? Solved;

        /// <summary>Raised after the pool's contents changed.</summary>
        public event Action? Changed;

        /// <summary>The slot's contents, or null when it is empty or out of range.</summary>
        public TreasureSlot? GetSlot(int slot)
        {
            if ((uint)slot >= SlotCount) return null;
            lock (_sync) return _slots[slot];
        }

        /// <summary>The occupied slots in slot order.</summary>
        public TreasureSlot[] Snapshot()
        {
            lock (_sync)
            {
                int n = 0;
                foreach (var s in _slots) if (s != null) n++;
                var result = new TreasureSlot[n];
                int i = 0;
                foreach (var s in _slots) if (s != null) result[i++] = s;
                return result;
            }
        }

        public int Count
        {
            get
            {
                lock (_sync)
                {
                    int n = 0;
                    foreach (var s in _slots) if (s != null) n++;
                    return n;
                }
            }
        }

        /// <summary>
        /// Time left before a slot's item is given out (the five-minute countdown, #143), from its
        /// <see cref="TreasureSlot.StartTime"/> on the server clock as estimated from the items found so far; never
        /// negative. The server's own clock is not sent (LandSandBoat's StartTime is milliseconds since its process
        /// started), so after a zone change, when only older items are re-sent, the estimate runs long until a new item
        /// is found.
        /// </summary>
        public TimeSpan GetRemaining(TreasureSlot slot)
        {
            ArgumentNullException.ThrowIfNull(slot);
            long lead;
            lock (_sync) lead = _serverLeadMs ?? (long)slot.StartTime - LocalMilliseconds();
            long elapsed = LocalMilliseconds() + lead - slot.StartTime;
            long left = (long)Lifetime.TotalMilliseconds - Math.Max(0, elapsed);
            return TimeSpan.FromMilliseconds(Math.Max(0, left));
        }

        /// <summary>
        /// A member's lot or pass on a slot's item, or null while the member has done neither. The local player's own
        /// entry also comes from the slot (0x0D2 carries it when the pool is re-sent, e.g. after a zone change).
        /// </summary>
        public TreasureMemberEntry? GetMemberEntry(int slot, uint memberId, uint localId)
        {
            if ((uint)slot >= SlotCount) return null;
            lock (_sync)
            {
                if (_entries[slot] is { } entries && entries.TryGetValue(memberId, out var entry)) return entry;
                if (memberId == localId && _slots[slot] is { } own)
                {
                    return own.Entry switch
                    {
                        TreasureEntryKind.Lot => new TreasureMemberEntry(false, own.LocalLot),
                        TreasureEntryKind.Pass => new TreasureMemberEntry(true, 0),
                        _ => null,
                    };
                }
                return null;
            }
        }

        /// <summary>Empties the pool (a zone change: the server sends the pool again for a party that is still in it).</summary>
        public void Clear()
        {
            bool had;
            lock (_sync)
            {
                had = false;
                _serverLeadMs = null; // another zone may be another map server, with its own clock
                for (int i = 0; i < SlotCount; i++)
                {
                    if (_slots[i] != null) had = true;
                    _slots[i] = null;
                    _entries[i] = null;
                }
            }
            if (had) Changed?.Invoke();
        }

        /// <summary>
        /// Applies an S2C 0x0D2. An item with an id fills its slot (replacing what was there); a packet with gil
        /// only has no slot. An out of range slot is ignored.
        /// </summary>
        internal void ApplyFound(in S2C_0x0D2_TrophyList packet)
        {
            bool hasItem = packet.ItemId != 0;
            if (hasItem && packet.Slot >= SlotCount) return;

            if (hasItem)
            {
                long lead = (long)packet.StartTime - LocalMilliseconds();
                lock (_sync)
                {
                    if (_serverLeadMs == null || lead > _serverLeadMs) _serverLeadMs = lead;
                    var known = _slots[packet.Slot];
                    if (known != null && known.ItemId == packet.ItemId && known.StartTime == packet.StartTime)
                    {
                        // The same item sent again (LandSandBoat's updatePool on a party change): what we know of the lots
                        // stays; LSB's re-sent packet says nothing about them.
                        _slots[packet.Slot] = known with { Count = Math.Max(1u, packet.ItemCount) };
                    }
                    else
                    {
                        var entries = new Dictionary<uint, TreasureMemberEntry>();
                        if (packet.LeaderId != 0 && packet.LeaderLot > 0) entries[packet.LeaderId] = new TreasureMemberEntry(false, packet.LeaderLot);
                        _entries[packet.Slot] = entries;
                        // Only a lot (2) is taken from Entry: LandSandBoat writes 1 there for every re-sent item
                        // ("isOldItem", updatePool), which XiPackets reads as a pass (#143: Cast Lot was greyed).
                        var entry = packet.Entry == TreasureEntryKind.Lot && packet.IsLocallyLotted ? TreasureEntryKind.Lot : TreasureEntryKind.None;
                        _slots[packet.Slot] = new TreasureSlot(packet.Slot, packet.ItemId, Math.Max(1u, packet.ItemCount), packet.DropperId,
                            packet.DropperIndex, packet.IsContainer, packet.Named, packet.StartTime, entry,
                            entry == TreasureEntryKind.Lot ? packet.LocalLot : (ushort)0,
                            packet.LeaderId, packet.LeaderIndex, packet.LeaderName, packet.LeaderLot);
                    }
                }
            }

            // A re-sent pool (LandSandBoat sends no dropper and no gil then) is not a find: nothing for the log.
            if (packet.Gold > 0 || packet.DropperId != 0)
            {
                Found?.Invoke(new TreasureFound(packet.Gold, packet.ItemId, packet.ItemCount, packet.DropperId,
                    packet.DropperIndex, packet.IsContainer, packet.Named));
            }
            if (hasItem) Changed?.Invoke();
        }

        /// <summary>
        /// Applies an S2C 0x0D3: updates the standings on a lot or pass (the local player's own entry when
        /// <paramref name="localId"/> is the entry), and empties the slot on any judgement.
        /// </summary>
        internal void ApplySolution(in S2C_0x0D3_TrophySolution packet, uint localId)
        {
            if (packet.Slot >= SlotCount) return;

            ushort itemId = 0;
            lock (_sync)
            {
                var slot = _slots[packet.Slot];
                if (slot != null)
                {
                    itemId = slot.ItemId;
                    if (packet.Judge != TreasureJudge.Progress)
                    {
                        _slots[packet.Slot] = null;
                        _entries[packet.Slot] = null;
                    }
                    else
                    {
                        var entries = _entries[packet.Slot] ??= new Dictionary<uint, TreasureMemberEntry>();
                        if (packet.EntryId != 0)
                        {
                            entries[packet.EntryId] = packet.EntryIsLot
                                ? new TreasureMemberEntry(false, (ushort)Math.Max((short)0, packet.EntryLot))
                                : new TreasureMemberEntry(true, 0);
                        }
                        bool mine = packet.EntryId == localId;
                        _slots[packet.Slot] = slot with
                        {
                            LeaderId = packet.LeaderId,
                            LeaderIndex = packet.LeaderIndex,
                            LeaderName = packet.LeaderName,
                            LeaderLot = (ushort)Math.Max((short)0, packet.LeaderLot),
                            Entry = mine ? (packet.EntryIsLot ? TreasureEntryKind.Lot : TreasureEntryKind.Pass) : slot.Entry,
                            LocalLot = mine && packet.EntryIsLot ? (ushort)Math.Max((short)0, packet.EntryLot) : slot.LocalLot,
                        };
                    }
                }
            }

            Solved?.Invoke(new TreasureSolution(packet.Slot, itemId, packet.Judge, packet.LeaderId, packet.LeaderName,
                packet.EntryId, packet.EntryName, packet.EntryIsLot, packet.EntryLot));
            Changed?.Invoke();
        }
    }
}
