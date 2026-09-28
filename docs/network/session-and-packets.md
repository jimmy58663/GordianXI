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
  - **Progression, Quests & Menus:** S2C/C2S for Mog House (`0x0CB`, `0x0FA`-`0x100`), Party/Alliance (`0x0C8`, `0x0DC`-`0x0E2`, `0x11C`), Merits/Job Points (`0x08C`/`0x08D`, `0x0BE`-`0x0C1`), RoE (`0x10C`-`0x10E`, `0x111`/`0x112`), Fishing (`0x066`, `0x110`, `0x115`), Chocobo Racing (`0x069`, `0x073`/`0x074`, `0x09B`), Unity (`0x063`, `0x110`, `0x116`-`0x118`), Conquest/Campaign (`0x05E`, `0x071`), and Cutscene Events (`0x032`-`0x036`, `0x05B`/`0x05C`)
- **Network Diagnostics & Datagram Telemetry:**
  - Real-time atomic datagram counters (Inbound/Outbound packets/sec, bytes/sec, rolling throughput window)
  - Packet sequence gap tracking & drop detection for UDP streams
  - Zero-allocation dispatch latency profiling (microsecond-level decode time)

## Movement packet (0x015) timing

- Smooth network locomotion synchronization: retail-accurate `0x015` packet protocol (accumulating 60 FPS Run Count in `MoveFlame`, zero `MovTime`, `0x0001` stationary stance), non-starving outbound queue bundling, and high-precision `Stopwatch` delta-time calibration guaranteeing authentic 5.0 yalms/sec running across remote clients (Windower/retail)

## Packet audit (2026-09-20)

*A full opcode-level audit found the coverage claim above is accurate at the decode/encode level (81 S2C decoders, 64 C2S builders are genuinely wired and reachable), but several packets that are "handled" don't actually do anything useful yet. Tracked here instead of re-opening Phase 3 as incomplete, since the wire-format work itself is done — what's missing is wiring the result into state/UI.*
- [x] Register orphaned decoder `S2C_0x073_ChocoboToteboard` (`ProgressionPackets.cs`) in `ProgressionPacketModule.Register()` ([#1](https://github.com/jimmy58663/GordianXI/issues/1)): the handler stores the slot index, race ident (`(grade << 18) | raceNumber`) and the 28 quinella odds in `ProgressionState` (`GetToteboardOdds`, `ToteboardUpdated`). Layout per LandSandBoat `0x073_chocobo_toteboard.h` / XiPackets `0x0073`; no in-game capture yet, since nothing sends the `0x09B` request that asks for it
- [ ] Wire up 14 fully-built but never-called C2S builders (currently dead code, meaning these player actions are impossible in the running client despite the packet layer existing): Auction House bid/buy (`0x04E`), subcontainer/mannequin equip (`0x03B`), equipset check & lockstyle (`0x052`/`0x053`), key item reading (`0x064`), Mog House room-is & furniture layout (`0x0CB`/`0x0FA`), Chocobo race entry (`0x09B`), Unity quest accept & toggle (`0x117`/`0x118`); also delete the 4 duplicate/legacy builders for opcodes already covered elsewhere (`HandshakePackets.BuildGameOkSubPacket`, `BuildNetEndSubPacket`, `LifecycleOutboundPackets.BuildEventEnd`, `BuildEventEndXzy`)
- [ ] Persist the following decoded-but-discarded S2C data into a `World/*State` cache instead of only logging it: status effect apply/expire + duration (`0x030 Effect` — blocks any buff/debuff countdown UI), Auction House state (`0x04C Auc` — no AH state object exists at all), Guild shop & Bazaar transaction results (`0x082`-`0x085`, `0x106`, `0x108`-`0x10A`), Equipset validation/result (`0x116`/`0x117`), linkshell comlink (`0x0E0`), party invite result (`0x11D`), Mog House operation result (`0x0FA`). (`0x036 TalkNum` and `0x02A TalkNumWork` now print through the event dialog, see [docs/ui/stock-ui.md](../ui/stock-ui.md#dialog-text-chunk-6).)
- [ ] Add a `Gordian.Core.Tests` `Network/Packets` regression test asserting every decoder defined in `Network/Packets/*.cs` is reachable from `PacketDispatcher` (would have caught the orphaned `0x073` decoder automatically)
