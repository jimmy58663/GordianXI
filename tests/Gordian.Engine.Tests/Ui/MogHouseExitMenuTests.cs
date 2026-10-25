using System.IO;
using System.Linq;
using Gordian.Core.Network.Packets;
using Gordian.Core.Resources;
using Gordian.Core.Resources.Models;
using Gordian.Core.World;
using Gordian.Engine.Ui;
using Xunit;

namespace Gordian.Engine.Tests.Ui
{
    /// <summary>The Mog House exit menu and its area list (#354, image-23 of round 1).</summary>
    public class MogHouseExitMenuTests
    {
        private const string GameDirectory = @"G:\Program Files (x86)\PlayOnline\SquareEnix\FINAL FANTASY XI";

        [Fact]
        public void NoExitUnlocked_IsTheYesNoPrompt()
        {
            Assert.False(MogHouseExitMenu.HasMenu(MogHouseExitBit.Default));
            Assert.Empty(MogHouseExitMenu.AreaOptions(MogHouseExitBit.Default, 230, _ => "x", out int cursor));
            Assert.Equal(0, cursor);
            Assert.True(MogHouseExitMenu.HasMenu(MogHouseExitBit.Adoulin));
        }

        [Fact]
        public void MainMenu_HasRetailsRows_ChangeFloorsGreyedOnTheFirstFloor()
        {
            var rows = MogHouseExitMenu.MainOptions(null, canChangeFloors: false);
            Assert.Equal(new[] { "Stay in your room.", "Area you entered from.", "Change floors.", "Select an area to exit to." }, rows.Select(o => o.Text));
            Assert.Equal(new[] { MogHouseExitMenu.Stay, MogHouseExitMenu.AreaEnteredFrom, MogHouseExitMenu.ChangeFloors, MogHouseExitMenu.SelectArea }, rows.Select(o => o.Number));
            Assert.Equal(new[] { false, false, true, false }, rows.Select(o => o.Greyed));
            Assert.False(MogHouseExitMenu.MainOptions(null, canChangeFloors: true)[2].Greyed);
            Assert.Equal("Which area do you want to exit to?", MogHouseExitMenu.Question(_ => null));
        }

        [Fact]
        public void Floors_FollowTheLoginPacket()
        {
            // LandSandBoat sends MyroomSubMapNumber 2 on the second floor; the client keeps (n >> 1) & 3.
            Assert.False(MogHouseRooms.CanChangeFloors(0));
            Assert.True(MogHouseRooms.CanChangeFloors(2));
            Assert.Equal(MogHouseExitMode.Mog2F, MogHouseRooms.FloorChangeMode(0x124));
            Assert.Equal(MogHouseExitMode.Mog1F, MogHouseRooms.FloorChangeMode(0x268));
        }

        [Fact]
        public void AreaList_IsTheCitysAreasThenTheMogGarden_WithTheCursorOnTheAreaEnteredFrom()
        {
            string? Name(int zone) => zone switch { 256 => "Western Adoulin", 257 => "Eastern Adoulin", 280 => "Mog Garden", _ => null };
            var options = MogHouseExitMenu.AreaOptions(MogHouseExitBit.Adoulin, 257, Name, out int cursor);
            Assert.Equal(new[] { "Western Adoulin", "Eastern Adoulin", "Mog Garden" }, options.Select(o => o.Text));
            Assert.Equal(new[] { 1, 2, 127 }, options.Select(o => o.Number));
            Assert.Equal(1, cursor);

            var jeuno = MogHouseExitMenu.AreaOptions(MogHouseExitBit.Jeuno, 245, null, out cursor);
            Assert.Equal(new[] { "Zone 243", "Zone 244", "Zone 245", "Zone 246", "Zone 280" }, jeuno.Select(o => o.Text));
            Assert.Equal(2, cursor);
        }

        [Fact]
        public void AreaAnswers_MapToExitModes_CancelToNothing()
        {
            Assert.Equal(MogHouseExitMode.Option1, MogHouseExitMenu.AreaModeOf(1, 4));
            Assert.Equal(MogHouseExitMode.Option4, MogHouseExitMenu.AreaModeOf(4, 4));
            Assert.Equal(MogHouseExitMode.MogGarden, MogHouseExitMenu.AreaModeOf(127, 2));
            Assert.Null(MogHouseExitMenu.AreaModeOf(255, 4));
            Assert.Null(MogHouseExitMenu.AreaModeOf(3, 2));
            Assert.Null(MogHouseExitMenu.AreaModeOf(0, 2));
        }

        [Fact]
        public void Texts_MatchTheMenuMessageTable()
        {
            if (!Directory.Exists(GameDirectory)) return;
            var rm = new ResourceManager(GameDirectory);
            string? Text(int id) => rm.TryGetString(DMsgCategory.MenuMessages, id, out var t) ? t : null;
            Assert.Equal(MogHouseExitMenu.QuestionFallback, Text(MogHouseExitMenu.QuestionText));
            Assert.Equal(MogHouseExitMenu.MainOptions(null, false).Select(o => o.Text), MogHouseExitMenu.MainOptions(Text, false).Select(o => o.Text));
            Assert.True(rm.TryGetString(DMsgCategory.ZoneNames, MogHouseRooms.MogGardenZone, out var garden));
            Assert.Equal("Mog Garden", garden);
        }
    }
}
