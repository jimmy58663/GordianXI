# Viewport, Camera & Zone Terrain

> Veldrid graphics context, viewport windows, camera, and the zone terrain renderer (Phases 5A and 5B), plus the three-tier frame composition. Status and open work: [ROADMAP.md](../../ROADMAP.md) and GitHub Issues.

## Graphics context and viewport windows (Phase 5A)

- Integrate Veldrid, Veldrid.SPIRV, and Veldrid.ImGui into client infrastructure
- Avalonia `NativeControlHost` cross-platform viewport control (`VeldridViewportControl`) supporting Windows (`HWND`), Linux (`X11`/`Wayland`), and macOS (`NSView`)
- Multi-backend auto-selection (Direct3D 11 on Windows, Vulkan on Linux/Windows, Metal on macOS, OpenGL fallback)
- Resilient 60/120 FPS render loop with device recreation on viewport resize
- Verification test scene: textured spinning 3D test cube and color gradient clearing to confirm GPU pipeline integrity
- **Decoupled 3D Rendering Window (`ViewportWindow`) & Lifecycle Coordinator (`ViewportWindowManager`):**
  - Gameplay graphics run in a separate hardware-accelerated window, preserving `MainWindow` as the central Control Panel (profiles, packet inspection, state diagnostics, chat console, and display settings).
  - Three display modes: **Borderless Window** (default, borderless work area alignment), **Windowed** (movable & resizable with min dimension safeguards), and **Fullscreen** (`WindowState.FullScreen`, toggleable via `F11`).
  - Multi-option character tab switcher styles: **Floating Pill** (top-center glass dynamic island - default), **Top Ribbon** (auto-hiding), **Side Rail** (vertical party deck with live HP/MP vitals), and **Hotkeys Only** (`Ctrl+Tab` / `Ctrl+Shift+Tab`).
  - **Multi-Monitor Tear-Off / Pop-Out (`⧉`):** Single master Veldrid `GraphicsDevice` context driving multiple independent window `Swapchain` instances across monitors with zero VRAM waste or asset duplication.
  - **Picture-in-Picture (PiP) Multi-Box Swarm Streaming:** Real-time thumbnail sub-viewports for up to 5 background characters (1 main + 5 alts) with 1-click `⇄` viewport promotion and throttled background render rates.

## Camera and zone terrain (Phase 5B)

- Third-person orbital follow camera, freecam, and first-person mode integrated with [PlayerLocomotionController](../../src/Gordian.Core/Input/PlayerLocomotionController.cs)
- Event camera ([#165](https://github.com/jimmy58663/GordianXI/issues/165)): while a running event's scene routine plays a camera Route (`EventDialogController.Presentation`), the viewport shows that pose instead of the orbit camera (`ViewportCamera.SetEventView`: eye, look-at, vertical field of view from the Route's focal length, roll), converted to display axes (-x, -y, z); the orbit camera keeps its state and takes over again when the event releases the camera. Routes, easing and the fades are in [ui/stock-ui.md](../ui/stock-ui.md) (*Cutscene schedulers*).
- GPU vertex & index buffer streaming for Phase 4 `ZoneGeometry` / `MeshGroup` models
- Texture palette decoding and Veldrid GPU texture sampler caching
- Directional sun/moon lighting, ambient color, and authentic FFXI distance fog shader pipeline
- Back-face culling as the client does it (2026-09-25): zone meshes without the 0x2E double-sided flag (0x2000) draw with back faces culled (front face = the side the stored normals point out of, clockwise on screen; 99.4% of culled triangles in Bibiki Bay and Southern San d'Oria follow it once mirrored placements, negative scale determinant, have their winding reversed as the client's `ClockwiseCulling` does). Drawing everything double-sided had shown a mountain's inner faces inside Bibiki Bay's entrance cave, covering the smooth tunnel mesh with its angular panels. `DisableZoneBackFaceCulling` restores the old behaviour (code only; no in-game toggle yet). Watch for any retail object that now shows missing faces.
- Generator-bound placements (BlockID FourCC not starting `_`/`@`) are drawn by their Section 0x05 generator, not the placement pass (xi-tools `docs/zone/format.md`): where the generator became an effect layer, the static copy is dropped. Bibiki Bay's cave mouths (`ent1`-`ent4`, mesh `yama_3c_ent`, texture `alb_fg1`) are alpha-blended fog gradients; an extra opaque copy z-fought (flickered) with the tunnel and hid the retail light-to-dark transition. The overlay re-tessellates the tunnel it lies on, so world-effect layers that do not write depth take the decal depth bias (`SkyLayerParams.w`) to stop them z-fighting with the terrain beneath.
  - *Ignore-texture-alpha (2026-09-25):* generator render-state bit 0x1000 (StandardSetup) and the blend opcode's alpha override now reach the effect shader (`WeatherSkyLayer.IgnoreTextureAlpha` / `AlphaOverride`, flag in `SunDirection.w`): the texel alpha counts as opaque (0.5, half scale) so only the vertex alpha ramp and texture factor set coverage. Bibiki Bay's `ent1`-`ent4` set the bit; their rock atlas (`alb_fg1`/`alb_wf1`, DXT3) carries a blocky alpha mask for decal sub-meshes, which had cut the cave-mouth gradient into fully-lit and unlit tiles with hard seams. Verified offscreen at the player's logged spot (-652.66, 21.82, 899.85) at 12:00, 18:00 and 19:46 against a Windower capture: smooth light-to-dark ramp, no tile seams. Open: at 19:46 the mouth's west wall still carries an orange sun tint where retail shows pink (the sun below the horizon still lights west-facing faces).

## Frame composition and fog (Tier 1)

- *Tier 1 (3D Scene):* Veldrid terrain, skybox/celestial sky dome (`SkyDomeRenderer`), entity models, directional sun/moon lighting, and authentic FFXI distance fog pass. Clean-room DAT Section `0x2F` Environment decoder (`EnvironmentDecoder`, `ZoneEnvironmentData`) supporting time-of-day keyframe extraction, 8-slice sky dome gradients, and time-of-day cycling (`F10` shortcut / `CycleTimeOfDay`). Fog calibration overhaul with authentic clear visibility presets (Day, Dusk, Night), soft atmospheric haze (Overcast), distant horizon projection for retail `FogStart = 0` keyframes, shader `FogParams.y > 0.0` guards, and runtime fog toggle (`Ctrl+F10` shortcut / `ToggleFog`). Frame composition decoupled into a 3-tier presentation pipeline (`RenderTier1_Scene3D` -> `RenderTier2_StockUi` -> `RenderTier3_ImGuiOverlays`).
