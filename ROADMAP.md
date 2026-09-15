# GordianXI Project Roadmap & Milestone Tracker

## Current State Summary
- **Target Framework:** .NET 10 (C# 14) + Avalonia UI 12.1.2 + ImGui.NET
- **Test Status:** 111 Passing Unit Tests (`dotnet test`)
- **Active Focus:** Phase 3: Complete LSB Packet Engine & Zone Transitions
- **North Star Goal:** High-performance, clean-room 64-bit cross-platform client replacement for Final Fantasy XI.

---

## 🏆 MVP Definition (Minimum Viable Product)
> **Goal:** A player can launch GordianXI, connect directly to a LandSandBoat private server, authenticate, spawn into a zone with 3D world geometry and character models rendered, control movement and camera using customizable Keyboard/Mouse or Gamepad, execute basic actions and slash commands via an interactive CLI console or hotkeys, view chat/vitals/inventory, and cross zonelines to new map servers without dropping session state.
> 
> *MVP includes Phases 1 through 5. Phases 6 through 10 represent Post-MVP extensions.*

---

## 🗺️ Phases & Milestones

### ✅ Phase 1: Foundation & Client Infrastructure
- [x] Four-tier decoupled monorepo (`Gordian.Core`, `Gordian.Automation`, `Gordian.Addons`, `Gordian.App`)
- [x] Clean-room reverse engineering boundaries enforced in `AGENTS.md`
- [x] Modern dark HUD UI with Avalonia MVVM
- [x] Encrypted profile storage with master AES key and defensive plaintext migration
- [x] Account profile management (Add, Edit, Delete, Select for Launch)
- [x] Profile & Global session termination commands (`TerminateCommand`, `TerminateAllCommand`)
- [x] Live Packet Inspector UI with Hex/ASCII view, pause/clear, and real-time capture

### ✅ Phase 2: Bootloader Session Handoff & Direct Authentication
- [x] Reverse-engineered `xiloader.exe` and `pol.exe` COM launch mechanisms
- [x] Multi-region 32-bit registry detection (`GameDirectoryDetector.cs` for US/EU/JP under `InstallFolder\0001`)
- [x] Ephemeral proxy swapping & self-healing startup (`ProxyStager.cs` managing `FFXiMain.dll` $\leftrightarrow$ `FFXiMain.dll.orig`)
- [x] COM-compliant 32-bit proxy (`Gordian.Proxy` with `DllGetClassObject`, `DllCanUnloadNow`, `IClassFactory`)
- [x] Named Pipe IPC bridge (`\\.\pipe\GordianXI_Handoff`)
- [x] Native LandSandBoat login client (`LsbLoginClient` with TLS auth on 54231, data on 54230, Blowfish session key derivation, and direct map connection)
- [x] Full in-game verification: launch `Local (Cybin)` -> direct LSB login -> live UDP packet stream

---

### ⏳ Phase 3: Complete LSB Packet Engine & Zone Transitions (MVP Core)
*Goal: 100% complete coverage of all known LandSandBoat server-to-client (S2C) and client-to-server (C2S) packet definitions with zero-allocation decoders.*

- [x] Full Blowfish cipher suite integration (`LegacyBlowfishCryptoSuite` with MD5 key digest and ECB cipher)
- [x] Incoming & outgoing UDP packet framing (`0x0A` chunking, sequence tracking, checksum verification)
- [x] Session keepalive / ping-pong loop (4Hz `GP_CLI_POS` 0x015 heartbeat & idle timeout prevention)
- [x] Login & handshake sequence (`0x00A` login -> `0x00A` ack -> `0x00C` gameok -> `0x008` enterzone -> `0x00D` netend -> `ActiveInWorld`)
- [x] **Zero-Allocation Packet Registry & Dispatcher Pipeline:**
  - [x] Direct-indexed $O(1)$ packet dispatcher architecture (512-slot opcode table, zero heap allocations)
  - [x] Zero-allocation `readonly ref struct` inbound decoders: `0x00A` (LoginAck), `0x008` (EnterZone), `0x00B` (Logout/ZoneTransition), `0x015` (PosPing), `0x0EE` (Policy)
  - [x] Zero-allocation outbound builders: `0x00A`, `0x00C` (GameOk), `0x00D` (NetEnd), `0x015` (Pos), `0x05B` (EventEnd), `0x05E` (MapRect)
  - [x] Decoupled `LifecyclePacketModule` coordinating handshake and lifecycle transitions
- [x] **Zone Transition Architecture:**
  - [x] Parse `0x00B` `GP_SERV_COMMAND_LOGOUT` (states: `LOGOUT=1`, `ZONECHANGE=2`, `MYROOM=3`, extraction of target IP/Port from `Iwasaki` struct)
  - [x] Build & transmit `0x05B` / `0x05E` zoneline & mog house transition requests (`RequestZoneChangeAsync`, `RequestMogHouseExitAsync`, `RequestLogoutAsync`)
  - [x] Dynamic UDP socket re-binding to target map server, resetting sequence numbers, and re-executing handshake seamlessly (`PerformZoneTransitionAsync`)
- [ ] **Declarative Zero-Allocation Packet Registry Expansion:**
  - [x] **Session & Zone Lifecycle:** S2C `0x00A`, `0x00B`, `0x008`, `0x05B`, `0x065`; C2S `0x00A`, `0x00C`, `0x00D`, `0x011`, `0x015`, `0x05B`, `0x05C`, `0x05E`, `0x0E7`
  - [x] **Entity & World State:** S2C `0x00D` (PC update), `0x00E` (NPC/Mob update), `0x01B` (Job info), `0x037` (Char status), `0x061`/`0x062` (Stats), `0x0DF` (Group attr), `0x076` (Effects), `0x077` (Vis); C2S `0x00F` (Target interact), `0x016`/`0x017` (Char reqs), `0x061` (Cli status req)
  - [ ] **Communication & Chat:** S2C `0x017` (Chat), `0x009` (SysMsg), `0x047` (Translate), `0x0CC` (LS Msg); C2S `0x0B5` (Chat send), `0x0B6` (Tell), `0x0B7` (Assist), `0x0E0`-`0x0E4` (LS mgmt)
  - [ ] **Combat & Action Pipeline:** S2C `0x028` (Combat action), `0x029`/`0x02D` (Battle msg), `0x030` (Effects), `0x0AA` (Magic), `0x0AC` (Commands), `0x119` (Recasts); C2S `0x01A` (Action req: attack, cast, ability), `0x05D` (Emotes), `0x0F1` (Buff cancel), `0x11D` (Jump)
  - [ ] **Inventory & Economy:** S2C `0x01C`-`0x020` (Inventory items & attrs), `0x021`-`0x025` (Trade), `0x026` (Subcontainers), `0x03C`-`0x03F` (Shops), `0x04C` (AH), `0x050` (Equipment), `0x082`-`0x086` (Guilds), `0x105`-`0x10A` (Bazaar), `0x113`/`0x118` (Currencies), `0x116`/`0x117` (Equip sets); C2S `0x028` (Item dump), `0x029` (Move), `0x032`-`0x034` (Trade), `0x036` (Transfer), `0x037` (Use), `0x03A` (Stack), `0x03B` (Subcontainer), `0x04E` (AH), `0x050`-`0x053` (Equip/Lockstyle), `0x083`-`0x085` (Shops), `0x104`-`0x10B` (Bazaar)
  - [ ] **Progression, Quests & Menus:** S2C/C2S for Mog House (`0x0CB`, `0x0FA`-`0x100`), Party/Alliance (`0x0C8`, `0x0DC`-`0x0E2`, `0x11C`), Merits/Job Points (`0x08C`/`0x08D`, `0x0BE`-`0x0C1`), RoE (`0x10C`-`0x10E`, `0x111`/`0x112`), Fishing (`0x066`, `0x110`, `0x115`), Chocobo Racing (`0x069`, `0x073`/`0x074`, `0x09B`), Unity (`0x063`, `0x110`, `0x116`-`0x118`), Conquest/Campaign (`0x05E`, `0x071`), and Cutscene Events (`0x032`-`0x036`, `0x05B`/`0x05C`)
- [x] **Network Diagnostics & Datagram Telemetry:**
  - [x] Real-time atomic datagram counters (Inbound/Outbound packets/sec, bytes/sec, rolling throughput window)
  - [x] Packet sequence gap tracking & drop detection for UDP streams
  - [x] Zero-allocation dispatch latency profiling (microsecond-level decode time)

---

### ⏳ Phase 4: World State, DAT Resource Pipeline & Modular VFS (MVP Core)
- [x] **Thread-Safe Spatial Entity Store (`Gordian.Core/World`):**
  - [x] Tracking for LocalPlayer, other PCs, NPCs, Monsters, Pets, and Trusts
  - [x] Indexing by Server ID (`uint32`) and Zone Target Index (`uint16`)
  - [x] Fast 3D spatial partitioning (Uniform Grid / BVH) for distance, cone, and line-of-sight queries
  - [x] Dead-reckoning & position interpolation between 250ms network ticks
- [ ] **Game State Caches:**
  - [ ] Multi-container inventory cache (Inventory, Wardrobes 1-8, Satchel, Sack, Case, Safe, Storage)
  - [x] Active player vitals (HP/MP/TP), base attributes, equipment loadout, buff/debuff timers
- [ ] **FFXI DAT Binary Decoders (`Gordian.Core/Resources`):**
  - [ ] Clean-room decoders for ROM directory DAT files (string tables, item tables, spell/ability tables)
  - [ ] Zone collision meshes, terrain geometry, entity models, and animation tables
- [ ] **Modular Virtual File System (VFS) & Asset Overrides (XIPivot Architecture):**
  - [ ] Priority-based asset resolution (`resources/mods/` overrides $\rightarrow$ base FFXI `.DAT` files)
  - [ ] Modern asset format support: **glTF 2.0 / FBX** models, **PNG / DDS (BC7)** high-res textures with normal/PBR maps, **OGG / FLAC** audio
  - [ ] Structured **JSON / YAML** data overrides alongside binary DMsg string tables
- [ ] **State & Memory Health Telemetry:**
  - [ ] Managed heap & GC pressure counters (.NET 10 Gen 0/1/2 collection tracking, heap allocation velocity)
  - [ ] Spatial partition & uniform grid query duration tracking with entity dead-reckoning cycle time benchmarking

---

### ⏳ Phase 5: Viewport Rendering, CLI Console & Input Subsystem (MVP Completion)
- [ ] **Interactive Character Command Console (CLI):**
  - [ ] In-client interactive console tab to control character via slash commands (`/pos`, `/target`, `/attack`, `/ws`, `/magic`, `/item`, `/heal`, `/zone <id>`, `/moveto x y z`)
  - [ ] Headless/early verification of movement, combat, and zoning without requiring full 3D rendering
- [ ] **Cross-Platform Input Subsystem:**
  - [ ] **Keyboard & Mouse:** Default layouts for FFXI Compact (WASD + IJKL camera) and FFXI Full (Numpad)
  - [ ] **Gamepad / Controller:** Full XInput, DirectInput, and SDL/Silk gamepad support (Xbox, PlayStation, generic HID) with deadzone, rumble, and axis calibration
  - [ ] Rebindable control mapping engine with JSON persistence and modifier key support
- [ ] **3D Viewport Rendering Surface:**
  - [ ] Silk.NET / Veldrid cross-platform graphics pipeline (Vulkan / DirectX / Metal) embedded in Avalonia via `NativeControlHost`
  - [ ] Zone terrain mesh rendering, character models, skeletal animations, and skybox/weather
  - [ ] Camera controller (third-person follow, first-person, free camera, target lock)
- [ ] **ImGui.NET In-Game HUD Overlays:**
  - [ ] Dark translucent HUD (`WindowRounding = 6.0f`) with Target bar, Party frames, Vitals gauges, Mini-map, and Combat Log
- [ ] **Render & Viewport Profiling:**
  - [ ] Frame pacing, draw calls, GPU pass timings, and pipeline stage metrics
  - [ ] ImGui.NET performance diagnostics overlay (FPS graph, frame time jitter, memory usage HUD)

---

### 🚀 Phase 6: Scripting Runtime, Central Addons & Package Manager (Post-MVP)
- [ ] **Dual Scripting Engine Sandbox (`Gordian.Addons`):**
  - [ ] Lua VM (NLua / KeraLua) with Windower/Ashita API compatibility shims
  - [ ] JavaScript / TypeScript VM (QuickJS / V8)
  - [ ] Strict isolation: Sandboxed I/O, event bus (`on_packet_in`, `on_packet_out`, `on_chat`, `on_zone_change`), zero access to `Gordian.Automation`
- [ ] **Addon Ecosystem & Package Manager:**
  - [ ] Central community repository manifest (`gordianxi/addons-index`) tracking verified plugins, versions, and dependencies
  - [ ] In-client Addon Browser: Search, 1-click install, auto-update check, enable/disable toggles
  - [ ] Custom Source Support: Direct Git clone or package URL input for private server or custom addons

---

### 🚀 Phase 7: Automation & Gambit Engine (Post-MVP)
- [ ] **FF12-Style Rule-Based Gambit Engine (`Gordian.Automation`):**
  - [ ] Priority evaluation tree (Condition $\rightarrow$ Target $\rightarrow$ Action, e.g. `PartyMember.HP < 50%` $\rightarrow$ `Cure IV`)
- [ ] **Multi-Box Swarm Coordination:**
  - [ ] Leader-follower formation tracking, assist targeting, synchronized skillchains / magic bursts
- [ ] **Zero-Drop Action Sequencing:**
  - [ ] Latency-compensating client-side command queueing with animation lock awareness
- [ ] **Killswitch Enforcement:**
  - [ ] Hardwired compliance with `ServerAutomationPolicy` from `Gordian.Core` (shuts down execution if restricted by private server)

---

### 🚀 Phase 8: Multi-Boxing Desktop Shell & UX Polish (Post-MVP)
- [ ] **Dockable Multi-Instance Desktop Shell (`Gordian.App`):**
  - [ ] Tabbed sessions, split-view grid, and floating pop-out windows for secondary characters
- [ ] **Global Hotkey Routing & Broadcasting:**
  - [ ] Broadcast input mode across multiple instances or pass-through to background sessions
- [ ] **Performance & Frame Pacing:**
  - [ ] Background instance throttling (reducing unfocused instances to 15-30 FPS to minimize GPU/CPU usage)
  - [ ] Multi-instance CPU/memory profiling and thread pool allocation tracking

---

### 🚀 Phase 9: Automated Application Updates & Distribution (Post-MVP)
- [ ] **Self-Updating Client Pipeline:**
  - [ ] Automated update checking via GitHub Releases API / Velopack
  - [ ] Background delta download, changelog presentation, and seamless restart-and-update
- [ ] **Cross-Platform Packaging:**
  - [ ] Automated CI/CD builds for Windows (x64/ARM64), Linux (x64/ARM64), and macOS (Apple Silicon)

---

### 🚀 Phase 10: Model Context Protocol (MCP) Server & AI Assistant Tooling (Post-MVP)
*Goal: Build a Model Context Protocol (MCP) server that connects AI coding and reasoning assistants directly to GordianXI schemas, DAT resources, and runtime APIs to assist players with configuration and developers with addon authoring.*

- [ ] **GordianXI MCP Server Core (`Gordian.Mcp`):**
  - [ ] Standard JSON-RPC 2.0 stdio & SSE transport compliant with the Model Context Protocol (MCP) spec
  - [ ] Contextual prompt templates, resources, and tool definitions with clean schema discovery
  - [ ] Read-only state queries and sandbox execution respecting `ServerAutomationPolicy`
- [ ] **Automation & Gambit Profile Assistant:**
  - [ ] Natural language to Gambit rule compilation (e.g., priority conditions $\rightarrow$ target $\rightarrow$ action mappings)
  - [ ] Gambit profile validation, conflict detection, and role simulation for multi-box swarm coordination
  - [ ] Automated tuning of latency thresholds and animation lock buffers
- [ ] **Addon Development & Runtime Tooling:**
  - [ ] API schema inspection, event signature lookup, and template scaffolding for Lua and JS/TS addons
  - [ ] Addon debugging, log/error diagnostics, and backward-compatibility linting against Windower/Ashita shims
- [ ] **GearSwap & Native Equipment Automation Assistant:**
  - [ ] Direct integration with Phase 4 DAT item/equipment database (stat queries, equipment slots, job restrictions, set bonuses)
  - [ ] GearSwap `.lua` parser, validator, and translator into GordianXI native fast-swap rules
  - [ ] Rule optimization for precast, midcast, aftercast, and situational macro sets
