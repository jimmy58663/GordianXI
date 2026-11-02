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

        /// <summary>The Alter Ego Points item list's category (<see cref="StockUiStatusPages.AlterEgoGroups"/>); -1 for the categories.</summary>
        public int AlterEgoGroup { get; internal set; } = -1;

        /// <summary>A Status list's rows' help line ids (ROM/165/75; -1 for none), parallel to <see cref="Rows"/>.</summary>
        public IReadOnlyList<int> StatusRowHelp { get; internal set; } = Array.Empty<int>();

        /// <summary>The help line id of the row under a Status list's cursor; -1 for none.</summary>
        public int SelectedStatusRowHelp
        {
            get
            {
                int index = EntryIndex(SelectedButtonId);
                if (index >= 0 && index < StatusRowHelp.Count) return StatusRowHelp[index];
                return StatusRowHelp.Count == 1 ? StatusRowHelp[0] : -1;
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

        /// <summary>Where a Status list's values end, from the window's left edge; null for the lists' default (the right edge).</summary>
        public float? StatusValueRight { get; internal set; }
    }

    public sealed partial class StockUiMenuController
    {
        /// <summary>What the Merit Points, Job Points, Alter Ego Points, Unity, Currencies and Play Time read; set by the session.</summary>
        public Func<StockUiStatusPoints?>? StatusPoints { get; set; }

        /// <summary>A menu short string by id (ROM/165/77): the currency names. Set by the HUD.</summary>
        public Func<int, string?>? MenuStringText { get; set; }

        /// <summary>The job points' strings by id (ROM/314/62): the jobs' names and lines. Set by the HUD.</summary>
        public Func<int, string?>? JobPointText { get; set; }

        /// <summary>The alter ego points' strings by id (ROM/384/121): the categories and items. Set by the HUD.</summary>
        public Func<int, string?>? AlterEgoText { get; set; }

        /// <summary>Asks the server for the currencies (C2S 0x10F; with true C2S 0x115, Currencies 2); set by the session.</summary>
        public Action<bool>? CurrenciesRequested { get; set; }

        /// <summary>Asks the server for the job points (C2S 0x0C0); set by the session.</summary>
        public Action? JobPointsRequested { get; set; }

        /// <summary>Sends the merit mode (C2S 0x0BE kind 2: 0 experience points, 1 limit points); set by the session.</summary>
        public Func<byte, Task>? MeritModeChanged { get; set; }

        /// <summary>A line the menus write to the log in the echo colour (Play Time, round 1 of #487).</summary>
        public event Action<string>? EchoPosted;

        /// <summary>Where Alter Ego Points' item values end ("0/60" under the header's middle, round 1 image-9).</summary>
        private const float AlterEgoValueRight = 252;

        /// <summary>
        /// The Status list (<c>statcom2</c>) as the character has it: without Job Points unless the job breaker key item is
        /// held (round 1: retail hides the entry; the entries under it move up a row, the frame a row shorter), with Master
        /// Levels greyed until a job is mastered and Alter Ego Points greyed without the cipher bracelet (their kind-4 labels).
        /// </summary>
        private UiMenuDefinition StatusListDefinition(UiMenuDefinition definition, out UiImage? frameImage, out HashSet<int>? greyed)
        {
            frameImage = null;
            var held = KeyItemHeld;
            greyed = new HashSet<int>();
            uint mastery = StatusInfo?.Invoke()?.JobMasteryFlags ?? 0;
            if (!StockUiStatusPages.MasterLevelsOpen(mastery, StatusPoints?.Invoke(), held?.Invoke(StockUiStatusPages.MasterBreakerKeyItem) == true))
                greyed.Add(StockUiStatus.MasterLevelsButton);
            if (held?.Invoke(StockUiStatusPages.CipherBraceletKeyItem) != true) greyed.Add(StockUiStatus.AlterEgoButton);
            if (greyed.Count == 0) greyed = null;
            if (held?.Invoke(StockUiStatusPages.JobBreakerKeyItem) == true || definition.FindButton(StockUiStatus.JobPointsButton) is not { } hidden)
                return definition;

            const int pitch = 16;
            var buttons = new List<UiMenuButton>(definition.Buttons.Count - 1);
            foreach (var b in definition.Buttons)
            {
                if (b.ButtonId == hidden.ButtonId) continue;
                bool below = b.Y > hidden.Y;
                sbyte up = b.NavUp == hidden.ButtonId ? hidden.NavUp : b.NavUp, down = b.NavDown == hidden.ButtonId ? hidden.NavDown : b.NavDown;
                buttons.Add(new UiMenuButton
                {
                    X = b.X, Y = (short)(below ? b.Y - pitch : b.Y), CursorOffsetX = b.CursorOffsetX, CursorOffsetY = b.CursorOffsetY,
                    Width = b.Width, Height = b.Height, SelectRectOffsetX = b.SelectRectOffsetX, SelectRectOffsetY = b.SelectRectOffsetY,
                    ButtonId = b.ButtonId, Shapes = b.Shapes, HelpTextId = b.HelpTextId, TitleTextId = b.TitleTextId,
                    NavUp = up, NavDown = down, NavLeft = b.NavLeft, NavRight = b.NavRight,
                });
            }
            var f = definition.Frame;
            short height = (short)(f.Height - pitch);
            foreach (var shape in f.Shapes)
            {
                if (shape.Kind == 0 && _library is { } library && library.TryGetImage(shape, out var image))
                {
                    frameImage = WithoutLastRow(image, f.Height, height, pitch);
                    break;
                }
            }
            return new UiMenuDefinition
            {
                DatId = definition.DatId, Category = definition.Category, Name = definition.Name, MenuType = definition.MenuType,
                Frame = new UiMenuFrame
                {
                    X = f.X, Y = f.Y, CursorOffsetX = f.CursorOffsetX, CursorOffsetY = f.CursorOffsetY, Width = f.Width, Height = height,
                    DrawOffsetX = f.DrawOffsetX, DrawOffsetY = f.DrawOffsetY, Anchor = f.Anchor, Shapes = f.Shapes,
                    HelpTextId = f.HelpTextId, TitleTextId = f.TitleTextId,
                },
                Buttons = buttons,
            };
        }

        /// <summary>
        /// <c>comwin</c> #12 (the Status list's frame: a background as tall as the frame and a capsule pair per row) a row
        /// shorter: the background cut to the new height and the lowest capsule pair left out.
        /// </summary>
        private static UiImage WithoutLastRow(UiImage image, int oldHeight, int newHeight, int pitch)
        {
            var parts = new List<UiSpritePart>(image.Parts.Count);
            foreach (var part in image.Parts)
            {
                int top = part.TopLeft.Y, bottom = part.BottomLeft.Y;
                if (top <= 0 && bottom >= oldHeight)
                {
                    parts.Add(new UiSpritePart
                    {
                        TopLeft = part.TopLeft, TopRight = part.TopRight,
                        BottomLeft = new UiPoint(part.BottomLeft.X, (short)newHeight), BottomRight = new UiPoint(part.BottomRight.X, (short)newHeight),
                        SourceX = part.SourceX, SourceY = part.SourceY, SourceWidth = part.SourceWidth,
                        SourceHeight = (ushort)Math.Max(1, part.SourceHeight * newHeight / Math.Max(1, bottom - top)),
                        Flags = part.Flags, TextureAttributes = part.TextureAttributes, TextureName = part.TextureName,
                        ColorTopLeft = part.ColorTopLeft, ColorTopRight = part.ColorTopRight,
                        ColorBottomLeft = part.ColorBottomLeft, ColorBottomRight = part.ColorBottomRight,
                    });
                }
                else if (bottom <= newHeight)
                {
                    parts.Add(part);
                }
            }
            return new UiImage { Parts = parts };
        }

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
                    EchoPosted?.Invoke(StockUiStatusPages.PlayTimeText(points?.PlayTimeSeconds));
                    return null;
                case StockUiStatusPage.Currencies:
                case StockUiStatusPage.Currencies2:
                    bool second = page == StockUiStatusPage.Currencies2;
                    CurrenciesRequested?.Invoke(second);
                    return OpenStatusList(page, StockUiStatusPages.CurrencyWindowMenu, from, StatusListRows(page, -1, points),
                        StockUiStatusPages.CurrencyHelpIds(second), 0);
                case StockUiStatusPage.JobPoints:
                    JobPointsRequested?.Invoke();
                    return OpenStatusList(page, StockUiStatusPages.JobPointMenu, from, StatusListRows(page, -1, points),
                        new[] { StockUiStatusPages.JobPointListHelp }, Math.Max(0, (points?.MainJob ?? 1) - 1));
                case StockUiStatusPage.AlterEgoPoints:
                    return OpenStatusList(page, StockUiStatusPages.AlterEgoMenu, from, StatusListRows(page, -1, points),
                        new[] { StockUiStatusPages.AlterEgoGroupHelp }, 0);
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

        private IReadOnlyList<StockUiListRow> StatusListRows(StockUiStatusPage page, int group, StockUiStatusPoints? points) => page switch
        {
            StockUiStatusPage.Currencies => StockUiStatusPages.CurrencyListRows(false, points, MenuStringText),
            StockUiStatusPage.Currencies2 => StockUiStatusPages.CurrencyListRows(true, points, MenuStringText),
            StockUiStatusPage.JobPoints => StockUiStatusPages.JobPointRows(JobPointText),
            StockUiStatusPage.AlterEgoPoints when group >= 0 => StockUiStatusPages.AlterEgoItemRows(group, points, AlterEgoText),
            StockUiStatusPage.AlterEgoPoints => StockUiStatusPages.AlterEgoGroupRows(AlterEgoText),
            _ => Array.Empty<StockUiListRow>(),
        };

        /// <summary>
        /// A Status list in a DAT window: the window's frame and its row buttons (the rows numbered from 1; the Job Points
        /// window's page arrows, buttons 11 and 12, are left out), the last row linked to itself so Down there scrolls.
        /// The cursor starts on row <paramref name="cursor"/>. A one-entry <paramref name="help"/> is every row's help line.
        /// Call under the lock.
        /// </summary>
        private StockUiOpenMenu? OpenStatusList(StockUiStatusPage page, string menuName, StockUiOpenMenu from, IReadOnlyList<StockUiListRow> rows,
            IReadOnlyList<int> help, int cursor, int group = -1)
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
                AlterEgoGroup = group,
                StatusRowHelp = help,
                Rows = rows,
                StatusValueRight = group >= 0 ? AlterEgoValueRight : null,
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
                    menu.Rows = StatusListRows(menu.StatusWindow, menu.AlterEgoGroup, points);
                    changed = true;
                }
            }
            if (changed) Changed?.Invoke();
        }

        /// <summary>
        /// Confirm on a Status list's row: an Alter Ego Points category lists its items (<c>fp_cat2</c>); spending job points and
        /// alter ego points is not done yet (#491).
        /// </summary>
        private void ActivateStatusList(StockUiOpenMenu list)
        {
            switch (list.StatusWindow)
            {
                case StockUiStatusPage.JobPoints:
                    NoticePosted?.Invoke("Job Points: spending job points is not available yet.");
                    break;
                case StockUiStatusPage.AlterEgoPoints when list.AlterEgoGroup < 0:
                    int group = list.SelectedStatusRow;
                    if (group < 0) return;
                    lock (_sync)
                    {
                        OpenStatusList(StockUiStatusPage.AlterEgoPoints, StockUiStatusPages.AlterEgoItemMenu, list,
                            StatusListRows(StockUiStatusPage.AlterEgoPoints, group, StatusPoints?.Invoke()),
                            new[] { StockUiStatusPages.AlterEgoItemHelp }, 0, group);
                    }
                    Changed?.Invoke();
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
