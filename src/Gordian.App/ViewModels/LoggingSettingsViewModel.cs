using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Input;
using Gordian.App.Common;
using Gordian.Core.Diagnostics;

namespace Gordian.App.ViewModels
{
    /// <summary>One category's level row in the Logs tab's settings.</summary>
    public sealed class CategoryLevelRow : ViewModelBase
    {
        private int _selectedIndex;

        public CategoryLevelRow(string category, LogLevel? builtIn, LogLevel? chosen)
        {
            Category = category;
            BuiltIn = builtIn;
            _selectedIndex = chosen is { } level ? LoggingSettingsViewModel.LevelIndex(level) + 1 : 0;
        }

        public string Category { get; }

        /// <summary>The level GordianXI uses for this category when the user sets none (null: the default level).</summary>
        public LogLevel? BuiltIn { get; }

        /// <summary>"Default" (with the built-in level when the category has one), then each level.</summary>
        public IReadOnlyList<string> Options =>
            new[] { BuiltIn is { } level ? $"Default ({level})" : "Default" }
                .Concat(LoggingSettingsViewModel.LevelChoices.Select(l => l.ToString())).ToArray();

        /// <summary>0 = default; otherwise the index into <see cref="LoggingSettingsViewModel.LevelChoices"/> plus one.</summary>
        public int SelectedIndex
        {
            get => _selectedIndex;
            set => SetProperty(ref _selectedIndex, Math.Clamp(value, 0, LoggingSettingsViewModel.LevelChoices.Count));
        }

        /// <summary>The level chosen, or null for the default.</summary>
        public LogLevel? Chosen => SelectedIndex == 0 ? null : LoggingSettingsViewModel.LevelChoices[SelectedIndex - 1];
    }

    /// <summary>
    /// The Logs tab's settings expander (#566): the default level, per-category levels, the network file and chat logs.
    /// Saving writes <c>logging.json</c> (<see cref="LoggingSettings"/>) and applies it to the running logger at once;
    /// retention and the roll size, which this editor keeps as they are, take effect at the next launch.
    /// </summary>
    public sealed class LoggingSettingsViewModel : ViewModelBase
    {
        /// <summary>The levels a user can pick, lowest first.</summary>
        public static readonly IReadOnlyList<LogLevel> LevelChoices = new[]
        {
            LogLevel.Trace, LogLevel.Debug, LogLevel.Info, LogLevel.Warn, LogLevel.Error, LogLevel.Off,
        };

        private readonly string _path;
        private readonly bool _debugByDefault;
        private readonly Action<LoggingSettings> _apply;
        private LoggingSettings _loaded = new();
        private int _defaultLevelIndex;
        private bool _networkFile;
        private bool _chatLog = true;
        private string _statusText = string.Empty;

        /// <summary>Edits the user's logging.json and applies it to the running logger.</summary>
        public LoggingSettingsViewModel()
            : this(LoggingSettings.DefaultPath, Program.LogDebugByDefault,
                settings => GordianLog.ApplySettings(settings, Program.LogDebugByDefault, Program.LogNetworkFileOverride))
        {
        }

        /// <summary>Edits <paramref name="path"/>; <paramref name="apply"/> receives each saved settings object.</summary>
        public LoggingSettingsViewModel(string path, bool debugByDefault, Action<LoggingSettings> apply)
        {
            _path = path ?? throw new ArgumentNullException(nameof(path));
            _debugByDefault = debugByDefault;
            _apply = apply ?? throw new ArgumentNullException(nameof(apply));
            SaveCommand = new RelayCommand(Save);
            ResetCommand = new RelayCommand(ResetToDefaults);
            Reload();
        }

        /// <summary>"Default (Debug|Info)", then each level, for the default-level picker.</summary>
        public IReadOnlyList<string> DefaultLevelOptions =>
            new[] { $"Default ({(_debugByDefault ? LogLevel.Debug : LogLevel.Info)})" }
                .Concat(LevelChoices.Select(l => l.ToString())).ToArray();

        public int DefaultLevelIndex
        {
            get => _defaultLevelIndex;
            set => SetProperty(ref _defaultLevelIndex, Math.Clamp(value, 0, LevelChoices.Count));
        }

        /// <summary>Write packet detail to logs/network/.</summary>
        public bool NetworkFile
        {
            get => _networkFile;
            set => SetProperty(ref _networkFile, value);
        }

        /// <summary>Write each character's chat to logs/chat/.</summary>
        public bool ChatLog
        {
            get => _chatLog;
            set => SetProperty(ref _chatLog, value);
        }

        public ObservableCollection<CategoryLevelRow> Categories { get; } = new();

        /// <summary>Where the settings are saved, for the expander's caption.</summary>
        public string SettingsPath => _path;

        /// <summary>The result of the last save.</summary>
        public string StatusText
        {
            get => _statusText;
            private set => SetProperty(ref _statusText, value);
        }

        public ICommand SaveCommand { get; }
        public ICommand ResetCommand { get; }

        /// <summary>Every category name in <see cref="LogCategories"/>, plus any other one the settings file names.</summary>
        public static IReadOnlyList<string> KnownCategories(LoggingSettings? settings = null)
        {
            var names = typeof(LogCategories)
                .GetFields(BindingFlags.Public | BindingFlags.Static)
                .Where(f => f.IsLiteral && f.FieldType == typeof(string))
                .Select(f => (string)f.GetRawConstantValue()!)
                .Where(n => n != LogCategories.Status)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (settings?.Categories is { } extra) names.UnionWith(extra.Keys.Select(k => k.ToUpperInvariant()));
            return names.OrderBy(n => n, StringComparer.Ordinal).ToList();
        }

        /// <summary>Reads the settings file into the editor.</summary>
        public void Reload()
        {
            _loaded = LoggingSettings.Load(_path);
            DefaultLevelIndex = _loaded.DefaultLevel is { } level ? LevelIndex(level) + 1 : 0;
            NetworkFile = _loaded.NetworkFile ?? false;
            ChatLog = _loaded.ChatLog ?? true;
            Categories.Clear();
            foreach (string category in KnownCategories(_loaded))
            {
                LogLevel? builtIn = LogCategories.DefaultLevels.TryGetValue(category, out var b) ? b : null;
                LogLevel? chosen = _loaded.Categories.TryGetValue(category, out var c) ? c : null;
                Categories.Add(new CategoryLevelRow(category, builtIn, chosen));
            }
        }

        /// <summary>The settings the editor holds, keeping the file's retention and roll size.</summary>
        public LoggingSettings ToSettings()
        {
            var settings = new LoggingSettings
            {
                DefaultLevel = DefaultLevelIndex == 0 ? null : LevelChoices[DefaultLevelIndex - 1],
                NetworkFile = NetworkFile ? true : null,
                ChatLog = ChatLog ? null : false,
                RetentionDays = _loaded.RetentionDays,
                MaxFileBytes = _loaded.MaxFileBytes,
            };
            foreach (var row in Categories)
            {
                if (row.Chosen is { } level) settings.Categories[row.Category] = level;
            }
            return settings;
        }

        private void Save()
        {
            try
            {
                LoggingSettings settings = ToSettings();
                settings.Save(_path);
                _loaded = settings;
                _apply(settings);
                StatusText = $"Saved to {_path} and applied.";
                GordianLog.Info(LogCategories.Logging, $"Logging settings saved to {_path}.");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                StatusText = $"Could not save: {ex.Message}";
                GordianLog.Error(LogCategories.Logging, $"Could not save logging settings to {_path}", ex);
            }
        }

        private void ResetToDefaults()
        {
            DefaultLevelIndex = 0;
            NetworkFile = false;
            ChatLog = true;
            foreach (var row in Categories) row.SelectedIndex = 0;
            StatusText = "Defaults restored in the editor; Save to keep them.";
        }

        /// <summary>The index of <paramref name="level"/> in <see cref="LevelChoices"/> (0 for a level not offered, such as Status).</summary>
        public static int LevelIndex(LogLevel level)
        {
            for (int i = 0; i < LevelChoices.Count; i++) if (LevelChoices[i] == level) return i;
            return 0;
        }
    }
}
