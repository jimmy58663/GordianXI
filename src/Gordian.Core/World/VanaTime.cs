// src/Gordian.Core/World/VanaTime.cs
using System;
using System.Numerics;

namespace Gordian.Core.World
{
    /// <summary>
    /// Vana'diel calendar and time utilities for Final Fantasy XI.
    /// Time runs 25 times faster than Earth time (1 Earth second = 25 Vana'diel seconds).
    /// Protocol specifications and epoch reference from LandSandBoat (https://github.com/LandSandBoat/server).
    /// </summary>
    public static class VanaTime
    {
        /// <summary>
        /// Unix timestamp in seconds corresponding to the Vana'diel epoch
        /// (2002-01-01 00:00:00 JST / 2001-12-31 15:00:00 UTC).
        /// </summary>
        public const long VanadielEpochUnixSeconds = 1009810800L;

        /// <summary>
        /// Multiplier for Vana'diel time relative to Earth time (25x).
        /// </summary>
        public const int TimeMultiplier = 25;

        /// <summary>
        /// Total Vana'diel seconds in one 24-hour Vana'diel day (86,400 seconds).
        /// </summary>
        public const int SecondsPerVanadielDay = 86400;

        private static long _serverClockOffsetSeconds = 0;

        /// <summary>
        /// Synchronizes the local Vana'diel clock with the authoritative game time sent by the server.
        /// Protocol specification referenced from LandSandBoat (https://github.com/LandSandBoat/server)
        /// and XiPackets (https://github.com/atom0s/XiPackets).
        /// </summary>
        /// <param name="serverGameTime">The server's Earth seconds since the Vana'diel epoch (1009810800).</param>
        public static void SynchronizeServerTime(uint serverGameTime)
        {
            if (serverGameTime == 0) return;
            long clientDeltaEarthSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - VanadielEpochUnixSeconds;
            _serverClockOffsetSeconds = (long)serverGameTime - clientDeltaEarthSeconds;
        }

        /// <summary>
        /// Gets the current server clock offset in Earth seconds.
        /// </summary>
        public static long ServerClockOffsetSeconds => _serverClockOffsetSeconds;

        /// <summary>
        /// Resets the server clock offset to zero (used for testing and disconnection).
        /// </summary>
        public static void ResetClockOffset()
        {
            _serverClockOffsetSeconds = 0;
        }

        /// <summary>
        /// Canonical weather directory and chunk names matching retail FFXI DAT conventions and server weather numbers.
        /// Mappings correspond to weather numbers 0 through 19.
        /// </summary>
        public static readonly string[] WeatherNames = new[]
        {
            "fine", // 0: None / Clear
            "suny", // 1: Sunshine
            "clod", // 2: Clouds / Overcast
            "mist", // 3: Fog
            "dryw", // 4: Hot spell
            "heat", // 5: Heat wave
            "rain", // 6: Rain
            "squl", // 7: Squall
            "dust", // 8: Dust storm
            "sand", // 9: Sand storm
            "wind", // 10: Wind
            "stom", // 11: Gales
            "snow", // 12: Snow
            "bliz", // 13: Blizzards
            "thdr", // 14: Thunder
            "bolt", // 15: Thunderstorms
            "aura", // 16: Auroras
            "ligt", // 17: Stellar glare
            "fogd", // 18: Gloom
            "dark"  // 19: Darkness
        };

        /// <summary>
        /// Converts an Earth UTC date/time into total accumulated Vana'diel seconds since the epoch,
        /// including any synchronized server clock offset.
        /// </summary>
        public static long GetVanadielSeconds(DateTime utcTime)
        {
            long earthUnixSeconds = new DateTimeOffset(DateTime.SpecifyKind(utcTime, DateTimeKind.Utc)).ToUnixTimeSeconds();
            long deltaEarthSeconds = (earthUnixSeconds - VanadielEpochUnixSeconds) + _serverClockOffsetSeconds;
            return deltaEarthSeconds * TimeMultiplier;
        }

        /// <summary>
        /// Computes the current Vana'diel time of day as fractional hours in the range [0.0f .. 24.0f).
        /// (e.g. 12.0 = 12:00 PM noon, 14.5 = 2:30 PM).
        /// </summary>
        public static float GetTimeOfDayHours(DateTime utcTime)
        {
            long vanaSeconds = GetVanadielSeconds(utcTime);
            long daySecond = ((vanaSeconds % SecondsPerVanadielDay) + SecondsPerVanadielDay) % SecondsPerVanadielDay;
            return (float)daySecond / 3600.0f;
        }

        /// <summary>
        /// Resolves a server numeric weather code (0..19) into its canonical 4-character DAT folder/key name.
        /// Defaults to "fine" if the weather number is unrecognized.
        /// </summary>
        public static string GetWeatherId(ushort weatherNumber)
        {
            if (weatherNumber < WeatherNames.Length)
            {
                return WeatherNames[weatherNumber];
            }
            return "fine";
        }

        /// <summary>
        /// Maps an authentic 4-character FFXI weather code into one of the four primary authored
        /// DAT sky environment categories ("fine", "suny", "clod", "mist").
        /// Protocol specification referenced from LandSandBoat (https://github.com/LandSandBoat/server)
        /// and xi-tools (https://github.com/vekien/xi-tools).
        /// </summary>
        public static string GetCanonicalWeatherCategory(string? weatherId)
        {
            if (string.IsNullOrWhiteSpace(weatherId)) return "fine";
            string w = weatherId.Trim().ToLowerInvariant();

            return w switch
            {
                "suny" or "dryw" or "heat" => "suny",
                "clod" or "rain" or "squl" or "dust" or "sand" or "stom" or
                "snow" or "bliz" or "thdr" or "bolt" or "dark" or "fogd" => "clod",
                "mist" => "mist",
                _ => "fine"
            };
        }

        /// <summary>
        /// Computes the authentic Vana'diel moon phase percentage (0% to 100%) for a given Earth UTC date/time.
        /// Protocol specification and 84-day lunar calendar referenced from LandSandBoat (https://github.com/LandSandBoat/server).
        /// </summary>
        public static int GetMoonPhase(DateTime utcTime)
        {
            long totalDays = (GetVanadielSeconds(utcTime) / SecondsPerVanadielDay) + (886L * 360L);
            long daysMod = ((totalDays + 26L) % 84L + 84L) % 84L;

            if (daysMod >= 42L)
            {
                return (int)(100.0 * ((daysMod - 42L) / 42.0) + 0.5);
            }
            else
            {
                return (int)(100.0 * (1.0 - (daysMod / 42.0)) + 0.5);
            }
        }

        /// <summary>
        /// Computes the moon direction (0 = neither, 1 = waning, 2 = waxing).
        /// Referenced from LandSandBoat (https://github.com/LandSandBoat/server).
        /// </summary>
        public static int GetMoonDirection(DateTime utcTime)
        {
            long totalDays = (GetVanadielSeconds(utcTime) / SecondsPerVanadielDay) + (886L * 360L);
            long daysMod = ((totalDays + 26L) % 84L + 84L) % 84L;

            if (daysMod == 42L || daysMod == 0L) return 0;
            return daysMod < 42L ? 1 : 2;
        }

        /// <summary>
        /// Computes the Vana'diel day of the week (0 = Firesday, 1 = Earthsday, 2 = Watersday, 3 = Windsday,
        /// 4 = Iceday, 5 = Lightningday, 6 = Lightsday, 7 = Darksday) as whole days since the epoch modulo 8.
        /// Weekday ordering and derivation referenced from LandSandBoat (https://github.com/LandSandBoat/server).
        /// </summary>
        public static int GetDayOfWeekIndex(DateTime utcTime)
        {
            long totalDays = Math.DivRem(GetVanadielSeconds(utcTime), SecondsPerVanadielDay, out long rem);
            if (rem < 0) totalDays--;
            return (int)(((totalDays % 8L) + 8L) % 8L);
        }

        /// <summary>
        /// Computes the 12-step moon phase index (0 to 11) for sprite-sheet animations and celestial shaders:
        /// 0: New Moon, 6: Full Moon.
        /// Derived from xi-model-viewer (https://github.com/vekien/xi-model-viewer).
        /// </summary>
        public static int GetMoonPhaseIndex(DateTime utcTime)
        {
            long totalDays = (GetVanadielSeconds(utcTime) / SecondsPerVanadielDay) + (886L * 360L);
            long daysMod = ((totalDays + 26L) % 84L + 84L) % 84L;

            // 84 days per lunar cycle / 12 phases = 7 days per phase.
            // Center New Moon (phase 0) at daysMod 42 and Full Moon (phase 6) at daysMod 0/84.
            int phase = (int)MathF.Floor((((daysMod + 3.5f) % 84f) / 7.0f));
            return Math.Clamp((phase + 6) % 12, 0, 11);
        }

        /// <summary>
        /// Computes the dynamic celestial sun direction vector in display space (+Y up, +X east, -X west)
        /// for a given Vana'diel hour (0.0 to 24.0).
        /// Protocol and celestial orbit referenced from xi-model-viewer (https://github.com/vekien/xi-model-viewer).
        /// </summary>
        public static Vector3 GetSunDirection(float vanaHour)
        {
            // Sun completes one circle every 24 hours:
            // Noon (12:00): Sun at peak zenith (+Y), Moon at nadir (-Y).
            // Midnight (00:00 / 24:00): Sun at nadir (-Y), Moon at zenith (+Y).
            // Dawn (06:00): Sun rising in east (+X).
            // Dusk (18:00): Sun setting in west (-X).
            float angle = vanaHour * (MathF.PI / 12f);
            float x = MathF.Sin(angle);
            float y = -MathF.Cos(angle);
            float z = 0.25f; // Slight seasonal ecliptic inclination
            return Vector3.Normalize(new Vector3(x, y, z));
        }
    }
}

