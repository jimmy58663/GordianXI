// src/Gordian.App/Services/WindowPlacementManager.cs
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Gordian.Core.Config;
using Gordian.Core.Diagnostics;

namespace Gordian.App.Services
{
    /// <summary>
    /// Coordinates saving and restoring window coordinates, sizes, and states across sessions.
    /// Protects against off-screen placement when monitors are disconnected or resolutions change.
    /// </summary>
    public sealed class WindowPlacementManager
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        };

        private static readonly Lazy<WindowPlacementManager> _defaultInstance =
            new(() => new WindowPlacementManager());

        public static WindowPlacementManager Default => _defaultInstance.Value;

        private readonly string _filePath;
        private readonly ConcurrentDictionary<string, WindowPlacement> _placements = new(StringComparer.OrdinalIgnoreCase);
        private readonly object _saveLock = new();
        private DispatcherTimer? _debounceSaveTimer;
        private bool _isDirty;

        public WindowPlacementManager(string? filePath = null)
        {
            _filePath = filePath ?? GetDefaultFilePath();
            Load();
        }

        public static string GetDefaultFilePath()
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (!string.IsNullOrWhiteSpace(localAppData))
            {
                return Path.Combine(localAppData, "GordianXI", "window_placements.json");
            }
            return Path.Combine(GordianStorage.RootDataDirectory, "window_placements.json");
        }

        public WindowPlacement? GetPlacement(string key)
        {
            ArgumentNullException.ThrowIfNull(key);
            return _placements.TryGetValue(key, out var placement) ? placement.Clone() : null;
        }

        public void SetPlacement(string key, WindowPlacement placement)
        {
            ArgumentNullException.ThrowIfNull(key);
            ArgumentNullException.ThrowIfNull(placement);

            _placements[key] = placement.Clone();
            ScheduleSave();
        }

        public void Load()
        {
            _placements.Clear();

            if (!File.Exists(_filePath))
            {
                return;
            }

            try
            {
                string json = File.ReadAllText(_filePath);
                var loaded = JsonSerializer.Deserialize<Dictionary<string, WindowPlacement>>(json, JsonOptions);
                if (loaded != null)
                {
                    foreach (var kvp in loaded)
                    {
                        _placements[kvp.Key] = kvp.Value;
                    }
                }
            }
            catch (Exception ex)
            {
                GordianLog.Warn("WINDOW", $"Failed to load window placements from '{_filePath}': {ex.Message}");
            }
        }

        public void Save()
        {
            lock (_saveLock)
            {
                try
                {
                    string? dir = Path.GetDirectoryName(_filePath);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    {
                        Directory.CreateDirectory(dir);
                    }

                    var dict = new Dictionary<string, WindowPlacement>(_placements, StringComparer.OrdinalIgnoreCase);
                    string json = JsonSerializer.Serialize(dict, JsonOptions);
                    File.WriteAllText(_filePath, json);
                    _isDirty = false;
                }
                catch (Exception ex)
                {
                    GordianLog.Warn("WINDOW", $"Failed to save window placements to '{_filePath}': {ex.Message}");
                }
            }
        }

        private void ScheduleSave()
        {
            _isDirty = true;
            if (Dispatcher.UIThread.CheckAccess())
            {
                if (_debounceSaveTimer == null)
                {
                    _debounceSaveTimer = new DispatcherTimer
                    {
                        Interval = TimeSpan.FromMilliseconds(500)
                    };
                    _debounceSaveTimer.Tick += (s, e) =>
                    {
                        _debounceSaveTimer.Stop();
                        if (_isDirty)
                        {
                            Save();
                        }
                    };
                }
                _debounceSaveTimer.Stop();
                _debounceSaveTimer.Start();
            }
            else
            {
                Save();
            }
        }

        /// <summary>
        /// Pure helper to verify if a given placement coordinates rectangle is on any active screen working area.
        /// Requires at least 50px horizontal and 30px vertical overlap so the title bar remains accessible.
        /// </summary>
        public static bool IsPlacementOnScreen(WindowPlacement placement, IEnumerable<PixelRect>? screenWorkingAreas)
        {
            ArgumentNullException.ThrowIfNull(placement);

            if (screenWorkingAreas == null)
            {
                return true;
            }

            var areas = screenWorkingAreas as IList<PixelRect> ?? screenWorkingAreas.ToList();
            if (areas.Count == 0)
            {
                return true;
            }

            int winWidth = (int)Math.Max(100, placement.Width);
            int winHeight = (int)Math.Max(50, placement.Height);

            foreach (var area in areas)
            {
                int overlapX = Math.Min(placement.X + winWidth, area.X + area.Width) - Math.Max(placement.X, area.X);
                int overlapY = Math.Min(placement.Y + winHeight, area.Y + area.Height) - Math.Max(placement.Y, area.Y);

                if (overlapX >= 50 && overlapY >= 30)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Restores the saved placement onto the target window, if found and valid.
        /// If off-screen, centers on primary screen or sets WindowStartupLocation.CenterScreen.
        /// </summary>
        public bool ApplyPlacement(Window window, string key)
        {
            ArgumentNullException.ThrowIfNull(window);
            ArgumentNullException.ThrowIfNull(key);

            if (!_placements.TryGetValue(key, out var placement))
            {
                return false;
            }

            // Screen visibility verification
            var screens = window.Screens?.All?.Select(s => s.WorkingArea).ToList();
            bool isOnScreen = IsPlacementOnScreen(placement, screens);

            if (!isOnScreen)
            {
                GordianLog.Warn("WINDOW", $"Saved placement for '{key}' ({placement.X}, {placement.Y}) is off-screen. Resetting to center.");
                window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
                if (placement.Width > 0 && placement.Height > 0)
                {
                    window.Width = placement.Width;
                    window.Height = placement.Height;
                }
                return false;
            }

            window.Position = new PixelPoint(placement.X, placement.Y);
            if (placement.Width > 0 && placement.Height > 0)
            {
                window.Width = placement.Width;
                window.Height = placement.Height;
            }

            if (placement.WindowState == WindowState.Maximized)
            {
                window.WindowState = WindowState.Maximized;
            }
            else if (placement.WindowState == WindowState.Normal)
            {
                window.WindowState = WindowState.Normal;
            }

            return true;
        }

        /// <summary>
        /// Tracks a window's movement, resize, state change, and closing events to persist its placement automatically.
        /// </summary>
        public void TrackWindow(Window window, string key)
        {
            ArgumentNullException.ThrowIfNull(window);
            ArgumentNullException.ThrowIfNull(key);

            // Apply existing placement if available
            ApplyPlacement(window, key);

            // Fetch or create tracking placement entry
            if (!_placements.TryGetValue(key, out var placement))
            {
                placement = new WindowPlacement
                {
                    X = window.Position.X,
                    Y = window.Position.Y,
                    Width = window.Width,
                    Height = window.Height,
                    WindowState = window.WindowState == WindowState.Maximized ? WindowState.Maximized : WindowState.Normal
                };
                _placements[key] = placement;
            }

            // Track position changes (when not maximized or minimized)
            window.PositionChanged += (_, e) =>
            {
                if (window.WindowState == WindowState.Normal)
                {
                    placement.X = window.Position.X;
                    placement.Y = window.Position.Y;
                    ScheduleSave();
                }
            };

            // Track size changes (when not maximized or minimized)
            window.SizeChanged += (_, e) =>
            {
                if (window.WindowState == WindowState.Normal && e.NewSize.Width > 0 && e.NewSize.Height > 0)
                {
                    placement.Width = e.NewSize.Width;
                    placement.Height = e.NewSize.Height;
                    ScheduleSave();
                }
            };

            // Track window state changes (Maximized / Normal)
            window.PropertyChanged += (_, e) =>
            {
                if (e.Property == Window.WindowStateProperty)
                {
                    if (window.WindowState == WindowState.Maximized)
                    {
                        placement.WindowState = WindowState.Maximized;
                        ScheduleSave();
                    }
                    else if (window.WindowState == WindowState.Normal)
                    {
                        placement.WindowState = WindowState.Normal;
                        placement.X = window.Position.X;
                        placement.Y = window.Position.Y;
                        if (window.Bounds.Width > 0 && window.Bounds.Height > 0)
                        {
                            placement.Width = window.Bounds.Width;
                            placement.Height = window.Bounds.Height;
                        }
                        ScheduleSave();
                    }
                }
            };

            // Ensure immediate save when closing
            window.Closing += (_, _) =>
            {
                if (window.WindowState == WindowState.Normal)
                {
                    placement.X = window.Position.X;
                    placement.Y = window.Position.Y;
                    if (window.Bounds.Width > 0 && window.Bounds.Height > 0)
                    {
                        placement.Width = window.Bounds.Width;
                        placement.Height = window.Bounds.Height;
                    }
                    placement.WindowState = WindowState.Normal;
                }
                else if (window.WindowState == WindowState.Maximized)
                {
                    placement.WindowState = WindowState.Maximized;
                }

                _debounceSaveTimer?.Stop();
                Save();
            };
        }
    }
}
