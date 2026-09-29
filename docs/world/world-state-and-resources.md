# World State, DAT Resources & VFS

> Entity store, game state caches, DAT decoders and the modular virtual file system (Phase 4). Runtime counters are described in [PERFORMANCE_TELEMETRY.md](../PERFORMANCE_TELEMETRY.md). Status and open work: [ROADMAP.md](../../ROADMAP.md) and GitHub Issues.

## Entity store and state caches

- **Thread-Safe Spatial Entity Store (`Gordian.Core/World`):**
  - Tracking for LocalPlayer, other PCs, NPCs, Monsters, Pets, and Trusts
  - Indexing by Server ID (`uint32`) and Zone Target Index (`uint16`)
  - Fast 3D spatial partitioning (Uniform Grid / BVH) for distance, cone, and line-of-sight queries
  - Dead-reckoning & position interpolation between 250ms network ticks
- **Game State Caches:**
  - Multi-container inventory cache (Inventory, Wardrobes 1-8, Satchel, Sack, Case, Safe, Storage)
  - Active player vitals (HP/MP/TP), base attributes, equipment loadout, buff/debuff timers
  - Server answers the UI will read later ([#5](https://github.com/jimmy58663/GordianXI/issues/5)); each raises its state's change event:

    | Packet | Cache |
    |---|---|
    | `0x030` crafting animation | `CombatState.TryGetCraftEffect(serverId)`, per entity |
    | `0x04C` Auction House | `InventoryState.LastAuctionResponse`; `SnapshotAuctionSlots()` keeps the latest answer per sale slot (work index ≥ 0) |
    | `0x082` / `0x084` guild buy / sell | `InventoryState.LastGuildTransaction` (`ItemId` 0 = failed, `Trade` gives the reason) |
    | `0x085` guild sell list | `InventoryState.SnapshotGuildSellList()`; packet 0 (`Stat & 0x3F`) starts a new list |
    | `0x106` bazaar purchase | `InventoryState.LastBazaarPurchase` |
    | `0x108` bazaar visitors | `InventoryState.SnapshotBazaarVisitors()`: Enter adds, anything else removes |
    | `0x109` / `0x10A` bazaar sales | `InventoryState.LastBazaarSlotSold`; `SnapshotBazaarSales()` keeps the last 50 |
    | `0x116` / `0x117` equipment sets | `InventoryState.SnapshotEquipsetValidation()` (17 entries); `LastEquipsetResult.FailedItems` are changed pieces not now worn |
    | `0x0E0` linkshell comlink | `PartyState.GetLinkshellItem(slot)`: the item's container and index |
    | `0x11D` party join requests | `PartyState.SnapshotJoinRequests()`: Status 0 adds, 1 removes |
    | `0x036` / `0x02A` zone dialog | `ProgressionState.LastDialogMessage` |
    | `0x0FA` Mog House operation | `ProgressionState.LastMyRoomOperation` |

## DAT decoders

- **FFXI DAT Binary Decoders (`Gordian.Core/Resources`):**
  - Clean-room decoders for ROM directory DAT files (string tables, item tables, spell/ability tables)
  - Zone collision meshes, terrain geometry, entity models, and animation tables
- **Container & Chunk Architecture (`DatSectionHeader`):**
  - Standard 16-byte chunk header (`DatSectionHeader`): 4-character identifier tag (`DatId`), 7-bit section type (`DatSectionType`), 19-bit size in 16-byte units (`SizeBytes = ((dword >> 7) & 0x7FFFF) * 16`), 6-bit flags.
  - Common section types: `0x00 End`, `0x01 Directory`, `0x04 Table`, `0x05 ParticleGenerator`, `0x07 EffectRoutine`, `0x19 ParticleKeyFrameData`, `0x1C ZoneDef`, `0x1F ParticleMesh`, `0x20 Texture`, `0x21 SpriteSheetMesh`, `0x25 WeightedMesh`, `0x29 Skeleton`, `0x2A SkeletonMesh`, `0x2B SkeletonAnimation`, `0x2E ZoneMesh`, `0x2F Environment`, `0x30 UiMenu`, `0x31 UiElementGroup`, `0x36 ZoneInteractions`, `0x3D SoundEffectPointer`.
- **File Table Resolution (`FileTableResolver`):**
  - Maps numeric client File IDs to physical paths using `FTABLE` (ushort per file ID) and `VTABLE` (byte per file ID indicating ROM root: 0 = unmapped, 1 = `ROM`, >1 = `ROM{rom}`).
  - Bit-packed fields: `subDir = ftVal >> 7`, `fileNum = ftVal & 0x7F`, resolving to canonical path `ROM{rom}/{subDir}/{fileNum}.DAT`.
- **Item Tables (`ItemTableDecoder`, `ItemNameResolver`):**
  - Circular bit-rotation decryption: circular left-shift by 3 bits `(b << 3) | (b >> 5)` across all record bytes.
  - Auto-detected record strides: `0xC00` (3,072 bytes, legacy private server / pre-Sept 2026) vs `0x1400` (5,120 bytes, modern retail). Icon image offset at `+0x280`, record terminator byte `0xFF`.
  - Item ID to DAT mapping: 0–4095 General 1 (`ROM/118/106.DAT`), 4096–8191 Consumables (`ROM/118/107.DAT`), 8192–8703 Automaton (`ROM/118/110.DAT`), 8704–10239 General 2 (`ROM/301/115.DAT`), 10240–16383 Armor 1 (`ROM/118/109.DAT`), 16384–23039 Weapons 1 (`ROM/118/108.DAT`), 23040–28671 Armor 2 (`ROM/286/73.DAT`), 28672–32767 Weapons 2 (`ROM/286/74.DAT`).
- **String Tables (`DMsgStringTable`):**
  - Container signature `d_msg` at byte 0. Decryption via XOR `0xFF` when the header byte at `+0x0A` is non-zero (otherwise plain). Fixed and variable stride indexing, CP932 / Shift-JIS text decoding, elemental glyph translation (Fire, Ice, Wind, Earth, Lightning, Water, Light, Dark).
  - Each record is a sub-entry count, then (offset, kind) u32 pairs. Kind 0 is text (a u32 `1`, 0x18 bytes of metadata, the NUL-terminated string); kind 1 is a number (the u32 at the offset, 4 bytes). Reading "value 1 means text" instead turns a numeric 1 into an empty string (key item 1, quest 1). Settled on the retail EN key-item, quest, mission, title, status, spell, ability, job and zone-name tables (2026-09-28, #89): every kind-1 entry is 4 bytes, every kind-0 entry starts with `1`. `DMsgRecord.TryGetNumber` reads numeric sub-entries.
  - Key items (`ROM/175/35`, EN, fixed stride 700, 3,244 rows) are sparse and stored in category order, so the row is not the id: sub 0 holds the key item id, sub 1 a number 1-4 of unknown meaning, subs 2-3 empty, sub 4 name, 5 plural, 6 description. Categories are the runs of rows between id-0 separator rows named `-Category` (xi-tools `docs/keyitems/categories.md`). `ResourceManager.TryGetKeyItemName` looks names up through `DMsgStringTable.TryGetById` (checked: 1 Zeruhn report, 8 airship pass, 512 Moghancement: Fire, 1271 traverser stone, 3072 Chocobo companion; mount key items carry a leading `♪`).
- **Zone dialog tables and event scripts (`ZoneDialogTable`, `EventMessageDecoder`, `ZoneEventScript`, `Resources/Tables` and `Resources/Events`):** the per-zone text NPC events print (file id 6420 + zone, XOR 0x80 body, dword offset table, control-coded strings) and the per-zone compiled event byte code (file id 5820 + zone, one block per actor). Formats, codes and the VM that runs them are in [docs/ui/stock-ui.md](../ui/stock-ui.md#dialog-text-chunk-6).

## Virtual file system and asset overrides

- **Modular Virtual File System (VFS) & Asset Overrides (XIPivot Architecture):**
  - Tiered VFS search paths (portable local root + `%LOCALAPPDATA%` user storage)
  - XIPivot-compatible legacy DAT overlay scanner (`resources/dats/`) with uppercase invariant indexing
  - Modern asset pack scanner (`resources/assets/`) with `manifest.json` parsing and DAT aliasing (`.glb` replacing `.DAT`)
  - Master `vfs.json` load order, priority stacking, and pack auto-discovery
  - Safe debounced runtime hot-reloading with master toggle
  - Modder documentation and reference manifests distributed in `resources/`

## State and memory telemetry

- **State & Memory Health Telemetry:**
  - Managed heap & GC pressure counters (.NET 10 Gen 0/1/2 collection tracking, heap allocation velocity)
  - Spatial partition & uniform grid query duration tracking with entity dead-reckoning cycle time benchmarking
