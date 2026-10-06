// tests/Gordian.Core.Tests/Ui/StockUiLogPageTests.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Gordian.Core.Input;
using Gordian.Core.Resources.Ui;
using Gordian.Core.Ui;
using Xunit;

namespace Gordian.Core.Tests.Ui
{
    public class StockUiLogPageTests
    {
        private static UiMenuButton Button(int id, short x, short y, short w, short h, int up, int down, int left, int right) => new()
        {
            ButtonId = (short)id, X = x, Y = y, Width = w, Height = h,
            NavUp = (sbyte)up, NavDown = (sbyte)down, NavLeft = (sbyte)left, NavRight = (sbyte)right,
        };

        private static UiMenuDefinition Menu(string name, short x, short y, UiAnchor anchor, short width, short height, IEnumerable<UiMenuButton> buttons) => new()
        {
            Name = name,
            Frame = new UiMenuFrame { X = x, Y = y, Width = width, Height = height, Anchor = anchor },
            Buttons = buttons.ToList(),
        };

        private static IEnumerable<UiMenuButton> Ring(int count, short x, short y0, short pitch, short width) =>
            Enumerable.Range(1, count).Select(i => Button(i, x, (short)(y0 + pitch * (i - 1)), width, 16, i == 1 ? count : i - 1, i == count ? 1 : i + 1, i, i));

        /// <summary>The Log and Effects windows as the English menu DAT authors them (ROM/119/51).</summary>
        private static StockUiMenuController Controller() => new()
        {
            Library = UiResourceLibrary.FromDefinitions(new[]
            {
                Menu(StockUiConfigPages.LogWindowMenu, 384, 48, UiAnchor.TopRight, 112, 58,
                    Ring(3, 16, 6, 16, 88).Append(Button(4, 0, 0, 112, 8, -1, -1, -1, -1))),
                Menu(StockUiConfigPages.LogCategoryMenu, 384, 48, UiAnchor.TopRight, 112, 74,
                    Ring(4, 16, 6, 16, 88).Append(Button(5, 0, 0, 112, 8, -1, -1, -1, -1))),
                Menu(StockUiConfigPages.LogListMenu, 16, 48, UiAnchor.TopLeft, 366, 248, Ring(14, 34, 21, 16, 256)),
                Menu(StockUiConfigPages.EffectsPage, 16, 48, UiAnchor.TopLeft, 366, 248,
                    Enumerable.Range(1, 14).Select(i => Button(i, 34, (short)(21 + 16 * (i - 1)), 256, 16, i == 1 ? 16 : i - 1, i == 14 ? 26 : i + 1, i == 1 ? 16 : i - 1, i == 14 ? 26 : i + 1))),
                Menu(StockUiMenuEntries.YesNoMenu, 16, 256, UiAnchor.BottomLeft, 112, 42, Ring(2, 16, 6, 16, 88)),
            }),
        };

        [Fact]
        public void DefaultRouting_SendsTheBattleTypesToWindow2()
        {
            uint mask = StockUiChatLog.DefaultWindow2Types;
            foreach (var type in Enum.GetValues<ChatLogType>())
            {
                bool battle = type >= ChatLogType.SelfRecover && type <= ChatLogType.CallsForHelp;
                Assert.Equal(battle, (mask & StockUiChatLog.Bit(type)) != 0);
            }
            Assert.Equal((int)mask, new StockUiSettings().GetValue(StockUiSettingKey.LogWindow2Types));
        }

        [Fact]
        public void Log_RoutesEachLineByItsType()
        {
            uint window2 = StockUiChatLog.Bit(ChatLogType.Party) | StockUiChatLog.Bit(ChatLogType.SelfLose);
            var log = new StockUiChatLog { MultiWindow = true, Window2Types = () => window2 };
            var t = new DateTime(2026, 10, 4, 12, 0, 0);
            log.Add(new ChatLogLine(ChatLogChannel.Say, "say", t));
            log.Add(new ChatLogLine(ChatLogChannel.Party, "party", t.AddSeconds(1)));
            log.Add(new ChatLogLine(ChatLogChannel.Combat, "you take 5", t.AddSeconds(2), StockUiFontColorId.SelfDamage, ChatLogType.SelfLose));
            log.Add(new ChatLogLine(ChatLogChannel.Combat, "it takes 5", t.AddSeconds(3), StockUiFontColorId.OthersDamage, ChatLogType.OthersLose));

            var lines = new List<ChatLogLine>();
            log.CopyVisible(1, 10, lines);
            Assert.Equal(new[] { "say", "it takes 5" }, lines.Select(l => l.Text));
            log.CopyVisible(2, 10, lines);
            Assert.Equal(new[] { "party", "you take 5" }, lines.Select(l => l.Text));

            // Without multi-window, Window 1 shows everything in order.
            log.MultiWindow = false;
            log.CopyVisible(1, 10, lines);
            Assert.Equal(4, lines.Count);
        }

        [Fact]
        public void LogPage_PicksAWindowThenACategory_AndRowsMoveTypesBetweenWindows()
        {
            var menus = Controller();
            Assert.True(menus.Open(StockUiConfigPages.LogWindowMenu));
            Assert.Equal(1, menus.Top!.SelectedButtonId);
            menus.Activate();                                   // Window 1
            Assert.Equal(StockUiConfigPages.LogCategoryMenu, menus.Top!.Name);
            Assert.Equal(1, menus.Top.LogWindow);
            menus.Activate();                                   // Chat
            var list = menus.Top!;
            Assert.Equal(StockUiListKind.LogRouting, list.ListKind);
            Assert.Equal(13, list.Rows.Count);
            Assert.Equal("Immediate vicinity (\"Say\")", list.Rows[0].Text);
            Assert.All(list.Rows, r => Assert.True(r.Marked));  // chat is in Window 1 by default

            menus.Move(InputAction.MenuDown);
            menus.Move(InputAction.MenuDown);                   // Party (retail order: Say, Tell, Party ...)
            Assert.Equal(ChatLogType.Party, list.LogRows[list.EntryIndex(list.SelectedButtonId)].Type);
            menus.Activate();                                   // OFF in Window 1 = to Window 2
            Assert.False(list.Rows[2].Marked);
            uint mask = (uint)menus.Settings.GetValue(StockUiSettingKey.LogWindow2Types);
            Assert.NotEqual(0u, mask & StockUiChatLog.Bit(ChatLogType.Party));

            // In Window 2's list the row shows ON; the battle rows too.
            menus.CloseTop();
            menus.CloseTop();
            menus.Move(InputAction.MenuDown);                   // Window 2
            menus.Activate();
            Assert.Equal(2, menus.Top!.LogWindow);
            menus.Activate();                                   // Chat
            Assert.True(menus.Top!.Rows[2].Marked);
            Assert.False(menus.Top.Rows[0].Marked);
            menus.CloseTop();
            menus.Move(InputAction.MenuDown);                   // For Self
            menus.Activate();
            Assert.Equal(6, menus.Top!.Rows.Count);
            Assert.All(menus.Top.Rows, r => Assert.True(r.Marked));

            // Down from the last chat row wraps (thirteen rows in fourteen).
            menus.CloseTop();
            menus.Move(InputAction.MenuUp);
            menus.Activate();
            for (int i = 0; i < 12; i++) menus.Move(InputAction.MenuDown);
            Assert.Equal(13, menus.Top!.SelectedButtonId);
            menus.Move(InputAction.MenuDown);
            Assert.Equal(1, menus.Top.SelectedButtonId);
        }

        [Fact]
        public async Task LogPage_DefaultAsksThenRestoresTheRouting()
        {
            var menus = Controller();
            menus.Settings.SetValue(StockUiSettingKey.LogWindow2Types, 0);
            Assert.True(menus.Open(StockUiConfigPages.LogWindowMenu));
            menus.Move(InputAction.MenuUp);                     // Default
            Assert.Equal(StockUiConfigPages.LogDefaultButton, menus.Top!.SelectedButtonId);
            menus.Activate();
            Assert.Equal(StockUiMenuEntries.YesNoMenu, menus.Top!.Name);
            menus.Move(InputAction.MenuUp);                     // Yes
            menus.Activate();
            await Task.Delay(50, TestContext.Current.CancellationToken);
            Assert.Equal((int)StockUiChatLog.DefaultWindow2Types, menus.Settings.GetValue(StockUiSettingKey.LogWindow2Types));
        }

        [Fact]
        public void EffectsPage_RowsToggleTheirFilterBits_AndTheListScrolls()
        {
            var menus = Controller();
            Assert.True(menus.Open(StockUiConfigPages.EffectsPage));
            var list = menus.Top!;
            Assert.Equal(StockUiListKind.Effects, list.ListKind);
            Assert.Equal(18, list.Rows.Count);
            Assert.True(list.CanScroll);
            Assert.All(list.Rows, r => Assert.False(r.Marked));
            menus.Activate();
            Assert.Equal(1, menus.Settings.GetValue(StockUiSettingKey.EffectFilters));
            Assert.True(list.Rows[0].Marked);
            for (int i = 0; i < 17; i++) menus.Move(InputAction.MenuDown);
            Assert.Equal(4, list.FirstRow);
            Assert.Equal("Screen shaking", list.Rows[list.EntryIndex(list.SelectedButtonId)].Text);
            menus.Activate();
            Assert.Equal(1 | (1 << 17), menus.Settings.GetValue(StockUiSettingKey.EffectFilters));
        }
    }
}
