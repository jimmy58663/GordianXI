# Post-MVP Design

> Design intent for Phases 6-10 (addons, automation, multi-boxing shell, updates, MCP tooling). Issues are opened from here when a phase starts. Status and open work: [ROADMAP.md](../../ROADMAP.md) and GitHub Issues.

## Phase 6: Scripting runtime, addons and package manager

- [ ] **Sandboxed Lua Scripting Engine (`Gordian.Addons`):**
  - [ ] Dedicated native Lua 5.4 VM per loaded addon via KeraLua P/Invoke: fault isolation for Lua errors, leak-proof unloading (`lua_close`) and no managed Lua heap. Cross-platform native binaries for Windows, Linux, and macOS (x64 and ARM64); decide who builds and signs them and add KeraLua and Lua to `THIRD_PARTY_NOTICES.md` when the package is introduced. Packet payloads cross the P/Invoke boundary by copy (or a read-only userdata view); the choice must respect the packet pipeline's no-allocation rule.
  - [ ] Allowlist-only script environment: every addon executes with a restricted `_ENV` containing exclusively curated API tables. Because KeraLua binds directly to pure ANSI C Lua, CLR reflection (`luanet`) does not exist; dangerous globals (`os.execute`, raw `io.*`, `require`/`dofile`/`loadstring`, `debug.*`) are absent from the sandbox environment at creation. Binary chunks are never loadable: every compile path uses text mode only (`mode = "t"`), since Lua bytecode loading is a known sandbox escape.
  - [ ] Per-VM resource limits, so one addon cannot hang or exhaust the engine: an instruction-count hook (`lua_sethook`) for CPU time per callback, a custom allocator (`lua_newstate`) for a memory cap, and a stack-depth limit. A limit breach unloads that addon only.
  - [ ] Scoped addon storage API (`storage.read_config()`, `storage.write_config(data)`, `storage.log(line)`) as the sanctioned replacement for raw `io.*`: confined to a per-addon subdirectory under `GordianStorage.AddonsDirectory`, with path-traversal validation and a size quota.
  - [ ] Runtime flavors, detected **only by the top-level directory** an addon is installed under: `addons/gordian/`, `addons/windower/`, `addons/ashita/`. The flavor is never read from the addon's own files (manifest or script), so an addon cannot claim a flavor with a larger API surface; symlinks that leave the flavor directory are rejected. The flavor fixes the VM's environment at creation and never changes. A C#-backed `AddonRegistry` tracks addons per flavor:
    - Windower addons receive `_G.windower` and standard pure-Lua helpers (`config.lua`, `tables.lua`), plus the 5.1 shim.
    - Ashita addons receive `_G.AshitaCore`, `_G.ashita` and `_G.imgui` (an adapter onto `gordian.imgui`, see below), plus the 5.1 shim.
    - Native Gordian addons receive `_G.gordian` and run on plain Lua 5.4 with no shim.
    - **No mixing:** an addon never loads more than one flavor's API. A Windower addon that wants Ashita's imgui, or any Gordian feature, is ported to a Gordian addon. Touching a global from another flavor fails at load time with an error naming the missing global.
  - [ ] Lua 5.1 Backward-Compatibility Shim, loaded **only** into the Windower and Ashita flavors, so legacy addons written for Lua 5.1/LuaJIT run on the Lua 5.4 VM. Contents, to be confirmed against a corpus of real addons before the phase is called done:
    - `bit` library: `band`, `bor`, `bxor`, `bnot`, `lshift`, `rshift`, `arshift`, `rol`, `ror`, `tobit`, `tohex`.
    - Globals: `unpack = table.unpack`, `loadstring` (bound to the addon's sandboxed `_ENV`, text mode only), `setfenv`/`getfenv` emulation over `_ENV`, `table.getn`, `math.pow`, `string.gfind`, `module`/`package.seeall` emulation where feasible.
    - Integer/float semantics (the 5.3+ integer subtype changes `/`, `//` and number formatting) are a known compatibility risk for packet bit-math. Cover them in tests rather than assume parity.
    - LuaJIT `ffi` is out of scope unless a needed Ashita addon requires it.
  - [ ] **`gordian.imgui`:** a near-1:1 binding of Dear ImGui over ImGui.NET, authored from Dear ImGui's own API (MIT) and not from Ashita's binding (GPL), with a denylist for anything that touches the host: no `.ini` persistence (window state goes through `storage`), no file dialogs, no clipboard, and no raw texture paths or GPU pointers (textures are opaque handles from a Gordian API). Each addon gets its own ID-stack scope, so two addons using the same window title do not collide, and draws run inside the HUD frame on the render thread under a per-frame draw budget. The pinned ImGui.NET version is part of the compatibility contract. The Ashita flavor's `imgui` is a thin adapter translating Ashita-style calls onto `gordian.imgui`, covering what popular Ashita addons need, and lives with the shims so it can be dropped later. Windower's own text/prim UI is separate and not ImGui.
  - [ ] C# Pub-Sub Event Registry & Opcode Pre-Filtering:
    - Event registrations (`windower.register_event`, `ashita.events.register`, `gordian.register_event`) register callbacks directly with C# dispatch dictionaries.
    - Incoming packets are pre-filtered by opcode in C# before FFI invocation, so a VM only wakes up if subscribed to that specific opcode.
    - The legacy API surfaces are implemented clean-room from public documentation and cited in XML doc-comments (Windower and Ashita are copyleft; see `AGENTS.md`).
  - [ ] Targeted Native IPC, **isolated per flavor**: messages never cross flavors, so Gordian addons cannot call legacy addons and legacy addons cannot call Gordian ones.
    - `gordian.ipc`: point-to-point messaging (`gordian.ipc.send('target_addon', action, payload)`), waking only the recipient VM, and topic pub-sub (`gordian.ipc.subscribe('channel', callback)`), reaching Gordian addons only.
    - Legacy `windower.send_ipc_message` is scoped strictly to active Windower VMs (an Ashita equivalent, if needed, likewise to Ashita VMs).
    - Payloads are structured tables deep-copied between Lua states (tables cannot be shared across VMs). Functions, userdata and cyclic references are rejected, and size and rate limits apply so IPC cannot bypass the sandbox quotas.
  - [ ] `Gordian.Addons` has no access to `Gordian.Automation`; nothing in the event bus, IPC or `gordian.equip_set` may become a route into it.
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
