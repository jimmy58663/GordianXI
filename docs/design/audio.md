# Audio Engine (Planned)

> Design notes for Phase 5H: sound backend, DAT sound decode and the effect-linked sounds already found in zone generators. Status and open work: [ROADMAP.md](../../ROADMAP.md) and GitHub Issues.

## Phase 5H plan

- [ ] Zone effect audio: ~5.9k Section 0x05 generators link a sound (`0x3D`) with near/far range (`0x4C`), time-of-day volume (`0x43`) and path-following emitters (`0x6B`, shoreline waves); they run on the existing zone particle runtime and need only the sound backend.
- [ ] Select a cross-platform managed audio backend (must avoid Windows-only APIs per `AGENTS.md`; no audio library of any kind is referenced anywhere in the codebase today).
- [ ] Clean-room decode of FFXI DAT sound resources (footstep sets, spell/weaponskill/ability SFX, UI cues, ambient zone loops, BGM tracks), if stored in DAT containers.
- [ ] Footstep & movement SFX tied to `PlayerLocomotionController`/animation state; the surface under each foot comes from the decoded collision terrain type (`CollisionTriangle.Terrain`: object, path, grass, sand, snow, stone, metal, wood, shallow/deep water), and sand/snow leave footprints. (FFXI has no swimming: water edges are ordinary collision barriers.)
- [ ] Combat/action SFX tied to `CombatPacketModule` action/effect events (`0x028`/`0x030`/`0x0AA`).
- [ ] Ambient zone loops & BGM playback tied to `WorldState.ZoneChanged`.
- [ ] UI/menu sound cues (target, cursor move, confirm, cancel).
- [ ] Master/category volume mixing (SFX/BGM/Ambient/UI) with persisted settings.
