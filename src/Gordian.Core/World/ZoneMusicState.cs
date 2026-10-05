// src/Gordian.Core/World/ZoneMusicState.cs
using System;
using System.Threading;

namespace Gordian.Core.World
{
    /// <summary>The music slots the server fills (S2C 0x05F <c>Slot</c>).</summary>
    /// <remarks>Slot meanings referenced from XiPackets <c>world/server/0x005F</c> (https://github.com/atom0s/XiPackets).</remarks>
    public enum MusicSlot
    {
        /// <summary>Zone music by day.</summary>
        ZoneDay = 0,

        /// <summary>Zone music by night.</summary>
        ZoneNight = 1,

        /// <summary>Battle music when fighting alone.</summary>
        BattleSolo = 2,

        /// <summary>Battle music when fighting in a party.</summary>
        BattleParty = 3,

        /// <summary>Riding a chocobo or mount.</summary>
        Mount = 4,

        /// <summary>Knocked out.</summary>
        Dead = 5,

        /// <summary>Inside the Mog House.</summary>
        MogHouse = 6,

        /// <summary>Fishing.</summary>
        Fishing = 7,
    }

    /// <summary>
    /// The zone's music as the server sets it: eight slots of music numbers (the login packet's <c>MusicNum[5]</c> fills
    /// slots 0-4 on zone-in, S2C 0x05F changes any slot) and the server music volume (S2C 0x060). The audio engine reads
    /// it to pick the track; GordianXI's network layer only stores it.
    /// </summary>
    /// <remarks>
    /// Referenced from XiPackets (https://github.com/atom0s/XiPackets) <c>world/server/0x000A</c> (MusicNum),
    /// <c>world/server/0x005F</c> (GP_SERV_MUSIC) and <c>world/server/0x0060</c> (GP_SERV_MUSICVOLUME).
    /// </remarks>
    public sealed class ZoneMusicState
    {
        /// <summary>Number of music slots.</summary>
        public const int SlotCount = 8;

        /// <summary>The loudest server music volume (0x060 <c>volume</c> ranges 0-127).</summary>
        public const int MaxVolume = 127;

        private readonly ushort[] _slots = new ushort[SlotCount];
        private readonly int[] _eventSlots = { -1, -1, -1, -1, -1, -1, -1, -1 };
        private readonly object _lock = new();
        private int _savedVolume = -1;
        private int _version;

        /// <summary>Raised (on the network thread) after any slot or the volume changes.</summary>
        public event Action? Changed;

        /// <summary>Increments on every change, so a reader can poll cheaply.</summary>
        public int Version => Volatile.Read(ref _version);

        /// <summary>The server music volume, 0-127 (127 until a 0x060 says otherwise).</summary>
        public int Volume { get; private set; } = MaxVolume;

        /// <summary>The fade time of the last 0x060, in the packet's units (provisional reading: 1/60 s frames).</summary>
        public int VolumeFadeTime { get; private set; }

        /// <summary>The music number in a slot (0 = none).</summary>
        public ushort Get(MusicSlot slot) => Get((int)slot);

        /// <summary>The music number in a slot index (0 = none; out-of-range slots are 0).</summary>
        public ushort Get(int slot)
        {
            lock (_lock)
            {
                if (slot is < 0 or >= SlotCount)
                {
                    return 0;
                }

                return _eventSlots[slot] >= 0 ? (ushort)_eventSlots[slot] : _slots[slot];
            }
        }

        /// <summary>A copy of all eight slots.</summary>
        public ushort[] Snapshot()
        {
            lock (_lock)
            {
                return (ushort[])_slots.Clone();
            }
        }

        /// <summary>S2C 0x05F: sets one slot.</summary>
        public void SetSlot(int slot, ushort musicNum)
        {
            if (slot is < 0 or >= SlotCount)
            {
                return;
            }

            lock (_lock)
            {
                _slots[slot] = musicNum;
            }

            Bump();
        }

        /// <summary>
        /// S2C 0x00A: the zone's <c>MusicNum[5]</c> table fills slots 0-4 (day, night, solo battle, party battle, mount). The
        /// other slots are kept: only 0x05F sets them.
        /// </summary>
        public void SetZoneTable(ReadOnlySpan<ushort> musicNum)
        {
            lock (_lock)
            {
                for (int i = 0; i < Math.Min(5, musicNum.Length); i++)
                {
                    _slots[i] = musicNum[i];
                }

                Volume = MaxVolume;
                VolumeFadeTime = 0;
            }

            Bump();
        }

        /// <summary>S2C 0x060: fade the music to <paramref name="volume"/> (0-127) over <paramref name="time"/>.</summary>
        public void SetVolume(int time, int volume)
        {
            lock (_lock)
            {
                Volume = Math.Clamp(volume, 0, MaxVolume);
                VolumeFadeTime = Math.Max(0, time);
            }

            Bump();
        }

        /// <summary>Whether a running event has set any slot or the volume.</summary>
        public bool HasEventMusic
        {
            get
            {
                lock (_lock)
                {
                    return _savedVolume >= 0 || Array.Exists(_eventSlots, s => s >= 0);
                }
            }
        }

        /// <summary>
        /// Event opcode 0x5C subs 0-7 / 0x80-0x87: an event sets a slot (it writes the same slot buffer as S2C 0x05F,
        /// XiEvents <c>OpCodes/0x005C</c>). GordianXI keeps the event's value apart and drops it in <see cref="EndEvent"/>,
        /// so the zone's music returns after the scene (#167).
        /// </summary>
        public void SetEventSlot(int slot, ushort musicNum)
        {
            if (slot is < 0 or >= SlotCount)
            {
                return;
            }

            lock (_lock)
            {
                _eventSlots[slot] = musicNum;
            }

            Bump();
        }

        /// <summary>
        /// Event opcodes 0x5C subs 0xA0 / 0xA1 and 0x5D: the event eases the music volume (the same call as S2C 0x060).
        /// The volume before the event comes back in <see cref="EndEvent"/>.
        /// </summary>
        public void SetEventVolume(int time, int volume)
        {
            lock (_lock)
            {
                if (_savedVolume < 0)
                {
                    _savedVolume = Volume;
                }

                Volume = Math.Clamp(volume, 0, MaxVolume);
                VolumeFadeTime = Math.Max(0, time);
            }

            Bump();
        }

        /// <summary>The event ended: its slots and volume are dropped.</summary>
        public void EndEvent()
        {
            bool changed;
            lock (_lock)
            {
                changed = _savedVolume >= 0 || Array.Exists(_eventSlots, s => s >= 0);
                Array.Fill(_eventSlots, -1);
                if (_savedVolume >= 0)
                {
                    Volume = _savedVolume;
                    VolumeFadeTime = 60;
                    _savedVolume = -1;
                }
            }

            if (changed)
            {
                Bump();
            }
        }

        private void Bump()
        {
            Interlocked.Increment(ref _version);
            Changed?.Invoke();
        }
    }
}
