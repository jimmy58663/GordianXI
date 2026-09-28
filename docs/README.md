# GordianXI Technical Documentation

How each subsystem works, what the retail data looks like, and how our behaviour was calibrated against the legacy client (Windower captures). Project status lives in [ROADMAP.md](../ROADMAP.md); open tasks live in [GitHub Issues](https://github.com/jimmy58663/GordianXI/issues).

All formats described here were researched clean-room from public sources (LandSandBoat, XiPackets, xi-tools, xi-model-viewer) and retail data; see `AGENTS.md` for the attribution rules.

| Area | Doc | Covers |
|---|---|---|
| App | [app/desktop-shell.md](app/desktop-shell.md) | Avalonia shell, account profiles, packet inspector |
| Network | [network/session-and-packets.md](network/session-and-packets.md) | Bootloader handoff, LSB login, Blowfish, UDP framing, packet registry, 0x015 timing, packet audit gaps |
| World | [world/world-state-and-resources.md](world/world-state-and-resources.md) | Entity store, state caches, DAT decoders, VFS & asset overrides |
| World | [world/entities-and-animation.md](world/entities-and-animation.md) | Character assembly, NPC/monster models, skeletal animation, combat animation plan, knockback |
| World | [world/collision-and-physics.md](world/collision-and-physics.md) | Collision soup, ground following, falling, walls, elevators, camera collision, WPOS authority |
| Input | [input/console-and-input.md](input/console-and-input.md) | Command console, `PlayerActionService`, keyboard/mouse/gamepad |
| Rendering | [rendering/viewport-and-terrain.md](rendering/viewport-and-terrain.md) | Veldrid context, viewport windows, camera, terrain, back-face culling, frame tiers |
| Rendering | [rendering/sky-and-weather.md](rendering/sky-and-weather.md) | Sky dome, stars, moon, clouds, sun, lens flares, live weather |
| Rendering | [rendering/lighting.md](rendering/lighting.md) | 0x2F light conversion, point lights, sub-environments, actor lighting |
| Rendering | [rendering/particles.md](rendering/particles.md) | Zone particle runtime, water surfaces, emitters, weather routines |
| UI | [ui/stock-ui.md](ui/stock-ui.md) | Stock DAT 2D UI: decoders, renderer, HUD, menus, config pages, dump tooling |
| Design | [design/character-lobby.md](design/character-lobby.md) | Phase 5G plan (MVP-blocking) |
| Design | [design/audio.md](design/audio.md) | Phase 5H plan |
| Design | [design/post-mvp.md](design/post-mvp.md) | Phases 6-10 |
| Design | [design/distribution.md](design/distribution.md) | Docs site, client updater, addon registry, patch diffs (preliminary) |
| Perf | [PERFORMANCE_TELEMETRY.md](PERFORMANCE_TELEMETRY.md) | Runtime performance counters |

## Keeping these docs useful
- One doc per subsystem. When a finding changes how something works (a new calibration, a decoded field, a corrected assumption), update the doc in the same commit as the code.
- Record the evidence with the value: which capture, zone, time of day, or reference source it came from.
- Open work belongs in GitHub Issues; a doc may keep a short "planned" section when the design notes are needed to do the work.
