// tests/Gordian.Engine.Tests/Ui/ZoneArrivalLogTests.cs
using System.Collections.Generic;
using Gordian.Engine.Ui;
using Xunit;

namespace Gordian.Engine.Tests.Ui
{
    /// <summary>The zone-name line on zone-in (#579), as retail prints it (local LSB capture 2026-11-15).</summary>
    public class ZoneArrivalLogTests
    {
        private static string? Names(int zone) => zone switch { 102 => "La Theine Plateau", 260 => "Yahse Hunting Grounds", _ => null };

        [Fact]
        public void EachZoneIn_PrintsOneLine_SameZoneIncluded()
        {
            var lines = new List<string>();
            var log = new ZoneArrivalLog(lines.Add);
            log.OnZoneEntered(260, Names);
            log.OnZoneEntered(102, Names);
            log.OnZoneEntered(102, Names);
            Assert.Equal(new[] { "=== Area: Yahse Hunting Grounds ===", "=== Area: La Theine Plateau ===", "=== Area: La Theine Plateau ===" }, lines);
        }

        [Fact]
        public void FirstLogin_BeforeTheNameTable_IsHeldUntilItIsAttached_Once()
        {
            var lines = new List<string>();
            var log = new ZoneArrivalLog(lines.Add);
            log.OnZoneEntered(102, null);
            Assert.Empty(lines);
            log.OnZoneNamesAttached(Names);
            log.OnZoneNamesAttached(Names);
            Assert.Equal(new[] { "=== Area: La Theine Plateau ===" }, lines);
        }

        [Fact]
        public void UnknownZone_PrintsNothing()
        {
            var lines = new List<string>();
            new ZoneArrivalLog(lines.Add).OnZoneEntered(9999, Names);
            Assert.Empty(lines);
        }
    }
}
