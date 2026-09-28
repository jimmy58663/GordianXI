// tests/Gordian.Core.Tests/Ui/StockUiQueryMenuTests.cs
using System;
using System.Collections.Generic;
using System.Linq;
using Gordian.Core.Input;
using Gordian.Core.Resources.Ui;
using Gordian.Core.Ui;
using Xunit;

namespace Gordian.Core.Tests.Ui
{
    /// <summary>The event query window: options over the "query" DAT frame, answered with the option's number.</summary>
    public class StockUiQueryMenuTests
    {
        /// <summary>The retail "query" window: three invisible 132 x 16 rows at (28, 28/48/68) in a 366 x 88 bottom-left frame.</summary>
        private static UiMenuDefinition QueryTemplate() => new()
        {
            Name = StockUiMenuController.QueryMenu,
            Frame = new UiMenuFrame { X = 16, Y = 208, Width = 366, Height = 88, Anchor = UiAnchor.BottomLeft },
            Buttons = new List<UiMenuButton>
            {
                new() { ButtonId = 1, X = 28, Y = 28, Width = 132, Height = 16, NavUp = 1, NavDown = 2, NavLeft = 1, NavRight = 1 },
                new() { ButtonId = 2, X = 28, Y = 48, Width = 132, Height = 16, NavUp = 1, NavDown = 3, NavLeft = 2, NavRight = 2 },
                new() { ButtonId = 3, X = 28, Y = 68, Width = 132, Height = 16, NavUp = 2, NavDown = 3, NavLeft = 3, NavRight = 3 },
            },
        };

        private static StockUiMenuController Controller() => new() { Library = UiResourceLibrary.FromDefinitions(new[] { QueryTemplate() }) };

        private static IReadOnlyList<StockUiQueryOption> Options(params string[] texts) =>
            texts.Select((t, i) => new StockUiQueryOption(i + 1, t)).ToList();

        [Fact]
        public void OpenQuery_BuildsOneRowPerOptionUnderTheComments()
        {
            var menus = Controller();
            int answer = -1;
            var menu = menus.OpenQuery(new[] { "What will you do?" }, Options("Travel.", "Set.", "Other.", "Never mind."), 0, n => answer = n);
            Assert.NotNull(menu);
            Assert.True(menu!.IsQuery);
            // Three rows show at a time (the DAT's), the fourth option scrolls in; with one comment line the window is
            // the authored 366 x 88 (a retail recording of this menu, 2026-09-28).
            Assert.Equal(3, menu.Menu.Buttons.Count);
            Assert.Equal(3, menu.VisibleRows);
            Assert.True(menu.CanScroll);
            Assert.Equal(new[] { "Travel.", "Set.", "Other.", "Never mind." }, menu.Rows.Select(r => r.Text));
            Assert.Equal(28, menu.Menu.Buttons[0].Y);
            Assert.Equal(48, menu.Menu.Buttons[1].Y);
            Assert.Equal(88, menu.Menu.Frame.Height);
            Assert.Equal(366, menu.Menu.Frame.Width);
            Assert.Equal(UiAnchor.BottomLeft, menu.Menu.Frame.Anchor);
            Assert.Equal(1, menu.SelectedButtonId);
            Assert.Equal(-1, answer);
        }

        [Fact]
        public void Confirm_AnswersWithTheOptionNumber_CancelWith255()
        {
            var menus = Controller();
            int answer = -1;
            menus.OpenQuery(Array.Empty<string>(), Options("Yes.", "No."), 1, n => answer = n);
            Assert.Equal(2, menus.Top!.SelectedButtonId); // the cursor starts on the default option
            menus.Move(InputAction.MenuUp);
            Assert.Equal(1, menus.Top.SelectedButtonId);
            menus.Activate();
            Assert.Equal(1, answer);
            Assert.False(menus.IsOpen);

            menus.OpenQuery(Array.Empty<string>(), Options("Yes.", "No."), 0, n => answer = n);
            menus.CloseTop();
            Assert.Equal(255, answer);
        }

        [Fact]
        public void HiddenOptions_KeepTheirNumbers()
        {
            var menus = Controller();
            int answer = -1;
            // The script hid option 2: the window shows options 1 and 3, and choosing the second row reports 3.
            var shown = new[] { new StockUiQueryOption(1, "Travel."), new StockUiQueryOption(3, "Other.") };
            menus.OpenQuery(Array.Empty<string>(), shown, 0, n => answer = n);
            menus.Move(InputAction.MenuDown);
            menus.Activate();
            Assert.Equal(3, answer);
        }

        [Fact]
        public void LongLists_ScrollPastTheVisibleRows()
        {
            var menus = Controller();
            var options = Options(Enumerable.Range(1, 12).Select(i => $"Home Point #{i}").ToArray());
            var menu = menus.OpenQuery(Array.Empty<string>(), options, 0, _ => { })!;
            Assert.Equal(StockUiMenuController.QueryMaxRows, menu.VisibleRows);
            Assert.True(menu.CanScroll);
            for (int i = 0; i < StockUiMenuController.QueryMaxRows - 1; i++) menus.Move(InputAction.MenuDown);
            Assert.Equal(StockUiMenuController.QueryMaxRows, menu.SelectedButtonId);
            Assert.Equal(0, menu.FirstRow);
            menus.Move(InputAction.MenuDown); // past the last visible row: the list scrolls one entry
            Assert.Equal(1, menu.FirstRow);
            Assert.Equal(StockUiMenuController.QueryMaxRows, menu.SelectedButtonId);
            Assert.Equal(StockUiMenuController.QueryMaxRows + 1, menu.QueryOptionNumber(menu.SelectedButtonId));
        }

        [Fact]
        public void CloseQuery_RemovesTheWindowWithoutAnswering()
        {
            var menus = Controller();
            int answer = -1;
            var menu = menus.OpenQuery(Array.Empty<string>(), Options("A", "B"), 0, n => answer = n)!;
            menus.CloseQuery(menu);
            Assert.False(menus.IsOpen);
            Assert.Equal(-1, answer);
        }

        [Fact]
        public void OpenQuery_WithoutTheLibrary_ReturnsNull()
        {
            var menus = new StockUiMenuController();
            Assert.Null(menus.OpenQuery(Array.Empty<string>(), Options("A"), 0, _ => { }));
        }
    }
}
