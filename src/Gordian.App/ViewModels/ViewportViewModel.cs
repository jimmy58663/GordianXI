// src/Gordian.App/ViewModels/ViewportViewModel.cs
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using Gordian.App.Common;
using Gordian.App.Graphics;
using Gordian.Core.Graphics;
using Gordian.Core.Network;

namespace Gordian.App.ViewModels
{
    /// <summary>
    /// ViewModel managing 3D viewport settings, graphics backend selection, display modes,
    /// character tabs, and real-time GPU telemetry.
    /// </summary>
    public sealed class ViewportViewModel : ViewModelBase
    {
        private GraphicsBackendPreference _selectedBackend = GraphicsBackendPreference.Auto;
        private ViewportDisplayMode _selectedDisplayMode = ViewportDisplayMode.BorderlessWindow;
        private ViewportTabStyle _selectedTabStyle = ViewportTabStyle.FloatingPill;
        private bool _autoLaunchOnConnect = true;
        private bool _isPipEnabled = false;
        private int _maxPipStreams = 5;
        private string _activeBackend = "Detecting...";
        private string _gpuName = "Detecting GPU...";
        private double _fps = 0.0;
        private double _frameTimeMs = 0.0;
        private string _resolution = "1920 x 1080";
        private bool _isVsyncEnabled = true;
        private int _backgroundFrameRate = ViewportRenderSettings.DefaultBackgroundFrameRate;
        private int _zoneCacheBudgetMb = ViewportRenderSettings.DefaultZoneCacheBudgetMb;
        private CameraMode _activeCameraMode = CameraMode.ThirdPersonOrbital;
        private int _drawCalls;
        private int _visibleMeshes;
        private int _culledMeshes;

        private ViewportCharacterTabViewModel? _activeTab;

        public event EventHandler<ViewportCharacterTabViewModel>? TabPoppedOut;
        public event EventHandler<ViewportDisplayMode>? DisplayModeChanged;
        public event EventHandler? ViewportWindowRequested;

        private readonly string _settingsPath;
        private readonly ViewportSettings _settings;
        private readonly bool _enableAutoSave;

        private void AutoSaveSettings()
        {
            if (!_enableAutoSave) return;

            try
            {
                _settings.SelectedBackend = _selectedBackend;
                _settings.SelectedDisplayMode = _selectedDisplayMode;
                _settings.SelectedTabStyle = _selectedTabStyle;
                _settings.AutoLaunchOnConnect = _autoLaunchOnConnect;
                _settings.IsPipEnabled = _isPipEnabled;
                _settings.MaxPipStreams = _maxPipStreams;
                _settings.IsVsyncEnabled = _isVsyncEnabled;
                _settings.BackgroundFrameRate = _backgroundFrameRate;
                _settings.ZoneCacheBudgetMb = _zoneCacheBudgetMb;
                _settings.SaveToFile(_settingsPath);
            }
            catch (Exception ex)
            {
                Gordian.Core.Diagnostics.GordianLog.Warn("VIEWPORT", $"Auto-save failed: {ex.Message}");
            }
        }

        public ICommand LaunchViewportCommand { get; }
        public ICommand ToggleCameraModeCommand { get; }
        public ICommand ToggleFreeCamCommand { get; }

        public ViewportViewModel(string? customSettingsPath = null, bool enableAutoSave = true)
        {
            _enableAutoSave = enableAutoSave;
            _settingsPath = customSettingsPath ?? ViewportSettings.GetDefaultSettingsPath();
            _settings = _enableAutoSave
                ? ViewportSettings.LoadOrCreate(_settingsPath)
                : ViewportSettings.LoadOrDefault(_settingsPath);

            _selectedBackend = _settings.SelectedBackend;
            _selectedDisplayMode = _settings.SelectedDisplayMode;
            _selectedTabStyle = _settings.SelectedTabStyle;
            _autoLaunchOnConnect = _settings.AutoLaunchOnConnect;
            _isPipEnabled = _settings.IsPipEnabled;
            _maxPipStreams = _settings.MaxPipStreams;
            _isVsyncEnabled = _settings.IsVsyncEnabled;
            _backgroundFrameRate = ViewportRenderSettings.ClampFrameRate(_settings.BackgroundFrameRate);
            _zoneCacheBudgetMb = Math.Max(0, _settings.ZoneCacheBudgetMb);
            // Every viewport window reads these each frame (#301, #322).
            ViewportRenderSettings.Apply(_settings);

            LaunchViewportCommand = new RelayCommand(() => ViewportWindowRequested?.Invoke(this, EventArgs.Empty));
            ToggleCameraModeCommand = new RelayCommand(() =>
            {
                ActiveCameraMode = ActiveCameraMode switch
                {
                    CameraMode.ThirdPersonOrbital => CameraMode.FirstPerson,
                    CameraMode.FirstPerson => CameraMode.FreeCam,
                    CameraMode.FreeCam => CameraMode.ThirdPersonOrbital,
                    _ => CameraMode.ThirdPersonOrbital
                };
            });
            ToggleFreeCamCommand = new RelayCommand(() =>
            {
                ActiveCameraMode = ActiveCameraMode == CameraMode.FreeCam
                    ? CameraMode.ThirdPersonOrbital
                    : CameraMode.FreeCam;
            });

            CharacterTabs.CollectionChanged += (_, _) =>
            {
                RaiseSwitcherVisibility();
                OnPropertyChanged(nameof(CharacterCountText));
            };
        }

        /// <summary>
        /// True for the main viewport window's view model, which decides the registry's primary rendering session; false
        /// for a popped-out window's, which shows one character and must not take that role from the main window.
        /// </summary>
        public bool IsPrimary { get; init; } = true;

        /// <summary>
        /// The active character's camera mode. Setting it switches that character's camera (the switcher's camera
        /// button and the free camera banner); <see cref="SyncFromActiveSession"/> follows changes made by key.
        /// </summary>
        public CameraMode ActiveCameraMode
        {
            get => _activeCameraMode;
            set
            {
                if (SetProperty(ref _activeCameraMode, value))
                {
                    OnPropertyChanged(nameof(IsFreeCamActive));
                    if (_activeTab?.Session.Locomotion is { } locomotion && locomotion.CameraMode != value)
                    {
                        locomotion.CameraMode = value;
                    }
                }
            }
        }

        /// <summary>
        /// Refreshes what the switchers show from the sessions (names, jobs, vitals, the camera mode); the viewport
        /// window calls it on its telemetry tick.
        /// </summary>
        public void SyncFromActiveSession()
        {
            foreach (var tab in CharacterTabs) tab.RefreshDisplay();
            if (_activeTab?.Session.Locomotion is { } locomotion && locomotion.CameraMode != _activeCameraMode)
            {
                _activeCameraMode = locomotion.CameraMode;
                OnPropertyChanged(nameof(ActiveCameraMode));
                OnPropertyChanged(nameof(IsFreeCamActive));
            }
        }

        /// <summary>
        /// Lets go of every key and mouse button the active character holds (the window lost the focus, so their
        /// releases will not arrive).
        /// </summary>
        public void ReleaseHeldInput() => _activeTab?.Session.InputState.Reset();

        public bool IsFreeCamActive => ActiveCameraMode == CameraMode.FreeCam;

        public int DrawCalls
        {
            get => _drawCalls;
            set => SetProperty(ref _drawCalls, value);
        }

        public int VisibleMeshes
        {
            get => _visibleMeshes;
            set => SetProperty(ref _visibleMeshes, value);
        }

        public int CulledMeshes
        {
            get => _culledMeshes;
            set => SetProperty(ref _culledMeshes, value);
        }

        public GraphicsBackendPreference SelectedBackend
        {
            get => _selectedBackend;
            set
            {
                if (SetProperty(ref _selectedBackend, value))
                {
                    AutoSaveSettings();
                }
            }
        }

        public ViewportDisplayMode SelectedDisplayMode
        {
            get => _selectedDisplayMode;
            set
            {
                if (SetProperty(ref _selectedDisplayMode, value))
                {
                    DisplayModeChanged?.Invoke(this, value);
                    AutoSaveSettings();
                }
            }
        }

        public ViewportTabStyle SelectedTabStyle
        {
            get => _selectedTabStyle;
            set
            {
                if (SetProperty(ref _selectedTabStyle, value))
                {
                    OnPropertyChanged(nameof(IsFloatingPill));
                    OnPropertyChanged(nameof(IsTopRibbon));
                    OnPropertyChanged(nameof(IsSideRail));
                    OnPropertyChanged(nameof(IsHotkeysOnly));
                    RaiseSwitcherVisibility();
                    AutoSaveSettings();
                }
            }
        }

        public bool IsFloatingPill => SelectedTabStyle == ViewportTabStyle.FloatingPill;
        public bool IsTopRibbon => SelectedTabStyle == ViewportTabStyle.TopRibbon;
        public bool IsSideRail => SelectedTabStyle == ViewportTabStyle.SideRail;
        public bool IsHotkeysOnly => SelectedTabStyle == ViewportTabStyle.HotkeysOnly;

        /// <summary>True while the window shows characters (not a lobby, not black on the way back to one).</summary>
        private bool ShowsCharacters => CharacterTabs.Count > 0 && _lobby == null && !_isReturningToLobby;

        /// <summary>Whether the floating pill is on screen: the style is chosen and a character is shown.</summary>
        public bool ShowFloatingPill => IsFloatingPill && ShowsCharacters;

        /// <summary>Whether the side rail is on screen: the style is chosen and a character is shown.</summary>
        public bool ShowSideRail => IsSideRail && ShowsCharacters;

        /// <summary>
        /// Whether the top ribbon is on screen: whenever the style is chosen, also over a lobby, since it carries the
        /// window's display mode, minimise and close buttons.
        /// </summary>
        public bool ShowTopRibbon => IsTopRibbon;

        /// <summary>Whether the PiP deck is on screen: enabled and at least one background character to show.</summary>
        public bool ShowPipDeck => IsPipEnabled && ShowsCharacters && PipThumbnails.Count > 0;

        private void RaiseSwitcherVisibility()
        {
            OnPropertyChanged(nameof(ShowFloatingPill));
            OnPropertyChanged(nameof(ShowSideRail));
            OnPropertyChanged(nameof(ShowTopRibbon));
            OnPropertyChanged(nameof(ShowPipDeck));
        }

        public bool IsPipEnabled
        {
            get => _isPipEnabled;
            set
            {
                if (SetProperty(ref _isPipEnabled, value))
                {
                    RaiseSwitcherVisibility();
                    AutoSaveSettings();
                }
            }
        }

        public int MaxPipStreams
        {
            get => _maxPipStreams;
            set
            {
                if (SetProperty(ref _maxPipStreams, value))
                {
                    RefreshPipThumbnails();
                    AutoSaveSettings();
                }
            }
        }

        public bool AutoLaunchOnConnect
        {
            get => _autoLaunchOnConnect;
            set
            {
                if (SetProperty(ref _autoLaunchOnConnect, value))
                {
                    AutoSaveSettings();
                }
            }
        }

        public string ActiveBackend
        {
            get => _activeBackend;
            set => SetProperty(ref _activeBackend, value);
        }

        public string GpuName
        {
            get => _gpuName;
            set => SetProperty(ref _gpuName, value);
        }

        public double Fps
        {
            get => _fps;
            set => SetProperty(ref _fps, value);
        }

        public double FrameTimeMs
        {
            get => _frameTimeMs;
            set => SetProperty(ref _frameTimeMs, value);
        }

        public string Resolution
        {
            get => _resolution;
            set => SetProperty(ref _resolution, value);
        }

        public bool IsVsyncEnabled
        {
            get => _isVsyncEnabled;
            set
            {
                if (SetProperty(ref _isVsyncEnabled, value))
                {
                    ViewportRenderSettings.VsyncEnabled = value;
                    AutoSaveSettings();
                }
            }
        }

        /// <summary>Frames per second of viewport windows other than the focused one (#301); applies at once to every window.</summary>
        public int BackgroundFrameRate
        {
            get => _backgroundFrameRate;
            set
            {
                if (SetProperty(ref _backgroundFrameRate, ViewportRenderSettings.ClampFrameRate(value)))
                {
                    ViewportRenderSettings.BackgroundFrameRate = _backgroundFrameRate;
                    AutoSaveSettings();
                }
            }
        }

        /// <summary>The background frame rates offered in Settings.</summary>
        public ObservableCollection<int> AvailableBackgroundFrameRates { get; } = new() { 10, 15, 20, 30, 60 };

        /// <summary>GPU memory (MB) for zones kept loaded for characters in other zones (#322); 0 keeps only zones on screen.</summary>
        public int ZoneCacheBudgetMb
        {
            get => _zoneCacheBudgetMb;
            set
            {
                if (SetProperty(ref _zoneCacheBudgetMb, Math.Max(0, value)))
                {
                    ViewportRenderSettings.ZoneCacheBudgetMb = _zoneCacheBudgetMb;
                    AutoSaveSettings();
                }
            }
        }

        /// <summary>The zone cache budgets offered in Settings (MB).</summary>
        public ObservableCollection<int> AvailableZoneCacheBudgets { get; } = new() { 0, 512, 1024, 1536, 2048, 4096 };

        public ObservableCollection<GraphicsBackendPreference> AvailableBackends { get; } = new()
        {
            GraphicsBackendPreference.Auto,
            GraphicsBackendPreference.Direct3D11,
            GraphicsBackendPreference.Vulkan,
            GraphicsBackendPreference.OpenGL
        };

        public ObservableCollection<ViewportDisplayMode> AvailableDisplayModes { get; } = new()
        {
            ViewportDisplayMode.BorderlessWindow,
            ViewportDisplayMode.Windowed,
            ViewportDisplayMode.Fullscreen
        };

        public ObservableCollection<ViewportTabStyle> AvailableTabStyles { get; } = new()
        {
            ViewportTabStyle.FloatingPill,
            ViewportTabStyle.TopRibbon,
            ViewportTabStyle.SideRail,
            ViewportTabStyle.HotkeysOnly
        };

        public ObservableCollection<ViewportCharacterTabViewModel> CharacterTabs { get; } = new();

        private Gordian.Core.Ui.Lobby.LobbyController? _lobby;

        /// <summary>
        /// The character lobby on show, if any: while set, the viewport draws the lobby and sends it the keyboard and mouse
        /// instead of the active tab's session (one lobby at a time, as the retail client).
        /// </summary>
        public Gordian.Core.Ui.Lobby.LobbyController? Lobby
        {
            get => _lobby;
            set
            {
                if (SetProperty(ref _lobby, value))
                {
                    OnPropertyChanged(nameof(IsLobbyOpen));
                    RaiseSwitcherVisibility();
                }
            }
        }

        public bool IsLobbyOpen => _lobby != null;

        private bool _isReturningToLobby;

        /// <summary>
        /// A Log Out is on its way back to the character select screen (#32): the window stays open with no tab and no
        /// lobby, and the viewport shows black until the lobby appears.
        /// </summary>
        public bool IsReturningToLobby
        {
            get => _isReturningToLobby;
            set
            {
                if (SetProperty(ref _isReturningToLobby, value)) RaiseSwitcherVisibility();
            }
        }

        public ObservableCollection<ViewportCharacterTabViewModel> PipThumbnails { get; } = new();

        public ViewportCharacterTabViewModel? ActiveTab
        {
            get => _activeTab;
            set
            {
                var previous = _activeTab;
                if (SetProperty(ref _activeTab, value))
                {
                    foreach (var tab in CharacterTabs)
                    {
                        tab.IsActive = (tab == value);
                    }

                    // Keys held for the character switched away from (Ctrl of Ctrl+Tab, a movement key) would be
                    // released into the new one; let them go so the old character does not keep running.
                    if (previous != null && (value == null || previous.Session != value.Session))
                    {
                        previous.Session.InputState.Reset();
                    }

                    if (value != null && IsPrimary)
                    {
                        SessionRegistry.Default.SetPrimaryRenderingSession(value.Session);
                    }

                    if (value?.Session.Locomotion is { } locomotion && locomotion.CameraMode != _activeCameraMode)
                    {
                        _activeCameraMode = locomotion.CameraMode;
                        OnPropertyChanged(nameof(ActiveCameraMode));
                        OnPropertyChanged(nameof(IsFreeCamActive));
                    }

                    RefreshPipThumbnails();
                }
            }
        }

        /// <summary>
        /// The background characters shown as PiP thumbnails: every tab but the active one and those popped out into
        /// their own window, up to <see cref="MaxPipStreams"/>.
        /// </summary>
        public void RefreshPipThumbnails()
        {
            PipThumbnails.Clear();
            foreach (var tab in CharacterTabs.Where(t => t != ActiveTab && !t.IsPoppedOut).Take(MaxPipStreams))
            {
                PipThumbnails.Add(tab);
            }
            OnPropertyChanged(nameof(ShowPipDeck));
        }

        public ViewportCharacterTabViewModel AddSession(CharacterSession session)
        {
            ArgumentNullException.ThrowIfNull(session);

            var existing = CharacterTabs.FirstOrDefault(t => t.SessionId == session.SessionId);
            if (existing != null)
            {
                existing.RefreshDisplay();
                return existing;
            }

            var newTab = new ViewportCharacterTabViewModel(
                session,
                onSelect: SelectTab,
                onPopOut: tab => TabPoppedOut?.Invoke(this, tab)
            );

            CharacterTabs.Add(newTab);

            if (ActiveTab == null)
            {
                ActiveTab = newTab;
            }
            else
            {
                RefreshPipThumbnails();
            }

            return newTab;
        }

        public void RemoveSession(CharacterSession session)
        {
            if (session == null) return;

            var existing = CharacterTabs.FirstOrDefault(t => t.SessionId == session.SessionId);
            if (existing != null)
            {
                bool wasActive = (ActiveTab == existing);
                CharacterTabs.Remove(existing);

                if (wasActive)
                {
                    ActiveTab = CharacterTabs.FirstOrDefault(t => !t.IsPoppedOut) ?? CharacterTabs.FirstOrDefault();
                }
                else
                {
                    RefreshPipThumbnails();
                }
            }
        }

        /// <summary>
        /// For a pop-out window's view model: the main window's view model, whose tab order Ctrl+Tab follows from the
        /// pop-out (so it can move on to the main window or another pop-out). Null for the main window.
        /// </summary>
        public ViewportViewModel? SwitchTarget { get; set; }

        /// <summary>
        /// Raised when a switcher click or Ctrl+Tab chose this window's character, so the window takes the focus (the
        /// switchers are popups that never activate it, and the gamepad follows the focused window).
        /// </summary>
        public event EventHandler? WindowActivationRequested;

        /// <summary>
        /// Chooses a character from a switcher or Ctrl+Tab: one popped out into its own window brings that window to the
        /// front (<see cref="TabPoppedOut"/>, which the window manager answers by activating it); any other becomes the
        /// active character here and this window takes the focus.
        /// </summary>
        public void SelectTab(ViewportCharacterTabViewModel tab)
        {
            ArgumentNullException.ThrowIfNull(tab);
            if (tab.IsPoppedOut)
            {
                TabPoppedOut?.Invoke(this, tab);
                return;
            }
            ActiveTab = tab;
            WindowActivationRequested?.Invoke(this, EventArgs.Empty);
            ViewportWindowRequested?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Ctrl+Tab: the next character in tab order, popped-out ones included (their window is focused).</summary>
        public ViewportCharacterTabViewModel? CycleNextCharacter() => (SwitchTarget ?? this).CycleFrom(ActiveTab?.Session, +1);

        /// <summary>Ctrl+Shift+Tab: the previous character in tab order, popped-out ones included.</summary>
        public ViewportCharacterTabViewModel? CyclePreviousCharacter() => (SwitchTarget ?? this).CycleFrom(ActiveTab?.Session, -1);

        /// <summary>
        /// Chooses the character <paramref name="step"/> tabs on from <paramref name="from"/> (the active tab when it is
        /// not one of ours), wrapping, with <see cref="SelectTab"/>.
        /// </summary>
        public ViewportCharacterTabViewModel? CycleFrom(CharacterSession? from, int step)
        {
            int count = CharacterTabs.Count;
            if (count == 0) return null;
            int start = -1;
            for (int i = 0; i < count && from != null; i++)
            {
                if (ReferenceEquals(CharacterTabs[i].Session, from)) start = i;
            }
            if (start < 0) start = ActiveTab != null ? CharacterTabs.IndexOf(ActiveTab) : 0;
            if (count == 1 && ReferenceEquals(CharacterTabs[0].Session, from)) return null;

            var next = CharacterTabs[((start + step) % count + count) % count];
            SelectTab(next);
            return next;
        }

        /// <summary>
        /// After the active tab was popped out into its own window, shows the next character that is not popped out
        /// (the popped-out one stays when every character is popped out).
        /// </summary>
        public void MoveOffPoppedOutTab()
        {
            if (ActiveTab is { IsPoppedOut: true })
            {
                int at = CharacterTabs.IndexOf(ActiveTab);
                for (int i = 1; i < CharacterTabs.Count; i++)
                {
                    var candidate = CharacterTabs[(at + i) % CharacterTabs.Count];
                    if (!candidate.IsPoppedOut)
                    {
                        ActiveTab = candidate;
                        break;
                    }
                }
            }
            RefreshPipThumbnails();
        }

        /// <summary>The collapsed floating pill's hint: how many characters the switcher holds.</summary>
        public string CharacterCountText => CharacterTabs.Count == 1 ? "1 char" : $"{CharacterTabs.Count} chars";
    }
}
