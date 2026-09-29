// src/Gordian.Core/World/TreasurePoolState.cs
using System;
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

        /// <summary>Empties the pool (a zone change: the server sends the pool again for a party that is still in it).</summary>
        public void Clear()
        {
            bool had;
            lock (_sync)
            {
                had = false;
                for (int i = 0; i < SlotCount; i++)
                {
                    if (_slots[i] != null) had = true;
                    _slots[i] = null;
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
                var slot = new TreasureSlot(packet.Slot, packet.ItemId, Math.Max(1u, packet.ItemCount), packet.DropperId,
                    packet.DropperIndex, packet.IsContainer, packet.Named, packet.StartTime, packet.Entry,
                    packet.IsLocallyLotted ? packet.LocalLot : (ushort)0,
                    packet.LeaderId, packet.LeaderIndex, packet.LeaderName, packet.LeaderLot);
                lock (_sync) _slots[packet.Slot] = slot;
            }

            Found?.Invoke(new TreasureFound(packet.Gold, packet.ItemId, packet.ItemCount, packet.DropperId,
                packet.DropperIndex, packet.IsContainer, packet.Named));
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
                    }
                    else
                    {
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
