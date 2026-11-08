// src/Gordian.Engine/Ui/StockUiMenuController.QuestLog.cs
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Gordian.Core.Resources.Tables;
using Gordian.Core.Resources.Ui;
using Gordian.Engine.Input;

namespace Gordian.Engine.Ui
{
    /// <summary>Which step of the Missions, Quests or Region Info windows an open menu is (#488).</summary>
    public enum StockUiQuestLogView
    {
        None,

        /// <summary>The paging window (<c>miss00</c> / <c>quest01</c>): one storyline's or quest area's page.</summary>
        Pages,

        /// <summary>The Quests window's Current / Completed choice (<c>quest00</c>).</summary>
        QuestKind,

        /// <summary>A mission's orders or a quest's summary (<c>missiton</c> / <c>quset</c>).</summary>
        Description,

        /// <summary>Region Info's Conquest map (<see cref="StockUiConquestView"/>).</summary>
        Conquest,

        /// <summary>A Records of Eminence objective's detail page (<see cref="StockUiRoeDetail"/>, #493).</summary>
        RoeDetail,
        /// <summary>Region Info's Besieged, Campaign or Colonization map (<see cref="StockUiRegionInfoView"/>, #492).</summary>
        RegionInfo,
    }

    public sealed partial class StockUiOpenMenu
    {
        public StockUiQuestLogView QuestLogView { get; init; }

        public bool IsQuestLogWindow => QuestLogView != StockUiQuestLogView.None;

        /// <summary>Whether this belongs to the Quests window (else the Missions window).</summary>
        public bool IsQuestsWindow { get; init; }

        /// <summary>The paging window's pages: the storylines or the quest areas.</summary>
        public IReadOnlyList<StockUiQuestLogBook> QuestLogBooks { get; init; } = Array.Empty<StockUiQuestLogBook>();

        /// <summary>The index into <see cref="QuestLogBooks"/> of the page shown.</summary>
        public int QuestLogPage { get; internal set; }

        /// <summary>The book a page or a description is for.</summary>
        public StockUiQuestLogBook? QuestLogBook { get; internal set; }

        /// <summary>Whether the Quests window's pages (and their descriptions) show the completed quests.</summary>
        public bool QuestLogCompleted { get; init; }

        /// <summary>A page's lines, parallel to <see cref="Rows"/>.</summary>
        public IReadOnlyList<StockUiQuestLogLine> QuestLogLines { get; internal set; } = Array.Empty<StockUiQuestLogLine>();

        /// <summary>The quest or mission a description shows.</summary>
        public StockUiQuestLogRow? QuestLogShown { get; init; }

        /// <summary>The page's title between the arrows and the help bar's line ("Chains of Promathia", "San d'Oria: Current Quests").</summary>
        public string? QuestLogHeading { get; internal set; }

        /// <summary>The Conquest map's windows for the region under its cursor, rebuilt when the cursor or S2C 0x05E changes.</summary>
        public StockUiConquestView? ConquestView { get; internal set; }

        /// <summary>
        /// The conquest map's cursor (0-1 across and down the map image): the movement input moves it, the map scrolls when
        /// it nears the screen's edge (<see cref="KeepMapCursorInView"/>), and the region ball it comes to selects the
        /// region the windows and the zone labels show (round 3 of #488, the maintainer's movement video).
        /// </summary>
        public float MapCursorX { get; private set; } = 0.5f;

        /// <inheritdoc cref="MapCursorX"/>
        public float MapCursorY { get; private set; } = 0.5f;

        internal void SetMapCursor(float x, float y)
        {
            MapCursorX = Math.Clamp(x, 0, 1);
            MapCursorY = Math.Clamp(y, 0, 1);
        }

        /// <summary>
        /// Keeps the conquest map's cursor on screen, as the drawer sees it this frame: the map (<paramref name="mapWidth"/> x
        /// <paramref name="mapHeight"/> screen px) scrolls while the cursor is outside the inner part of the screen
        /// (<see cref="StockUiConquest.CursorLeft"/> ...), and once the map is at its edge the cursor stays
        /// <paramref name="margin"/> px inside the screen (<paramref name="top"/> px from its top: under the help bar).
        /// </summary>
        public void KeepMapCursorInView(float mapWidth, float mapHeight, float screenWidth, float screenHeight, float margin, float top)
        {
            if (mapWidth <= 0 || mapHeight <= 0 || screenWidth <= 0 || screenHeight <= 0) return;
            float Screen(float cursor, float center, float span, float screen) => screen * 0.5f + (cursor - center) * span;
            float sx = Screen(MapCursorX, MapCenterX, mapWidth, screenWidth), sy = Screen(MapCursorY, MapCenterY, mapHeight, screenHeight);
            float dx = sx < screenWidth * StockUiConquest.CursorLeft ? sx - screenWidth * StockUiConquest.CursorLeft
                : sx > screenWidth * StockUiConquest.CursorRight ? sx - screenWidth * StockUiConquest.CursorRight : 0;
            float dy = sy < screenHeight * StockUiConquest.CursorTop ? sy - screenHeight * StockUiConquest.CursorTop
                : sy > screenHeight * StockUiConquest.CursorBottom ? sy - screenHeight * StockUiConquest.CursorBottom : 0;
            if (dx != 0 || dy != 0)
            {
                PanMap(dx / mapWidth, dy / mapHeight);
                ClampMapCenter(screenWidth * 0.5f / mapWidth, screenHeight * 0.5f / mapHeight);
            }
            sx = Math.Clamp(Screen(MapCursorX, MapCenterX, mapWidth, screenWidth), margin, screenWidth - margin);
            sy = Math.Clamp(Screen(MapCursorY, MapCenterY, mapHeight, screenHeight), top, screenHeight - margin);
            SetMapCursor(MapCenterX + (sx - screenWidth * 0.5f) / mapWidth, MapCenterY + (sy - screenHeight * 0.5f) / mapHeight);
        }

        /// <summary>The region map a Region Info page draws behind it, panned like the Map window.</summary>
        public RegionMap? RegionMap { get; init; }

        /// <summary>The quest or mission under a page's cursor, if any.</summary>
        public StockUiQuestLogRow? SelectedQuestLogRow
        {
            get
            {
                int index = EntryIndex(SelectedButtonId);
                return QuestLogView == StockUiQuestLogView.Pages && index >= 0 && index < QuestLogLines.Count ? QuestLogLines[index].Row : null;
            }
        }

        /// <summary>Whether line <paramref name="index"/> of a page is a quest or mission the cursor can rest on.</summary>
        internal bool IsQuestLogEntry(int index) => index >= 0 && index < QuestLogLines.Count && QuestLogLines[index].IsEntry;

        /// <summary>The window title the help bar shows: "Missions", "Quests" or "Region Info".</summary>
        public string QuestLogTitle => QuestLogView == StockUiQuestLogView.Conquest ? StockUiConquest.Title
            : QuestLogView == StockUiQuestLogView.RoeDetail ? StockUiRecordsOfEminence.HelpTitle
            : QuestLogView == StockUiQuestLogView.RegionInfo ? RegionInfoView?.Title ?? StockUiConquest.Title
            : IsQuestsWindow ? StockUiQuestLog.QuestsTitle : StockUiQuestLog.MissionsTitle;
    }

    public sealed partial class StockUiMenuController
    {
        /// <summary>The quest and mission name tables; null without the game's DATs. Set by the HUD.</summary>
        public Func<StockUiQuestLogCatalog?>? QuestLogCatalog { get; set; }

        /// <summary>The quest log as S2C 0x056 left it (a fresh copy each call).</summary>
        public Func<StockUiQuestLogState?>? QuestLogState { get; set; }

        /// <summary>Region Info's Conquest windows for where the character is now (S2C 0x05E).</summary>
        public Func<StockUiConquestView?>? ConquestView { get; set; }

        /// <summary>
        /// The Conquest windows for a region the map's cursor is on (-1: between regions); null falls back to
        /// <see cref="ConquestView"/>.
        /// </summary>
        public Func<int, StockUiConquestView?>? ConquestRegionView { get; set; }

        /// <summary>Asks the server for the conquest figures (C2S 0x05A) as the Conquest map opens; its 0x05E refreshes the page.</summary>
        public Func<Task>? ConquestRequested { get; set; }

        /// <summary>The open map the movement input pans: the Map window or a Region Info map.</summary>
        public StockUiOpenMenu? PannedMap
        {
            get
            {
                var open = _open;
                for (int i = open.Length - 1; i >= 0; i--)
                {
                    if (open[i].IsMapWindow || open[i].RegionMap != null) return open[i];
                }
                return null;
            }
        }

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
        /// pitch n (w x h), the end rows linked to themselves (the page's own moves step past them), and the DAT's buttons
        /// <paramref name="keep"/> as authored (the paging window's arrows); no rows for a description or a Conquest window.
        /// </summary>
        private UiMenuDefinition? QuestLogDefinition(string name, int rows, int x, int y, int pitch, int width, int height, params int[] keep) =>
            QuestLogDefinition(name, rows, x, i => y + i * pitch, width, height, keep);

        /// <summary>A quest log window whose row i (from 0) stands at <paramref name="rowY"/>(i).</summary>
        private UiMenuDefinition? QuestLogDefinition(string name, int rows, int x, Func<int, int> rowY, int width, int height, int[] keep)
        {
            var library = _library;
            if (library == null || !library.TryGetMenu(name, out var window)) return null;
            var buttons = new List<UiMenuButton>(rows + keep.Length);
            for (int i = 1; i <= rows; i++)
            {
                buttons.Add(new UiMenuButton
                {
                    ButtonId = (short)i,
                    X = (short)x,
                    Y = (short)rowY(i - 1),
                    Width = (short)width,
                    Height = (short)height,
                    NavUp = (sbyte)(i == 1 ? 1 : i - 1),
                    NavDown = (sbyte)(i == rows ? i : i + 1),
                    NavLeft = (sbyte)i,
                    NavRight = (sbyte)i,
                });
            }
            foreach (int id in keep)
            {
                if (window.FindButton(id) is { } authored) buttons.Add(authored);
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

        /// <summary>Puts line <paramref name="index"/> under the cursor (none for -1), scrolling it into view with the heading above it.</summary>
        private static void ScrollTo(StockUiOpenMenu menu, int index)
        {
            int count = menu.Rows.Count, visible = menu.VisibleRows;
            int maxFirst = Math.Max(0, count - visible);
            if (index < 0 || count == 0)
            {
                menu.FirstRow = 0;
                menu.ScrollFrom = 0;
                menu.SelectedButtonId = 0;
                return;
            }
            index = Math.Clamp(index, 0, count - 1);
            int first = menu.FirstRow;
            if (index < first) first = index > 0 && !menu.IsQuestLogEntry(index - 1) ? index - 1 : index;
            else if (index >= first + visible) first = index - visible + 1;
            menu.FirstRow = Math.Clamp(first, 0, maxFirst);
            menu.ScrollFrom = menu.FirstRow;
            menu.SelectedButtonId = index - menu.FirstRow + 1;
        }

        /// <summary>A Missions group (<c>missionm</c> button <paramref name="group"/>): the paging window on its first storyline.</summary>
        public StockUiOpenMenu? OpenMissionGroup(int group, StockUiOpenMenu? parent)
        {
            if (!StockUiQuestLog.Groups.ContainsKey(group) || !TryGetQuestLog(out var catalog, out var state)) return null;
            var books = StockUiQuestLog.MissionPages;
            int page = Math.Max(0, IndexOf(books, StockUiQuestLog.FirstPageOf(group, state)));
            return OpenPages(StockUiQuestLog.StorylineMenu, books, page, quests: false, completed: false, parent, catalog, state);
        }

        /// <summary>Opens the Quests window (#488): Current, Completed, and Records of Eminence's Current and Objective List (#493).</summary>
        public StockUiOpenMenu? OpenQuests(StockUiOpenMenu? parent)
        {
            if (!TryGetQuestLog(out _, out _)) return null;
            var definition = QuestLogDefinition(StockUiQuestLog.QuestKindMenu, StockUiQuestLog.KindRowYs.Length, StockUiQuestLog.KindRowX,
                i => StockUiQuestLog.KindRowYs[i], StockUiQuestLog.PickerRowWidth, StockUiQuestLog.PickerRowHeight, Array.Empty<int>());
            if (definition == null) return null;
            var kind = new StockUiOpenMenu(definition, parent, Array.Empty<string>(), null, 4)
            {
                QuestLogView = StockUiQuestLogView.QuestKind,
                IsQuestsWindow = true,
                ItemOwner = this,
            };
            lock (_sync)
            {
                kind.Rows = QuestKindRows();
                kind.SelectedButtonId = 1;
                PushOpen(kind);
            }
            Changed?.Invoke();
            return kind;
        }

        private static int IndexOf(IReadOnlyList<StockUiQuestLogBook> books, StockUiQuestLogBook book)
        {
            for (int i = 0; i < books.Count; i++)
            {
                if (books[i] == book) return i;
            }
            return -1;
        }

        /// <summary>The paging window (<c>miss00</c> / <c>quest01</c>) on page <paramref name="page"/> of <paramref name="books"/>.</summary>
        private StockUiOpenMenu? OpenPages(string name, IReadOnlyList<StockUiQuestLogBook> books, int page, bool quests, bool completed, StockUiOpenMenu? parent,
            StockUiQuestLogCatalog catalog, StockUiQuestLogState state)
        {
            var definition = QuestLogDefinition(name, StockUiQuestLog.PickerRows, StockUiQuestLog.PickerRowX, StockUiQuestLog.PickerRowY,
                StockUiQuestLog.PickerRowPitch, StockUiQuestLog.PickerRowWidth, StockUiQuestLog.PickerRowHeight,
                StockUiQuestLog.PreviousPageButton, StockUiQuestLog.NextPageButton);
            if (definition == null) return null;
            var pages = new StockUiOpenMenu(definition, parent, Array.Empty<string>(), null, StockUiQuestLog.PickerRows)
            {
                QuestLogView = StockUiQuestLogView.Pages,
                IsQuestsWindow = quests,
                QuestLogBooks = books,
                QuestLogCompleted = completed,
                ItemOwner = this,
            };
            lock (_sync)
            {
                ShowPage(pages, page, catalog, state, keep: null);
                PushOpen(pages);
            }
            Changed?.Invoke();
            return pages;
        }

        /// <summary>
        /// Fills a paging window with page <paramref name="page"/> (wrapping): the cursor on the entry with id
        /// <paramref name="keep"/> if it is still there, else on the mission in progress, else on the first entry, else nowhere.
        /// Call under the lock.
        /// </summary>
        private void ShowPage(StockUiOpenMenu pages, int page, StockUiQuestLogCatalog catalog, StockUiQuestLogState state, int? keep)
        {
            var books = pages.QuestLogBooks;
            page = ((page % books.Count) + books.Count) % books.Count;
            var book = books[page];
            string emptyQuests = pages.QuestLogCompleted
                ? MenuText(StockUiQuestLog.NoCompletedQuestsText, StockUiQuestLog.NoCompletedQuestsFallback)
                : MenuText(StockUiQuestLog.NoCurrentQuestsText, StockUiQuestLog.NoCurrentQuestsFallback);
            var lines = StockUiQuestLog.PageLines(catalog, state, book, pages.QuestLogCompleted, emptyQuests);
            var rows = new List<StockUiListRow>(lines.Count);
            // The mission in progress is marked; a quest page's rows are all alike.
            foreach (var line in lines) rows.Add(new StockUiListRow(rows.Count + 1, line.Text, line.Row?.Status == StockUiQuestLogStatus.Current));
            pages.QuestLogPage = page;
            pages.QuestLogBook = book;
            pages.QuestLogHeading = StockUiQuestLog.PageTitle(book, pages.QuestLogCompleted);
            pages.QuestLogLines = lines;
            pages.Rows = rows;
            int index = keep is { } id ? lines.FindIndex(l => l.Row is { } r && r.Entry.Id == id && r.Status != StockUiQuestLogStatus.Heading) : -1;
            if (index < 0) index = lines.FindIndex(l => l.Row?.Status == StockUiQuestLogStatus.Current);
            if (index < 0) index = lines.FindIndex(l => l.IsEntry);
            pages.FirstRow = 0;
            ScrollTo(pages, index);
        }

        private void OpenQuestLogDescription(StockUiOpenMenu pages, StockUiQuestLogRow row)
        {
            var definition = QuestLogDefinition(pages.IsQuestsWindow ? StockUiQuestLog.QuestListMenu : StockUiQuestLog.MissionListMenu, 0, 0, 0, 0, 0, 0);
            if (definition == null) return;
            var page = new StockUiOpenMenu(definition, pages, Array.Empty<string>(), null)
            {
                QuestLogView = StockUiQuestLogView.Description,
                IsQuestsWindow = pages.IsQuestsWindow,
                QuestLogBook = pages.QuestLogBook,
                QuestLogCompleted = pages.QuestLogCompleted,
                QuestLogHeading = pages.QuestLogHeading,
                QuestLogShown = row,
                ItemOwner = this,
            };
            lock (_sync) PushOpen(page);
            Changed?.Invoke();
        }

        /// <summary>
        /// Up and down on a page step over its headings and notes to the next quest or mission (wrapping); left and right
        /// turn the page (wrapping). Call under the lock.
        /// </summary>
        private bool TryMoveQuestLog(InputAction direction)
        {
            if (Top is not { QuestLogView: StockUiQuestLogView.Pages } top) return false;
            if (direction is InputAction.MenuLeft or InputAction.MenuRight)
            {
                TurnQuestLogPage(top, direction == InputAction.MenuRight ? 1 : -1);
                return true;
            }
            if (direction is not (InputAction.MenuUp or InputAction.MenuDown)) return false;
            int count = top.QuestLogLines.Count, step = direction == InputAction.MenuDown ? 1 : -1;
            int from = top.EntryIndex(top.SelectedButtonId);
            if (!top.IsQuestLogEntry(from)) from = step > 0 ? -1 : count;
            for (int i = 1; i <= count; i++)
            {
                int index = ((from + step * i) % count + count) % count;
                if (!top.IsQuestLogEntry(index)) continue;
                if (index != from) ScrollTo(top, index);
                break;
            }
            return true;
        }

        /// <summary>A paging window's red arrow: the pointer turns the page with it (the cursor never rests on it).</summary>
        private static bool IsQuestLogArrow(StockUiOpenMenu menu, UiMenuButton button) =>
            menu.QuestLogView == StockUiQuestLogView.Pages && button.ButtonId is StockUiQuestLog.PreviousPageButton or StockUiQuestLog.NextPageButton;

        /// <summary>Shows the next or previous page of a paging window. Call under the lock.</summary>
        private void TurnQuestLogPage(StockUiOpenMenu pages, int step)
        {
            if (QuestLogCatalog?.Invoke() is not { } catalog || QuestLogState?.Invoke() is not { } state) return;
            ShowPage(pages, pages.QuestLogPage + step, catalog, state, keep: null);
        }

        /// <summary>
        /// Region Info's Conquest map: the conquest map behind (<see cref="RegionMap.Conquest"/>), <c>conquer1</c> in a
        /// conquest region or <c>conq6sta</c> elsewhere (<see cref="StockUiConquestView"/>).
        /// </summary>
        public StockUiOpenMenu? OpenConquest(StockUiOpenMenu? parent)
        {
            if (ConquestRequested is { } request) _ = request();
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
                RegionMap = Core.Resources.Tables.RegionMap.Conquest,
                ItemOwner = this,
            };
            lock (_sync)
            {
                page.ConquestView = view;
                page.MapZoom = StockUiConquest.MapZoom;
                // Retail opens with the cursor on the character's region or city (round 2 of #488, image-49), the map
                // centred on it inside its edges.
                if (StockUiConquest.CenterOf(view.Region) is { } center)
                {
                    page.SetMapCenter(center.U, center.V);
                    page.SetMapCursor(center.U, center.V);
                }
                SelectConquestRegion(page);
                PushOpen(page);
            }
            Changed?.Invoke();
            return page;
        }

        /// <summary>
        /// Confirm in a quest log window: Current or Completed opens the quest pages, a quest or mission its description.
        /// </summary>
        private bool TryActivateQuestLog(StockUiOpenMenu top, UiMenuButton button)
        {
            if (!top.IsQuestLogWindow) return false;
            switch (top.QuestLogView)
            {
                case StockUiQuestLogView.QuestKind when button.ButtonId is StockUiRecordsOfEminence.CurrentButton or StockUiRecordsOfEminence.ObjectiveListButton:
                    OpenRoeList(button.ButtonId, top);
                    break;
                case StockUiQuestLogView.QuestKind:
                    if (TryGetQuestLog(out var catalog, out var state))
                    {
                        OpenPages(StockUiQuestLog.AreaMenu, StockUiQuestLog.Areas, 0, quests: true, completed: button.ButtonId == 2, top, catalog, state);
                    }
                    break;
                case StockUiQuestLogView.Pages when top.SelectedQuestLogRow is { } row:
                    OpenQuestLogDescription(top, row);
                    break;
            }
            return true;
        }

        /// <summary>S2C 0x056 changed the quest log: the open pages take fresh lines, keeping the cursor on its entry.</summary>
        public void RefreshQuestLog()
        {
            if (QuestLogCatalog?.Invoke() is not { } catalog || QuestLogState?.Invoke() is not { } state) return;
            bool changed = false;
            lock (_sync)
            {
                foreach (var menu in _open)
                {
                    if (menu.QuestLogView != StockUiQuestLogView.Pages) continue;
                    ShowPage(menu, menu.QuestLogPage, catalog, state, menu.SelectedQuestLogRow?.Entry.Id);
                    changed = true;
                }
            }
            if (changed) Changed?.Invoke();
        }

        /// <summary>S2C 0x05E changed: an open Conquest map shows the new figures.</summary>
        public void RefreshConquest()
        {
            bool changed = false;
            lock (_sync)
            {
                foreach (var menu in _open)
                {
                    if (menu.QuestLogView != StockUiQuestLogView.Conquest) continue;
                    if (ConquestViewOf(menu.ConquestView?.Region ?? -1) is { } view) menu.ConquestView = view;
                    changed = true;
                }
            }
            if (changed) Changed?.Invoke();
        }

        /// <summary>Moves the conquest map's cursor by (<paramref name="dx"/>, <paramref name="dy"/>) map px.</summary>
        private void MoveConquestCursor(StockUiOpenMenu page, float dx, float dy)
        {
            page.SetMapCursor(page.MapCursorX + dx / StockUiConquest.MapSize, page.MapCursorY + dy / StockUiConquest.MapSize);
            SelectConquestRegion(page);
        }

        /// <summary>The region under the conquest map's cursor becomes the page's, its windows and zone labels rebuilt.</summary>
        private void SelectConquestRegion(StockUiOpenMenu page)
        {
            int region = StockUiConquest.RegionAt(page.MapCursorX * StockUiConquest.MapSize, page.MapCursorY * StockUiConquest.MapSize);
            if (page.ConquestView is { } shown && shown.Region == region) return;
            if (ConquestViewOf(region) is { } view) page.ConquestView = view;
        }

        private StockUiConquestView? ConquestViewOf(int region) => ConquestRegionView != null ? ConquestRegionView(region) : ConquestView?.Invoke();
    }
}
