using System.Linq;
using Gordian.Core.Network.Packets;
using Gordian.Engine.Ui;
using Xunit;

namespace Gordian.Engine.Tests.Ui
{
    /// <summary>The Mog House exit's area list (#354).</summary>
    public class MogHouseExitMenuTests
    {
        [Fact]
        public void NoExitUnlocked_HasNoAreas()
        {
            var options = MogHouseExitMenu.Options(MogHouseExitBit.Default, 230, _ => "x", out int cursor);
            Assert.Empty(options);
            Assert.Equal(0, cursor);
        }

        [Fact]
        public void CityAreas_AreNumberedByExitMode_WithTheCursorOnTheAreaEnteredFrom()
        {
            string? Name(int zone) => zone switch { 230 => "Southern San d'Oria", 231 => "Northern San d'Oria", _ => null };
            var options = MogHouseExitMenu.Options(MogHouseExitBit.SandOria, 231, Name, out int cursor);
            Assert.Equal(new[] { 1, 2, 3 }, options.Select(o => o.Number));
            Assert.Equal(new[] { "Southern San d'Oria", "Northern San d'Oria", "Zone 232" }, options.Select(o => o.Text));
            Assert.Equal(1, cursor);
        }

        [Fact]
        public void Answers_MapToOptionModes_CancelToNothing()
        {
            Assert.Equal(MogHouseExitMode.Option1, MogHouseExitMenu.ModeOf(1, 4));
            Assert.Equal(MogHouseExitMode.Option4, MogHouseExitMenu.ModeOf(4, 4));
            Assert.Null(MogHouseExitMenu.ModeOf(255, 4));
            Assert.Null(MogHouseExitMenu.ModeOf(3, 2));
            Assert.Null(MogHouseExitMenu.ModeOf(0, 2));
        }
    }
}
