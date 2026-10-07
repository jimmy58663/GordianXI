# Viewport, Camera & Zone Terrain

> NeoVeldrid graphics context, viewport windows, camera, and the zone terrain renderer (Phases 5A and 5B), plus the three-tier frame composition. Status and open work: [ROADMAP.md](../../ROADMAP.md) and GitHub Issues.

## Graphics context and viewport windows (Phase 5A)

- Graphics through [NeoVeldrid](https://github.com/jhm-ciberman/neo-veldrid) and NeoVeldrid.SPIRV 1.2.1 (#262, 2026-10-05), the maintained fork of Veldrid 4.9.0 with the same API (namespace `NeoVeldrid`). It has no Metal backend: macOS runs Vulkan through the bundled MoltenVK. `Veldrid.ImGui` was dropped unused; the ImGui binding and renderer for Tier 3 are chosen in #263.
- Avalonia `NativeControlHost` cross-platform viewport control (`VeldridViewportControl`) supporting Windows (`HWND`), Linux (`X11`/`Wayland`), and macOS (`NSView`)
- Multi-backend auto-selection (Direct3D 11 on Windows, Vulkan on Linux/Windows, Vulkan through MoltenVK on macOS; a saved `Metal` preference from before #262 selects Vulkan)
- Resilient 60/120 FPS render loop with device recreation on viewport resize
- Verification test scene: textured spinning 3D test cube and color gradient clearing to confirm GPU pipeline integrity
- **Decoupled 3D Rendering Window (`ViewportWindow`) & Lifecycle Coordinator (`ViewportWindowManager`):**
  - Gameplay graphics run in a separate hardware-accelerated window, preserving `MainWindow` as the central Control Panel (profiles, packet inspection, state diagnostics, chat console, and display settings).
  - Three display modes: **Borderless Window** (default, borderless work area alignment), **Windowed** (movable & resizable with min dimension safeguards), and **Fullscreen** (`WindowState.FullScreen`, toggleable via `F11`).
  - Character tab switcher styles (`ViewportTabStyle`, saved as `ViewportSettings.SelectedTabStyle`; a change in Settings applies at once, to pop-out windows too). See [Multi-box](#multi-box-windows-switching-and-input-154) for how each behaves.
  - **Pop-Out (`⧉`):** a character gets its own `ViewportWindow` (windowed, its own view model with `IsPrimary = false`); the main window moves on to the next character not popped out. Each window currently creates its own `GraphicsDevice` ([#300](https://github.com/jimmy58663/GordianXI/issues/300)).
  - **Picture-in-Picture (PiP) deck:** cards for up to `MaxPipStreams` (5) background characters with 1-click `⇄` promotion into the main view and `⧉`. Cards show name, job and HP; live scene thumbnails are [#302](https://github.com/jimmy58663/GordianXI/issues/302).

- **Lobby and zoning (#32, #36).** While a character lobby is open (`ViewportViewModel.Lobby`) the viewport draws it instead of the active session (`LobbyFrameRenderer`: the lobby backgrounds, the preview model through the entity renderer alone, the lobby windows) and sends it the keyboard and mouse. While a session connects or zones, `ZoneLoadingScreen` fades the frame to black over the scene and the HUD, and the placeholder models are left out until it is clear again. Between a Log Out and the lobby's return (`ViewportViewModel.IsReturningToLobby`) the viewport draws black (`HoldBlack`), and a lobby fades in from black when it appears. See [character-lobby.md](../design/character-lobby.md).

## Multi-box: windows, switching and input (#154)

One process can hold several characters (`CharacterSession`, one per character; one per account, see [desktop-shell.md](../app/desktop-shell.md)). `ViewportWindowManager` gives all of them tabs in the main viewport window (`ViewportViewModel.CharacterTabs`; `ActiveTab` is the character drawn and driven there) and opens a pop-out window per torn-off character.

**Airspace (#152, 2026-10-07).** On Windows the 3D surface is a native child window (`Win32ChildWindowHelper`), and Avalonia cannot draw over it: every switcher overlaid on it was invisible (all four styles, the PiP deck and the free camera banner). Now nothing in the window overlaps the surface. The top ribbon and side rail take their own row / column beside it; the floating pill, the PiP deck and the free camera banner are `Popup`s (small owned windows, not topmost, not light-dismissed, not taking focus) placed over it, opened while their view model flag is set and closed while the window is hidden or minimised (`ViewportWindow.UpdateOverlayPopups`). Switcher buttons are not focusable, so a click leaves the keyboard with the game.

| Style | Shows | Where |
|---|---|---|
| `FloatingPill` (default) | active character, job, `⧉`, a button per other character, camera mode button | popup at the top centre of the view, while a character is shown (not over a lobby or the black return to it) |
| `TopRibbon` | a tab per character (name, job, `⧉`), FPS and backend, display mode, minimise and close | strip above the view, always (also over a lobby, for its window buttons) |
| `SideRail` | a card per character (name, job, HP, `⧉`, Focus) | 180 px column left of the view, while a character is shown |
| `HotkeysOnly` | nothing | Ctrl+Tab / Ctrl+Shift+Tab only |

Checked 2026-10-07 by launching the shell with three unconnected sessions (Gordian, Knot, Claude) and screen captures of each style: all four show as above, the PiP deck shows two cards, and Ctrl+Tab / Ctrl+Shift+Tab (sent to the window) move the pill's active character Gordian -> Knot, then back twice -> Claude.

**Switching (#151).** Ctrl+Tab / Ctrl+Shift+Tab (`ViewportShortcuts`) are read on the window's tunnelling key-down with the other window shortcuts (F8-F11). They were on the bubbling key-down, where Avalonia's keyboard navigation can take Tab (Ctrl+Tab included) when a control in the window has the focus. Cycling wraps and skips characters popped out into their own window. Each switch is logged (`Viewport` `Ctrl+Tab: A -> B (n tabs)`). The keys and buttons the previous character held are released (`InputState.Reset`), otherwise the Ctrl of Ctrl+Tab or a held movement key stayed down for it; the same happens when a viewport window loses the focus. The camera mode button and the free camera banner's Exit now switch the active character's camera (`PlayerLocomotionController.CameraMode`); they had only changed a view model field nothing read.

**Gamepad focus (#150).** `ViewportWindowManager.InputFocus` (`InputFocusTracker`) records which viewport window has the focus and which character it shows; any feature that should follow focus can read it. The pad drives the focused viewport's character (none while it shows a lobby); with the control panel or another program focused, the character of the viewport focused last. Every other character gets a disconnected pad. Before, the pad went to `SessionRegistry.PrimaryRenderingSession`, which each pop-out took when it opened, so the last window popped out kept the pad whatever had the focus. Across processes, SDL's Raw Input, GameInput and Windows.Gaming.Input joystick drivers are turned off on Windows so every GordianXI process can read the pad (see [console-and-input.md](../input/console-and-input.md#input-subsystem)).

**Audit checklist (2026-10-07, without a game login; "unit" = App tests, "shell" = the capture above):**

| Item | Result | How checked |
|---|---|---|
| One session per character; tabs added / removed on login, logout, disconnect | works; a removed active tab hands over to the first tab not popped out; a pop-out closes with its session | code, existing `ViewportWindowManagerTests` |
| Ctrl+Tab / Ctrl+Shift+Tab, `⇄`, switcher buttons switch the primary render | fixed (#151) | unit (`CycleCharacter_*`, `ViewportShortcutsTests`), shell |
| Four switcher styles show and update live | fixed (#152) | unit (`SwitcherVisibility_*`), shell |
| Tab names, jobs and HP update | fixed: they only refreshed when a session was re-added; now on the 250 ms telemetry tick | code |
| Keyboard and mouse reach only the window's active character | works (each window feeds its own `ActiveTab`); held keys released on switch and focus loss | unit (`SwitchingCharacter_ReleasesTheKeysHeldForThePreviousOne`) |
| Gamepad follows focus, incl. pop-outs and Always Enable Gamepad | fixed (#150); Always Enable reads the target character's profile | unit (`InputFocusTrackerTests`); in game: pending |
| Gamepad across several GordianXI processes | SDL driver hints changed; not verifiable here | in game: pending |
| Pop-out shares one `GraphicsDevice` | no: one device per window | code; [#300](https://github.com/jimmy58663/GordianXI/issues/300) |
| Background / unfocused render throttling | none | code; [#301](https://github.com/jimmy58663/GordianXI/issues/301) |
| PiP live thumbnails | status cards only | code, shell; [#302](https://github.com/jimmy58663/GordianXI/issues/302) |
| Reopening the main viewport after closing it | added: Settings -> 3D Viewport -> Open Viewport Window (the command existed but had no button) | shell build |
| Per-session state isolation (world, appearance, camera, stock UI, target) | each `CharacterSession` owns its `WorldState`, `PlayerActionService` (menus, UI layout, target) and locomotion camera; the viewport rebinds them on a switch. The same-gear bug (#153) is closed | code |
| Shared `ResourceManager` caches | keyed by content, not entity ids ([Resource cache keys](#resource-cache-keys)) | code |
| Frame time with 1 / 2 / 6 characters | not measured (needs logins); with #301 | pending |

Open, not filed: `MainWindowViewModel` still writes `SessionRegistry.PrimaryRenderingSession` from the console's selected character; nothing reads it for input any more.

## Camera and zone terrain (Phase 5B)

- Third-person orbital follow camera, freecam, and first-person mode integrated with [PlayerLocomotionController](../../src/Gordian.Core/Input/PlayerLocomotionController.cs)
- Event camera ([#165](https://github.com/jimmy58663/GordianXI/issues/165)): while a running event's scene routine plays a camera Route (`EventDialogController.Presentation`), the viewport shows that pose instead of the orbit camera (`ViewportCamera.SetEventView`: eye, look-at, vertical field of view from the Route's focal length, roll), converted to display axes (-x, -y, z); the orbit camera keeps its state and takes over again when the event releases the camera. Routes, easing and the fades are in [events/vm.md](../events/vm.md#cutscene-schedulers).
- GPU vertex & index buffer streaming for Phase 4 `ZoneGeometry` / `MeshGroup` models
- Texture palette decoding and NeoVeldrid GPU texture sampler caching
- Directional sun/moon lighting, ambient color, and authentic FFXI distance fog shader pipeline
- Back-face culling as the client does it (2026-09-25): zone meshes without the 0x2E double-sided flag (0x2000) draw with back faces culled (front face = the side the stored normals point out of, clockwise on screen; 99.4% of culled triangles in Bibiki Bay and Southern San d'Oria follow it once mirrored placements, negative scale determinant, have their winding reversed as the client's `ClockwiseCulling` does). Drawing everything double-sided had shown a mountain's inner faces inside Bibiki Bay's entrance cave, covering the smooth tunnel mesh with its angular panels. `DisableZoneBackFaceCulling` restores the old behaviour (code only; no in-game toggle yet). Watch for any retail object that now shows missing faces.
- Zone submesh render state and draw order (#251, 2026-10-04): the client takes a 0x2E submesh's state from its flag word and its mesh name only (xi-tools `docs/zone/format.md`, `docs/zone/export.md` "Clipped floors and walls"; xi-model-viewer `ui/js/zoneModel.js` after xim `GLDrawer.drawXim`). `ZoneSubmeshPasses.Classify` maps it:

  | Submesh | Pass (`ZoneSubmeshPass`) | State |
  |---|---|---|
  | no 0x8000, name not `_` | Pass 1, authored order (`Opaque`) | solid (texture alpha ignored), depth written |
  | no 0x8000, name `_` | Pass 1, authored order (`Cutout`) | alpha test 0.375 on 4 x vertex.a x texel.a, depth written |
  | 0x8000, not water | Pass 1, authored order (`BlendDecal`) | alpha blend, depth tested, not written |
  | 0x8000, name `_` | Pass 3, after entities (`DeferredBlend`) | alpha blend, depth tested, not written |
  | 0x8000 and a water name (`ZoneDefDecoder.IsWaterSurface`) | Pass 3, after entities (`Water`) | alpha blend, water depth bias, not written |

  - *Rule:* only the 0x8000 blend flag makes a submesh translucent. The name and texture hints of `ZoneDefDecoder.IsWaterMesh` (`kawa`, `quf`, `shir`, `umi`...) are GordianXI's own, not the client's, and now apply to blended submeshes only. The world-effect layers (rivers, waterfalls, seas from Section 0x05 generators) still draw in Pass 3a, back to front, after everything that writes depth.
  - *Evidence (Fort Ghelsba, zone 141):* the palisade logs, posts and fences use the bark textures `kawa` and `kawa_hos` (248 placed submeshes in `uge_he01`-`uge_he03`, `uge_bo5x`, `uge_rf05`, `_uge_bo56`, `_uge_uw63`...; all but 2 without the blend flag; 23 double-sided). The river hint `kawa` sent them to the water pass, drawn after the entities without writing depth, so the river (`kw01`-`kw14`, texture `kaw1`) and waterfall (`kwt1`-`kwt4`, `kws1`/`kws2`, texture `tak1`/`tak2`; spray emitters `kem1`/`kem2`) effects drawn after them painted over nearer logs, the logs painted over nearer planks, and a far fence's logs over a near fence. They also covered the blended rope-lashing overlays (`@hata`, 0x8000) that follow them in authored order. Offscreen before/after at Fort Ghelsba (display (-100, 52, 125), orbit pitch 12, yaw 255, distance 10, 12:00): the river no longer shows over the walkway posts and the posts no longer show over the walkway.
  - *Other zones (survey of zones 0-299, 2026-10-04):* 21,031 placed submeshes without the blend flag (677 of them alpha-tested) had been drawn as water and now draw solid: the ships' and airships' sea `em_umi01` / `em_umi02` (zones 46, 47, 58-61, 220-228; offscreen on 220 the sea had striped holes toward the horizon and is now solid), the `quf_*` walls and floors of zones 41, 126 and 127, `shiro` / `shiro01` (50, 245), `gun_umi` (50, 53), `sea_00` (51, 52), `ad_umi` (256, 257), `namiuchi` (249, 250), `suimen` (33, an alpha-tested `_` mesh), and a few more. Blended submeshes are unchanged; Bibiki Bay's sea and cave mouths are world-effect layers and render identically (offscreen diff 0 pixels at the cave mouth at 12:00 and 19:46, and on the sand at 15:58). Open: Qufim's blended `quf_*` decals still match the `quf` hint and draw in the water pass instead of in authored order.
  - *In game (2026-10-04):* the maintainer confirmed Fort Ghelsba's palisades now hide the river and waterfall behind them. Open: blended `quf_*` decals still take the water pass through the name hint ([#255](https://github.com/jimmy58663/GordianXI/issues/255)).
- Generator-bound placements (BlockID FourCC not starting `_`/`@`) are drawn by their Section 0x05 generator, not the placement pass (xi-tools `docs/zone/format.md`): where the generator became an effect layer, the static copy is dropped. Bibiki Bay's cave mouths (`ent1`-`ent4`, mesh `yama_3c_ent`, texture `alb_fg1`) are alpha-blended fog gradients; an extra opaque copy z-fought (flickered) with the tunnel and hid the retail light-to-dark transition. The overlay re-tessellates the tunnel it lies on, so world-effect layers that do not write depth take the decal depth bias (`SkyLayerParams.w`) to stop them z-fighting with the terrain beneath.
  - *Ignore-texture-alpha (2026-09-25):* generator render-state bit 0x1000 (StandardSetup) and the blend opcode's alpha override now reach the effect shader (`WeatherSkyLayer.IgnoreTextureAlpha` / `AlphaOverride`, flag in `SunDirection.w`): the texel alpha counts as opaque (0.5, half scale) so only the vertex alpha ramp and texture factor set coverage. Bibiki Bay's `ent1`-`ent4` set the bit; their rock atlas (`alb_fg1`/`alb_wf1`, DXT3) carries a blocky alpha mask for decal sub-meshes, which had cut the cave-mouth gradient into fully-lit and unlit tiles with hard seams. Verified offscreen at the player's logged spot (-652.66, 21.82, 899.85) at 12:00, 18:00 and 19:46 against a Windower capture: smooth light-to-dark ramp, no tile seams. Open: at 19:46 the mouth's west wall still carries an orange sun tint where retail shows pink (the sun below the horizon still lights west-facing faces).

## Resource cache keys

A cache key must say what the data *is*, unique across the whole install, not what it is called (#163, 2026-09-30). Names (texture names, section ids) repeat across DATs with different contents; keying on them drew one NPC model's face on another.

- **Textures:** every decoded texture carries `DecodedTexture.Source`, the DAT it came from (`file<id>`, or a path for DATs loaded by path such as `ROM/0/0.DAT`) and the section's offset in it (`file1364@266384`). The loaders stamp it: `EntityModelLoader.ParseDatContainer` (model, face and gear DATs), `ZoneDataLoader.ParseZoneContainer` (zones, actor effects), `SharedEffectResources.Parse`. `GpuTextureCache` uploads once per source, shared by every model or zone that loaded that section, and caches a texture without a source on its own (code-built textures, UI). A name is only used to find a texture inside one model's or zone's own texture table (`GpuTextureCache.Resolve`).
- **Not entity ids:** many entities share one model (and its textures), and an entity can change its look under the same id, so per-entity keys would multiply uploads and still need invalidation. Per-entity state (joint palettes, event poses) is keyed by server id because it belongs to the entity.
- **Audited (2026-09-30):** models (`Monster_<model id>`, or race + face + the full gear table), GPU models (the model object), zones, collision and actor effects (zone / model id), item names (item id) and VFS resolution (path) were already keyed by what they are. The stock UI's textures are looked up by name inside one UI library, which is replaced as a whole.

## Frame composition and fog (Tier 1)

- *Tier 1 (3D Scene):* NeoVeldrid terrain, skybox/celestial sky dome (`SkyDomeRenderer`), entity models, directional sun/moon lighting, and authentic FFXI distance fog pass. Clean-room DAT Section `0x2F` Environment decoder (`EnvironmentDecoder`, `ZoneEnvironmentData`) supporting time-of-day keyframe extraction, 8-slice sky dome gradients, and time-of-day cycling (`F10` shortcut / `CycleTimeOfDay`). Fog calibration overhaul with authentic clear visibility presets (Day, Dusk, Night), soft atmospheric haze (Overcast), distant horizon projection for retail `FogStart = 0` keyframes, shader `FogParams.y > 0.0` guards, and runtime fog toggle (`Ctrl+F10` shortcut / `ToggleFog`). Frame composition decoupled into a 3-tier presentation pipeline (`RenderTier1_Scene3D` -> `RenderTier2_StockUi` -> `RenderTier3_ImGuiOverlays`).
