// src/Gordian.App/Graphics/VeldridViewportControl.cs
using System;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.Versioning;
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
using Gordian.Core.Resources.Models;
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
        private ZoneGeometry? _currentZoneGeom;
        private float _lastVanaHour = -1f;
        private string? _lastWeatherId;
        private int _timeOfDayCycleIndex = 0;

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
                            _currentZoneGeom = zoneGeom;

                            if (zoneGeom?.EnvironmentData != null)
                            {
                                float vanaHour = VanaTime.GetTimeOfDayHours(DateTime.UtcNow);
                                string weather = _activeSession?.World.WeatherId ?? WorldState?.WeatherId ?? Environment.WeatherId ?? "fine";
                                _lastVanaHour = vanaHour;
                                _lastWeatherId = weather;
                                var keyframe = zoneGeom.EnvironmentData.Interpolate(vanaHour, weather);
                                if (keyframe != null)
                                {
                                    Environment.ApplyKeyframe(keyframe);
                                    Environment.SetTimeOfDay(vanaHour);
                                    Environment.WeatherId = weather;
                                    _renderer?.SkyDomeRenderer?.UpdateDome(Environment);
                                    GordianLog.Info("Graphics", $"Applied Zone {zoneToLoad} 0x2F environment lighting and sky dome slices (weather={weather}, hour={vanaHour:F1}).");
                                }
                            }
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

        protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
        {
            if (OperatingSystem.IsWindows())
            {
                double scale = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1.0;
                int pixelW = Math.Max(1, (int)(Bounds.Width * scale));
                int pixelH = Math.Max(1, (int)(Bounds.Height * scale));

                _childHwnd = Win32ChildWindowHelper.CreateChildWindow(parent.Handle, pixelW, pixelH);
                Win32ChildWindowHelper.SetRawMouseHandler(_childHwnd, OnRawMouseEvent);
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
                _currentZoneGeom = null;
                _lastVanaHour = -1f;
                _lastWeatherId = null;
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

        /// <summary>
        /// Raised for mouse buttons pressed while the pointer is over this control's rendering
        /// surface. See <see cref="Win32ChildWindowHelper.CreateChildWindow"/> for why this exists
        /// instead of the normal Avalonia PointerPressed routed event.
        /// </summary>
        public event Action<Avalonia.Input.MouseButton>? RawMouseButtonDown;

        /// <summary>Raised for mouse buttons released while over this control's rendering surface.</summary>
        public event Action<Avalonia.Input.MouseButton>? RawMouseButtonUp;

        /// <summary>Raised on mouse move while over this control's rendering surface, in raw child-local pixels.</summary>
        public event Action<double, double>? RawMouseMoved;

        [SupportedOSPlatform("windows")]
        private void OnRawMouseEvent(Win32ChildWindowHelper.RawMouseEvent e)
        {
            RawMouseMoved?.Invoke(e.X, e.Y);

            if (e.ButtonDown.HasValue)
            {
                RawMouseButtonDown?.Invoke(ToAvaloniaButton(e.ButtonDown.Value));
            }

            if (e.ButtonUp.HasValue)
            {
                RawMouseButtonUp?.Invoke(ToAvaloniaButton(e.ButtonUp.Value));
            }
        }

        private static Avalonia.Input.MouseButton ToAvaloniaButton(RawMouseButton button) => button switch
        {
            RawMouseButton.Left => Avalonia.Input.MouseButton.Left,
            RawMouseButton.Right => Avalonia.Input.MouseButton.Right,
            RawMouseButton.Middle => Avalonia.Input.MouseButton.Middle,
            _ => Avalonia.Input.MouseButton.None
        };

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
                uint localPlayerServerId = 0;
                bool isLocalPlayerEngaged = false;

                if (_activeSession != null)
                {
                    localPlayerServerId = _activeSession.LocalPlayer.ServerId != 0
                        ? _activeSession.LocalPlayer.ServerId
                        : _activeSession.CharacterId;
                    isLocalPlayerEngaged = _activeSession.Combat.IsEngaged;

                    if (localPlayerServerId != 0 && _activeSession.World.TryGetByServerId(localPlayerServerId, out var localEnt) && localEnt != null)
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

                Vector3? displayPlayerPos = hasPlayerPos
                    ? new Vector3(-playerPos.X, -playerPos.Y, playerPos.Z)
                    : null;

                if (Camera.Mode != CameraMode.FreeCam)
                {
                    if (displayPlayerPos.HasValue)
                    {
                        Camera.Update(displayPlayerPos.Value, Camera.Pitch, Camera.Yaw, Camera.Distance, aspect);
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
                            // Dynamic Vana'diel time and weather evaluation for 0x2F environment lighting and sky dome
                            // Only advances automatically when in live mode (_timeOfDayCycleIndex == 0); manual F10 presets are preserved.
                            if (_currentZoneGeom?.EnvironmentData != null && _timeOfDayCycleIndex == 0)
                            {
                                float vanaHour = VanaTime.GetTimeOfDayHours(DateTime.UtcNow);
                                string activeWeather = _activeSession?.World.WeatherId ?? WorldState?.WeatherId ?? Environment.WeatherId ?? "fine";
                                if (Math.Abs(vanaHour - _lastVanaHour) >= 0.05f || activeWeather != _lastWeatherId)
                                {
                                    _lastVanaHour = vanaHour;
                                    _lastWeatherId = activeWeather;
                                    var kf = _currentZoneGeom.EnvironmentData.Interpolate(vanaHour, activeWeather);
                                    if (kf != null)
                                    {
                                        Environment.ApplyKeyframe(kf);
                                        Environment.SetTimeOfDay(vanaHour);
                                        Environment.WeatherId = activeWeather;
                                        _renderer.SkyDomeRenderer?.UpdateDome(Environment);
                                    }
                                }
                            }

                            // Tier 1: 3D Scene Pass (Terrain, Sky Dome, Cutout Foliage, Entities, Blend Water)
                            _renderer.Render(
                                Camera,
                                Environment,
                                deltaSeconds,
                                _deviceManager.CurrentWidth,
                                _deviceManager.CurrentHeight,
                                WorldState?.Entities,
                                ResourceManager,
                                localPlayerServerId,
                                isLocalPlayerEngaged,
                                displayPlayerPos,
                                present: false);

                            // Tier 2: Stock FFXI 2D UI Pass (gated by StockUiVisibilityState)
                            RenderTier2_StockUi();

                            // Tier 3: ImGui Overlays & Addons Pass
                            RenderTier3_ImGuiOverlays();

                            // Final composite present
                            _deviceManager.Device?.SwapBuffers();
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

                    GordianLog.Debug("Graphics", $"RenderStats: DrawCalls={dc}, VisibleMeshes={vis}, CulledMeshes={culled}, " +
                        $"OceanWaterActive={_renderer?.IsOceanWaterPlaneActive ?? false}, " +
                        $"CameraPos={Camera.Position:F1}, CameraTarget={Camera.Target:F1}, PlayerPos={playerPos:F1}, hasPlayerPos={hasPlayerPos}, " +
                        $"ZoneSubmeshCount={_renderer?.LoadedZoneSubmeshCount ?? -1}, " +
                        $"FirstSubmeshBounds=[{_renderer?.FirstSubmeshMinBounds:F1} .. {_renderer?.FirstSubmeshMaxBounds:F1}]");

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

        /// <summary>
        /// Tier 2: Stock FFXI 2D UI render pass (orthographic HUD projection).
        /// Reserved hook for Phase 5E Tier 2.
        /// </summary>
        private void RenderTier2_StockUi()
        {
            // Future Phase 5E Tier 2 implementation: Blue marble menus, finger cursor, vitals gauges, status icons
        }

        /// <summary>
        /// Tier 3: ImGui Overlays and Addons render pass.
        /// Reserved hook for Phase 5E Tier 3.
        /// </summary>
        private void RenderTier3_ImGuiOverlays()
        {
            // Future Phase 5E Tier 3 implementation: Translucent HUD overlays, performance graphs, addon canvases
        }

        /// <summary>
        /// Sets a specific time-of-day environment preset (day, dusk, night, overcast, live) or evaluates from 0x2F keyframes.
        /// </summary>
        public void SetTimeOfDayPreset(string preset)
        {
            string currentWeather = Environment.WeatherId ?? "fine";
            string p = (preset ?? string.Empty).Trim().ToLowerInvariant();

            if (p == "live")
            {
                _timeOfDayCycleIndex = 0;
                if (_currentZoneGeom?.EnvironmentData != null)
                {
                    float vanaHour = VanaTime.GetTimeOfDayHours(DateTime.UtcNow);
                    var kf = _currentZoneGeom.EnvironmentData.Interpolate(vanaHour, currentWeather);
                    if (kf != null)
                    {
                        Environment.ApplyKeyframe(kf);
                        Environment.SetTimeOfDay(vanaHour);
                        Environment.WeatherId = currentWeather;
                        _lastVanaHour = vanaHour;
                        _lastWeatherId = currentWeather;
                        _renderer?.SkyDomeRenderer?.UpdateDome(Environment);
                        GordianLog.Info("Graphics", $"Returned to live Vana'diel time ({vanaHour:F1}, weather={currentWeather}).");
                        return;
                    }
                }
                Environment = ZoneEnvironmentSettings.CreateDay();
                Environment.WeatherId = currentWeather;
                _renderer?.SkyDomeRenderer?.UpdateDome(Environment);
                return;
            }

            float targetHour = p switch
            {
                "day" => 12.0f,
                "dusk" or "sunset" => 18.0f,
                "night" or "midnight" => 0.0f,
                "overcast" or "cloudy" => 12.0f,
                _ => float.TryParse(p, out float h) ? h : 12.0f
            };
            string targetWeather = (p is "overcast" or "cloudy") ? "clod" : currentWeather;

            if (_currentZoneGeom?.EnvironmentData != null)
            {
                var kf = _currentZoneGeom.EnvironmentData.Interpolate(targetHour, targetWeather);
                if (kf != null)
                {
                    Environment.ApplyKeyframe(kf);
                    Environment.SetTimeOfDay(targetHour);
                    Environment.WeatherId = targetWeather;
                    _lastVanaHour = targetHour;
                    _lastWeatherId = targetWeather;
                    _renderer?.SkyDomeRenderer?.UpdateDome(Environment);
                    GordianLog.Info("Graphics", $"Applied 0x2F environment for preset '{p}' (hour={targetHour:F1}, weather={targetWeather}).");
                    return;
                }
            }

            // Fallback presets if the zone has no 0x2F environment data
            Environment = p switch
            {
                "day" => ZoneEnvironmentSettings.CreateDay(),
                "dusk" or "sunset" => ZoneEnvironmentSettings.CreateDusk(),
                "night" or "midnight" => ZoneEnvironmentSettings.CreateNight(),
                "overcast" or "cloudy" => ZoneEnvironmentSettings.CreateOvercast(),
                _ => ZoneEnvironmentSettings.CreateDay()
            };
            Environment.WeatherId = targetWeather;
            _lastWeatherId = targetWeather;
            _renderer?.SkyDomeRenderer?.UpdateDome(Environment);
            GordianLog.Info("Graphics", $"Switched fallback time of day to {preset} (weather={targetWeather}).");
        }

        /// <summary>
        /// Cycles through time-of-day presets (Day -> Dusk -> Night -> Overcast -> Live).
        /// </summary>
        public void CycleTimeOfDay()
        {
            _timeOfDayCycleIndex = (_timeOfDayCycleIndex + 1) % 5;
            switch (_timeOfDayCycleIndex)
            {
                case 1:
                    SetTimeOfDayPreset("day");
                    break;
                case 2:
                    SetTimeOfDayPreset("dusk");
                    break;
                case 3:
                    SetTimeOfDayPreset("night");
                    break;
                case 4:
                    SetTimeOfDayPreset("overcast");
                    break;
                default:
                    SetTimeOfDayPreset("live");
                    break;
            }
        }

        /// <summary>
        /// Cycles through active weather presets (fine [Clear] -> suny [Sunshine] -> clod [Clouds] -> mist [Fog]).
        /// </summary>
        public void CycleWeather()
        {
            string current = Environment.WeatherId ?? "fine";
            string next = current switch
            {
                "fine" => "suny",
                "suny" => "clod",
                "clod" => "mist",
                _ => "fine"
            };
            SetWeather(next);
        }

        /// <summary>
        /// Sets a specific weather preset ("fine", "suny", "clod", "mist") and updates environment lighting and sky layers.
        /// </summary>
        public void SetWeather(string weatherId)
        {
            Environment.WeatherId = weatherId;
            if (WorldState != null)
            {
                WorldState.WeatherId = weatherId;
            }
            _lastWeatherId = weatherId;
            if (_renderer?.LoadedZone?.EnvironmentData != null)
            {
                float vanaHour = VanaTime.GetTimeOfDayHours(DateTime.UtcNow);
                var kf = _renderer.LoadedZone.EnvironmentData.Interpolate(vanaHour, weatherId);
                if (kf != null)
                {
                    Environment.ApplyKeyframe(kf);
                    Environment.SetTimeOfDay(vanaHour);
                    _renderer?.SkyDomeRenderer?.UpdateDome(Environment);
                }
            }
            GordianLog.Info("Graphics", $"Switched weather to {weatherId}.");
        }

        /// <summary>
        /// Toggles distance fog on/off for the active 3D environment.
        /// </summary>
        public void ToggleFog()
        {
            Environment.FogEnabled = !Environment.FogEnabled;
            GordianLog.Info("Graphics", $"Distance fog {(Environment.FogEnabled ? "enabled" : "disabled")}.");
        }

        /// <summary>
        /// Toggles base sea-level ocean water plane rendering on/off.
        /// </summary>
        public void ToggleOceanWater()
        {
            if (_renderer != null)
            {
                _renderer.EnableOceanWaterPlane = !_renderer.EnableOceanWaterPlane;
                GordianLog.Info("Graphics", $"Ocean water plane {(_renderer.EnableOceanWaterPlane ? "enabled" : "disabled")}.");
            }
        }

        // Right-click-drag camera look and wheel zoom are NOT handled here. On Windows this
        // control's rendering surface is a real native Win32 child window (see
        // Win32ChildWindowHelper), so the OS delivers its mouse messages directly to that child
        // HWND rather than through Avalonia's routed-event tree - Avalonia InputElement pointer
        // overrides on this control never actually fire. Camera look/zoom is instead driven by
        // ViewportWindow's own pointer handlers feeding the shared InputState bus, consumed by
        // PlayerLocomotionController - the same architecture keyboard movement already uses.
    }
}
