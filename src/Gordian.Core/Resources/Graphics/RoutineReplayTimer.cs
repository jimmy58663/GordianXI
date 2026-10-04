// src/Gordian.Core/Resources/Graphics/RoutineReplayTimer.cs
using System;
using System.Collections.Generic;

namespace Gordian.Core.Resources.Graphics
{
    /// <summary>
    /// One Section 0x07 op 0x52 command (TimeBasedReplay): the Vana'diel clock window in which the client replays the
    /// routine by itself, and how often. The four dwords after the command's delay / duration words are, in order, the
    /// window's start and end on the Vana'diel clock and the longest and shortest replay interval, all in Earth
    /// milliseconds of the Vana'diel clock (2,400 = one Vana'diel minute, 3,456,000 = one Vana'diel day).
    /// <para>
    /// Read from all 485 op 0x52 commands in the retail zone DATs 0-299 (2026-10-03, #81): every command is 24 bytes;
    /// 479 starts and 454 ends are whole Vana'diel minutes; 54 commands have the end below the start (the window wraps
    /// past midnight: Carpenters' Landing's lightning <c>kmi1/s001</c> 23:15-03:00), and 134 ends and 127 starts lie
    /// above 24:00 and are taken modulo the day (<c>kmi1/s002</c> 28:30-33:00 is 04:30-09:00). The shortest interval
    /// never exceeds the longest (equal in the 12 daily triggers such as Windurst's <c>kaza/tim1</c>, 3,600 / 3,600 in
    /// a 00:00-00:01 window) and takes only the values 3,600, 7,200, 10,800, 18,000, 21,600 and 36,000; the longest
    /// often equals the window's length (Ronfaure's pigeons <c>hato/s002</c>: 22:30-23:54 and 201,600 = 84 minutes).
    /// The field names follow xi-tools docs/fx/effect_system.md ("start, end, interval"); that the fourth dword is the
    /// shortest interval of a random range is <em>inferred</em> from these values and awaits the in-game check.
    /// <strong>Beyond xi-tools:</strong> the units, the wrap and modulo, and the fourth dword.
    /// </para>
    /// </summary>
    public readonly record struct TimedReplayWindow(int StartMs, int EndMs, int MaxIntervalMs, int MinIntervalMs)
    {
        /// <summary>Earth milliseconds per Vana'diel minute on the clock these fields use.</summary>
        public const int MillisecondsPerMinute = 2400;

        /// <summary>Earth milliseconds per Vana'diel day (24 x 60 minutes).</summary>
        public const int MillisecondsPerDay = MillisecondsPerMinute * 60 * 24;

        /// <summary>The window's start as a fraction of the Vana'diel day, modulo the day.</summary>
        public float StartFraction => Fraction(StartMs);

        /// <summary>The window's end as a fraction of the Vana'diel day, modulo the day.</summary>
        public float EndFraction => Fraction(EndMs);

        private static float Fraction(int ms)
        {
            long wrapped = ((long)ms % MillisecondsPerDay + MillisecondsPerDay) % MillisecondsPerDay;
            return wrapped / (float)MillisecondsPerDay;
        }

        /// <summary>
        /// Whether the Vana'diel time <paramref name="dayFraction"/> (0 = midnight, 0.5 = noon) falls inside the window,
        /// which wraps past midnight when its end lies before its start.
        /// </summary>
        public bool Contains(float dayFraction)
        {
            if ((long)EndMs - StartMs >= MillisecondsPerDay) return true; // a whole day or more: always open
            float t = dayFraction - MathF.Floor(dayFraction);
            float start = StartFraction, end = EndFraction;
            if (start == end) return false;
            return start < end ? t >= start && t < end : t >= start || t < end;
        }

        /// <summary>The window as Vana'diel clock times, e.g. <c>23:15-03:00</c>.</summary>
        public override string ToString() => $"{Clock(StartMs)}-{Clock(EndMs)}";

        private static string Clock(int ms)
        {
            long minutes = (((long)ms % MillisecondsPerDay + MillisecondsPerDay) % MillisecondsPerDay) / MillisecondsPerMinute;
            return $"{minutes / 60:00}:{minutes % 60:00}";
        }
    }

    /// <summary>
    /// The replay timer of a zone routine with op 0x52 (<see cref="TimedReplayWindow"/>), shared by every generator
    /// start the routine (and the routines it starts) schedule: while one of its windows is open the routine plays when
    /// the window opens and again after each interval, drawn at random between the window's shortest and longest; outside
    /// its windows it does not play. Each emitter keeps its own <see cref="State"/>, seeded alike (<see cref="Seed"/>), so
    /// the generators of one routine fire together without sharing mutable state.
    /// </summary>
    public sealed class RoutineReplayTimer
    {
        public RoutineReplayTimer(IReadOnlyList<TimedReplayWindow> windows, int seed)
        {
            Windows = windows ?? throw new ArgumentNullException(nameof(windows));
            Seed = seed;
        }

        public IReadOnlyList<TimedReplayWindow> Windows { get; }

        /// <summary>Seed of the interval sequence (from the routine's path), the same for every emitter of the routine.</summary>
        public int Seed { get; }

        /// <summary>The index of the first window open at <paramref name="dayFraction"/>, or -1.</summary>
        public int OpenWindow(float dayFraction)
        {
            for (int i = 0; i < Windows.Count; i++)
            {
                if (Windows[i].Contains(dayFraction)) return i;
            }
            return -1;
        }

        /// <summary>Per-emitter replay state: which window is open and the time left until the next replay.</summary>
        public sealed class State
        {
            private readonly RoutineReplayTimer _timer;
            private readonly Random _random;
            private int _window = -1;
            private float _untilNextMs;

            public State(RoutineReplayTimer timer)
            {
                _timer = timer ?? throw new ArgumentNullException(nameof(timer));
                _random = new Random(timer.Seed);
            }

            /// <summary>
            /// Advances by <paramref name="frames"/> 60 Hz frames at Vana'diel time <paramref name="dayFraction"/>; true when
            /// the routine replays in this step (a window opened, or the interval ran out inside one).
            /// </summary>
            public bool Advance(float frames, float dayFraction)
            {
                int open = _timer.OpenWindow(dayFraction);
                if (open < 0)
                {
                    _window = -1;
                    return false;
                }
                if (_window != open)
                {
                    _window = open;
                    _untilNextMs = NextInterval(_timer.Windows[open]);
                    return true;
                }
                _untilNextMs -= frames * (1000f / 60f);
                if (_untilNextMs > 0f) return false;
                _untilNextMs += NextInterval(_timer.Windows[open]);
                if (_untilNextMs <= 0f) _untilNextMs = NextInterval(_timer.Windows[open]);
                return true;
            }

            private float NextInterval(TimedReplayWindow window)
            {
                int max = Math.Max(window.MaxIntervalMs, 1);
                int min = Math.Clamp(window.MinIntervalMs, 1, max);
                return min + (float)_random.NextDouble() * (max - min);
            }
        }
    }
}
