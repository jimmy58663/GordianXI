// src/Gordian.App/ViewportWindow.axaml.cs
using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Gordian.App.Graphics;
using Gordian.App.Services;
using Gordian.App.ViewModels;
using Gordian.Core.Input;

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

            // Pointer press/release/move/wheel are observed on the Bubble phase, after
            // VeldridViewportControl's own handling has already run (on the platforms where it
            // actually fires - see below), so we never pre-empt or race the viewport's own camera
            // input handling. handledEventsToo is required because the control marks these Handled.
            //
            // On Windows the viewport renders into a real native Win32 child window (see
            // Win32ChildWindowHelper), so the OS delivers that window's mouse messages directly to
            // it, never through Avalonia's routed-event tree - VeldridViewportControl's own
            // OnPointerMoved/OnPointerWheelChanged overrides simply never fire there. This window
            // level InputState bus (consumed by PlayerLocomotionController) is what actually drives
            // right-click camera look and wheel zoom in practice; the control's own handling is a
            // fallback for platforms where NativeControlHost is Avalonia-composited instead.
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
            if (session == null || !_isRightDragging || !_lastPointerPosition.HasValue) return;

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

            session.InputState.AddMouseWheel((float)e.Delta.Y);
        }

        private void OnRawMouseButtonDown(Avalonia.Input.MouseButton button)
        {
            var session = _viewModel?.ActiveTab?.Session;
            if (session == null) return;

            session.InputState.SetMouseButtonDown(AvaloniaInputMapper.ToMouseButton(button));

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

            session.InputState.SetMouseButtonUp(AvaloniaInputMapper.ToMouseButton(button));

            if (button == Avalonia.Input.MouseButton.Right)
            {
                _isRightDragging = false;
                _lastPointerPosition = null;
            }
        }

        private void OnRawMouseMoved(double x, double y)
        {
            var session = _viewModel?.ActiveTab?.Session;
            if (session == null || !_isRightDragging) return;

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
            }
        }

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
