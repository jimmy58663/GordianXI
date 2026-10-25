// tests/Gordian.Engine.Tests/Ui/StockUiStatusTests.cs
using System.Linq;
using Gordian.Engine.Ui;
using Xunit;

namespace Gordian.Engine.Tests.Ui
{
    /// <summary>The Status window's data (#23): the job rows, the skill lines from S2C 0x062's words, and the level box's numbers.</summary>
    public class StockUiStatusTests
    {
        [Fact]
        public void JobRows_LeftColumnOddIds_RightColumnEvenIds()
        {
            Assert.Equal(1, StockUiStatus.JobOfRow(1));   // Warrior
            Assert.Equal(3, StockUiStatus.JobOfRow(2));   // White Mage
            Assert.Equal(21, StockUiStatus.JobOfRow(11)); // Geomancer
            Assert.Equal(2, StockUiStatus.JobOfRow(12));  // Monk
            Assert.Equal(18, StockUiStatus.JobOfRow(20)); // Puppetmaster
            Assert.Equal(22, StockUiStatus.JobOfRow(22)); // Rune Fencer
            Assert.Equal(0, StockUiStatus.JobOfRow(23));
        }

        [Fact]
        public void SkillLines_LeaveOutUntrainedSkills_AndReadTheCapBit()
        {
            var raw = new ushort[64];
            raw[1] = 424 | 0x8000; // Hand-to-Hand, capped
            raw[3] = 144;          // Sword
            raw[22] = 300;         // the automaton's melee skill is not a Combat Skills line
            var lines = StockUiStatus.SkillLines(StockUiStatus.CombatSkills, raw);
            Assert.Equal(new[] { "Hand-to-Hand", "Sword" }, lines.Select(l => l.Name));
            Assert.Equal(424, lines[0].Level);
            Assert.True(lines[0].Capped);
            Assert.False(lines[1].Capped);
        }

        [Fact]
        public void CraftLines_ListEveryCraft_WithRankAndLevel()
        {
            var raw = new ushort[64];
            raw[49] = (ushort)(60 * 0x20 + 6); // Woodworking 60, Craftsman
            raw[48] = 0xFFFF;                  // unused entry
            var lines = StockUiStatus.CraftLines(raw);
            Assert.Equal(10, lines.Count);
            Assert.Equal(("Fishing", 0, "Amateur"), (lines[0].Name, lines[0].Level, lines[0].Rank));
            Assert.Equal(("Woodworking", 60, "Craftsman"), (lines[1].Name, lines[1].Level, lines[1].Rank));
            Assert.Equal("Synergy", lines[^1].Name);
        }

        [Fact]
        public void Numbers_NamesAndPages()
        {
            Assert.Equal("55,999", StockUiStatus.Number(55999));
            Assert.Equal("San d'Oria", StockUiStatus.NationName(0));
            Assert.Equal("Tarutaru", StockUiStatus.RaceName(6));
            Assert.Equal(StockUiStatus.MagicSkillMenu, StockUiStatus.MenuOf(StockUiStatusPage.CraftSkills));
            Assert.Equal(StockUiStatus.ProfileMenu, StockUiStatus.MenuOf(StockUiStatusPage.Profile));
        }

        [Fact]
        public void MainMenuStatus_OpensTheStatusList()
        {
            Assert.True(StockUiMenuEntries.TryGet(StockUiMenuEntries.MainMenu, 1, out var status));
            Assert.Equal(StockUiStatus.CommandMenu, status.Opens);
            Assert.True(StockUiMenuEntries.TryGet(StockUiStatus.CommandMenu, StockUiStatus.CraftSkillsButton, out var craft));
            Assert.Equal(StockUiMenuCommand.StatusPage, craft.Command);
            Assert.Equal((int)StockUiStatusPage.CraftSkills, craft.Argument);
        }
    }
}
