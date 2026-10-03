// tests/Gordian.Core.Tests/Resources/EventRealDataTests.cs
using System.IO;
using System.Linq;
using Gordian.Core.Resources;
using Gordian.Core.Resources.Events;
using Gordian.Core.Resources.Tables;
using Xunit;
using Xunit.Abstractions;

namespace Gordian.Core.Tests.Resources
{
    /// <summary>
    /// Checks the dialog table and event script decoders against a retail install, when one is present
    /// (Southern San d'Oria, zone 230, whose text ids LandSandBoat's IDs.lua documents).
    /// </summary>
    public class EventRealDataTests
    {
        private const string GameDirectory = @"G:\Program Files (x86)\PlayOnline\SquareEnix\FINAL FANTASY XI";
        private const int SouthernSandoria = 230;
        private readonly ITestOutputHelper _output;

        public EventRealDataTests(ITestOutputHelper output) => _output = output;

        private static ResourceManager? Open()
        {
            if (!Directory.Exists(GameDirectory)) return null;
            var rm = new ResourceManager(GameDirectory);
            rm.InitializeFileTable();
            return rm;
        }

        [Fact]
        public void SouthernSandoria_DialogTable_MatchesLandSandBoatIds()
        {
            var rm = Open();
            if (rm == null) return;
            var bytes = rm.LoadDatBytesByFileId(ZoneDialogTable.GetFileId(SouthernSandoria));
            Assert.NotNull(bytes);
            var table = ZoneDialogTable.Parse(bytes!);
            Assert.NotNull(table);
            _output.WriteLine($"{table!.Count} messages");
            Assert.Equal(16941, table.Count);

            // IDs.lua: HOMEPOINT_SET = 24, ITEM_CANNOT_BE_OBTAINED = 6431, ITEM_OBTAINED = 6439, YOU_ACCEPT_THE_MISSION = 7239.
            Assert.Equal("Home point set!", table.GetPlainText(24));
            Assert.StartsWith("You cannot obtain the", table.GetPlainText(6431));
            Assert.Equal("You accept the mission.", table.GetPlainText(7239));

            var obtained = table.GetMessage(6439)!;
            Assert.True(obtained.HasPrompt);
            var item = obtained.Segments.Single(s => s.Kind == EventMessageSegmentKind.Name);
            Assert.Equal((byte)'#', item.Code);
            Assert.Equal(0, item.Argument);

            // Message 2 is a query: a question line, then two choices.
            var query = table.GetMessage(2)!;
            Assert.True(query.HasChoices);
            Assert.Equal("Care to learn about Mog Houses? / Sure! No, thanks.", query.ToPlainText());

            // Every message decodes without running past its bytes (no exceptions, plain text never empty for real lines).
            int empty = 0;
            for (int i = 0; i < table.Count; i++)
            {
                if (table.GetPlainText(i).Length == 0) empty++;
            }
            _output.WriteLine($"{empty} empty messages");
            Assert.True(empty < table.Count / 10);
        }

        /// <summary>Zones 256-299 (Western Adoulin is 256) resolve to their own event, dialog and entity DATs (#71).</summary>
        [Fact]
        public void WesternAdoulin_ZoneFiles_Parse()
        {
            var rm = Open();
            if (rm == null) return;
            const int westernAdoulin = 256;
            var script = ZoneEventScript.Parse(rm.LoadDatBytesByFileId(ZoneEventScript.GetFileId(westernAdoulin))!);
            Assert.NotNull(script);
            Assert.True(script!.Blocks.Count > 100);
            Assert.True(script.TryGetBlock(EventBlock.PlayerActor, out _));

            var dialog = ZoneDialogTable.Parse(rm.LoadDatBytesByFileId(ZoneDialogTable.GetFileId(westernAdoulin))!);
            Assert.NotNull(dialog);
            Assert.True(dialog!.Count > 1000);
            Assert.NotNull(ZoneDialogTable.Parse(rm.LoadDatBytesByFileId(ZoneDialogTable.GetFileId(westernAdoulin, japanese: true))!));

            var entities = ZoneEntityList.Parse(rm.LoadDatBytesByFileId(ZoneEntityList.GetFileId(westernAdoulin))!);
            Assert.NotNull(entities);
            Assert.True(entities!.Count > 100);
            _output.WriteLine($"{script.Blocks.Count} blocks, {dialog.Count} messages, {entities.Count} entities");
        }

        /// <summary>
        /// Retail lines that use the codes fixed for #202 / #74 (corpus scan of 2026-10-03), formatted with the game's own
        /// item and key item names: Phanauet Channel (zone 1) carries the shared system lines, Al Zahbi (48) the Mog Locker.
        /// </summary>
        [Fact]
        public void RetailLines_FormatPluralsItemFormsDatesAndCase()
        {
            var rm = Open();
            if (rm == null) return;
            var channel = ZoneDialogTable.Parse(rm.LoadDatBytesByFileId(ZoneDialogTable.GetFileId(1))!)!;
            var alZahbi = ZoneDialogTable.Parse(rm.LoadDatBytesByFileId(ZoneDialogTable.GetFileId(48))!)!;
            string Line(ZoneDialogTable table, int id, params int[] numbers)
            {
                var context = new Gordian.Core.Events.SimpleMessageContext(numbers, "Cybin", "", (kind, value) => Gordian.Core.Events.EventMessageNames.Resolve(rm, kind, value));
                var lines = Gordian.Core.Events.EventMessageFormatter.FormatLines(table.GetMessage(id)!, context);
                _output.WriteLine($"{id}: {string.Join(" | ", lines)}");
                return string.Join(" | ", lines);
            }

            // 01 05 03 (a number) and 01 09 29 (item by count): plural and singular log names.
            Assert.Equal("You obtain 12 fire crystals!", Line(channel, 6401, 4096, 12));
            Assert.Equal("You obtain 1 fire crystal!", Line(channel, 6401, 4096, 1));
            // 01 01 01 (the article) before 01 05 24 (item log name).
            Assert.Equal("You have been rewarded a fire crystal as compensation.", Line(channel, 7059, 4096));
            // 7F 80 01 before a plural item name at the start of a sentence.
            Assert.Equal("Unable to proceed. | Fire crystals are not suitable for use as synergy ingredients.", Line(channel, 38, 4096));
            // 7F 80 01 before a key item name.
            Assert.Equal("Obtained key item: Blue acidity tester.", Line(channel, 6398, 3));
            // 7F 92 n [singular/plural].
            Assert.Equal("Objective: 1 arcana-type creature. | Equipment: Target item must be equipped.", Line(channel, 2327, 1));
            Assert.Equal("Objective: 3 arcana-type creatures. | Equipment: Target item must be equipped.", Line(channel, 2327, 3));

            // Date fields: seconds since 2002-01-01 00:00 JST.
            int seconds = (int)(new System.DateTime(2026, 10, 3, 7, 4, 9) - new System.DateTime(2002, 1, 1)).TotalSeconds;
            Assert.Equal("You will be able to use the Assist Channel until 10/3/2026 at 7:04 (JST).", Line(channel, 6380, seconds));
            Assert.Equal("Your Mog Locker lease is valid until 2026/10/3 7:04:09, kupo.", Line(alZahbi, 7409, seconds));
        }

        [Fact]
        public void ZoneNames_ComeFromTheZoneNameTable()
        {
            var rm = Open();
            if (rm == null) return;
            Assert.True(rm.TryGetString(Gordian.Core.Resources.Models.DMsgCategory.ZoneNames, 230, out var name));
            Assert.Equal("Southern San d'Oria", name);
            Assert.True(rm.TryGetString(Gordian.Core.Resources.Models.DMsgCategory.ZoneNamesShort, 231, out var shortName));
            Assert.Equal("N.San d'Oria", shortName);
        }

        [Fact]
        public void SouthernSandoria_EventScript_HoldsTheScriptedEvents()
        {
            var rm = Open();
            if (rm == null) return;
            var bytes = rm.LoadDatBytesByFileId(ZoneEventScript.GetFileId(SouthernSandoria));
            Assert.NotNull(bytes);
            var script = ZoneEventScript.Parse(bytes!);
            Assert.NotNull(script);
            _output.WriteLine($"{script!.Blocks.Count} blocks, {script.Blocks.Sum(b => b.EventIds.Count)} events");
            Assert.Equal(502, script.Blocks.Count);
            Assert.True(script.TryGetBlock(EventBlock.PlayerActor, out var player));
            Assert.True(player.EventIds.Count > 100);

            // Ailevia's talk event (LandSandBoat Ailevia.lua: startEvent(615)) and the home point menu (HomePoint#1.lua: 8700).
            var ailevia = script.FindEvent(615);
            Assert.NotNull(ailevia);
            Assert.Equal(0x010E607Eu, ailevia!.ActorId);
            Assert.True(ailevia.TryGetEvent(615, out int start, out int end));
            Assert.Equal(0x1E, ailevia.Code[start]); // look at the player and talk
            Assert.True(end > start);

            var homePoint = script.FindEvent(8700);
            Assert.NotNull(homePoint);
            Assert.Equal(0x010E6087u, homePoint!.ActorId);
        }
    }
}
