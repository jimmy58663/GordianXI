// src/Gordian.Core/World/EventSoundVolumes.cs
using System;
using System.Threading;

namespace Gordian.Core.World
{
    /// <summary>The sound categories event opcodes 0x69 / 0x6A address, as a mask.</summary>
    /// <remarks>Mask bits referenced from XiEvents <c>OpCodes/0x0069</c> (https://github.com/atom0s/XiEvents).</remarks>
    [Flags]
    public enum EventSoundCategory
    {
        /// <summary>No category.</summary>
        None = 0,

        /// <summary>Effects.</summary>
        Effect = 0x01,

        /// <summary>System sounds.</summary>
        System = 0x02,

        /// <summary>Zone sounds.</summary>
        Zone = 0x04,

        /// <summary>Master.</summary>
        Master = 0x08,

        /// <summary>The sounds special chat messages play (0x69 only).</summary>
        SpecialChat = 0x10,
    }

    /// <summary>
    /// The sound category volumes a running event sets (opcode 0x69: on / off) or eases (opcode 0x6A: value / 1000 over
    /// a time), on top of the player's volume sliders. Reset when the event ends.
    /// </summary>
    /// <remarks>Referenced from XiEvents <c>OpCodes/0x0069</c> and <c>OpCodes/0x006A</c> (https://github.com/atom0s/XiEvents).</remarks>
    public sealed class EventSoundVolumes
    {
        private readonly float[] _volumes = { 1f, 1f, 1f, 1f, 1f };
        private readonly object _lock = new();
        private int _version;
        private int _fadeTime;

        /// <summary>Increments on every change.</summary>
        public int Version => Volatile.Read(ref _version);

        /// <summary>The fade time of the last change (event time units: 1/60 s frames, provisional).</summary>
        public int FadeTime
        {
            get
            {
                lock (_lock)
                {
                    return _fadeTime;
                }
            }
        }

        /// <summary>The scripted volume (0-1) of one category bit.</summary>
        public float Get(EventSoundCategory category)
        {
            int index = IndexOf(category);
            lock (_lock)
            {
                return index >= 0 ? _volumes[index] : 1f;
            }
        }

        /// <summary>Sets every category in <paramref name="mask"/> to <paramref name="volume"/> over <paramref name="time"/>.</summary>
        public void Set(EventSoundCategory mask, float volume, int time)
        {
            volume = Math.Clamp(volume, 0f, 1f);
            lock (_lock)
            {
                for (int i = 0; i < _volumes.Length; i++)
                {
                    if (((int)mask & (1 << i)) != 0)
                    {
                        _volumes[i] = volume;
                    }
                }

                _fadeTime = Math.Max(0, time);
            }

            Interlocked.Increment(ref _version);
        }

        /// <summary>Back to full volume (the event ended).</summary>
        public void Reset()
        {
            bool changed;
            lock (_lock)
            {
                changed = Array.Exists(_volumes, v => v != 1f);
                Array.Fill(_volumes, 1f);
                _fadeTime = 30;
            }

            if (changed)
            {
                Interlocked.Increment(ref _version);
            }
        }

        private static int IndexOf(EventSoundCategory category) => category switch
        {
            EventSoundCategory.Effect => 0,
            EventSoundCategory.System => 1,
            EventSoundCategory.Zone => 2,
            EventSoundCategory.Master => 3,
            EventSoundCategory.SpecialChat => 4,
            _ => -1,
        };
    }
}
