// src/Gordian.App/ViewportWindow.axaml.cs
using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Gordian.App.Graphics;
using Gordian.App.Services;
using Gordian.App.ViewModels;
using Gordian.Core.Diagnostics;
using Gordian.Core.Input;
using Gordian.Core.Network;
using Gordian.Core.Ui;

namespace Gordian.App
{
    /// <summary>
    /// Dedicated 3D graphics rendering window for GordianXI character sessions.
    /// Supports BorderlessWindow, Windowed, and Fullscreen modes with auto-hiding tabs.
    /// </summary>
    public partial class ViewportWindow : Window
    {
        private readonly string _windowKey;
        private ViewportViewModel? _viewModel;
        private VeldridViewportControl? _viewportControl;
        private DispatcherTimer? _telemetryTimer;
        private Point? _lastPointerPosition;
        private bool _isRightDragging;
        private readonly Popup? _pillPopup;
        private readonly Popup? _pipPopup;
        private readonly Popup? _freeCamPopup;
        private readonly Popup? _ribbonPopup;
        private readonly Popup? _railPopup;
        private readonly ComboBox? _displayModeCombo;
        private readonly DispatcherTimer _repositionTimer;
        private bool _isShown;
        private bool _ribbonRevealed;
        private bool _railRevealed;

        /// <summary>How close to the top / left edge of the view (in device-independent pixels) reveals the ribbon / rail.</summary>
        private const double EdgeRevealDip = 6.0;

        // The last pointer position over the rendering surface (framebuffer pixels), from the raw Win32 mouse
        // messages; a raw button event carries no position of its own, but a move always precedes it.
        private Point? _lastRawMouse;

        public ViewportWindow() : this("ViewportWindow")
        {
        }

        public ViewportWindow(string windowKey)
        {
            _windowKey = string.IsNullOrWhiteSpace(windowKey) ? "ViewportWindow" : windowKey;
            InitializeComponent();
            WindowPlacementManager.Default.TrackWindow(this, _windowKey);

            _viewportControl = this.FindControl<VeldridViewportControl>("ViewportControl");
            if (_viewportControl != null)
            {
                // Bypasses Avalonia's routed-event tree entirely for mouse buttons/move: see
                // Win32ChildWindowHelper for why the native rendering surface's own mouse messages
                // never reach the InputElement.PointerPressed/Moved handlers below. Wheel is
                // unaffected (Windows routes it by keyboard focus, not hit-test), so it's already
                // handled correctly by the ordinary routed PointerWheelChanged handler.
                _viewportControl.RawMouseButtonDown += OnRawMouseButtonDown;
                _viewportControl.RawMouseButtonUp += OnRawMouseButtonUp;
                _viewportControl.RawMouseMoved += OnRawMouseMoved;
                _viewportControl.RawMouseLeft += OnRawMouseLeft;
                _viewportControl.PointerExited += OnViewportPointerExited;

                // The focused window's character is the one heard (Phase 5H).
                var viewport = _viewportControl;
                Activated += (_, _) => Audio.GameAudioService.Instance.Claim(viewport);
            }

            var minimizeBtn = this.FindControl<Button>("MinimizeButton");
            if (minimizeBtn != null)
            {
                minimizeBtn.Click += (_, _) => WindowState = WindowState.Minimized;
            }

            var closeBtn = this.FindControl<Button>("CloseButton");
            if (closeBtn != null)
            {
                closeBtn.Click += (_, _) => Close();
            }

            DataContextChanged += OnDataContextChanged;

            // Overlays over the native 3D surface are popups (it cannot be drawn over in this window; see the .axaml).
            _pillPopup = this.FindControl<Popup>("FloatingPillPopup");
            _pipPopup = this.FindControl<Popup>("PipDeckPopup");
            _freeCamPopup = this.FindControl<Popup>("FreeCamPopup");
            _ribbonPopup = this.FindControl<Popup>("TopRibbonPopup");
            _railPopup = this.FindControl<Popup>("SideRailPopup");
            _displayModeCombo = this.FindControl<ComboBox>("DisplayModeCombo");
            Opened += (_, _) =>
            {
                _isShown = true;
                UpdateOverlayPopups();
            };
            PropertyChanged += (_, e) =>
            {
                if (e.Property == WindowStateProperty || e.Property == IsVisibleProperty) UpdateOverlayPopups();
            };

            // The floating pill is minimal until the pointer is over it, then lists every character.
            if (this.FindControl<Panel>("PillRoot") is { } pillRoot)
            {
                var collapsed = this.FindControl<Border>("PillCollapsed");
                var expanded = this.FindControl<Border>("PillExpanded");
                pillRoot.PointerEntered += (_, _) => SetPillExpanded(collapsed, expanded, true);
                pillRoot.PointerExited += (_, _) => SetPillExpanded(collapsed, expanded, false);
            }

            // The auto-hiding ribbon and rail hide again once the pointer leaves them (unless a drop-down of theirs is open).
            if (this.FindControl<Border>("TopRibbonBorder") is { } ribbonBorder)
            {
                ribbonBorder.PointerExited += (_, _) =>
                {
                    if (_displayModeCombo?.IsDropDownOpen == true) return;
                    _ribbonRevealed = false;
                    UpdateOverlayPopups();
                };
            }
            if (this.FindControl<Border>("SideRailBorder") is { } railBorder)
            {
                railBorder.PointerExited += (_, _) =>
                {
                    _railRevealed = false;
                    UpdateOverlayPopups();
                };
            }

            // Popups follow the window when it moves; the PiP deck was left behind on the old monitor in game, so once a
            // move settles every open popup is placed again from scratch.
            _repositionTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
            _repositionTimer.Tick += (_, _) =>
            {
                _repositionTimer.Stop();
                ReopenOverlayPopups();
            };
            PositionChanged += (_, _) =>
            {
                _repositionTimer.Stop();
                _repositionTimer.Start();
            };
            ScalingChanged += (_, _) =>
            {
                _repositionTimer.Stop();
                _repositionTimer.Start();
            };

            // Gameplay keyboard/mouse-button input is captured here (the window that actually
            // renders and receives focus during play), not on MainWindow, which never has focus
            // while this window is active and would otherwise miss every key/click.
            AddHandler(InputElement.KeyDownEvent, OnGameKeyDown, RoutingStrategies.Tunnel);
            AddHandler(InputElement.KeyUpEvent, OnGameKeyUp, RoutingStrategies.Tunnel);
            AddHandler(InputElement.TextInputEvent, OnGameTextInput, RoutingStrategies.Tunnel);

            // Pointer press/release/move/wheel are observed on the Bubble phase, after
            // VeldridViewportControl's own handling has already run (on the platforms where it
            // actually fires - see below), so we never pre-empt or race the viewport's own camera
            // input handling. handledEventsToo is required because the control marks these Handled.
            //
            // On Windows the viewport renders into a real native Win32 child window (see
            // Win32ChildWindowHelper), which now claims its own mouse messages, so over the viewport
            // only the Raw* handlers above fire; these routed handlers see the wheel (routed by focus)
            // and, on platforms where NativeControlHost is Avalonia-composited, the pointer too.
            AddHandler(InputElement.PointerPressedEvent, OnGamePointerPressed, RoutingStrategies.Bubble, handledEventsToo: true);
            AddHandler(InputElement.PointerReleasedEvent, OnGamePointerReleased, RoutingStrategies.Bubble, handledEventsToo: true);
            AddHandler(InputElement.PointerMovedEvent, OnGamePointerMoved, RoutingStrategies.Bubble, handledEventsToo: true);
            AddHandler(InputElement.PointerWheelChangedEvent, OnGamePointerWheelChanged, RoutingStrategies.Bubble, handledEventsToo: true);

            // Start live telemetry syncing between ViewportControl and ViewModel
            _telemetryTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(250)
            };
            _telemetryTimer.Tick += OnTelemetryTick;
            _telemetryTimer.Start();
        }

        private void OnDataContextChanged(object? sender, EventArgs e)
        {
            if (_viewModel != null)
            {
                _viewModel.DisplayModeChanged -= OnDisplayModeChanged;
                _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
                _viewModel.WindowActivationRequested -= OnWindowActivationRequested;
            }

            _viewModel = DataContext as ViewportViewModel;

            if (_viewModel != null)
            {
                _viewModel.DisplayModeChanged += OnDisplayModeChanged;
                _viewModel.PropertyChanged += OnViewModelPropertyChanged;
                _viewModel.WindowActivationRequested += OnWindowActivationRequested;
                ApplyDisplayMode(_viewModel.SelectedDisplayMode);
                SyncActiveSessionToViewport();
            }
            UpdateOverlayPopups();
        }

        private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(ViewportViewModel.ActiveTab) or nameof(ViewportViewModel.Lobby) or nameof(ViewportViewModel.IsReturningToLobby))
            {
                SyncActiveSessionToViewport();
            }
            if (e.PropertyName is nameof(ViewportViewModel.ShowFloatingPill) or nameof(ViewportViewModel.ShowPipDeck)
                or nameof(ViewportViewModel.ShowTopRibbon) or nameof(ViewportViewModel.ShowSideRail)
                or nameof(ViewportViewModel.IsFreeCamActive) or nameof(ViewportViewModel.Lobby) or nameof(ViewportViewModel.IsReturningToLobby))
            {
                UpdateOverlayPopups();
            }
        }

        /// <summary>
        /// A switcher chose this window's character (a click, or Ctrl+Tab from another window): take the focus, so the
        /// gamepad and keyboard follow at once. The switchers are popups that never activate the window themselves.
        /// </summary>
        private void OnWindowActivationRequested(object? sender, EventArgs e)
        {
            if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
            Activate();
        }

        /// <summary>
        /// Opens or closes the popups drawn over the native 3D surface: the floating pill, PiP deck and free camera banner
        /// while their view model flag is set, the ribbon and rail while also revealed by the pointer; all closed while the
        /// window is hidden or minimised (a popup is its own window and would otherwise be left floating).
        /// </summary>
        private void UpdateOverlayPopups()
        {
            bool onScreen = _isShown && IsVisible && WindowState != WindowState.Minimized;
            bool showsWorld = _viewModel is { IsLobbyOpen: false, IsReturningToLobby: false } && _viewModel.ActiveTab != null;
            SetPopupOpen(_pillPopup, onScreen && _viewModel?.ShowFloatingPill == true);
            SetPopupOpen(_pipPopup, onScreen && _viewModel?.ShowPipDeck == true);
            SetPopupOpen(_freeCamPopup, onScreen && showsWorld && _viewModel?.IsFreeCamActive == true);
            SetPopupOpen(_ribbonPopup, onScreen && _ribbonRevealed && _viewModel?.ShowTopRibbon == true);
            SetPopupOpen(_railPopup, onScreen && _railRevealed && _viewModel?.ShowSideRail == true);
        }

        /// <summary>Closes and reopens every open popup so it is placed again against the window where it now is.</summary>
        private void ReopenOverlayPopups()
        {
            foreach (var popup in new[] { _pillPopup, _pipPopup, _freeCamPopup, _ribbonPopup, _railPopup })
            {
                if (popup is not { IsOpen: true }) continue;
                SetPopupOpen(popup, false);
                SetPopupOpen(popup, true);
            }
        }

        /// <summary>
        /// The pointer over the 3D surface (surface pixels): at the top edge it reveals the ribbon, at the left edge the
        /// rail; anywhere else on the surface it hides them (the pointer is off them, since they cover their edge).
        /// </summary>
        private void UpdateEdgeReveal(Point surfacePoint)
        {
            if (_viewModel == null || _isRightDragging) return;
            double edge = EdgeRevealDip * RenderScaling;
            bool ribbon = _viewModel.ShowTopRibbon && surfacePoint.Y <= edge;
            bool rail = _viewModel.ShowSideRail && surfacePoint.X <= edge;
            if (_displayModeCombo?.IsDropDownOpen == true) ribbon = _ribbonRevealed;
            if (ribbon == _ribbonRevealed && rail == _railRevealed) return;
            _ribbonRevealed = ribbon;
            _railRevealed = rail;
            UpdateOverlayPopups();
        }

        private static void SetPillExpanded(Border? collapsed, Border? expanded, bool expand)
        {
            if (collapsed != null) collapsed.IsVisible = !expand;
            if (expanded != null) expanded.IsVisible = expand;
        }

        private static void SetPopupOpen(Popup? popup, bool open)
        {
            if (popup == null || popup.IsOpen == open) return;
            try
            {
                popup.IsOpen = open;
            }
            catch (Exception ex)
            {
                GordianLog.Warning("Viewport", $"Could not {(open ? "open" : "close")} the {popup.Name} overlay: {ex.Message}");
            }
        }

        private void SyncActiveSessionToViewport()
        {
            var viewportControl = this.FindControl<VeldridViewportControl>("ViewportControl");
            if (viewportControl != null)
            {
                viewportControl.ResourceManager = AppResourceManager.Instance;
                viewportControl.ActiveSession = _viewModel?.ActiveTab?.Session;
                viewportControl.Lobby = _viewModel?.Lobby;
                viewportControl.HoldBlack = _viewModel?.IsReturningToLobby == true;
            }
        }

        /// <summary>The character lobby on show in this window, if any (it takes the keyboard and mouse).</summary>
        private Gordian.Core.Ui.Lobby.LobbyController? ActiveLobby => _viewModel?.Lobby;

        /// <summary>
        /// The lobby's keys: the arrows and numeric keypad 8/2/4/6 move the cursor, Enter / keypad 5 confirm, Escape
        /// cancels, Backspace deletes a letter of a name being typed. Every key is the lobby's while it is open.
        /// </summary>
        private static void HandleLobbyKey(Gordian.Core.Ui.Lobby.LobbyController lobby, KeyEventArgs e)
        {
            Gordian.Core.Ui.Lobby.LobbyInput? input = e.Key switch
            {
                Key.Up or Key.NumPad8 => Gordian.Core.Ui.Lobby.LobbyInput.Up,
                Key.Down or Key.NumPad2 => Gordian.Core.Ui.Lobby.LobbyInput.Down,
                Key.Left or Key.NumPad4 => Gordian.Core.Ui.Lobby.LobbyInput.Left,
                Key.Right or Key.NumPad6 => Gordian.Core.Ui.Lobby.LobbyInput.Right,
                Key.Enter or Key.NumPad5 => Gordian.Core.Ui.Lobby.LobbyInput.Confirm,
                Key.Escape => Gordian.Core.Ui.Lobby.LobbyInput.Cancel,
                Key.Back => Gordian.Core.Ui.Lobby.LobbyInput.Backspace,
                _ => null,
            };
            // Letter keys stay unhandled so their text input follows (the name step types it, OnGameTextInput); Avalonia
            // drops the WM_CHAR text input after a handled key press on Windows.
            if (input is not { } action) return;
            lobby.HandleInput(action);
            e.Handled = true;
        }

        /// <summary>The mouse over the lobby: hovering a button moves the cursor there, a left press activates it, a right press cancels.</summary>
        private void LobbyPointer(Gordian.Core.Ui.Lobby.LobbyController lobby, Point point, Avalonia.Input.MouseButton? pressed)
        {
            if (_viewportControl == null) return;
            var (width, height) = _viewportControl.SurfaceSize;
            if (pressed == Avalonia.Input.MouseButton.Right)
            {
                lobby.HandleInput(Gordian.Core.Ui.Lobby.LobbyInput.Cancel);
                return;
            }
            if (StockUiLobby.HitTest(lobby, width, height, (float)point.X, (float)point.Y) is not { } hit) return;
            if (pressed == Avalonia.Input.MouseButton.Left) lobby.Activate(hit.Menu, hit.ButtonId);
            else if (pressed == null) lobby.PointAt(hit.Menu, hit.ButtonId);
        }

        private void OnDisplayModeChanged(object? sender, ViewportDisplayMode mode)
        {
            Dispatcher.UIThread.Post(() => ApplyDisplayMode(mode));
        }

        /// <summary>
        /// Applies the requested display mode (Borderless, Windowed, Fullscreen).
        /// </summary>
        public void ApplyDisplayMode(ViewportDisplayMode mode)
        {
            switch (mode)
            {
                case ViewportDisplayMode.BorderlessWindow:
                    WindowDecorations = Avalonia.Controls.WindowDecorations.None;
                    WindowState = WindowState.Normal;
                    var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
                    if (screen != null)
                    {
                        var area = screen.WorkingArea;
                        Position = new PixelPoint(area.X, area.Y);
                        Width = area.Width / (screen.Scaling > 0 ? screen.Scaling : 1.0);
                        Height = area.Height / (screen.Scaling > 0 ? screen.Scaling : 1.0);
                    }
                    break;

                case ViewportDisplayMode.Fullscreen:
                    WindowDecorations = Avalonia.Controls.WindowDecorations.None;
                    WindowState = WindowState.FullScreen;
                    break;

                case ViewportDisplayMode.Windowed:
                default:
                    WindowDecorations = Avalonia.Controls.WindowDecorations.Full;
                    WindowState = WindowState.Normal;
                    var placement = WindowPlacementManager.Default.GetPlacement(_windowKey);
                    if (placement != null && placement.Width > 0 && placement.Height > 0)
                    {
                        Width = placement.Width;
                        Height = placement.Height;
                        Position = new PixelPoint(placement.X, placement.Y);
                    }
                    else
                    {
                        Width = 1280;
                        Height = 720;
                    }
                    break;
            }
        }

        /// <summary>
        /// The window's own shortcuts (see <see cref="ViewportShortcuts"/>): character switching, display mode, and the
        /// fog / post-process / ocean / weather / time-of-day debug toggles. They never reach the character's input.
        /// </summary>
        private void ApplyShortcut(ViewportShortcut shortcut)
        {
            switch (shortcut)
            {
                case ViewportShortcut.NextCharacter:
                case ViewportShortcut.PreviousCharacter:
                    if (_viewModel == null) return;
                    var before = _viewModel.ActiveTab;
                    var chosen = shortcut == ViewportShortcut.NextCharacter ? _viewModel.CycleNextCharacter() : _viewModel.CyclePreviousCharacter();
                    GordianLog.Info("Viewport", $"{(shortcut == ViewportShortcut.NextCharacter ? "Ctrl+Tab" : "Ctrl+Shift+Tab")}: " +
                        $"{before?.CharacterName ?? "none"} -> {chosen?.CharacterName ?? "none"}{(chosen?.IsPoppedOut == true ? " (its own window)" : string.Empty)}");
                    break;
                case ViewportShortcut.ToggleFullscreen:
                    if (_viewModel == null) return;
                    _viewModel.SelectedDisplayMode = _viewModel.SelectedDisplayMode == ViewportDisplayMode.Fullscreen
                        ? ViewportDisplayMode.BorderlessWindow
                        : ViewportDisplayMode.Fullscreen;
                    break;
                case ViewportShortcut.ToggleFog: _viewportControl?.ToggleFog(); break;
                case ViewportShortcut.TogglePostProcess: _viewportControl?.TogglePostProcess(); break;
                case ViewportShortcut.ToggleOceanWater: _viewportControl?.ToggleOceanWater(); break;
                case ViewportShortcut.CycleWeather: _viewportControl?.CycleWeather(); break;
                case ViewportShortcut.CycleTimeOfDay: _viewportControl?.CycleTimeOfDay(); break;
            }
        }

        private void OnGameKeyDown(object? sender, KeyEventArgs e)
        {
            // Window-level shortcuts are taken here, on the tunnelling pass, before a focused control (or Avalonia's
            // Tab navigation) can take the key, and are not fed into the character's InputState.
            var shortcut = ViewportShortcuts.Classify(e.Key, e.KeyModifiers);
            if (shortcut != ViewportShortcut.None)
            {
                ApplyShortcut(shortcut);
                e.Handled = true;
                return;
            }

            if (ActiveLobby is { } lobby)
            {
                HandleLobbyKey(lobby, e);
                return;
            }

            var session = _viewModel?.ActiveTab?.Session;
            if (session == null) return;

            var gKey = AvaloniaInputMapper.ToGordianKey(e.Key);
            var mods = AvaloniaInputMapper.ToInputModifiers(e.KeyModifiers);

            // The stock chat input line owns the keyboard while it is open (the character does not move while you
            // type); Enter or the chat key opens it from gameplay. Characters arrive as text input (OnGameTextInput);
            // Avalonia drops the WM_CHAR text input that follows a handled key press on Windows, so character keys
            // are not handled here.
            var chat = session.Chat;
            bool typed = StockUiChatInput.IsTypedSymbol(e.KeySymbol) && (mods & (InputModifiers.Control | InputModifiers.Alt)) == 0;
            if (chat.Input.IsOpen)
            {
                // Tab / Shift+Tab still cycle targets while typing.
                if (gKey == GordianKey.Tab)
                {
                    session.InputState.SetModifiers(mods);
                    session.InputState.SetKeyDown(gKey);
                    e.Handled = true;
                    return;
                }
                // A character key is left unhandled so the platform's text input for it follows (OnGameTextInput):
                // that text carries Shift, Caps Lock and the keyboard layout, which the key symbol does not (it
                // reports letters in upper case). Handling the key press would make Avalonia drop that text.
                if (typed) return;
                if (gKey == GordianKey.V && mods == InputModifiers.Control) _ = PasteIntoChatAsync(chat.Input);
                else chat.Input.HandleKey(gKey, mods);
                e.Handled = true;
                return;
            }
            // With the log selected (keypad +) Enter is Confirm: it opens the full-screen log, not the input line.
            bool confirmTaken = session.ActionService.CurrentTarget != null || session.Events.IsActive || chat.IsSelecting;
            if (gKey != GordianKey.None && chat.TryOpen(gKey, mods, session.Locomotion.Profile, session.ActionService.Menus.IsOpen, e.KeySymbol, confirmTaken))
            {
                e.Handled = true;
                return;
            }

            // Escape locks an unlocked stock UI (once no menu is open for it to cancel first).
            if (e.Key == Key.Escape && !session.ActionService.Menus.IsOpen && session.ActionService.UiDrag.Unlocked)
            {
                session.ActionService.UiLayout.SetUnlocked(false);
                e.Handled = true;
                return;
            }

            if (gKey is GordianKey.T or GordianKey.NumPadMultiply)
            {
                Gordian.Core.Diagnostics.GordianLog.Info("LockOn", $"KeyDown {e.Key} -> {gKey} reached the game input (modifiers {mods})");
            }

            if (gKey != GordianKey.None)
            {
                session.InputState.SetModifiers(mods);
                session.InputState.SetKeyDown(gKey);

                // Suppress default UI focus navigation for gameplay keys like Tab or arrows
                if (e.Key is Key.Tab or Key.Up or Key.Down or Key.Left or Key.Right)
                {
                    e.Handled = true;
                }
            }
        }

        /// <summary>Typed text for the stock chat input line (shifted and layout-specific characters included).</summary>
        private void OnGameTextInput(object? sender, TextInputEventArgs e)
        {
            if (ActiveLobby is { } lobby)
            {
                if (!string.IsNullOrEmpty(e.Text)) lobby.HandleText(e.Text);
                e.Handled = true;
                return;
            }
            var input = _viewModel?.ActiveTab?.Session?.Chat.Input;
            if (input == null || !input.IsOpen || string.IsNullOrEmpty(e.Text)) return;
            input.InsertText(e.Text);
            e.Handled = true;
        }

        private async System.Threading.Tasks.Task PasteIntoChatAsync(StockUiChatInput input)
        {
            var clipboard = Clipboard;
            if (clipboard == null) return;
            string? text = await clipboard.TryGetTextAsync();
            if (!string.IsNullOrEmpty(text)) input.InsertText(text.Replace("\r", string.Empty).Replace('\n', ' '));
        }

        private void OnGameKeyUp(object? sender, KeyEventArgs e)
        {
            if (ActiveLobby != null) return;
            var session = _viewModel?.ActiveTab?.Session;
            if (session == null) return;

            var gKey = AvaloniaInputMapper.ToGordianKey(e.Key);
            var mods = AvaloniaInputMapper.ToInputModifiers(e.KeyModifiers);

            if (gKey != GordianKey.None)
            {
                session.InputState.SetModifiers(mods);
                session.InputState.SetKeyUp(gKey);
            }
        }

        private void OnGamePointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (ActiveLobby is { } lobby)
            {
                if (TryGetViewportPoint(e, out var lobbyPoint))
                {
                    LobbyPointer(lobby, lobbyPoint, e.Properties.IsRightButtonPressed ? Avalonia.Input.MouseButton.Right
                        : e.Properties.IsLeftButtonPressed ? Avalonia.Input.MouseButton.Left : null);
                }
                return;
            }
            var session = _viewModel?.ActiveTab?.Session;
            if (session == null) return;

            // Use e.Properties directly rather than GetCurrentPoint(this): the latter needs a
            // coordinate transform from wherever the pointer actually is (including from inside
            // the native-embedded viewport surface) into this Window's space, which is exactly
            // the kind of cross-boundary transform NativeControlHost content cannot reliably
            // provide. Properties only reports button state, so no transform is needed.
            var btn = AvaloniaInputMapper.ToMouseButton(e.Properties);

            // An unlocked stock UI takes a left press over one of its windows as the start of a drag, and an open
            // menu takes presses over it; neither is game input.
            if (TryGetViewportPoint(e, out var point) && TryStockUiPress("avalonia", session, btn, point)) return;
            session.InputState.SetMouseButtonDown(btn);

            if (e.Properties.IsRightButtonPressed)
            {
                _isRightDragging = true;
                _lastPointerPosition = e.GetPosition(this);
            }
        }

        private void OnGamePointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            var session = _viewModel?.ActiveTab?.Session;
            if (session == null) return;

            // InitialPressMouseButton identifies which button this release corresponds to; by
            // release time e.Properties would already show it as up. Also avoids GetCurrentPoint
            // (see OnGamePointerPressed).
            var btn = AvaloniaInputMapper.ToMouseButton(e.InitialPressMouseButton);
            if (TryGetViewportPoint(e, out var point) && TryStockUiRelease(session, btn, point))
            {
                return; // the release ends a stock UI press (window drag, menu click) that never reached the input bus
            }
            session.InputState.SetMouseButtonUp(btn);

            if (e.InitialPressMouseButton == Avalonia.Input.MouseButton.Right)
            {
                _isRightDragging = false;
                _lastPointerPosition = null;
            }
        }

        private void OnGamePointerMoved(object? sender, PointerEventArgs e)
        {
            if (TryGetViewportPoint(e, out var edgePoint)) UpdateEdgeReveal(edgePoint);
            if (ActiveLobby is { } lobby)
            {
                if (TryGetViewportPoint(e, out var lobbyPoint)) LobbyPointer(lobby, lobbyPoint, null);
                return;
            }
            var session = _viewModel?.ActiveTab?.Session;
            if (session == null) return;
            if (TryGetViewportPoint(e, out var point)) StockUiMove(session, point);
            UpdateViewportCursor();
            if (!_isRightDragging || !_lastPointerPosition.HasValue) return;

            var currentPos = e.GetPosition(this);
            float dx = (float)(currentPos.X - _lastPointerPosition.Value.X);
            float dy = (float)(currentPos.Y - _lastPointerPosition.Value.Y);
            _lastPointerPosition = currentPos;

            session.InputState.AddMouseDelta(dx, dy);
        }

        private void OnGamePointerWheelChanged(object? sender, PointerWheelEventArgs e)
        {
            var session = _viewModel?.ActiveTab?.Session;
            if (session == null) return;

            // The wheel is routed by focus, not by position, so on Windows it arrives here with the viewport's
            // pointer known only from the raw moves.
            Point? point = _lastRawMouse;
            if (point == null && TryGetViewportPoint(e, out var routed)) point = routed;
            if (point is { } p && session.ActionService.Menus.OnMouseWheel((float)p.X, (float)p.Y, (float)e.Delta.Y)) return;
            session.InputState.AddMouseWheel((float)e.Delta.Y);
        }

        /// <summary>
        /// Offers a mouse press to the stock UI: the unlocked UI's drag (left button), then an open menu under the
        /// pointer. Returns true when the stock UI took it, so it is not game input.
        /// </summary>
        private bool TryStockUiPress(string path, CharacterSession session, Gordian.Core.Input.MouseButton button, Point point)
        {
            float x = (float)point.X, y = (float)point.Y;
            if (button == Gordian.Core.Input.MouseButton.Left)
            {
                var drag = session.ActionService.UiDrag;
                if (drag.Unlocked) LogUnlockedPress(path, drag, x, y);
                if (drag.OnMouseDown(x, y)) return true;
            }
            return session.ActionService.Menus.OnMouseDown(button, x, y);
        }

        /// <summary>Offers a mouse release to the stock UI; true when its press was the stock UI's.</summary>
        private static bool TryStockUiRelease(CharacterSession session, Gordian.Core.Input.MouseButton button, Point point)
        {
            float x = (float)point.X, y = (float)point.Y;
            bool dragged = button == Gordian.Core.Input.MouseButton.Left && session.ActionService.UiDrag.OnMouseUp(x, y);
            bool menu = session.ActionService.Menus.OnMouseUp(button, x, y);
            return dragged || menu;
        }

        /// <summary>The pointer moved over the viewport: the stock pointer, the unlocked UI's drag and menu hover follow it.</summary>
        private static void StockUiMove(CharacterSession session, Point point)
        {
            float x = (float)point.X, y = (float)point.Y;
            session.ActionService.UiPointer.MoveTo(x, y);
            session.ActionService.UiDrag.OnMouseMove(x, y);
            session.ActionService.Menus.OnMouseMove(x, y);
        }

        private void OnViewportPointerExited(object? sender, PointerEventArgs e) => _viewModel?.ActiveTab?.Session?.ActionService.UiPointer.Leave();

        private void OnRawMouseLeft()
        {
            _lastRawMouse = null;
            _viewModel?.ActiveTab?.Session?.ActionService.UiPointer.Leave();
        }

        /// <summary>
        /// The pointer's position in the rendering surface's pixels (the stock UI's screen space), for the Avalonia
        /// pointer events that do fire (platforms where the viewport is composited rather than a native child).
        /// </summary>
        /// <summary>
        /// Logs an unlocked-UI press: which pointer path delivered it, where, what it hit and the surface size, so a
        /// drag that does not start in-game can be traced (the viewport's mouse reaches us by different routes per
        /// platform and host window).
        /// </summary>
        private void LogUnlockedPress(string path, StockUiDragController drag, float x, float y)
        {
            var regions = drag.Regions;
            string hit = drag.HitTest(x, y) ?? "nothing";
            double scale = RenderScaling;
            var bounds = _viewportControl?.Bounds ?? default;
            GordianLog.Info("UI", $"Stock UI unlocked press ({path}) at ({x:0}, {y:0}) hit {hit}; {regions.Count} regions; " +
                $"viewport {bounds.Width:0} x {bounds.Height:0} at scale {scale:0.##}");
        }

        private bool TryGetViewportPoint(PointerEventArgs e, out Point point)
        {
            if (_viewportControl == null)
            {
                point = default;
                return false;
            }
            var position = e.GetPosition(_viewportControl);
            double scale = RenderScaling;
            point = new Point(position.X * scale, position.Y * scale);
            return true;
        }

        private void OnRawMouseButtonDown(Avalonia.Input.MouseButton button)
        {
            if (ActiveLobby is { } lobby)
            {
                if (_lastRawMouse is { } lobbyPoint) LobbyPointer(lobby, lobbyPoint, button);
                return;
            }
            var session = _viewModel?.ActiveTab?.Session;
            if (session == null) return;

            var btn = AvaloniaInputMapper.ToMouseButton(button);
            if (_lastRawMouse is { } point && TryStockUiPress("raw", session, btn, point)) return;
            session.InputState.SetMouseButtonDown(btn);

            if (button == Avalonia.Input.MouseButton.Right)
            {
                _isRightDragging = true;
                _lastPointerPosition = null;
            }
        }

        private void OnRawMouseButtonUp(Avalonia.Input.MouseButton button)
        {
            if (ActiveLobby != null) return;
            var session = _viewModel?.ActiveTab?.Session;
            if (session == null) return;

            var btn = AvaloniaInputMapper.ToMouseButton(button);
            if (TryStockUiRelease(session, btn, _lastRawMouse ?? default)) return;
            session.InputState.SetMouseButtonUp(btn);

            if (button == Avalonia.Input.MouseButton.Right)
            {
                _isRightDragging = false;
                _lastPointerPosition = null;
            }
        }

        private void OnRawMouseMoved(double x, double y)
        {
            _lastRawMouse = new Point(x, y);
            UpdateEdgeReveal(new Point(x, y));
            if (ActiveLobby is { } lobby)
            {
                LobbyPointer(lobby, new Point(x, y), null);
                return;
            }
            var session = _viewModel?.ActiveTab?.Session;
            if (session == null) return;
            StockUiMove(session, new Point(x, y));
            if (!_isRightDragging) return;

            if (_lastPointerPosition.HasValue)
            {
                float dx = (float)(x - _lastPointerPosition.Value.X);
                float dy = (float)(y - _lastPointerPosition.Value.Y);
                session.InputState.AddMouseDelta(dx, dy);
            }

            _lastPointerPosition = new Point(x, y);
        }

        private void OnTelemetryTick(object? sender, EventArgs e)
        {
            var viewportControl = this.FindControl<VeldridViewportControl>("ViewportControl");
            if (viewportControl != null && _viewModel != null)
            {
                if (viewportControl.ActiveSession != _viewModel.ActiveTab?.Session)
                {
                    viewportControl.ActiveSession = _viewModel.ActiveTab?.Session;
                }
                if (!ReferenceEquals(viewportControl.Lobby, _viewModel.Lobby)) viewportControl.Lobby = _viewModel.Lobby;
                viewportControl.HoldBlack = _viewModel.IsReturningToLobby;
                if (viewportControl.ResourceManager == null)
                {
                    viewportControl.ResourceManager = AppResourceManager.Instance;
                }

                _viewModel.Fps = viewportControl.CurrentFps;
                _viewModel.FrameTimeMs = viewportControl.FrameTimeMs;
                _viewModel.ActiveBackend = viewportControl.ActiveBackendName;
                _viewModel.GpuName = viewportControl.GpuDeviceName;
                _viewModel.SyncFromActiveSession();

                UpdateViewportCursor();
            }
        }

        /// <summary>
        /// The pointer over the viewport is the stock arrow, hidden while the HUD draws the hover pointer over a menu
        /// entry. On Windows the native surface picks it itself (WM_SETCURSOR); elsewhere the control's cursor is set.
        /// </summary>
        private void UpdateViewportCursor()
        {
            if (OperatingSystem.IsWindows() || _viewportControl == null) return;
            var cursor = _viewportControl.StockUi.PointerDrawn ? HiddenCursor : ArrowCursor.Value;
            if (!ReferenceEquals(_viewportControl.Cursor, cursor)) _viewportControl.Cursor = cursor;
        }

        private static readonly Cursor HiddenCursor = new(StandardCursorType.None);

        private static readonly Lazy<Cursor> ArrowCursor = new(() =>
        {
            var art = StockUiPointerArt.Arrow;
            var bitmap = new Avalonia.Media.Imaging.WriteableBitmap(new PixelSize(art.Width, art.Height), new Vector(96, 96),
                Avalonia.Platform.PixelFormat.Rgba8888, Avalonia.Platform.AlphaFormat.Unpremul);
            using (var buffer = bitmap.Lock())
            {
                for (int row = 0; row < art.Height; row++)
                {
                    System.Runtime.InteropServices.Marshal.Copy(art.RgbaPixels, row * art.Width * 4, buffer.Address + row * buffer.RowBytes, art.Width * 4);
                }
            }
            return new Cursor(bitmap, new PixelPoint(StockUiPointerArt.HotspotX, StockUiPointerArt.HotspotY));
        });

        protected override void OnClosed(EventArgs e)
        {
            _telemetryTimer?.Stop();
            _telemetryTimer = null;
            _repositionTimer.Stop();
            _isShown = false;
            UpdateOverlayPopups();
            if (_viewModel != null) _viewModel.WindowActivationRequested -= OnWindowActivationRequested;

            if (_viewModel != null)
            {
                _viewModel.DisplayModeChanged -= OnDisplayModeChanged;
                _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            }

            if (_viewportControl != null)
            {
                _viewportControl.RawMouseButtonDown -= OnRawMouseButtonDown;
                _viewportControl.RawMouseButtonUp -= OnRawMouseButtonUp;
                _viewportControl.RawMouseMoved -= OnRawMouseMoved;
            }

            base.OnClosed(e);
        }
    }
}
