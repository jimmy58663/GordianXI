// src/Gordian.App/ViewModels/ViewportViewModel.cs
using System;
using System.Collections.ObjectModel;
using Gordian.App.Common;
using Gordian.App.Graphics;

namespace Gordian.App.ViewModels
{
    /// <summary>
    /// ViewModel managing 3D viewport settings, graphics backend selection, and real-time GPU telemetry.
    /// </summary>
    public sealed class ViewportViewModel : ViewModelBase
    {
        private GraphicsBackendPreference _selectedBackend = GraphicsBackendPreference.Auto;
        private string _activeBackend = "Detecting...";
        private string _gpuName = "Detecting GPU...";
        private double _fps = 0.0;
        private double _frameTimeMs = 0.0;
        private string _resolution = "1280 x 720";
        private bool _isVsyncEnabled = true;

        public GraphicsBackendPreference SelectedBackend
        {
            get => _selectedBackend;
            set => SetProperty(ref _selectedBackend, value);
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
    }
}
