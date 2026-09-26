# World State, DAT Resources & VFS

> Entity store, game state caches, DAT decoders and the modular virtual file system (Phase 4). Runtime counters are described in [PERFORMANCE_TELEMETRY.md](../PERFORMANCE_TELEMETRY.md). Status and open work: [ROADMAP.md](../../ROADMAP.md) and GitHub Issues.

## Entity store and state caches

- **Thread-Safe Spatial Entity Store (`Gordian.Core/World`):**
  - Tracking for LocalPlayer, other PCs, NPCs, Monsters, Pets, and Trusts
  - Indexing by Server ID (`uint32`) and Zone Target Index (`uint16`)
  - Fast 3D spatial partitioning (Uniform Grid / BVH) for distance, cone, and line-of-sight queries
  - Dead-reckoning & position interpolation between 250ms network ticks
- **Game State Caches:**
  - Multi-container inventory cache (Inventory, Wardrobes 1-8, Satchel, Sack, Case, Safe, Storage)
  - Active player vitals (HP/MP/TP), base attributes, equipment loadout, buff/debuff timers

## DAT decoders

- **FFXI DAT Binary Decoders (`Gordian.Core/Resources`):**
  - Clean-room decoders for ROM directory DAT files (string tables, item tables, spell/ability tables)
  - Zone collision meshes, terrain geometry, entity models, and animation tables

## Virtual file system and asset overrides

- **Modular Virtual File System (VFS) & Asset Overrides (XIPivot Architecture):**
  - Tiered VFS search paths (portable local root + `%LOCALAPPDATA%` user storage)
  - XIPivot-compatible legacy DAT overlay scanner (`resources/dats/`) with uppercase invariant indexing
  - Modern asset pack scanner (`resources/assets/`) with `manifest.json` parsing and DAT aliasing (`.glb` replacing `.DAT`)
  - Master `vfs.json` load order, priority stacking, and pack auto-discovery
  - Safe debounced runtime hot-reloading with master toggle
  - Modder documentation and reference manifests distributed in `resources/`

## State and memory telemetry

- **State & Memory Health Telemetry:**
  - Managed heap & GC pressure counters (.NET 10 Gen 0/1/2 collection tracking, heap allocation velocity)
  - Spatial partition & uniform grid query duration tracking with entity dead-reckoning cycle time benchmarking
