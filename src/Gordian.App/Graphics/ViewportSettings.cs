// src/Gordian.App/Graphics/ViewportSettings.cs
// Manages JSON persistence and defaults for 3D Viewport display and rendering preferences.

using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Gordian.Core.Config;
using Gordian.Core.Diagnostics;

namespace Gordian.App.Graphics
{
    /// <summary>
    /// Persistent configuration for 3D Viewport presentation, graphics backend, and multi-boxing PiP.
    /// </summary>
    public sealed class ViewportSettings
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        };

        public GraphicsBackendPreference SelectedBackend { get; set; } = GraphicsBackendPreference.Auto;
        public ViewportDisplayMode SelectedDisplayMode { get; set; } = ViewportDisplayMode.BorderlessWindow;
        public ViewportTabStyle SelectedTabStyle { get; set; } = ViewportTabStyle.FloatingPill;
        public bool AutoLaunchOnConnect { get; set; } = true;
        public bool IsPipEnabled { get; set; } = false;
        public int MaxPipStreams { get; set; } = 5;
        public bool IsVsyncEnabled { get; set; } = true;

        public static string GetDefaultSettingsPath()
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (!string.IsNullOrWhiteSpace(localAppData))
            {
                return Path.Combine(localAppData, "GordianXI", "viewport_settings.json");
            }
            return Path.Combine(GordianStorage.RootDataDirectory, "viewport_settings.json");
        }

        public void SaveToFile(string filePath)
        {
            ArgumentNullException.ThrowIfNull(filePath);
            string? dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            string json = JsonSerializer.Serialize(this, JsonOptions);
            File.WriteAllText(filePath, json);
        }

        public static ViewportSettings LoadOrDefault(string filePath)
        {
            ArgumentNullException.ThrowIfNull(filePath);

            if (File.Exists(filePath))
            {
                try
                {
                    string json = File.ReadAllText(filePath);
                    var settings = JsonSerializer.Deserialize<ViewportSettings>(json, JsonOptions);
                    if (settings != null)
                    {
                        return settings;
                    }
                }
                catch (Exception ex)
                {
                    GordianLog.Warn("VIEWPORT", $"Failed to load viewport settings from '{filePath}': {ex.Message}. Falling back to defaults.");
                }
            }

            return new ViewportSettings();
        }

        public static ViewportSettings LoadOrCreate(string filePath)
        {
            ArgumentNullException.ThrowIfNull(filePath);

            if (File.Exists(filePath))
            {
                return LoadOrDefault(filePath);
            }

            var defaultSettings = new ViewportSettings();
            try
            {
                defaultSettings.SaveToFile(filePath);
            }
            catch (Exception ex)
            {
                GordianLog.Warn("VIEWPORT", $"Could not save default viewport settings to '{filePath}': {ex.Message}");
            }
            return defaultSettings;
        }
    }
}
