// src/Gordian.App/ViewportWindow.axaml.cs
using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
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
            KeyDown += OnKeyDown;

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
            }

            _viewModel = DataContext as ViewportViewModel;

            if (_viewModel != null)
            {
                _viewModel.DisplayModeChanged += OnDisplayModeChanged;
                _viewModel.PropertyChanged += OnViewModelPropertyChanged;
                ApplyDisplayMode(_viewModel.SelectedDisplayMode);
                SyncActiveSessionToViewport();
            }
        }

        private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ViewportViewModel.ActiveTab))
            {
                SyncActiveSessionToViewport();
            }
        }

        private void SyncActiveSessionToViewport()
        {
            var viewportControl = this.FindControl<VeldridViewportControl>("ViewportControl");
            if (viewportControl != null)
            {
                viewportControl.ResourceManager = AppResourceManager.Instance;
                viewportControl.ActiveSession = _viewModel?.ActiveTab?.Session;
            }
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

        private void OnKeyDown(object? sender, KeyEventArgs e)
        {
            // Ctrl+Tab / Ctrl+Shift+Tab to cycle character viewports
            if (e.Key == Key.Tab && (e.KeyModifiers & KeyModifiers.Control) != 0)
            {
                if ((e.KeyModifiers & KeyModifiers.Shift) != 0)
                {
                    _viewModel?.CyclePreviousCharacter();
                }
                else
                {
                    _viewModel?.CycleNextCharacter();
                }
                e.Handled = true;
                return;
            }

            // F11 toggles Fullscreen / Borderless
            if (e.Key == Key.F11 && _viewModel != null)
            {
                _viewModel.SelectedDisplayMode = _viewModel.SelectedDisplayMode == ViewportDisplayMode.Fullscreen
                    ? ViewportDisplayMode.BorderlessWindow
                    : ViewportDisplayMode.Fullscreen;
                e.Handled = true;
                return;
            }

            // Ctrl+F10 toggles distance fog on/off
            if (e.Key == Key.F10 && (e.KeyModifiers & KeyModifiers.Control) != 0)
            {
                _viewportControl?.ToggleFog();
                e.Handled = true;
                return;
            }

            // Ctrl+F9 toggles base sea-level ocean water plane on/off
            if (e.Key == Key.F9 && (e.KeyModifiers & KeyModifiers.Control) != 0)
            {
                _viewportControl?.ToggleOceanWater();
                e.Handled = true;
                return;
            }

            // F9 cycles active Weather presets (Clear "fine" -> Sunshine "suny" -> Clouds "clod" -> Fog "mist")
            if (e.Key == Key.F9 && (e.KeyModifiers & KeyModifiers.Control) == 0)
            {
                _viewportControl?.CycleWeather();
                e.Handled = true;
                return;
            }

            // F10 cycles Time of Day presets (Day -> Dusk -> Night -> Overcast)
            if (e.Key == Key.F10)
            {
                _viewportControl?.CycleTimeOfDay();
                e.Handled = true;
                return;
            }
        }

        private void OnGameKeyDown(object? sender, KeyEventArgs e)
        {
            // Reserved for window-level shortcuts (character/viewport cycling, fullscreen toggle, TOD cycle);
            // don't also feed these into the character's InputState.
            if ((e.Key == Key.Tab && (e.KeyModifiers & KeyModifiers.Control) != 0) ||
                e.Key == Key.F11 || e.Key == Key.F10 || e.Key == Key.F9)
            {
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
            bool confirmTaken = session.ActionService.CurrentTarget != null || session.Events.IsActive;
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
                if (viewportControl.ResourceManager == null)
                {
                    viewportControl.ResourceManager = AppResourceManager.Instance;
                }

                _viewModel.Fps = viewportControl.CurrentFps;
                _viewModel.FrameTimeMs = viewportControl.FrameTimeMs;
                _viewModel.ActiveBackend = viewportControl.ActiveBackendName;
                _viewModel.GpuName = viewportControl.GpuDeviceName;

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
