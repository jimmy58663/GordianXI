// src/Gordian.App/Graphics/VeldridViewportControl.cs
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;
using Gordian.Core.Diagnostics;
using Veldrid;

namespace Gordian.App.Graphics
{
    /// <summary>
    /// Avalonia control embedding a native hardware-accelerated Veldrid 3D viewport surface.
    /// Supports Direct3D 11 (Windows), Vulkan (Linux/Windows), and Metal (macOS).
    /// </summary>
    public class VeldridViewportControl : NativeControlHost
    {
        public static readonly StyledProperty<GraphicsBackendPreference> BackendPreferenceProperty =
            AvaloniaProperty.Register<VeldridViewportControl, GraphicsBackendPreference>(
                nameof(BackendPreference),
                GraphicsBackendPreference.Auto);

        public GraphicsBackendPreference BackendPreference
        {
            get => GetValue(BackendPreferenceProperty);
            set => SetValue(BackendPreferenceProperty, value);
        }

        public static readonly DirectProperty<VeldridViewportControl, string> ActiveBackendNameProperty =
            AvaloniaProperty.RegisterDirect<VeldridViewportControl, string>(
                nameof(ActiveBackendName),
                o => o.ActiveBackendName);

        public static readonly DirectProperty<VeldridViewportControl, string> GpuDeviceNameProperty =
            AvaloniaProperty.RegisterDirect<VeldridViewportControl, string>(
                nameof(GpuDeviceName),
                o => o.GpuDeviceName);

        public static readonly DirectProperty<VeldridViewportControl, double> CurrentFpsProperty =
            AvaloniaProperty.RegisterDirect<VeldridViewportControl, double>(
                nameof(CurrentFps),
                o => o.CurrentFps);

        public static readonly DirectProperty<VeldridViewportControl, double> FrameTimeMsProperty =
            AvaloniaProperty.RegisterDirect<VeldridViewportControl, double>(
                nameof(FrameTimeMs),
                o => o.FrameTimeMs);

        private string _activeBackendName = "None";
        public string ActiveBackendName
        {
            get => _activeBackendName;
            private set => SetAndRaise(ActiveBackendNameProperty, ref _activeBackendName, value);
        }

        private string _gpuDeviceName = "None";
        public string GpuDeviceName
        {
            get => _gpuDeviceName;
            private set => SetAndRaise(GpuDeviceNameProperty, ref _gpuDeviceName, value);
        }

        private double _currentFps;
        public double CurrentFps
        {
            get => _currentFps;
            private set => SetAndRaise(CurrentFpsProperty, ref _currentFps, value);
        }

        private double _frameTimeMs;
        public double FrameTimeMs
        {
            get => _frameTimeMs;
            private set => SetAndRaise(FrameTimeMsProperty, ref _frameTimeMs, value);
        }

        private readonly VeldridDeviceManager _deviceManager = new();
        private TestCubeRenderer? _renderer;
        private IntPtr _childHwnd = IntPtr.Zero;
        private readonly object _renderLock = new();
        private CancellationTokenSource? _renderLoopCts;
        private Task? _renderTask;

        protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
        {
            if (OperatingSystem.IsWindows())
            {
                double scale = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1.0;
                int pixelW = Math.Max(1, (int)(Bounds.Width * scale));
                int pixelH = Math.Max(1, (int)(Bounds.Height * scale));

                _childHwnd = Win32ChildWindowHelper.CreateChildWindow(parent.Handle, pixelW, pixelH);
                var swapchainSource = SwapchainSource.CreateWin32(_childHwnd, IntPtr.Zero);

                lock (_renderLock)
                {
                    _deviceManager.Initialize(
                        swapchainSource,
                        (uint)pixelW,
                        (uint)pixelH,
                        BackendPreference,
                        vsync: true);

                    if (_deviceManager.Device != null)
                    {
                        ActiveBackendName = _deviceManager.ActiveBackend.ToString();
                        GpuDeviceName = _deviceManager.DeviceName;
                        _renderer = new TestCubeRenderer(_deviceManager.Device);
                    }
                }

                StartRenderLoop();
                return new PlatformHandle(_childHwnd, "HWND");
            }

            GordianLog.Warning("Graphics", "Non-Windows native viewport creation currently defaults to base host.");
            return base.CreateNativeControlCore(parent);
        }

        protected override void DestroyNativeControlCore(IPlatformHandle control)
        {
            StopRenderLoop();

            lock (_renderLock)
            {
                _renderer?.Dispose();
                _renderer = null;

                _deviceManager.Dispose();

                if (OperatingSystem.IsWindows() && _childHwnd != IntPtr.Zero)
                {
                    Win32ChildWindowHelper.DestroyChildWindow(_childHwnd);
                    _childHwnd = IntPtr.Zero;
                }
            }

            base.DestroyNativeControlCore(control);
        }

        protected override void OnSizeChanged(SizeChangedEventArgs e)
        {
            base.OnSizeChanged(e);

            if (_childHwnd == IntPtr.Zero || _deviceManager.Device == null)
            {
                return;
            }

            double scale = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1.0;
            uint pixelW = (uint)Math.Max(1, (int)(e.NewSize.Width * scale));
            uint pixelH = (uint)Math.Max(1, (int)(e.NewSize.Height * scale));

            lock (_renderLock)
            {
                if (OperatingSystem.IsWindows())
                {
                    Win32ChildWindowHelper.ResizeChildWindow(_childHwnd, (int)pixelW, (int)pixelH);
                }
                _deviceManager.Resize(pixelW, pixelH);
            }
        }

        private void StartRenderLoop()
        {
            StopRenderLoop();

            _renderLoopCts = new CancellationTokenSource();
            var token = _renderLoopCts.Token;

            _renderTask = Task.Run(() => RenderLoopWorker(token), token);
        }

        private void StopRenderLoop()
        {
            if (_renderLoopCts != null)
            {
                _renderLoopCts.Cancel();
                try
                {
                    _renderTask?.Wait(200);
                }
                catch
                {
                    // Ignore cancellation wait exceptions
                }
                _renderLoopCts.Dispose();
                _renderLoopCts = null;
                _renderTask = null;
            }
        }

        private void RenderLoopWorker(CancellationToken cancellationToken)
        {
            var sw = Stopwatch.StartNew();
            long lastTicks = sw.ElapsedTicks;
            long fpsLastTicks = lastTicks;
            int frameCount = 0;

            while (!cancellationToken.IsCancellationRequested)
            {
                long currentTicks = sw.ElapsedTicks;
                float deltaSeconds = (float)(currentTicks - lastTicks) / Stopwatch.Frequency;
                lastTicks = currentTicks;

                var frameStart = Stopwatch.GetTimestamp();

                lock (_renderLock)
                {
                    if (_renderer != null && _deviceManager.IsInitialized)
                    {
                        try
                        {
                            _renderer.Render(deltaSeconds, _deviceManager.CurrentWidth, _deviceManager.CurrentHeight);
                        }
                        catch (Exception ex)
                        {
                            GordianLog.Warning("Graphics", $"Frame render error: {ex.Message}");
                        }
                    }
                }

                var frameEnd = Stopwatch.GetTimestamp();
                double frameElapsedMs = (double)(frameEnd - frameStart) / Stopwatch.Frequency * 1000.0;

                frameCount++;
                if ((currentTicks - fpsLastTicks) >= Stopwatch.Frequency)
                {
                    double fps = (double)frameCount * Stopwatch.Frequency / (currentTicks - fpsLastTicks);
                    frameCount = 0;
                    fpsLastTicks = currentTicks;

                    Dispatcher.UIThread.Post(() =>
                    {
                        CurrentFps = Math.Round(fps, 1);
                        FrameTimeMs = Math.Round(frameElapsedMs, 2);
                    });
                }

                // If VSync is off or running faster than display, yield slightly
                Thread.Sleep(1);
            }
        }
    }
}
