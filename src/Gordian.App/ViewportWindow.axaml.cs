// src/Gordian.App/ViewportWindow.axaml.cs
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Gordian.App.Graphics;
using Gordian.App.ViewModels;

namespace Gordian.App
{
    /// <summary>
    /// Dedicated 3D graphics rendering window for GordianXI character sessions.
    /// Supports BorderlessWindow, Windowed, and Fullscreen modes with auto-hiding tabs.
    /// </summary>
    public partial class ViewportWindow : Window
    {
        private ViewportViewModel? _viewModel;
        private DispatcherTimer? _telemetryTimer;

        public ViewportWindow()
        {
            InitializeComponent();

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
            }

            _viewModel = DataContext as ViewportViewModel;

            if (_viewModel != null)
            {
                _viewModel.DisplayModeChanged += OnDisplayModeChanged;
                ApplyDisplayMode(_viewModel.SelectedDisplayMode);
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
                    Width = 1280;
                    Height = 720;
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
        }

        private void OnTelemetryTick(object? sender, EventArgs e)
        {
            var viewportControl = this.FindControl<VeldridViewportControl>("ViewportControl");
            if (viewportControl != null && _viewModel != null)
            {
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
            }

            base.OnClosed(e);
        }
    }
}
