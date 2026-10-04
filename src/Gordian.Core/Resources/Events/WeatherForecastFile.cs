// src/Gordian.Core/Resources/Events/WeatherForecastFile.cs
using System;

namespace Gordian.Core.Resources.Events
{
    /// <summary>
    /// One day's weather forecast for a zone: three weather ids (the standard FFXI weather enum: 1 Sunshine, 2 Clouds,
    /// 6 Rain, ...), or <see cref="WeatherForecastFile.NoWeather"/> (255) for an empty slot. The slots are the zone's
    /// usual weather of the day, then a common and a rare alternative (LandSandBoat's normal / common / rare).
    /// </summary>
    public readonly record struct WeatherForecast(byte Normal, byte Common, byte Rare);

    /// <summary>
    /// The weather forecast files event opcode 0x72 (XiEvents <c>CodeGETWEATER</c>) reads: file 7033 for zones below
    /// 100, file 7037 for zones 100 and up.
    /// <para>
    /// Format (decoded 2026-10-03, #125, from the retail files ROM/27/78.DAT (7033, 246,240 bytes) and ROM/188/67.DAT
    /// (7037, 336,960 bytes)): no header; a run of 6,480-byte blocks (38 in 7033, 52 in 7037), one per weather pattern,
    /// each 2,160 days of three bytes (normal, common, rare weather id; 0xFF = none). A day is the Vana'diel day count
    /// modulo 2,160; the forecast of day d of block b is at <c>b * 6480 + d * 3</c>. Several zones share a block, and
    /// the client's zone-to-block tables (XiEvents' <c>WeatherHead2</c> for zones 0-99 and <c>WeatherHead</c> for
    /// 100+) are not in the files.
    /// </para>
    /// <para>
    /// The zone-to-block tables here are ours: every block was compared day by day with LandSandBoat's 2,160-day packed
    /// weather table (<c>sql/zone_weather.sql</c>, https://github.com/LandSandBoat/server), and for each of its 300
    /// zones a block of the zone's file matches all 2,160 days once LandSandBoat's packing is accounted for (where it
    /// leaves a day out the previous day repeats; an empty slot repeats the slot before it, and a day whose common slot
    /// is empty but whose rare slot is set has the rare weather in all three). Where blocks tie (identical content)
    /// the lowest is used. The Adoulin field zones 260-263, 265-268 and 272 have only sunshine in LandSandBoat; they
    /// map, provisionally, to the blocks no LandSandBoat zone uses whose weathers match the zone's own <c>weat/</c>
    /// folders, in file order (see docs/events/vm.md).
    /// </para>
    /// Event VM opcode referenced from XiEvents (https://github.com/atom0s/XiEvents), OpCodes/0x0072.
    /// </summary>
    public sealed class WeatherForecastFile
    {
        /// <summary>The forecast file of zones 0-99.</summary>
        public const int LowZonesFileId = 7033;

        /// <summary>The forecast file of zones 100 and up.</summary>
        public const int HighZonesFileId = 7037;

        /// <summary>The length of the weather cycle in Vana'diel days.</summary>
        public const int DaysPerCycle = 2160;

        /// <summary>Bytes per day: normal, common, rare.</summary>
        public const int BytesPerDay = 3;

        /// <summary>Bytes per block (one zone pattern): 2,160 days of three bytes.</summary>
        public const int BlockSize = DaysPerCycle * BytesPerDay;

        /// <summary>The byte of an empty forecast slot.</summary>
        public const byte NoWeather = 0xFF;

        /// <summary>The highest zone id the tables cover.</summary>
        public const int MaxZoneId = 299;

        /// <summary>Block of file 7033 for zones 0-99 (retail <c>WeatherHead2</c>'s role; derived, see the class notes).</summary>
        private static readonly byte[] LowZoneBlocks =
        {
            4, 1, 1, 10, 10, 3, 3, 5, 5, 6, 6, 7, 7, 7, 4, 4, 4, 4, 4, 4, // 0-19
            4, 4, 4, 4, 11, 11, 0, 1, 11, 12, 12, 11, 11, 13, 13, 13, 13, 14, 15, 15, // 20-39
            15, 15, 15, 4, 4, 4, 16, 16, 18, 4, 18, 17, 17, 18, 19, 19, 20, 19, 21, 21, // 40-59
            15, 22, 22, 22, 22, 17, 17, 17, 17, 20, 4, 18, 24, 4, 4, 4, 4, 25, 15, 20, // 60-79
            26, 26, 28, 28, 29, 27, 26, 30, 30, 31, 32, 33, 32, 30, 34, 34, 35, 36, 37, 36, // 80-99
        };

        /// <summary>Block of file 7037 for zones 100-299 (retail <c>WeatherHead</c>'s role; derived, see the class notes).</summary>
        private static readonly byte[] HighZoneBlocks =
        {
            0, 0, 1, 2, 3, 4, 5, 5, 6, 7, 8, 9, 10, 11, 12, 13, 13, 14, 15, 16, // 100-119
            17, 18, 18, 20, 20, 12, 21, 21, 11, 22, 23, 24, 24, 24, 26, 26, 9, 10, 28, 0, // 120-139
            0, 0, 0, 5, 5, 13, 13, 7, 7, 3, 3, 16, 16, 18, 18, 28, 26, 21, 21, 20, // 140-159
            20, 28, 28, 20, 29, 28, 27, 0, 12, 13, 13, 30, 5, 31, 11, 32, 18, 23, 23, 21, // 160-179
            23, 23, 26, 24, 21, 26, 26, 26, 26, 24, 0, 5, 13, 1, 13, 4, 6, 8, 14, 24, // 180-199
            17, 33, 34, 35, 9, 20, 9, 36, 12, 37, 24, 38, 11, 15, 24, 24, 24, 24, 24, 24, // 200-219
            31, 31, 24, 24, 24, 24, 24, 31, 31, 24, 0, 0, 0, 0, 5, 5, 5, 5, 13, 13, // 220-239
            13, 13, 13, 39, 39, 39, 39, 12, 22, 22, 22, 24, 31, 24, 24, 24, 24, 24, 24, 24, // 240-259
            41, 41, 42, 43, 24, 44, 45, 46, 47, 24, 24, 24, 48, 24, 24, 24, 24, 24, 24, 24, // 260-279 (260-263, 265-268, 272 provisional)
            24, 24, 24, 24, 24, 24, 24, 24, 24, 24, 24, 24, 24, 24, 26, 26, 26, 26, 26, 24, // 280-299
        };

        private readonly byte[] _data;

        private WeatherForecastFile(int fileId, byte[] data)
        {
            FileId = fileId;
            _data = data;
        }

        /// <summary>Which file this is (<see cref="LowZonesFileId"/> or <see cref="HighZonesFileId"/>).</summary>
        public int FileId { get; }

        /// <summary>How many 2,160-day blocks the file holds.</summary>
        public int BlockCount => _data.Length / BlockSize;

        /// <summary>The forecast file a zone's forecast is in (7033 below zone 100, else 7037), as sub 0 of 0x72 picks it.</summary>
        public static int GetFileId(int zoneId) => zoneId >= 100 ? HighZonesFileId : LowZonesFileId;

        /// <summary>The block a zone's forecast is in, inside the file <see cref="GetFileId"/> names. False for an unknown zone.</summary>
        public static bool TryGetBlockIndex(int zoneId, out int block)
        {
            block = -1;
            if (zoneId < 0 || zoneId > MaxZoneId) return false;
            block = zoneId < 100 ? LowZoneBlocks[zoneId] : HighZoneBlocks[zoneId - 100];
            return true;
        }

        /// <summary>
        /// Wraps a forecast file's bytes (kept, not copied). Null when <paramref name="fileId"/> is not a forecast file
        /// or the bytes are not a whole number of blocks.
        /// </summary>
        public static WeatherForecastFile? Parse(int fileId, byte[]? data)
        {
            if (fileId != LowZonesFileId && fileId != HighZonesFileId) return null;
            if (data == null || data.Length < BlockSize || data.Length % BlockSize != 0) return null;
            return new WeatherForecastFile(fileId, data);
        }

        /// <summary>
        /// The forecast for a zone on a day (any Vana'diel day count; taken modulo 2,160, negative days wrapped).
        /// False when the zone's forecast is not in this file (the other file, an unknown zone, or a block past the end).
        /// </summary>
        public bool TryGetForecast(int zoneId, int day, out WeatherForecast forecast)
        {
            forecast = default;
            if (GetFileId(zoneId) != FileId || !TryGetBlockIndex(zoneId, out int block) || block >= BlockCount) return false;
            int cycleDay = day % DaysPerCycle;
            if (cycleDay < 0) cycleDay += DaysPerCycle;
            int at = block * BlockSize + cycleDay * BytesPerDay;
            forecast = new WeatherForecast(_data[at], _data[at + 1], _data[at + 2]);
            return true;
        }
    }
}
