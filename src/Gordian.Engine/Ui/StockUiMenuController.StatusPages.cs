// src/Gordian.Engine/Ui/StockUiMenuController.StatusPages.cs
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Gordian.Core.Resources.Ui;

namespace Gordian.Engine.Ui
{
    public sealed partial class StockUiOpenMenu
    {
        /// <summary>
        /// The Status entry a window of its own belongs to (#487, <see cref="StockUiStatusPages"/>): Currencies, Currencies 2,
        /// Job Points and Alter Ego Points for their lists, Merit Points for <c>merit1</c> / <c>merit2</c>, Unity for
        /// <c>ut_menu</c>; None for other menus.
        /// </summary>
        public StockUiStatusPage StatusWindow { get; internal set; }

        /// <summary>A Status list's rows' help line ids (ROM/165/75; -1 for none), parallel to <see cref="Rows"/>.</summary>
        public IReadOnlyList<int> StatusRowHelp { get; internal set; } = Array.Empty<int>();

        /// <summary>The help line id of the row under a Status list's cursor; -1 for none.</summary>
        public int SelectedStatusRowHelp
        {
            get
            {
                int index = EntryIndex(SelectedButtonId);
                return index >= 0 && index < StatusRowHelp.Count ? StatusRowHelp[index] : -1;
            }
        }

        /// <summary>The Status list's row under the cursor (0-based), or -1.</summary>
        public int SelectedStatusRow
        {
            get
            {
                int index = EntryIndex(SelectedButtonId);
                return index >= 0 && index < Rows.Count ? index : -1;
            }
        }

        /// <summary>A Currencies, Job Points or Alter Ego Points list (<see cref="StockUiListKind.StatusValues"/>).</summary>
        public bool IsStatusValueList => ListKind == StockUiListKind.StatusValues;
    }

    public sealed partial class StockUiMenuController
    {
        /// <summary>What the Merit Points, Job Points, Alter Ego Points, Unity, Currencies and Play Time read; set by the session.</summary>
        public Func<StockUiStatusPoints?>? StatusPoints { get; set; }

        /// <summary>A menu short string by id (ROM/165/77): the currency names. Set by the HUD.</summary>
        public Func<int, string?>? MenuStringText { get; set; }

        /// <summary>Asks the server for the currencies (C2S 0x10F; with true C2S 0x115, Currencies 2); set by the session.</summary>
        public Action<bool>? CurrenciesRequested { get; set; }

        /// <summary>Asks the server for the job points (C2S 0x0C0); set by the session.</summary>
        public Action? JobPointsRequested { get; set; }

        /// <summary>Sends the merit mode (C2S 0x0BE kind 2: 0 experience points, 1 limit points); set by the session.</summary>
        public Func<byte, Task>? MeritModeChanged { get; set; }

        /// <summary>
        /// The Status entries with a window of their own, or none (#487, <see cref="StockUiStatusPages"/>). Returns the window
        /// opened, or null (Play Time writes to the log). Call under the lock.
        /// </summary>
        private StockUiOpenMenu? OpenStatusWindow(StockUiStatusPage page, StockUiOpenMenu from)
        {
            var points = StatusPoints?.Invoke();
            switch (page)
            {
                case StockUiStatusPage.PlayTime:
                    NoticePosted?.Invoke(StockUiStatusPages.PlayTimeText(points?.PlayTimeSeconds));
                    return null;
                case StockUiStatusPage.Currencies:
                case StockUiStatusPage.Currencies2:
                    bool second = page == StockUiStatusPage.Currencies2;
                    CurrenciesRequested?.Invoke(second);
                    return OpenStatusList(page, StockUiStatusPages.CurrencyWindowMenu, from, StatusListRows(page, points),
                        StockUiStatusPages.CurrencyHelpIds(second), 0);
                case StockUiStatusPage.JobPoints:
                    JobPointsRequested?.Invoke();
                    return OpenStatusList(page, StockUiStatusPages.JobPointMenu, from, StatusListRows(page, points), Array.Empty<int>(),
                        Math.Max(0, (points?.MainJob ?? 1) - 1));
                case StockUiStatusPage.AlterEgoPoints:
                    return OpenStatusList(page, StockUiStatusPages.AlterEgoMenu, from, StatusListRows(page, points), Array.Empty<int>(), 0);
                case StockUiStatusPage.MeritPoints:
                case StockUiStatusPage.Unity:
                    // Unity's points box shows the Unity Accolades of S2C 0x113.
                    if (page == StockUiStatusPage.Unity) CurrenciesRequested?.Invoke(false);
                    var menu = Push(page == StockUiStatusPage.Unity ? StockUiStatusPages.UnityMenu : StockUiStatusPages.MeritMenu, from,
                        Array.Empty<string>(), null, null);
                    if (menu != null) menu.StatusWindow = page;
                    else NoticePosted?.Invoke("Status: this page is not available in this session.");
                    return menu;
            }
            return null;
        }

        private IReadOnlyList<StockUiListRow> StatusListRows(StockUiStatusPage page, StockUiStatusPoints? points) => page switch
        {
            StockUiStatusPage.Currencies => StockUiStatusPages.CurrencyListRows(false, points, MenuStringText),
            StockUiStatusPage.Currencies2 => StockUiStatusPages.CurrencyListRows(true, points, MenuStringText),
            StockUiStatusPage.JobPoints => StockUiStatusPages.JobPointRows(points),
            StockUiStatusPage.AlterEgoPoints => StockUiStatusPages.AlterEgoRows(points),
            _ => Array.Empty<StockUiListRow>(),
        };

        /// <summary>
        /// A Status list in a DAT window: the window's frame and its row buttons (the rows numbered from 1; the Job Points
        /// window's page arrows, buttons 11 and 12, are left out), the last row linked to itself so Down there scrolls.
        /// The cursor starts on row <paramref name="cursor"/>. Call under the lock.
        /// </summary>
        private StockUiOpenMenu? OpenStatusList(StockUiStatusPage page, string menuName, StockUiOpenMenu from, IReadOnlyList<StockUiListRow> rows,
            IReadOnlyList<int> help, int cursor)
        {
            var library = _library;
            if (library == null || !library.TryGetMenu(menuName, out var window))
            {
                NoticePosted?.Invoke("Status: this page is not available in this session.");
                return null;
            }
            var rowButtons = new List<UiMenuButton>();
            for (int id = 1; window.FindButton(id) is { Width: > 100 } row; id++) rowButtons.Add(row);
            int visible = rowButtons.Count;
            if (visible == 0) return null;
            var buttons = new List<UiMenuButton>(visible);
            for (int i = 0; i < visible; i++)
            {
                var b = rowButtons[i];
                int id = i + 1;
                buttons.Add(new UiMenuButton
                {
                    X = (short)(b.X + StockUiStatusPages.ListRowInset), Y = b.Y, CursorOffsetX = b.CursorOffsetX, CursorOffsetY = b.CursorOffsetY,
                    Width = (short)(b.Width - StockUiStatusPages.ListRowInset), Height = b.Height,
                    SelectRectOffsetX = b.SelectRectOffsetX, SelectRectOffsetY = b.SelectRectOffsetY, ButtonId = b.ButtonId, Shapes = b.Shapes,
                    NavUp = (sbyte)(id == 1 ? 1 : id - 1),
                    NavDown = (sbyte)(id == visible ? id : id + 1),
                    NavLeft = (sbyte)id,
                    NavRight = (sbyte)id,
                });
            }
            var definition = new UiMenuDefinition
            {
                DatId = window.DatId, Category = window.Category, Name = window.Name, MenuType = window.MenuType,
                Frame = window.Frame, Buttons = buttons,
            };
            var menu = new StockUiOpenMenu(definition, from, Array.Empty<string>(), null, visible, StockUiListKind.StatusValues)
            {
                StatusWindow = page,
                StatusRowHelp = help,
                Rows = rows,
            };
            cursor = Math.Clamp(cursor, 0, Math.Max(0, rows.Count - 1));
            menu.FirstRow = Math.Clamp(cursor - visible + 1, 0, Math.Max(0, rows.Count - visible));
            menu.ScrollFrom = menu.FirstRow;
            menu.SelectedButtonId = rows.Count > 0 ? cursor - menu.FirstRow + 1 : 0;
            PushOpen(menu);
            return menu;
        }

        /// <summary>
        /// The currencies, job points, merits or alter ego points changed (S2C 0x113 / 0x118 / 0x063 / 0x08E): the open Status
        /// lists show the new values, the cursor kept.
        /// </summary>
        public void OnStatusPointsChanged()
        {
            bool changed = false;
            lock (_sync)
            {
                StockUiStatusPoints? points = null;
                foreach (var menu in _open)
                {
                    if (!menu.IsStatusValueList) continue;
                    points ??= StatusPoints?.Invoke();
                    menu.Rows = StatusListRows(menu.StatusWindow, points);
                    changed = true;
                }
            }
            if (changed) Changed?.Invoke();
        }

        /// <summary>Confirm on a Status list's row: the lists only show (spending job points and alter ego points is not done yet).</summary>
        private void ActivateStatusList(StockUiOpenMenu list)
        {
            switch (list.StatusWindow)
            {
                case StockUiStatusPage.JobPoints:
                    NoticePosted?.Invoke("Job Points: spending job points is not available yet.");
                    break;
                case StockUiStatusPage.AlterEgoPoints:
                    NoticePosted?.Invoke("Alter Ego Points: spending alter ego points is not available yet.");
                    break;
            }
        }

        /// <summary>
        /// Mode Switch's EXP or Limit Points (<c>merit2</c>): sends the mode (C2S 0x0BE kind 2) and closes the list. Limit
        /// Points needs level 75 and the Limit Breaker key item (S2C 0x063 type 0x02's flag); without them a notice says so.
        /// </summary>
        private void SwitchMeritMode(StockUiMenuEntry entry, StockUiOpenMenu from)
        {
            byte mode = (byte)entry.Argument;
            if (mode == 1 && StatusPoints?.Invoke() is { CanUseMeritMode: false })
            {
                NoticePosted?.Invoke("Limit Points: you cannot earn limit points yet.");
                return;
            }
            if (MeritModeChanged is not { } send)
            {
                NoticePosted?.Invoke($"{entry.Label} is not available in this session.");
                return;
            }
            _ = send(mode);
            lock (_sync)
            {
                if (_open.Length > 0 && ReferenceEquals(_open[^1], from)) _open = _open[..^1];
            }
            Changed?.Invoke();
        }
    }
}
