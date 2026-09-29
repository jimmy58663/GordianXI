# Session Handoff & Packet Engine

> Bootloader handoff, direct LandSandBoat login, UDP framing, Blowfish, and the zero-allocation packet registry (Phases 2 and 3). The handoff/proxy rules themselves are in `AGENTS.md`. Status and open work: [ROADMAP.md](../../ROADMAP.md) and GitHub Issues.

## Bootloader handoff and direct login (Phase 2)

- Reverse-engineered `xiloader.exe` and `pol.exe` COM launch mechanisms
- Multi-region 32-bit registry detection (`GameDirectoryDetector.cs` for US/EU/JP under `InstallFolder\0001`)
- Ephemeral proxy swapping & self-healing startup (`ProxyStager.cs` managing `FFXiMain.dll` $\leftrightarrow$ `FFXiMain.dll.orig`)
- COM-compliant 32-bit proxy (`Gordian.Proxy` with `DllGetClassObject`, `DllCanUnloadNow`, `IClassFactory`)
- Named Pipe IPC bridge (`\\.\pipe\GordianXI_Handoff`)
- Native LandSandBoat login client (`LsbLoginClient` with TLS auth on 54231, data on 54230, Blowfish session key derivation, and direct map connection)
- Full in-game verification: launch `Local (Cybin)` -> direct LSB login -> live UDP packet stream

## Packet engine (Phase 3)

*Goal: 100% complete coverage of all known LandSandBoat server-to-client (S2C) and client-to-server (C2S) packet definitions with zero-allocation decoders.*

- Full Blowfish cipher suite integration (`LegacyBlowfishCryptoSuite` with MD5 key digest and ECB cipher)
- Incoming & outgoing UDP packet framing (`0x0A` chunking, sequence tracking, checksum verification)
- Session keepalive / ping-pong loop (4Hz `GP_CLI_POS` 0x015 heartbeat & idle timeout prevention)
- Login & handshake sequence (`0x00A` login -> `0x00A` ack -> `0x00C` gameok -> `0x008` enterzone -> `0x00D` netend -> `ActiveInWorld`)
- **Zero-Allocation Packet Registry & Dispatcher Pipeline:**
  - Direct-indexed $O(1)$ packet dispatcher architecture (512-slot opcode table, zero heap allocations)
  - Zero-allocation `readonly ref struct` inbound decoders: `0x00A` (LoginAck), `0x008` (EnterZone), `0x00B` (Logout/ZoneTransition), `0x015` (PosPing), `0x0EE` (Policy)
  - Zero-allocation outbound builders: `0x00A`, `0x00C` (GameOk), `0x00D` (NetEnd), `0x015` (Pos), `0x05B` (EventEnd), `0x05E` (MapRect)
  - Decoupled `LifecyclePacketModule` coordinating handshake and lifecycle transitions
- **Zone Transition Architecture:**
  - Parse `0x00B` `GP_SERV_COMMAND_LOGOUT` (states: `LOGOUT=1`, `ZONECHANGE=2`, `MYROOM=3`, extraction of target IP/Port from `Iwasaki` struct)
  - Build & transmit `0x05B` / `0x05E` zoneline & mog house transition requests (`RequestZoneChangeAsync`, `RequestMogHouseExitAsync`, `RequestLogoutAsync`)
  - Dynamic UDP socket re-binding to target map server, resetting sequence numbers, and re-executing handshake seamlessly (`PerformZoneTransitionAsync`)
  - Zone-in events ([#122](https://github.com/jimmy58663/GordianXI/issues/122)): when a zone's `onZoneIn` returns a cutscene, LandSandBoat sends no 0x032/0x034 but fills the event fields of S2C `0x00A` (`EventNo` +60 = text table zone, `EventNum` +94 = zone, `EventPara` +96 = event id, `EventMode` +98 = flags; all 0 otherwise) and holds the character in the event until C2S `0x05B` ends it. `LifecyclePacketModule.ZoneInEventReceived` fires after the zone is set, and `PacketParser` starts it through `ProgressionState.StartEvent` with the player as the actor, so the event VM runs it like any other ([dialog text](../ui/stock-ui.md#dialog-text-chunk-6)). Fields from XiPackets `world/server/0x000A` and LandSandBoat `src/map/packets/s2c/0x00a_login.{h,cpp}`; cases: Rhapsodies of Vana'diel 1-1 (event 30035 on entering a nation city at level 3+) and, seen in-game on 2026-09-28, Seekers of Adoulin 1-1 (event 878 on entering Northern San d'Oria, once); `ZoneInCutsceneTests` replays both from the Northern San d'Oria DAT.
- **Declarative Zero-Allocation Packet Registry Expansion:**
  - **Session & Zone Lifecycle:** S2C `0x00A`, `0x00B`, `0x008`, `0x05B`, `0x065`; C2S `0x00A`, `0x00C`, `0x00D`, `0x011`, `0x015`, `0x05B`, `0x05C`, `0x05E`, `0x0E7`
  - **Entity & World State:** S2C `0x00D` (PC update), `0x00E` (NPC/Mob update), `0x01B` (Job info), `0x037` (Char status), `0x061`/`0x062` (Stats), `0x0DF` (Group attr), `0x076` (Effects), `0x077` (Vis); C2S `0x00F` (Target interact), `0x016`/`0x017` (Char reqs), `0x061` (Cli status req)
  - **Communication & Chat:** S2C `0x017` (Chat), `0x009` (SysMsg), `0x047` (Translate), `0x0CC` (LS Msg); C2S `0x0B5` (Chat send), `0x0B6` (Tell), `0x0B7` (Assist), `0x0E0`-`0x0E4` (LS mgmt)
  - **Party & Alliance Networking:** S2C `0x0DC` (Group solicit/invite), `0x0C8` (Group table), `0x0DD` (Group list/member info), `0x0DE` (Invite reset); C2S `0x06E` (Invite req), `0x06F` (Leave), `0x070` (Disband), `0x071` (Kick), `0x074` (Invite response accept/decline)
  - **Client-Side Command Router:** Full slash command routing (`/join`, `/decline`, `/pcmd`, `/invite`, `/leave`, `/disband`, `/tell`, `/say`, `/party`, `/shout`, `/yell`, `/linkshell`, `/echo`) and server `!` command pass-through
  - **Standard FFXI System Message Resolution:** `StandardMessages` (`MsgStd`) mapping standard message IDs to clean, authentic in-game text
  - **Combat & Action Pipeline:** S2C `0x028` (Combat action), `0x029`/`0x02D` (Battle msg), `0x030` (Effects), `0x0AA` (Magic), `0x0AC` (Commands), `0x119` (Recasts); C2S `0x01A` (Action req: attack, cast, ability), `0x0DD` (Check / equip inspect: `/check` and the command menu's Check; the mob reply is a `0x029` battle message, the PC reply `0x0C9` is not decoded), `0x05D` (Emotes), `0x0F1` (Buff cancel), `0x11D` (Jump)
  - **Inventory & Economy:** S2C `0x01C`-`0x020` (Inventory items & attrs), `0x021`-`0x025` (Trade), `0x026` (Subcontainers), `0x03C`-`0x03F` (Shops), `0x04C` (AH), `0x050` (Equipment), `0x082`-`0x086` (Guilds), `0x105`-`0x10A` (Bazaar), `0x113`/`0x118` (Currencies), `0x116`/`0x117` (Equip sets); C2S `0x028` (Item dump), `0x029` (Move), `0x032`-`0x034` (Trade), `0x036` (Transfer), `0x037` (Use), `0x03A` (Stack), `0x03B` (Subcontainer), `0x04E` (AH), `0x050`-`0x053` (Equip/Lockstyle), `0x083`-`0x085` (Shops), `0x104`-`0x10B` (Bazaar)
  - **Progression, Quests & Menus:** Mog House (S2C `0x02E`, `0x096`, `0x0FA`; C2S `0x0CB`, `0x0FA`, `0x100`), Party/Alliance (S2C `0x0C8`, `0x0DC`-`0x0E0`, `0x0E2`, `0x11D`; C2S `0x11C`), Merits/Job Points (S2C `0x08C`/`0x08D`; C2S `0x0BE`-`0x0C0`), RoE (S2C `0x111`/`0x112`; C2S `0x10C`-`0x10E`), Fishing (S2C `0x115`; C2S `0x110`), Chocobo Racing (S2C `0x073`; C2S `0x09B`), Unity (S2C `0x110`; C2S `0x116`-`0x118`), Conquest (S2C `0x05E`; C2S `0x05A`), Key items (S2C `0x055`; C2S `0x064`), Missions (S2C `0x056`), and Cutscene Events (S2C `0x032`-`0x034`, `0x036`, `0x052`; C2S `0x05B`/`0x05C`). What is still missing is listed in the coverage audit below
- **Network Diagnostics & Datagram Telemetry:**
  - Real-time atomic datagram counters (Inbound/Outbound packets/sec, bytes/sec, rolling throughput window)
  - Packet sequence gap tracking & drop detection for UDP streams
  - Zero-allocation dispatch latency profiling (microsecond-level decode time)

## Movement packet (0x015) timing

- Smooth network locomotion synchronization: retail-accurate `0x015` packet protocol (accumulating 60 FPS Run Count in `MoveFlame`, zero `MovTime`, `0x0001` stationary stance), non-starving outbound queue bundling, and high-precision `Stopwatch` delta-time calibration guaranteeing authentic 5.0 yalms/sec running across remote clients (Windower/retail)

## XiPackets coverage audit (2026-09-28)

This audit compared every opcode in XiPackets `world/client/` and `world/server/` with the `S2C_0x*` decoder types and the header opcodes written by the `Build*` methods. Each opcode we handle was also checked field by field against its XiPackets README, with LandSandBoat `src/map/packets/{s2c,c2s}/` used as the tie-breaker because it is the server we target. The priorities come from the `Unhandled Packet ID` lines in `logs/gordian_system_20260928.log`.

**Coverage.** XiPackets documents 169 S2C and 154 C2S world packets. We decode 86 S2C opcodes (84 in XiPackets, plus LSB's `0x015` and our own `0x0EE`) and build 77 C2S opcodes. Every opcode we build is in XiPackets.

LandSandBoat sends almost every missing S2C packet. These arrive in an ordinary session and are dropped today:
- `0x0D2` treasure pool
- `0x067` char sync
- `0x051` own model
- `0x0CA`, `0x0AE`, `0x0AD`, `0x08E`, `0x04F`, `0x041`, `0x058`, `0x038`, `0x053`

**Why a wrong size matters.** LSB's `ValidatedPacketHandler` (`packet_system.cpp`) silently drops a fixed-size C2S packet unless its header size equals `roundUp4(sizeof(struct))`. A C2S size that differs from XiPackets or LSB is therefore a dead packet, not a cosmetic difference. Only `0x01E`, `0x01F`, `0x02B`, `0x02C`, `0x0A0`, `0x0A1`, `0x0B5`, `0x0B6` and `0x0D3` are variable-length.

**Offsets.** XiPackets offsets count from the start of the packet. Our decoders receive the payload after the 4-byte header (`PacketParser`), so our offset = XiPackets offset − 4.

Fixed since the audit:
- [#97](https://github.com/jimmy58663/GordianXI/issues/97): linkshell C2S `0x0E1`/`0x0E2`/`0x0E4` are 144 bytes (`0x90`, XiPackets). `0x0E2` has two builders: `BuildSetLsMsg` sets byte +4 bit 6 (`0x40`, change the message) and `BuildSetLsWriteLevel` sets bit 5 (`0x20`, change the access level, level in bits 2-3 of +5). LSB ignores the packet unless one of them is set.
- [#98](https://github.com/jimmy58663/GordianXI/issues/98): `PartyKind.Alliance` is 5 (XiPackets `0x006E`, LSB `enums/party_kind.h`).
- [#99](https://github.com/jimmy58663/GordianXI/issues/99): S2C `0x05E` is a 176-byte payload (LSB `s2c/0x05e_conquest.h`; XiPackets has no layout yet). Payload offsets: balance 0, alliance 1, 27 region records at 22 (4 bytes each: ranking with beastmen, ranking without, graphics, owner 0 = neutral or nation + 1), current-region percentages 130-135, next tally 136, conquest points 140, beastmen percentage 144, Besieged overview word 156 (bits 0-1 Astral Candescence, 2-3 Al Zahbi orders), Mamook/Halvung/Arrapago stronghold words 160/164/168 (bits 0-2 orders, 3-10 forces, 11-14 level, 15 mirror destroyed, 16-19 mirrors (LSB sends the count halved), 20-23 prisoners), Imperial Standing 172.
- [#100](https://github.com/jimmy58663/GordianXI/issues/100): S2C field fixes. Payload offsets:
  - `0x061`: `ExpNow`/`ExpNext` (12/14) are read unsigned; XiPackets and LSB declare them `int16`, but LSB's exp-to-next table goes above 32767. The payload is `0x6C` (packet `0x70`).
  - `0x062`: entries 48-63 are crafting skills, `level * 0x20 + rank` with bit 15 set at the rank's cap; unused entries are `0xFFFF` (LSB `charutils::BuildingCharSkillsTable`). Entries 0-47 stay `level | cap bit`.
  - `0x112`: `Offset` (128) is a chunk index 0-3; the chunk goes to byte `Offset * 128` of the 512-byte completed-records table (XiPackets).
  - `0x08C`: `merit_count` (0) is the number of entries in this packet (up to 61), not a point total. An odd entry index means merit `index - 1` was lowered to 0 and is removed (XiPackets).
  - `0x026` mannequin: race/hair 6, head 8, body 10, hands 12, legs 14, feet 16, main 18, sub 20, range 22. XiPackets' data ends there (24 bytes); LSB appends race 24 and pose 25, read only when present.
  - `0x037`: `dead_counter1` (56) is the homepoint countdown in 1/60 s ticks plus 6 minutes (LSB `char_status.cpp`); seconds left = ticks / 60 - 360.
  - `0x030`: `EffectNum` (6) is the crystal's element, `0x10` Water to `0x17` Dark (LSB `enums/synthesis_effect.h`); `Type` (8) carries the result, 0 fail, 1 success, 2-4 HQ tiers (LSB `SYNTHESIS_RESULT`).
  - `0x0E2`: the fixed fields end at 36 and the packet is sized to the name, so short names are valid (XiPackets). The old 44-byte guard dropped names under about 8 letters.
  - `0x0FA`: the references disagree. XiPackets: u32 item 0, u32 result 4, index 8, category 9. LSB's unpacked struct: u16 item 0, u8 result 2, index 6, category 7. Bytes 6-7 are always 0 in the XiPackets layout, so the decoder picks the layout from them.
  - `0x00E`: the u16 at `0x2C` is `SubKind:3 | Status:13`, so look size is its low 3 bits. `AnimationSub` is flags3 `MonStat`, the low 3 bits of `0x26`. The elevator `EndTime` (`0x38`) is a u32 (LSB fills its low byte). Names are `Name[16]`, read from one of three places (XiPackets "Entity Name"): `0x40` for SubKind 1 with the `Name2` flag; `0x31` for a static NPC (index < 1024) whose `HasName` byte at `0x30` is 1; otherwise `0x30`, which is where LSB puts every NPC and mob name. Monster or NPC (both below index 1024) is `Flags1.MonsterFlag`, bit 0 of packet byte `0x20`: LSB writes the entity status there in every update, `Update` (1) for mob-allegiance entities and `Normal` (0) for NPCs (`CBaseEntity::Spawn`); packet byte `0x25` is `Flags2.g`, the hitbox size x 10, not a living-mob flag (a Marine Dhalmel with hitbox 37 lacks its bit 0x08; #133, in-game 2026-09-29).
  - `0x077`: the data is a UniqueNo list only when `Flags` (0) is 1; other flag values are ignored.
  - `0x0CC`: `encodedLsName` (156) is the 6-bit packed name (`LinkshellNameCodec`): an MSB-first bit stream of 6-bit values, 1-26 a-z, 27-52 A-Z, 53-62 0-9, 63 or 0 ends it, at most 20 characters. LSB really sends it packed: `linkshell.cpp` `LoadLinkshell` stores `EncodeStringLinkshell(name)` as the name both 0x0CC paths send. The bit order is confirmed against a retail `/lsmes` capture (Windower `capture`, 2026-09-28): `84 F4 84 24 13 B2 8F F0` at packet `0xA0` decodes to "GordianXI" as the client printed it; the capture is the `S2C_0x0CC_LinkshellMessage_DecodesRetailCapture` test.
- [#101](https://github.com/jimmy58663/GordianXI/issues/101): `0x05B`/`0x065` modes 3 (`PlaceAndDeletePop`) and 6 (`PlaceAndDeleteMaterialize`) place another entity and then remove it from `WorldState` (the pop effect is not played yet). For the local player they only place it. Mode 7 (`OpenIndoor`) places the entity; the indoor area it opens for the local player is not modelled. LSB defines these modes but never sends them, so only unit tests cover them.

Wrong today (bugs):
- [#102](https://github.com/jimmy58663/GordianXI/issues/102): C2S corrections. `0x116` block index, `0x01A` ground-target axes, `0x00A` login fields, `0x052` (`RemoveItemFlg`, `Equipment[16]`) and `0x053` (echo flag) are fixed, and `0x11C` and `0x02B` carry comments citing both sources.
- The Auction House can't sell: `0x04E` AskCommit/LotIn are missing, and `AuctionCommand.Open` isn't a client command. Noted on [#90](https://github.com/jimmy58663/GordianXI/issues/90).

Missing packets, grouped by feature:
- [#103](https://github.com/jimmy58663/GordianXI/issues/103): death flow. Home point, Raise and Tractor menus, and S2C `0x0F9`.
- [#104](https://github.com/jimmy58663/GordianXI/issues/104): the remaining `0x01A` action kinds.
- ~~[#105](https://github.com/jimmy58663/GordianXI/issues/105)~~: S2C `0x063` is decoded, see below. Monstrosity (types `0x03`/`0x04`) is left for post-MVP.
- [#106](https://github.com/jimmy58663/GordianXI/issues/106): treasure pool.
- [#107](https://github.com/jimmy58663/GordianXI/issues/107): `0x067`/`0x068` char and pet sync.
- [#108](https://github.com/jimmy58663/GordianXI/issues/108): `0x051`/`0x04F`.
- [#109](https://github.com/jimmy58663/GordianXI/issues/109): scheduler packets `0x038`-`0x03A`.
- [#110](https://github.com/jimmy58663/GordianXI/issues/110): message and event-parameter packets, including the `0x05A` emote echo.
- [#111](https://github.com/jimmy58663/GordianXI/issues/111): `/heal`, `/sit`, `/random`, widescan.
- [#112](https://github.com/jimmy58663/GordianXI/issues/112): synthesis and guild-shop requests.
- [#113](https://github.com/jimmy58663/GordianXI/issues/113): delivery box, blacklist, linkshell equip.
- [#114](https://github.com/jimmy58663/GordianXI/issues/114): music.
- [#115](https://github.com/jimmy58663/GordianXI/issues/115): login-time data (mounts, Trust points, BLU/PUP).
- [#116](https://github.com/jimmy58663/GordianXI/issues/116): undecoded fields in handled packets (`0x00A`, `0x056`, `0x057`, `0x028`, `0x04C`, and others).
- [#119](https://github.com/jimmy58663/GordianXI/issues/119): search (cache) server client.
- [#117](https://github.com/jimmy58663/GordianXI/issues/117): the post-MVP backlog, including the packets LSB doesn't implement.

The other XiPackets folders:
- **`lobby/`** (TCP 54001): `LsbLoginClient` implements 0x26 login → 0x05/0x04, 0x20 character info, and 0x07 select → 0x0B. Get-character 0x1F, the world list 0x24/0x23, create 0x22/0x21, delete 0x14 and rename 0x28 are still missing ([#35](https://github.com/jimmy58663/GordianXI/issues/35)). Our 0x07 (64 bytes) and 0x26 (128) are shorter than retail (0x58 and 0x98). LSB's `view_session.cpp` doesn't check lengths, so they work on LSB.
- **`cache/`** is the search server (LSB `src/search/`, TCP 54002). It serves AH item lists and price history, `/sea`, and party/linkshell member lists, and we have no client for it ([#119](https://github.com/jimmy58663/GordianXI/issues/119)). XiPackets has no per-packet pages for it yet.
- **`patch/`** is the POL version-check and file-update protocol (8 packets). LSB has no patch server and xiloader bypasses it, so the client doesn't need it.

To repeat the opcode diff, list `XiPackets/world/{client,server}`, then compare against `grep -rhoE "struct S2C_0x[0-9A-Fa-f]+" src/Gordian.Core` and the opcodes passed to `PacketHeader.Write(...)` or `(0xNNN | (size << 9))` under `src/Gordian.Core/Network`.

## Packet audit (2026-09-20)

*A full opcode-level audit found the coverage claim above is accurate at the decode/encode level (81 S2C decoders, 64 C2S builders are genuinely wired and reachable), but several packets that are "handled" don't actually do anything useful yet. Tracked here instead of re-opening Phase 3 as incomplete, since the wire-format work itself is done — what's missing is wiring the result into state/UI.*
- [x] Register orphaned decoder `S2C_0x073_ChocoboToteboard` (`ProgressionPackets.cs`) in `ProgressionPacketModule.Register()` ([#1](https://github.com/jimmy58663/GordianXI/issues/1)): the handler stores the slot index, race ident (`(grade << 18) | raceNumber`) and the 28 quinella odds in `ProgressionState` (`GetToteboardOdds`, `ToteboardUpdated`). Layout per LandSandBoat `0x073_chocobo_toteboard.h` / XiPackets `0x0073`; no in-game capture yet, since nothing sends the `0x09B` request that asks for it
- [x] Give the never-called C2S builders a caller ([#4](https://github.com/jimmy58663/GordianXI/issues/4)). Each now has a packet-module send method: `InventoryPacketModule.UseSubcontainerAsync` (`0x03B` mannequin), `BidAuctionAsync` (`0x04E` Bid), `CheckEquipsetAsync` (`0x052`), `SetLockstyleAsync` (`0x053`); `ProgressionPacketModule.MarkKeyItemSeenAsync` (`0x064`), `SendMyRoomIsAsync` (`0x0CB`), `SendMyRoomLayoutAsync` (`0x0FA`), `SendChocoboRaceReqAsync` (`0x09B`), `SendUnityQuestAsync` (`0x117`), `SendUnityToggleAsync` (`0x118`). `/lockstyle` sends `0x053` Query, `/lockstyle on` Enable, `/lockstyle off` Disable, and `/lockstyleset` with no number Enable: the modes retail uses for each form per XiPackets `world/client/0x0053`. `MarkKeyItemSeenAsync` sets the seen bit in `ProgressionState` and sends the whole 512-item table's seen flags, because LandSandBoat stores every set bit and sends no `0x055` back. The rest wait for their UI: the Auction House ([#90](https://github.com/jimmy58663/GordianXI/issues/90)), key items ([#91](https://github.com/jimmy58663/GordianXI/issues/91)), equipment sets and `/lockstyleset <n>` ([#92](https://github.com/jimmy58663/GordianXI/issues/92)), Mog House ([#93](https://github.com/jimmy58663/GordianXI/issues/93)), Unity ([#94](https://github.com/jimmy58663/GordianXI/issues/94)) and Chocobo Circuit ([#95](https://github.com/jimmy58663/GordianXI/issues/95)). `0x04E` was 52 bytes; it is now 60 (`0x3C`, the size XiPackets gives), with the 40-byte parcel zeroed. Verify with `dotnet test tests/Gordian.Core.Tests --filter "OutboundActionPacketTests|Lockstyle|MarkKeyItemSeen"`
- [x] Delete the 4 duplicate/legacy C2S builders ([#3](https://github.com/jimmy58663/GordianXI/issues/3)): `HandshakePackets.BuildGameOkSubPacket`/`BuildNetEndSubPacket` (0x00C/0x00D live in `LifecycleOutboundPackets.BuildGameOk`/`BuildNetEnd`) and `LifecycleOutboundPackets.BuildEventEnd`/`BuildEventEndXzy` (0x05B/0x05C live in `ProgressionPacketBuilder`, which `ProgressionPacketModule` sends)
- [x] Persist the decoded-but-discarded S2C data into `World/*State` caches ([#5](https://github.com/jimmy58663/GordianXI/issues/5)); the caches are listed in [world-state-and-resources.md](../world/world-state-and-resources.md#entity-store-and-state-caches). Three decoders were corrected against LandSandBoat and XiPackets on the way: `0x11D` is another player asking to join the party (`Flags`, `Status` 0 = asking / 1 = withdrawn, `sName`, `Race`), not a result byte at offset 6; `0x117` now reads its two 16-entry arrays (`ItemsChanged`, `ItemsEquipped`), not just `Count`; `0x04C` also reads the parcel's `Stat` and `ItemIndex`. `0x030 Effect` is the entity's crafting animation (XiPackets `world/server/0x0030`: `CraftAnimationEffect`, `CraftParam`, `StatusServer`, `CraftTimer`), not status effects; buff durations come from `0x063` and are tracked in [#17](https://github.com/jimmy58663/GordianXI/issues/17). Verify with `dotnet test tests/Gordian.Core.Tests --filter DecodedStateCacheTests`
- [x] Regression test that every decoder is reachable ([#2](https://github.com/jimmy58663/GordianXI/issues/2)): `PacketDecoderRegistrationTests` reflects over every `S2C_0x*` type in `Gordian.Core.Network.Packets`, checks its `PacketId` const matches the type name, and asserts the dispatcher built by `PacketParser` has a handler for it. Verify with `dotnet test tests/Gordian.Core.Tests --filter PacketDecoderRegistrationTests`; un-registering `0x073` makes it fail naming `S2C_0x073_ChocoboToteboard`

### S2C 0x063 misc data

`S2C_0x063_MiscData` (`ProgressionPackets.cs`) switches on the u16 `type` at packet +0x04 and is registered by `ProgressionPacketModule`. The payload starts at `type`; the per-type data starts at payload +4 (packet +0x08). Layouts from XiPackets `world/server/0x0063` and LandSandBoat `s2c/0x063_miscdata_*.h`.

| type | state | notes |
|---|---|---|
| `0x02` merits | `ProgressionState.LimitPoints`, `MeritPoints`, `MaxMeritPoints`, merit-mode flags | LSB layout: u16 limit points, u16 bitfield (7 merit points, 6 BLU bonus, 3 flags), u8 max merits |
| `0x05` job points | `ProgressionState.GetJobPointTotals(job)`, `JobPointsUnlocked` | flags byte, then 24 x (capacity, points, spent) u16 |
| `0x06` teleports | `ProgressionState.HasHomePoint` / `HasSurvivalGuide` / `HasWaypoint` / `HasTelepoint` / `HasAtmos` / `HasEschanPortal` | 16 mask words; home point, survival guide and waypoint are 4 words each |
| `0x07` Unity | `ProgressionState.UnityWeeks[0 previous, 1 current]` | one packet per kind: base (0x00, timestamp), members (0x01), points (0x02), personal ranking (0x14); the u32 arrays follow a u16 readiness flag at data +8 |
| `0x09` status icons | `LocalPlayerState.StatusIconIds` / `StatusIconTimestamps`, `GetStatusIconRemainingSeconds` | 32 u16 icons (0xFF empty) then 32 u32 end timestamps; 0x7FFFFFFF = no timer |
| `0x03`, `0x04`, `0x0A` | not decoded | Monstrosity (post-MVP); 0x0A is unused by the client |

The status icon timestamp is meant to overflow a u32; `GetStatusIconRemainingSeconds` subtracts modulo 2^32, taking `now` as Vana'diel seconds. That the unit is Vana'diel seconds x 60 comes from reading LandSandBoat's `0x063_miscdata_status_icons.cpp`; it is not yet checked against a live capture, so verify it in game before relying on the blink threshold in [#17](https://github.com/jimmy58663/GordianXI/issues/17). The Unity layouts (`0x07`) come from LSB only (XiPackets marks them as not reversed). Verify with `dotnet test tests/Gordian.Core.Tests --filter ProgressionPacketTests`.
