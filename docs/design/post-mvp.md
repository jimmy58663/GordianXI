# Post-MVP Design

> Design intent for Phases 6-10 (addons, automation, multi-boxing shell, updates, MCP tooling). Issues are opened from here when a phase starts. Status and open work: [ROADMAP.md](../../ROADMAP.md) and GitHub Issues.

## Phase 6: Scripting runtime, addons and package manager

- [ ] **Sandboxed Lua Scripting Engine (`Gordian.Addons`):**
  - [ ] Dedicated native Lua 5.4 VM per loaded addon via KeraLua P/Invoke: complete fault isolation (crashes never affect other addons or the engine), leak-proof unloading (`lua_close`), and zero .NET GC overhead. Cross-platform native binaries for Windows, Linux, and macOS (x64 and ARM64).
  - [ ] Allowlist-only script environment: every addon executes with a restricted `_ENV` containing exclusively curated API tables. Because KeraLua binds directly to pure ANSI C Lua, CLR reflection (`luanet`) does not exist; dangerous globals (`os.execute`, raw `io.*`, `require`/`dofile`/`loadstring`, `debug.*`) are absent from the sandbox environment at creation.
  - [ ] Scoped addon storage API (`storage.read_config()`, `storage.write_config(data)`, `storage.log(line)`) as the sanctioned replacement for raw `io.*`: confined to a per-addon subdirectory under `GordianStorage.AddonsDirectory`, with path-traversal validation and a size quota.
  - [ ] C#-backed `AddonRegistry` categorized by runtime flavor (`Gordian`, `Windower`, `Ashita`):
    - Windower addons receive `_G.windower` and standard pure-Lua helpers (`config.lua`, `tables.lua`).
    - Ashita addons receive `_G.AshitaCore`, `_G.ashita`, and direct passthrough to GordianXI's native `_G.imgui`.
    - Native Gordian addons receive `_G.gordian` with zero legacy overhead.
  - [ ] Lua 5.1 Backward-Compatibility Shim: a lightweight compatibility module providing the `bit` library (`band`, `bor`, `bxor`, `rshift`, `lshift`), `unpack = table.unpack`, and `loadstring = load` so legacy Windower/Ashita addons written for Lua 5.1/LuaJIT run natively on the modern Lua 5.4 VM.
  - [ ] C# Pub-Sub Event Registry & Opcode Pre-Filtering:
    - Event registrations (`windower.register_event`, `ashita.events.register`, `gordian.register_event`) register callbacks directly with C# dispatch dictionaries.
    - Incoming packets are pre-filtered by opcode in C# before FFI invocation—VMs only wake up if subscribed to that specific opcode, eliminating broadcast overhead.
  - [ ] Targeted Native IPC:
    - Point-to-point direct messaging (`gordian.ipc.send('target_addon', action, payload)`), waking only the recipient VM.
    - Topic-based pub-sub (`gordian.ipc.subscribe('channel', callback)`).
    - Native structured table payloads (no string concatenation or manual serialization required).
    - Legacy `windower.send_ipc_message` scoped strictly to active Windower VMs.
- [ ] **3-Tier Menu & Action API for Addon Authors:**
  - [ ] *High-Level Intent API:* Safe, validated one-line triggers (`actions.cast("Cure IV")`, `actions.use_ability("Provoke")`, `inventory.equip()`, `event.choose(index)`).
  - [ ] *Reactive Live State Access:* Continuous, non-blocking read access to live cached game state (`LocalPlayerState`, recasts, inventory, party, world entities) without needing to wait for button click responses.
  - [ ] *Low-Level Raw Packet Injection:* Fallback hook for custom packet crafting (`network.inject_outgoing(opcode, payload)`), securely gated behind `FeatureRestrictions.RawPacketInjection`.
- [ ] **Capability-Based Security & Server Policy Enforcement:**
  - [ ] Addon manifest permission model (`manifest.json` capabilities: e.g., `ui.draw`, `chat.read`, `world.query` vs restricted `action.inject`, `locomotion.override`)
  - [ ] Dynamic API gating tied to `FeatureRestrictions`: when a server restricts automation/combat, the sandbox physically unbinds restricted C# APIs at runtime, defeating name-spoofing trojans (e.g. embedding unauthorized code in whitelisted addon names) without relying on brittle file hashes
  - [ ] Dual trust tiers: cryptographically signed packages from official addon registry vs unsigned local development scripts
  - [ ] *(Future consideration)* Server-sent addon identity allowlist/blocklist: a separate, packet-level mechanism letting server operators permit or block specific addons by name/hash. Distinct from the API-capability sandbox above — this would govern *which addons* may run at all, not *what a running addon* is capable of doing
- [ ] **Addon Ecosystem & Package Manager** (registry repo, trust model and API reference generation: [distribution.md](distribution.md)):
  - [ ] Central community repository manifest (`gordianxi/addons-index`) tracking verified plugins, versions, and dependencies
  - [ ] In-client Addon Browser: Search, 1-click install, auto-update check, enable/disable toggles
  - [ ] Custom Source Support: Direct Git clone or package URL input for private server or custom addons

## Phase 7: Automation and gambit engine

- [ ] **FF12-Style Rule-Based Gambit Engine (`Gordian.Automation`):**
  - [ ] Priority evaluation tree (Condition $\rightarrow$ Target $\rightarrow$ Action, e.g. `PartyMember.HP < 50%` $\rightarrow$ `Cure IV`)
- [ ] **Multi-Box Swarm Coordination:**
  - [ ] Leader-follower formation tracking, assist targeting, synchronized skillchains / magic bursts
- [ ] **Zero-Drop Action Sequencing:**
  - [ ] Latency-compensating client-side command queueing with animation lock awareness
- [ ] **Killswitch Enforcement:**
  - [ ] Hardwired compliance with `FeatureRestrictions` from `Gordian.Core` (shuts down execution if restricted by private server)

## Phase 8: Multi-boxing desktop shell and UX polish

- [ ] **Dockable Multi-Instance Desktop Shell (`Gordian.App`):**
  - [ ] Tabbed sessions, split-view grid, and floating pop-out windows for secondary characters
- [ ] **Global Hotkey Routing & Broadcasting:**
  - [ ] Broadcast input mode across multiple instances or pass-through to background sessions
- [ ] **Performance & Frame Pacing:**
  - [ ] Background instance throttling (reducing unfocused instances to 15-30 FPS to minimize GPU/CPU usage)
  - [ ] Multi-instance CPU/memory profiling and thread pool allocation tracking

## Phase 9: Automated updates and distribution

Updater constraints, docs site and open questions: [distribution.md](distribution.md).

- [ ] **Self-Updating Client Pipeline:**
  - [ ] Automated update checking via GitHub Releases API / Velopack
  - [ ] Background delta download, changelog presentation, and seamless restart-and-update
- [ ] **Cross-Platform Packaging:**
  - [ ] Automated CI/CD builds for Windows (x64/ARM64), Linux (x64/ARM64), and macOS (Apple Silicon)

## Phase 10: MCP server and AI assistant tooling

*Goal: Build a Model Context Protocol (MCP) server that connects AI coding and reasoning assistants directly to GordianXI schemas, DAT resources, and runtime APIs to assist players with configuration and developers with addon authoring.*

- [ ] **GordianXI MCP Server Core (`Gordian.Mcp`):**
  - [ ] Standard JSON-RPC 2.0 stdio & SSE transport compliant with the Model Context Protocol (MCP) spec
  - [ ] Contextual prompt templates, resources, and tool definitions with clean schema discovery
  - [ ] Read-only state queries and sandbox execution respecting `FeatureRestrictions.ReadGameState`
- [ ] **Automation & Gambit Profile Assistant:**
  - [ ] Natural language to Gambit rule compilation (e.g., priority conditions $\rightarrow$ target $\rightarrow$ action mappings)
  - [ ] Gambit profile validation, conflict detection, and role simulation for multi-box swarm coordination
  - [ ] Automated tuning of latency thresholds and animation lock buffers
- [ ] **Addon Development & Runtime Tooling:**
  - [ ] API schema inspection, event signature lookup, and template scaffolding for Lua addons
  - [ ] Addon debugging, log/error diagnostics, and backward-compatibility linting against Windower/Ashita shims
- [ ] **GearSwap & Native Equipment Automation Assistant:**
  - [ ] Direct integration with Phase 4 DAT item/equipment database (stat queries, equipment slots, job restrictions, set bonuses)
  - [ ] Native GordianXI GearSwap Lua addon: pure Lua addon targeting Gordian's native API (`gordian.equip_set`) with direct in-memory DAT item database integration, eliminating legacy raw packet crafting, `packets.lua`, and sleep loops
  - [ ] Backward-compatible GearSwap `.lua` profile loader and validator for precast, midcast, aftercast, and situational macro sets
  - [ ] Atomic multi-slot equipment batching executed directly in C# network buffers
