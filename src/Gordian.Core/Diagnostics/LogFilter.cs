using System;
using System.Collections.Frozen;
using System.Collections.Generic;

namespace Gordian.Core.Diagnostics
{
    /// <summary>
    /// An immutable snapshot of the effective logging levels: the built-in defaults (<see cref="LogCategories.DefaultLevels"/>)
    /// overlaid with the user's <see cref="LoggingSettings"/> and the environment overrides. <see cref="GordianLog"/> swaps
    /// the whole snapshot when settings change, so a check on a logging thread never sees half an update.
    /// </summary>
    internal sealed class LogFilter
    {
        private readonly FrozenDictionary<string, LogLevel> _levels;

        private LogFilter(LogLevel defaultLevel, bool networkFile, int retentionDays, long maxFileBytes, FrozenDictionary<string, LogLevel> levels)
        {
            DefaultLevel = defaultLevel;
            NetworkFile = networkFile;
            RetentionDays = retentionDays;
            MaxFileBytes = maxFileBytes;
            _levels = levels;
        }

        /// <summary>Minimum level of categories without their own entry.</summary>
        public LogLevel DefaultLevel { get; }

        /// <summary>Whether network-category detail below <see cref="LogLevel.Info"/> is written to <c>logs/network/</c>.</summary>
        public bool NetworkFile { get; }

        /// <summary>Days to keep system, network and status logs.</summary>
        public int RetentionDays { get; }

        /// <summary>Size at which a system or network log rolls.</summary>
        public long MaxFileBytes { get; }

        /// <summary>
        /// Builds the effective filter. <paramref name="debugDefault"/> is the Debug-build / <c>GORDIAN_DEBUG</c> default,
        /// <paramref name="networkFileEnv"/> the <c>GORDIAN_LOG_NETWORK_DISK</c> override.
        /// </summary>
        public static LogFilter Build(LoggingSettings settings, bool debugDefault, bool networkFileEnv)
        {
            ArgumentNullException.ThrowIfNull(settings);
            LogLevel defaultLevel = settings.DefaultLevel ?? (debugDefault ? LogLevel.Debug : LogLevel.Info);
            bool networkFile = networkFileEnv || (settings.NetworkFile ?? false);

            var levels = new Dictionary<string, LogLevel>(StringComparer.OrdinalIgnoreCase);
            foreach (var (category, level) in LogCategories.DefaultLevels) levels[category] = level;

            // Turning the network file on asks for packet detail: the network categories drop to Debug unless the
            // user gave one its own level.
            if (networkFile)
            {
                foreach (string category in LogCategories.Network) levels[category] = LogLevel.Debug;
            }

            if (settings.Categories is not null)
            {
                foreach (var (category, level) in settings.Categories)
                {
                    if (!string.IsNullOrWhiteSpace(category)) levels[category.Trim()] = level;
                }
            }

            int retention = settings.RetentionDays is > 0 ? settings.RetentionDays.Value : LoggingSettings.DefaultRetentionDays;
            long maxBytes = settings.MaxFileBytes is > 0 ? settings.MaxFileBytes.Value : LoggingSettings.DefaultMaxFileBytes;
            return new LogFilter(defaultLevel, networkFile, retention, maxBytes, levels.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase));
        }

        /// <summary>The minimum level a record of <paramref name="category"/> needs.</summary>
        public LogLevel MinLevel(string category) =>
            _levels.TryGetValue(category, out LogLevel level) ? level : DefaultLevel;

        /// <summary>Whether a record passes: status always does; network detail below Info needs the network file.</summary>
        public bool IsEnabled(LogLevel level, string category)
        {
            if (level == LogLevel.Status) return true;
            if (level < MinLevel(category)) return false;
            if (level < LogLevel.Info && LogCategories.IsNetwork(category)) return NetworkFile;
            return true;
        }
    }
}
