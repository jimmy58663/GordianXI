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
- `0x063` misc data: about 900 per session
- `0x0D2` treasure pool
- `0x067` char sync
- `0x051` own model
- `0x0CA`, `0x0AE`, `0x0AD`, `0x08E`, `0x04F`, `0x041`, `0x058`, `0x038`, `0x053`

**Why a wrong size matters.** LSB's `ValidatedPacketHandler` (`packet_system.cpp`) silently drops a fixed-size C2S packet unless its header size equals `roundUp4(sizeof(struct))`. A C2S size that differs from XiPackets or LSB is therefore a dead packet, not a cosmetic difference. Only `0x01E`, `0x01F`, `0x02B`, `0x02C`, `0x0A0`, `0x0A1`, `0x0B5`, `0x0B6` and `0x0D3` are variable-length.

**Offsets.** XiPackets offsets count from the start of the packet. Our decoders receive the payload after the 4-byte header (`PacketParser`), so our offset = XiPackets offset − 4.

Wrong today (bugs):
- [#97](https://github.com/jimmy58663/GordianXI/issues/97): linkshell C2S `0x0E1`/`0x0E2`/`0x0E4` are 148 bytes instead of 144, and `0x0E2` sets no action bits.
- [#98](https://github.com/jimmy58663/GordianXI/issues/98): `PartyKind.Alliance` is 1; it should be 5.
- [#99](https://github.com/jimmy58663/GordianXI/issues/99): S2C `0x05E` conquest never decodes.
- [#100](https://github.com/jimmy58663/GordianXI/issues/100): S2C field fixes. `0x061` exp, `0x062` craft skills, `0x112` RoE chunk, `0x08C` merits, `0x026` mannequin, `0x037` dead counter, `0x030` synthesis enum, `0x00E` masks, `0x077` flags, `0x0CC` name (needs a capture).
- [#101](https://github.com/jimmy58663/GordianXI/issues/101): `0x05B`/`0x065` position modes 3 and 6 should despawn the entity.
- [#102](https://github.com/jimmy58663/GordianXI/issues/102): C2S corrections. `0x116` block index, `0x01A` ground-target axes, `0x00A` login fields.
- The Auction House can't sell: `0x04E` AskCommit/LotIn are missing, and `AuctionCommand.Open` isn't a client command. Noted on [#90](https://github.com/jimmy58663/GordianXI/issues/90).

Missing packets, grouped by feature:
- [#103](https://github.com/jimmy58663/GordianXI/issues/103): death flow. Home point, Raise and Tractor menus, and S2C `0x0F9`.
- [#104](https://github.com/jimmy58663/GordianXI/issues/104): the remaining `0x01A` action kinds.
- [#105](https://github.com/jimmy58663/GordianXI/issues/105): S2C `0x063`.
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
- [ ] Persist the following decoded-but-discarded S2C data into a `World/*State` cache instead of only logging it: status effect apply/expire + duration (`0x030 Effect` — blocks any buff/debuff countdown UI), Auction House state (`0x04C Auc` — no AH state object exists at all), Guild shop & Bazaar transaction results (`0x082`-`0x085`, `0x106`, `0x108`-`0x10A`), Equipset validation/result (`0x116`/`0x117`), linkshell comlink (`0x0E0`), party invite result (`0x11D`), Mog House operation result (`0x0FA`). (`0x036 TalkNum` and `0x02A TalkNumWork` now print through the event dialog, see [docs/ui/stock-ui.md](../ui/stock-ui.md#dialog-text-chunk-6).)
- [x] Regression test that every decoder is reachable ([#2](https://github.com/jimmy58663/GordianXI/issues/2)): `PacketDecoderRegistrationTests` reflects over every `S2C_0x*` type in `Gordian.Core.Network.Packets`, checks its `PacketId` const matches the type name, and asserts the dispatcher built by `PacketParser` has a handler for it. Verify with `dotnet test tests/Gordian.Core.Tests --filter PacketDecoderRegistrationTests`; un-registering `0x073` makes it fail naming `S2C_0x073_ChocoboToteboard`
