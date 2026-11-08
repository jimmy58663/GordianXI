// tests/Gordian.App.Tests/Graphics/StockUiMenuHelpBarTests.cs
using Gordian.App.Graphics;
using Gordian.Core.Resources.Models;
using Gordian.Engine.Ui;
using Xunit;

namespace Gordian.App.Tests.Graphics
{
    /// <summary>
    /// The help bar over menus that draw none of their own (#313), against the retail menu DAT and the title and help tables
    /// (ROM/165/76, ROM/165/75), as the maintainer's captures of 2026-10-16/17 show it.
    /// </summary>
    public class StockUiMenuHelpBarTests
    {
        private static StockUiMenuController Menus(StockUiOffscreen screen) => new()
        {
            Library = screen.Library,
            MenuTitleText = id => screen.Resources.TryGetString(DMsgCategory.MenuTitles, id, out var t) ? t : null,
            MenuHelpText = id => screen.Resources.TryGetString(DMsgCategory.MenuHelp, id, out var t) ? t : null,
        };

        [Fact]
        public void ConfigList_TitleStaysConfig_HelpFollowsTheEntry()
        {
            using var screen = StockUiOffscreen.TryCreate(64, 64);
            if (screen == null) return;
            var menus = Menus(screen);
            Assert.True(menus.Open("configwi"));
            for (int i = 0; i < 20 && menus.Top!.SelectedButtonId != 1; i++) menus.Move(Gordian.Engine.Input.InputAction.MenuDown);
            // Gameplay.png: the Config list with the cursor on Gameplay.
            var (title, help) = StockUiMenuHelpBar.Of(menus.Top!, menus.MenuTitleText, menus.MenuHelpText);
            Assert.Equal("Config", title);
            Assert.Equal("Adjust in-game settings exclusive to FINAL FANTASY XI.", help);
        }

        [Fact]
        public void MainMenu_TitleIsTheSelectedEntrysWindow()
        {
            using var screen = StockUiOffscreen.TryCreate(64, 64);
            if (screen == null) return;
            var menus = Menus(screen);
            Assert.True(menus.OpenMainMenu());
            // menuwind's frame has no title; its entries carry the title of the window each opens (Status: title 81, help 321).
            for (int i = 0; i < 20 && menus.Top!.SelectedButtonId != 1; i++) menus.Move(Gordian.Engine.Input.InputAction.MenuDown);
            var (title, help) = StockUiMenuHelpBar.Of(menus.Top!, menus.MenuTitleText, menus.MenuHelpText);
            Assert.Equal("Status", title);
            Assert.Equal("View status.", help);
        }

        [Fact]
        public void ConfirmWindow_StandsOnTheLogAtItsOffset_WhateverTheLogsLinesAndPlace()
        {
            // comyn: 130,208, 252 x 88, authored 114 px right of the log (logwindo, 16). Round 1 of the 2026-11-03 test.
            var comyn = new Gordian.Core.Resources.Ui.UiMenuFrame { X = 130, Y = 208, Width = 252, Height = 88 };
            Assert.Equal((130f, 312f), StockUiWindowPlacement.OnLog(comyn, 16, 400, 1, 1280, 720));
            // A taller log, and a log moved right at scale 2.
            Assert.Equal((130f, 212f), StockUiWindowPlacement.OnLog(comyn, 16, 300, 1, 1280, 720));
            Assert.Equal((328f, 424f), StockUiWindowPlacement.OnLog(comyn, 100, 600, 2, 1280, 720));
            // Never off the screen.
            Assert.Equal(0f, StockUiWindowPlacement.OnLog(comyn, 16, 40, 1, 1280, 720).Y);
        }

        [Fact]
        public void SpellOptionsWindow_KeepsItsPlace_NotTheCommandMenusOffset()
        {
            // Round 2 of the 2026-11-03 test: the Options window slid down by the log's line count when + / Y opened it.
            var frame = new Gordian.Core.Resources.Ui.UiMenuDefinition { Name = "mgcsort", Frame = new Gordian.Core.Resources.Ui.UiMenuFrame { Width = 100, Height = 50 } };
            Assert.True(StockUiWindowPlacement.KeepsOwnPlace(new StockUiOpenMenu(frame, null, Array.Empty<string>(), null) { IsActionOptions = true }));
            Assert.False(StockUiWindowPlacement.KeepsOwnPlace(new StockUiOpenMenu(frame, null, Array.Empty<string>(), null)));
        }

        [Fact]
        public void LongHelpLine_WaitsScrollsWaitsAndJumpsBack()
        {
            const float overflow = 60;
            Assert.Equal(0, StockUiHelpBar.HelpScrollOffset(overflow, 0.5));
            Assert.InRange(StockUiHelpBar.HelpScrollOffset(overflow, 2.5), 1, overflow - 1);
            float end = StockUiHelpBar.HelpScrollOffset(overflow, 1.5 + overflow / 30.0 + 0.5);
            Assert.Equal(overflow, end);
            // After the second pause it starts over.
            Assert.Equal(0, StockUiHelpBar.HelpScrollOffset(overflow, 1.5 + overflow / 30.0 + 1.5 + 0.2));
            Assert.Equal(0, StockUiHelpBar.HelpScrollOffset(0, 5));
        }
    }
}
