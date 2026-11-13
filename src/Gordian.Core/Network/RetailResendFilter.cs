// src/Gordian.Core/Network/RetailResendFilter.cs
using System;
using System.Collections.Generic;

namespace Gordian.Core.Network
{
    /// <summary>
    /// Recognises the sub-packets a retail map server repeats. The server stamps each sub-packet with the <c>sync</c>
    /// (header bytes 2-3, <c>SendCnt</c>) of the datagram it was first sent in and repeats it, unchanged, in its next
    /// datagrams until our ack covers it (live retail, 2026-11-12: datagram 5 repeated all 48 sub-packets of datagram 4,
    /// datagram 12 carried syncs 10, 11 and 12). A sub-packet is a repeat only when the same bytes with the same sync were
    /// already dispatched; a group that turns up late with an older sync than one already seen is still new.
    /// <para>
    /// #533: a repeated 0x034 restarted the Home Point event after its 0x05C had overwritten the event's numbers. A first
    /// filter that dropped every sync not newer than the newest one seen lost the destination Home Point's spawn after a
    /// same-zone Home Point warp (round 1, Windurst Waters, 2026-11-12), so repeats are matched exactly.
    /// </para>
    /// Sync semantics referenced from XiPackets (https://github.com/atom0s/XiPackets/blob/main/world/HEADER.md), <c>sync</c>.
    /// **Beyond XiPackets**, which does not describe the repeats.
    /// </summary>
    internal sealed class RetailResendFilter
    {
        /// <summary>Syncs further than this behind the newest one are forgotten (the server never repeats that far back).</summary>
        internal const int Window = 1024;

        private readonly Dictionary<(ushort Sync, ulong Hash), int> _dispatched = new();
        private readonly Dictionary<(ushort Sync, ulong Hash), int> _inDatagram = new();
        private bool _hasNewest;
        private ushort _newest;

        /// <summary>The newest sync dispatched, or null before the first.</summary>
        internal ushort? NewestSync => _hasNewest ? _newest : null;

        /// <summary>Starts a datagram: identical sub-packets inside one datagram are counted, not merged.</summary>
        internal void BeginDatagram() => _inDatagram.Clear();

        /// <summary>
        /// True when this sub-packet (header included) was already dispatched: the same bytes with the same sync, as many
        /// times as it occurs so far in this datagram. Otherwise records it as dispatched and returns false.
        /// </summary>
        internal bool IsRepeat(ushort sync, ReadOnlySpan<byte> subPacket, out bool olderThanNewest)
        {
            var key = (sync, Hash(subPacket));
            _inDatagram.TryGetValue(key, out int occurrence);
            _inDatagram[key] = occurrence + 1;
            _dispatched.TryGetValue(key, out int dispatched);
            olderThanNewest = false;
            if (occurrence < dispatched) return true;

            _dispatched[key] = dispatched + 1;
            if (!_hasNewest || unchecked((short)(sync - _newest)) > 0)
            {
                _newest = sync;
                _hasNewest = true;
                Prune();
            }
            else if (sync != _newest)
            {
                olderThanNewest = true;
            }
            return false;
        }

        private void Prune()
        {
            if (_dispatched.Count < 4 * Window) return;
            var stale = new List<(ushort, ulong)>();
            foreach (var key in _dispatched.Keys)
            {
                if (unchecked((ushort)(_newest - key.Sync)) > Window) stale.Add(key);
            }
            foreach (var key in stale) _dispatched.Remove(key);
        }

        /// <summary>FNV-1a 64 over the sub-packet bytes.</summary>
        private static ulong Hash(ReadOnlySpan<byte> bytes)
        {
            ulong hash = 14695981039346656037UL;
            foreach (byte b in bytes)
            {
                hash ^= b;
                hash *= 1099511628211UL;
            }
            return hash;
        }
    }
}
