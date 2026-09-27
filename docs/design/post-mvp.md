# Post-MVP Design

> Design intent for Phases 6-10 (addons, automation, multi-boxing shell, updates, MCP tooling). Issues are opened from here when a phase starts. Status and open work: [ROADMAP.md](../../ROADMAP.md) and GitHub Issues.

## Phase 6: Scripting runtime, addons and package manager

- [ ] **Sandboxed Lua Scripting Engine (`Gordian.Addons`):**
  - [ ] Single Lua VM (NLua / KeraLua) with Windower/Ashita API compatibility shims — the sole supported addon language; no secondary JavaScript/TypeScript runtime
  - [ ] Allowlist-only script environment: every addon executes with a restricted `_ENV` containing exclusively the curated addon API table (below). The dangerous parts of the Lua/NLua standard surface — CLR interop (`luanet`), `os.execute`, raw `io.*`, `require`/`dofile`/`loadstring`, `debug.*` — are never present in that environment in the first place, rather than removed/blocklisted after the fact
  - [ ] Scoped addon storage API (`storage.read_config()`, `storage.write_config(data)`, `storage.log(line)`) as the sanctioned replacement for raw `io.*`: confined to a per-addon subdirectory under `GordianStorage.AddonsDirectory`, with path-traversal validation and a size quota so an addon can persist settings/logs without ever reaching an arbitrary path on disk
  - [ ] Event bus (`on_packet_in`, `on_packet_out`, `on_chat`, `on_zone_change`), zero access to `Gordian.Automation`
- [ ] **3-Tier Menu & Action API for Addon Authors:**
  - [ ] *High-Level Intent API:* Safe, validated one-line triggers (`actions.cast("Cure IV")`, `actions.use_ability("Provoke")`, `inventory.equip()`, `event.choose(index)`).
  - [ ] *Reactive Live State Access:* Continuous, non-blocking read access to live cached game state (`LocalPlayerState`, recasts, inventory, party, world entities) without needing to wait for button click responses.
  - [ ] *Low-Level Raw Packet Injection:* Fallback hook for custom packet crafting (`network.inject_outgoing(opcode, payload)`), securely gated behind `FeatureRestrictions.RawPacketInjection`.
- [ ] **Capability-Based Security & Server Policy Enforcement:**
  - [ ] Addon manifest permission model (`manifest.json` capabilities: e.g., `ui.draw`, `chat.read`, `world.query` vs restricted `action.inject`, `locomotion.override`)
  - [ ] Dynamic API gating tied to `FeatureRestrictions`: when a server restricts automation/combat, the sandbox physically unbinds restricted C# APIs at runtime, defeating name-spoofing trojans (e.g. embedding unauthorized code in whitelisted addon names) without relying on brittle file hashes
  - [ ] Dual trust tiers: cryptographically signed packages from official addon registry vs unsigned local development scripts
  - [ ] *(Future consideration)* Server-sent addon identity allowlist/blocklist: a separate, packet-level mechanism letting server operators permit or block specific addons by name/hash. Distinct from the API-capability sandbox above — this would govern *which addons* may run at all, not *what a running addon* is capable of doing
- [ ] **Addon Ecosystem & Package Manager:**
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
  - [ ] GearSwap `.lua` parser, validator, and translator into GordianXI native fast-swap rules
  - [ ] Rule optimization for precast, midcast, aftercast, and situational macro sets
