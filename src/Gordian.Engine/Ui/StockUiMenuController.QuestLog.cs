// src/Gordian.Engine/Ui/StockUiMenuController.QuestLog.cs
using System;
using System.Collections.Generic;
using Gordian.Core.Resources.Ui;

namespace Gordian.Engine.Ui
{
    /// <summary>Which step of the Missions, Quests or Region Info windows an open menu is (#488).</summary>
    public enum StockUiQuestLogView
    {
        None,

        /// <summary>A Missions group's storylines (<c>miss00</c>).</summary>
        Storylines,

        /// <summary>The Quests window's areas (<c>quest01</c>).</summary>
        Areas,

        /// <summary>An area's Current / Completed choice (<c>quest00</c>).</summary>
        QuestKind,

        /// <summary>A storyline's missions (<c>missiton</c>) or an area's quests (<c>quset</c>).</summary>
        List,

        /// <summary>A mission's orders or a quest's summary, in the list's window.</summary>
        Description,

        /// <summary>Region Info's Conquest page (<see cref="StockUiConquestView"/>).</summary>
        Conquest,
    }

    public sealed partial class StockUiOpenMenu
    {
        public StockUiQuestLogView QuestLogView { get; init; }

        public bool IsQuestLogWindow => QuestLogView != StockUiQuestLogView.None;

        /// <summary>A picker, list or description of the quest log; the Conquest page is apart.</summary>
        public bool IsQuestLogList => QuestLogView is StockUiQuestLogView.Storylines or StockUiQuestLogView.Areas or StockUiQuestLogView.List;

        /// <summary>Whether this belongs to the Quests window (else the Missions window).</summary>
        public bool IsQuestsWindow { get; init; }

        /// <summary>The books a Storylines or Areas picker lists, parallel to <see cref="Rows"/>.</summary>
        public IReadOnlyList<StockUiQuestLogBook> QuestLogBooks { get; init; } = Array.Empty<StockUiQuestLogBook>();

        /// <summary>The book a Current / Completed choice, a list or a description is for.</summary>
        public StockUiQuestLogBook? QuestLogBook { get; init; }

        /// <summary>Whether a quest list (and its description) shows the completed quests.</summary>
        public bool QuestLogCompleted { get; init; }

        /// <summary>A list's rows, parallel to <see cref="Rows"/>.</summary>
        public IReadOnlyList<StockUiQuestLogRow> QuestLogRows { get; internal set; } = Array.Empty<StockUiQuestLogRow>();

        /// <summary>The quest or mission a description shows.</summary>
        public StockUiQuestLogRow? QuestLogShown { get; init; }

        /// <summary>The help bar's line: the group, area or list title ("San d'Oria: Current Quests").</summary>
        public string? QuestLogHeading { get; init; }

        /// <summary>The Conquest page's contents, rebuilt when S2C 0x05E changes.</summary>
        public StockUiConquestView? ConquestView { get; internal set; }

        /// <summary>The row under a list's cursor, if any.</summary>
        public StockUiQuestLogRow? SelectedQuestLogRow
        {
            get
            {
                int index = EntryIndex(SelectedButtonId);
                return QuestLogView == StockUiQuestLogView.List && index >= 0 && index < QuestLogRows.Count ? QuestLogRows[index] : null;
            }
        }

        /// <summary>The window title the help bar shows: "Missions", "Quests" or "Region Info".</summary>
        public string QuestLogTitle => QuestLogView == StockUiQuestLogView.Conquest ? StockUiConquest.Title
            : IsQuestsWindow ? StockUiQuestLog.QuestsTitle : StockUiQuestLog.MissionsTitle;
    }

    public sealed partial class StockUiMenuController
    {
        /// <summary>The quest and mission name tables; null without the game's DATs. Set by the HUD.</summary>
        public Func<StockUiQuestLogCatalog?>? QuestLogCatalog { get; set; }

        /// <summary>The quest log as S2C 0x056 left it (a fresh copy each call).</summary>
        public Func<StockUiQuestLogState?>? QuestLogState { get; set; }

        /// <summary>Region Info's Conquest page for where the character is now (S2C 0x05E).</summary>
        public Func<StockUiConquestView?>? ConquestView { get; set; }

        private bool TryGetQuestLog(out StockUiQuestLogCatalog catalog, out StockUiQuestLogState state)
        {
            catalog = QuestLogCatalog?.Invoke()!;
            state = QuestLogState?.Invoke()!;
            if (catalog != null && state != null) return true;
            NoticePosted?.Invoke("Quest log: the quest and mission tables are not loaded in this session.");
            return false;
        }

        /// <summary>
        /// A quest log window over <paramref name="name"/>'s frame with <paramref name="rows"/> client-drawn rows at x, y +
        /// pitch n (w x h), the end rows linked to themselves so up / down past them scroll; no rows for a description page.
        /// </summary>
        private UiMenuDefinition? QuestLogDefinition(string name, int rows, int x, int y, int pitch, int width, int height)
        {
            var library = _library;
            if (library == null || !library.TryGetMenu(name, out var window)) return null;
            var buttons = new List<UiMenuButton>(rows);
            for (int i = 1; i <= rows; i++)
            {
                buttons.Add(new UiMenuButton
                {
                    ButtonId = (short)i,
                    X = (short)x,
                    Y = (short)(y + (i - 1) * pitch),
                    Width = (short)width,
                    Height = (short)height,
                    NavUp = (sbyte)(i == 1 ? 1 : i - 1),
                    NavDown = (sbyte)(i == rows ? i : i + 1),
                    NavLeft = (sbyte)i,
                    NavRight = (sbyte)i,
                });
            }
            var frame = window.Frame;
            return new UiMenuDefinition
            {
                DatId = window.DatId,
                Category = window.Category,
                Name = window.Name,
                MenuType = window.MenuType,
                // missiton authors its cursor 160 px left of the rows; the rows' own place is used instead (PROVISIONAL).
                Frame = new UiMenuFrame
                {
                    X = frame.X, Y = frame.Y, Width = frame.Width, Height = frame.Height, AuthoredHeight = frame.AuthoredHeight,
                    DrawOffsetX = frame.DrawOffsetX, DrawOffsetY = frame.DrawOffsetY, Anchor = frame.Anchor, Shapes = frame.Shapes,
                    HelpTextId = frame.HelpTextId, TitleTextId = frame.TitleTextId,
                },
                Buttons = buttons,
            };
        }

        private UiMenuDefinition? PickerDefinition(string name, int rows) => QuestLogDefinition(name, Math.Min(rows, StockUiQuestLog.PickerRows),
            StockUiQuestLog.PickerRowX, StockUiQuestLog.PickerRowY, StockUiQuestLog.PickerRowPitch, StockUiQuestLog.PickerRowWidth, StockUiQuestLog.PickerRowHeight);

        private UiMenuDefinition? ListDefinition(string name, int rows) => QuestLogDefinition(name, Math.Min(rows, StockUiQuestLog.ListRows),
            StockUiQuestLog.ListRowX, StockUiQuestLog.ListRowY, StockUiQuestLog.ListRowPitch, StockUiQuestLog.ListRowWidth, StockUiQuestLog.ListRowHeight);

        private StockUiOpenMenu Show(StockUiOpenMenu menu, IReadOnlyList<string> texts, int selected = 0)
        {
            var rows = new List<StockUiListRow>(texts.Count);
            foreach (string text in texts) rows.Add(new StockUiListRow(rows.Count + 1, text, false));
            lock (_sync)
            {
                menu.Rows = rows;
                ScrollTo(menu, selected);
                PushOpen(menu);
            }
            Changed?.Invoke();
            return menu;
        }

        /// <summary>Puts entry <paramref name="index"/> under the cursor, scrolling it into view.</summary>
        private static void ScrollTo(StockUiOpenMenu menu, int index)
        {
            int count = menu.Rows.Count;
            if (count == 0)
            {
                menu.SelectedButtonId = 0;
                return;
            }
            index = Math.Clamp(index, 0, count - 1);
            int maxFirst = Math.Max(0, count - menu.VisibleRows);
            if (index < menu.FirstRow || index >= menu.FirstRow + menu.VisibleRows) menu.FirstRow = Math.Clamp(index - menu.VisibleRows + 1, 0, maxFirst);
            menu.FirstRow = Math.Min(menu.FirstRow, maxFirst);
            menu.ScrollFrom = menu.FirstRow;
            menu.SelectedButtonId = index - menu.FirstRow + 1;
        }

        /// <summary>A Missions group (<c>missionm</c> button <paramref name="group"/>): its one storyline, or a picker of them.</summary>
        public StockUiOpenMenu? OpenMissionGroup(int group, StockUiOpenMenu? parent)
        {
            if (!StockUiQuestLog.Groups.TryGetValue(group, out var books) || !TryGetQuestLog(out var catalog, out var state)) return null;
            if (books.Count == 1) return OpenQuestLogList(books[0], completed: false, parent, catalog, state);
            var definition = PickerDefinition(StockUiQuestLog.StorylineMenu, books.Count);
            if (definition == null) return null;
            var names = new List<string>(books.Count);
            foreach (var book in books) names.Add(StockUiQuestLog.NameOf(book));
            var picker = new StockUiOpenMenu(definition, parent, Array.Empty<string>(), null, definition.Buttons.Count)
            {
                QuestLogView = StockUiQuestLogView.Storylines,
                QuestLogBooks = books,
                ItemOwner = this,
            };
            // The home nation's row first under the cursor (PROVISIONAL).
            int selected = books[0] == StockUiQuestLogBook.MissionsSandoria ? Math.Clamp(state.Nation, 0, 2) : 0;
            return Show(picker, names, selected);
        }

        /// <summary>Opens the Quests window (#488): the areas.</summary>
        public StockUiOpenMenu? OpenQuestAreas(StockUiOpenMenu? parent)
        {
            if (!TryGetQuestLog(out _, out _)) return null;
            var areas = StockUiQuestLog.Areas;
            var definition = PickerDefinition(StockUiQuestLog.AreaMenu, areas.Count);
            if (definition == null) return null;
            var names = new List<string>(areas.Count);
            foreach (var area in areas) names.Add(StockUiQuestLog.NameOf(area));
            var picker = new StockUiOpenMenu(definition, parent, Array.Empty<string>(), null, definition.Buttons.Count)
            {
                QuestLogView = StockUiQuestLogView.Areas,
                IsQuestsWindow = true,
                QuestLogBooks = areas,
                ItemOwner = this,
            };
            return Show(picker, names);
        }

        private StockUiOpenMenu? OpenQuestKind(StockUiOpenMenu areas, StockUiQuestLogBook area)
        {
            var definition = QuestLogDefinition(StockUiQuestLog.QuestKindMenu, 2, StockUiQuestLog.KindRowX, StockUiQuestLog.KindRowY,
                StockUiQuestLog.KindRowPitch, StockUiQuestLog.PickerRowWidth, StockUiQuestLog.PickerRowHeight);
            if (definition == null) return null;
            var kind = new StockUiOpenMenu(definition, areas, Array.Empty<string>(), null, 2)
            {
                QuestLogView = StockUiQuestLogView.QuestKind,
                IsQuestsWindow = true,
                QuestLogBook = area,
                QuestLogHeading = StockUiQuestLog.NameOf(area),
                ItemOwner = this,
            };
            return Show(kind, new[] { StockUiQuestLog.QuestKindLabel(area, false), StockUiQuestLog.QuestKindLabel(area, true) });
        }

        private StockUiOpenMenu? OpenQuestLogList(StockUiQuestLogBook book, bool completed, StockUiOpenMenu? parent, StockUiQuestLogCatalog catalog, StockUiQuestLogState state)
        {
            bool quests = StockUiQuestLog.IsQuestArea(book);
            var rows = quests ? StockUiQuestLog.QuestRows(catalog, state, book, completed) : StockUiQuestLog.MissionRows(catalog, state, book);
            var definition = ListDefinition(quests ? StockUiQuestLog.QuestListMenu : StockUiQuestLog.MissionListMenu, Math.Max(1, rows.Count));
            if (definition == null) return null;
            var list = new StockUiOpenMenu(definition, parent, Array.Empty<string>(), null, definition.Buttons.Count)
            {
                QuestLogView = StockUiQuestLogView.List,
                IsQuestsWindow = quests,
                QuestLogBook = book,
                QuestLogCompleted = completed,
                QuestLogHeading = quests ? StockUiQuestLog.QuestListTitle(book, completed) : StockUiQuestLog.NameOf(book),
                ItemOwner = this,
            };
            lock (_sync)
            {
                FillQuestLogRows(list, rows, state);
                // A mission list opens on the mission in progress, else its last row.
                int current = rows.FindLastIndex(r => r.Status == StockUiQuestLogStatus.Current);
                ScrollTo(list, quests ? 0 : current >= 0 ? current : rows.Count - 1);
                PushOpen(list);
            }
            Changed?.Invoke();
            return list;
        }

        private void FillQuestLogRows(StockUiOpenMenu list, IReadOnlyList<StockUiQuestLogRow> rows, StockUiQuestLogState state)
        {
            var listRows = new List<StockUiListRow>(rows.Count);
            // The mission in progress is marked; a quest list's rows are all alike.
            foreach (var row in rows) listRows.Add(new StockUiListRow(listRows.Count + 1, row.Entry.Name, !list.IsQuestsWindow && row.Status == StockUiQuestLogStatus.Current));
            list.QuestLogRows = rows;
            list.Rows = listRows;
            if (rows.Count > 0) list.EmptyText = null;
            else if (list.IsQuestsWindow)
            {
                list.EmptyText = list.QuestLogCompleted
                    ? MenuText(StockUiQuestLog.NoCompletedQuestsText, StockUiQuestLog.NoCompletedQuestsFallback)
                    : MenuText(StockUiQuestLog.NoCurrentQuestsText, StockUiQuestLog.NoCurrentQuestsFallback);
            }
            else if (list.QuestLogBook is { } book) list.EmptyText = StockUiQuestLog.EmptyMissionText(state, book);
        }

        private void OpenQuestLogDescription(StockUiOpenMenu list, StockUiQuestLogRow row)
        {
            var definition = QuestLogDefinition(list.Menu.Name, 0, 0, 0, 0, 0, 0);
            if (definition == null) return;
            var page = new StockUiOpenMenu(definition, list, Array.Empty<string>(), null)
            {
                QuestLogView = StockUiQuestLogView.Description,
                IsQuestsWindow = list.IsQuestsWindow,
                QuestLogBook = list.QuestLogBook,
                QuestLogCompleted = list.QuestLogCompleted,
                QuestLogHeading = list.QuestLogHeading,
                QuestLogShown = row,
                ItemOwner = this,
            };
            lock (_sync) PushOpen(page);
            Changed?.Invoke();
        }

        /// <summary>Region Info's Conquest page: <c>conquer1</c> in a conquest region, <c>conq6sta</c> elsewhere (<see cref="StockUiConquestView"/>).</summary>
        public StockUiOpenMenu? OpenConquest(StockUiOpenMenu? parent)
        {
            var view = ConquestView?.Invoke();
            if (view == null)
            {
                NoticePosted?.Invoke("Region Info: no conquest information from the server yet.");
                return null;
            }
            var definition = QuestLogDefinition(view.InConquestRegion ? StockUiConquest.ControlWindow : StockUiConquest.BalanceWindow, 0, 0, 0, 0, 0, 0);
            if (definition == null) return null;
            var page = new StockUiOpenMenu(definition, parent, Array.Empty<string>(), null)
            {
                QuestLogView = StockUiQuestLogView.Conquest,
                ItemOwner = this,
            };
            lock (_sync)
            {
                page.ConquestView = view;
                PushOpen(page);
            }
            Changed?.Invoke();
            return page;
        }

        /// <summary>
        /// Confirm in a quest log window: a storyline opens its missions, an area its Current / Completed choice, a choice its
        /// quests, a quest or mission its description.
        /// </summary>
        private bool TryActivateQuestLog(StockUiOpenMenu top, UiMenuButton button)
        {
            if (!top.IsQuestLogWindow) return false;
            int index = top.EntryIndex(top.SelectedButtonId);
            switch (top.QuestLogView)
            {
                case StockUiQuestLogView.Storylines when index >= 0 && index < top.QuestLogBooks.Count:
                    if (TryGetQuestLog(out var catalog, out var state)) OpenQuestLogList(top.QuestLogBooks[index], false, top, catalog, state);
                    break;
                case StockUiQuestLogView.Areas when index >= 0 && index < top.QuestLogBooks.Count:
                    OpenQuestKind(top, top.QuestLogBooks[index]);
                    break;
                case StockUiQuestLogView.QuestKind when top.QuestLogBook is { } area:
                    if (TryGetQuestLog(out var questCatalog, out var questState)) OpenQuestLogList(area, button.ButtonId == 2, top, questCatalog, questState);
                    break;
                case StockUiQuestLogView.List when top.SelectedQuestLogRow is { } row:
                    OpenQuestLogDescription(top, row);
                    break;
            }
            return true;
        }

        /// <summary>S2C 0x056 changed the quest log: the open lists take fresh rows, keeping the cursor on its entry.</summary>
        public void RefreshQuestLog()
        {
            if (QuestLogCatalog?.Invoke() is not { } catalog || QuestLogState?.Invoke() is not { } state) return;
            bool changed = false;
            lock (_sync)
            {
                foreach (var menu in _open)
                {
                    if (menu.QuestLogView != StockUiQuestLogView.List || menu.QuestLogBook is not { } book) continue;
                    int? id = menu.SelectedQuestLogRow?.Entry.Id;
                    var rows = StockUiQuestLog.IsQuestArea(book) ? StockUiQuestLog.QuestRows(catalog, state, book, menu.QuestLogCompleted)
                        : StockUiQuestLog.MissionRows(catalog, state, book);
                    FillQuestLogRows(menu, rows, state);
                    int index = id is { } keep ? rows.FindIndex(r => r.Entry.Id == keep) : -1;
                    ScrollTo(menu, index >= 0 ? index : menu.EntryIndex(menu.SelectedButtonId));
                    changed = true;
                }
            }
            if (changed) Changed?.Invoke();
        }

        /// <summary>S2C 0x05E changed: an open Conquest page shows the new figures.</summary>
        public void RefreshConquest()
        {
            if (ConquestView?.Invoke() is not { } view) return;
            bool changed = false;
            lock (_sync)
            {
                foreach (var menu in _open)
                {
                    if (menu.QuestLogView != StockUiQuestLogView.Conquest) continue;
                    menu.ConquestView = view;
                    changed = true;
                }
            }
            if (changed) Changed?.Invoke();
        }
    }
}
