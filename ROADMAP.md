# GordianXI Project Roadmap & Milestone Tracker

## Current State Summary
- **Target Framework:** .NET 10 (C# 14) + Avalonia UI 12.1.3 + Dear ImGui (Hexa.NET.ImGui)
- **Test Status:** every PR must pass `dotnet test` on Windows and Ubuntu CI (counts are in each PR's test report).
- **Active Focus:** finishing Phase 5 (MVP): the remaining stock UI windows and menus (5E Tier 2), the Tier 3 overlays, and the open issues in the [MVP milestone](https://github.com/jimmy58663/GordianXI/milestones). Next up: issues labelled [`next-up`](https://github.com/jimmy58663/GordianXI/issues?q=is%3Aopen+label%3Anext-up). Stock UI details: [docs/ui/stock-ui.md](docs/ui/stock-ui.md).
- **North Star Goal:** High-performance, clean-room 64-bit cross-platform client replacement for Final Fantasy XI.

## How This Repo Tracks Work
- **This file:** phase status, MVP definition, active focus. Keep it short; do not grow it into a changelog. Active Focus stays a few sentences (the phase being finished and where to look); per-sub-phase status lives in the table, one short cell each, with details in issues and `docs/`. Update it in the same PR that changes a phase's status or the active focus.
- **What to work on next:** open issues labelled `next-up`, then the rest of the current milestone.
- **[`docs/`](docs/README.md):** how each subsystem works: formats, formulas, calibrations against Windower captures, verification commands. Read the matching doc before working in an area, and record new findings there.
- **[GitHub Issues](https://github.com/jimmy58663/GordianXI/issues):** open tasks, known gaps and bugs, grouped by [milestone](https://github.com/jimmy58663/GordianXI/milestones) and labelled by area: one milestone per open phase (MVP (Phase 5), Phase 5H: Audio, Phase 9: CI, packaging & releases), plus a Post-MVP backlog for enhancements no phase has scheduled yet. A later phase gets its milestone when it starts. Reference them from commits (`Closes #N`).
- **Git history:** what was done and when.

---

## 🏆 MVP Definition (Minimum Viable Product)
> **Goal:** A player can launch GordianXI, connect directly to a LandSandBoat private server, authenticate through a character lobby (create/select/delete a character), spawn into a zone with 3D world geometry and character models rendered, control movement and camera using customizable Keyboard/Mouse or Gamepad while being correctly blocked by walls/terrain/water instead of clipping through them, execute basic actions and slash commands via an interactive CLI console or hotkeys, view chat/vitals/inventory, and cross zonelines to new map servers without dropping session state.
>
> *MVP includes Phases 1 through 5, including sub-phases 5A-5G — World Collision & Navigation (5F) and the Character Lobby (5G) are MVP-blocking, since a client that lets players clip through geometry or cannot create/select a character without an external tool does not meet the MVP goal above. Phase 5H (Audio) and Phases 6 through 10 represent Post-MVP extensions.*

---

## 🗺️ Phases & Milestones

### ✅ Phase 1: Foundation & Client Infrastructure
Four-tier monorepo, clean-room rules, Avalonia MVVM shell, encrypted account profiles, packet inspector. → [docs/app/desktop-shell.md](docs/app/desktop-shell.md)

### ✅ Phase 2: Bootloader Session Handoff & Direct Authentication
`xiloader`/`pol` COM handoff through the ephemeral `FFXiMain.dll` proxy, Named Pipe IPC, native LSB login (`LsbLoginClient`). → [docs/network/session-and-packets.md](docs/network/session-and-packets.md)

### ✅ Phase 3: Complete LSB Packet Engine & Zone Transitions
Blowfish, UDP framing, zero-allocation O(1) dispatcher, 86 S2C decoders / 77 C2S opcodes, zone transitions, datagram telemetry. → [docs/network/session-and-packets.md](docs/network/session-and-packets.md)
- Packet audit gaps closed (#1-#5); the UI that reads the new caches is tracked in #90-#95.
- Open: the [XiPackets coverage audit](docs/network/session-and-packets.md#xipackets-coverage-audit-2026-09-28) gaps still open: #112, #117 (scheduler playback: #311).

### ✅ Phase 4: World State, DAT Resource Pipeline & Modular VFS
Spatial entity store, dead reckoning, state caches, DAT decoders, XIPivot-compatible VFS with hot reload. → [docs/world/world-state-and-resources.md](docs/world/world-state-and-resources.md)

### ⏳ Phase 5: Viewport Rendering, CLI Console & Input Subsystem (MVP Completion)
| Sub-phase | Status | Doc |
|---|---|---|
| Console, action service & input | ✅ | [input/console-and-input.md](docs/input/console-and-input.md) |
| 5A Graphics context & multi-box viewports | ✅ | [rendering/viewport-and-terrain.md](docs/rendering/viewport-and-terrain.md) |
| 5B Camera & zone terrain | ✅ | [rendering/viewport-and-terrain.md](docs/rendering/viewport-and-terrain.md) |
| 5C Entity models & equipment | ✅ (model-embedded idle effects done, e.g. Home Point crystal; open: routine playback from the network, actor status visuals, actor-attached weather #121) | [world/entities-and-animation.md](docs/world/entities-and-animation.md) |
| 5D Skeletal animation | ✅ | [world/entities-and-animation.md](docs/world/entities-and-animation.md) |
| 5D.1 Transient combat & action animation | ✅ (follow-ups #132, #307, #308) | [world/entities-and-animation.md](docs/world/entities-and-animation.md) |
| 5E Tier 1: sky, weather, lighting, particles | ✅ (open: retail brightness checks) | [sky-and-weather](docs/rendering/sky-and-weather.md), [lighting](docs/rendering/lighting.md), [particles](docs/rendering/particles.md) |
| 5E Tier 2: stock DAT 2D UI | ⏳ HUD, chat and log, config pages, command menu, shop, name plates and event dialog done; remaining windows and menus in the MVP milestone | [ui/stock-ui.md](docs/ui/stock-ui.md) |
| 5E Tier 3: ImGui overlays, UI suppression API, perf overlay | ⬜ | [ui/stock-ui.md](docs/ui/stock-ui.md) |
| 5F World collision & ground physics (blocking) | ✅ | [world/collision-and-physics.md](docs/world/collision-and-physics.md) |
| 5G Character lobby, creation & deletion (blocking) | ✅ | [design/character-lobby.md](docs/design/character-lobby.md) |
| 5H Audio (non-blocking) | ⏳ music, ambience, zone and event sound, footsteps and UI cues play through OpenAL Soft, with a clean-room ATRAC3 decoder; open: combat sounds #41, footprints #40, sound controls #265 | [design/audio.md](docs/design/audio.md) |

### 🚀 Post-MVP (Phases 6-10)
Design intent for each is in [docs/design/post-mvp.md](docs/design/post-mvp.md); issues are opened when a phase starts.
- **Phase 6:** Sandboxed Lua addon runtime, 3-tier addon action API, capability security, package manager.
- **Phase 7:** Gambit automation engine, multi-box swarm coordination, `FeatureRestrictions` kill-switch compliance.
- **Phase 8:** Multi-instance desktop shell, input broadcasting, background throttling.
- **Phase 9:** Self-updating client and cross-platform CI packaging.
- **Phase 10:** GordianXI MCP server and AI assistant tooling.
