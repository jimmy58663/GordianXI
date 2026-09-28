// src/Gordian.Core/Ui/StockUiMenuController.cs
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Gordian.Core.Actions;
using Gordian.Core.Diagnostics;
using Gordian.Core.Input;
using Gordian.Core.Resources.Ui;
using Gordian.Core.World;

namespace Gordian.Core.Ui
{
    /// <summary>
    /// One client-drawn row of a list page (the Chat Filters list): its hit button, text and whether it is marked
    /// (a filter that is ON, drawn with the "ON" ball instead of "OFF").
    /// </summary>
    public readonly record struct StockUiListRow(int ButtonId, string Text, bool Marked);

    /// <summary>One option of an event query: its number in the message's choice list (1-based, hidden ones counted) and its text.</summary>
    public readonly record struct StockUiQueryOption(int Number, string Text);

    /// <summary>Client text drawn beside a button's label, starting at layout <paramref name="X"/> from the frame's left edge.</summary>
    public readonly record struct StockUiSideText(string Text, float X);

    /// <summary>
    /// Where the HUD drew an open menu on the last frame (its frame's top-left in screen pixels and the UI scale),
    /// for hit-testing the mouse against its buttons. Menus the HUD hides (covered by a later window) are not listed.
    /// </summary>
    public readonly record struct StockUiMenuPlacement(StockUiOpenMenu Menu, float X, float Y, float Scale);

    /// <summary>
    /// One stock menu window currently open: its DAT layout, the button under the cursor and its place in the stack.
    /// Instances are immutable to readers except the fields the controller updates (selection, markers, slider
    /// fills, list rows), which are replaced whole so the render thread can read them without a lock.
    /// </summary>
    public sealed class StockUiOpenMenu
    {
        private static readonly IReadOnlyDictionary<int, float> NoSliders = new Dictionary<int, float>();
        private HashSet<int>? _marked;

        private static readonly IReadOnlyDictionary<int, StockUiSideText> NoTexts = new Dictionary<int, StockUiSideText>();
        private HashSet<int>? _greyed;

        internal StockUiOpenMenu(UiMenuDefinition menu, StockUiOpenMenu? parent, IReadOnlyList<string> pageRing, string? message,
            int visibleRows = 0)
        {
            Menu = menu;
            Parent = parent;
            PageRing = pageRing;
            Message = message;
            StockUiConfigPages.TryGet(menu.Name, out var page);
            ConfigPage = page;
            IsChatFilterList = menu.Name.Equals(StockUiConfigPages.ChatFiltersPage, StringComparison.OrdinalIgnoreCase);
            VisibleRows = IsChatFilterList ? StockUiConfigPages.ChatFilterRowsPerPage : visibleRows;
        }

        public UiMenuDefinition Menu { get; }
        public string Name => Menu.Name;
        public StockUiOpenMenu? Parent { get; }

        /// <summary>
        /// A composed target command menu's rows (<see cref="StockUiCommandMenu"/>), indexed by ButtonId - 1; empty
        /// for DAT menus, whose entries come from <see cref="StockUiMenuEntries"/>.
        /// </summary>
        public IReadOnlyList<StockUiCommandRow> CommandRows { get; init; } = Array.Empty<StockUiCommandRow>();

        public bool IsCommandMenu => CommandRows.Count > 0;

        /// <summary>The target a command menu was opened on (null for other menus).</summary>
        public StockUiTargetContext? Target { get; init; }

        /// <summary>
        /// A frame image drawn in place of the frame's kind-0 shape references (a command menu's frame rebuilt for
        /// its row count); null for DAT windows.
        /// </summary>
        public UiImage? FrameImage { get; init; }

        /// <summary>
        /// Whether a button is drawn with its kind-4 alternate, the greyed look (Invite when you cannot invite; the
        /// chat modes without a linkshell or Unity). Confirm on it posts why instead of acting.
        /// </summary>
        public bool IsGreyed(int buttonId) => _greyed?.Contains(buttonId) == true;

        internal void SetGreyed(HashSet<int>? greyed) => _greyed = greyed;

        /// <summary>
        /// Client text drawn to the right of a button's label, keyed by ButtonId (the tell partner's name after the
        /// red arrow, "No Linkshell" / "No Unity" beside greyed chat modes).
        /// </summary>
        public IReadOnlyDictionary<int, StockUiSideText> SideTexts { get; internal set; } = NoTexts;

        /// <summary>The chat-mode list's tell candidates and which one the Tell row shows.</summary>
        public IReadOnlyList<string> TellCandidates { get; internal set; } = Array.Empty<string>();
        public int TellIndex { get; internal set; }

        /// <summary>ButtonId of the button under the cursor (0 when the menu has no selectable button).</summary>
        public int SelectedButtonId { get; internal set; }

        /// <summary>Menus this one flips between with left/right (the main menu's two pages); empty otherwise.</summary>
        public IReadOnlyList<string> PageRing { get; }

        /// <summary>Text the client draws inside the window (yes/no prompts); null for DAT-only windows.</summary>
        public string? Message { get; }

        /// <summary>The config page this menu draws, if it is one (<see cref="StockUiConfigPages"/>).</summary>
        public StockUiConfigPage? ConfigPage { get; }

        /// <summary>Whether this is the Chat Filters list (client-drawn rows over invisible hit buttons).</summary>
        public bool IsChatFilterList { get; }

        /// <summary>Whether a button carries the "value in effect" marker (retail's red bar).</summary>
        public bool IsMarked(int buttonId) => _marked?.Contains(buttonId) == true;

        internal void SetMarks(HashSet<int>? marked) => _marked = marked;

        /// <summary>Fill fraction (0-1) of each slider button on a config page, keyed by ButtonId.</summary>
        public IReadOnlyDictionary<int, float> SliderFractions { get; internal set; } = NoSliders;

        /// <summary>Every entry of a list page (client-drawn text), in list order; empty for other menus.</summary>
        public IReadOnlyList<StockUiListRow> Rows { get; internal set; } = Array.Empty<StockUiListRow>();

        /// <summary>How many rows a list page shows at once (its row buttons); 0 for other menus.</summary>
        public int VisibleRows { get; }

        /// <summary>Index into <see cref="Rows"/> of the entry on the first visible row.</summary>
        public int FirstRow { get; internal set; }

        /// <summary>The entry a row button (1-based) shows.</summary>
        public int EntryIndex(int buttonId) => FirstRow + buttonId - 1;

        public bool CanScroll => VisibleRows > 0 && Rows.Count > VisibleRows;

        /// <summary>
        /// Smooth scrolling (retail slides the rows rather than jumping): the first-row value the list is scrolling
        /// away from and when the scroll started (Stopwatch ticks); the drawer interpolates towards <see cref="FirstRow"/>.
        /// </summary>
        public float ScrollFrom { get; internal set; }
        public long ScrollStartedAt { get; internal set; }

        /// <summary>Completes a prompt when set: true on Yes, false on No or cancel.</summary>
        internal TaskCompletionSource<bool>? Prompt { get; init; }

        public bool IsPrompt => Prompt != null;

        /// <summary>An event query's comment lines (the question), drawn above its options.</summary>
        public IReadOnlyList<string> Comments { get; internal set; } = Array.Empty<string>();

        /// <summary>An event query's options in row order (their numbers are the choice numbers reported to the script).</summary>
        public IReadOnlyList<StockUiQueryOption> QueryOptions { get; internal set; } = Array.Empty<StockUiQueryOption>();

        /// <summary>Receives an event query's answer: an option number, or 255 when cancelled.</summary>
        internal Action<int>? QueryCompleted { get; init; }

        public bool IsQuery => QueryCompleted != null;

        /// <summary>The number of the option a row button shows, or 0 past the end.</summary>
        public int QueryOptionNumber(int buttonId)
        {
            int index = EntryIndex(buttonId);
            return index >= 0 && index < QueryOptions.Count ? QueryOptions[index].Number : 0;
        }

        /// <summary>Whether this window's authored rectangle overlaps another's (both in 512 x 448 layout space).</summary>
        public bool OverlapsAuthored(StockUiOpenMenu other)
        {
            var a = Menu.Frame;
            var b = other.Menu.Frame;
            return a.X < b.X + b.Width && b.X < a.X + a.Width && a.Y < b.Y + b.Height && b.Y < a.Y + a.Height;
        }

        /// <summary>The selected button, if any.</summary>
        public UiMenuButton? SelectedButton => Menu.FindButton(SelectedButtonId);
    }

    /// <summary>
    /// The stock menu system: a stack of open DAT menus (Section 0x30) driven by the logical input actions.
    /// <para>
    /// <see cref="InputAction.OpenMenu"/> opens the main "Commands" menu (and turns its page while it is open); the
    /// menu navigation actions move the cursor
    /// along each button's authored navigation links (with key repeat), left/right on a link that points back at the
    /// button flips the main menu's pages, <see cref="InputAction.Confirm"/> activates the selected entry
    /// (<see cref="StockUiMenuEntries"/>) and <see cref="InputAction.Cancel"/> closes the top window. Menus that
    /// need a decision call <see cref="PromptYesNoAsync"/>. The controller is UI-agnostic: the HUD draws the stack
    /// from <see cref="OpenMenus"/> and the locomotion controller feeds it input each tick.
    /// </para>
    /// <para>
    /// Config pages (<see cref="StockUiConfigPages"/>) read and write <see cref="Settings"/>: confirm on an option
    /// selects it (the value in effect is marked), left/right on a slider move it by its step, and the Chat Filters
    /// list toggles a filter per row across its pages. Window Type and Party Icon Display live on the layout and go
    /// through the delegates below.
    /// </para>
    /// The retail client remembers the cursor position of each menu; so does this controller (per menu name).
    /// </summary>
    public sealed class StockUiMenuController
    {
        /// <summary>Delay before a held direction starts repeating, then the interval between repeats.</summary>
        public static readonly TimeSpan RepeatDelay = TimeSpan.FromSeconds(0.4);
        public static readonly TimeSpan RepeatInterval = TimeSpan.FromSeconds(0.09);

        private readonly object _sync = new();
        private volatile StockUiOpenMenu[] _open = Array.Empty<StockUiOpenMenu>();
        private readonly Dictionary<string, int> _lastSelection = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> _lastPage = new(StringComparer.OrdinalIgnoreCase);
        private string _mainMenuPage = StockUiMenuEntries.MainMenu;
        private volatile UiResourceLibrary? _library;
        private StockUiSettings _settings = new();

        private readonly TimeSpan?[] _repeat = new TimeSpan?[4];

        public StockUiMenuController()
        {
            _settings.Synchronized += OnSettingsSynchronized;
        }

        /// <summary>The menu layouts; navigation needs it. Set by the HUD once the UI resources have loaded.</summary>
        public UiResourceLibrary? Library
        {
            get => _library;
            set => _library = value;
        }

        /// <summary>The config-menu settings the pages show and edit (the character's, once the session knows it).</summary>
        public StockUiSettings Settings
        {
            get => _settings;
            set
            {
                ArgumentNullException.ThrowIfNull(value);
                if (ReferenceEquals(_settings, value)) return;
                _settings.Synchronized -= OnSettingsSynchronized;
                _settings = value;
                _settings.Synchronized += OnSettingsSynchronized;
                RefreshAll();
            }
        }

        /// <summary>The open menus from the root (the first opened) to the top (the one taking input).</summary>
        public IReadOnlyList<StockUiOpenMenu> OpenMenus => _open;

        public bool IsOpen => _open.Length > 0;

        public StockUiOpenMenu? Top
        {
            get
            {
                var open = _open;
                return open.Length > 0 ? open[^1] : null;
            }
        }

        /// <summary>Raised after the stack or a selection changes (on the input thread).</summary>
        public event Action? Changed;

        /// <summary>A client message for the log window (entries without a window, the current time).</summary>
        public event Action<string>? NoticePosted;

        /// <summary>Called after the player confirms "Log out?"; the argument is true for Shut Down.</summary>
        public Func<bool, Task>? LogoutRequested { get; set; }

        /// <summary>The window skin currently in effect (1-8), for the Windows config page's marker.</summary>
        public Func<int>? CurrentWindowSkin { get; set; }

        /// <summary>Applies a window skin (1-8) chosen on the Windows config page.</summary>
        public Action<int>? WindowSkinSelected { get; set; }

        /// <summary>Whether party status icons are shown, for the Misc. 3 page's marker.</summary>
        public Func<bool>? CurrentPartyIcons { get; set; }

        /// <summary>Applies the Misc. 3 page's "Party Icon Display" choice.</summary>
        public Action<bool>? PartyIconsSelected { get; set; }

        /// <summary>Sets the default chat mode picked in the chat-mode list (as <c>/chatmode</c> does).</summary>
        public Action<ChatInputMode>? ChatModeSelected { get; set; }

        /// <summary>The last tell partner's name (shown after Tell's red arrow; empty when there is none).</summary>
        public Func<string>? TellTarget { get; set; }

        /// <summary>
        /// The names the Tell row cycles through with left/right (retail offers the last tell partner and the
        /// players around you, yourself included); the one shown is taken on Confirm.
        /// </summary>
        public Func<IReadOnlyList<string>>? TellCandidates { get; set; }

        /// <summary>Makes a name the tell partner (Confirm on the Tell row).</summary>
        public Action<string>? TellTargetSelected { get; set; }

        /// <summary>Whether a linkshell (1 or 2) is equipped; without one its chat mode is greyed "No Linkshell".</summary>
        public Func<int, bool>? HasLinkshell { get; set; }

        /// <summary>Whether the character is in a Unity; without one its chat mode is greyed "No Unity".</summary>
        public Func<bool>? HasUnity { get; set; }

        /// <summary>
        /// Runs a command menu action on the target the menu was opened for (Attack, Disengage, Invite, Check); a
        /// warning or error result is posted to the log.
        /// </summary>
        public Func<StockUiMenuCommand, StockUiTargetContext, Task<PlayerActionResult>>? TargetCommand { get; set; }

        #region Opening and closing

        /// <summary>
        /// Opens the target command menu (<see cref="StockUiCommandMenu"/>) composed for a target, when no menu is
        /// open; false when the target's kind has no menu or the UI resources are not loaded.
        /// </summary>
        public bool OpenCommandMenu(in StockUiTargetContext context)
        {
            var rows = StockUiCommandMenu.Compose(context);
            if (rows.Count == 0) return false;
            var library = _library;
            if (library == null) return false;
            lock (_sync)
            {
                if (_open.Length > 0) return false;
                var definition = StockUiCommandMenu.Build(library, rows, out var frameImage);
                if (definition == null)
                {
                    GordianLog.Warning("UI", $"Stock menu '{StockUiCommandMenu.Template}' is not available.");
                    return false;
                }
                var menu = new StockUiOpenMenu(definition, null, Array.Empty<string>(), null)
                {
                    CommandRows = rows,
                    Target = context,
                    FrameImage = frameImage,
                };
                var greyed = new HashSet<int>();
                for (int i = 0; i < rows.Count; i++)
                {
                    if (rows[i].Greyed) greyed.Add(i + 1);
                }
                menu.SetGreyed(greyed.Count > 0 ? greyed : null);
                // The cursor is remembered by entry (the lists differ per target), as retail keeps it on the row.
                int selected = 0;
                for (int i = 0; i < rows.Count && selected == 0; i++)
                {
                    if (rows[i].Label.Text.Equals(_lastCommandLabel, StringComparison.Ordinal)) selected = i + 1;
                }
                menu.SelectedButtonId = selected != 0 ? selected : FirstSelectable(definition);
                _open = new[] { menu };
            }
            Changed?.Invoke();
            return true;
        }

        /// <summary>The target the open command menu is for, if the root menu is one.</summary>
        public StockUiTargetContext? CommandMenuTarget
        {
            get
            {
                var open = _open;
                return open.Length > 0 ? open[0].Target : null;
            }
        }

        /// <summary>Closes the command menu and everything opened from it (the target changed or went away).</summary>
        public void CloseCommandMenu()
        {
            if (CommandMenuTarget != null) CloseAll();
        }

        /// <summary>Opens the main menu on the page it was last on; false when the UI resources are not loaded.</summary>
        public bool OpenMainMenu()
        {
            lock (_sync)
            {
                if (_open.Length > 0) return true;
                return Push(_mainMenuPage, null, StockUiMenuEntries.MainMenuPages, null, null) != null;
            }
        }

        public void ToggleMainMenu()
        {
            if (IsOpen) CloseAll();
            else OpenMainMenu();
        }

        /// <summary>Opens a DAT menu by name on top of the stack; false when it does not exist.</summary>
        public bool Open(string menuName)
        {
            lock (_sync) return Push(menuName, Top, Array.Empty<string>(), null, null) != null;
        }

        /// <summary>Closes the top menu (a prompt closes as "No").</summary>
        public void CloseTop()
        {
            StockUiOpenMenu? closed;
            lock (_sync)
            {
                if (_open.Length == 0) return;
                closed = _open[^1];
                Remember(closed);
                _open = _open[..^1];
            }
            closed.Prompt?.TrySetResult(false);
            closed.QueryCompleted?.Invoke(255);
            Changed?.Invoke();
        }

        /// <summary>Closes every menu (prompts close as "No").</summary>
        public void CloseAll()
        {
            StockUiOpenMenu[] closed;
            lock (_sync)
            {
                if (_open.Length == 0) return;
                closed = _open;
                foreach (var menu in closed) Remember(menu);
                _open = Array.Empty<StockUiOpenMenu>();
            }
            foreach (var menu in closed)
            {
                menu.Prompt?.TrySetResult(false);
                menu.QueryCompleted?.Invoke(255);
            }
            Changed?.Invoke();
        }

        /// <summary>
        /// Asks a yes/no question with the stock prompt window ("comyn" with a message, the bare "yesno" list without
        /// one) and completes with the answer; cancelling answers No. Fails with false when the UI is not loaded.
        /// </summary>
        public Task<bool> PromptYesNoAsync(string? message = null, bool defaultYes = true)
        {
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            StockUiOpenMenu? prompt;
            lock (_sync)
            {
                string name = string.IsNullOrEmpty(message) ? StockUiMenuEntries.YesNoMenu : StockUiMenuEntries.MessageYesNoMenu;
                prompt = Push(name, Top, Array.Empty<string>(), message, tcs);
                if (prompt != null) prompt.SelectedButtonId = defaultYes ? 1 : 2;
            }
            if (prompt == null) tcs.TrySetResult(false);
            else Changed?.Invoke();
            return tcs.Task;
        }

        /// <summary>The DAT window an event query is built from (bottom-left, three invisible 20 px rows authored).</summary>
        public const string QueryMenu = "query";

        /// <summary>
        /// Rows an event query shows at once before it scrolls: the DAT's three. A retail recording (2026-09-28) shows the
        /// home point menu's four options in the authored 366 x 88 window, three at a time with a scrollbar.
        /// </summary>
        public const int QueryMaxRows = 3;

        /// <summary>The first comment line sits at y 8 and the rows follow from y 28 at the DAT's 20 px pitch (recording 2026-09-28).</summary>
        private const int QueryRowPitch = 20, QueryFirstRowY = 28, QueryCommentPitch = 16, QueryRowX = 28, QueryRowRightPad = 16;

        /// <summary>
        /// Opens an event query (opcode 0x24): a window built from the "query" DAT frame with one row per shown
        /// option (up to <see cref="QueryMaxRows"/>, then scrolling) under the comment lines; with one comment and
        /// three rows it is the authored 366 x 88 window. Confirm answers with
        /// the option's number, Cancel with 255, through <paramref name="completed"/>. Returns the open menu, or null
        /// when the UI is not loaded (the caller then treats the query as cancelled).
        /// </summary>
        public StockUiOpenMenu? OpenQuery(IReadOnlyList<string> comments, IReadOnlyList<StockUiQueryOption> options, int cursorIndex, Action<int> completed)
        {
            var library = _library;
            if (library == null || options.Count == 0 || !library.TryGetMenu(QueryMenu, out var template)) return null;
            int visible = Math.Min(options.Count, QueryMaxRows);
            var rowTemplate = template.FindButton(1) ?? new UiMenuButton { X = QueryRowX, Y = 28, Width = 132, Height = 16 };
            int top = QueryFirstRowY + Math.Max(0, comments.Count - 1) * QueryCommentPitch;
            var buttons = new List<UiMenuButton>(visible);
            for (int i = 0; i < visible; i++)
            {
                buttons.Add(new UiMenuButton
                {
                    ButtonId = (short)(i + 1),
                    X = rowTemplate.X,
                    Y = (short)(top + i * QueryRowPitch),
                    Width = (short)(template.Frame.Width - rowTemplate.X - QueryRowRightPad),
                    Height = rowTemplate.Height,
                    NavUp = (sbyte)(i == 0 ? visible + 1 : i),
                    NavDown = (sbyte)(i + 2),
                    NavLeft = (sbyte)(i + 1),
                    NavRight = (sbyte)(i + 1),
                    Shapes = rowTemplate.Shapes,
                });
            }
            var frame = new UiMenuFrame
            {
                X = template.Frame.X,
                Y = template.Frame.Y,
                Width = template.Frame.Width,
                Height = (short)(top + visible * QueryRowPitch),
                Anchor = template.Frame.Anchor,
                Shapes = template.Frame.Shapes,
                CursorOffsetX = template.Frame.CursorOffsetX,
                CursorOffsetY = template.Frame.CursorOffsetY,
            };
            var definition = new UiMenuDefinition
            {
                DatId = template.DatId,
                Category = template.Category,
                Name = template.Name,
                MenuType = template.MenuType,
                Frame = frame,
                Buttons = buttons,
            };
            var rows = new List<StockUiListRow>(options.Count);
            foreach (var option in options) rows.Add(new StockUiListRow(rows.Count + 1, option.Text, false));

            var menu = new StockUiOpenMenu(definition, Top, Array.Empty<string>(), null, visible) { QueryCompleted = completed };
            menu.Comments = comments;
            menu.QueryOptions = options;
            menu.Rows = rows;
            int cursor = Math.Clamp(cursorIndex, 0, options.Count - 1);
            menu.FirstRow = Math.Clamp(cursor - visible + 1, 0, Math.Max(0, options.Count - visible));
            menu.ScrollFrom = menu.FirstRow;
            menu.SelectedButtonId = cursor - menu.FirstRow + 1;
            lock (_sync)
            {
                var open = new StockUiOpenMenu[_open.Length + 1];
                Array.Copy(_open, open, _open.Length);
                open[^1] = menu;
                _open = open;
            }
            Changed?.Invoke();
            return menu;
        }

        /// <summary>Closes an event query the script no longer waits on (the event ended); it answers nothing.</summary>
        public void CloseQuery(StockUiOpenMenu query)
        {
            bool removed = false;
            lock (_sync)
            {
                int index = Array.IndexOf(_open, query);
                if (index >= 0)
                {
                    var open = new StockUiOpenMenu[_open.Length - 1];
                    Array.Copy(_open, 0, open, 0, index);
                    Array.Copy(_open, index + 1, open, index, _open.Length - index - 1);
                    _open = open;
                    removed = true;
                }
            }
            if (removed) Changed?.Invoke();
        }

        private StockUiOpenMenu? Push(string menuName, StockUiOpenMenu? parent, IReadOnlyList<string> pageRing, string? message,
            TaskCompletionSource<bool>? prompt)
        {
            var library = _library;
            if (library == null || !library.TryGetMenu(menuName, out var definition))
            {
                GordianLog.Warning("UI", $"Stock menu '{menuName}' is not available.");
                return null;
            }

            var menu = new StockUiOpenMenu(definition, parent, pageRing, message) { Prompt = prompt };
            menu.SelectedButtonId = _lastSelection.TryGetValue(menuName, out int last) && definition.FindButton(last) != null
                ? last
                : FirstSelectable(definition);
            Refresh(menu);
            if (menu.VisibleRows > 0 && _lastPage.TryGetValue(menuName, out int first))
            {
                menu.FirstRow = Math.Clamp(first, 0, Math.Max(0, menu.Rows.Count - menu.VisibleRows));
                menu.ScrollFrom = menu.FirstRow;
            }

            var open = new StockUiOpenMenu[_open.Length + 1];
            Array.Copy(_open, open, _open.Length);
            open[^1] = menu;
            _open = open;
            if (prompt == null) Changed?.Invoke();
            return menu;
        }

        private string? _lastCommandLabel;

        private void Remember(StockUiOpenMenu menu)
        {
            if (menu.IsPrompt || menu.IsQuery) return;
            if (menu.IsCommandMenu)
            {
                int index = menu.SelectedButtonId - 1;
                if (index >= 0 && index < menu.CommandRows.Count) _lastCommandLabel = menu.CommandRows[index].Label.Text;
                return;
            }
            _lastSelection[menu.Name] = menu.SelectedButtonId;
            if (menu.VisibleRows > 0) _lastPage[menu.Name] = menu.FirstRow;
            if (menu.PageRing.Count > 0) _mainMenuPage = menu.Name;
        }

        /// <summary>
        /// The first button that can take the cursor: the topmost (then leftmost) one with a navigation link.
        /// Buttons without links are decorations (title bands).
        /// </summary>
        public static int FirstSelectable(UiMenuDefinition menu)
        {
            UiMenuButton? best = null;
            foreach (var button in menu.Buttons)
            {
                if (!IsSelectable(button)) continue;
                if (best == null || button.Y < best.Y || (button.Y == best.Y && button.X < best.X)) best = button;
            }
            return best?.ButtonId ?? 0;
        }

        public static bool IsSelectable(UiMenuButton button) =>
            button.NavUp != -1 || button.NavDown != -1 || button.NavLeft != -1 || button.NavRight != -1;

        #endregion

        #region Settings

        /// <summary>The value a config page shows for a setting (layout-backed keys come from the delegates).</summary>
        public int GetSetting(StockUiSettingKey key) => key switch
        {
            StockUiSettingKey.WindowType => Math.Clamp(CurrentWindowSkin?.Invoke() ?? 1, 1, UiResourceLibrary.WindowSkinCount),
            StockUiSettingKey.PartyIconDisplay => CurrentPartyIcons?.Invoke() == true ? 1 : 0,
            _ => _settings.GetValue(key),
        };

        /// <summary>Applies a config page's choice: the layout delegates for layout-backed keys, <see cref="Settings"/> otherwise.</summary>
        public void SetSetting(StockUiSettingKey key, int value)
        {
            switch (key)
            {
                case StockUiSettingKey.WindowType:
                    WindowSkinSelected?.Invoke(value);
                    break;
                case StockUiSettingKey.PartyIconDisplay:
                    PartyIconsSelected?.Invoke(value != 0);
                    break;
                default:
                    _settings.SetValue(key, value);
                    break;
            }
        }

        /// <summary>Rebuilds a menu's markers, slider fills and list rows from the current settings.</summary>
        private void Refresh(StockUiOpenMenu menu)
        {
            if (menu.ConfigPage is { } page)
            {
                var marked = new HashSet<int>();
                var sliders = new Dictionary<int, float>();
                foreach (var row in page.Rows)
                {
                    int value = GetSetting(row.Key);
                    switch (row)
                    {
                        case StockUiOptionRow option:
                            int button = option.ButtonFor(value);
                            if (button != 0) marked.Add(button);
                            break;
                        case StockUiSliderRow slider:
                            sliders[slider.ButtonId] = slider.Fraction(value);
                            break;
                    }
                }
                menu.SetMarks(marked);
                menu.SliderFractions = sliders;
            }
            else if (menu.IsChatFilterList)
            {
                var entries = StockUiConfigPages.ChatFilters;
                uint filter1 = _settings.MessageFilter1, filter2 = _settings.MessageFilter2;
                uint clientFilters = (uint)_settings.GetValue(StockUiSettingKey.ClientChatFilters);
                int systemLevel = _settings.GetValue(StockUiSettingKey.SystemMessageFilterLevel);
                var rows = new List<StockUiListRow>(entries.Count);
                for (int i = 0; i < entries.Count; i++)
                {
                    rows.Add(new StockUiListRow(i + 1, entries[i].Label, entries[i].IsFiltered(filter1, filter2, clientFilters, systemLevel)));
                }
                menu.Rows = rows;
                menu.SetMarks(null);
            }
            else if (menu.Name.Equals(StockUiMenuEntries.ChatModeMenu, StringComparison.OrdinalIgnoreCase))
            {
                // Chat modes: the tell partner's name after Tell's red arrow; Linkshell, Linkshell 2 and Unity greyed
                // (their kind-4 alternates) with "No Linkshell" / "No Unity" beside them when there is none.
                var greyed = new HashSet<int>();
                var texts = new Dictionary<int, StockUiSideText>();
                var arrow = menu.Menu.FindButton(StockUiMenuEntries.ChatModeArrowButton);
                float textX = arrow?.X ?? ChatModeSideTextX;
                float nameX = arrow != null ? arrow.X + arrow.Width + ChatModeNameGap : textX;
                string tell = TellTarget?.Invoke() ?? string.Empty;
                var candidates = TellCandidates?.Invoke() ?? Array.Empty<string>();
                if (candidates.Count == 0 && tell.Length > 0) candidates = new[] { tell };
                menu.TellCandidates = candidates;
                if (menu.TellIndex >= candidates.Count) menu.TellIndex = 0;
                if (candidates.Count > 0) texts[StockUiMenuEntries.ChatModeTellButton] = new StockUiSideText(candidates[menu.TellIndex], nameX);
                if (HasLinkshell?.Invoke(1) != true) { greyed.Add(StockUiMenuEntries.ChatModeLinkshellButton); texts[StockUiMenuEntries.ChatModeLinkshellButton] = new StockUiSideText(NoLinkshellText, textX); }
                if (HasLinkshell?.Invoke(2) != true) { greyed.Add(StockUiMenuEntries.ChatModeLinkshell2Button); texts[StockUiMenuEntries.ChatModeLinkshell2Button] = new StockUiSideText(NoLinkshellText, textX); }
                if (HasUnity?.Invoke() != true) { greyed.Add(StockUiMenuEntries.ChatModeUnityButton); texts[StockUiMenuEntries.ChatModeUnityButton] = new StockUiSideText(NoUnityText, textX); }
                menu.SetGreyed(greyed.Count > 0 ? greyed : null);
                menu.SideTexts = texts;
            }
        }

        /// <summary>The chat-mode list's client text beside a greyed mode (retail wording per the issue's DAT notes).</summary>
        public const string NoLinkshellText = "No Linkshell", NoUnityText = "No Unity";

        /// <summary>Where the chat-mode list's client text starts when the DAT lacks the arrow button: its authored x.</summary>
        private const float ChatModeSideTextX = 81;

        /// <summary>Gap between Tell's red arrow and the tell partner's name.</summary>
        private const float ChatModeNameGap = 2;

        private void RefreshAll()
        {
            lock (_sync)
            {
                foreach (var menu in _open) Refresh(menu);
            }
            if (IsOpen) Changed?.Invoke();
        }

        private void OnSettingsSynchronized() => RefreshAll();

        #endregion

        #region Input

        /// <summary>
        /// Handles this tick's menu input: opening/closing, cursor movement (with repeat), confirm and cancel.
        /// Returns true when a menu was open at the start of the tick, in which case the caller should leave the
        /// camera keys, targeting and gamepad stick movement alone.
        /// </summary>
        public bool ProcessInput(InputState input, TimeSpan elapsed)
        {
            bool wasOpen = IsOpen;
            if (input.WasActionTriggered(InputAction.OpenMenu))
            {
                // The menu button opens the main menu; pressed again it turns the page of a paged menu (retail
                // behaviour, confirmed in-game 2026-09-26). Only Cancel closes menus.
                if (!wasOpen) OpenMainMenu();
                else if (Top is { PageRing.Count: > 1 }) Move(InputAction.MenuRight);
                Array.Clear(_repeat);
                return wasOpen;
            }
            if (!wasOpen)
            {
                Array.Clear(_repeat);
                return false;
            }

            if (input.WasActionTriggered(InputAction.Cancel))
            {
                CloseTop();
                return true;
            }
            if (input.WasActionTriggered(InputAction.Confirm))
            {
                Activate();
                return true;
            }

            ReadOnlySpan<InputAction> directions = stackalloc InputAction[]
            {
                InputAction.MenuUp, InputAction.MenuDown, InputAction.MenuLeft, InputAction.MenuRight,
            };
            for (int i = 0; i < directions.Length; i++)
            {
                var action = directions[i];
                if (!input.IsActionHeld(action))
                {
                    _repeat[i] = null;
                    continue;
                }
                if (input.WasActionTriggered(action) || _repeat[i] == null)
                {
                    Move(action);
                    _repeat[i] = RepeatDelay;
                    continue;
                }
                var remaining = _repeat[i]!.Value - elapsed;
                if (remaining <= TimeSpan.Zero)
                {
                    Move(action);
                    remaining = RepeatInterval;
                }
                _repeat[i] = remaining;
            }
            return true;
        }

        /// <summary>
        /// Moves the cursor of the top menu along the selected button's navigation link. On a slider, left/right
        /// move the value instead; on a multi-page list, a link past the page turns it.
        /// </summary>
        public void Move(InputAction direction)
        {
            bool changed = false;
            (StockUiSettingKey Key, int Value)? edit = null;
            lock (_sync)
            {
                var top = Top;
                var button = top?.SelectedButton;
                if (top == null || button == null) return;

                bool horizontal = direction is InputAction.MenuLeft or InputAction.MenuRight;
                if (horizontal && top.ConfigPage is { } page && page.TryGetSlider(button.ButtonId, out var slider))
                {
                    var d = slider.Definition;
                    int value = d.Clamp(GetSetting(slider.Key) + (direction == InputAction.MenuRight ? d.Step : -d.Step));
                    if (value != GetSetting(slider.Key)) edit = (slider.Key, value);
                }
                else
                {
                    int link = direction switch
                    {
                        InputAction.MenuUp => button.NavUp,
                        InputAction.MenuDown => button.NavDown,
                        InputAction.MenuLeft => button.NavLeft,
                        InputAction.MenuRight => button.NavRight,
                        _ => -1,
                    };

                    if (link >= 0 && link != button.ButtonId && top.Menu.FindButton(link) != null && IsPopulated(top, link))
                    {
                        top.SelectedButtonId = link;
                        changed = true;
                    }
                    else if (horizontal && IsChatModeTell(top, button.ButtonId) && top.TellCandidates.Count > 1)
                    {
                        // The Tell row cycles through the tell candidates (retail: left/right on Tell).
                        int count = top.TellCandidates.Count;
                        top.TellIndex = (top.TellIndex + (direction == InputAction.MenuRight ? 1 : -1) + count) % count;
                        Refresh(top);
                        changed = true;
                    }
                    else if (direction == InputAction.MenuRight && TryGetEntry(top, button.ButtonId, out var arrowed) && arrowed.Opens != null
                        && !top.IsGreyed(button.ButtonId))
                    {
                        // Right on an arrowed entry (Chat ▶, Magic ▶) opens its list, as Confirm does (retail).
                        Push(arrowed.Opens, top, Array.Empty<string>(), null, null);
                    }
                    else if (horizontal && top.PageRing.Count > 1)
                    {
                        changed = FlipPage(top, direction == InputAction.MenuRight ? 1 : -1);
                    }
                    else if (link >= 0 && link != button.ButtonId && top.VisibleRows > 0)
                    {
                        // A link past the visible rows (the DAT links the last row down to 26 and the first up to
                        // 16 on the 14-row filter list) or to a row with no entry scrolls the list by one entry
                        // (forward from the bottom, back from the top), wrapping at the ends.
                        bool forward = direction is InputAction.MenuDown or InputAction.MenuRight;
                        changed = ScrollList(top, forward);
                    }
                }
            }
            if (edit is { } e)
            {
                SetSetting(e.Key, e.Value);
                lock (_sync)
                {
                    if (Top is { } top) Refresh(top);
                }
                changed = true;
            }
            if (changed) Changed?.Invoke();
        }

        private static bool IsChatModeTell(StockUiOpenMenu menu, int buttonId) =>
            buttonId == StockUiMenuEntries.ChatModeTellButton && menu.Name.Equals(StockUiMenuEntries.ChatModeMenu, StringComparison.OrdinalIgnoreCase);

        /// <summary>What a menu's button does: a composed command menu's row, else the entry table.</summary>
        private static bool TryGetEntry(StockUiOpenMenu menu, int buttonId, out StockUiMenuEntry entry)
        {
            if (menu.IsCommandMenu)
            {
                int index = buttonId - 1;
                if (index >= 0 && index < menu.CommandRows.Count)
                {
                    entry = menu.CommandRows[index].Entry;
                    return true;
                }
                entry = default;
                return false;
            }
            return StockUiMenuEntries.TryGet(menu.Name, buttonId, out entry);
        }

        /// <summary>On a list page, only rows showing an entry take the cursor (the rows past the end do not).</summary>
        private static bool IsPopulated(StockUiOpenMenu menu, int buttonId)
        {
            if (menu.VisibleRows == 0) return true;
            int index = menu.EntryIndex(buttonId);
            return index >= 0 && index < menu.Rows.Count;
        }

        /// <summary>How long a list takes to slide one entry (retail scrolls smoothly rather than jumping).</summary>
        public static readonly TimeSpan ScrollDuration = TimeSpan.FromSeconds(0.12);

        /// <summary>
        /// Replaces the top menu with the previous/next page of its ring, keeping the cursor on the same row.
        /// </summary>
        private bool FlipPage(StockUiOpenMenu top, int step)
        {
            int index = -1;
            for (int i = 0; i < top.PageRing.Count; i++)
            {
                if (top.PageRing[i].Equals(top.Name, StringComparison.OrdinalIgnoreCase)) index = i;
            }
            if (index < 0) return false;
            string next = top.PageRing[(index + step + top.PageRing.Count) % top.PageRing.Count];
            var library = _library;
            if (library == null || !library.TryGetMenu(next, out var definition)) return false;

            var page = new StockUiOpenMenu(definition, top.Parent, top.PageRing, null);
            var current = top.SelectedButton;
            int selected = 0;
            if (current != null)
            {
                foreach (var button in definition.Buttons)
                {
                    if (IsSelectable(button) && button.Y == current.Y && button.X == current.X) selected = button.ButtonId;
                }
            }
            page.SelectedButtonId = selected != 0 ? selected : FirstSelectable(definition);
            Remember(top);
            _mainMenuPage = next;

            var open = (StockUiOpenMenu[])_open.Clone();
            open[^1] = page;
            _open = open;
            return true;
        }

        /// <summary>
        /// Scrolls a list by one entry with the cursor staying on the edge row (the rows slide, as retail's do),
        /// or wraps to the other end when there is nothing further.
        /// </summary>
        private static bool ScrollList(StockUiOpenMenu top, bool forward)
        {
            int total = top.Rows.Count, visible = top.VisibleRows;
            if (visible <= 0 || total == 0) return false;
            int maxFirst = Math.Max(0, total - visible);
            int lastRow = Math.Min(visible, total);
            if (forward)
            {
                if (top.FirstRow < maxFirst) { BeginScroll(top, top.FirstRow + 1); top.SelectedButtonId = lastRow; }
                else { BeginScroll(top, 0); top.SelectedButtonId = 1; }
            }
            else
            {
                if (top.FirstRow > 0) { BeginScroll(top, top.FirstRow - 1); top.SelectedButtonId = 1; }
                else { BeginScroll(top, maxFirst); top.SelectedButtonId = lastRow; }
            }
            return true;
        }

        /// <summary>Moves the first row, animating a one-entry step (a wrap to the other end jumps).</summary>
        private static void BeginScroll(StockUiOpenMenu top, int first)
        {
            top.ScrollFrom = Math.Abs(first - top.FirstRow) == 1 ? top.FirstRow : first;
            top.ScrollStartedAt = Stopwatch.GetTimestamp();
            top.FirstRow = first;
        }

        /// <summary>Activates the top menu's selected button.</summary>
        public void Activate()
        {
            StockUiOpenMenu? top;
            UiMenuButton? button;
            lock (_sync)
            {
                top = Top;
                button = top?.SelectedButton;
            }
            if (top == null || button == null) return;

            if (top.IsPrompt)
            {
                bool yes = button.ButtonId == 1;
                lock (_sync)
                {
                    if (ReferenceEquals(Top, top)) _open = _open[..^1];
                }
                top.Prompt!.TrySetResult(yes);
                Changed?.Invoke();
                return;
            }

            if (top.IsQuery)
            {
                int number = top.QueryOptionNumber(button.ButtonId);
                if (number == 0) return;
                lock (_sync)
                {
                    if (ReferenceEquals(Top, top)) _open = _open[..^1];
                }
                top.QueryCompleted!.Invoke(number);
                Changed?.Invoke();
                return;
            }

            if (top.ConfigPage is { } page)
            {
                if (page.TryGetOption(button.ButtonId, out var row, out var choice))
                {
                    SetSetting(row.Key, choice.Value);
                    lock (_sync) Refresh(top);
                    Changed?.Invoke();
                }
                return; // sliders take left/right, not confirm
            }

            if (top.IsChatFilterList)
            {
                ToggleChatFilter(top, button.ButtonId);
                return;
            }

            if (!TryGetEntry(top, button.ButtonId, out var entry)) return;
            if (top.IsGreyed(button.ButtonId))
            {
                NoticePosted?.Invoke(DescribeGreyed(top, button.ButtonId, entry));
                return;
            }
            if (entry.Opens != null)
            {
                lock (_sync) Push(entry.Opens, top, Array.Empty<string>(), null, null);
                return;
            }
            Run(entry, top);
        }

        /// <summary>Why a greyed entry does nothing: the chat modes name what is missing, Invite that you do not lead.</summary>
        private static string DescribeGreyed(StockUiOpenMenu menu, int buttonId, StockUiMenuEntry entry)
        {
            if (menu.SideTexts.TryGetValue(buttonId, out var side) && !string.IsNullOrEmpty(side.Text)) return $"{entry.Label}: {side.Text.ToLowerInvariant()}.";
            return entry.Command == StockUiMenuCommand.Invite
                ? "Invite: only the party leader can invite."
                : $"{entry.Label} is not available.";
        }

        private void ToggleChatFilter(StockUiOpenMenu list, int rowButton)
        {
            int index = list.EntryIndex(rowButton);
            var entries = StockUiConfigPages.ChatFilters;
            if (index < 0 || index >= entries.Count) return;
            var entry = entries[index];
            if (entry.IsServerBit)
            {
                uint filter1 = _settings.MessageFilter1, filter2 = _settings.MessageFilter2;
                if (entry.Word == 1) filter1 ^= entry.Bit;
                else filter2 ^= entry.Bit;
                _settings.SetChatFilters(filter1, filter2);
            }
            else if (entry.IsSystemLevel)
            {
                // The levels stack: turning Lv. N on raises the level to N, turning it off drops it to N - 1.
                int level = _settings.GetValue(StockUiSettingKey.SystemMessageFilterLevel), n = (int)entry.Bit;
                _settings.SetValue(StockUiSettingKey.SystemMessageFilterLevel, level >= n ? n - 1 : n);
            }
            else
            {
                _settings.SetValue(StockUiSettingKey.ClientChatFilters, (int)((uint)_settings.GetValue(StockUiSettingKey.ClientChatFilters) ^ entry.Bit));
            }
            lock (_sync) Refresh(list);
            Changed?.Invoke();
        }

        private void Run(StockUiMenuEntry entry, StockUiOpenMenu from)
        {
            switch (entry.Command)
            {
                case StockUiMenuCommand.NotAvailable:
                    NoticePosted?.Invoke($"{entry.Label}: this window is not available yet.");
                    break;

                case StockUiMenuCommand.CurrentTime:
                    NoticePosted?.Invoke(DescribeCurrentTime(DateTime.UtcNow));
                    break;

                case StockUiMenuCommand.LogOut:
                case StockUiMenuCommand.ShutDown:
                    _ = ConfirmLogoutAsync(entry.Command == StockUiMenuCommand.ShutDown);
                    break;

                case StockUiMenuCommand.ChatMode:
                    var mode = (ChatInputMode)entry.Argument;
                    if (mode == ChatInputMode.Tell)
                    {
                        // The Tell row's shown candidate becomes the tell partner.
                        var candidates = from.TellCandidates;
                        if (candidates.Count == 0)
                        {
                            NoticePosted?.Invoke("Tell: no one to send tells to. Use /tell <name> <message> first.");
                            break;
                        }
                        TellTargetSelected?.Invoke(candidates[Math.Clamp(from.TellIndex, 0, candidates.Count - 1)]);
                    }
                    // The menus close first: the handler opens the input line, which takes the keyboard.
                    CloseAll();
                    ChatModeSelected?.Invoke(mode);
                    break;

                case StockUiMenuCommand.Attack:
                case StockUiMenuCommand.Disengage:
                case StockUiMenuCommand.Invite:
                case StockUiMenuCommand.Check:
                    var target = CommandMenuTarget;
                    CloseAll();
                    if (target is { } t) _ = RunTargetCommandAsync(entry, t);
                    break;
            }
        }

        private async Task RunTargetCommandAsync(StockUiMenuEntry entry, StockUiTargetContext target)
        {
            var run = TargetCommand;
            if (run == null)
            {
                NoticePosted?.Invoke($"{entry.Label} is not available in this session.");
                return;
            }
            try
            {
                var result = await run(entry.Command, target).ConfigureAwait(false);
                if (result.Kind is PlayerActionResultKind.Warning or PlayerActionResultKind.Error && !string.IsNullOrEmpty(result.Message))
                {
                    NoticePosted?.Invoke(result.Message);
                }
            }
            catch (Exception ex)
            {
                GordianLog.Warning("UI", $"{entry.Label} failed: {ex.Message}");
                NoticePosted?.Invoke($"{entry.Label} failed: {ex.Message}");
            }
        }

        private async Task ConfirmLogoutAsync(bool shutdown)
        {
            bool yes = await PromptYesNoAsync(shutdown ? "Shut down?" : "Log out?", defaultYes: false).ConfigureAwait(false);
            if (!yes) return;
            CloseAll();
            try
            {
                if (LogoutRequested != null) await LogoutRequested(shutdown).ConfigureAwait(false);
                else NoticePosted?.Invoke("Log out is not available in this session.");
            }
            catch (Exception ex)
            {
                GordianLog.Warning("UI", $"Logout request failed: {ex.Message}");
                NoticePosted?.Invoke($"Log out failed: {ex.Message}");
            }
        }

        /// <summary>The "Current Time" entry's text: Vana'diel date and time, then Earth time.</summary>
        public static string DescribeCurrentTime(DateTime utcNow)
        {
            long vanaSeconds = VanaTime.GetVanadielSeconds(utcNow);
            long day = vanaSeconds / VanaTime.SecondsPerVanadielDay;
            int hour = (int)(vanaSeconds % VanaTime.SecondsPerVanadielDay / 3600);
            int minute = (int)(vanaSeconds % 3600 / 60);
            long year = day / 360 + 886; // the Vana'diel epoch (2002-01-01 Earth) is C.E. 886
            int month = (int)(day % 360 / 30) + 1;
            int dayOfMonth = (int)(day % 30) + 1;
            string[] days = { "Firesday", "Earthsday", "Watersday", "Windsday", "Iceday", "Lightningday", "Lightsday", "Darksday" };
            string weekday = days[day % 8];
            return $"Vana'diel time: {weekday}, {year}/{month}/{dayOfMonth} {hour:00}:{minute:00}. Earth time: {utcNow.ToLocalTime():HH:mm:ss}.";
        }

        #endregion

        #region Mouse

        private volatile StockUiMenuPlacement[] _placements = Array.Empty<StockUiMenuPlacement>();
        private MouseButton _pressesTaken;
        private StockUiOpenMenu? _sliderMenu;
        private UiMenuButton? _sliderButton;
        private StockUiMenuPlacement _sliderPlacement;

        /// <summary>
        /// Publishes where the HUD drew the visible menus this frame (render thread), root first; the mouse handlers
        /// hit-test against the last published set.
        /// </summary>
        public void SetScreenPlacements(IReadOnlyList<StockUiMenuPlacement> placements)
        {
            var current = _placements;
            if (current.Length == placements.Count)
            {
                bool same = true;
                for (int i = 0; i < current.Length && same; i++) same = current[i] == placements[i];
                if (same) return;
            }
            var copy = new StockUiMenuPlacement[placements.Count];
            for (int i = 0; i < copy.Length; i++) copy[i] = placements[i];
            _placements = copy;
        }

        /// <summary>
        /// The pointer moved (screen pixels). Over a button of the menu taking input, the cursor follows the pointer
        /// (the hovered entry is the selected one); while a slider is held, the value follows it. Returns true when
        /// the pointer is over an open menu.
        /// </summary>
        public bool OnMouseMove(float x, float y)
        {
            (StockUiSettingKey Key, int Value)? edit = null;
            bool changed = false, over;
            lock (_sync)
            {
                if (_sliderMenu != null && _sliderButton != null && ReferenceEquals(Top, _sliderMenu))
                {
                    edit = SliderValueAt(_sliderMenu, _sliderPlacement, _sliderButton, x);
                }
                over = TryHit(x, y, out var menu, out var button, out _);
                if (_sliderMenu == null && over && button != null && ReferenceEquals(menu, Top) && !IsPageArrow(menu!, button)
                    && menu!.SelectedButtonId != button.ButtonId)
                {
                    menu.SelectedButtonId = button.ButtonId;
                    changed = true;
                }
            }
            if (edit is { } e) changed |= ApplyEdit(e.Key, e.Value);
            if (changed) Changed?.Invoke();
            return over;
        }

        /// <summary>
        /// A mouse button was pressed (screen pixels). Returns true when the press was the menus' (it landed on an
        /// open menu window), in which case it is not game input. Left on an entry selects and activates it (a
        /// parent menu still on screen first closes the windows opened from it; a slider takes the value under the
        /// pointer and follows it until release; a page arrow turns the page); right over a menu cancels, as the
        /// Cancel key does.
        /// </summary>
        public bool OnMouseDown(MouseButton mouseButton, float x, float y)
        {
            if (mouseButton is not (MouseButton.Left or MouseButton.Right)) return false;
            StockUiOpenMenu[] closed = Array.Empty<StockUiOpenMenu>();
            (StockUiSettingKey Key, int Value)? edit = null;
            bool activate = false, changed = false;
            lock (_sync)
            {
                if (!TryHit(x, y, out var menu, out var button, out var placement)) return false;
                _pressesTaken |= mouseButton;
                // A right press cancels (below, outside the lock); a left press on a window's body only stops there.
                if (mouseButton == MouseButton.Left && button != null && menu != null)
                {
                    var top = Top!;
                    // A prompt takes its own answer only; the windows behind it wait.
                    if (top.IsPrompt && !ReferenceEquals(top, menu)) return true;
                    if (!ReferenceEquals(top, menu))
                    {
                        // Clicking an entry of a parent window still on screen leaves the windows opened from it.
                        int index = Array.IndexOf(_open, menu);
                        closed = _open[(index + 1)..];
                        foreach (var m in closed) Remember(m);
                        _open = _open[..(index + 1)];
                        changed = true;
                    }

                    if (IsPageArrow(menu, button))
                    {
                        changed |= FlipPage(menu, button.X < 0 ? -1 : 1);
                    }
                    else
                    {
                        changed |= menu.SelectedButtonId != button.ButtonId;
                        menu.SelectedButtonId = button.ButtonId;
                        if (menu.ConfigPage is { } page && page.TryGetSlider(button.ButtonId, out _))
                        {
                            _sliderMenu = menu;
                            _sliderButton = button;
                            _sliderPlacement = placement;
                            edit = SliderValueAt(menu, placement, button, x);
                        }
                        else
                        {
                            activate = true;
                        }
                    }
                }
            }

            if (mouseButton == MouseButton.Right)
            {
                CloseTop();
                return true;
            }
            foreach (var m in closed) m.Prompt?.TrySetResult(false);
            if (edit is { } e) changed |= ApplyEdit(e.Key, e.Value);
            if (changed) Changed?.Invoke();
            if (activate) Activate();
            return true;
        }

        /// <summary>
        /// A mouse button was released. Ends a slider drag; returns true when the matching press was the menus'
        /// (so the release is not game input either).
        /// </summary>
        public bool OnMouseUp(MouseButton mouseButton, float x, float y)
        {
            lock (_sync)
            {
                if (mouseButton == MouseButton.Left)
                {
                    _sliderMenu = null;
                    _sliderButton = null;
                }
                bool taken = (_pressesTaken & mouseButton) != 0;
                _pressesTaken &= ~mouseButton;
                return taken;
            }
        }

        /// <summary>
        /// The mouse wheel turned over the screen (<paramref name="delta"/> in notches, positive away from the
        /// player). Over a scrolling list it scrolls by one entry a notch (no wrap); over any other menu it is only
        /// swallowed (not the camera's zoom). Returns true when the pointer is over an open menu.
        /// </summary>
        public bool OnMouseWheel(float x, float y, float delta)
        {
            bool changed = false;
            lock (_sync)
            {
                if (!TryHit(x, y, out var menu, out _, out _)) return false;
                if (menu is { CanScroll: true } && delta != 0)
                {
                    int steps = Math.Max(1, (int)Math.Round(Math.Abs(delta)));
                    int maxFirst = Math.Max(0, menu.Rows.Count - menu.VisibleRows);
                    int first = Math.Clamp(menu.FirstRow + (delta > 0 ? -steps : steps), 0, maxFirst);
                    if (first != menu.FirstRow)
                    {
                        BeginScroll(menu, first);
                        changed = true;
                    }
                }
            }
            if (changed) Changed?.Invoke();
            return true;
        }

        /// <summary>
        /// Whether a screen point is over an entry the pointer can click (retail then shows its hover pointer).
        /// </summary>
        public bool IsOverEntry(float x, float y)
        {
            if (_open.Length == 0) return false;
            lock (_sync) return TryHit(x, y, out _, out var button, out _) && button != null;
        }

        /// <summary>
        /// The topmost visible menu under a screen point and the entry there (null over the window's body). Page
        /// arrows sit outside their frame, so buttons are tested before the frame.
        /// </summary>
        private bool TryHit(float x, float y, out StockUiOpenMenu? menu, out UiMenuButton? button, out StockUiMenuPlacement placement)
        {
            var placements = _placements;
            var open = _open;
            for (int i = placements.Length - 1; i >= 0; i--)
            {
                var p = placements[i];
                if (Array.IndexOf(open, p.Menu) < 0 || p.Scale <= 0) continue;
                float lx = (x - p.X) / p.Scale, ly = (y - p.Y) / p.Scale;
                UiMenuButton? hit = null;
                foreach (var b in p.Menu.Menu.Buttons)
                {
                    if (lx >= b.X && ly >= b.Y && lx < b.X + b.Width && ly < b.Y + b.Height && IsClickable(p.Menu, b))
                    {
                        hit = b;
                        break;
                    }
                }
                var frame = p.Menu.Menu.Frame;
                if (hit != null || (lx >= 0 && ly >= 0 && lx < frame.Width && ly < frame.Height))
                {
                    menu = p.Menu;
                    button = hit;
                    placement = p;
                    return true;
                }
            }
            menu = null;
            button = null;
            placement = default;
            return false;
        }

        /// <summary>
        /// Buttons the pointer can take: those the cursor can reach (list rows only while they show an entry) and a
        /// paged menu's page arrows (drawn outside the frame only on paged menus).
        /// </summary>
        private static bool IsClickable(StockUiOpenMenu menu, UiMenuButton button) =>
            IsPageArrow(menu, button) || (IsSelectable(button) && IsPopulated(menu, button.ButtonId) && !IsOutsideFrame(menu, button));

        private static bool IsOutsideFrame(StockUiOpenMenu menu, UiMenuButton button) =>
            button.X < 0 || button.X >= menu.Menu.Frame.Width;

        private static bool IsPageArrow(StockUiOpenMenu menu, UiMenuButton button) =>
            menu.PageRing.Count > 1 && IsOutsideFrame(menu, button);

        /// <summary>A slider's value at a screen x: the share of the bar left of the pointer, snapped to the setting's step.</summary>
        private (StockUiSettingKey Key, int Value)? SliderValueAt(StockUiOpenMenu menu, StockUiMenuPlacement placement, UiMenuButton button, float x)
        {
            if (menu.ConfigPage is not { } page || !page.TryGetSlider(button.ButtonId, out var slider) || button.Width <= 0) return null;
            var d = slider.Definition;
            int step = Math.Max(1, d.Step);
            float fraction = Math.Clamp(((x - placement.X) / placement.Scale - button.X) / button.Width, 0f, 1f);
            int value = d.Clamp(d.Min + (int)Math.Round(fraction * (d.Max - d.Min) / step) * step);
            return value != GetSetting(slider.Key) ? (slider.Key, value) : null;
        }

        private bool ApplyEdit(StockUiSettingKey key, int value)
        {
            SetSetting(key, value);
            lock (_sync)
            {
                if (Top is { } top) Refresh(top);
            }
            return true;
        }

        #endregion
    }
}
