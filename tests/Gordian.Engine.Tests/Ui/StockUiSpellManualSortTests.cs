// tests/Gordian.Engine.Tests/Ui/StockUiSpellManualSortTests.cs
using System;
using System.Collections.Generic;
using System.Linq;
using Gordian.Core.Resources.Ui;
using Gordian.Engine.Actions;
using Gordian.Engine.Ui;
using Xunit;
using static Gordian.Engine.Tests.Ui.StockUiPartyActionsTests;

namespace Gordian.Engine.Tests.Ui
{
    /// <summary>The spell lists' Options > Manual, Trust's among them (#476): the item lists' Manual sort.</summary>
    public class StockUiSpellManualSortTests
    {
        private static StockUiActionRow Row(ushort id, string name) =>
            new(StockUiActionType.Spell, id, name, ActionTargetFlags.Self, 0, 0, 0, null);

        private static StockUiMenuController Controller(List<StockUiActionRow> rows)
        {
            var library = UiResourceLibrary.FromDefinitions(new[]
            {
                List(StockUiActionMenu.SpellListMenu, 16, 96, UiAnchor.BottomLeft, Enumerable.Range(1, StockUiActionMenu.VisibleRows).ToArray()),
                List(StockUiActionMenu.OptionsMenu, 1, 2),
            });
            return new StockUiMenuController { Library = library, ActionRowsProvider = (_, _, _) => rows };
        }

        private static string[] Names(StockUiOpenMenu list) => list.ActionRows.Select(r => r.Name).ToArray();

        private static void To(StockUiMenuController menus, StockUiOpenMenu list, int row)
        {
            list.SelectedButtonId = row + 1;
            menus.Activate();
        }

        private static StockUiOpenMenu ManualList(StockUiMenuController menus)
        {
            var list = menus.OpenActionList(StockUiActionList.Spells, StockUiActionMenu.TrustKind, null)!;
            Assert.True(menus.ToggleActionOptions());
            menus.Top!.SelectedButtonId = StockUiActionMenu.OptionsManualButton;
            menus.Activate();
            Assert.Same(list, menus.Top);
            Assert.True(list.IsManualSort);
            return list;
        }

        [Fact]
        public void Manual_TheFirstPickTakesTheSecondsPlace_UntilCancel()
        {
            var menus = Controller(new() { Row(1, "Shantotto"), Row(2, "Kupipi"), Row(3, "Ayame"), Row(4, "Nanaa") });
            var list = ManualList(menus);

            To(menus, list, 0);
            Assert.Equal(0, list.ManualSortFrom);
            To(menus, list, 2);
            Assert.Null(list.ManualSortFrom);
            Assert.Equal(new[] { "Kupipi", "Ayame", "Shantotto", "Nanaa" }, Names(list));

            // Upwards, and the mode stays for the next pair.
            To(menus, list, 3);
            To(menus, list, 0);
            Assert.Equal(new[] { "Nanaa", "Kupipi", "Ayame", "Shantotto" }, Names(list));
            Assert.True(list.IsManualSort);

            // Cancel leaves the mode for the Options window with Manual under the cursor; a second Cancel returns to the list.
            menus.CloseTop();
            Assert.False(list.IsManualSort);
            Assert.True(menus.Top!.IsActionOptions);
            Assert.Equal(StockUiActionMenu.OptionsManualButton, menus.Top.SelectedButtonId);
            menus.CloseTop();
            Assert.Same(list, menus.Top);
        }

        [Fact]
        public void TheOrderOutlivesTheListAndRefreshes_AndAutoPutsTheTableOrderBack()
        {
            var rows = new List<StockUiActionRow> { Row(1, "Shantotto"), Row(2, "Kupipi"), Row(3, "Ayame") };
            var menus = Controller(rows);
            var list = ManualList(menus);
            To(menus, list, 0);
            To(menus, list, 2);
            menus.CloseTop();
            menus.CloseTop();
            menus.RefreshActionLists();
            Assert.Equal(new[] { "Kupipi", "Ayame", "Shantotto" }, Names(list));

            // A newly learned trust goes after the ordered ones.
            rows.Insert(0, Row(9, "Excenmille"));
            menus.CloseAll();
            list = menus.OpenActionList(StockUiActionList.Spells, StockUiActionMenu.TrustKind, null)!;
            Assert.Equal(new[] { "Kupipi", "Ayame", "Shantotto", "Excenmille" }, Names(list));

            Assert.True(menus.ToggleActionOptions());
            menus.Top!.SelectedButtonId = StockUiActionMenu.OptionsAutoButton;
            menus.Activate();
            Assert.Equal(new[] { "Excenmille", "Shantotto", "Kupipi", "Ayame" }, Names(list));
        }

        [Fact]
        public void ManualConfirmMovesAGreyedRowAndCastsNothing()
        {
            var menus = Controller(new() { Row(1, "Shantotto") with { GreyedReason = "No." }, Row(2, "Kupipi") });
            bool cast = false;
            menus.ActionRequested = _ => { cast = true; return System.Threading.Tasks.Task.FromResult(PlayerActionResult.Ok("", default)); };
            var list = ManualList(menus);
            To(menus, list, 0);
            To(menus, list, 1);
            Assert.Equal(new[] { "Kupipi", "Shantotto" }, Names(list));
            Assert.False(cast);
        }
    }
}
