// tests/Gordian.Core.Tests/Events/WeatherForecastTests.cs
using System;
using System.IO;
using System.Linq;
using Gordian.Core.Events;
using Gordian.Core.Resources;
using Gordian.Core.Resources.Events;
using Gordian.Core.Resources.Tables;
using Xunit;
using Xunit.Abstractions;

namespace Gordian.Core.Tests.Events
{
    /// <summary>
    /// The weather forecast files 7033 / 7037 (<see cref="WeatherForecastFile"/>) and event opcode 0x72, which writes a
    /// zone's forecast into zone work values 2-4 (#125).
    /// </summary>
    public class WeatherForecastTests
    {
        private const string GameDirectory = @"G:\Program Files (x86)\PlayOnline\SquareEnix\FINAL FANTASY XI";
        private static readonly TimeSpan Frame = TimeSpan.FromSeconds(1.0 / 60);
        private readonly ITestOutputHelper _output;

        public WeatherForecastTests(ITestOutputHelper output) => _output = output;

        /// <summary>A forecast file of <paramref name="blocks"/> blocks, all sunshine, with one day set.</summary>
        private static byte[] SyntheticFile(int blocks, int block, int day, byte normal, byte common, byte rare)
        {
            var data = new byte[blocks * WeatherForecastFile.BlockSize];
            for (int i = 0; i < data.Length; i += 3)
            {
                data[i] = 1;
                data[i + 1] = WeatherForecastFile.NoWeather;
                data[i + 2] = WeatherForecastFile.NoWeather;
            }
            int at = block * WeatherForecastFile.BlockSize + day * 3;
            data[at] = normal;
            data[at + 1] = common;
            data[at + 2] = rare;
            return data;
        }

        private static byte[] Ref(int index) => new[] { (byte)(0x8000 | index), (byte)((0x8000 | index) >> 8) };

        /// <summary><c>72 00 zone ; 72 01 zone day ; 21</c> with the zone and day as immediate data 0 and 1.</summary>
        private static byte[] ForecastCode() =>
            new byte[] { 0x72, 0x00 }.Concat(Ref(0))
                .Concat(new byte[] { 0x72, 0x01 }).Concat(Ref(0)).Concat(Ref(1))
                .Concat(new byte[] { 0x21 }).ToArray();

        private static EventVm Make(byte[] code, RecordingHost host, EventWorkZone zone, params uint[] references)
        {
            var block = new EventBlock(0x010E6001, offsets: new ushort[] { 0 }, eventIds: new ushort[] { 100 }, references, code);
            return new EventVm(block, 100, zone, host, 0x010E6001, 1);
        }

        // ---- The decoder ----

        [Fact]
        public void FileId_ZonesBelow100Use7033_OthersUse7037()
        {
            Assert.Equal(7033, WeatherForecastFile.GetFileId(0));
            Assert.Equal(7033, WeatherForecastFile.GetFileId(99));
            Assert.Equal(7037, WeatherForecastFile.GetFileId(100));
            Assert.Equal(7037, WeatherForecastFile.GetFileId(299));
        }

        [Fact]
        public void BlockIndex_CoversZones0To299Only()
        {
            for (int zone = 0; zone <= 299; zone++)
            {
                Assert.True(WeatherForecastFile.TryGetBlockIndex(zone, out int block));
                Assert.InRange(block, 0, zone < 100 ? 37 : 51);
            }
            Assert.False(WeatherForecastFile.TryGetBlockIndex(-1, out _));
            Assert.False(WeatherForecastFile.TryGetBlockIndex(300, out _));
        }

        [Fact]
        public void Parse_RejectsOtherFilesAndPartialBlocks()
        {
            Assert.Null(WeatherForecastFile.Parse(7034, new byte[WeatherForecastFile.BlockSize]));
            Assert.Null(WeatherForecastFile.Parse(7037, null));
            Assert.Null(WeatherForecastFile.Parse(7037, Array.Empty<byte>()));
            Assert.Null(WeatherForecastFile.Parse(7037, new byte[WeatherForecastFile.BlockSize + 1]));
            Assert.Null(WeatherForecastFile.Parse(7037, new byte[WeatherForecastFile.BlockSize - 3]));
            var file = WeatherForecastFile.Parse(7037, new byte[2 * WeatherForecastFile.BlockSize]);
            Assert.NotNull(file);
            Assert.Equal(2, file!.BlockCount);
        }

        [Fact]
        public void Forecast_ReadsBlockDayTriples_WrappingTheDayOver2160()
        {
            // La Theine Plateau (102) is block 1 of 7037.
            var file = WeatherForecastFile.Parse(7037, SyntheticFile(2, 1, 5, 6, 10, 0xFF))!;
            Assert.True(file.TryGetForecast(102, 5, out var forecast));
            Assert.Equal(new WeatherForecast(6, 10, 0xFF), forecast);
            Assert.True(file.TryGetForecast(102, 5 + 2160 * 3, out forecast));
            Assert.Equal(new WeatherForecast(6, 10, 0xFF), forecast);
            Assert.True(file.TryGetForecast(102, 5 - 2160, out forecast));
            Assert.Equal(new WeatherForecast(6, 10, 0xFF), forecast);
            Assert.True(file.TryGetForecast(102, 6, out forecast));
            Assert.Equal(new WeatherForecast(1, 0xFF, 0xFF), forecast);
            // West Ronfaure (100) is block 0.
            Assert.True(file.TryGetForecast(100, 5, out forecast));
            Assert.Equal(new WeatherForecast(1, 0xFF, 0xFF), forecast);
        }

        [Fact]
        public void Forecast_FailsForZonesOfTheOtherFileOrPastTheEnd()
        {
            var file = WeatherForecastFile.Parse(7037, SyntheticFile(2, 0, 0, 1, 2, 3))!;
            Assert.False(file.TryGetForecast(5, 0, out _));      // a 7033 zone
            Assert.False(file.TryGetForecast(103, 0, out _));    // block 2: past this two-block file
            Assert.False(file.TryGetForecast(300, 0, out _));
        }

        // ---- Opcode 0x72 ----

        [Fact]
        public void Opcode72_WritesTheForecastIntoZoneWork2To4()
        {
            var host = new RecordingHost();
            host.Dats[7037] = SyntheticFile(2, 1, 1, 6, 10, 0xFF);
            var zone = new EventWorkZone();
            var vm = Make(ForecastCode(), host, zone, 102, 2161);

            vm.Tick(Frame); // sub 0 reads the file and yields
            Assert.Equal(4, vm.ProgramCounter);
            Assert.Equal(new[] { 7037 }, host.LoadedDats);
            vm.Tick(Frame); // sub 1 writes and yields
            Assert.Equal(10, vm.ProgramCounter);
            Assert.Equal(new[] { 6, 10, 255 }, zone.Zone[2..5]);
            vm.Tick(Frame);
            Assert.True(vm.IsFinished);
            Assert.DoesNotContain((byte)0x72, host.Skipped);
        }

        [Fact]
        public void Opcode72_ZoneBelow100_ReadsFile7033()
        {
            var host = new RecordingHost();
            host.Dats[7033] = SyntheticFile(4, 3, 0, 12, 0xFF, 13); // Bastok Mines (5) is block 3 of 7033
            var zone = new EventWorkZone();
            var vm = Make(ForecastCode(), host, zone, 5, 0);
            for (int i = 0; i < 5 && !vm.IsFinished; i++) vm.Tick(Frame);
            Assert.Equal(new[] { 7033 }, host.LoadedDats);
            Assert.Equal(new[] { 12, 255, 13 }, zone.Zone[2..5]);
        }

        [Theory]
        [InlineData(false, 102)]  // the file cannot be read
        [InlineData(true, 103)]   // the zone's block is past the end of the file
        [InlineData(true, 300)]   // no such zone
        public void Opcode72_UnreadableForecast_SkipsSub1(bool fileThere, int zoneId)
        {
            var host = new RecordingHost();
            if (fileThere) host.Dats[7037] = SyntheticFile(2, 1, 0, 6, 10, 0xFF);
            var zone = new EventWorkZone();
            zone.Zone[2] = 7;
            zone.Zone[3] = 8;
            zone.Zone[4] = 9;
            var vm = Make(ForecastCode(), host, zone, (uint)zoneId, 0);
            vm.Tick(Frame);
            Assert.Equal(10, vm.ProgramCounter); // sub 0's failure path: past its own 72 01
            vm.Tick(Frame);
            Assert.True(vm.IsFinished);
            Assert.Equal(new[] { 7, 8, 9 }, zone.Zone[2..5]);
        }

        [Fact]
        public void Opcode72_BadFileLength_TakesTheFailurePath()
        {
            var host = new RecordingHost();
            host.Dats[7037] = new byte[2 * WeatherForecastFile.BlockSize - 1];
            var vm = Make(ForecastCode(), host, new EventWorkZone(), 102, 0);
            vm.Tick(Frame);
            Assert.Equal(10, vm.ProgramCounter);
        }

        [Fact]
        public void Opcode72_Sub1WithoutARead_WritesNothing()
        {
            var host = new RecordingHost();
            host.Dats[7037] = SyntheticFile(2, 1, 0, 6, 10, 0xFF);
            var code = new byte[] { 0x72, 0x01 }.Concat(Ref(0)).Concat(Ref(1)).Concat(new byte[] { 0x21 }).ToArray();
            var zone = new EventWorkZone();
            zone.Zone[2] = 7;
            var vm = Make(code, host, zone, 102, 0);
            vm.Tick(Frame);
            Assert.Equal(6, vm.ProgramCounter);
            Assert.Equal(7, zone.Zone[2]);
            Assert.Empty(host.LoadedDats);
        }

        // ---- The retail files (skipped without the install) ----

        private static ResourceManager? Open()
        {
            if (!Directory.Exists(GameDirectory)) return null;
            var rm = new ResourceManager(GameDirectory);
            rm.InitializeFileTable();
            return rm;
        }

        [Fact]
        public void RetailFiles_AreWholeBlocks_AndHoldEveryZonesBlock()
        {
            var rm = Open();
            if (rm == null) return;
            var low = WeatherForecastFile.Parse(7033, rm.LoadDatBytesByFileId(7033));
            var high = WeatherForecastFile.Parse(7037, rm.LoadDatBytesByFileId(7037));
            Assert.NotNull(low);
            Assert.NotNull(high);
            Assert.Equal(38, low!.BlockCount);
            Assert.Equal(52, high!.BlockCount);
            for (int zone = 0; zone <= 299; zone++)
            {
                var file = zone < 100 ? low : high;
                Assert.True(file.TryGetForecast(zone, 0, out _), $"zone {zone}");
            }
        }

        /// <summary>
        /// Days read from the retail files that LandSandBoat's packed table (<c>sql/zone_weather.sql</c>, normal &lt;&lt; 10 |
        /// common &lt;&lt; 5 | rare, 0 = same as the day before) also has: La Theine Plateau 6474, 1354, 10273, 0 (days 0-3);
        /// West Ronfaure 1090, 0, 1057, 1090. LandSandBoat fills an empty slot with the slot before it.
        /// </summary>
        [Theory]
        [InlineData(102, 0, 6, 10, 255)]
        [InlineData(102, 1, 1, 10, 255)]
        [InlineData(102, 2, 10, 1, 255)]
        [InlineData(102, 3, 10, 1, 255)]
        [InlineData(100, 0, 1, 2, 255)]
        [InlineData(100, 1, 1, 2, 255)]
        [InlineData(100, 2, 1, 255, 255)]
        [InlineData(100, 3, 1, 2, 255)]
        [InlineData(230, 2160, 1, 2, 255)] // Southern San d'Oria shares West Ronfaure's block; day 2160 is day 0
        // Bastok Mines (5) days 0-1: LandSandBoat has 13,13,13 and 12,1,13 (it packs an empty common slot with a rare
        // weather as the rare weather three times).
        [InlineData(5, 0, 12, 255, 13)]
        [InlineData(5, 1, 12, 1, 13)]
        public void RetailFiles_MatchLandSandBoatsTable(int zoneId, int day, int normal, int common, int rare)
        {
            var rm = Open();
            if (rm == null) return;
            int fileId = WeatherForecastFile.GetFileId(zoneId);
            var file = WeatherForecastFile.Parse(fileId, rm.LoadDatBytesByFileId(fileId))!;
            Assert.True(file.TryGetForecast(zoneId, day, out var forecast));
            Assert.Equal(new WeatherForecast((byte)normal, (byte)common, (byte)rare), forecast);
        }

        /// <summary>
        /// Southern San d'Oria's weather reporter Maleme (LandSandBoat: Maleme.lua, startEvent(632, 0, 0, 0, 0, 0, 0, 0,
        /// VanadielTime())): the player picks an area, the script divides the eighth parameter by 3456 for the day, then
        /// runs <c>72 00</c> / <c>72 01</c> for that day and the next two, and prints one of four lines a day by which of
        /// zone work values 3 and 4 are 255 (no common / rare weather). The weather after "will be" is an adjective
        /// (0x01 kind 0x17: "sunny"), the others nouns (kind 0x18: "rain"), as the maintainer's retail check showed.
        /// </summary>
        [Theory]
        [InlineData(3, 102, 0, // La Theine Plateau, days 0-2
            "Today, that area's weather will be rainy with a chance of winds.",
            "Tomorrow, the weather will be sunny with a chance of winds.",
            "And, the day after tomorrow, the weather will be windy with a chance of sunshine.")]
        [InlineData(3, 102, 2158, null, null, null)]   // across the end of the 2,160-day cycle
        [InlineData(2, 100, 41, // West Ronfaure
            "Today, that area's weather will be cloudy with a chance of sunshine.",
            "The weather tomorrow will be cloudy.",
            "And, the day after tomorrow, the weather will be cloudy with a chance of sunshine.")]
        [InlineData(11, 111, 700, // Beaucedine Glacier: snow, blizzards and gloom
            "That area's forecast for today is for snow with occasional gloom. There is also a slight chance of blizzards, so caution is advised.",
            "Tomorrow, the weather will be snowy. There is also the possibility of blizzards, so caution is advised.",
            "And, the forecast for the day after tomorrow is for gloom with occasional clouds. There is also a slight chance of blizzards, so caution is advised.")]
        public void Maleme_Event632_ReadsTheForecast(int option, int zoneId, int day, string? line0, string? line1, string? line2)
        {
            string?[] lines = { line0, line1, line2 };
            var rm = Open();
            if (rm == null) return;
            var script = ZoneEventScript.Parse(rm.LoadDatBytesByFileId(ZoneEventScript.GetFileId(230))!)!;
            var dialog = ZoneDialogTable.Parse(rm.LoadDatBytesByFileId(ZoneDialogTable.GetFileId(230))!)!;
            Assert.True(script.TryGetBlock(0x010E6085, out var block));
            var forecasts = WeatherForecastFile.Parse(7037, rm.LoadDatBytesByFileId(7037))!;

            var host = new RecordingHost { DatSource = rm.LoadDatBytesByFileId };
            var zone = new EventWorkZone();
            zone.SetParameters(new[] { 0, 0, 0, 0, 0, 0, 0, day * 3456 + 1000 });
            var vm = new EventVm(block, 632, zone, host, block.ActorId, (ushort)(block.ActorId & 0xFFF));
            bool answered = false;
            for (int i = 0; i < 5000 && !vm.IsFinished; i++)
            {
                vm.Tick(Frame);
                if (vm.IsWaitingForConfirm) vm.Confirm();
                if (!answered && host.Queries.Count > 0 && host.QueryResult == 0)
                {
                    host.QueryResult = option;
                    answered = true;
                }
            }
            Assert.True(vm.IsFinished);
            Assert.Equal(new[] { 7037, 7037, 7037 }, host.LoadedDats);

            // Messages 6563-6574: per day k (0-2), 6572 + k normal only, 6569 + k normal and rare, 6566 + k normal and
            // common, 6563 + k all three; then 6575. Each names the day's weathers from message parameters 0-2.
            var expected = new System.Collections.Generic.List<int>();
            WeatherForecast last = default;
            for (int k = 0; k < 3; k++)
            {
                Assert.True(forecasts.TryGetForecast(zoneId, day + k, out last));
                bool common = last.Common != WeatherForecastFile.NoWeather, rare = last.Rare != WeatherForecastFile.NoWeather;
                int message = (common, rare) switch { (false, false) => 6572, (false, true) => 6569, (true, false) => 6566, _ => 6563 } + k;
                expected.Add(message);
                var context = new SimpleMessageContext(new int[] { last.Normal, last.Common, last.Rare }, "Cybin", "Maleme",
                    (kind, value) => EventMessageNames.Resolve(rm, kind, value));
                string line = string.Join(" ", EventMessageFormatter.FormatLines(dialog.GetMessage(message)!, context));
                _output.WriteLine($"{message}: {line}");
                if (lines[k] != null) Assert.Equal(lines[k], line);
                // The all-three line (6563 + k) names the normal weather as a noun, the others as an adjective.
                Assert.True(rm.TryGetWeatherName(last.Normal, adjective: message - k != 6563, out string normalName));
                Assert.Contains(normalName, line);
                Assert.DoesNotContain("<17", line);
                Assert.DoesNotContain("<18", line);
            }
            expected.Add(6575);
            Assert.Equal(expected, host.Printed.Select(p => p.Message).Where(m => m >= 6563 && m <= 6575));
            Assert.Equal(new int[] { last.Normal, last.Common, last.Rare }, zone.Zone[2..5]);
        }
    }
}
