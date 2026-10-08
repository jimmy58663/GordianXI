# DAT File Registry

> Index of the retail DAT files GordianXI reads: how a file id becomes a path, which ids and paths each subsystem uses, the section types inside the files, and the per-format encodings. Formats, calibrations and evidence stay in the subsystem docs linked from each row; this file only points at them. Decoder overview: [world-state-and-resources.md](world-state-and-resources.md#dat-decoders).

Paths below use `/` for readability; the code builds them with `Path.Combine`. Path examples were resolved against the retail install's file tables on 2026-10-01 unless a row gives another date.

## File ids and paths

### How a file id resolves (`FileTableResolver`)

- **Tables.** `ResourceManager.InitializeFileTable` reads `FTABLE.DAT` / `VTABLE.DAT` from the game root, then `ROM{n}/FTABLE{n}.DAT` / `ROM{n}/VTABLE{n}.DAT` for n = 2-10 where present. Each pair goes through the VFS first (so a pack can override the tables), then the game directory.
- **Per id.** VTABLE holds one byte per file id: 0 = not mapped, 1 = `ROM`, n > 1 = `ROM{n}`. FTABLE holds one u16 per file id: directory = value >> 7, file = value & 0x7F. The path is `ROM{n}/{directory}/{file}.DAT`, so a directory holds at most 128 files. Format referenced from xi-model-viewer and xi-tools (`xi/ftable/xi_core.py`), cited in the `FileTableResolver` doc comment.
- **Merge rule.** The first table that maps an id keeps it (`TryAdd`). On retail this never decides anything: every table has 109,701 slots, the base VTABLE marks 82,919 ids as `ROM`, and each expansion VTABLE marks only its own ids (ROM2 681, ROM3 796, ROM4 666, ROM5 266, ROM6 84, ROM7 66, ROM8 67, ROM9 608; no ROM10). The sets do not overlap (checked 2026-10-01).
- **Loading.** `ResourceManager.LoadDatBytesByFileId(id)` resolves the id, then `LoadDatBytes(path)` tries the VFS overlay ([VFS](world-state-and-resources.md#virtual-file-system-and-asset-overrides)) and then the game directory. Code that knows a path calls `LoadDatBytes` directly.
- **Three ways to name a file.** The code uses:
  - file ids, resolved through the tables;
  - fixed `ROM/...` paths (item tables, `d_msg` tables, race skeletons, battle packs, `ROM/0/0`);
  - motion file numbers, `folder * 1000 + file` (Hume male emotes 32040 = `ROM/32/40`). `CharacterEquipmentResolver.MotFileNoToPath` converts them and carries files past 127 into the next folder (after xi-model-viewer `motFileNoToPath`). A motion file number is not a file id: 32040 is file id 10087.

### Zone files

Zone z, where 0 ≤ z ≤ 299. The formulas switch at zone 256 (Western Adoulin).

| File id or range | Path example | Contents | Decoder | Source |
|---|---|---|---|---|
| 100 + z; 83891 (0x147B3) + (z - 256) | 330 = `ROM/1/31` (Southern San d'Oria, 230); 83891 = `ROM9/0/3` (256) | Zone model container: 0x2E meshes, 0x1C placements, lights and collision, 0x20 textures, 0x2F environments, effects (0x05 / 0x07 / 0x19 / 0x1F / 0x21) | `ZoneDataLoader.GetZoneModelFileId`, `ParseZoneContainer`; collision only: `ResourceManager.TryLoadZoneCollision` | xi-model-viewer, xi-tools; [viewport-and-terrain.md](../rendering/viewport-and-terrain.md#camera-and-zone-terrain-phase-5b), [collision-and-physics.md](collision-and-physics.md) |
| 5820 + z; 84991 + (z - 256) | 6050 = `ROM/21/39` (230, 502 blocks); 84991 = `ROM9/5/53` | Compiled event scripts, one block per actor | `ZoneEventScript` | XiEvents "Event DAT Files.md"; [stock-ui.md](../ui/stock-ui.md#dialog-text-chunk-6) |
| 6420 + z; 85591 + (z - 256) (EN). 6120 + z; 85291 + (z - 256) (JP) | 6650 = `ROM/25/39` (230, 16,941 messages); 85591 = `ROM9/5/101` | Zone dialog table | `ZoneDialogTable`, `EventMessageDecoder` | XiEvents; [stock-ui.md](../ui/stock-ui.md#dialog-text-chunk-6) |
| 6720 + z; 86491 + (z - 256) | 6950 = `ROM/27/39` (230); 86491 = `ROM9/6/45` | Zone entity list: NPC names by server id (32-byte records) | `ZoneEntityList` | XiEvents "Zone Entities" |

Checked: every ROM path in XiEvents' zone list resolves to these ids in the retail file table (2026-09-28). The first 256-299 formulas were 256 ids too low ([#71](https://github.com/jimmy58663/GordianXI/issues/71)).

### Event resources

| File id or range | Path example | Contents | Decoder | Source |
|---|---|---|---|---|
| 30704 + p (p < 300); 56641 + p (300-599); 70347 + p (600+); 51183 + p (0x9F) | 30704 = `ROM/61/111` (`evte`) | Cutscene scene resource: 0x06 camera routes, 0x07 shot / fade / effect routines, generators | `EventSceneResource.GetFileId`, `GetSecondFileId`, `Parse`; generators `ResourceManager.GetSceneEffects` | XiEvents OpCodes/0x0045, xi-tools `docs/events/camera_scene_ids.md`; [stock-ui.md](../ui/stock-ui.md#dialog-text-chunk-6), [viewport-and-terrain.md](../rendering/viewport-and-terrain.md#camera-and-zone-terrain-phase-5b) |
| 32104 + n (n < 512); 49135 + n (< 1024); 56345 + n (< 2048); 59739 + n (< 3072); 66339 + n | 32104 = `ROM/68/76` (`mot_`); 49647 = `ROM2/0/73` | Event motion bank: gesture routines (0x07) and clips (0x2B) loaded by opcode 0x5B | `EventVm.MotionBankFileId`, `EventMotionBank` | XiEvents OpCodes/0x005B; [entities-and-animation.md](entities-and-animation.md#transient-combat-and-action-animation-phase-5d1) (Event gestures) |
| 32360 + 2n, else 32712 + n (n < 70); 61171 + n (70-139); 87685 + n (140-209); 102029 + n (210-279); the 0x5B bands from 280 | 32360 = `ROM/70/73`; 32712 = `ROM/72/67`; 61241 = `ROM/189/43` (Hume male `atp0`), 61281 = `ROM/189/83` (Tarutaru); 87825 = `ROM/293/99` (Hume male `orz0` / `mab0`), 87834 = `ROM/293/108` (Hume male `tlk0`), 87894 = `ROM/294/40` (Galka `tlk0`); 102239 = `ROM/339/41` (Hume male `uku0`), 102242 = `ROM/339/44` (`fyu0`-`fyu3`), 102308 = `ROM/339/110` | Player-model motion package of opcode 0x66; 70-279 are three tables of ten per player race (their waist-part twins follow each table 70 files on: 61311, 87895 and 87965, 102309; not read). From 280 on the scripts' numbers are 0x5B bank numbers (provisional). **Beyond XiEvents** (it names ReadTpcEventMotionRes, not its files) | `EventMotionBank.PackageFiles`, `RaceSetFileId` | [events/schedulers-and-motions.md](../events/schedulers-and-motions.md); [#209](https://github.com/jimmy58663/GordianXI/issues/209), [#228](https://github.com/jimmy58663/GordianXI/issues/228) |
| 7033, 7037 | 7033 = `ROM/27/78` (246,240 bytes, zones 0-99); 7037 = `ROM/188/67` (336,960 bytes, zones 100+) | Weather forecasts event opcode 0x72 reads: no header, 6,480-byte blocks (38 / 52), each 2,160 days x (normal, common, rare) weather id bytes, 0xFF = none; the zone-to-block tables are the client's, ours derived by matching LandSandBoat's `zone_weather` table (Adoulin fields provisional). **Beyond XiEvents / xi-tools** (layout unverified there) | `WeatherForecastFile.Parse`, `TryGetForecast`; `EventVm.ExecWeatherForecast` | XiEvents OpCodes/0x0072; LandSandBoat `sql/zone_weather.sql`; [events/vm.md](../events/vm.md#weather-forecast-0x72) ([#125](https://github.com/jimmy58663/GordianXI/issues/125)) |

### Entity models and motions

| File id or range | Path example | Contents | Decoder | Source |
|---|---|---|---|---|
| model + 1300 (< 1500); + 50295 (< 3000); + 96907 (< 3193); + 98546 (3193+) | model 51 → 1351 = `ROM/3/25` (Home Point crystal); 3000 → 99907 = `ROM/309/20` | NPC, monster and trust model: skeleton, meshes, clips, routines, textures, effects | `CharacterEquipmentResolver.GetMonsterFileId`, `EntityModelLoader.LoadMonsterModel` | xi-model-viewer; [entities-and-animation.md](entities-and-animation.md#models-and-equipment-phase-5c) |
| Per race and slot: a list of (base, count) groups, file = group base + offset within the group | Hume male face 7080 = `ROM/27/87`; head 7112 = `ROM/27/103`, later groups from 63323 = `ROM/229/98` | PC face and gear meshes (Face, Head, Body, Hands, Legs, Feet, Main, Sub, Ranged) | `CharacterEquipmentResolver.TryResolveGearFileId`, `EntityModelLoader.AssembleCharacter` | xi-model-viewer |
| Child races: slot base + model id (64 per slot) | girl face 29592 = `ROM/61/36`; Mithra kitten face 30376 = `ROM/61/95` | NPC-only child outfits (no weapons) | `CharacterEquipmentResolver.ChildGearBases` | xi-model-viewer `characters.json`; [entities-and-animation.md](entities-and-animation.md#models-and-equipment-phase-5c) |
| By path | `ROM/27/82` (Hume M, id 7072), `ROM/32/58`, `ROM/37/31`, `ROM/42/4`, `ROM/46/93` (both Tarutaru), `ROM/51/89`, `ROM/56/59`; children `ROM/61/58`, `ROM/61/85`, `ROM/61/110` | Race skeleton and lower-body locomotion clips; the upper-body pack is the next file, the waist pack three files on (folder carry at 128) | `GetBaseSkeletonPath`, `GetLocomotionPackPaths` | xi-model-viewer `pclists.js` |
| By path, per race and weapon animation type (0x45 Info byte 3 of the main weapon) | `ROM/32/13` (Hume M hand-to-hand, id 9799); extra types in `ROM/98`, `ROM/99` | Battle motion pack: swings, draw / sheathe routines and clips | `GetBattlePackPath` (by path only: a fallback that passed the pack's motion file number as a file id was removed, #203; 32013 is unmapped, 37013 is `ROM/95/48`, an unrelated file) | xi-model-viewer `characters.json`; [entities-and-animation.md](entities-and-animation.md#transient-combat-and-action-animation-phase-5d1) |
| Motion file numbers 32040, 37013, 41114, 46075, 51037, 51071, 56041, 61008 (race order HM, HF, EM, EF, TM, TF, Mithra, Galka) | 32040 = `ROM/32/40`; waist part 32046 = `ROM/32/46` | Emotes: six files per race, routines `em00`-`em07`; slot n is file n / 8, routine n % 8 | `EmoteMotion` | xi-tools `_EMOTE_MOTION_FILE`; [entities-and-animation.md](entities-and-animation.md#transient-combat-and-action-animation-phase-5d1) (Emotes) |
| 0 | `ROM/0/0` | Shared effects tree `syst/effe`: sprite sheets, textures and routines that zone and actor effects link to | `SharedEffectResources` (loaded by path) | xi-model-viewer `particle/system.js`; [sky-and-weather.md](../rendering/sky-and-weather.md#night-sky-stars-and-moon-chunk-3) |

The routines and clips inside the emote files were read from the retail DATs (2026-10-01); xi-tools gives only the file numbers.

### Item tables

All loaded by path through one list, `ItemTables` (shared by `ItemNameResolver` and `ResourceManager.TryGetItem` since [#203](https://github.com/jimmy58663/GordianXI/issues/203)), decoded by `ItemTableDecoder`. Source: xi-model-viewer, LandSandBoat and, from 28672 on, xi-tools (`docs/reference/named-dats.md` and its item parser's table list). Records and encoding: [world-state-and-resources.md](world-state-and-resources.md#dat-decoders). Each table's first record was checked to hold its start id in the retail install (2026-10-03, `ItemTablesTests`; General 1's record 0 is item 0, which the decoder skips).

| Item ids | Path | File id |
|---|---|---|
| 0-4095 General 1 | `ROM/118/106` | 73 |
| 4096-8191 Consumables | `ROM/118/107` | 74 |
| 8192-8703 Automaton | `ROM/118/110` | 77 |
| 8704-10239 General 2 (8704 Bismuth Ingot ...) | `ROM/301/115` | 55671 |
| 10240-16383 Armor 1 | `ROM/118/109` | 76 |
| 16384-23039 Weapons 1 | `ROM/118/108` | 75 |
| 23040-28671 Armor 2 | `ROM/286/73` | 55668 |
| 28672-29695 Moblin Maze Mongers (28672 Maze Tabula M01 ...) | `ROM/217/21` | 55667 |
| 29696-30719 Monstrosity 1 | `ROM/288/80` | 55670 |
| 30720-31743 General 7 (new in the 10 September 2026 update, all placeholders) | `ROM/387/14` | 55675 |
| 61432-61439 General 3 | `ROM/314/89` | 95 |
| 61440-61951 Monstrosity 2 | `ROM/288/67` | 55669 |
| 62976-62995 General 4 | `ROM/320/26` | 55504 |
| 63008-63023 General 5 | `ROM/332/49` | 55678 |
| 63024-63263 General 6 | `ROM/332/48` | 55677 |
| 65535 Gil | `ROM/174/48` | 91 |

`ItemNameResolver` names 65535 "Gil" without reading the file. Ids 31744-61431, 61952-62975 and the other gaps have no item table (57344-61431 and 61952-62975 hold Records of Eminence objectives and categories, xi-tools). Monstrosity 1 records load, but `ItemTableDecoder` reads no names from them (not investigated).

**Differs from the earlier GordianXI table:** it put 28672-32767 "Weapons 2" in `ROM/286/74`. That file (9,437,184 bytes, the old 0xC00 stride) holds ids 25600-28671, overlapping Armor 2; it is not an item table for 28672 on (checked 2026-10-03).

### String tables (`d_msg`)

All loaded by path (`ResourceManager.GetDMsgRelativePath`) and decoded by `DMsgStringTable`. Source: xi-model-viewer and xi-tools (`xi_database.py`). Record layout, the kind 0 / kind 1 sub-entries and the key item id lookup: [world-state-and-resources.md](world-state-and-resources.md#dat-decoders).

| Table (`DMsgCategory`) | Path | File id |
|---|---|---|
| Abilities / ability help | `ROM/181/72` / `ROM/181/74` | 55701 / 55733 |
| Spells / spell help | `ROM/181/73` / `ROM/181/75` | 55702 / 55734 |
| Status names | `ROM/180/102` | 55732 |
| Titles | `ROM/180/78` | 55704 |
| Jobs | `ROM/165/86` | 55467 |
| Key items | `ROM/175/35` | 55714 |
| Zone names / short zone names | `ROM/165/84` / `ROM/165/83` | 55465 / 55661 |
| Compact zone names (`DMsgCategory.ZoneNamesCompact`) | `ROM/165/85` | 55466 |
| Weather names by weather id (`DMsgCategory.WeatherNames`, 20 rows of noun and adjective: 0 "fine patches" / "fine", 1 "sunshine" / "sunny" ... 19 "darkness" / "dark"; the dialog's 0x01 kind `18` names the noun, `17` the adjective) | `ROM/165/79` | 55657 |
| Quests San d'Oria, Bastok, Windurst, Jeuno | `ROM/176/60`-`63` | 55706-55709 |
| Missions San d'Oria, Bastok, Windurst, Zilart, CoP | `ROM/176/67`-`71` | 55715-55719 |
| Missions ToAU | `ROM/176/73` | 55721 |
| Missions WotG / Adoulin / RoV | `ROM/196/7` / `ROM/293/69` / `ROM/333/4` | 55723 / 55738 / 56281 |
| Menu config rows / help / window titles (EN; JP is 120 ids lower) | `ROM/165/74` / `75` / `76` | 55650 / 55651 / 55652 |

The three menu tables are research only: nothing reads them at run time yet ([stock-ui.md](../ui/stock-ui.md#menu-string-tables-19)); the Font Colors rows (config rows 63-88, 197, 204-205; `StockUiFontColors`), the Log page rows (36-62, 196) and the Effects rows (153-170; both `StockUiConfigPages`) are typed in code word for word from ROM/165/74. **Beyond xi-tools:** xi-tools points at the `XISTRING` tables in `ROM/97` (help `ROM/97/42` = id 54, titles `ROM/97/41` = id 53). Those are an older snapshot that lacks the ids current menus use; the current tables are in `ROM/165`.

### Client message tables

Both in the zone dialog table format (`ZoneDialogTable`) and read by `ClientMessageTables` through the file-id loader ([#110](https://github.com/jimmy58663/GordianXI/issues/110)); codes in [events/message-codes.md](../events/message-codes.md#client-message-tables-110).

| File id | Path | Contents | Decoder | Source |
|---|---|---|---|---|
| 7031 (EN), 7030 (JP) | `ROM/27/76`, `ROM/27/75` | System messages: 326, ids = LandSandBoat `MsgStd` (2 "You could not enter the next area.", 88 the `/random` roll, 117 "Event skipped.") | `ClientMessageTables.SystemMessages`; printed for S2C 0x053 | XiPackets `world/server/0x0053` |
| 7027 | (file id; path not checked) | Basic messages, the ids of S2C 0x029 (LandSandBoat `MsgBasic`): 38 "'s ... skill rises ...", 53 "'s ... skill reaches level ..." in codes for the skill name and the 0.1 value that are not decoded; `CombatLogFormatter` words them by hand | none yet; found 2026-10-07 by searching file ids 6000-8000 for "skill rises" | retail DAT |
| 49 | `ROM/0/49` (file id; path not checked) | Client string table: plain NUL separated ASCII after an offset table, including the synthesis results ("Synthesis canceled. That combination of materials cannot be synthesized." ...), "Too many materials.", "Dispose of this item?" | `CraftingLog.ResultText` repeats the texts | found 2026-10-07 by searching every file id for the text; **Beyond XiPackets:** it describes the result texts but not where the client keeps them |
| Message table 7031, ids 160-168, 200-205 | `ROM/27/76` | 160-163 "You synthesized ..." by grade, 164 "... was lost.", 165 / 166 "You buy ... from the guild.", 167 / 168 "You sell ... to the guild.", 169 "You obtained ...", 200-203 "<name> synthesized ...", 204 "<name> lost ...", 205 "<name> obtained ..." (item parameters: 0x04 count, 0x2A count and item, 0x27 item) | `ClientMessageController.FormatTableMessage`, `CraftingLog`, `GuildShopLog` | XiPackets `world/server/0x006F`, `0x0070`; read from the retail DAT 2026-10-07 |
| Hume male base motion (and each race's base file) | `ROM/32/58` | Routines `lc00`-`lc06`, `lc10`, `lc11` and `ls00`-`ls06`, `ls10`, `ls11`: the ranged attack start / finish (`calg` / `shlg` in `ROM/0/0`, ops 0x76 / 0x77, n = the ranged weapon's RangeType, #158). Not a synthesis; no synthesis routine found | `/playroutine` | read from the DAT 2026-10-07; see session-and-packets.md |
| 7025 (EN), 7024 (JP) | `ROM/27/70`, `ROM/27/69` | Emote log lines: `2 * id` with a target, `2 * id + 1` without (16 / 17 `/wave`) | `ClientMessageTables.EmoteMessages`; printed for S2C 0x05A | **Beyond XiPackets:** found by searching the retail DATs (2026-10-03), checked against LandSandBoat's emote ids |

### UI and lobby

| File id | Path | Contents | Decoder | Source |
|---|---|---|---|---|
| 39542 | `ROM/119/51` | English menu DAT: 0x30 menus, 0x31 element groups, 0x20 textures (fonts, `moji` log font) | `UiResourceLibrary` | xi-tools `docs/ui/export.md`, `list.md`; [stock-ui.md](../ui/stock-ui.md#data-layer-chunk-1) |
| 1 | `ROM/0/1` | Base (Japanese) menu DAT; also carries fonts the English one omits | `UiResourceLibrary` | xi-tools |
| 14-21 | `ROM/0/14`-`21` (`win0`) | Window skins 1-8: frame textures that override the menu DATs | `UiResourceLibrary` | xi-tools |
| 87 | `ROM/119/57` | Status icons: 640 records of 0x1800 bytes in status id order, 32 x 32 icon at +0x280 | `StatusIconLibrary` | **Beyond xi-tools:** record layout read from the retail data; the icon layout follows xi-tools' item icon format. [stock-ui.md](../ui/stock-ui.md#live-hud-chunk-3) |
| 39551 | `ROM/280/15` (`mgc_`) | Spell icons | Not read yet | [stock-ui.md](../ui/stock-ui.md) (Reference DATs) |
| 23 | `ROM/0/23` (`titl`) | Title fly-through: 0x06 camera routes, 0x07 routines, 0x2F environments | Not read yet | xi-tools `docs/title/`, `docs/dats/ROM_0_23.md`; [character-lobby.md](../design/character-lobby.md#what-the-lobby-is-made-of) |
| 39541 | `ROM/119/50` (`lobb`, EN; JP `ROM/91/16` = 39533) | Lobby UI: 0x30 menus, 0x31 group `lobbywin`, 0x20 textures | `UiResourceLibrary.LoadLobby` (read by path, ahead of the menu DATs; `lobbyps2` is aliased to `lobbywin`), drawn by `StockUiLobby` | xi-tools; **Differs from xi-tools:** button captions are sprite references, not text ids; **Beyond xi-tools:** the creation and prompt menus' `lobbyps2` images are `lobbywin`'s at the same indices ([character-lobby.md](../design/character-lobby.md#what-the-lobby-is-made-of)) |
| (by path) | `ROM/165/71` (`XISTRING`, EN; older copy `ROM/97/36`) | Lobby status lines, help bar lines, prompts, nation descriptions (231 strings) | `XiStringTable`, `LobbyTextTables.Status` | XiPackets `lobby/Protocol.md` names `ROM/97/36`; **Beyond XiPackets:** the current copy is `ROM/165/71`; rows read 2026-10-04 ([character-lobby.md](../design/character-lobby.md#lobby-text)) |
| (by path) | `ROM/165/70` (`d_msg`, EN) | Lobby error messages (201 rows; row 1 "Error code: FFXI-%04d") | `DMsgStringTable`, `LobbyTextTables.ErrorLines` | XiPackets `lobby/Protocol.md` (file 55646); code-to-row map matched by text ([character-lobby.md](../design/character-lobby.md#lobby-text)) |

### Sound and music

No DAT holds audio. DATs carry 0x3D sound pointers (`SoundEffectPointer`) whose id names a sound file outside the DAT tree: sound effects at `sound*/win/se/seNNN/seNNNNNN.spw`, music at `sound*/win/music/data/musicNNN.bgw` (roots `sound`, `sound2`-`sound6`, `sound9`). `FfxiSoundHeader` / `FfxiAdpcm` / `FfxiSoundStream` decode them (ADPCM and PCM; ATRAC3 through `Atrac3Stream`, whose header reads `blocks` / `loopStart` as sample counts) and `ZoneSoundTable` groups a zone model DAT's pointers (weather ambient loops, footsteps, doors). Formats and the retail census: [audio.md](../design/audio.md#sound-files-38).

## Section types (`DatSectionType`)

Every section starts with the 16-byte header `DatSectionHeader`: a 4-character id; then a dword with the type in bits 0-6, the size in 16-byte units in bits 7-25 (header included), and flags in bits 26-31. `DatSectionWalker` walks the headers, and `DatDirectoryTree` nests sections between 0x01 and 0x00. The names follow xi-tools (`SECTION_TYPE_NAMES`, `docs/reference/dat_sections.md`, after xim) and xi-model-viewer, as cited in the `DatSectionType` doc comment. An unknown code is kept as `RawTypeCode`.

Status: **decoded** = every field GordianXI needs is read; **partly** = some fields or ops are read and the rest are skipped; **not decoded** = the section is skipped.

| Code | Name | Holds | GordianXI decoder | Status | Detail |
|---|---|---|---|---|---|
| `0x00` | End | End of a directory | `DatDirectoryTree`, `ZoneDataLoader` | decoded | |
| `0x01` | Directory | Opens a named group of sections | `DatDirectoryTree`, `ZoneDataLoader` | decoded | |
| `0x04` | Table | Generic table | none | not decoded | |
| `0x05` | ParticleGenerator | An effect: initializer, updater and expiration opcode lists, linked resources | `ParticleGeneratorDecoder` (zone and actor effects) | partly: opcode coverage in the doc | [particles.md](../rendering/particles.md#opcode-coverage) |
| `0x06` | Route | Camera path: keyframes of eye, look-at, focal length, roll | `EventSceneResource` (`CameraRoute`) | decoded in scene resources; the title DAT's routes are not read | [viewport-and-terrain.md](../rendering/viewport-and-terrain.md#camera-and-zone-terrain-phase-5b) |
| `0x07` | EffectRoutine | Timed command list: spawns generators, plays clips, links routines, fades, camera shots | `EffectRoutineDecoder` (spawns, routine starts 0x03 / 0x73, the third list's loop op 0x01 and the timed replay 0x52 that make a zone routine start on zone load; 0x52's window and intervals, `TimedReplayWindow`, [#81](https://github.com/jimmy58663/GordianXI/issues/81)), `MotionRoutineDecoder` (motion ops), `EventSceneResource` (`SceneRoutine`, also the zone's on-demand routines played by `ZoneRoutinePlayer`), `DoorRoutineDecoder` (door ops 0x0C / 0x0D) | partly: listed ops only | [particles.md](../rendering/particles.md#how-zone-routines-start-210), [entities-and-animation.md](entities-and-animation.md#transient-combat-and-action-animation-phase-5d1) |
| `0x19` | ParticleKeyFrameData | (time, value) curves for generator properties | `ParticleKeyFrameDecoder` | decoded | [sky-and-weather.md](../rendering/sky-and-weather.md) |
| `0x1C` | ZoneDef | Object placements (TRS, mesh name), point-light table, collision block | `ZoneDefDecoder`, `ZoneCollisionDecoder` | decoded | [collision-and-physics.md](collision-and-physics.md), [lighting.md](../rendering/lighting.md#point-lights) |
| `0x1F` | ParticleMesh | Small triangle lists that generators draw (water surfaces, surf) | `ParticleMeshDecoder` | decoded | [particles.md](../rendering/particles.md#particle-mesh-water-surfaces-0x1f) |
| `0x20` | Texture | Paletted 8 / 16 bpp, RGBA32 or DXT1/3/5 image | `TextureDecoder` | decoded | [viewport-and-terrain.md](../rendering/viewport-and-terrain.md#resource-cache-keys) |
| `0x21` | SpriteSheetMesh | One texture and N billboard cards (moon phases, flares) | `SpriteSheetDecoder` | decoded | [sky-and-weather.md](../rendering/sky-and-weather.md#sun-horizon-and-lens-flares-chunk-5) |
| `0x25` | WeightedMesh | Morphable mesh that generators draw: up to five morph targets (positions, 10:10:10 normals) blended by each particle's weights (the eye-shaped mask of Port Jeuno 324's blink, fish, birds, Alzadaal's tentacles) | `WeightedMeshDecoder` | decoded (alpha-discard flag 0x80 read, not applied). **Beyond xi-model-viewer:** it reads only the header; the target, normal and index layout was read from all 680 retail sections (#204) | [particles.md](../rendering/particles.md#weighted-meshes-0x25) |
| `0x29` | Skeleton | Joint hierarchy, bind pose, joint references | `SkeletonDecoder` | decoded | [entities-and-animation.md](entities-and-animation.md#skeletal-animation-phase-5d) |
| `0x2A` | SkeletonMesh | Skinned entity mesh, occlude type, render properties | `SkeletonMeshDecoder` | decoded | [entities-and-animation.md](entities-and-animation.md#models-and-equipment-phase-5c) |
| `0x2B` | SkeletonAnimation | One clip: per-joint rotation, translation and scale keys | `SkeletonAnimationDecoder` | decoded; scale keys kept as stored, zero included (a zero scale hides the joint's geometry, [#76](https://github.com/jimmy58663/GordianXI/issues/76)). **Differs from xi-tools:** `docs/anim/format.md` calls scale effectively unused; 6,748 clips in 725 model DATs carry keys of 1e-4 or less | [entities-and-animation.md](entities-and-animation.md#skeletal-animation-phase-5d) |
| `0x2E` | ZoneMesh | Static zone geometry in local space (encrypted, see below); per submesh flag 0x8000 = alpha blend, 0x2000 = double-sided, mesh name starting `_` = alpha test | `ZoneMeshDecoder` | decoded | [viewport-and-terrain.md](../rendering/viewport-and-terrain.md#camera-and-zone-terrain-phase-5b) |
| `0x2F` | Environment | Time-of-day lighting, fog, sky dome slices | `EnvironmentDecoder` | decoded | [lighting.md](../rendering/lighting.md#terrain-lighting-from-0x2f-chunk-1), [sky-and-weather.md](../rendering/sky-and-weather.md#sky-dome-and-dithering-chunk-2) |
| `0x30` | UiMenu | Menu frame, buttons, navigation links, shape references | `UiMenuDecoder` | decoded | [stock-ui.md](../ui/stock-ui.md#data-layer-chunk-1) |
| `0x31` | UiElementGroup | Texture list and images of quad parts | `UiElementGroupDecoder` | decoded | [stock-ui.md](../ui/stock-ui.md#data-layer-chunk-1) |
| `0x36` | ZoneInteractions | Zone interaction volumes (`RID`): turned boxes with a 4-char id whose first character is the kind (`_` door, `@` lift, `z` zone line, `m` sub-area) | `ZoneInteractionDecoder` | partly: doors' boxes used ([#15](https://github.com/jimmy58663/GordianXI/issues/15), first table only); lifts' two stops (+0x34 / +0x36 s16, `/ 256 + Y`) used from every table ([#66](https://github.com/jimmy58663/GordianXI/issues/66)): a zone has several 0x36 sections and the `@` records sit in their own (Metalworks `e237`, Palborough Mines `l143`, Pso'Xja `l009`, Davoi `l149`, Fort Ghelsba `z141`), not the first. **Differs from xi-tools:** `docs/zone/elevators.md` has each car parked on one of its stops; Davoi's `@450` car is authored at -8.19, a yalm off its upper stop (-9.17), and the landings show the stops are absolute | [collision-and-physics.md](collision-and-physics.md) |
| `0x3D` | SoundEffectPointer | `"SeSep  "` magic + `u32` sound effect id (the section name is not the id) | `SoundEffectPointer`, `ZoneSoundTable` (zone `weat/<weather>` ambient loops keyed `HHMM`, `fses` / `fser` footsteps, `door/<door>`); generators record the link as `ParticleLinkedDataType.Audio` | decoded. **Beyond xi-tools:** the `fser` running set and the `HHMM` time keys of the weather ambient loops | [audio.md](../design/audio.md#sound-files-38) |
| `0x3E` | PointList | Point list | none | not decoded | |
| `0x45` | Info | Model info block | `EntityModelLoader` reads byte 3 (weapon animation type) and byte 6 (standard joint) of weapon DATs | partly | [entities-and-animation.md](entities-and-animation.md#models-and-equipment-phase-5c) |
| `0x49` | SpellList | Spell id list | none | not decoded | |
| `0x4A` | Path | Points a sound generator follows: `"RAB\0"`, u32 7, ..., u32 count at +0x30, points of 0x20 bytes from +0x40 (`f32 x, y, z, w`, rest unknown) | `ZoneSoundEmitterDecoder.ReadPath` | partly. **Beyond xi-tools:** layout read from the retail zones (Bibiki Bay `mina` / `gake` / `hama`, Southern San d'Oria `kiji` / `choc`) | [audio.md](../design/audio.md#zone-effect-audio-39) |
| `0x53` | AbilityList | Ability id list | none | not decoded | |
| `0x54` | WeaponTrace | Weapon swing trail | none | not decoded | |
| `0x5D` | BumpMap | Bump map | none | not decoded | |
| `0x5E` | Blur | Blur effect | none | not decoded | |

xi-tools also lists `0x46` and `0x5F` with unknown meaning; they are not in the enum.

Files that are not section containers: zone event scripts, zone dialog tables, zone entity lists, item tables, `d_msg` and `XISTRING` string tables, the status icon DAT, and the FTABLE / VTABLE pairs. Each has its own layout, described in the docs linked above.

## Encodings

| Format | Encoding | Detail |
|---|---|---|
| Item tables | Each record byte rotated left by 3 bits; record stride 0xC00 (legacy) or 0x1400 (current retail), detected per file | [world-state-and-resources.md](world-state-and-resources.md#dat-decoders) |
| `d_msg` string tables | XOR 0xFF on every byte when the header byte at +0x0A is non-zero, else plain | [world-state-and-resources.md](world-state-and-resources.md#dat-decoders), [stock-ui.md](../ui/stock-ui.md#menu-string-tables-19) |
| Zone dialog tables | First dword `0x10000000 \| (size - 4)` in the clear, every later byte XOR 0x80 | [stock-ui.md](../ui/stock-ui.md#dialog-text-chunk-6) |
| Named things in dialog text (code 0x01) | Sub-block lengths and value bytes XOR 0x80, inside the already-decoded text | [stock-ui.md](../ui/stock-ui.md#dialog-text-chunk-6) |
| Zone event scripts, zone entity lists, status icons, `XISTRING` tables | Plain | [stock-ui.md](../ui/stock-ui.md#dialog-text-chunk-6), [stock-ui.md](../ui/stock-ui.md#menu-string-tables-19) |
| 0x2E ZoneMesh | Two passes keyed by two 256-byte tables: a keyed XOR stream when the mode byte is 5 or more, then a keyed swap of 8-byte blocks between the payload's two halves when the u16 at +6 is 0xFFFF. Counters must be 64-bit, or every byte past 32 KB decodes wrong | `ZoneMeshDecoder.DecryptZoneMesh` (after xi-tools `xi/zone/xi_decrypt.py`); no doc section yet |
| 0x1C ZoneDef | Mode byte above 0x1A: runs of 16-23 bytes XOR 0xFF, chosen and sized by a key from the first table; object names then XOR 0x55; record stride 0x64 (prototype 0x54) | `ZoneDefDecoder.DecryptZoneObjects`; [collision-and-physics.md](collision-and-physics.md) |
| Zone key tables | Not a DAT: `ZoneDataLoader.TryExtractKeyTables` finds the two 256-byte tables by signature in `FFXiMain.dll` (or `FFXiMain.dll.orig`) at run time; without them zone geometry stays encrypted | `ResourceManager.EnsureKeyTables` |
| 0x20 textures, other sections | Plain | [viewport-and-terrain.md](../rendering/viewport-and-terrain.md#resource-cache-keys) |
