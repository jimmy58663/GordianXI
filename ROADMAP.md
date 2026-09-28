# GordianXI Project Roadmap & Milestone Tracker

## Current State Summary
- **Target Framework:** .NET 10 (C# 14) + Avalonia UI 12.1.2 + ImGui.NET
- **Test Status:** every PR must pass `dotnet test` on Windows and Ubuntu CI (counts are in each PR's test report).
- **Active Focus:** Phase 5E Tier 2 (stock DAT 2D UI): chunks 1-5, 4b and 4c are done (chunk 4b, the movable stock UI [#25](https://github.com/jimmy58663/GordianXI/issues/25), tested in-game; the viewport's mouse now reaches the client, so right-drag camera look works too) except the expiring-icon blink ([#17](https://github.com/jimmy58663/GordianXI/issues/17), waits on S2C 0x063); mouse input for the stock menus and a look-alike retail pointer ([#22](https://github.com/jimmy58663/GordianXI/issues/22)) are done, tested in-game; chunk 6, dialog text ([#27](https://github.com/jimmy58663/GordianXI/issues/27): zone dialog tables, event scripts, a talk subset of the event VM, the query window) is done, tested in-game and against retail captures; chunk 6b, the target command menu and its chat-mode list ([#50](https://github.com/jimmy58663/GordianXI/issues/50)), is built and awaits the in-game test; chunk 6c, the NPC shop window ([#60](https://github.com/jimmy58663/GordianXI/issues/60): Buy/Sell, item lists with icons, the quantity prompt, sell appraisal), is through two in-game rounds; the other chat follow-ups are [#48](https://github.com/jimmy58663/GordianXI/issues/48)-[#53](https://github.com/jimmy58663/GordianXI/issues/53). Next up: issues labelled [`next-up`](https://github.com/jimmy58663/GordianXI/issues?q=is%3Aopen+label%3Anext-up). See [docs/ui/stock-ui.md](docs/ui/stock-ui.md) and the [MVP milestone](https://github.com/jimmy58663/GordianXI/milestones).
- **North Star Goal:** High-performance, clean-room 64-bit cross-platform client replacement for Final Fantasy XI.

## How This Repo Tracks Work
- **This file:** phase status, MVP definition, active focus. Keep it short; do not grow it into a changelog. Update it in the same PR that changes a phase's status or the active focus.
- **What to work on next:** open issues labelled `next-up`, then the rest of the current milestone.
- **[`docs/`](docs/README.md):** how each subsystem works: formats, formulas, calibrations against Windower captures, verification commands. Read the matching doc before working in an area, and record new findings there.
- **[GitHub Issues](https://github.com/jimmy58663/GordianXI/issues):** open tasks, known gaps and bugs, grouped by milestone (one per open phase) and labelled by area. Reference them from commits (`Closes #N`).
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
Blowfish, UDP framing, zero-allocation O(1) dispatcher, 81 S2C decoders / 64 C2S builders, zone transitions, datagram telemetry. → [docs/network/session-and-packets.md](docs/network/session-and-packets.md)
- Open: packet audit gaps (decoded-but-discarded S2C data).

### ✅ Phase 4: World State, DAT Resource Pipeline & Modular VFS
Spatial entity store, dead reckoning, state caches, DAT decoders, XIPivot-compatible VFS with hot reload. → [docs/world/world-state-and-resources.md](docs/world/world-state-and-resources.md)

### ⏳ Phase 5: Viewport Rendering, CLI Console & Input Subsystem (MVP Completion)
| Sub-phase | Status | Doc |
|---|---|---|
| Console, action service & input | ✅ | [input/console-and-input.md](docs/input/console-and-input.md) |
| 5A Graphics context & multi-box viewports | ✅ | [rendering/viewport-and-terrain.md](docs/rendering/viewport-and-terrain.md) |
| 5B Camera & zone terrain | ✅ | [rendering/viewport-and-terrain.md](docs/rendering/viewport-and-terrain.md) |
| 5C Entity models & equipment | ✅ (open: actor status visuals, model-embedded effect routines) | [world/entities-and-animation.md](docs/world/entities-and-animation.md) |
| 5D Skeletal animation | ✅ | [world/entities-and-animation.md](docs/world/entities-and-animation.md) |
| 5D.1 Transient combat & action animation | ⬜ | [world/entities-and-animation.md](docs/world/entities-and-animation.md) |
| 5E Tier 1: sky, weather, lighting, particles | ✅ (open: retail brightness checks) | [sky-and-weather](docs/rendering/sky-and-weather.md), [lighting](docs/rendering/lighting.md), [particles](docs/rendering/particles.md) |
| 5E Tier 2: stock DAT 2D UI | ⏳ chunks 1-6, 4b, 4c done, 6b (command menu) awaiting the in-game test, 6c (shop) through two in-game rounds; name plates open | [ui/stock-ui.md](docs/ui/stock-ui.md) |
| 5E Tier 3: ImGui overlays, UI suppression API, perf overlay | ⬜ | [ui/stock-ui.md](docs/ui/stock-ui.md) |
| 5F World collision & ground physics (blocking) | ✅ | [world/collision-and-physics.md](docs/world/collision-and-physics.md) |
| 5G Character lobby, creation & deletion (blocking) | ⬜ | [design/character-lobby.md](docs/design/character-lobby.md) |
| 5H Audio (non-blocking) | ⬜ | [design/audio.md](docs/design/audio.md) |

### 🚀 Post-MVP (Phases 6-10)
Design intent for each is in [docs/design/post-mvp.md](docs/design/post-mvp.md); issues are opened when a phase starts.
- **Phase 6:** Sandboxed Lua addon runtime, 3-tier addon action API, capability security, package manager.
- **Phase 7:** Gambit automation engine, multi-box swarm coordination, `FeatureRestrictions` kill-switch compliance.
- **Phase 8:** Multi-instance desktop shell, input broadcasting, background throttling.
- **Phase 9:** Self-updating client and cross-platform CI packaging.
- **Phase 10:** GordianXI MCP server and AI assistant tooling.
