// src/Gordian.Core/Resources/Vfs/PackManifest.cs
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Gordian.Core.Resources.Vfs
{
    /// <summary>
    /// Data contract for modern asset pack manifest.json.
    /// Defines pack metadata, entity bindings, and legacy DAT aliases.
    /// </summary>
    public sealed class PackManifest
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("version")]
        public string Version { get; set; } = "1.0.0";

        [JsonPropertyName("author")]
        public string Author { get; set; } = string.Empty;

        [JsonPropertyName("description")]
        public string Description { get; set; } = string.Empty;

        [JsonPropertyName("overrides")]
        public ManifestOverrides Overrides { get; set; } = new();

        [JsonPropertyName("skeleton")]
        public SkeletonMetadata Skeleton { get; set; } = new();

        public static PackManifest? FromJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;

            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            };

            return JsonSerializer.Deserialize<PackManifest>(json, options);
        }
    }

    public sealed class ManifestOverrides
    {
        [JsonPropertyName("items")]
        public Dictionary<string, ItemOverrideEntry> Items { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        [JsonPropertyName("zones")]
        public Dictionary<string, ZoneOverrideEntry> Zones { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Maps legacy DAT paths (e.g. "ROM/28/52.DAT") directly to modern assets inside this pack.
        /// </summary>
        [JsonPropertyName("datAliases")]
        public Dictionary<string, string> DatAliases { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    public sealed class ItemOverrideEntry
    {
        [JsonPropertyName("model")]
        public string Model { get; set; } = string.Empty;

        [JsonPropertyName("materialVariant")]
        public string? MaterialVariant { get; set; }
    }

    public sealed class ZoneOverrideEntry
    {
        [JsonPropertyName("terrain")]
        public string? Terrain { get; set; }

        [JsonPropertyName("collision")]
        public string? Collision { get; set; }

        [JsonPropertyName("skybox")]
        public string? Skybox { get; set; }
    }

    public sealed class SkeletonMetadata
    {
        [JsonPropertyName("type")]
        public string Type { get; set; } = "ffxi_standard";

        [JsonPropertyName("retargeting")]
        public string Retargeting { get; set; } = "auto";
    }
}
