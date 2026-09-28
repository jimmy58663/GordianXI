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
- [x] Give the never-called C2S builders a caller ([#4](https://github.com/jimmy58663/GordianXI/issues/4)). Each now has a packet-module send method: `InventoryPacketModule.UseSubcontainerAsync` (`0x03B` mannequin), `BidAuctionAsync` (`0x04E` Bid), `CheckEquipsetAsync` (`0x052`), `SetLockstyleAsync` (`0x053`); `ProgressionPacketModule.MarkKeyItemSeenAsync` (`0x064`), `SendMyRoomIsAsync` (`0x0CB`), `SendMyRoomLayoutAsync` (`0x0FA`), `SendChocoboRaceReqAsync` (`0x09B`), `SendUnityQuestAsync` (`0x117`), `SendUnityToggleAsync` (`0x118`). `/lockstyle` sends `0x053` Query, `/lockstyle on` Enable, `/lockstyle off` Disable, and `/lockstyleset` with no number Enable: the modes retail uses for each form per XiPackets `world/client/0x0053`. `MarkKeyItemSeenAsync` sets the seen bit in `ProgressionState` and sends the whole 512-item table's seen flags, because LandSandBoat stores every set bit and sends no `0x055` back. The rest wait for their UI: the Auction House ([#90](https://github.com/jimmy58663/GordianXI/issues/90)), key items ([#91](https://github.com/jimmy58663/GordianXI/issues/91)), equipment sets and `/lockstyleset <n>` ([#92](https://github.com/jimmy58663/GordianXI/issues/92)), Mog House ([#93](https://github.com/jimmy58663/GordianXI/issues/93)), Unity ([#94](https://github.com/jimmy58663/GordianXI/issues/94)) and Chocobo Circuit ([#95](https://github.com/jimmy58663/GordianXI/issues/95)). `0x04E` was 52 bytes; it is now 60 (`0x3C`, the size XiPackets gives), with the 40-byte parcel zeroed. Verify with `dotnet test tests/Gordian.Core.Tests --filter "OutboundActionPacketTests|Lockstyle|MarkKeyItemSeen"`
- [x] Delete the 4 duplicate/legacy C2S builders ([#3](https://github.com/jimmy58663/GordianXI/issues/3)): `HandshakePackets.BuildGameOkSubPacket`/`BuildNetEndSubPacket` (0x00C/0x00D live in `LifecycleOutboundPackets.BuildGameOk`/`BuildNetEnd`) and `LifecycleOutboundPackets.BuildEventEnd`/`BuildEventEndXzy` (0x05B/0x05C live in `ProgressionPacketBuilder`, which `ProgressionPacketModule` sends)
- [x] Persist the decoded-but-discarded S2C data into `World/*State` caches ([#5](https://github.com/jimmy58663/GordianXI/issues/5)); the caches are listed in [world-state-and-resources.md](../world/world-state-and-resources.md#entity-store-and-state-caches). Three decoders were corrected against LandSandBoat and XiPackets on the way: `0x11D` is another player asking to join the party (`Flags`, `Status` 0 = asking / 1 = withdrawn, `sName`, `Race`), not a result byte at offset 6; `0x117` now reads its two 16-entry arrays (`ItemsChanged`, `ItemsEquipped`), not just `Count`; `0x04C` also reads the parcel's `Stat` and `ItemIndex`. `0x030 Effect` is the entity's crafting animation (XiPackets `world/server/0x0030`: `CraftAnimationEffect`, `CraftParam`, `StatusServer`, `CraftTimer`), not status effects; buff durations come from `0x063` and are tracked in [#17](https://github.com/jimmy58663/GordianXI/issues/17). Verify with `dotnet test tests/Gordian.Core.Tests --filter DecodedStateCacheTests`
- [x] Regression test that every decoder is reachable ([#2](https://github.com/jimmy58663/GordianXI/issues/2)): `PacketDecoderRegistrationTests` reflects over every `S2C_0x*` type in `Gordian.Core.Network.Packets`, checks its `PacketId` const matches the type name, and asserts the dispatcher built by `PacketParser` has a handler for it. Verify with `dotnet test tests/Gordian.Core.Tests --filter PacketDecoderRegistrationTests`; un-registering `0x073` makes it fail naming `S2C_0x073_ChocoboToteboard`
