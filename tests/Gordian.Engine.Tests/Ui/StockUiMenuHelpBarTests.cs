// tests/Gordian.Engine.Tests/Ui/StockUiMenuHelpBarTests.cs
using System.Collections.Generic;
using Gordian.Core.Resources.Ui;
using Gordian.Engine.Input;
using Gordian.Engine.Ui;
using Xunit;

namespace Gordian.Engine.Tests.Ui
{
    /// <summary>Which title and help line the generic help bar shows over a menu (#313).</summary>
    public class StockUiMenuHelpBarTests
    {
        private static readonly Dictionary<int, string> Titles = new() { [1] = "Frame", [2] = "Entry", [3] = "Other" };
        private static readonly Dictionary<int, string> Helps = new() { [10] = "Frame help.", [11] = "Entry help." };

        private static UiMenuDefinition Menu(string name, int frameTitle, int frameHelp, params (int Title, int Help)[] buttons)
        {
            var list = new List<UiMenuButton>();
            for (int i = 0; i < buttons.Length; i++)
            {
                list.Add(new UiMenuButton
                {
                    ButtonId = (short)(i + 1), X = 16, Y = (short)(6 + 16 * i), Width = 88, Height = 16,
                    NavUp = (sbyte)(i == 0 ? buttons.Length : i), NavDown = (sbyte)(i + 1 == buttons.Length ? 1 : i + 2),
                    NavLeft = (sbyte)(i + 1), NavRight = (sbyte)(i + 1),
                    TitleTextId = buttons[i].Title, HelpTextId = buttons[i].Help,
                });
            }
            return new UiMenuDefinition
            {
                Name = name,
                Frame = new UiMenuFrame { X = 384, Y = 48, Width = 112, Height = 60, TitleTextId = frameTitle, HelpTextId = frameHelp },
                Buttons = list,
            };
        }

        private static (string? Title, string? Help) BarOver(params UiMenuDefinition[] menus)
        {
            var controller = new StockUiMenuController { Library = UiResourceLibrary.FromDefinitions(menus) };
            foreach (var menu in menus) Assert.True(controller.Open(menu.Name));
            return StockUiMenuHelpBar.Of(controller.Top!, id => Titles.GetValueOrDefault(id), id => Helps.GetValueOrDefault(id));
        }

        [Fact]
        public void FrameTitleWins_OverTheEntrysTitle()
        {
            Assert.Equal(("Frame", "Entry help."), BarOver(Menu("a", 1, 10, (2, 11), (3, -1))));
        }

        [Fact]
        public void WithoutAFrameTitle_TheSelectedEntryNamesIt()
        {
            Assert.Equal(("Entry", "Entry help."), BarOver(Menu("a", -1, 10, (2, 11))));
        }

        [Fact]
        public void EntryWithoutHelp_ShowsTheFramesHelp()
        {
            Assert.Equal(("Frame", "Frame help."), BarOver(Menu("a", 1, 10, (-1, -1))));
        }

        [Fact]
        public void WindowWithNoTitle_TakesItsParents()
        {
            Assert.Equal(("Entry", null), BarOver(Menu("parent", -1, -1, (2, 11)), Menu("child", -1, -1, (-1, -1))));
        }
    }
}
