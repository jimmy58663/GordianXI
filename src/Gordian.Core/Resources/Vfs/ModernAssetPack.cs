// src/Gordian.Core/Resources/Vfs/ModernAssetPack.cs
using System;
using System.Collections.Generic;
using System.IO;
using Gordian.Core.Diagnostics;
using Gordian.Core.Resources.Vfs.Models;

namespace Gordian.Core.Resources.Vfs
{
    /// <summary>
    /// Represents an indexed modern asset pack containing glTF 2.0 models, DDS/PNG textures,
    /// OGG/FLAC audio, and an optional manifest.json declaring entity overrides and DAT aliases.
    /// </summary>
    public sealed class ModernAssetPack
    {
        private readonly Dictionary<string, string> _fileIndex = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _datAliases = new(StringComparer.Ordinal);

        public string Id { get; }
        public string RootPath { get; }
        public int Priority { get; set; }
        public PackManifest? Manifest { get; private set; }

        public int FileCount => _fileIndex.Count;
        public int AliasCount => _datAliases.Count;
        public IReadOnlyDictionary<string, string> FileIndex => _fileIndex;
        public IReadOnlyDictionary<string, string> DatAliases => _datAliases;

        public ModernAssetPack(string id, string rootPath, int priority = 50)
        {
            Id = id ?? string.Empty;
            RootPath = rootPath ?? string.Empty;
            Priority = priority;
        }

        /// <summary>
        /// Mounts and indexes all assets and manifest mappings within the pack directory.
        /// </summary>
        public bool Mount()
        {
            _fileIndex.Clear();
            _datAliases.Clear();
            Manifest = null;

            if (string.IsNullOrWhiteSpace(RootPath) || !Directory.Exists(RootPath))
            {
                GordianLog.Warn("VFS", $"Cannot mount asset pack '{Id}': directory does not exist ({RootPath}).");
                return false;
            }

            try
            {
                // 1. Check for manifest.json
                string manifestPath = Path.Combine(RootPath, "manifest.json");
                if (File.Exists(manifestPath))
                {
                    try
                    {
                        string json = File.ReadAllText(manifestPath);
                        Manifest = PackManifest.FromJson(json);
                        GordianLog.Info("VFS", $"Loaded manifest for asset pack '{Id}' (v{Manifest?.Version})");
                    }
                    catch (Exception ex)
                    {
                        GordianLog.Error("VFS", $"Failed to parse manifest.json in pack '{Id}': {ex.Message}");
                    }
                }

                // 2. Index all files within the pack
                var files = Directory.EnumerateFiles(RootPath, "*", SearchOption.AllDirectories);

                foreach (var physicalPath in files)
                {
                    string fileName = Path.GetFileName(physicalPath);

                    if (fileName.StartsWith('.') || fileName.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    string relPath = Path.GetRelativePath(RootPath, physicalPath);
                    string key = VfsPathNormalizer.Normalize(relPath);

                    if (!string.IsNullOrEmpty(key))
                    {
                        _fileIndex[key] = physicalPath;
                    }
                }

                // 3. Register DAT aliases from manifest
                if (Manifest?.Overrides?.DatAliases != null)
                {
                    foreach (var (datPath, targetAssetRelPath) in Manifest.Overrides.DatAliases)
                    {
                        string datKey = VfsPathNormalizer.Normalize(datPath);
                        string assetKey = VfsPathNormalizer.Normalize(targetAssetRelPath);

                        if (_fileIndex.TryGetValue(assetKey, out string? resolvedPhysical))
                        {
                            _datAliases[datKey] = resolvedPhysical;
                        }
                        else
                        {
                            // If not in index, check direct relative file path on disk
                            string directPath = Path.Combine(RootPath, targetAssetRelPath);
                            if (File.Exists(directPath))
                            {
                                _datAliases[datKey] = directPath;
                            }
                            else
                            {
                                GordianLog.Warn("VFS", $"Pack '{Id}' DAT alias '{datPath}' target not found: {targetAssetRelPath}");
                            }
                        }
                    }
                }

                GordianLog.Info("VFS", $"Mounted modern asset pack '{Id}' with {_fileIndex.Count} files and {_datAliases.Count} DAT aliases (Priority: {Priority}).");
                return true;
            }
            catch (Exception ex)
            {
                GordianLog.Error("VFS", $"Error mounting modern asset pack '{Id}': {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Attempts to resolve a relative asset path within this pack.
        /// </summary>
        public bool TryResolveAsset(string normalizedKey, out string physicalPath)
        {
            return _fileIndex.TryGetValue(normalizedKey, out physicalPath!);
        }

        /// <summary>
        /// Attempts to resolve a legacy DAT path redirected by this pack's manifest to a modern asset.
        /// </summary>
        public bool TryResolveDatAlias(string normalizedDatKey, out string physicalPath)
        {
            return _datAliases.TryGetValue(normalizedDatKey, out physicalPath!);
        }

        /// <summary>
        /// Resolves an Item ID override declared in manifest.json.
        /// </summary>
        public bool TryResolveItemOverride(uint itemId, out ItemOverrideEntry? entry, out string physicalModelPath)
        {
            entry = null;
            physicalModelPath = string.Empty;

            if (Manifest?.Overrides?.Items != null &&
                Manifest.Overrides.Items.TryGetValue(itemId.ToString(), out entry) &&
                !string.IsNullOrEmpty(entry.Model))
            {
                string assetKey = VfsPathNormalizer.Normalize(entry.Model);
                if (_fileIndex.TryGetValue(assetKey, out physicalModelPath!) ||
                    File.Exists(physicalModelPath = Path.Combine(RootPath, entry.Model)))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Resolves a Zone ID override declared in manifest.json.
        /// </summary>
        public bool TryResolveZoneOverride(ushort zoneId, out ZoneOverrideEntry? entry)
        {
            entry = null;
            if (Manifest?.Overrides?.Zones != null)
            {
                return Manifest.Overrides.Zones.TryGetValue(zoneId.ToString(), out entry);
            }

            return false;
        }

        public void Clear()
        {
            _fileIndex.Clear();
            _datAliases.Clear();
            Manifest = null;
        }

        public override string ToString() => $"[Asset Pack] {Id} ({FileCount} files, {AliasCount} aliases, Priority: {Priority})";
    }
}
