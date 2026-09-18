// src/Gordian.App/Graphics/VeldridViewportControl.cs
using System;
using System.Diagnostics;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform;
using Avalonia.Threading;
using Gordian.App.Services;
using Gordian.Core.Diagnostics;
using Gordian.Core.Graphics;
using Gordian.Core.Network;
using Gordian.Core.Resources;
using Gordian.Core.World;
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

        public static readonly DirectProperty<VeldridViewportControl, int> DrawCallsProperty =
            AvaloniaProperty.RegisterDirect<VeldridViewportControl, int>(
                nameof(DrawCalls),
                o => o.DrawCalls);

        public static readonly DirectProperty<VeldridViewportControl, int> VisibleMeshesProperty =
            AvaloniaProperty.RegisterDirect<VeldridViewportControl, int>(
                nameof(VisibleMeshes),
                o => o.VisibleMeshes);

        public static readonly DirectProperty<VeldridViewportControl, int> CulledMeshesProperty =
            AvaloniaProperty.RegisterDirect<VeldridViewportControl, int>(
                nameof(CulledMeshes),
                o => o.CulledMeshes);

        private int _drawCalls;
        public int DrawCalls
        {
            get => _drawCalls;
            private set => SetAndRaise(DrawCallsProperty, ref _drawCalls, value);
        }

        private int _visibleMeshes;
        public int VisibleMeshes
        {
            get => _visibleMeshes;
            private set => SetAndRaise(VisibleMeshesProperty, ref _visibleMeshes, value);
        }

        private int _culledMeshes;
        public int CulledMeshes
        {
            get => _culledMeshes;
            private set => SetAndRaise(CulledMeshesProperty, ref _culledMeshes, value);
        }

        public ViewportCamera Camera { get; set; } = new();
        public ZoneEnvironmentSettings Environment { get; set; } = ZoneEnvironmentSettings.CreateDay();
        public ZoneTerrainRenderer? TerrainRenderer => _renderer;

        private CharacterSession? _activeSession;
        public CharacterSession? ActiveSession
        {
            get => _activeSession;
            set
            {
                if (_activeSession != value)
                {
                    if (_activeSession != null)
                    {
                        _activeSession.World.ZoneChanged -= OnWorldZoneChanged;
                        if (_activeSession.Locomotion != null)
                        {
                            _activeSession.Locomotion.CameraUpdated -= OnLocomotionCameraUpdated;
                        }
                    }

                    _activeSession = value;
                    WorldState = value?.World;

                    if (_activeSession != null)
                    {
                        _activeSession.IsRendering3D = true;
                        _activeSession.World.ZoneChanged += OnWorldZoneChanged;
                        if (_activeSession.Locomotion != null)
                        {
                            Camera.Pitch = _activeSession.Locomotion.CameraPitch;
                            Camera.Yaw = _activeSession.Locomotion.CameraYaw;
                            Camera.Distance = _activeSession.Locomotion.CameraDistance;
                            Camera.Mode = _activeSession.Locomotion.Camera.Mode;
                            _activeSession.Locomotion.CameraUpdated += OnLocomotionCameraUpdated;
                        }
                        if (_activeSession.World.CurrentZoneId != 0)
                        {
                            OnWorldZoneChanged(_activeSession.World.CurrentZoneId);
                        }
                    }
                }
            }
        }

        private void OnLocomotionCameraUpdated(float pitch, float yaw, float distance)
        {
            Camera.Pitch = pitch;
            Camera.Yaw = yaw;
            Camera.Distance = distance;
        }

        private WorldState? _worldState;
        public WorldState? WorldState
        {
            get => _worldState;
            set
            {
                if (_worldState != value)
                {
                    if (_worldState != null)
                    {
                        _worldState.ZoneChanged -= OnWorldZoneChanged;
                    }
                    _worldState = value;
                    if (_worldState != null)
                    {
                        _worldState.ZoneChanged += OnWorldZoneChanged;
                        if (_worldState.CurrentZoneId != 0)
                        {
                            OnWorldZoneChanged(_worldState.CurrentZoneId);
                        }
                    }
                }
            }
        }

        private ResourceManager? _resourceManager;
        public ResourceManager? ResourceManager
        {
            get => _resourceManager ?? AppResourceManager.Instance;
            set => _resourceManager = value;
        }

        private ushort _loadedZoneId;
        private volatile int _pendingZoneLoad;
        private int _isZoneLoading;

        private void OnWorldZoneChanged(ushort zoneId)
        {
            if (zoneId == 0 || zoneId == _loadedZoneId) return;
            _pendingZoneLoad = zoneId;
        }

        private void CheckAndLoadPendingZone()
        {
            int targetZone = _pendingZoneLoad;
            if (targetZone == 0 || targetZone == _loadedZoneId) return;

            var rm = ResourceManager;
            if (rm == null) return;

            if (Interlocked.CompareExchange(ref _isZoneLoading, 1, 0) != 0)
            {
                // Already loading a zone in the background
                return;
            }

            _pendingZoneLoad = 0;
            ushort zoneToLoad = (ushort)targetZone;

            Task.Run(() =>
            {
                try
                {
                    GordianLog.Info("Graphics", $"Starting background load for Zone {zoneToLoad}...");
                    if (rm.TryLoadZone(zoneToLoad, out var zoneGeom, out var zoneTextures))
                    {
                        lock (_renderLock)
                        {
                            _renderer?.LoadZone(zoneGeom, zoneTextures);
                            _loadedZoneId = zoneToLoad;
                        }
                        GordianLog.Info("Graphics", $"Successfully loaded and streamed Zone {zoneToLoad} to GPU.");
                    }
                    else
                    {
                        // Re-queue so the render loop retries on the next frame
                        _pendingZoneLoad = zoneToLoad;
                        GordianLog.Warning("Graphics", $"ResourceManager could not find or load Zone {zoneToLoad}. Will retry.");
                    }
                }
                catch (Exception ex)
                {
                    _pendingZoneLoad = zoneToLoad;
                    GordianLog.Error("Graphics", $"Failed to load Zone {zoneToLoad}: {ex.Message}");
                }
                finally
                {
                    Interlocked.Exchange(ref _isZoneLoading, 0);
                }
            });
        }

        private readonly VeldridDeviceManager _deviceManager = new();
        private ZoneTerrainRenderer? _renderer;
        private IntPtr _childHwnd = IntPtr.Zero;
        private readonly object _renderLock = new();
        private CancellationTokenSource? _renderLoopCts;
        private Task? _renderTask;

        private Avalonia.Point? _lastMousePos;
        private bool _isRightMouseDown;

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
                        _renderer = new ZoneTerrainRenderer(_deviceManager.Device);

                        ushort initialZone = _activeSession?.World.CurrentZoneId ?? _worldState?.CurrentZoneId ?? 0;
                        if (initialZone != 0)
                        {
                            OnWorldZoneChanged(initialZone);
                        }
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
                _loadedZoneId = 0;
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

                CheckAndLoadPendingZone();

                float aspect = Math.Max(0.1f, (float)_deviceManager.CurrentWidth / Math.Max(1, _deviceManager.CurrentHeight));

                Vector3 playerPos = Vector3.Zero;
                bool hasPlayerPos = false;

                if (_activeSession != null)
                {
                    uint localServerId = _activeSession.LocalPlayer.ServerId != 0
                        ? _activeSession.LocalPlayer.ServerId
                        : _activeSession.CharacterId;

                    if (localServerId != 0 && _activeSession.World.TryGetByServerId(localServerId, out var localEnt) && localEnt != null)
                    {
                        playerPos = localEnt.Position;
                        hasPlayerPos = true;
                    }
                }
                
                if (!hasPlayerPos && WorldState != null)
                {
                    foreach (var ent in WorldState.Entities)
                    {
                        if (ent.Type == EntityType.Player)
                        {
                            playerPos = ent.Position;
                            hasPlayerPos = true;
                            break;
                        }
                    }
                }

                if (_activeSession?.Locomotion != null)
                {
                    Camera.Pitch = _activeSession.Locomotion.CameraPitch;
                    Camera.Yaw = _activeSession.Locomotion.CameraYaw;
                    Camera.Distance = _activeSession.Locomotion.CameraDistance;
                    Camera.Mode = _activeSession.Locomotion.Camera.Mode;
                }

                if (Camera.Mode != CameraMode.FreeCam)
                {
                    if (hasPlayerPos)
                    {
                        Camera.Update(playerPos, Camera.Pitch, Camera.Yaw, Camera.Distance, aspect);
                    }
                    else
                    {
                        Camera.AspectRatio = aspect;
                    }
                }
                else
                {
                    Camera.AspectRatio = aspect;
                }

                lock (_renderLock)
                {
                    if (_renderer != null && _deviceManager.IsInitialized)
                    {
                        try
                        {
                            _renderer.Render(
                                Camera,
                                Environment,
                                deltaSeconds,
                                _deviceManager.CurrentWidth,
                                _deviceManager.CurrentHeight,
                                WorldState?.Entities,
                                ResourceManager);
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

                    int dc = _renderer?.DrawCalls ?? 0;
                    int vis = _renderer?.VisibleMeshes ?? 0;
                    int culled = _renderer?.CulledMeshes ?? 0;

                    Dispatcher.UIThread.Post(() =>
                    {
                        CurrentFps = Math.Round(fps, 1);
                        FrameTimeMs = Math.Round(frameElapsedMs, 2);
                        DrawCalls = dc;
                        VisibleMeshes = vis;
                        CulledMeshes = culled;
                    });
                }

                // If VSync is off or running faster than display, yield slightly
                Thread.Sleep(1);
            }
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            var props = e.GetCurrentPoint(this).Properties;
            if (props.IsRightButtonPressed)
            {
                _isRightMouseDown = true;
                _lastMousePos = e.GetPosition(this);
                e.Pointer.Capture(this);
                e.Handled = true;
            }
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            base.OnPointerMoved(e);
            if (_isRightMouseDown && _lastMousePos.HasValue)
            {
                var cur = e.GetPosition(this);
                float dx = (float)(cur.X - _lastMousePos.Value.X);
                float dy = (float)(cur.Y - _lastMousePos.Value.Y);
                _lastMousePos = cur;

                Camera.Yaw += dx * 0.25f;
                Camera.Pitch -= dy * 0.25f;

                if (_activeSession?.Locomotion != null)
                {
                    _activeSession.Locomotion.CameraYaw = Camera.Yaw;
                    float minPitch = Camera.Mode == CameraMode.ThirdPersonOrbital ? -15.0f : -80.0f;
                    _activeSession.Locomotion.CameraPitch = Math.Clamp(Camera.Pitch, minPitch, 80.0f);
                }

                e.Handled = true;
            }
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            base.OnPointerReleased(e);
            if (_isRightMouseDown)
            {
                _isRightMouseDown = false;
                _lastMousePos = null;
                e.Pointer.Capture(null);
                e.Handled = true;
            }
        }

        protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
        {
            base.OnPointerWheelChanged(e);
            float zoomDelta = -(float)e.Delta.Y * 1.0f;
            Camera.Distance = Math.Clamp(Camera.Distance + zoomDelta, 0.5f, 35.0f);
            if (Camera.Distance <= 1.0f && zoomDelta < 0)
            {
                Camera.Mode = CameraMode.FirstPerson;
            }
            else if (Camera.Mode == CameraMode.FirstPerson && zoomDelta > 0)
            {
                Camera.Mode = CameraMode.ThirdPersonOrbital;
                Camera.Distance = 2.0f;
            }

            if (_activeSession?.Locomotion != null)
            {
                _activeSession.Locomotion.CameraDistance = Camera.Distance;
                _activeSession.Locomotion.CameraMode = Camera.Mode;
            }

            e.Handled = true;
        }
    }
}
