// src/Gordian.App/ViewModels/ViewportViewModel.cs
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using Gordian.App.Common;
using Gordian.App.Graphics;
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

        private ViewportCharacterTabViewModel? _activeTab;

        public event EventHandler<ViewportCharacterTabViewModel>? TabPoppedOut;
        public event EventHandler<ViewportDisplayMode>? DisplayModeChanged;
        public event EventHandler? ViewportWindowRequested;

        public ViewportViewModel()
        {
            LaunchViewportCommand = new RelayCommand(() => ViewportWindowRequested?.Invoke(this, EventArgs.Empty));
        }

        public GraphicsBackendPreference SelectedBackend
        {
            get => _selectedBackend;
            set => SetProperty(ref _selectedBackend, value);
        }

        public ViewportDisplayMode SelectedDisplayMode
        {
            get => _selectedDisplayMode;
            set
            {
                if (SetProperty(ref _selectedDisplayMode, value))
                {
                    DisplayModeChanged?.Invoke(this, value);
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
                }
            }
        }

        public bool IsFloatingPill => SelectedTabStyle == ViewportTabStyle.FloatingPill;
        public bool IsTopRibbon => SelectedTabStyle == ViewportTabStyle.TopRibbon;
        public bool IsSideRail => SelectedTabStyle == ViewportTabStyle.SideRail;
        public bool IsHotkeysOnly => SelectedTabStyle == ViewportTabStyle.HotkeysOnly;

        public bool IsPipEnabled
        {
            get => _isPipEnabled;
            set => SetProperty(ref _isPipEnabled, value);
        }

        public int MaxPipStreams
        {
            get => _maxPipStreams;
            set => SetProperty(ref _maxPipStreams, value);
        }

        public bool AutoLaunchOnConnect
        {
            get => _autoLaunchOnConnect;
            set => SetProperty(ref _autoLaunchOnConnect, value);
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
            set => SetProperty(ref _isVsyncEnabled, value);
        }

        public ObservableCollection<GraphicsBackendPreference> AvailableBackends { get; } = new()
        {
            GraphicsBackendPreference.Auto,
            GraphicsBackendPreference.Direct3D11,
            GraphicsBackendPreference.Vulkan,
            GraphicsBackendPreference.Metal,
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

        public ObservableCollection<ViewportCharacterTabViewModel> PipThumbnails { get; } = new();

        public ViewportCharacterTabViewModel? ActiveTab
        {
            get => _activeTab;
            set
            {
                if (SetProperty(ref _activeTab, value))
                {
                    foreach (var tab in CharacterTabs)
                    {
                        tab.IsActive = (tab == value);
                    }

                    if (value != null)
                    {
                        SessionRegistry.Default.SetPrimaryRenderingSession(value.Session);
                    }

                    RefreshPipThumbnails();
                }
            }
        }

        private void RefreshPipThumbnails()
        {
            PipThumbnails.Clear();
            foreach (var tab in CharacterTabs.Where(t => t != ActiveTab).Take(MaxPipStreams))
            {
                PipThumbnails.Add(tab);
            }
        }

        public ICommand LaunchViewportCommand { get; }

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
                onSelect: tab => ActiveTab = tab,
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
                    ActiveTab = CharacterTabs.FirstOrDefault();
                }
                else
                {
                    RefreshPipThumbnails();
                }
            }
        }

        public void CycleNextCharacter()
        {
            if (CharacterTabs.Count <= 1 || ActiveTab == null) return;

            int idx = CharacterTabs.IndexOf(ActiveTab);
            int nextIdx = (idx + 1) % CharacterTabs.Count;
            ActiveTab = CharacterTabs[nextIdx];
        }

        public void CyclePreviousCharacter()
        {
            if (CharacterTabs.Count <= 1 || ActiveTab == null) return;

            int idx = CharacterTabs.IndexOf(ActiveTab);
            int prevIdx = (idx - 1 + CharacterTabs.Count) % CharacterTabs.Count;
            ActiveTab = CharacterTabs[prevIdx];
        }
    }
}
