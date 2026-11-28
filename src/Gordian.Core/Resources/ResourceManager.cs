// src/Gordian.Core/Resources/ResourceManager.cs
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Threading;
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
    public sealed partial class ResourceManager : IDisposable
    {
        private readonly object _lock = new();
        private readonly string _gameDirectory;
        private Lazy<Tables.RecordsOfEminenceTables?>? _recordsOfEminence;
        private readonly IVirtualFileSystem _vfs;
        private readonly bool _ownsVfs;
        private int _disposed;
        // Replaced whole on a reload (built off to the side, then swapped), so readers never see a half-built table.
        private volatile FileTableResolver _fileTable = new();
        private volatile bool _fileTableInitialized;

        private readonly ConcurrentDictionary<uint, ItemRecord> _itemCache = new();
        // Item tables already read by TryGetItem, so an id with no record (an empty slot) does not reread its table.
        private readonly ConcurrentDictionary<string, Lazy<int>> _loadedItemTables = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<DMsgCategory, DMsgStringTable> _dmsgCache = new();
        private readonly ConcurrentDictionary<int, (ZoneGeometry Geometry, Dictionary<string, DecodedTexture> Textures)> _zoneCache = new();
        // Mount and chair models by name; player and monster models are in ResourceManager.ModelEviction.cs.
        private readonly ConcurrentDictionary<string, CachedEntityModel> _entityModelCache = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<uint, Gordian.Core.Resources.Graphics.ActorEffectSet?> _actorEffectCache = new();
        private readonly ConcurrentDictionary<int, Gordian.Core.Resources.Graphics.ActorEffectSet?> _sceneEffectCache = new();
        private readonly ConcurrentDictionary<int, Gordian.Core.Resources.Graphics.ActorEffectSet?> _gearEffectCache = new();
        private byte[]? _keyTable1;
        private byte[]? _keyTable2;
        private SharedEffectResources? _sharedEffects;
        private volatile bool _sharedEffectsLoaded;

        public string GameDirectory => _gameDirectory;
        public IVirtualFileSystem Vfs => _vfs;
        public FileTableResolver FileTable => _fileTable;

        /// <summary>The Records of Eminence tables (ROM/307/16, ROM/307/24; #493), read once on first use; null without them.</summary>
        public Tables.RecordsOfEminenceTables? RecordsOfEminence =>
            LazyInitializer.EnsureInitialized(ref _recordsOfEminence, () => new Lazy<Tables.RecordsOfEminenceTables?>(
                () => Tables.RecordsOfEminenceTables.Load(LoadDatBytes))).Value;

        private Lazy<AutoTranslateCatalog>? _autoTranslate;
        /// <summary>Installed English dictionary; missing/invalid data yields an explicitly empty catalog.</summary>
        public AutoTranslateCatalog AutoTranslate => LazyInitializer.EnsureInitialized(ref _autoTranslate,
            () => new Lazy<AutoTranslateCatalog>(() =>
            {
                var bytes = LoadDatBytes(Path.Combine("ROM", "168", "25.DAT"));
                if (bytes == null) return AutoTranslateCatalog.Empty;
                try { return AutoTranslateCatalog.Parse(bytes, ResolveAutoTranslate); }
                catch (InvalidDataException) { GordianLog.Warn(LogCategories.Res, "Auto-translate dictionary is invalid."); return AutoTranslateCatalog.Empty; }
            })).Value;

        // Reference kinds and hexadecimal indices: Windower/ResourceExtractor linked-name schema.
        private string? ResolveAutoTranslate(char kind, int id)
        {
            var category = kind switch
            {
                'J' => DMsgCategory.Jobs, 'A' => DMsgCategory.ZoneNames,
                'C' => DMsgCategory.Spells, 'Y' => DMsgCategory.Abilities,
                _ => throw new ArgumentOutOfRangeException(nameof(kind))
            };
            return TryGetString(category, id, out string value) ? value : null;
        }

        public ResourceManager(string gameDirectory)
            : this(gameDirectory, null, null)
        {
        }

        public ResourceManager(string gameDirectory, string? resourcesDirectory, IVirtualFileSystem? vfs = null)
        {
            _gameDirectory = gameDirectory ?? string.Empty;
            _ownsVfs = vfs == null;
            _vfs = vfs ?? new VirtualFileSystem(_gameDirectory, resourcesDirectory);
            // A VFS reload (hot reload, a pack toggled, the VFS switched off) can change what any DAT resolves to.
            _vfs.OnReloaded += ClearCache;
            _furniture = new Lazy<Dictionary<ushort, Tables.FurnitureInfo>>(LoadFurnitureTable);
        }

        /// <summary>
        /// Initializes the master file table resolver by reading FTABLE.DAT and VTABLE.DAT,
        /// as well as expansion tables (ROM2..ROM10) if present.
        /// First checks the Modular VFS for overlays, then falls back to the base game root.
        /// </summary>
        public bool InitializeFileTable()
        {
            var table = new FileTableResolver();
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
                    table.LoadTablePair(ftBytes, vtBytes);

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
                            table.LoadTablePair(expFt, expVt);
                        }
                    }

                    _fileTable = table;
                    _fileTableInitialized = true;
                    GordianLog.Info(LogCategories.Res, $"Loaded master file table with {table.Count} entries across base and expansions.");
                    return true;
                }
            }
            catch (Exception ex)
            {
                GordianLog.Error(LogCategories.Res, $"Failed to load master file tables: {ex.Message}");
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
                GordianLog.Error(LogCategories.Res, $"Failed to load DAT tree for {relativePath}: {ex.Message}");
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
                GordianLog.Error(LogCategories.Res, $"Failed to parse d_msg table {category}: {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// Queries a string by category and 0-based row index. Not for key items, whose rows are not ids: use
        /// <see cref="TryGetKeyItemName"/>.
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
        /// Retrieves a key item's name by key item id (the id in S2C 0x055 and in dialog key-item tags). The table is
        /// indexed by each record's own id, not its row (#89).
        /// </summary>
        public bool TryGetKeyItemName(uint keyItemId, out string name)
        {
            var table = GetDMsgTable(DMsgCategory.KeyItems);
            if (table != null && table.TryGetById(keyItemId, out var record) &&
                record.NamedFields.TryGetValue("name", out var found) && !string.IsNullOrEmpty(found))
            {
                name = found;
                return true;
            }

            name = string.Empty;
            return false;
        }

        /// <summary>
        /// A key item's plural name by key item id ("traverser stones"; the record's "plural" field, which repeats the
        /// name for key items without a plural), for the dialog tag kind 0x35.
        /// </summary>
        /// <summary>
        /// A weather's noun ("rain") or adjective ("rainy") by weather id, from the d_msg weather table (ROM/165/79, two
        /// strings a row; the dialog's 0x01 kinds 0x18 / 0x17, #125). The adjective falls back to the noun when empty.
        /// </summary>
        public bool TryGetWeatherName(int weatherId, bool adjective, out string name)
        {
            var table = GetDMsgTable(DMsgCategory.WeatherNames);
            if (table != null && weatherId >= 0 && weatherId < table.Count)
            {
                var subs = table.Records[weatherId].SubStrings;
                int sub = adjective && subs.Count > 1 && !string.IsNullOrEmpty(subs[1]) ? 1 : 0;
                name = subs.Count > sub ? subs[sub] : string.Empty;
                return !string.IsNullOrEmpty(name);
            }

            name = string.Empty;
            return false;
        }

        public bool TryGetKeyItemPlural(uint keyItemId, out string plural)
        {
            var table = GetDMsgTable(DMsgCategory.KeyItems);
            if (table != null && table.TryGetById(keyItemId, out var record) &&
                record.NamedFields.TryGetValue("plural", out var found) && !string.IsNullOrEmpty(found))
            {
                plural = found;
                return true;
            }

            return TryGetKeyItemName(keyItemId, out plural);
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

                    GordianLog.Info(LogCategories.Res, $"Loaded {loaded} items from {relativePath}");
                    return loaded;
                }
            }
            catch (Exception ex)
            {
                GordianLog.Error(LogCategories.Res, $"Failed to load items from {relativePath}: {ex.Message}");
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
                _ = _loadedItemTables.GetOrAdd(relPath, path => new Lazy<int>(() => LoadItemDat(path))).Value;
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
            DMsgCategory.ZoneNames => Path.Combine("ROM", "165", "84.DAT"),
            DMsgCategory.ZoneNamesShort => Path.Combine("ROM", "165", "83.DAT"),
            DMsgCategory.ZoneNamesCompact => Path.Combine("ROM", "165", "85.DAT"),
            DMsgCategory.RegionNames => Path.Combine("ROM", "165", "78.DAT"),
            DMsgCategory.WeatherNames => Path.Combine("ROM", "165", "79.DAT"),
            DMsgCategory.MiscStrings => Path.Combine("ROM", "165", "61.DAT"),
            DMsgCategory.MenuConfigRows => Path.Combine("ROM", "165", "74.DAT"),
            DMsgCategory.MenuHelp => Path.Combine("ROM", "165", "75.DAT"),
            DMsgCategory.MenuTitles => Path.Combine("ROM", "165", "76.DAT"),
            DMsgCategory.MenuMessages => Path.Combine("ROM", "165", "72.DAT"),
            DMsgCategory.MenuStrings => Path.Combine("ROM", "165", "77.DAT"),
            DMsgCategory.JobPointStrings => Path.Combine("ROM", "314", "62.DAT"),
            DMsgCategory.AlterEgoStrings => Path.Combine("ROM", "384", "121.DAT"),
            DMsgCategory.MeritStrings => Path.Combine("ROM", "169", "75.DAT"),
            DMsgCategory.JobGiftStrings => Path.Combine("ROM", "324", "59.DAT"),
            DMsgCategory.MountNames => Path.Combine("ROM", "351", "84.DAT"),
            DMsgCategory.MountHelp => Path.Combine("ROM", "351", "85.DAT"),
            DMsgCategory.QuestsSandoria => Path.Combine("ROM", "176", "60.DAT"),
            DMsgCategory.QuestsBastok => Path.Combine("ROM", "176", "61.DAT"),
            DMsgCategory.QuestsWindurst => Path.Combine("ROM", "176", "62.DAT"),
            DMsgCategory.QuestsJeuno => Path.Combine("ROM", "176", "63.DAT"),
            DMsgCategory.QuestsOther => Path.Combine("ROM", "176", "64.DAT"),
            DMsgCategory.QuestsOutlands => Path.Combine("ROM", "176", "65.DAT"),
            DMsgCategory.QuestsAhtUrhgan => Path.Combine("ROM", "176", "66.DAT"),
            DMsgCategory.QuestsAbyssea => Path.Combine("ROM", "242", "64.DAT"),
            DMsgCategory.QuestsCrystalWar => Path.Combine("ROM", "196", "6.DAT"),
            DMsgCategory.QuestsAdoulin => Path.Combine("ROM", "293", "70.DAT"),
            DMsgCategory.QuestsCoalition => Path.Combine("ROM", "293", "71.DAT"),
            DMsgCategory.MissionsSandoria => Path.Combine("ROM", "176", "67.DAT"),
            DMsgCategory.MissionsBastok => Path.Combine("ROM", "176", "68.DAT"),
            DMsgCategory.MissionsWindurst => Path.Combine("ROM", "176", "69.DAT"),
            DMsgCategory.MissionsZilart => Path.Combine("ROM", "176", "70.DAT"),
            DMsgCategory.MissionsCop => Path.Combine("ROM", "176", "71.DAT"),
            DMsgCategory.MissionsToau => Path.Combine("ROM", "176", "73.DAT"),
            DMsgCategory.MissionsWotg => Path.Combine("ROM", "196", "7.DAT"),
            DMsgCategory.MissionsAdoulin => Path.Combine("ROM", "293", "69.DAT"),
            DMsgCategory.MissionsRov => Path.Combine("ROM", "333", "4.DAT"),
            DMsgCategory.MissionsAssault => Path.Combine("ROM", "176", "72.DAT"),
            DMsgCategory.MissionsCampaign => Path.Combine("ROM", "196", "8.DAT"),
            DMsgCategory.MissionsAcp => Path.Combine("ROM", "222", "18.DAT"),
            DMsgCategory.MissionsAmk => Path.Combine("ROM", "223", "12.DAT"),
            DMsgCategory.MissionsAsa => Path.Combine("ROM", "223", "13.DAT"),
            DMsgCategory.MissionsTvr => Path.Combine("ROM", "364", "36.DAT"),
            _ => string.Empty
        };

        private static IReadOnlyList<string>? GetDMsgFieldNames(DMsgCategory category) => category switch
        {
            DMsgCategory.QuestsSandoria or DMsgCategory.QuestsBastok or DMsgCategory.QuestsWindurst or
            DMsgCategory.QuestsJeuno or DMsgCategory.MissionsSandoria or DMsgCategory.MissionsBastok or
            DMsgCategory.MissionsWindurst or DMsgCategory.MissionsZilart or DMsgCategory.MissionsCop or
            DMsgCategory.MissionsToau or DMsgCategory.MissionsWotg or DMsgCategory.MissionsAdoulin or
            DMsgCategory.MissionsRov or DMsgCategory.QuestsOther or DMsgCategory.QuestsOutlands or DMsgCategory.QuestsAhtUrhgan or
            DMsgCategory.QuestsAbyssea or DMsgCategory.QuestsCrystalWar or DMsgCategory.QuestsAdoulin or DMsgCategory.QuestsCoalition or
            DMsgCategory.MissionsAssault or DMsgCategory.MissionsCampaign or DMsgCategory.MissionsAcp or DMsgCategory.MissionsAmk or
            DMsgCategory.MissionsAsa or DMsgCategory.MissionsTvr => new[] { "id", "name", "description" },

            DMsgCategory.SpellHelp or DMsgCategory.AbilityHelp => new[] { "name", "help" },
            DMsgCategory.StatusNames => new[] { "name", "adjective" },
            DMsgCategory.WeatherNames => new[] { "name", "adjective" },
            // EN key items: sub 0 the key item id, sub 1 a number 1-4 of unknown meaning (not the category, which is
            // the row's place between separator rows), subs 2-3 empty text, then name, plural and description.
            DMsgCategory.KeyItems => new[] { "id", "unk1", "unk2", "unk3", "name", "plural", "description" },
            _ => new[] { "name" }
        };

        /// <summary>The item table holding an item id, from the table shared with <see cref="ItemNameResolver"/> (#203).</summary>
        internal static string GetItemTablePathForId(uint itemId) => Tables.ItemTables.GetTablePath(itemId);

        private readonly ConcurrentDictionary<int, World.Collision.ZoneCollisionMesh?> _collisionCache = new();

        /// <summary>
        /// Loads (once per zone) just the player-collision mesh of a zone, without its meshes, textures or effects, so
        /// every session, displayed or not, can follow the ground of the zone it is in. Null when unavailable.
        /// </summary>
        public World.Collision.ZoneCollisionMesh? TryLoadZoneCollision(int zoneId)
        {
            MarkZoneUsed(zoneId);
            if (_collisionCache.TryGetValue(zoneId, out var cached)) return cached;
            if (_zoneCache.TryGetValue(zoneId, out var zone)) return _collisionCache.GetOrAdd(zoneId, zone.Geometry.Collision);

            EnsureKeyTables();
            byte[]? datBytes = ReadZoneModelDat(zoneId);
            World.Collision.ZoneCollisionMesh? collision = null;
            if (datBytes != null && _keyTable1 != null)
            {
                // A voyage zone's player walks its vessel's deck (#403); every other zone its first ZoneDef.
                if (ZoneDataLoader.FindCollisionZoneDef(datBytes) is { } header)
                {
                    byte[] payload = datBytes.AsSpan(header.DataOffset, header.DataSizeBytes).ToArray();
                    int nodeCount = ZoneDefDecoder.DecryptZoneObjects(payload, _keyTable1);
                    collision = ZoneCollisionDecoder.Decode(payload);
                    if (collision != null)
                    {
                        var placements = ZoneDefDecoder.ParseZonePlacements(payload, nodeCount);
                        // Lift and zone-line records live in every 0x36 table; the door blockers come from the tables without lifts.
                        var allInteractions = ZoneInteractionDecoder.DecodeAllFromDat(datBytes);
                        collision.MovingPlatforms = ZoneDataLoader.CreateMovingPlatforms(datBytes, _keyTable1, _keyTable2 ?? Array.Empty<byte>(), placements, collision,
                                                                                         allInteractions);
                        collision.Doors = World.Collision.ZoneDoors.CreateBlockers(ZoneInteractionDecoder.DecodeDoorTablesFromDat(datBytes));
                        collision.ZoneLineVolumes = MogHouseRooms.ZoneLineVolumes(zoneId, allInteractions);
                        collision.SubAreaVolumes = World.Collision.ZoneSubAreas.Select(allInteractions);
                    }
                }
            }

            GordianLog.Info(LogCategories.Res, collision != null
                ? $"Loaded zone {zoneId} collision ({collision.TriangleCount} triangles)."
                : $"Zone {zoneId} has no collision mesh.");
            return _collisionCache.GetOrAdd(zoneId, collision);
        }

        private void EnsureKeyTables()
        {
            if (_keyTable1 == null || _keyTable2 == null)
            {
                if (ZoneDataLoader.TryExtractKeyTables(_gameDirectory, out var t1, out var t2))
                {
                    _keyTable1 = t1;
                    _keyTable2 = t2;
                }
                else
                {
                    GordianLog.Warn(LogCategories.Res, "Could not extract key tables from FFXiMain.dll — zone geometry will not be decrypted.");
                }
            }
        }

        /// <summary>
        /// Reads a zone's model DAT through the FTABLE, VFS or game directory; null when it cannot be found.
        /// </summary>
        private byte[]? ReadZoneModelDat(int zoneId)
        {
            int fileId = ZoneDataLoader.GetZoneModelFileId(zoneId);
            GordianLog.Debug(LogCategories.Res, $"ReadZoneModelDat({zoneId}): fileId={fileId}, ftable entries={_fileTable.Count}, gameDir='{_gameDirectory}'");

            byte[]? datBytes = null;

            if (TryResolveFile(fileId, out string fullPath) && File.Exists(fullPath))
            {
                GordianLog.Debug(LogCategories.Res, $"ReadZoneModelDat({zoneId}): resolved to '{fullPath}' via TryResolveFile.");
                datBytes = File.ReadAllBytes(fullPath);
            }
            else if (_fileTable.TryResolve(fileId, out string relPath))
            {
                GordianLog.Debug(LogCategories.Res, $"ReadZoneModelDat({zoneId}): FTABLE resolved fileId={fileId} -> '{relPath}', trying VFS/gameDir.");
                if (_vfs.TryResolveDat(relPath, out var resolved))
                {
                    datBytes = resolved!.ReadAllBytes();
                }
                else if (!string.IsNullOrEmpty(_gameDirectory))
                {
                    string p = Path.Combine(_gameDirectory, relPath);
                    if (File.Exists(p)) datBytes = File.ReadAllBytes(p);
                    else GordianLog.Warn(LogCategories.Res, $"ReadZoneModelDat({zoneId}): DAT file does not exist on disk: '{p}'");
                }
            }
            else
            {
                GordianLog.Warn(LogCategories.Res, $"ReadZoneModelDat({zoneId}): FTABLE has no entry for fileId={fileId}. Check that FTABLE.DAT loaded correctly.");
            }
            return datBytes;
        }

        private readonly ConcurrentDictionary<int, (ZoneGeometry Geometry, Dictionary<string, DecodedTexture> Textures)?> _interiorCache = new();
        private readonly ConcurrentDictionary<int, World.Collision.ZoneCollisionMesh?> _interiorCollisionCache = new();

        /// <summary>
        /// Loads (once) a building interior's DAT (<see cref="World.Collision.ZoneSubAreas.InteriorFileId"/>, #68): a small zone
        /// DAT already in its parent zone's space. Its point-light references name its own light table (the renderer moves them
        /// into free slots of the zone's). False when the file is missing or holds nothing.
        /// </summary>
        public bool TryLoadSubAreaInterior(int subAreaId, [NotNullWhen(true)] out ZoneGeometry? interior, out Dictionary<string, DecodedTexture> textures)
        {
            MarkInteriorUsed(subAreaId);
            var entry = _interiorCache.GetOrAdd(subAreaId, ReadInterior);
            interior = entry?.Geometry;
            textures = entry?.Textures ?? new Dictionary<string, DecodedTexture>(StringComparer.OrdinalIgnoreCase);
            return interior != null && interior.MeshGroups.Count > 0;
        }

        /// <summary>
        /// Reads a building interior again for a GPU upload when the cached copy's render data was released after an upload
        /// that has left the GPU since (#585): the new copy replaces it in the cache. When another reader already replaced
        /// it, that copy is returned; the cached copy as it is when its render data was not released.
        /// </summary>
        public bool TryReloadSubAreaInterior(int subAreaId, ZoneGeometry released, [NotNullWhen(true)] out ZoneGeometry? interior, out Dictionary<string, DecodedTexture> textures)
        {
            ArgumentNullException.ThrowIfNull(released);
            MarkInteriorUsed(subAreaId);
            if (_interiorCache.TryGetValue(subAreaId, out var cached) && cached is { } current && !current.Geometry.IsRenderDataReleased)
            {
                interior = current.Geometry;
                textures = current.Textures;
                return interior.MeshGroups.Count > 0;
            }
            var entry = ReadInterior(subAreaId);
            if (entry is { } read)
            {
                _interiorCache[subAreaId] = read;
                GordianLog.Info(LogCategories.Res, $"Loaded sub-area {subAreaId} interior again for the GPU (#585).");
            }
            interior = entry?.Geometry;
            textures = entry?.Textures ?? new Dictionary<string, DecodedTexture>(StringComparer.OrdinalIgnoreCase);
            return interior != null && interior.MeshGroups.Count > 0;
        }

        private (ZoneGeometry Geometry, Dictionary<string, DecodedTexture> Textures)? ReadInterior(int id)
        {
            EnsureKeyTables();
            int fileId = World.Collision.ZoneSubAreas.InteriorFileId(id);
            byte[]? datBytes = LoadDatBytesByFileId(fileId);
            if (datBytes == null || datBytes.Length == 0 || _keyTable1 == null) return null;
            var decoded = new Dictionary<string, DecodedTexture>(StringComparer.OrdinalIgnoreCase);
            var geometry = ZoneDataLoader.ParseZoneContainer(datBytes, 0, _keyTable1, _keyTable2 ?? ReadOnlySpan<byte>.Empty, decoded,
                GetSharedEffects(), datSource: Graphics.DecodedTexture.FileLabel(fileId), interior: true);
            _interiorCollisionCache.TryAdd(id, geometry.Collision);
            GordianLog.Info(LogCategories.Res, $"Loaded sub-area {id} interior (file {fileId}: {geometry.MeshGroups.Count} submeshes, {geometry.Collision?.TriangleCount ?? 0} collision triangles).");
            return (geometry, decoded);
        }

        /// <summary>
        /// A ship zone's vessel for a ship model drawn at a dock (#403, #585), read from a copy of the zone's DAT of its own
        /// that is not cached: the App uploads the vessel once per device and drops this copy, so a ship at a dock keeps
        /// nothing of its ship zone on the CPU, and the cached copy (shown and released by a window on the voyage) is never
        /// touched. False when the zone has no DAT or no vessel.
        /// </summary>
        public bool TryReadZoneVessel(int zoneId, [NotNullWhen(true)] out ZoneVessel? vessel, out Dictionary<string, DecodedTexture> textures)
        {
            textures = new Dictionary<string, DecodedTexture>(StringComparer.OrdinalIgnoreCase);
            vessel = null;
            EnsureKeyTables();
            byte[]? datBytes = ReadZoneModelDat(zoneId);
            if (datBytes == null || datBytes.Length == 0) return false;
            var zone = ZoneDataLoader.ParseZoneContainer(datBytes, zoneId, _keyTable1 ?? ReadOnlySpan<byte>.Empty, _keyTable2 ?? ReadOnlySpan<byte>.Empty,
                textures, GetSharedEffects(), datSource: Graphics.DecodedTexture.FileLabel(ZoneDataLoader.GetZoneModelFileId(zoneId)));
            vessel = zone.Vessel;
            if (vessel != null) GordianLog.Info(LogCategories.Res, $"Read the vessel of zone {zoneId} for a ship model ({vessel.MeshGroups.Count} submeshes).");
            return vessel != null;
        }

        /// <summary>A building interior's collision mesh alone (headless sessions walk into shops too, #68); null when none.</summary>
        public World.Collision.ZoneCollisionMesh? TryLoadSubAreaCollision(int subAreaId)
        {
            MarkInteriorUsed(subAreaId);
            if (_interiorCollisionCache.TryGetValue(subAreaId, out var cached)) return cached;
            EnsureKeyTables();
            World.Collision.ZoneCollisionMesh? collision = null;
            byte[]? datBytes = LoadDatBytesByFileId(World.Collision.ZoneSubAreas.InteriorFileId(subAreaId));
            if (datBytes != null && _keyTable1 != null && ZoneDataLoader.FindCollisionZoneDef(datBytes) is { } header)
            {
                byte[] payload = datBytes.AsSpan(header.DataOffset, header.DataSizeBytes).ToArray();
                ZoneDefDecoder.DecryptZoneObjects(payload, _keyTable1);
                collision = ZoneCollisionDecoder.Decode(payload);
            }
            return _interiorCollisionCache.GetOrAdd(subAreaId, collision);
        }

        /// <summary>A zone already loaded by <see cref="TryLoadZone"/>, without loading anything; null otherwise.</summary>
        public ZoneGeometry? TryGetLoadedZone(int zoneId) => _zoneCache.TryGetValue(zoneId, out var cached) ? cached.Geometry : null;

        /// <summary>
        /// Attempts to load and parse a zone's 3D terrain geometry and texture resources.
        /// </summary>
        public bool TryLoadZone(int zoneId, [NotNullWhen(true)] out ZoneGeometry? zone, out Dictionary<string, DecodedTexture> textures)
        {
            MarkZoneUsed(zoneId);
            if (_zoneCache.TryGetValue(zoneId, out var cached))
            {
                zone = cached.Geometry;
                textures = cached.Textures;
                return true;
            }
            return TryParseZone(zoneId, replacing: null, out zone, out textures);
        }

        /// <summary>
        /// Reads a zone's DAT and caches the result: when <paramref name="replacing"/> is given, only in place of that
        /// geometry (a copy whose render data was released, #576); a geometry cached meanwhile by another reader wins.
        /// </summary>
        private bool TryParseZone(int zoneId, ZoneGeometry? replacing, [NotNullWhen(true)] out ZoneGeometry? zone, out Dictionary<string, DecodedTexture> textures)
        {
            textures = new Dictionary<string, DecodedTexture>(StringComparer.OrdinalIgnoreCase);

            // Diagnostic: warn if FTABLE is empty (init failed or game dir not found)
            if (_fileTable.Count == 0)
            {
                GordianLog.Warn(LogCategories.Res, $"TryLoadZone({zoneId}): FileTable is empty — InitializeFileTable() may have failed or game directory not found.");
            }

            EnsureKeyTables();
            byte[]? datBytes = ReadZoneModelDat(zoneId);

            if (datBytes != null && datBytes.Length > 0)
            {
                zone = ZoneDataLoader.ParseZoneContainer(
                    datBytes,
                    zoneId,
                    _keyTable1 ?? ReadOnlySpan<byte>.Empty,
                    _keyTable2 ?? ReadOnlySpan<byte>.Empty,
                    textures,
                    GetSharedEffects(),
                    datSource: Graphics.DecodedTexture.FileLabel(ZoneDataLoader.GetZoneModelFileId(zoneId)));

                // Share one collision mesh per zone with sessions that loaded it on their own.
                if (_collisionCache.TryGetValue(zoneId, out var sharedCollision) && sharedCollision != null) zone.Collision = sharedCollision;
                else _collisionCache[zoneId] = zone.Collision;

                if (replacing == null)
                {
                    _zoneCache[zoneId] = (zone, textures);
                }
                else if (!_zoneCache.TryGetValue(zoneId, out var current) || !ReferenceEquals(current.Geometry, replacing) ||
                         !_zoneCache.TryUpdate(zoneId, (zone, textures), current))
                {
                    // Another reader replaced it first (or the purge dropped it): use what is cached, else this copy.
                    if (_zoneCache.TryGetValue(zoneId, out var winner) && !winner.Geometry.IsRenderDataReleased)
                    {
                        zone = winner.Geometry;
                        textures = winner.Textures;
                        return true;
                    }
                    _zoneCache[zoneId] = (zone, textures);
                }
                GordianLog.Info(LogCategories.Res, $"Loaded zone {zoneId} ({zone.MeshGroups.Count} submeshes, {textures.Count} textures){(replacing != null ? " again for the GPU (#576)" : string.Empty)}.");

                if (zone.MeshGroups.Count == 0)
                {
                    GordianLog.Warn(LogCategories.Res, $"TryLoadZone({zoneId}): DAT parsed but produced 0 submeshes. keyTable1={((_keyTable1 != null && _keyTable1.Length > 0) ? "OK" : "MISSING")}, keyTable2={((_keyTable2 != null && _keyTable2.Length > 0) ? "OK" : "MISSING")}");
                }

                return true;
            }

            zone = null;
            return false;
        }

        /// <summary>
        /// Loads (once) the shared ROM/0/0.DAT effects tree that zone particle generators fall back to.
        /// </summary>
        private SharedEffectResources? GetSharedEffects()
        {
            if (_sharedEffectsLoaded) return _sharedEffects;
            lock (_lock)
            {
                if (!_sharedEffectsLoaded)
                {
                    string sharedPath = Path.Combine("ROM", "0", "0.DAT");
                    byte[]? bytes = LoadDatBytes(sharedPath);
                    _sharedEffects = bytes != null && bytes.Length > 0 ? SharedEffectResources.Parse(bytes, sharedPath) : null;
                    if (_sharedEffects == null) GordianLog.Warn(LogCategories.Res, "Shared effects DAT ROM/0/0.DAT unavailable; generators linking shared effects will be skipped.");
                    _sharedEffectsLoaded = true;
                }
                return _sharedEffects;
            }
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
                GordianLog.Error(LogCategories.Res, $"Failed to load DAT bytes for '{relativePath}': {ex.Message}");
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
        /// The particle effects an NPC or monster model's DAT carries for its actor (e.g. the Home Point crystal, model 51),
        /// or null when the model has none. Cached per model id, including models without effects.
        /// </summary>
        public Gordian.Core.Resources.Graphics.ActorEffectSet? GetActorEffects(uint modelId)
        {
            if (modelId == 0) return null;
            return _actorEffectCache.GetOrAdd(modelId, id =>
            {
                int fileId = CharacterEquipmentResolver.GetMonsterFileId(id);
                var bytes = LoadDatBytesByFileId(fileId);
                return bytes == null ? null : Gordian.Core.Resources.Graphics.ActorEffectLoader.Load(bytes, GetSharedEffects(), Graphics.DecodedTexture.FileLabel(fileId));
            });
        }

        /// <summary>
        /// The effects a worn weapon's model DAT runs on its wearer while it is equipped (#8, the Afterglow of the REMA and
        /// Prime weapons: <see cref="ActorEffectSet.EquippedRoutines"/>), or null when the slot's DAT has none. Only the
        /// weapon slots (main, sub, ranged) carry them. <paramref name="modelId"/> is the grap table entry without its slot
        /// nibble. Cached per file id, including DATs without such effects.
        /// </summary>
        public Gordian.Core.Resources.Graphics.ActorEffectSet? GetGearEffects(CharacterRace race, CharacterSlot slot, ushort modelId)
        {
            if (modelId == 0 || slot is not (CharacterSlot.Main or CharacterSlot.Sub or CharacterSlot.Ranged) ||
                !CharacterEquipmentResolver.TryResolveGearFileId(race, slot, modelId, out int fileId))
            {
                return null;
            }
            return _gearEffectCache.GetOrAdd(fileId, id =>
            {
                var bytes = LoadDatBytesByFileId(id);
                var effects = bytes == null ? null : Gordian.Core.Resources.Graphics.ActorEffectLoader.Load(bytes, GetSharedEffects(), Graphics.DecodedTexture.FileLabel(id));
                return effects is { EquippedRoutines.Count: > 0 } ? effects : null;
            });
        }

        /// <summary>
        /// The particle effects of a cutscene scene resource DAT (the files the scheduler opcodes 0x45 / 0x9F load, see
        /// <see cref="Events.EventSceneResource"/>), camera-following generators included, or null when it has none.
        /// Cached per file id.
        /// </summary>
        public Gordian.Core.Resources.Graphics.ActorEffectSet? GetSceneEffects(int fileId)
        {
            if (fileId <= 0) return null;
            return _sceneEffectCache.GetOrAdd(fileId, id =>
            {
                var bytes = LoadDatBytesByFileId(id);
                if (bytes == null) return null;
                // Scene DATs can draw Section 0x2E meshes, encrypted like a zone's (#459).
                EnsureKeyTables();
                return Gordian.Core.Resources.Graphics.ActorEffectLoader.Load(bytes, GetSharedEffects(), Graphics.DecodedTexture.FileLabel(id), cameraEffects: true,
                    _keyTable1 ?? ReadOnlySpan<byte>.Empty, _keyTable2 ?? ReadOnlySpan<byte>.Empty);
            });
        }

        private SharedHitRoutines? _sharedHitRoutines;
        private int _sharedHitRoutinesState; // 0 not started, 1 loading, 2 done

        /// <summary>
        /// The shared hit routines of <c>ROM/0/0.DAT</c> (#328), or null while they load: the first call starts reading
        /// them on the thread pool, so the frame that hears the first hit does not wait for the file.
        /// </summary>
        public SharedHitRoutines? GetSharedHitRoutines()
        {
            if (Volatile.Read(ref _sharedHitRoutinesState) == 2) return _sharedHitRoutines;
            if (Interlocked.CompareExchange(ref _sharedHitRoutinesState, 1, 0) == 0)
            {
                System.Threading.Tasks.Task.Run(() =>
                {
                    try
                    {
                        byte[]? bytes = LoadDatBytes(Path.Combine("ROM", "0", "0.DAT"));
                        _sharedHitRoutines = bytes is { Length: > 0 } ? SharedHitRoutines.Parse(bytes) : null;
                    }
                    catch (Exception ex)
                    {
                        GordianLog.Warn(LogCategories.Res, $"Shared hit routines not read: {ex.Message}");
                    }
                    Volatile.Write(ref _sharedHitRoutinesState, 2);
                });
            }
            return null;
        }

        private (Events.EventSceneResource Routines, Gordian.Core.Resources.Graphics.ActorEffectSet Effects)? _sharedRoutineEffects;
        private bool _sharedRoutineEffectsLoaded;

        /// <summary>
        /// The routines and particle generators of the shared effects DAT <c>ROM/0/0</c> that actors' motion routines link
        /// (#132: the chant circles <c>ner1</c> / <c>ner2</c> / <c>nefa</c>, the release flashes <c>eis3</c> / <c>eis4</c>, the
        /// weapon skill flash <c>eis1</c>, the stops <c>stwh</c> / <c>stbk</c>), or null when it cannot be read. Loaded once.
        /// </summary>
        public (Events.EventSceneResource Routines, Gordian.Core.Resources.Graphics.ActorEffectSet Effects)? GetSharedRoutineEffects()
        {
            if (Volatile.Read(ref _sharedRoutineEffectsLoaded)) return _sharedRoutineEffects;
            lock (_lock)
            {
                if (!_sharedRoutineEffectsLoaded)
                {
                    string sharedPath = Path.Combine("ROM", "0", "0.DAT");
                    byte[]? bytes = LoadDatBytes(sharedPath);
                    var effects = bytes is { Length: > 0 } ? Gordian.Core.Resources.Graphics.ActorEffectLoader.Load(bytes, GetSharedEffects(), sharedPath, cameraEffects: true) : null;
                    _sharedRoutineEffects = effects != null ? (Events.EventSceneResource.Parse(bytes), effects) : null;
                    Volatile.Write(ref _sharedRoutineEffectsLoaded, true);
                }
                return _sharedRoutineEffects;
            }
        }

        // Generations are drawn from one counter for the whole process, so no two resource managers (or two states of one)
        // ever share a number: a holder keyed by the generation alone (GPU model keys, the stock UI library, item icons,
        // footwear) rebuilds when the app switches installs (#665) just as it does after a VFS reload.
        private static int s_lastGeneration;
        private int _cacheGeneration = Interlocked.Increment(ref s_lastGeneration);

        /// <summary>
        /// Changes each time <see cref="ClearCache"/> runs, after the caches are cleared, and is unique across every
        /// resource manager in the process. Holders of data built from cached resources (GPU copies of entity models, the
        /// loaded zone, the stock UI library) watch it and rebuild, which also covers a switch to another install (#665).
        /// </summary>
        public int CacheGeneration => Volatile.Read(ref _cacheGeneration);

        /// <summary>
        /// Resets the process-wide caches of DAT data that are not owned by one resource manager: the action motion
        /// library, the zone entity names and the zone DAT loader's generation (the client message tables follow it).
        /// <see cref="ClearCache"/> runs it; so does the app when it switches installs (#665).
        /// </summary>
        public static void ResetSharedCaches()
        {
            Gordian.Core.Animation.ActionMotionLibrary.Shared.Clear();
            ZoneEntityNames.Reset();
            ZoneDatLoader.Invalidate();
        }

        /// <summary>
        /// Stops following VFS reloads and disposes the VFS this manager created (its hot reload watchers). Used when the
        /// app switches installs (#665); nothing may use the manager afterwards.
        /// </summary>
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            _vfs.OnReloaded -= ClearCache;
            if (_ownsVfs) _vfs.Dispose();
        }

        /// <summary>
        /// Drops everything read from DATs so the next request reads the files again; runs on every VFS reload. A file
        /// table that was loaded is rebuilt first (the old one stays in use if that fails), since nothing refills it lazily.
        /// </summary>
        public void ClearCache()
        {
            if (_fileTableInitialized && !InitializeFileTable())
            {
                GordianLog.Warn(LogCategories.Res, "Could not rebuild the file table after a VFS reload; keeping the previous one.");
            }
            _zoneCache.Clear();
            _collisionCache.Clear();
            _interiorCache.Clear();
            _interiorCollisionCache.Clear();
            _zoneLastUse.Clear();
            _interiorLastUse.Clear();
            _entityModelCache.Clear();
            ClearEntityModelCaches();
            ClearMountCaches();
            _actorEffectCache.Clear();
            _sceneEffectCache.Clear();
            _gearEffectCache.Clear();
            lock (_lock)
            {
                _sharedRoutineEffects = null;
                _sharedRoutineEffectsLoaded = false;
            }
            _itemCache.Clear();
            _loadedItemTables.Clear();
            _dmsgCache.Clear();
            lock (_lock)
            {
                _sharedEffectsLoaded = false;
                _sharedEffects = null;
            }
            ResetSharedCaches();
            Volatile.Write(ref _cacheGeneration, Interlocked.Increment(ref s_lastGeneration));
            GordianLog.Info(LogCategories.Res, "Resource caches cleared; DATs will be read again.");
        }
    }
}
