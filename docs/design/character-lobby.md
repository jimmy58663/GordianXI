# Character Lobby (Planned)

> Design notes for Phase 5G: character select, creation, deletion and loading screens. MVP-blocking. Status and open work: [ROADMAP.md](../../ROADMAP.md) and GitHub Issues.

## Phase 5G plan

- [ ] **Research Task:** Determine whether retail's character-select/creation lobby (background, race/face preview models, menu chrome) is DAT-driven or hardcoded in `ffxi.exe`/`pol.exe`; identify the specific ROM/DAT section(s) if DAT-driven, per the clean-room boundary rules in `AGENTS.md`.
- [ ] Clean-room decode of any identified lobby DAT resources (layout, textures, preview model refs, button hit regions) — cite the reference source in XML doc-comments per `AGENTS.md` protocol-attribution standards if community research (e.g. LandSandBoat's login/char-select handling) informs the wire format.
- [ ] Render the lobby/character-select screen: existing character list, race/face/job preview, and navigation.
- [ ] **Character Creation Flow:** race, face, starting nation, name entry, live appearance preview.
- [ ] **Character Deletion Flow:** confirmation step, matching LSB's delete-code/security flow if one is enforced server-side.
- [ ] Wire lobby actions (list/create/delete/select-and-enter-world) to LSB login-server requests, extending `LsbLoginClient`.
- [ ] **Loading Screen:** identify whether retail uses a DAT-sourced loading-screen asset (background art, progress indicator) and implement a loading/transition UI state for character-select → zone-in and for zone-to-zone transitions — `PerformZoneTransitionAsync` currently has no visual loading state at all.
- [ ] Integrate the lobby ahead of the existing `ProxyStager`/Named Pipe handoff flow without breaking the current direct-to-zone "saved profile" fast path used by `Local (Cybin)`-style launches.
