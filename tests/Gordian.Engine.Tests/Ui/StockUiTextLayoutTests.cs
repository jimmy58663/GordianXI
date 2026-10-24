// tests/Gordian.Engine.Tests/Ui/StockUiTextLayoutTests.cs
using System.Linq;
using Gordian.Core.Network.Packets;
using Gordian.Core.Resources.Models;
using Gordian.Engine.Ui;
using Xunit;

namespace Gordian.Engine.Tests.Ui
{
    /// <summary>The shared stock UI text layout (<see cref="StockUiTextLayout"/>) and the item descriptions on it (round 4, image-17).</summary>
    public class StockUiTextLayoutTests
    {
        /// <summary>A measure where capitals are twice as wide as other characters, like the log font's.</summary>
        private static float Wide(string text) => text.Sum(c => char.IsUpper(c) ? 10f : 5f);

        [Fact]
        public void Wrap_BreaksAtSpacesByMeasuredWidth_KeepsLineBreaks_AndCapsTheLines()
        {
            var lines = StockUiTextLayout.Wrap("aaa bbb ccc\nddd", t => t.Length, 7);
            Assert.Equal(new[] { "aaa bbb", "ccc", "ddd" }, lines);
            Assert.All(StockUiTextLayout.Wrap("AAAA BBBB CCCC", Wide, 50), l => Assert.True(Wide(l) <= 50, l));
            Assert.Equal(2, StockUiTextLayout.Wrap("a b c d e f", t => t.Length, 1, maxLines: 2).Count);
            Assert.Empty(StockUiTextLayout.Wrap(null, t => t.Length, 10));
            // Blank lines go unless asked for (key item texts keep them, but not at the start or end).
            Assert.Equal(new[] { "a", "b" }, StockUiTextLayout.Wrap("\na\n\nb\n", t => t.Length, 10));
            Assert.Equal(new[] { "a", "", "b" }, StockUiTextLayout.Wrap("\na\n\nb\n", t => t.Length, 10, keepBlankLines: true));
        }

        [Fact]
        public void Wrap_BreaksAWordWiderThanTheBox_SoNoLineIsWider()
        {
            var lines = StockUiTextLayout.Wrap("abcdefghij xy", t => t.Length, 4);
            Assert.Equal(new[] { "abcd", "efgh", "ij", "xy" }, lines);
            Assert.All(lines, l => Assert.True(l.Length <= 4));
        }

        [Fact]
        public void LinesIn_CountsTheLinesABoxHolds()
        {
            Assert.Equal(12, StockUiTextLayout.LinesIn(196, 16));
            Assert.Equal(0, StockUiTextLayout.LinesIn(10, 16));
            Assert.Equal(0, StockUiTextLayout.LinesIn(10, 0));
        }

        /// <summary>
        /// The Leather Bandana's fifteen jobs (image-17): the old 42-column rule put 40 characters on the first line, wider than
        /// the 310 px text width in a font whose capitals are wide; the shared layout wraps them to the width as measured.
        /// </summary>
        [Fact]
        public void ItemDescription_WrapsTheJobListToTheWindowWidth_ByTheFontsMeasure()
        {
            const uint jobs = (1u << 1) | (1u << 5) | (1u << 6) | (1u << 7) | (1u << 8) | (1u << 9) | (1u << 10) | (1u << 11) | (1u << 12)
                | (1u << 13) | (1u << 14) | (1u << 16) | (1u << 17) | (1u << 19) | (1u << 22);
            var record = new ItemRecord
            {
                ItemId = 12440, Name = "Leather Bandana", LogName = "leather bandana", Level = 7, JobsMask = jobs, RacesMask = 0x1FE,
                EquipSlotsMask = 1 << (int)EquipSlotId.Head, ItemType = 5, Description = "DEF:3",
            };
            // Without a font (about 42 characters to the line) the first line is image-17's 40 characters, too wide in wide capitals.
            string old = StockUiItemDescription.LevelLines(record)[0];
            Assert.Equal("Lv.7 WAR RDM THF PLD DRK BST BRD RNG SAM", old);
            Assert.True(Wide(old) > StockUiItemDescription.TextWidth);
            var level = StockUiItemDescription.LevelLines(record, Wide);
            Assert.True(level.Count >= 2);
            Assert.All(level, l => Assert.True(Wide(l) <= StockUiItemDescription.TextWidth, l));
            var page = Assert.Single(StockUiItemDescription.Pages(record, Wide));
            Assert.All(page.Lines, l => Assert.True(Wide(l) <= StockUiItemDescription.TextWidth, l));
            Assert.Equal(new[] { "Leather bandana", "[Head]All Races", "DEF:3" }, page.Lines.Take(3));
        }
    }
}
