# Issue drafts (delete this file once the issues are created)

Milestones: **MVP (Phase 5)** for everything MVP; **Phase 5H: Audio** for audio.
Labels: `area:network`, `area:rendering`, `area:world`, `area:animation`, `area:ui`, `area:lobby`, `area:audio`, plus `mvp-blocking`, `research`, `retail-verification` (needs a Windower/retail capture), and the stock `bug` / `enhancement`.

Format: each `### ` heading is one issue; the `labels:` / `milestone:` lines are metadata; everything below is the body.

---

### Register orphaned S2C 0x073 ChocoboToteboard decoder
labels: area:network, bug
milestone: MVP (Phase 5)

`S2C_0x073_ChocoboToteboard` in `ProgressionPackets.cs` is fully implemented but never added in `ProgressionPacketModule.Register()`, so it never fires. Found by the 2026-09-20 packet audit ([docs/network/session-and-packets.md](docs/network/session-and-packets.md#packet-audit-2026-09-20)).

### Add a test that every S2C decoder is reachable from PacketDispatcher
labels: area:network, enhancement
milestone: MVP (Phase 5)

Add a `Gordian.Core.Tests` `Network/Packets` regression test asserting every decoder defined in `Network/Packets/*.cs` is registered with `PacketDispatcher`. It would have caught the orphaned 0x073 decoder automatically.

### Delete duplicate legacy C2S builders
labels: area:network, enhancement
milestone: MVP (Phase 5)

Remove the four builders for opcodes already covered elsewhere: `HandshakePackets.BuildGameOkSubPacket`, `HandshakePackets.BuildNetEndSubPacket`, `LifecycleOutboundPackets.BuildEventEnd`, `LifecycleOutboundPackets.BuildEventEndXzy`.

### Wire up uncalled C2S builders
labels: area:network, enhancement
milestone: MVP (Phase 5)

These builders are complete but never called, so the actions are impossible in the running client. Each needs a caller (action service method, command or UI):
- [ ] Auction House bid/buy (`0x04E`)
- [ ] Subcontainer / mannequin equip (`0x03B`)
- [ ] Equipset check and lockstyle (`0x052` / `0x053`)
- [ ] Key item reading (`0x064`)
- [ ] Mog House room and furniture layout (`0x0CB` / `0x0FA`)
- [ ] Chocobo race entry (`0x09B`)
- [ ] Unity quest accept and toggle (`0x117` / `0x118`)

### Persist decoded-but-discarded S2C data into World state caches
labels: area:network, area:world, enhancement
milestone: MVP (Phase 5)

These packets are decoded but only logged. Store them in a `World/*State` cache:
- [ ] Status effect apply/expire and duration (`0x030 Effect`; blocks buff/debuff countdown UI)
- [ ] Auction House state (`0x04C Auc`; no AH state object exists yet)
- [ ] Guild shop and Bazaar transaction results (`0x082`-`0x085`, `0x106`, `0x108`-`0x10A`)
- [ ] Equipset validation/result (`0x116` / `0x117`)
- [ ] Linkshell comlink (`0x0E0`)
- [ ] Party invite result (`0x11D`)
- [ ] NPC dialog/menu text (`0x036 TalkNum`)
- [ ] Mog House operation result (`0x0FA`)

### Bibiki Bay cave mouth: west wall tinted orange at dusk instead of pink
labels: area:rendering, bug, retail-verification
milestone: MVP (Phase 5)

At 19:46 the entrance cave mouth's west wall (player spot display (-652.66, 21.82, 899.85)) carries an orange sun tint where retail shows pink: the sun below the horizon still lights west-facing faces. See [docs/rendering/viewport-and-terrain.md](docs/rendering/viewport-and-terrain.md) (ignore-texture-alpha) and [docs/rendering/lighting.md](docs/rendering/lighting.md).

### Verify lighting brightness against Windower captures
labels: area:rendering, retail-verification
milestone: MVP (Phase 5)

Remaining side-by-side checks from the lighting and sky calibrations ([lighting.md](docs/rendering/lighting.md), [sky-and-weather.md](docs/rendering/sky-and-weather.md)):
- [ ] Terrain at noon: flat sand now saturates to near-white (unclamped 1.5x sun plus 0.7 ambient); compare against a noon capture.
- [ ] Characters at noon: the summed actor light cap of 0.75 is an estimate.
- [ ] Sun glow brightness behind overcast (`clod/sun1`) and sunshine (`suny/sun0`).

### Actor status visuals (Refresh/Regen afterglow)
labels: area:rendering, area:animation, enhancement
milestone: MVP (Phase 5)

Effects attached to characters by status effects, e.g. the Refresh/Regen afterglow on the characters in the Bibiki Bay captures. Shares the actor-attached generator work with model-embedded effect routines. See [docs/world/entities-and-animation.md](docs/world/entities-and-animation.md).

### Model-embedded effect routines (Home Point crystal, actor-attached weather)
labels: area:rendering, area:animation, enhancement
milestone: MVP (Phase 5)

Some NPC models are drawn mostly by particle effects inside their own DAT, played by the model's Section 0x07 routines (e.g. the Home Point crystal, model 51 `ROM/3/25.DAT`: 13 generators started by routines `bind` / `aper`; its skeleton mesh is a small placeholder). Also covers actor-attached weather effects (e.g. `weat/clod/tobi` birds). Needs actor-attached generators on the zone particle runtime; shares machinery with spell and ability effects. See [docs/world/entities-and-animation.md](docs/world/entities-and-animation.md).

### Combat action event queue (ActionPlaybackQueue)
labels: area:animation, enhancement
milestone: MVP (Phase 5)

Bridge incoming S2C `0x028` (`S2C_0x028_CombatAction`) events from `CombatPacketModule` into entity animation dispatchers. Foundation for the rest of Phase 5D.1 ([docs/world/entities-and-animation.md](docs/world/entities-and-animation.md)).

### Weapon-specific attack swing resolution
labels: area:animation, enhancement
milestone: MVP (Phase 5)

Resolve swing clips (`atk`, `atk0`, `atk1`) from the entity's equipped weapon type / combat skill (Hand-to-Hand, Daggers, 1H/2H Swords, Axes, Scythes, Polearms, Katanas, Clubs, Staves, Archery, Marksmanship). Depends on the combat action event queue.

### One-shot transient playback and battle stance recovery
labels: area:animation, enhancement
milestone: MVP (Phase 5)

Play attack swings, weaponskills (`ws`), casting gestures (`cas`) and job abilities to completion, then return to the continuous `"btl"` ready stance. Depends on the combat action event queue.

### Damage and hit reaction animations
labels: area:animation, enhancement
milestone: MVP (Phase 5)

Trigger brief hit-reaction clips (`dam`, `hit`) on the target when attack/damage battle messages and actions arrive. Depends on the combat action event queue.

### Knockback playback with wall collision
labels: area:animation, area:world, enhancement
milestone: MVP (Phase 5)

Knockback is the upper 3 bits of each target result's `scale` field in S2C `0x028`; the client slides the character and reports the position via `0x015`. Level parameters and the design are in [docs/world/entities-and-animation.md](docs/world/entities-and-animation.md#planned-transient-combat-and-action-animation-phase-5d1).
- [ ] Local player slide through `ZoneCollisionMesh.ResolveWalls` + ground following
- [ ] Anchor toggle (ignore knockback), default off
- [ ] `FeatureRestrictions.KnockbackOverride` server kill-switch bit

### Animate door placements
labels: area:world, area:rendering, enhancement
milestone: MVP (Phase 5)

ZoneDef placements with `_` BlockIDs (doors, with open/close routines) are still drawn static. Elevators (`@`) already move. See [docs/world/collision-and-physics.md](docs/world/collision-and-physics.md).

### Confirm entity bump collision radii and timings
labels: area:world, retail-verification
milestone: MVP (Phase 5)

`EntityBumpCollision` radii (0.35/0.7/1.4 by size) and timings (0.35 s hold, 1.5 s grace) are estimates pending a retail capture.

### Blink expiring status icons
labels: area:ui, enhancement
milestone: MVP (Phase 5)

Status icons should blink before they expire. Needs status durations from S2C `0x063` (status-icon packet) wired into `LocalPlayerState`. Last open item of Tier 2 chunk 3 ([docs/ui/stock-ui.md](docs/ui/stock-ui.md#live-hud-chunk-3)).

### Confirm claimed-by-others target name colour
labels: area:ui, retail-verification
milestone: MVP (Phase 5)

Claimed-by-others names are pink (240, 122, 180), taken from an alliance capture of Tiamat. Confirm against a capture where the claim is known to be another group's.

### Research the menu string table (help text and window titles)
labels: area:ui, research
milestone: MVP (Phase 5)

Help text (`helpwind`) and window titles need the menu string table referenced by the help/title ids on frames and buttons. xi-tools points at ROM/97. Unblocks the Font Colors / Log / Effects config pages.

### Config pages: Font Colors, Log, Effects
labels: area:ui, enhancement
milestone: MVP (Phase 5)

List pages whose row text needs the menu string table: Font Colors (`conftxtc` -> `textcol1` rows + `textcol3` RGB sliders), Log (`conf11l` -> `conf11s` -> `conf11m`), Effects (`fxfilter`). Blocked by the menu string table research.

### Gamepad key-assignment editor
labels: area:ui, enhancement
milestone: MVP (Phase 5)

The config list's Gamepad entry opens the key-assignment editor (`keypad` / `k1assign`).

### Mouse input for stock menus
labels: area:ui, enhancement
milestone: MVP (Phase 5)

Clicking menu buttons with the mouse (`yubi` is the mouse pointer sprite).

### Main-menu entry windows
labels: area:ui, enhancement
milestone: MVP (Phase 5)

Entries currently post a "not available" notice. Each needs its client-composed window:
- [ ] Status
- [ ] Equipment
- [ ] Magic
- [ ] Items
- [ ] Abilities
- [ ] Map
- [ ] Remaining page 1 and 2 entries (Synthesis, Party, Trade, Search, Linkshell, Region Info, Missions, Quests, Key Items, View House, Bazaar, Macros, Help Desk, Communication)

### Confirm retail config defaults and undecoded config details
labels: area:ui, retail-verification, research
milestone: MVP (Phase 5)

From chunk 4c ([docs/ui/stock-ui.md](docs/ui/stock-ui.md#config-pages-and-settings-chunk-4c)):
- [ ] Defaults: Multi-window (kept OFF), DamageDisplay (Both), volumes, gamma
- [ ] Chat Filters green "Hold" state (`framesus` #83) and the Tell row's music-note icon
- [ ] How retail makes menu bodies opaque below the title band (currently redrawn opaque by `opaqueBody`)
- [ ] Whether Global "Chat Language Filter" maps to SAVE_CONF's 2-bit Language field
- [ ] Meaning of the kind-4 alternate button images (Synthesis, Party, Gamepad)

### Movable stock UI (Tier 2 chunk 4b)
labels: area:ui, enhancement
milestone: MVP (Phase 5)

Opt-in unlock mode (locked by default for legacy parity) that outlines the persistent windows and lets the player drag them, writing the chunk 2 layout overrides. Runtime-placed windows follow their parent. See [docs/ui/stock-ui.md](docs/ui/stock-ui.md#planned-chunks).

### Chat/log window (Tier 2 chunk 5)
labels: area:ui, enhancement, mvp-blocking
milestone: MVP (Phase 5)

Log window with channel colours and scrollback, line-count variants and the second log window; chat input line routed to the existing slash-command dispatcher; `fep`/`fepp` chat-mode selector. See [docs/ui/stock-ui.md](docs/ui/stock-ui.md#planned-chunks).

### Dialog text (Tier 2 chunk 6)
labels: area:ui, enhancement
milestone: MVP (Phase 5)

Event message boxes and choice menus.

### In-world name plates
labels: area:ui, area:rendering, enhancement
milestone: MVP (Phase 5)

Names over characters, NPCs and monsters in the 3D view (larger for the current target, coloured by type/claim), the red "?" seeking icon and mentor/rank stars. Source font and icon art still to be located (`fontshp` has icons past 0x7E).

### Stock UI visibility API for addons and ImGui replacements
labels: area:ui, enhancement
milestone: MVP (Phase 5)

`StockUiVisibilityState`: granular visibility flags per stock element (target bar, vitals, party, alliance, buffs, menus, chat) so addons and ImGui replacements can hide stock elements. Per-window hiding already exists through `StockUiLayout` overrides.

### Viewport performance overlay
labels: area:ui, area:rendering, enhancement
milestone: MVP (Phase 5)

FPS counter, frame pacing graph, draw call counters and GPU pass timings (Tier 3 ImGui).

### Research: is the character lobby DAT-driven?
labels: area:lobby, research, mvp-blocking
milestone: MVP (Phase 5)

Determine whether retail's character select/creation lobby (background, preview models, menu chrome) is DAT-driven or hardcoded in the executables; identify the ROM/DAT sections if DAT-driven, within the clean-room rules. See [docs/design/character-lobby.md](docs/design/character-lobby.md).

### Character select screen
labels: area:lobby, enhancement, mvp-blocking
milestone: MVP (Phase 5)

Decode any lobby DAT resources and render the character list with race/face/job preview and navigation.

### Character creation flow
labels: area:lobby, enhancement, mvp-blocking
milestone: MVP (Phase 5)

Race, face, starting nation, name entry, live appearance preview.

### Character deletion flow
labels: area:lobby, enhancement, mvp-blocking
milestone: MVP (Phase 5)

Confirmation step, matching LSB's delete-code/security flow if enforced server-side.

### Wire lobby actions to the LSB login server
labels: area:lobby, area:network, enhancement, mvp-blocking
milestone: MVP (Phase 5)

List/create/delete/select-and-enter-world requests through `LsbLoginClient`, integrated ahead of the `ProxyStager`/Named Pipe handoff without breaking the direct-to-zone saved-profile fast path.

### Loading screen for zone-in and zone transitions
labels: area:lobby, area:ui, enhancement, mvp-blocking
milestone: MVP (Phase 5)

`PerformZoneTransitionAsync` has no visual loading state. Identify whether retail uses a DAT loading-screen asset and add a loading state for character select -> zone-in and zone-to-zone.

### Select a cross-platform audio backend
labels: area:audio, research
milestone: Phase 5H: Audio

No audio library is referenced today. Must avoid Windows-only APIs. See [docs/design/audio.md](docs/design/audio.md).

### Decode DAT sound resources
labels: area:audio, research
milestone: Phase 5H: Audio

Footstep sets, spell/weaponskill/ability SFX, UI cues, ambient loops and BGM, if stored in DAT containers.

### Zone effect audio
labels: area:audio, enhancement
milestone: Phase 5H: Audio

~5.9k Section 0x05 generators link a sound (`0x3D`) with near/far range (`0x4C`), time-of-day volume (`0x43`) and path-following emitters (`0x6B`, shoreline waves). They already run on the zone particle runtime; only the sound backend is missing.

### Footstep and movement sounds
labels: area:audio, enhancement
milestone: Phase 5H: Audio

Tied to locomotion/animation; surface from the collision terrain type (`CollisionTriangle.Terrain`); sand/snow footprints.

### Combat and action sounds
labels: area:audio, enhancement
milestone: Phase 5H: Audio

Tied to `CombatPacketModule` action/effect events (`0x028` / `0x030` / `0x0AA`).

### Ambient zone loops and BGM
labels: area:audio, enhancement
milestone: Phase 5H: Audio

Playback tied to `WorldState.ZoneChanged`.

### UI and menu sound cues
labels: area:audio, area:ui, enhancement
milestone: Phase 5H: Audio

Target, cursor move, confirm, cancel.

### Volume mixing
labels: area:audio, enhancement
milestone: Phase 5H: Audio

Master/category volumes (SFX/BGM/Ambient/UI), read from the config page volume sliders already stored in `StockUiSettings`.

### Zone back-face culling in-game toggle and retail missing-faces audit
labels: area:rendering, retail-verification
milestone: MVP (Phase 5)

Zone meshes without the 0x2E double-sided flag (0x2000) draw with back faces culled (front face = clockwise on screen with normals pointing out). `DisableZoneBackFaceCulling` restores the previous double-sided behavior, but is currently code-only. Add an in-game / config toggle and monitor for any retail objects/geometry that show missing faces with culling enabled. See [docs/rendering/viewport-and-terrain.md](docs/rendering/viewport-and-terrain.md).
