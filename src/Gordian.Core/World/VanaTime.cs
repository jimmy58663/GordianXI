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
        /// Converts an Earth UTC date/time into total accumulated Vana'diel seconds since the epoch.
        /// </summary>
        public static long GetVanadielSeconds(DateTime utcTime)
        {
            long earthUnixSeconds = new DateTimeOffset(DateTime.SpecifyKind(utcTime, DateTimeKind.Utc)).ToUnixTimeSeconds();
            long deltaEarthSeconds = earthUnixSeconds - VanadielEpochUnixSeconds;
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
