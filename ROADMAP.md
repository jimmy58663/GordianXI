# GordianXI Project Roadmap & Milestone Tracker

## Current State Summary
- **Target Framework:** .NET 10 (C# 14) + Avalonia UI 12.1.2 + ImGui.NET
- **Test Status:** 829 Passing Unit Tests (`dotnet test`)
- **Active Focus:** Phase 5E: UI Layering, Stock DAT 2D HUD & ImGui In-Game Overlays (MVP Completion)
- **North Star Goal:** High-performance, clean-room 64-bit cross-platform client replacement for Final Fantasy XI.

---

## 🏆 MVP Definition (Minimum Viable Product)
> **Goal:** A player can launch GordianXI, connect directly to a LandSandBoat private server, authenticate through a character lobby (create/select/delete a character), spawn into a zone with 3D world geometry and character models rendered, control movement and camera using customizable Keyboard/Mouse or Gamepad while being correctly blocked by walls/terrain/water instead of clipping through them, execute basic actions and slash commands via an interactive CLI console or hotkeys, view chat/vitals/inventory, and cross zonelines to new map servers without dropping session state.
> 
> *MVP includes Phases 1 through 5, including sub-phases 5A-5G — World Collision & Navigation (5F) and the Character Lobby (5G) are MVP-blocking, since a client that lets players clip through geometry or cannot create/select a character without an external tool does not meet the MVP goal above. Phase 5H (Audio) and Phases 6 through 10 represent Post-MVP extensions.*

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

### ✅ Phase 3: Complete LSB Packet Engine & Zone Transitions (MVP Core)
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
- [x] **Declarative Zero-Allocation Packet Registry Expansion:**
  - [x] **Session & Zone Lifecycle:** S2C `0x00A`, `0x00B`, `0x008`, `0x05B`, `0x065`; C2S `0x00A`, `0x00C`, `0x00D`, `0x011`, `0x015`, `0x05B`, `0x05C`, `0x05E`, `0x0E7`
  - [x] **Entity & World State:** S2C `0x00D` (PC update), `0x00E` (NPC/Mob update), `0x01B` (Job info), `0x037` (Char status), `0x061`/`0x062` (Stats), `0x0DF` (Group attr), `0x076` (Effects), `0x077` (Vis); C2S `0x00F` (Target interact), `0x016`/`0x017` (Char reqs), `0x061` (Cli status req)
  - [x] **Communication & Chat:** S2C `0x017` (Chat), `0x009` (SysMsg), `0x047` (Translate), `0x0CC` (LS Msg); C2S `0x0B5` (Chat send), `0x0B6` (Tell), `0x0B7` (Assist), `0x0E0`-`0x0E4` (LS mgmt)
  - [x] **Party & Alliance Networking:** S2C `0x0DC` (Group solicit/invite), `0x0C8` (Group table), `0x0DD` (Group list/member info), `0x0DE` (Invite reset); C2S `0x06E` (Invite req), `0x06F` (Leave), `0x070` (Disband), `0x071` (Kick), `0x074` (Invite response accept/decline)
  - [x] **Client-Side Command Router:** Full slash command routing (`/join`, `/decline`, `/pcmd`, `/invite`, `/leave`, `/disband`, `/tell`, `/say`, `/party`, `/shout`, `/yell`, `/linkshell`, `/echo`) and server `!` command pass-through
  - [x] **Standard FFXI System Message Resolution:** `StandardMessages` (`MsgStd`) mapping standard message IDs to clean, authentic in-game text
  - [x] **Combat & Action Pipeline:** S2C `0x028` (Combat action), `0x029`/`0x02D` (Battle msg), `0x030` (Effects), `0x0AA` (Magic), `0x0AC` (Commands), `0x119` (Recasts); C2S `0x01A` (Action req: attack, cast, ability), `0x05D` (Emotes), `0x0F1` (Buff cancel), `0x11D` (Jump)
  - [x] **Inventory & Economy:** S2C `0x01C`-`0x020` (Inventory items & attrs), `0x021`-`0x025` (Trade), `0x026` (Subcontainers), `0x03C`-`0x03F` (Shops), `0x04C` (AH), `0x050` (Equipment), `0x082`-`0x086` (Guilds), `0x105`-`0x10A` (Bazaar), `0x113`/`0x118` (Currencies), `0x116`/`0x117` (Equip sets); C2S `0x028` (Item dump), `0x029` (Move), `0x032`-`0x034` (Trade), `0x036` (Transfer), `0x037` (Use), `0x03A` (Stack), `0x03B` (Subcontainer), `0x04E` (AH), `0x050`-`0x053` (Equip/Lockstyle), `0x083`-`0x085` (Shops), `0x104`-`0x10B` (Bazaar)
  - [x] **Progression, Quests & Menus:** S2C/C2S for Mog House (`0x0CB`, `0x0FA`-`0x100`), Party/Alliance (`0x0C8`, `0x0DC`-`0x0E2`, `0x11C`), Merits/Job Points (`0x08C`/`0x08D`, `0x0BE`-`0x0C1`), RoE (`0x10C`-`0x10E`, `0x111`/`0x112`), Fishing (`0x066`, `0x110`, `0x115`), Chocobo Racing (`0x069`, `0x073`/`0x074`, `0x09B`), Unity (`0x063`, `0x110`, `0x116`-`0x118`), Conquest/Campaign (`0x05E`, `0x071`), and Cutscene Events (`0x032`-`0x036`, `0x05B`/`0x05C`)
- [x] **Network Diagnostics & Datagram Telemetry:**
  - [x] Real-time atomic datagram counters (Inbound/Outbound packets/sec, bytes/sec, rolling throughput window)
  - [x] Packet sequence gap tracking & drop detection for UDP streams
  - [x] Zero-allocation dispatch latency profiling (microsecond-level decode time)

#### ⚠️ Known Gaps (Packet Audit, 2026-09-20)
*A full opcode-level audit found the coverage claim above is accurate at the decode/encode level (81 S2C decoders, 64 C2S builders are genuinely wired and reachable), but several packets that are "handled" don't actually do anything useful yet. Tracked here instead of re-opening Phase 3 as incomplete, since the wire-format work itself is done — what's missing is wiring the result into state/UI.*
- [ ] Register orphaned decoder `S2C_0x073_ChocoboToteboard` (`ProgressionPackets.cs`) in `ProgressionPacketModule.Register()` — fully implemented but never added to the dispatcher, so it never fires
- [ ] Wire up 14 fully-built but never-called C2S builders (currently dead code, meaning these player actions are impossible in the running client despite the packet layer existing): Auction House bid/buy (`0x04E`), subcontainer/mannequin equip (`0x03B`), equipset check & lockstyle (`0x052`/`0x053`), key item reading (`0x064`), Mog House room-is & furniture layout (`0x0CB`/`0x0FA`), Chocobo race entry (`0x09B`), Unity quest accept & toggle (`0x117`/`0x118`); also delete the 4 duplicate/legacy builders for opcodes already covered elsewhere (`HandshakePackets.BuildGameOkSubPacket`, `BuildNetEndSubPacket`, `LifecycleOutboundPackets.BuildEventEnd`, `BuildEventEndXzy`)
- [ ] Persist the following decoded-but-discarded S2C data into a `World/*State` cache instead of only logging it: status effect apply/expire + duration (`0x030 Effect` — blocks any buff/debuff countdown UI), Auction House state (`0x04C Auc` — no AH state object exists at all), Guild shop & Bazaar transaction results (`0x082`-`0x085`, `0x106`, `0x108`-`0x10A`), Equipset validation/result (`0x116`/`0x117`), linkshell comlink (`0x0E0`), party invite result (`0x11D`), NPC dialog/menu text (`0x036 TalkNum`), Mog House operation result (`0x0FA`)
- [ ] Add a `Gordian.Core.Tests` `Network/Packets` regression test asserting every decoder defined in `Network/Packets/*.cs` is reachable from `PacketDispatcher` (would have caught the orphaned `0x073` decoder automatically)

---

### ✅ Phase 4: World State, DAT Resource Pipeline & Modular VFS (MVP Core)
- [x] **Thread-Safe Spatial Entity Store (`Gordian.Core/World`):**
  - [x] Tracking for LocalPlayer, other PCs, NPCs, Monsters, Pets, and Trusts
  - [x] Indexing by Server ID (`uint32`) and Zone Target Index (`uint16`)
  - [x] Fast 3D spatial partitioning (Uniform Grid / BVH) for distance, cone, and line-of-sight queries
  - [x] Dead-reckoning & position interpolation between 250ms network ticks
- [x] **Game State Caches:**
  - [x] Multi-container inventory cache (Inventory, Wardrobes 1-8, Satchel, Sack, Case, Safe, Storage)
  - [x] Active player vitals (HP/MP/TP), base attributes, equipment loadout, buff/debuff timers
- [x] **FFXI DAT Binary Decoders (`Gordian.Core/Resources`):**
  - [x] Clean-room decoders for ROM directory DAT files (string tables, item tables, spell/ability tables)
  - [x] Zone collision meshes, terrain geometry, entity models, and animation tables
- [x] **Modular Virtual File System (VFS) & Asset Overrides (XIPivot Architecture):**
  - [x] Tiered VFS search paths (portable local root + `%LOCALAPPDATA%` user storage)
  - [x] XIPivot-compatible legacy DAT overlay scanner (`resources/dats/`) with uppercase invariant indexing
  - [x] Modern asset pack scanner (`resources/assets/`) with `manifest.json` parsing and DAT aliasing (`.glb` replacing `.DAT`)
  - [x] Master `vfs.json` load order, priority stacking, and pack auto-discovery
  - [x] Safe debounced runtime hot-reloading with master toggle
  - [x] Modder documentation and reference manifests distributed in `resources/`
- [x] **State & Memory Health Telemetry:**
  - [x] Managed heap & GC pressure counters (.NET 10 Gen 0/1/2 collection tracking, heap allocation velocity)
  - [x] Spatial partition & uniform grid query duration tracking with entity dead-reckoning cycle time benchmarking

---

### ⏳ Phase 5: Viewport Rendering, CLI Console & Input Subsystem (MVP Completion)
- [x] **Interactive Character Command Console (CLI) & Action Subsystem:**
  - [x] Unified `PlayerActionService` in `Gordian.Core` (typed methods for combat, magic, abilities, targeting, and locomotion)
  - [x] In-client interactive console tab in `Gordian.App` with history navigation (Up/Down arrows), color-coded output, autoscroll, and slash commands (`/pos`, `/target`, `/attack`, `/ws`, `/magic`, `/vitals`, `/nearby`, `/moveto x y z`)
  - [x] Command permission & policy gating: distinction between standard vanilla commands (always allowed), server admin passthrough (`!pos`, `!zone`), and synthetic locomotion gated behind `FeatureRestrictions.Movement`
  - [x] Console text selection & copy support (`SelectableTextBlock`), robust comma/parenthesis coordinate parsing for `/moveto`, categorized command discovery (`/help`, `/commands`), GM command discovery (`/gmhelp`, `/gmcommands`) gated by GM permissions (`"You are not a GM."`), and `FeatureRestrictions` filtering
  - [x] Headless/early verification of movement, combat, and zoning against live server without requiring 3D rendering
  - [x] Robust locomotion keepalive synchronization and automated C2S 0x016 CharReq entity discovery for reliable live server and Windower multi-session pairing
- [x] **Cross-Platform Input Subsystem:**
  - [x] **Keyboard & Mouse:** Default layouts for FFXI Compact (WASD + IJKL camera) and FFXI Full (Numpad) with smart text-input isolation
  - [x] **Gamepad / Controller:** Full XInput, DirectInput, and SDL/Silk gamepad support (Xbox, PlayStation, generic HID) with deadzone, rumble, and axis calibration
  - [x] Rebindable control mapping engine with JSON persistence and modifier key support (`keybinds.json`)
  - [x] Real-time 60Hz locomotion and camera controller updating `WorldEntity` coordinates and dispatching to server Pos loop
  - [x] Smooth network locomotion synchronization: retail-accurate `0x015` packet protocol (accumulating 60 FPS Run Count in `MoveFlame`, zero `MovTime`, `0x0001` stationary stance), non-starving outbound queue bundling, and high-precision `Stopwatch` delta-time calibration guaranteeing authentic 5.0 yalms/sec running across remote clients (Windower/retail)
  - [x] Dedicated "Controls & Input" dashboard tab with live input monitor and preset switcher
- [x] **Phase 5A: Graphics Context & Decoupled Multi-Box Viewport Architecture:**
  - [x] Integrate Veldrid, Veldrid.SPIRV, and Veldrid.ImGui into client infrastructure
  - [x] Avalonia `NativeControlHost` cross-platform viewport control (`VeldridViewportControl`) supporting Windows (`HWND`), Linux (`X11`/`Wayland`), and macOS (`NSView`)
  - [x] Multi-backend auto-selection (Direct3D 11 on Windows, Vulkan on Linux/Windows, Metal on macOS, OpenGL fallback)
  - [x] Resilient 60/120 FPS render loop with device recreation on viewport resize
  - [x] Verification test scene: textured spinning 3D test cube and color gradient clearing to confirm GPU pipeline integrity
  - [x] **Decoupled 3D Rendering Window (`ViewportWindow`) & Lifecycle Coordinator (`ViewportWindowManager`):**
    - [x] Gameplay graphics run in a separate hardware-accelerated window, preserving `MainWindow` as the central Control Panel (profiles, packet inspection, state diagnostics, chat console, and display settings).
    - [x] Three display modes: **Borderless Window** (default, borderless work area alignment), **Windowed** (movable & resizable with min dimension safeguards), and **Fullscreen** (`WindowState.FullScreen`, toggleable via `F11`).
    - [x] Multi-option character tab switcher styles: **Floating Pill** (top-center glass dynamic island - default), **Top Ribbon** (auto-hiding), **Side Rail** (vertical party deck with live HP/MP vitals), and **Hotkeys Only** (`Ctrl+Tab` / `Ctrl+Shift+Tab`).
    - [x] **Multi-Monitor Tear-Off / Pop-Out (`⧉`):** Single master Veldrid `GraphicsDevice` context driving multiple independent window `Swapchain` instances across monitors with zero VRAM waste or asset duplication.
    - [x] **Picture-in-Picture (PiP) Multi-Box Swarm Streaming:** Real-time thumbnail sub-viewports for up to 5 background characters (1 main + 5 alts) with 1-click `⇄` viewport promotion and throttled background render rates.
- [x] **Phase 5B: Camera Subsystem & Zone Terrain Renderer:**
  - [x] Third-person orbital follow camera, freecam, and first-person mode integrated with [PlayerLocomotionController](file:///g:/git/GordianXI/src/Gordian.Core/Input/PlayerLocomotionController.cs)
  - [x] GPU vertex & index buffer streaming for Phase 4 `ZoneGeometry` / `MeshGroup` models
  - [x] Texture palette decoding and Veldrid GPU texture sampler caching
  - [x] Directional sun/moon lighting, ambient color, and authentic FFXI distance fog shader pipeline
- [x] **Phase 5C: Entity Models & Modular Equipment Assembly:**
  - [x] Dynamic character mesh decoder stitching Race + Face + 5 Armor Slots (Head, Body, Hands, Legs, Feet) + Weapons from distinct DATs
  - [x] Bind-pose entity rendering at live `WorldEntity` coordinates
  - [x] NPC, Monster, and Trust model rendering from DAT resource caches
- [x] **Phase 5D: Skeletal Animation Engine:**
  - [x] FFXI bone hierarchy & joint matrix tree parser (Section `0x2B` `SkeletonAnimationDecoder`, GPU joint-palette skinning in `EntityRenderer`/`ZoneShaders`)
  - [x] Quaternion normalized-lerp (NLERP, matching documented retail behavior) rotation & translation keyframe interpolation (`AnimationClip.TrySample`, `SkeletonPoseEvaluator.EvaluatePose`)
  - [x] Full-body locomotion & battle motion packs enabled by default (`EnableSpeculativeMotionPacks = true`). Resolved origin blob collapse root cause (additive translation kinematics $t = t_{\text{bind}} + \Delta t$, Hamilton rotation composition, normalized multi-rate track sampling phase alignment, and folder-carry legacy battle pack file number resolution). Resting locomotion is preserved against battle locomotion clobbering, and in-game renderer supports battle stance (`"btl"`) and death (`"ded"`).
  - [x] Authentic entity animation state & motion resolution: monster suffix resolution (`idl0`, `wlk0`, `run0`), packet `MovTime` parsing ensuring stationary NPCs idle instead of running in place, and zero-jitter camera-synchronized player mesh positioning with boundary-accurate keyframe interval interpolation.
  - [x] Generalized NPC stance & dual-channel animation blending (`NpcStanceResolver`, `SkeletonPoseEvaluator.EvaluateBlendedPose`, `EntityAnimationState`): convention-based auto-discovery across multi-stance mob families (Omega quadruped/biped, Uragnite shell, Hpemde open/submerged, Adamantoise guard), one-shot transition sequencing (`sp00`/`sp10`/`sp20`/`sp30`), strict priority interruption (`Death` > locomotion > stance transition), and zero-distortion local-space NLERP pose blending.
- [ ] **Phase 5D.1: Transient Combat & Action Animation Dispatcher:**
  - [ ] **Combat Action Event Queue (`ActionPlaybackQueue`):** Bridge incoming S2C `0x028` (`S2C_0x028_CombatAction`) events from `CombatPacketModule` into entity animation dispatchers.
  - [ ] **Weapon-Specific Attack Swing Resolution:** Resolve weapon swing animation clips (`atk`, `atk0`, `atk1`) based on the entity's equipped weapon type / combat skill (Hand-to-Hand, Daggers, 1H/2H Swords, Axes, Scythes, Polearms, Katanas, Clubs, Staves, Archery, Marksmanship).
  - [ ] **One-Shot Transient Playback & Battle Stance Recovery:** Play transient attack swings, weaponskills (`ws`), casting gestures (`cas`), and job abilities to completion before seamlessly returning to the continuous `"btl"` ready stance.
  - [ ] **Damage & Hit Reactions:** Trigger brief target hit-reaction flinch clips (`dam`, `hit`) when incoming attack/damage battle messages and actions occur.
- [ ] **Phase 5E: UI Layering, Stock DAT 2D HUD & ImGui In-Game Overlays:**
  - [ ] **3-Tier Rendering Architecture:**
    - [x] *Tier 1 (3D Scene):* Veldrid terrain, skybox/celestial sky dome (`SkyDomeRenderer`), entity models, directional sun/moon lighting, and authentic FFXI distance fog pass. Clean-room DAT Section `0x2F` Environment decoder (`EnvironmentDecoder`, `ZoneEnvironmentData`) supporting time-of-day keyframe extraction, 8-slice sky dome gradients, and time-of-day cycling (`F10` shortcut / `CycleTimeOfDay`). Fog calibration overhaul with authentic clear visibility presets (Day, Dusk, Night), soft atmospheric haze (Overcast), distant horizon projection for retail `FogStart = 0` keyframes, shader `FogParams.y > 0.0` guards, and runtime fog toggle (`Ctrl+F10` shortcut / `ToggleFog`). Frame composition decoupled into a 3-tier presentation pipeline (`RenderTier1_Scene3D` -> `RenderTier2_StockUi` -> `RenderTier3_ImGuiOverlays`).
    - [ ] *Phase 5E (Tier 1) Sky, Weather & Celestial Subsystem (5 Modular Chunks):*
      - [x] **Chunk 1: Sand & Terrain Lighting Balance:** Calibrated diffuse and ambient lighting coefficients (`0.5 * amb + 0.5 * df0`) in terrain shaders to prevent overexposure of light-colored ground/sand while maintaining shadow depth.
      - [x] **Chunk 2: Clean Sky Dome & Rasterizer Dithering:**
        - Authentically rendered 8-slice vertex-interpolated hemispherical sky dome (`SkyDomeRenderer`).
        - Integrated triangular screen-space dither (`+/- 0.5/255.0` in `SkyDomeFragmentShaderGlsl`) emulating PS2 GS / D3D8 hardware rasterizer dithering, eliminating 8-bit color quantization banding in dark gradients.
        - Synchronized horizon clear colors and distance fog across Day, Dusk, Night, and Overcast.
        - Eliminated the overhead concentric ring artifact at dusk by separating the inverted `stardust` dome geometry from additive celestial passes.
      - [ ] **Chunk 3: Celestial Night Sky (Stars & Moon) [Active Focus]:**
        - [x] Decoupled celestial night bodies from daytime sun and cloud layers via discrete renderer flags (`EnableWeatherCelestialBodies = true`, `EnableCelestialMoon = true`, `EnableCelestialSun = false`, `EnableWeatherClouds = false`).
        - [x] Lunar disc (`moonsphere`) and soft lunar halo (`kasa`) billboard rendering with dedicated additive shader and authentic scale (`<20, 20, 20>`).
        - [x] Gated the inverted 2,004-triangle `stardust` / Milky Way shell (`EnableMilkyWay = false`) to isolate the true 666-star billboard starfield (`sta1`).
        - [x] Zeroed celestial UV drift offsets to prevent star/moon billboards from moving across the sky like clouds.
        - [x] Corrected `SunDirection` in `ZoneEnvironmentSettings.CreateNight()` to negative Y (`-0.7f`).
        - [ ] **Next Step for Fresh Session:** Investigate star vertex color / texture format decoding for `star_rivstar01`: determine why star billboards render with isolated red, magenta, and blue chromatic tints instead of brilliant silver-white pinpricks (verify texture color channels / palette indices and vertex RGBA multipliers in `ZoneShaders.FragmentShaderWeatherSkyGlsl`).
      - [ ] **Chunk 4: Dynamic Cloud Shells:** Weather-gated dynamic cloud layers (`clod`, `suny`, `fine`, `mist`) drifting with authentic UV velocities and daylight-modulated ambient lighting.
      - [ ] **Chunk 5: Solar & Horizon Alignment:** Daytime solar disc (`sunsphere`), radiant corona flare, and horizon alignment opposite the moon along the seasonal ecliptic arc.
      - [x] **Step: Base Sea-Level Ocean Water Plane:** Implement an ocean water plane at sea level ($Y=0.0$) in Pass 5 so that island beaches and bays show translucent ocean water over the seabed while awaiting the particle generator engine.
      - [x] **Step: Live Weather & Water Dynamic Simulation:** Dynamic Vana'diel time clock and weather synchronization (`VanaTime`, `WorldState.WeatherId`, `0x00A` login ack, `0x057` weather packet). Automatic per-frame keyframe interpolation and sky dome update in `VeldridViewportControl`. Water surface particle generator instancing (Phase 2b in `ZoneDataLoader` for `shi1..shi5`, `hum1`, `mizu`, `hna0`), per-submesh and zone master UV scroll animations (`UVScrollVelocity`), and high-definition ocean water plane tuning (tile UV 200) matching retail FFXI.
    - [ ] *Tier 2 (Stock FFXI 2D UI):* Authentic DAT-driven menu boxes (blue marble), finger cursor hand, targeting brackets, vitals gauges, status icons, and dialog text.
    - [ ] *Tier 3 (ImGui Overlays & Addons):* Modern translucent HUD (`WindowRounding = 6.0f`), performance profiling overlay, radar/minimap, and addon plugin canvases.
  - [ ] **Modular Stock UI Suppression (`StockUiVisibilityState`):**
    - [ ] Granular visibility flags for each stock element (Target Bar, Player Vitals, Party Frames, Alliance Frames, Buff Bar, Menus, In-Game Chat).
    - [ ] Allows addon authors and players to selectively or entirely disable stock HUD elements to run custom ImGui replacements (e.g. XIVParty, modern target frames, or clean cinematic mode) without visual overlap.
  - [ ] Viewport performance overlay: FPS counter, frame pacing graph, draw call counters, and GPU pass timings
- [ ] **Phase 5F: World Collision, Terrain Navigation & Ground Physics (MVP Completion — Blocking):**
  - [ ] **Ground Height Sampling:** Raycast/heightfield-sample the local player's Y position each locomotion tick against decoded `ZoneGeometry`/`MeshGroup` collision triangles. Confirmed gap: `PlayerLocomotionController.UpdateLocomotion` currently only ever writes X/Z from input and never touches Y, so the player has no floor at all today.
  - [ ] **Horizontal Wall/Obstacle Collision:** Sweep or AABB-test the intended movement vector against nearby `MeshGroup` triangles before committing a position delta, instead of applying `dx`/`dz` unconditionally as today.
  - [ ] **Server Position Reconciliation Backstop:** Replace the current hard skip of server-authoritative position updates for the local player (`EntityPacketModule.cs` ignores every `0x00D`/`0x0DF` position update where `UniqueNo == _localPlayer.ServerId`) with a tolerant snap-back/correction so LSB's own server-side collision can catch client-side clipping instead of being silently discarded.
  - [ ] **Remote Entity Collision:** Extend `WorldState.ProjectPosition` dead-reckoning to respect the same collision surface for other players/NPCs/monsters, not just the local player.
  - [ ] **Water/Ocean Plane Detection:** Detect open-water surfaces to gate swim state, speed, and (once implemented in Phase 5H) swim animation/sound, instead of letting the player walk across or through water.
  - [ ] **Collision Acceleration Structure:** Reuse/extend `SpatialPartitionGrid` (or a zone-local BVH over `MeshGroup` bounds) for collision queries — a brute-force per-tick triangle scan against the full zone mesh will not hold a 60Hz budget.
  - [ ] Dedicated `Gordian.Core.Tests` `World/Collision` test coverage (no `Physics`/`Collision` test namespace currently exists).
- [ ] **Phase 5G: Character Lobby, Creation & Deletion (MVP Completion — Blocking):**
  - [ ] **Research Task:** Determine whether retail's character-select/creation lobby (background, race/face preview models, menu chrome) is DAT-driven or hardcoded in `ffxi.exe`/`pol.exe`; identify the specific ROM/DAT section(s) if DAT-driven, per the clean-room boundary rules in `AGENTS.md`.
  - [ ] Clean-room decode of any identified lobby DAT resources (layout, textures, preview model refs, button hit regions) — cite the reference source in XML doc-comments per `AGENTS.md` protocol-attribution standards if community research (e.g. LandSandBoat's login/char-select handling) informs the wire format.
  - [ ] Render the lobby/character-select screen: existing character list, race/face/job preview, and navigation.
  - [ ] **Character Creation Flow:** race, face, starting nation, name entry, live appearance preview.
  - [ ] **Character Deletion Flow:** confirmation step, matching LSB's delete-code/security flow if one is enforced server-side.
  - [ ] Wire lobby actions (list/create/delete/select-and-enter-world) to LSB login-server requests, extending `LsbLoginClient`.
  - [ ] **Loading Screen:** identify whether retail uses a DAT-sourced loading-screen asset (background art, progress indicator) and implement a loading/transition UI state for character-select → zone-in and for zone-to-zone transitions — `PerformZoneTransitionAsync` currently has no visual loading state at all.
  - [ ] Integrate the lobby ahead of the existing `ProxyStager`/Named Pipe handoff flow without breaking the current direct-to-zone "saved profile" fast path used by `Local (Cybin)`-style launches.
- [ ] **Phase 5H: Audio & Sound Engine (Legacy Parity — Non-Blocking):**
  - [ ] Select a cross-platform managed audio backend (must avoid Windows-only APIs per `AGENTS.md`; no audio library of any kind is referenced anywhere in the codebase today).
  - [ ] Clean-room decode of FFXI DAT sound resources (footstep sets, spell/weaponskill/ability SFX, UI cues, ambient zone loops, BGM tracks), if stored in DAT containers.
  - [ ] Footstep & movement SFX tied to `PlayerLocomotionController`/animation state.
  - [ ] Combat/action SFX tied to `CombatPacketModule` action/effect events (`0x028`/`0x030`/`0x0AA`).
  - [ ] Ambient zone loops & BGM playback tied to `WorldState.ZoneChanged`.
  - [ ] UI/menu sound cues (target, cursor move, confirm, cancel).
  - [ ] Master/category volume mixing (SFX/BGM/Ambient/UI) with persisted settings.

---

### 🚀 Phase 6: Scripting Runtime, Central Addons & Package Manager (Post-MVP)
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

---

### 🚀 Phase 7: Automation & Gambit Engine (Post-MVP)
- [ ] **FF12-Style Rule-Based Gambit Engine (`Gordian.Automation`):**
  - [ ] Priority evaluation tree (Condition $\rightarrow$ Target $\rightarrow$ Action, e.g. `PartyMember.HP < 50%` $\rightarrow$ `Cure IV`)
- [ ] **Multi-Box Swarm Coordination:**
  - [ ] Leader-follower formation tracking, assist targeting, synchronized skillchains / magic bursts
- [ ] **Zero-Drop Action Sequencing:**
  - [ ] Latency-compensating client-side command queueing with animation lock awareness
- [ ] **Killswitch Enforcement:**
  - [ ] Hardwired compliance with `FeatureRestrictions` from `Gordian.Core` (shuts down execution if restricted by private server)

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
  - [ ] Read-only state queries and sandbox execution respecting `FeatureRestrictions.ReadGameState`
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
