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
