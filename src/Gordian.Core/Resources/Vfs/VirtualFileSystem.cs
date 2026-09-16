// src/Gordian.Core/Resources/Vfs/VirtualFileSystem.cs
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Gordian.Core.Diagnostics;
using Gordian.Core.Resources.Vfs.Models;

namespace Gordian.Core.Resources.Vfs
{
    /// <summary>
    /// Master coordinator for the GordianXI Modular Virtual File System.
    /// Directs asset queries across modern asset packs (glTF/PBR/Audio), legacy DAT overlays,
    /// and the genuine base game install with prioritized caching and hot-reloading support.
    /// </summary>
    public sealed class VirtualFileSystem : IVirtualFileSystem
    {
        private readonly object _lock = new();
        private string _baseGameDirectory = string.Empty;
        private string _resourcesDirectory = string.Empty;
        private bool _isEnabled = true;
        private VfsConfig _config = new();

        private readonly List<DatOverlayPack> _activeDatPacks = new();
        private readonly List<ModernAssetPack> _activeAssetPacks = new();

        private readonly ConcurrentDictionary<string, ResolvedAsset> _datResolutionCache = new(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, ResolvedAsset> _assetResolutionCache = new(StringComparer.Ordinal);

        private VfsHotReloadWatcher? _hotReloadWatcher;
        private bool _disposed;

        public string BaseGameDirectory => _baseGameDirectory;
        public string ResourcesDirectory => _resourcesDirectory;

        public bool IsEnabled
        {
            get => _isEnabled;
            set
            {
                if (_isEnabled != value)
                {
                    _isEnabled = value;
                    ClearCache();
                    OnReloaded?.Invoke();
                }
            }
        }

        public IReadOnlyList<DatOverlayPack> ActiveDatPacks
        {
            get
            {
                lock (_lock) { return _activeDatPacks.ToList(); }
            }
        }

        public IReadOnlyList<ModernAssetPack> ActiveAssetPacks
        {
            get
            {
                lock (_lock) { return _activeAssetPacks.ToList(); }
            }
        }

        public event Action? OnReloaded;

        public VirtualFileSystem() { }

        public VirtualFileSystem(string baseGameDirectory, string? resourcesDirectory = null)
        {
            Initialize(baseGameDirectory, resourcesDirectory);
        }

        public void Initialize(string baseGameDirectory, string? resourcesDirectory = null)
        {
            _baseGameDirectory = baseGameDirectory ?? string.Empty;

            if (string.IsNullOrWhiteSpace(resourcesDirectory))
            {
                // Default to 'resources' directory next to application base
                _resourcesDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "resources");
            }
            else
            {
                _resourcesDirectory = resourcesDirectory;
            }

            EnsureDirectoriesExist();
            Reload();
            SetupHotReload();
        }

        private void EnsureDirectoriesExist()
        {
            if (string.IsNullOrWhiteSpace(_resourcesDirectory)) return;

            try
            {
                string datsDir = Path.Combine(_resourcesDirectory, "dats");
                string assetsDir = Path.Combine(_resourcesDirectory, "assets");

                if (!Directory.Exists(datsDir)) Directory.CreateDirectory(datsDir);
                if (!Directory.Exists(assetsDir)) Directory.CreateDirectory(assetsDir);
            }
            catch (Exception ex)
            {
                GordianLog.Warn("VFS", $"Could not create resources directory scaffolding: {ex.Message}");
            }
        }

        public void Reload()
        {
            lock (_lock)
            {
                ClearCache();
                _activeDatPacks.Clear();
                _activeAssetPacks.Clear();

                if (string.IsNullOrWhiteSpace(_resourcesDirectory) || !Directory.Exists(_resourcesDirectory))
                {
                    GordianLog.Info("VFS", "No resources directory mounted. Running in base game passthrough mode.");
                    OnReloaded?.Invoke();
                    return;
                }

                string configPath = Path.Combine(_resourcesDirectory, "vfs.json");
                _config = VfsConfig.LoadOrCreate(configPath);
                _isEnabled = _config.Enabled;

                string datsRoot = Path.Combine(_resourcesDirectory, "dats");
                string assetsRoot = Path.Combine(_resourcesDirectory, "assets");

                var discoveredDats = DiscoverPacks(datsRoot);
                var discoveredAssets = DiscoverPacks(assetsRoot);

                bool configUpdated = _config.SyncDiscoveredPacks(discoveredDats, discoveredAssets);
                if (configUpdated)
                {
                    _config.Save(configPath);
                }

                // 1. Mount enabled DAT overlay packs
                foreach (var entry in _config.Dats.Where(d => d.Enabled).OrderByDescending(d => d.Priority))
                {
                    string packPath = Path.Combine(datsRoot, entry.Id);
                    if (Directory.Exists(packPath))
                    {
                        var datPack = new DatOverlayPack(entry.Id, packPath, entry.Priority);
                        if (datPack.Mount())
                        {
                            _activeDatPacks.Add(datPack);
                        }
                    }
                }

                // 2. Mount enabled modern asset packs
                foreach (var entry in _config.Assets.Where(a => a.Enabled).OrderByDescending(a => a.Priority))
                {
                    string packPath = Path.Combine(assetsRoot, entry.Id);
                    if (Directory.Exists(packPath))
                    {
                        var assetPack = new ModernAssetPack(entry.Id, packPath, entry.Priority);
                        if (assetPack.Mount())
                        {
                            _activeAssetPacks.Add(assetPack);
                        }
                    }
                }

                GordianLog.Info("VFS", $"VFS Reloaded: {_activeDatPacks.Count} active DAT packs, {_activeAssetPacks.Count} active Asset packs.");
            }

            OnReloaded?.Invoke();
        }

        private static List<string> DiscoverPacks(string rootDirectory)
        {
            var result = new List<string>();
            if (Directory.Exists(rootDirectory))
            {
                foreach (var dir in Directory.EnumerateDirectories(rootDirectory))
                {
                    string folderName = Path.GetFileName(dir);
                    if (!folderName.StartsWith('.'))
                    {
                        result.Add(folderName);
                    }
                }
            }
            return result;
        }

        private void SetupHotReload()
        {
            _hotReloadWatcher?.Dispose();
            _hotReloadWatcher = null;

            if (!_config.HotReload || string.IsNullOrWhiteSpace(_resourcesDirectory) || !Directory.Exists(_resourcesDirectory))
            {
                return;
            }

            _hotReloadWatcher = new VfsHotReloadWatcher();
            _hotReloadWatcher.WatchDirectory(Path.Combine(_resourcesDirectory, "dats"));
            _hotReloadWatcher.WatchDirectory(Path.Combine(_resourcesDirectory, "assets"));

            _hotReloadWatcher.OnPacksStructureChanged += () =>
            {
                GordianLog.Info("VFS", "Hot-reload triggered: Re-mounting packs.");
                Reload();
            };

            _hotReloadWatcher.OnAssetsChanged += (files) =>
            {
                GordianLog.Info("VFS", $"Hot-reload triggered: Evicting {files.Count} files from cache.");
                ClearCache();
                OnReloaded?.Invoke();
            };
        }

        /// <summary>
        /// Resolves a legacy DAT path through the prioritized VFS pipeline:
        /// 1. Active Modern Asset Packs (DAT Aliases e.g. .glb replacing .DAT)
        /// 2. Active DAT Overlay Packs (by descending priority)
        /// 3. Genuine Base Game Installation
        /// </summary>
        public bool TryResolveDat(string relativePath, out ResolvedAsset? resolved)
        {
            string key = VfsPathNormalizer.Normalize(relativePath);
            if (string.IsNullOrEmpty(key))
            {
                resolved = null;
                return false;
            }

            if (_datResolutionCache.TryGetValue(key, out resolved))
            {
                return true;
            }

            if (_isEnabled)
            {
                lock (_lock)
                {
                    // 1. Check Modern Asset Packs for DAT Aliases (e.g. .glb overrides .DAT)
                    foreach (var pack in _activeAssetPacks)
                    {
                        if (pack.TryResolveDatAlias(key, out string physicalPath))
                        {
                            var category = ClassifyAsset(physicalPath);
                            resolved = new ResolvedAsset(key, physicalPath, pack.Id, category, isOverride: true);
                            _datResolutionCache[key] = resolved;
                            return true;
                        }
                    }

                    // 2. Check DAT Overlay Packs (Priority order)
                    foreach (var pack in _activeDatPacks)
                    {
                        if (pack.TryResolve(key, out string physicalPath))
                        {
                            resolved = new ResolvedAsset(key, physicalPath, pack.Id, AssetCategory.Dat, isOverride: true);
                            _datResolutionCache[key] = resolved;
                            return true;
                        }
                    }
                }
            }

            // 3. Fallback to genuine base game install
            if (!string.IsNullOrEmpty(_baseGameDirectory))
            {
                string basePhysical = Path.Combine(_baseGameDirectory, key.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(basePhysical))
                {
                    resolved = new ResolvedAsset(key, basePhysical, "BaseGame", AssetCategory.Dat, isOverride: false);
                    _datResolutionCache[key] = resolved;
                    return true;
                }
            }

            resolved = null;
            return false;
        }

        /// <summary>
        /// Resolves an asset within the active modern asset packs.
        /// </summary>
        public bool TryResolveAsset(string relativeAssetPath, out ResolvedAsset? resolved)
        {
            string key = VfsPathNormalizer.Normalize(relativeAssetPath);
            if (string.IsNullOrEmpty(key))
            {
                resolved = null;
                return false;
            }

            if (_assetResolutionCache.TryGetValue(key, out resolved))
            {
                return true;
            }

            if (_isEnabled)
            {
                lock (_lock)
                {
                    foreach (var pack in _activeAssetPacks)
                    {
                        if (pack.TryResolveAsset(key, out string physicalPath))
                        {
                            var category = ClassifyAsset(physicalPath);
                            resolved = new ResolvedAsset(key, physicalPath, pack.Id, category, isOverride: true);
                            _assetResolutionCache[key] = resolved;
                            return true;
                        }
                    }
                }
            }

            resolved = null;
            return false;
        }

        /// <summary>
        /// Attempts to resolve an Item ID model override from active modern asset packs.
        /// </summary>
        public bool TryResolveItemModel(uint itemId, out ResolvedAsset? resolved, out string? materialVariant)
        {
            resolved = null;
            materialVariant = null;

            if (!_isEnabled) return false;

            lock (_lock)
            {
                foreach (var pack in _activeAssetPacks)
                {
                    if (pack.TryResolveItemOverride(itemId, out var entry, out string physicalModelPath))
                    {
                        materialVariant = entry?.MaterialVariant;
                        string key = VfsPathNormalizer.Normalize(entry?.Model);
                        resolved = new ResolvedAsset(key, physicalModelPath, pack.Id, AssetCategory.Model, isOverride: true);
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Attempts to resolve a Zone ID asset (terrain, collision, skybox) from active modern asset packs.
        /// </summary>
        public bool TryResolveZoneAsset(ushort zoneId, string assetType, out ResolvedAsset? resolved)
        {
            resolved = null;
            if (!_isEnabled) return false;

            lock (_lock)
            {
                foreach (var pack in _activeAssetPacks)
                {
                    if (pack.TryResolveZoneOverride(zoneId, out var entry) && entry != null)
                    {
                        string? targetRel = assetType.ToLowerInvariant() switch
                        {
                            "terrain" => entry.Terrain,
                            "collision" => entry.Collision,
                            "skybox" => entry.Skybox,
                            _ => null
                        };

                        if (!string.IsNullOrEmpty(targetRel))
                        {
                            string key = VfsPathNormalizer.Normalize(targetRel);
                            if (pack.TryResolveAsset(key, out string physicalPath) ||
                                File.Exists(physicalPath = Path.Combine(pack.RootPath, targetRel)))
                            {
                                resolved = new ResolvedAsset(key, physicalPath, pack.Id, AssetCategory.Model, isOverride: true);
                                return true;
                            }
                        }
                    }
                }
            }

            return false;
        }

        private static AssetCategory ClassifyAsset(string path)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            return ext switch
            {
                ".dat" => AssetCategory.Dat,
                ".glb" or ".gltf" => AssetCategory.Model,
                ".dds" or ".png" or ".webp" or ".jpg" or ".jpeg" => AssetCategory.Texture,
                ".ogg" or ".flac" or ".wav" or ".mp3" => AssetCategory.Audio,
                ".json" or ".yaml" or ".yml" or ".xml" => AssetCategory.Data,
                _ => AssetCategory.Other
            };
        }

        public void ClearCache()
        {
            _datResolutionCache.Clear();
            _assetResolutionCache.Clear();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _hotReloadWatcher?.Dispose();
            ClearCache();
        }
    }
}
