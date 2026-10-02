# GordianXI Project Roadmap & Milestone Tracker

## Current State Summary
- **Target Framework:** .NET 10 (C# 14) + Avalonia UI 12.1.2 + ImGui.NET
- **Test Status:** every PR must pass `dotnet test` on Windows and Ubuntu CI (counts are in each PR's test report).
- **Active Focus:** Phase 5E Tier 2 (stock DAT 2D UI): chunks 1-5, 4b and 4c are done (chunk 4b, the movable stock UI [#25](https://github.com/jimmy58663/GordianXI/issues/25), tested in-game; the viewport's mouse now reaches the client, so right-drag camera look works too) except the expiring-icon blink ([#17](https://github.com/jimmy58663/GordianXI/issues/17), waits on S2C 0x063); mouse input for the stock menus and a look-alike retail pointer ([#22](https://github.com/jimmy58663/GordianXI/issues/22)) are done, tested in-game; chunk 6, dialog text ([#27](https://github.com/jimmy58663/GordianXI/issues/27): zone dialog tables, event scripts, a talk subset of the event VM, the query window) is done, tested in-game and against retail captures (its fixes for zones 256-299, events that run past the next offset and the corpus-checked opcode lengths, [#71](https://github.com/jimmy58663/GordianXI/issues/71)-[#73](https://github.com/jimmy58663/GordianXI/issues/73), are tested in-game); multi-entity events ([#85](https://github.com/jimmy58663/GordianXI/issues/85): every entity carrying the event runs its own VM with request stacks and the 0x27-0x2A companion requests, so the new-character intros play their NPC scenes) are tested in-game (the cutscene camera is part of [#86](https://github.com/jimmy58663/GordianXI/issues/86)); cutscene staging, the first part of the intros ([#86](https://github.com/jimmy58663/GordianXI/issues/86): actors placed, walked, turned and hidden, NO_PCS / NO_NPCS, the cutscene HUD, the event clock and weather) is tested in-game (fixed NPC models (Curilla, Trion, Balasiel: body-region clips, init-hidden weapons, textures keyed by source), [#163](https://github.com/jimmy58663/GordianXI/issues/163), are tested in-game; the cutscene camera, fades and gestures ([#165](https://github.com/jimmy58663/GordianXI/issues/165)) are tested in-game (the Southern San d'Oria intro against a retail recording); the remaining actor opcodes ([#166](https://github.com/jimmy58663/GordianXI/issues/166): motion resets, turn waits, name plates through the intros) are tested in-game; event emotes, the 0x66 motion packages and the event mode mask ([#176](https://github.com/jimmy58663/GordianXI/issues/176)) and the Windurst intros' extra scene zone ([#175](https://github.com/jimmy58663/GordianXI/issues/175)) and the event head look ([#174](https://github.com/jimmy58663/GordianXI/issues/174)) are tested in-game; the talking mouth, blink and head tilt ([#185](https://github.com/jimmy58663/GordianXI/issues/185)) are through one in-game round; the 0x79 sub 2 look axis ([#188](https://github.com/jimmy58663/GordianXI/issues/188), provisional reading) awaits its in-game test; scene effects ([#192](https://github.com/jimmy58663/GordianXI/issues/192): every scheduler band ([#199](https://github.com/jimmy58663/GordianXI/issues/199) but 0xA1), the particle generators of scene routines, the 0x72 white fades, 0x33 / 0x59 sub 5 placed heights) are through one in-game round (the eye-shaped blink mask needs weighted meshes, [#204](https://github.com/jimmy58663/GordianXI/issues/204)); then [#167](https://github.com/jimmy58663/GordianXI/issues/167), [#168](https://github.com/jimmy58663/GordianXI/issues/168)); chunk 6b, the target command menu and its chat-mode list ([#50](https://github.com/jimmy58663/GordianXI/issues/50)), is built and awaits the in-game test; chunk 6c, the NPC shop window ([#60](https://github.com/jimmy58663/GordianXI/issues/60): Buy/Sell, item lists with icons, the quantity prompt, sell appraisal), is through two in-game rounds; in-world name plates ([#28](https://github.com/jimmy58663/GordianXI/issues/28)) are done, tested in-game; the other chat follow-ups are [#48](https://github.com/jimmy58663/GordianXI/issues/48)-[#53](https://github.com/jimmy58663/GordianXI/issues/53). Next up: issues labelled [`next-up`](https://github.com/jimmy58663/GordianXI/issues?q=is%3Aopen+label%3Anext-up). See [docs/ui/stock-ui.md](docs/ui/stock-ui.md) and the [MVP milestone](https://github.com/jimmy58663/GordianXI/milestones).
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
Blowfish, UDP framing, zero-allocation O(1) dispatcher, 86 S2C decoders / 77 C2S opcodes, zone transitions, datagram telemetry. → [docs/network/session-and-packets.md](docs/network/session-and-packets.md)
- Packet audit gaps closed (#1-#5); the UI that reads the new caches is tracked in #90-#95.
- Open: the [XiPackets coverage audit](docs/network/session-and-packets.md#xipackets-coverage-audit-2026-09-28) gaps (#97-#117, #119).

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
| 5D.1 Transient combat & action animation | ⏳ swings, casts, flinch / guard reactions, stance recovery and the engagement end (#10-#13, #67) tested in game; knockback (#14) built, awaiting an in-game knockback check; follow-ups #132, #136-#138 | [world/entities-and-animation.md](docs/world/entities-and-animation.md) |
| 5E Tier 1: sky, weather, lighting, particles | ✅ (open: retail brightness checks) | [sky-and-weather](docs/rendering/sky-and-weather.md), [lighting](docs/rendering/lighting.md), [particles](docs/rendering/particles.md) |
| 5E Tier 2: stock DAT 2D UI | ⏳ chunks 1-6, 4b, 4c done, 6b (command menu) awaiting the in-game test, 6c (shop) through two in-game rounds; name plates (#28) tested in-game | [ui/stock-ui.md](docs/ui/stock-ui.md) |
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
