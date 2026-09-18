// src/Gordian.Core/Resources/ResourceManager.cs
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using Gordian.Core.Diagnostics;
using Gordian.Core.Resources.Containers;
using Gordian.Core.Resources.Graphics;
using Gordian.Core.Resources.Models;
using Gordian.Core.Resources.Tables;
using Gordian.Core.Resources.Vfs;
using Gordian.Core.Resources.Vfs.Models;
using Gordian.Core.World;

namespace Gordian.Core.Resources
{
    /// <summary>
    /// Thread-safe client resource manager providing cached, high-speed access to FFXI ROM assets,
    /// DMsg string tables, item databases, and file table mappings via the Modular VFS.
    /// Derived from community research in xi-model-viewer (https://github.com/vekien/xi-model-viewer)
    /// and LandSandBoat (https://github.com/LandSandBoat/server).
    /// </summary>
    public sealed class ResourceManager
    {
        private readonly object _lock = new();
        private readonly string _gameDirectory;
        private readonly IVirtualFileSystem _vfs;
        private readonly FileTableResolver _fileTable = new();

        private readonly ConcurrentDictionary<uint, ItemRecord> _itemCache = new();
        private readonly ConcurrentDictionary<DMsgCategory, DMsgStringTable> _dmsgCache = new();
        private readonly ConcurrentDictionary<int, (ZoneGeometry Geometry, Dictionary<string, DecodedTexture> Textures)> _zoneCache = new();
        private readonly ConcurrentDictionary<string, EntityModel> _entityModelCache = new(StringComparer.OrdinalIgnoreCase);
        private byte[]? _keyTable1;
        private byte[]? _keyTable2;

        public string GameDirectory => _gameDirectory;
        public IVirtualFileSystem Vfs => _vfs;
        public FileTableResolver FileTable => _fileTable;

        public ResourceManager(string gameDirectory)
            : this(gameDirectory, null, null)
        {
        }

        public ResourceManager(string gameDirectory, string? resourcesDirectory, IVirtualFileSystem? vfs = null)
        {
            _gameDirectory = gameDirectory ?? string.Empty;
            _vfs = vfs ?? new VirtualFileSystem(_gameDirectory, resourcesDirectory);
        }

        /// <summary>
        /// Initializes the master file table resolver by reading FTABLE.DAT and VTABLE.DAT,
        /// as well as expansion tables (ROM2..ROM10) if present.
        /// First checks the Modular VFS for overlays, then falls back to the base game root.
        /// </summary>
        public bool InitializeFileTable()
        {
            try
            {
                byte[]? ftBytes = null;
                byte[]? vtBytes = null;

                if (_vfs.TryResolveDat("FTABLE.DAT", out var ftAsset) &&
                    _vfs.TryResolveDat("VTABLE.DAT", out var vtAsset))
                {
                    ftBytes = ftAsset!.ReadAllBytes();
                    vtBytes = vtAsset!.ReadAllBytes();
                }
                else if (!string.IsNullOrWhiteSpace(_gameDirectory) && Directory.Exists(_gameDirectory))
                {
                    string ftable = Path.Combine(_gameDirectory, "FTABLE.DAT");
                    string vtable = Path.Combine(_gameDirectory, "VTABLE.DAT");

                    if (File.Exists(ftable) && File.Exists(vtable))
                    {
                        ftBytes = File.ReadAllBytes(ftable);
                        vtBytes = File.ReadAllBytes(vtable);
                    }
                }

                if (ftBytes != null && vtBytes != null)
                {
                    _fileTable.LoadTablePair(ftBytes, vtBytes);

                    // Also load expansion tables (ROM2 through ROM10)
                    for (int rom = 2; rom <= 10; rom++)
                    {
                        byte[]? expFt = null;
                        byte[]? expVt = null;
                        string ftRel = Path.Combine($"ROM{rom}", $"FTABLE{rom}.DAT");
                        string vtRel = Path.Combine($"ROM{rom}", $"VTABLE{rom}.DAT");

                        if (_vfs.TryResolveDat(ftRel, out var expFtAsset) &&
                            _vfs.TryResolveDat(vtRel, out var expVtAsset))
                        {
                            expFt = expFtAsset!.ReadAllBytes();
                            expVt = expVtAsset!.ReadAllBytes();
                        }
                        else if (!string.IsNullOrWhiteSpace(_gameDirectory))
                        {
                            string ftPath = Path.Combine(_gameDirectory, ftRel);
                            string vtPath = Path.Combine(_gameDirectory, vtRel);
                            if (File.Exists(ftPath) && File.Exists(vtPath))
                            {
                                expFt = File.ReadAllBytes(ftPath);
                                expVt = File.ReadAllBytes(vtPath);
                            }
                        }

                        if (expFt != null && expVt != null)
                        {
                            _fileTable.LoadTablePair(expFt, expVt);
                        }
                    }

                    GordianLog.Info("RES", $"Loaded master file table with {_fileTable.Count} entries across base and expansions.");
                    return true;
                }
            }
            catch (Exception ex)
            {
                GordianLog.Error("RES", $"Failed to load master file tables: {ex.Message}");
            }

            return false;
        }

        /// <summary>
        /// Attempts to resolve a numeric File ID to its physical path on disk via the VFS or base game directory.
        /// </summary>
        public bool TryResolveFile(int fileId, out string fullPath)
        {
            if (_fileTable.TryResolve(fileId, out var relPath))
            {
                if (_vfs.TryResolveDat(relPath, out var resolved))
                {
                    fullPath = resolved!.PhysicalPath;
                    return true;
                }

                if (!string.IsNullOrEmpty(_gameDirectory))
                {
                    fullPath = Path.Combine(_gameDirectory, relPath);
                    return File.Exists(fullPath);
                }
            }

            fullPath = string.Empty;
            return false;
        }

        /// <summary>
        /// Reads and decodes an entire DAT file into a hierarchical directory tree.
        /// </summary>
        public DatDirectoryNode? LoadDatTree(string relativePath)
        {
            try
            {
                byte[]? bytes = null;
                if (_vfs.TryResolveDat(relativePath, out var resolved))
                {
                    bytes = resolved!.ReadAllBytes();
                }
                else if (!string.IsNullOrEmpty(_gameDirectory))
                {
                    string fullPath = Path.Combine(_gameDirectory, relativePath);
                    if (File.Exists(fullPath))
                    {
                        bytes = File.ReadAllBytes(fullPath);
                    }
                }

                if (bytes != null)
                {
                    return DatDirectoryTree.Build(bytes);
                }
            }
            catch (Exception ex)
            {
                GordianLog.Error("RES", $"Failed to load DAT tree for {relativePath}: {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// Loads or retrieves a cached d_msg string table by category.
        /// </summary>
        public DMsgStringTable? GetDMsgTable(DMsgCategory category)
        {
            if (_dmsgCache.TryGetValue(category, out var cached))
            {
                return cached;
            }

            string relPath = GetDMsgRelativePath(category);
            if (string.IsNullOrEmpty(relPath)) return null;

            try
            {
                byte[]? bytes = null;
                if (_vfs.TryResolveDat(relPath, out var resolved))
                {
                    bytes = resolved!.ReadAllBytes();
                }
                else if (!string.IsNullOrEmpty(_gameDirectory))
                {
                    string fullPath = Path.Combine(_gameDirectory, relPath);
                    if (File.Exists(fullPath))
                    {
                        bytes = File.ReadAllBytes(fullPath);
                    }
                }

                if (bytes != null)
                {
                    var fieldNames = GetDMsgFieldNames(category);
                    var table = DMsgStringTable.Parse(bytes, fieldNames);

                    if (table != null)
                    {
                        _dmsgCache.TryAdd(category, table);
                        return table;
                    }
                }
            }
            catch (Exception ex)
            {
                GordianLog.Error("RES", $"Failed to parse d_msg table {category}: {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// Queries a string by category and 0-based index.
        /// </summary>
        public bool TryGetString(DMsgCategory category, int index, out string text)
        {
            var table = GetDMsgTable(category);
            if (table != null && index >= 0 && index < table.Count)
            {
                text = table.Records[index].PrimaryText;
                return !string.IsNullOrEmpty(text);
            }

            text = string.Empty;
            return false;
        }

        /// <summary>
        /// Retrieves the authentic name of a spell by Spell ID.
        /// </summary>
        public bool TryGetSpellName(ushort spellId, out string spellName)
        {
            return TryGetString(DMsgCategory.Spells, spellId, out spellName);
        }

        /// <summary>
        /// Retrieves the authentic name of an ability by Ability ID.
        /// </summary>
        public bool TryGetAbilityName(ushort abilityId, out string abilityName)
        {
            return TryGetString(DMsgCategory.Abilities, abilityId, out abilityName);
        }

        /// <summary>
        /// Retrieves the authentic name of a status effect by Effect ID.
        /// </summary>
        public bool TryGetStatusName(byte statusId, out string statusName)
        {
            return TryGetString(DMsgCategory.StatusNames, statusId, out statusName);
        }

        /// <summary>
        /// Preloads and caches an item table from a given relative path.
        /// </summary>
        public int LoadItemDat(string relativePath)
        {
            try
            {
                byte[]? bytes = null;
                if (_vfs.TryResolveDat(relativePath, out var resolved))
                {
                    bytes = resolved!.ReadAllBytes();
                }
                else if (!string.IsNullOrEmpty(_gameDirectory))
                {
                    string fullPath = Path.Combine(_gameDirectory, relativePath);
                    if (File.Exists(fullPath))
                    {
                        bytes = File.ReadAllBytes(fullPath);
                    }
                }

                if (bytes != null)
                {
                    var items = ItemTableDecoder.ParseItemDat(bytes);
                    int loaded = 0;

                    for (int i = 0; i < items.Count; i++)
                    {
                        var item = items[i];
                        _itemCache[item.ItemId] = item;
                        loaded++;
                    }

                    GordianLog.Info("RES", $"Loaded {loaded} items from {relativePath}");
                    return loaded;
                }
            }
            catch (Exception ex)
            {
                GordianLog.Error("RES", $"Failed to load items from {relativePath}: {ex.Message}");
            }

            return 0;
        }

        /// <summary>
        /// Attempts to get an item record by Item ID from the cache or disk.
        /// </summary>
        public bool TryGetItem(uint itemId, out ItemRecord? item)
        {
            if (_itemCache.TryGetValue(itemId, out item))
            {
                return true;
            }

            string relPath = GetItemTablePathForId(itemId);
            if (!string.IsNullOrEmpty(relPath))
            {
                LoadItemDat(relPath);
                return _itemCache.TryGetValue(itemId, out item);
            }

            item = null;
            return false;
        }

        private static string GetDMsgRelativePath(DMsgCategory category) => category switch
        {
            DMsgCategory.Spells => Path.Combine("ROM", "181", "73.DAT"),
            DMsgCategory.SpellHelp => Path.Combine("ROM", "181", "75.DAT"),
            DMsgCategory.Abilities => Path.Combine("ROM", "181", "72.DAT"),
            DMsgCategory.AbilityHelp => Path.Combine("ROM", "181", "74.DAT"),
            DMsgCategory.StatusNames => Path.Combine("ROM", "180", "102.DAT"),
            DMsgCategory.Titles => Path.Combine("ROM", "180", "78.DAT"),
            DMsgCategory.Jobs => Path.Combine("ROM", "165", "86.DAT"),
            DMsgCategory.KeyItems => Path.Combine("ROM", "175", "35.DAT"),
            DMsgCategory.QuestsSandoria => Path.Combine("ROM", "176", "60.DAT"),
            DMsgCategory.QuestsBastok => Path.Combine("ROM", "176", "61.DAT"),
            DMsgCategory.QuestsWindurst => Path.Combine("ROM", "176", "62.DAT"),
            DMsgCategory.QuestsJeuno => Path.Combine("ROM", "176", "63.DAT"),
            DMsgCategory.MissionsSandoria => Path.Combine("ROM", "176", "67.DAT"),
            DMsgCategory.MissionsBastok => Path.Combine("ROM", "176", "68.DAT"),
            DMsgCategory.MissionsWindurst => Path.Combine("ROM", "176", "69.DAT"),
            DMsgCategory.MissionsZilart => Path.Combine("ROM", "176", "70.DAT"),
            DMsgCategory.MissionsCop => Path.Combine("ROM", "176", "71.DAT"),
            DMsgCategory.MissionsToau => Path.Combine("ROM", "176", "73.DAT"),
            DMsgCategory.MissionsWotg => Path.Combine("ROM", "196", "7.DAT"),
            DMsgCategory.MissionsAdoulin => Path.Combine("ROM", "293", "69.DAT"),
            DMsgCategory.MissionsRov => Path.Combine("ROM", "333", "4.DAT"),
            _ => string.Empty
        };

        private static IReadOnlyList<string>? GetDMsgFieldNames(DMsgCategory category) => category switch
        {
            DMsgCategory.QuestsSandoria or DMsgCategory.QuestsBastok or DMsgCategory.QuestsWindurst or
            DMsgCategory.QuestsJeuno or DMsgCategory.MissionsSandoria or DMsgCategory.MissionsBastok or
            DMsgCategory.MissionsWindurst or DMsgCategory.MissionsZilart or DMsgCategory.MissionsCop or
            DMsgCategory.MissionsToau or DMsgCategory.MissionsWotg or DMsgCategory.MissionsAdoulin or
            DMsgCategory.MissionsRov => new[] { "id", "name", "description" },

            DMsgCategory.SpellHelp or DMsgCategory.AbilityHelp => new[] { "name", "help" },
            DMsgCategory.StatusNames => new[] { "name", "adjective" },
            DMsgCategory.KeyItems => new[] { "id", "category", "unk2", "unk3", "name", "plural", "description" },
            _ => new[] { "name" }
        };

        private static string GetItemTablePathForId(uint itemId)
        {
            if (itemId <= 4095) return Path.Combine("ROM", "118", "106.DAT");             // General
            if (itemId <= 8191) return Path.Combine("ROM", "118", "107.DAT");             // Consumables
            if (itemId <= 8703) return Path.Combine("ROM", "118", "110.DAT");             // Automaton
            if (itemId is >= 10240 and <= 16383) return Path.Combine("ROM", "118", "109.DAT"); // Armor
            if (itemId is >= 16384 and <= 23039) return Path.Combine("ROM", "118", "108.DAT"); // Weapons
            if (itemId is >= 23040 and <= 28671) return Path.Combine("ROM", "286", "73.DAT");  // Armor 2
            if (itemId == 65535) return Path.Combine("ROM", "174", "48.DAT");             // Gil/Currency
            return string.Empty;
        }

        /// <summary>
        /// Attempts to load and parse a zone's 3D terrain geometry and texture resources.
        /// </summary>
        public bool TryLoadZone(int zoneId, out ZoneGeometry? zone, out Dictionary<string, DecodedTexture> textures)
        {
            if (_zoneCache.TryGetValue(zoneId, out var cached))
            {
                zone = cached.Geometry;
                textures = cached.Textures;
                return true;
            }

            textures = new Dictionary<string, DecodedTexture>(StringComparer.OrdinalIgnoreCase);

            // Ensure key tables are extracted if possible
            if (_keyTable1 == null || _keyTable2 == null)
            {
                if (ZoneDataLoader.TryExtractKeyTables(_gameDirectory, out var t1, out var t2))
                {
                    _keyTable1 = t1;
                    _keyTable2 = t2;
                }
            }

            int fileId = ZoneDataLoader.GetZoneModelFileId(zoneId);
            byte[]? datBytes = null;

            if (TryResolveFile(fileId, out string fullPath) && File.Exists(fullPath))
            {
                datBytes = File.ReadAllBytes(fullPath);
            }
            else if (_fileTable.TryResolve(fileId, out string relPath))
            {
                if (_vfs.TryResolveDat(relPath, out var resolved))
                {
                    datBytes = resolved!.ReadAllBytes();
                }
                else if (!string.IsNullOrEmpty(_gameDirectory))
                {
                    string p = Path.Combine(_gameDirectory, relPath);
                    if (File.Exists(p)) datBytes = File.ReadAllBytes(p);
                }
            }

            if (datBytes != null && datBytes.Length > 0)
            {
                zone = ZoneDataLoader.ParseZoneContainer(
                    datBytes,
                    zoneId,
                    _keyTable1 ?? ReadOnlySpan<byte>.Empty,
                    _keyTable2 ?? ReadOnlySpan<byte>.Empty,
                    textures);

                _zoneCache[zoneId] = (zone, textures);
                GordianLog.Info("RES", $"Loaded zone {zoneId} ({zone.MeshGroups.Count} submeshes, {textures.Count} textures).");
                return true;
            }

            zone = null;
            return false;
        }

        /// <summary>
        /// Reads raw binary bytes for a DAT file via the VFS or base game root.
        /// </summary>
        public byte[]? LoadDatBytes(string relativePath)
        {
            if (string.IsNullOrEmpty(relativePath)) return null;

            try
            {
                if (_vfs.TryResolveDat(relativePath, out var resolved) && resolved != null)
                {
                    return resolved.ReadAllBytes();
                }

                if (!string.IsNullOrEmpty(_gameDirectory))
                {
                    string fullPath = Path.Combine(_gameDirectory, relativePath);
                    if (File.Exists(fullPath))
                    {
                        return File.ReadAllBytes(fullPath);
                    }
                }
            }
            catch (Exception ex)
            {
                GordianLog.Error("RES", $"Failed to load DAT bytes for '{relativePath}': {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// Resolves a numeric File ID and loads its raw DAT binary payload.
        /// </summary>
        public byte[]? LoadDatBytesByFileId(int fileId)
        {
            if (_fileTable.TryResolve(fileId, out var relPath))
            {
                return LoadDatBytes(relPath);
            }

            return null;
        }

        /// <summary>
        /// Attempts to load or stitch an EntityModel for a WorldEntity (Player, NPC, Monster, Trust).
        /// Caches assembled models to minimize redundant DAT parsing.
        /// </summary>
        public bool TryLoadEntityModel(WorldEntity entity, out EntityModel? model)
        {
            if (entity == null)
            {
                model = null;
                return false;
            }

            // 1. Monster / NPC with numeric ModelId
            if (entity.Appearance.ModelId > 0)
            {
                string cacheKey = $"Monster_{entity.Appearance.ModelId}";
                if (_entityModelCache.TryGetValue(cacheKey, out model))
                {
                    return true;
                }

                model = EntityModelLoader.LoadMonsterModel(entity.Appearance.ModelId, LoadDatBytesByFileId);
                if (model != null)
                {
                    _entityModelCache[cacheKey] = model;
                    return true;
                }
            }

            // 2. Player or Equipped NPC with GrapIdTable
            var grap = entity.Appearance.GrapIdTable;
            byte rawRace = (byte)((entity.Appearance.FaceModel >> 8) & 0xFF);
            var race = (CharacterRace)rawRace;

            if (race == CharacterRace.Unknown && entity.Type == EntityType.Player)
            {
                race = CharacterRace.HumeMale;
            }

            if (race != CharacterRace.Unknown)
            {
                ushort face = (ushort)(entity.Appearance.FaceModel & 0xFF);
                string cacheKey = $"PC_{race}_{face}_{string.Join('-', grap)}";

                if (_entityModelCache.TryGetValue(cacheKey, out model))
                {
                    return true;
                }

                model = EntityModelLoader.AssembleCharacter(
                    race,
                    face,
                    grap,
                    LoadDatBytes,
                    LoadDatBytesByFileId);

                if (model != null)
                {
                    _entityModelCache[cacheKey] = model;
                    return true;
                }
            }

            model = null;
            return false;
        }

        public void ClearCache()
        {
            _zoneCache.Clear();
            _entityModelCache.Clear();
            _itemCache.Clear();
            _dmsgCache.Clear();
            _fileTable.Clear();
        }
    }
}
