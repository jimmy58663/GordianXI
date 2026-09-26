// src/Gordian.Core/Ui/StockUiMenuController.cs
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
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

    /// <summary>
    /// One stock menu window currently open: its DAT layout, the button under the cursor and its place in the stack.
    /// Instances are immutable to readers except the fields the controller updates (selection, markers, slider
    /// fills, list rows), which are replaced whole so the render thread can read them without a lock.
    /// </summary>
    public sealed class StockUiOpenMenu
    {
        private static readonly IReadOnlyDictionary<int, float> NoSliders = new Dictionary<int, float>();
        private HashSet<int>? _marked;

        internal StockUiOpenMenu(UiMenuDefinition menu, StockUiOpenMenu? parent, IReadOnlyList<string> pageRing, string? message)
        {
            Menu = menu;
            Parent = parent;
            PageRing = pageRing;
            Message = message;
            StockUiConfigPages.TryGet(menu.Name, out var page);
            ConfigPage = page;
            IsChatFilterList = menu.Name.Equals(StockUiConfigPages.ChatFiltersPage, StringComparison.OrdinalIgnoreCase);
            VisibleRows = IsChatFilterList ? StockUiConfigPages.ChatFilterRowsPerPage : 0;
        }

        public UiMenuDefinition Menu { get; }
        public string Name => Menu.Name;
        public StockUiOpenMenu? Parent { get; }

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

        #region Opening and closing

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
            foreach (var menu in closed) menu.Prompt?.TrySetResult(false);
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

        private void Remember(StockUiOpenMenu menu)
        {
            if (menu.IsPrompt) return;
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
        }

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

            if (!StockUiMenuEntries.TryGet(top.Name, button.ButtonId, out var entry)) return;
            if (entry.Opens != null)
            {
                lock (_sync) Push(entry.Opens, top, Array.Empty<string>(), null, null);
                return;
            }
            Run(entry, top);
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
    }
}
