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
  - [x] NPC-only child races (look race 29 Mithra kitten, 30 girl, 31 boy; 115 server NPCs such as Southern San d'Oria's Authere and Blendare): skeletons `ROM/61/110`, `ROM/61/58`, `ROM/61/85` carry their own idle / walk / run clips, and each outfit slot resolves to `slotBase + modelId` (0-19 Hume, 20+ Elvaan variants).
  - [ ] **Model-embedded effect routines:** some NPC models are drawn mostly by particle effects inside their own DAT, played by the model's Section 0x07 routines (e.g. the Home Point crystal, model 51 `ROM/3/25.DAT`: 13 generators, particle meshes and sprite sheets started by routines `bind` / `aper`; its skeleton mesh is only a small placeholder). Also covers actor-attached weather effects (e.g. `weat/clod/tobi` birds). Needs actor-attached generators on the zone particle runtime; shares machinery with spell and ability effects.
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
  - [ ] **Knockback Playback:** Knockback is not a server placement: it is the upper 3 bits of each target result's `scale` field in S2C `0x028` (shared with hit distortion), and the client slides the character itself, then reports the new position via `0x015` (LandSandBoat trusts it). Levels 1-7 carry push vector / damper / duration (LSB `enums/action/knockback.h`: e.g. Level 1 = vec 0.083, damper 0.075, timer 5.0; Level 7 = vec 0.167, damper 0.05, timer 45.0). Drive the local player's slide through the Phase 5F wall collision (`ZoneCollisionMesh.ResolveWalls` + ground following) so knockback stops at walls and never pushes into geometry; remote entities follow their server positions as usual.
    - [ ] **Anchor Toggle (built-in Windower "Anchor"):** Client option to ignore the knockback field (the addon zeroes it in incoming `0x028`), default off for legacy parity.
    - [ ] **Server Kill-Switch:** New `FeatureRestrictions.KnockbackOverride` bit that forces knockback on, following the collision-toggle pattern (`CollisionSettings`).
    - Out of scope by design: discarding WPOS (`0x05B`/`0x065`) placements. Ignoring them would defeat monster draw-in, GM/script teleports and event positions, so GordianXI always honors them and offers no toggle.
- [ ] **Phase 5E: UI Layering, Stock DAT 2D HUD & ImGui In-Game Overlays:**
  - [ ] **3-Tier Rendering Architecture:**
    - [x] *Tier 1 (3D Scene):* Veldrid terrain, skybox/celestial sky dome (`SkyDomeRenderer`), entity models, directional sun/moon lighting, and authentic FFXI distance fog pass. Clean-room DAT Section `0x2F` Environment decoder (`EnvironmentDecoder`, `ZoneEnvironmentData`) supporting time-of-day keyframe extraction, 8-slice sky dome gradients, and time-of-day cycling (`F10` shortcut / `CycleTimeOfDay`). Fog calibration overhaul with authentic clear visibility presets (Day, Dusk, Night), soft atmospheric haze (Overcast), distant horizon projection for retail `FogStart = 0` keyframes, shader `FogParams.y > 0.0` guards, and runtime fog toggle (`Ctrl+F10` shortcut / `ToggleFog`). Frame composition decoupled into a 3-tier presentation pipeline (`RenderTier1_Scene3D` -> `RenderTier2_StockUi` -> `RenderTier3_ImGuiOverlays`).
    - [x] *Phase 5E (Tier 1) Sky, Weather & Celestial Subsystem (5 Modular Chunks):*
      - [x] **Chunk 1: Sand & Terrain Lighting Balance:** Calibrated diffuse and ambient lighting coefficients (`0.5 * amb + 0.5 * df0`) in terrain shaders to prevent overexposure of light-colored ground/sand while maintaining shadow depth. *Superseded (2026-09-24, legacy parity):* terrain/entity lighting is now `clamp(amb + sun + moon)` with 0x2F colors converted as the legacy client does (ambient `/510` + dark bias, clamp 0.5; sun/moon `* diffuseMult` + dark bias), and a moonlight term opposite the sun.
      - [x] **Chunk 2: Clean Sky Dome & Rasterizer Dithering:**
        - Authentically rendered 8-slice vertex-interpolated hemispherical sky dome (`SkyDomeRenderer`).
        - Integrated triangular screen-space dither (`+/- 0.5/255.0` in `SkyDomeFragmentShaderGlsl`) emulating PS2 GS / D3D8 hardware rasterizer dithering, eliminating 8-bit color quantization banding in dark gradients.
        - Synchronized horizon clear colors and distance fog across Day, Dusk, Night, and Overcast.
        - Eliminated the overhead concentric ring artifact at dusk by separating the inverted `stardust` dome geometry from additive celestial passes.
      - [x] **Chunk 3: Celestial Night Sky (Stars & Moon):**
        - [x] Decoupled celestial night bodies from daytime sun and cloud layers via discrete renderer flags (`EnableWeatherCelestialBodies = true`, `EnableCelestialMoon = true`, `EnableCelestialSun = false`, `EnableWeatherClouds = false`).
        - [x] **Root cause of the chromatic star tints and invisible clouds:** a D3D11 shader stage-interface mismatch. The weather-sky fragment shader never read `fsin_WorldPos`/`fsin_Normal`, so SPIR-V cross-compilation stripped them and D3D11 (which links stages by register) fed world position into the UVs and world normals into the vertex color (alpha 0 → clouds discarded). Fixed by matching the sky VS/FS varyings exactly and giving the sky pipeline a Normal-less vertex layout; `ShaderStageInterfaces_MatchAndConsumeEveryInput` now guards every shader pair against this class of bug.
        - [x] Sky layers are matched to the generator whose `LinkedDataId` draws them (not a same-named generator): `weat/*/star` cross-links generator `star` → mesh `sta1` (666-star field) and generator `sta1` → mesh `star` (`stardust` shell).
        - [x] Generator-driven celestial rendering (`DrawCelestialGenerator`): initial rotation (Sec 2 `0x09`), base color (`0x16`), blend (`0x1E`), time-of-day alpha curve (Sec 2 keyframe link + Sec 3 `0x3F`, e.g. `ksta` / `k000`), weekday tints (`0x4E`) and moon-phase tints (`0x4F`), composited with the client's two modulate-2x texture stages.
        - [x] Moon disc decoded from the Section 0x21 SpriteSheetMesh (`SpriteSheetDecoder`): 12 phase cards on `moonshap`, card selected by moon phase (`0x45`), camera-facing, tinted by the elemental weekday. The `moonsphere` 0x2E mesh is correctly the untextured `kasa` halo (visible only near full moon).
        - [x] `stardust` shell re-enabled (`EnableMilkyWay = true`); its former ring artifact was the same shader-interface bug.
        - [x] Pole star: generator `weat/*/star/pole` draws sprite sheet `hit6`, which lives only in the shared ROM/0/0.DAT `syst/effe` tree (`SharedEffectResources`, loaded once by `ResourceManager`; link resolution falls back local → zone → shared). Rendered as a camera-facing card at its authored camera-relative offset with its warm generator tint and the `ksta` time-of-day curve.
        - [x] Moon lens flare: generator `kas1` draws lens-flare sheet `molf` in screen space (1/16 NDC per sprite unit, sprites strung from the moon's screen position through the screen centre by their per-card offsets), visible only near full moon per its authored moon-phase alpha. Veldrid exposes no occlusion queries, so instead of the client's all-or-nothing visibility test the flare is drawn at sky depth and terrain occludes it per-pixel; sun flares (`lf01`–`lf03`, multi-sprite streaks) will need a real visibility test in Chunk 5.
      - [x] **Chunk 4: Dynamic Cloud Shells:**
        - [x] Cloud shells are generator-driven: every camera-following Section 0x05 generator declared directly in a weather directory that draws a 0x2E cloud mesh is its own layer, so meshes drawn by several generators composite as authored (e.g. `weat/fine`: `cld1` alpha-blended + `cld2` additive over `cld_`; `cld3` draws the `ykum` sunset band). Rain, lightning, smoke and tornado generators in the same directories are excluded (future weather-particle work).
        - [x] Authored placement and motion: raw base position (cloud domes sit below the camera so the rim meets the horizon; the old `|y|` flip is gone), initial rotation, rotation velocity (Sec 2 `0x0B` + Sec 3 `0x05`, e.g. `fine/cld1` slowly spins instead of scrolling), per-frame UV scroll (`0x27`/`0x28`), and time-of-day position curves (`0x6B`–`0x6D`, e.g. `ykum` height via `k007`).
        - [x] Authored color: time-of-day R/G/B curves (`0x3C`–`0x3E`, e.g. `kcr1/kcg1/kcb1`, `k00r/k00g/k00b`) replace the old day-factor/night-slate shader heuristic; alpha curves via `0x3F`.
        - [x] All particle blend functions (`ParticleBlendFunc`): alpha, additive, reverse-subtract (new pipeline; darkens for the `ykum` sunset band) and darken-by-alpha; painter's order follows the authored DAT order within each weather directory (verified against Windower: every weather authors its daytime sun glow before its clouds, so overcast veils the sun; the reference viewer's projection-bias sort (`0x30`) would paint the sun over the clouds).
        - [x] Per-generator distance fog (renderState bit `0x0200` clear, e.g. `mist/cld2`), fogging toward black for additive layers.
        - [x] Weather gating uses the zone's directory for the exact active weather (e.g. `thdr`, `dust`) and falls back to the canonical category (`clod`, `suny`, `fine`, `mist`) only when the zone authors none; `EnableWeatherClouds` now defaults to true.
        - [x] Celestial layers are weather-scoped as authored (verified against a live Windower client in Bibiki Bay): stars, stardust, moon, halo, pole star and moon flare exist only under the weather directories that author them (e.g. only `fine`/`suny` carry `star`/`moon`), so overcast and mist skies show none (`WeatherSkyLayer.WeatherIds`).
      - [x] **Chunk 5: Solar & Horizon Alignment:**
        - [x] Sun path matches the client: `VanaTime.GetSunDirection` now orbits as the client does (display `(-sin a, -cos a, 0)`, a = hour * pi / 12), rising in the east (display -X) and setting in the west (+X) with no invented inclination; terrain lighting direction follows, and the moon (`-sunDir`) now rises in the east at dusk. The authored `ykum` sunset band confirms the orientation.
        - [x] Sun is generator-driven per weather: every Sun-attached generator in a weather directory that draws the 0x2E sun mesh is its own layer (e.g. `weat/fine`: `sun1` daytime glow with `ksr1/ksg1/ksb1` color and `k006` alpha, `sun2` sunset disc and `sun3` sunset corona with `k002` alpha and time-of-day scale curves `k003`/`k004` via Sec 3 `0x40`-`0x42`; `clod`/`mist` draw a single large diffuse glow). The hand-tuned golden sun-disc shader branch and inferred `sunsphere` texture are gone.
        - [x] No horizon gating for sun or moon: clock alpha curves fade them, so the sunset corona correctly outlives the sun's centre crossing the horizon.
        - [x] Sun lens flares (`lf01`/`lf02`/`lf03`/`lf31`, weather-specific) and the moon flare are drawn in screen space over the finished scene (Pass 4, no depth test) with all-or-nothing visibility from a terrain raycast toward the light (`ZoneRaycaster`, ~0.1-0.25 ms, re-cast only when the eye or light moves), standing in for the client's occlusion query.
        - [x] Verify against retail: sun glow brightness behind overcast (`clod/sun1`) and sunshine (`suny/sun0`, scale-50 additive glare). Both now sit beneath their weather's clouds in authored order and match Windower's layering, but the core brightness has not been compared side by side.
        - Out of scope for Tier 1: `weat/*/yuhi` (sunset sparkles/glows) are world-positioned, draw-distance-culled particle emitters rather than sky layers, and belong with general zone particle effects.
      - [x] **Step: Base Sea-Level Ocean Water Plane:** Implement an ocean water plane at sea level ($Y=0.0$) in Pass 5 so that island beaches and bays show translucent ocean water over the seabed while awaiting the particle generator engine. *Now off by default (Ctrl+F9 toggles):* the legacy client has no such plane. Zone water uses the plain blend shader with its own texture and authored 60 fps UV scroll (removed the invented glow floor, Fresnel, crest highlights and texture substitution).
      - [x] **Step: 0x1F Particle-Mesh Water Surfaces (persistent):** `ParticleMeshDecoder` decodes Section 0x1F meshes; `ZoneDataLoader` turns every zone-anchored, infinite-life (max life span 0) Section 0x05 generator linked to a 0x1F/0x2E mesh into a `ZoneGeometry.EffectLayers` world effect (replacing the name-based Phase 2b water instancing). `ZoneTerrainRenderer` draws them with the particle modulate-2x stages, generator color/alpha/clock curves, UV scroll, per-vertex particle lighting, fog, distance fade (0x2E near/far), depth mask and the client's opaque snap for blended particle meshes. Restores Bibiki Bay's open sea (`umi1`), shoreline caustic bands (`umat`/`uma3`), sunset glints (`yuhi`) and hillside fog patches.
      - [x] **Step: Finite-Life Particle Emitters (surf & wave crests):** `ZoneParticleEmitter` (Core) simulates zone-anchored generators on the 60 Hz effect clock: emission cadence/variance, particles-per-emission, continuous singletons, life span + variance, the 0x05 repeat handler, the 0x0A emission cull, and the initializers/updaters zone water uses (velocity, acceleration, rotation/scale velocity, progress curves for position/rotation/scale/color/UV with initial-value seeding and cycles, constant UV scroll, clock color/alpha, 0x2E distance fade, daylight tint). `ParticleGeneratorDecoder` now keeps every opcode, keyframe link cycles and expiration handlers; curves resolve from the generator's own directory first. Auto-running generators outside weather directories run continuously; non-auto-running ones run when a looping ambient Section 0x07 routine in their directory starts them (`EffectRoutineDecoder`, e.g. Bibiki umi2/s000 rolls kwa1..kwa3 in). `ZoneTerrainRenderer` pre-warms emitters on zone load and draws each live particle through the shared generator submit path.
      - [x] **Step: Remaining Particle Coverage:** A survey of all 299 zone DATs drove the opcode set. `ZoneParticleEmitter` now keeps one transform record per slot and covers velocity/relative velocity and their variance, spherical spawn scatter (0x06/0x07/0x1F), rotation/scale variance, incremental rotation (0x3B), oscillation (0x3D-0x40 / 0x29-0x2B), dampening, velocity rotation, progress velocity, clock scale/rotation/position, integrated UV/rotation, angular-distance rotation, color variance and color transforms (0x17-0x1A / 0x0B-0x0C), double-range fade (0x48), occlusion probes (0x53), day-of-week and moon-phase tints. The daylight tint uses the 0x2F model sun/moon lights. Zone sprite-sheet particles (Section 0x21, ~17k generators: lamp halos, sparkles, fountain spray) run as emitters with XYZ / XZ billboarding and per-particle card selection (0x0D / 0x45). Child generators run as child-only emitter layers: 0x3C once at birth, 0x44/0x53/0x6A streams for the parent's life (0x25 / 0x33 / 0x46), and expiration 0x01; 0x45 copies the parent position.
      - [x] **Step: Weather Emitters & Point Lights:** Looping ambient routines at their authored total length matched retail side by side on Bibiki Bay (2026-09-24).
        - [x] *Chunk 1: Weather emitters.* Auto-running generators in weather directories (rain, snow, splashes, fireflies, `thdr/lig1`/`lig2` lightning bolts and their children) run as zone emitters gated on the active weather (a weather change clears them). Weather generators emit a third of their authored count (doubled first when batched); batched generators (`0x6B` flag `0x20`) emit one particle whose sub-particles take the spawn scatter, relative velocity, drift and dampening and draw once each at their world offset. Camera-following generators (setup flag `0x04`) track the camera plus their base; camera-anchored ones (render-state `0x0400`) stay where the camera was at birth. Birth daylight tint `0x90` (strongest model light) keeps rain mist (`smok`) dim under overcast, matching a Windower capture in Carpenters' Landing. Camera-oriented `0x1F` spawn shells face the view; Movement / MovementHorizontal / Camera billboards orient along the particle's last movement or toward the eye (movement ignored when batched). Sky-layer generators (clouds, sun, celestial shells and sprites) are excluded, and per-draw frustum culling keeps Xarcabard's snow at ~2.6k draws. Verified offscreen: Carpenters' Landing rain and thunder, Uleguerand snow; Bibiki unchanged.
        - [x] Verified against retail (2026-09-24): rain density matches Windower; billboard-None rain cards are drawn flat in world space as the reference does.
        - [x] *Caves:* camera-following and camera-anchored weather stops emitting while the floor under the camera belongs to a placement linked to a sub-environment (ZoneDef record `+0x4C`, e.g. `ev01`); verified offscreen in an Uleguerand Range cave (snow stops) and outdoors (snow falls).
        - [x] *Child orientation:* transform-following child streams (`0x33`, and `0x46` billboarded toward the camera) place children in the parent particle's rotated, scaled frame (X-Y-Z rotation order, parent movement/camera billboards) and track the live parent while they follow their generator; parent-copy initializers `0x46` velocity, `0x47`/`0x79` rotation, `0x48` color, `0x49` scale, `0x4A` UV and `0x9B` position.
        - [x] *Lightning strikes:* weather routines shorter than 1000 frames (`kmi*`, `kam*`, `kami`: 0-480 frames) form per-directory groups (`WeatherRoutinePlayer`) that play one random routine at a time under their weather; longer weather routines (birds, butterflies, fireflies, ducks: 1890-9000 frames) loop like zone ambient routines. The pause between strikes (4-15 s) is an estimate; an exact retail match was judged unnecessary.
        - [x] *Chunk 2: Point lights (~15.6k generators).* The ZoneDef light table (header `+0x18`, 256 x `0x4C`, generator FourCC at `+0`) binds each `0x47` point-light generator to a slot by its own FourCC, and each placement's four 1-based references at `+0x54` are the only lights that shine on it (binding from xi-tools `docs/zone/format.md`). Light generators run through the emitter (`0x58` range / theta / multipliers `2^x`, clock theta `0x49`, progress `0x5B`-`0x5E`, parent `0x7E`/`0x7F`); life-0 generators (and 1-frame point lights) now emit once and live forever. Terrain shaders add `vColor x max(N.L, 0) x 2 x color x theta x (1 - d/range)^2` per referenced light inside the ambient + sun + moon clamp. Southern San d'Oria's lamps turn on at ~19:00 via their `pttm` theta curve, and the falloff/power were calibrated against Windower captures there (22:03 door lamps, 20:30 wall lamp).
        - Closed: point lights on entities (not needed). Repeat-expiration (`0x05`) generators do not accumulate: all 6,085 in the zone DATs are continuous singletons. Actor-attached weather effects (e.g. `weat/clod/tobi` birds) moved to Phase 5C model-embedded effect routines.
      - [x] **Step: Live Weather & Water Dynamic Simulation:** Dynamic Vana'diel time clock and weather synchronization (`VanaTime`, `WorldState.WeatherId`, `0x00A` login ack, `0x057` weather packet). Automatic per-frame keyframe interpolation and sky dome update in `VeldridViewportControl`. Water surface particle generator instancing (Phase 2b in `ZoneDataLoader` for `shi1..shi5`, `hum1`, `mizu`, `hna0`), per-submesh and zone master UV scroll animations (`UVScrollVelocity`), and high-definition ocean water plane tuning (tile UV 200) matching retail FFXI.
    - [ ] *Tier 2 (Stock FFXI 2D UI):* Authentic DAT-driven menu boxes (blue marble), finger cursor hand, targeting brackets, vitals gauges, status icons, and dialog text.
    - [ ] *Tier 3 (ImGui Overlays & Addons):* Modern translucent HUD (`WindowRounding = 6.0f`), performance profiling overlay, radar/minimap, and addon plugin canvases.
  - [ ] **Modular Stock UI Suppression (`StockUiVisibilityState`):**
    - [ ] Granular visibility flags for each stock element (Target Bar, Player Vitals, Party Frames, Alliance Frames, Buff Bar, Menus, In-Game Chat).
    - [ ] Allows addon authors and players to selectively or entirely disable stock HUD elements to run custom ImGui replacements (e.g. XIVParty, modern target frames, or clean cinematic mode) without visual overlap.
  - [ ] Viewport performance overlay: FPS counter, frame pacing graph, draw call counters, and GPU pass timings
- [ ] **Phase 5F: World Collision, Terrain Navigation & Ground Physics (MVP Completion — Blocking):**
  - [x] **Collision Mesh Decode:** `ZoneCollisionDecoder` reads the retail player-collision soup from the ZoneDef collision block (header `+0x08`: local-space meshes placed by `0xC0` transforms through (transform, mesh) pair groups; per-triangle wall bit, terrain type and camera transparency), layout referenced from xi-tools `docs/zone/collision.md`. This is the same soup LSB's navmesh and LoS are baked from, not the visible `0x2E` meshes. Verified against retail data: collision floors match the visible terrain with a median height difference of 0.000 yalms (Bibiki Bay 194k tris, Southern San d'Oria 106k tris).
  - [x] **Ground Height Sampling:** every moving locomotion tick settles the local player onto the highest walkable (up-facing) surface at most 0.5 yalms above its feet (a little more forgiving than retail, which blocks the low side walls of San d'Oria's ramps) and at most 60 yalms below, so it climbs stairs/slopes and drops off ledges without popping onto bridges or upper floors overhead. Level treads (stairs, curbs) round the feet over their edges with a 0.9-yalm foot sphere (`TryGetSteppedGround`), so stairs climb and descend as a ramp like the legacy client: calibrated on a Windower capture of Southern San d'Oria's stairs (mean error 0.034 yalms vs 0.217 for per-tread snapping); slopes keep exact heights. `WorldState.Collision` is handed over by the viewport once the zone loads and cleared on zone change; `PlayerLocomotionController.CollisionEnabled` turns it off. `ZoneCollisionService` gives every registered session (displayed or not) its own zone's collision through `ResourceManager.TryLoadZoneCollision`, which decrypts only the ZoneDef section and caches one mesh per zone. The orbital camera eases its follow height (rate 6/s, teleports > 8 yalms snap) so steps and slopes no longer jolt the view. `/moveto` without a height lands on the floor nearest the current height; a placement with a height (`/moveto x y z`, `!pos`) holds in mid-air until the player moves, then falls under gravity while horizontal movement continues (66 yalms/s² to a 30 yalms/s top speed, fitted to a Windower capture of 12- and 20-yalm falls; a fall continues after the keys are released), so a higher drop carries further forward, as with Project Tako's position hacks (lifting onto a ledge). Drops deeper than 0.5 yalms fall; stairs, slopes and curbs still settle at once. `!pos` takes Windower order (x, y, z = height; commas and parentheses accepted, as with `/moveto`) and is reordered for LSB's `setPos` (x, height, y).
  - [x] **Collision Toggles:** `/collision [ground|walls|entities|all] [on|off]` (`CollisionSettings`), per session. New server kill-switch bits `WallCollisionOverride` (keeps ground + walls on) and `EntityCollisionOverride` (keeps entity collision on) pin layers the server protects.
  - [x] **Entity Collision (soft bump):** `EntityBumpCollision` stops the player on walking into a player, NPC, monster, pet or trust, lets it through after 0.35 s of pushing, then grants 1.5 s of free movement for crowds; overlapped characters never block. Size from 0x00D/0x00E `GraphSize`; hidden, invisible and non-blocking (Flags3 bit 28, XiPackets) entities are skipped. Radii (0.35/0.7/1.4 by size) and timings are estimates pending a retail capture. `/collision entities` turns it off (JA0Wait-style).
  - [ ] **Elevators & Moving Platforms (research):** LSB sends an elevator as an NPC with the `elevator` look (no model; the client resolves it by its door id) plus an up/down animation, leg start timestamp and travel time; the client animates the platform and carries whoever stands on it. The platforms (Metalworks `lift*` placements) are not in the static collision soup, so ground-following would drop a rider down the shaft. Need: door id -> placement/collision mapping, platform motion, rider carry. Ship zones (Manaclipper, Selbina/Mhaura ships, 3/220/221/227/228) have no collision block, so riders keep the server's height.
  - [x] **Horizontal Wall/Obstacle Collision:** `ZoneCollisionMesh.ResolveWalls` pushes a column of 0.53-yalm body spheres (from the 0.5 step height to 1.6 yalms; the radius measured from a Windower capture of a character pressed into a Southern San d'Oria corner, 0.529/0.535 yalms from the walls) horizontally out of wall-like triangles (|normal.Y| < 0.5) in sub-steps of half a radius, sliding along walls; one-sided like the client (only front faces block). Anything below the step height is left to ground following, a move onto no floor at all is refused, and a sub-step whose accumulated push would cross a wall's front face is undone (a body wedged against a prop could otherwise be shoved through a thin wall). Verified on retail data: 0 of 600 walls in Southern San d'Oria and Bibiki Bay passed through, the retail stair climb is unobstructed, ~14 µs per move.
  - [x] **Server Position Authority:** LandSandBoat trusts the client's reported position (`0x015` is stored as sent, no server-side collision), so its only corrections are WPOS (`0x05B`/`0x065`) placements: GM/script `setPos`, monster draw-in, event-end positions. WPOS is broadcast to everyone in range, so it is now routed by `UniqueNo` (previously every WPOS moved the local player, e.g. when another player was drawn in); other entities `Warp`. Local placements queue on `LocalPlayerState` and apply at the start of the next locomotion tick, so an in-flight movement tick cannot overwrite them or report a stale position (LSB ignores `0x015` only until the WPOS is sent). Modes per XiPackets: `0x0A` rotates only, `0x08` locks movement until `0x09`/`0x05` (or a zone change), and a charmed player ignores input and takes its own `0x00D` positions.
  - [x] **Remote Entity Grounding:** Other players, NPCs and monsters are drawn on the collision floor under their reported position (`EntityGrounding`, same step-up and stair rounding as the local player), not at the reported height: a Windower capture shows a player position-hacked 10 yalms up drawn walking on the floor by another client, before and after a teleport. Actors with the server's GroundFlag (Flags0 bit 15, "ignores world collision", XiPackets) and doors/elevators/ships keep the reported height; display only, server positions untouched.
  - [x] **Collision Acceleration Structure:** `ZoneCollisionMesh` indexes the soup in a 4-yalm uniform XZ grid (CSR arrays, allocation-free queries), so a query scans only the triangles in the query cell.
  - [x] Dedicated `Gordian.Core.Tests` `World/Collision` test coverage (synthetic decode, ground queries, locomotion grounding, retail-data alignment).
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
  - [ ] Zone effect audio: ~5.9k Section 0x05 generators link a sound (`0x3D`) with near/far range (`0x4C`), time-of-day volume (`0x43`) and path-following emitters (`0x6B`, shoreline waves); they run on the existing zone particle runtime and need only the sound backend.
  - [ ] Select a cross-platform managed audio backend (must avoid Windows-only APIs per `AGENTS.md`; no audio library of any kind is referenced anywhere in the codebase today).
  - [ ] Clean-room decode of FFXI DAT sound resources (footstep sets, spell/weaponskill/ability SFX, UI cues, ambient zone loops, BGM tracks), if stored in DAT containers.
  - [ ] Footstep & movement SFX tied to `PlayerLocomotionController`/animation state; the surface under each foot comes from the decoded collision terrain type (`CollisionTriangle.Terrain`: object, path, grass, sand, snow, stone, metal, wood, shallow/deep water), and sand/snow leave footprints. (FFXI has no swimming: water edges are ordinary collision barriers.)
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
