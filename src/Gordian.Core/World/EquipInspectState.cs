// src/Gordian.Core/World/EquipInspectState.cs
using System;
using System.Collections.Generic;
using Gordian.Core.Network.Packets;

namespace Gordian.Core.World
{
    /// <summary>
    /// What a player check (S2C 0x0C9) told about a character: its equipment by slot and the general block (jobs, levels,
    /// mastery, linkshell). <see cref="Message"/> is the S2C 0x0CA that came with it (name, bazaar message, title), if any.
    /// </summary>
    public sealed record EquipInspectInfo(
        uint ServerId,
        ushort TargetIndex,
        IReadOnlyDictionary<EquipSlotId, ushort> Equipment,
        byte MainJob,
        byte MainJobLevel,
        byte SubJob,
        byte SubJobLevel,
        byte MasteryJob,
        byte MasteryLevel,
        byte MasteryFlags,
        ushort LinkshellItemId,
        string LinkshellName,
        ushort LinkshellColor,
        InspectMessageInfo? Message)
    {
        /// <summary>Whether the server hid the jobs (an anonymous character: every job field is 0).</summary>
        public bool IsAnonymous => MainJob == 0 && MainJobLevel == 0;

        /// <summary>Whether the character wears a linkshell.</summary>
        public bool HasLinkshell => LinkshellItemId != 0 || LinkshellName.Length > 0;

        /// <summary>The item in a slot, or 0 when the slot is empty.</summary>
        public ushort ItemIn(EquipSlotId slot) => Equipment.TryGetValue(slot, out ushort id) ? id : (ushort)0;

        /// <summary>
        /// The linkshell colour's 4-bit channels widened as the entity updates carry them ((c &lt;&lt; 4) + 15, LandSandBoat
        /// <c>char_update.cpp</c>), so the check window's pearl takes the same tint as the name plates' pearl. (XiPackets
        /// documents the client rebuilding the icon colour as 13 c + 0x38 per channel,
        /// https://github.com/atom0s/XiPackets/tree/main/world/server/0x00C9; the plates' tint was calibrated in game.)
        /// </summary>
        public (byte R, byte G, byte B) LinkshellRgb => (Widen(LinkshellColor & 0xF), Widen((LinkshellColor >> 4) & 0xF), Widen((LinkshellColor >> 8) & 0xF));

        private static byte Widen(int nibble) => (byte)((nibble << 4) + 15);
    }

    /// <summary>
    /// Collects the S2C 0x0C9 packets of a player check: the equipment (mode 3, eight items a packet, or the older modes
    /// 0 / 2) builds up per checked character, and the general block (mode 1), which LandSandBoat and retail send last,
    /// completes the check and raises <see cref="Completed"/>. One per session.
    /// </summary>
    public sealed class EquipInspectState
    {
        private readonly object _sync = new();
        private readonly Dictionary<EquipSlotId, ushort> _pending = new();
        private uint _pendingId;
        private EquipInspectInfo? _last;

        /// <summary>The last completed check, or null.</summary>
        public EquipInspectInfo? Last { get { lock (_sync) return _last; } }

        /// <summary>A check's general block arrived: the check is complete (raised outside the lock).</summary>
        public event Action<EquipInspectInfo>? Completed;

        /// <summary>
        /// Adds equipment of a checked character (modes 0, 2, 3). Items for another character than the one being
        /// collected start a new check.
        /// </summary>
        public void AddItems(uint serverId, ReadOnlySpan<EquipInspectItem> items)
        {
            lock (_sync)
            {
                if (serverId != _pendingId)
                {
                    _pending.Clear();
                    _pendingId = serverId;
                }
                foreach (var item in items) _pending[item.Slot] = item.ItemId;
            }
        }

        /// <summary>
        /// Completes a check with its general block (mode 1): the equipment collected for the same character, with the
        /// bazaar message passed in (S2C 0x0CA, sent just before), becomes <see cref="Last"/>.
        /// </summary>
        public EquipInspectInfo Complete(uint serverId, ushort targetIndex, byte mainJob, byte mainLevel, byte subJob, byte subLevel,
            byte masteryJob, byte masteryLevel, byte masteryFlags, ushort linkshellItemId, string linkshellName, ushort linkshellColor,
            InspectMessageInfo? message)
        {
            EquipInspectInfo info;
            lock (_sync)
            {
                var equipment = serverId == _pendingId ? new Dictionary<EquipSlotId, ushort>(_pending) : new Dictionary<EquipSlotId, ushort>();
                info = new EquipInspectInfo(serverId, targetIndex, equipment, mainJob, mainLevel, subJob, subLevel, masteryJob, masteryLevel,
                    masteryFlags, linkshellItemId, linkshellName ?? string.Empty, linkshellColor, message);
                _last = info;
                _pending.Clear();
                _pendingId = 0;
            }
            Completed?.Invoke(info);
            return info;
        }

        /// <summary>Forgets a check in progress (a zone change).</summary>
        public void Clear()
        {
            lock (_sync)
            {
                _pending.Clear();
                _pendingId = 0;
            }
        }
    }
}
