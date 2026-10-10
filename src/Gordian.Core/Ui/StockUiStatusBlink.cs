// src/Gordian.Core/Ui/StockUiStatusBlink.cs
using System;
using System.Collections.Generic;

namespace Gordian.Core.Ui
{
    /// <summary>
    /// Expiring status icons blink (#17, the last item of Tier 2 chunk 3). The status bar lists the icons of S2C 0x037;
    /// their end times come from S2C 0x063 type 0x09 (<see cref="World.LocalPlayerState.GetStatusIconTimers"/>), which
    /// LandSandBoat fills from the same effect list in the same order, skipping effects without an icon, so the n-th
    /// shown icon is matched to the n-th timer of the same id (<see cref="MatchRemaining"/>; the two packets can arrive
    /// apart, so an id that does not line up is matched to the first unused timer of that id).
    /// <para>
    /// An icon blinks once fewer than <see cref="BlinkThresholdSeconds"/> are left (15 s, the maintainer's retail check,
    /// 2026-10-07), fading out and back once every <see cref="BlinkPeriodSeconds"/> down to <see cref="MinimumOpacity"/>
    /// (the fade was accepted in game; its period and depth are ours). Under each timed icon the time left shows in whole
    /// minutes ("2m") and, from 60 s, counts down in seconds (<see cref="DurationText"/>; retail screenshot and the
    /// maintainer's answer, 2026-10-08).
    /// </para>
    /// </summary>
    public static class StockUiStatusBlink
    {
        /// <summary>Seconds left below which an icon blinks (retail: 15).</summary>
        public const double BlinkThresholdSeconds = 15;

        /// <summary>The time left from which the duration counts down in seconds (retail: 60 s); above it, whole minutes.</summary>
        public const double SecondsShownFrom = 60;

        /// <summary>
        /// The time left drawn under an icon, as retail shows it (the maintainer's retail checks, 2026-10-08 and 2026-10-09):
        /// above 60 s, whole minutes rounded down with an "m" (61-119 s "1m", 120-179 s "2m", "15m"); from 60 s, plain seconds
        /// rounded up ("60" ... "1"), so "1m" hands over to "60" with no gap and no value shown twice. Null (nothing drawn)
        /// for an effect without a timer or once it has run out. Past an hour the minutes go on ("90m"): retail likely shows
        /// hours ("1h"), and staying in minutes is a deliberate difference, the maintainer's choice of 2026-10-09.
        /// </summary>
        public static string? DurationText(double? remainingSeconds)
        {
            if (remainingSeconds is not { } left || left <= 0) return null;
            var culture = System.Globalization.CultureInfo.InvariantCulture;
            if (left <= SecondsShownFrom) return ((int)Math.Ceiling(left)).ToString(culture);
            return ((long)Math.Floor(left / 60)).ToString(culture) + "m";
        }

        /// <summary>One fade out and back in.</summary>
        public const double BlinkPeriodSeconds = 1.0;

        /// <summary>The faintest the icon gets while blinking.</summary>
        public const float MinimumOpacity = 0.2f;

        /// <summary>
        /// The seconds left on each shown icon (null: no timer known), matched to the 0x063 timers by order and id.
        /// </summary>
        public static double?[] MatchRemaining(IReadOnlyList<ushort> shownIds, IReadOnlyList<(ushort Id, double? RemainingSeconds)> timers)
        {
            var result = new double?[shownIds.Count];
            var used = new bool[timers.Count];
            for (int i = 0; i < shownIds.Count; i++)
            {
                int match = -1;
                if (i < timers.Count && !used[i] && timers[i].Id == shownIds[i]) match = i;
                for (int j = 0; j < timers.Count && match < 0; j++)
                {
                    if (!used[j] && timers[j].Id == shownIds[i]) match = j;
                }
                if (match < 0) continue;
                used[match] = true;
                result[i] = timers[match].RemainingSeconds;
            }
            return result;
        }

        /// <summary>Whether an icon with this much time left blinks.</summary>
        public static bool IsBlinking(double? remainingSeconds) => remainingSeconds is { } left && left < BlinkThresholdSeconds;

        /// <summary>
        /// The icon's opacity (0-1) at <paramref name="clockSeconds"/> (any steadily running clock): 1 when it is not
        /// blinking, else a cosine fade from 1 down to <see cref="MinimumOpacity"/> and back once per period.
        /// </summary>
        public static float Opacity(double? remainingSeconds, double clockSeconds)
        {
            if (!IsBlinking(remainingSeconds)) return 1f;
            double phase = clockSeconds / BlinkPeriodSeconds * 2 * Math.PI;
            double wave = (1 + Math.Cos(phase)) / 2; // 1 at the start of each period, 0 half way
            return (float)(MinimumOpacity + (1 - MinimumOpacity) * wave);
        }
    }
}
