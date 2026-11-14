using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Gordian.Core.Config;

namespace Gordian.Core.Diagnostics
{
    /// <summary>
    /// The user's logging overrides, stored in <c>logging.json</c> under <see cref="GordianStorage.UserSettingsDirectory"/>.
    /// Every property is optional: an unset one keeps the built-in default, so a later release can change a default
    /// without the file freezing the old one. See <c>docs/app/logging.md</c>.
    /// </summary>
    public sealed class LoggingSettings
    {
        /// <summary>The file name of the settings file.</summary>
        public const string FileName = "logging.json";

        /// <summary>Default for <see cref="RetentionDays"/>: system, network and status logs older than this are deleted at startup.</summary>
        public const int DefaultRetentionDays = 7;

        /// <summary>Default for <see cref="MaxFileBytes"/>: a system or network log rolls to its next file at this size.</summary>
        public const long DefaultMaxFileBytes = 20L * 1024 * 1024;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Converters = { new JsonStringEnumConverter() },
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        };

        /// <summary>
        /// Minimum level for categories without their own entry. Unset: <see cref="LogLevel.Debug"/> in Debug builds or
        /// with <c>GORDIAN_DEBUG=1</c>, otherwise <see cref="LogLevel.Info"/>.
        /// </summary>
        public LogLevel? DefaultLevel { get; set; }

        /// <summary>Per-category minimum levels, keyed by category name (<see cref="LogCategories"/>); case-insensitive.</summary>
        public Dictionary<string, LogLevel> Categories { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Whether packet detail (debug lines of the network categories) is written to <c>logs/network/</c>. Unset: off,
        /// unless <c>GORDIAN_LOG_NETWORK_DISK=1</c>.
        /// </summary>
        public bool? NetworkFile { get; set; }

        /// <summary>Whether each character's chat log is written to <c>logs/chat/</c> (#565). Unset: on.</summary>
        public bool? ChatLog { get; set; }

        /// <summary>Days to keep system, network and status logs. Unset: <see cref="DefaultRetentionDays"/>. Chat logs are never deleted.</summary>
        public int? RetentionDays { get; set; }

        /// <summary>Size at which a system or network log rolls to its next file. Unset: <see cref="DefaultMaxFileBytes"/>.</summary>
        public long? MaxFileBytes { get; set; }

        /// <summary>The settings file's path.</summary>
        public static string DefaultPath => Path.Combine(GordianStorage.UserSettingsDirectory, FileName);

        /// <summary>Reads the settings file; a missing or unreadable file gives empty settings (all defaults).</summary>
        public static LoggingSettings Load(string path)
        {
            try
            {
                if (!File.Exists(path)) return new LoggingSettings();
                var loaded = JsonSerializer.Deserialize<LoggingSettings>(File.ReadAllText(path), JsonOptions) ?? new LoggingSettings();
                // Re-key case-insensitively: the deserializer builds its own dictionary with the default comparer.
                loaded.Categories = new Dictionary<string, LogLevel>(loaded.Categories ?? new(), StringComparer.OrdinalIgnoreCase);
                return loaded;
            }
            catch (Exception)
            {
                return new LoggingSettings();
            }
        }

        /// <summary>Writes the settings file, creating its folder.</summary>
        public void Save(string path)
        {
            string? directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOptions));
        }
    }
}
