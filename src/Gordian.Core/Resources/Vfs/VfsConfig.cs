// src/Gordian.Core/Resources/Vfs/VfsConfig.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Gordian.Core.Diagnostics;

namespace Gordian.Core.Resources.Vfs
{
    /// <summary>
    /// Configuration data contract for vfs.json.
    /// Manages user activation toggles and priority stacking for legacy DAT packs and modern asset packs.
    /// </summary>
    public sealed class VfsConfig
    {
        [JsonPropertyName("version")]
        public int Version { get; set; } = 1;

        [JsonPropertyName("enabled")]
        public bool Enabled { get; set; } = true;

        [JsonPropertyName("hotReload")]
        public bool HotReload { get; set; } = true;

        [JsonPropertyName("dats")]
        public List<VfsPackEntry> Dats { get; set; } = new();

        [JsonPropertyName("assets")]
        public List<VfsPackEntry> Assets { get; set; } = new();

        public static VfsConfig LoadOrCreate(string configPath)
        {
            if (File.Exists(configPath))
            {
                try
                {
                    string json = File.ReadAllText(configPath);
                    var loaded = JsonSerializer.Deserialize<VfsConfig>(json, JsonOptions);
                    if (loaded != null)
                    {
                        return loaded;
                    }
                }
                catch (Exception ex)
                {
                    GordianLog.Error("VFS", $"Failed to parse vfs.json from {configPath}: {ex.Message}");
                }
            }

            return new VfsConfig();
        }

        public void Save(string configPath)
        {
            try
            {
                string? dir = Path.GetDirectoryName(configPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                string json = JsonSerializer.Serialize(this, JsonOptions);
                File.WriteAllText(configPath, json);
            }
            catch (Exception ex)
            {
                GordianLog.Error("VFS", $"Failed to save vfs.json to {configPath}: {ex.Message}");
            }
        }

        /// <summary>
        /// Auto-discovers newly added packs on disk and appends them to the configuration as disabled,
        /// preserving user-defined ordering and priorities.
        /// </summary>
        public bool SyncDiscoveredPacks(IEnumerable<string> discoveredDatPackIds, IEnumerable<string> discoveredAssetPackIds)
        {
            bool modified = false;

            var existingDatIds = new HashSet<string>(Dats.Select(d => d.Id), StringComparer.OrdinalIgnoreCase);
            int nextDatPriority = Dats.Count > 0 ? Math.Max(10, Dats.Min(d => d.Priority) - 10) : 50;

            foreach (var packId in discoveredDatPackIds)
            {
                if (!existingDatIds.Contains(packId))
                {
                    Dats.Add(new VfsPackEntry
                    {
                        Id = packId,
                        Enabled = false,
                        Priority = Math.Max(1, nextDatPriority)
                    });
                    nextDatPriority -= 10;
                    modified = true;
                }
            }

            var existingAssetIds = new HashSet<string>(Assets.Select(a => a.Id), StringComparer.OrdinalIgnoreCase);
            int nextAssetPriority = Assets.Count > 0 ? Math.Max(10, Assets.Min(a => a.Priority) - 10) : 50;

            foreach (var packId in discoveredAssetPackIds)
            {
                if (!existingAssetIds.Contains(packId))
                {
                    Assets.Add(new VfsPackEntry
                    {
                        Id = packId,
                        Enabled = false,
                        Priority = Math.Max(1, nextAssetPriority)
                    });
                    nextAssetPriority -= 10;
                    modified = true;
                }
            }

            return modified;
        }

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };
    }

    public sealed class VfsPackEntry
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("enabled")]
        public bool Enabled { get; set; } = true;

        [JsonPropertyName("priority")]
        public int Priority { get; set; } = 50;
    }
}
