# Character Lobby (Planned)

> Design notes for Phase 5G: character select, creation, deletion and loading screens. MVP-blocking. Status and open work: [ROADMAP.md](../../ROADMAP.md) and GitHub Issues.

## Phase 5G plan

- [x] **Research Task:** Determine whether retail's character-select/creation lobby (background, race/face preview models, menu chrome) is DAT-driven or hardcoded in `ffxi.exe`/`pol.exe`; identify the specific ROM/DAT section(s) if DAT-driven, per the clean-room boundary rules in `AGENTS.md`. It is DAT-driven: see [What the lobby is made of](#what-the-lobby-is-made-of) ([#31](https://github.com/jimmy58663/GordianXI/issues/31)).
- [ ] Clean-room decode of any identified lobby DAT resources (layout, textures, preview model refs, button hit regions) — cite the reference source in XML doc-comments per `AGENTS.md` protocol-attribution standards if community research (e.g. LandSandBoat's login/char-select handling) informs the wire format.
- [ ] Render the lobby/character-select screen: existing character list, race/face/job preview, and navigation.
- [ ] **Character Creation Flow:** race, face, starting nation, name entry, live appearance preview.
- [ ] **Character Deletion Flow:** confirmation step, matching LSB's delete-code/security flow if one is enforced server-side.
- [ ] Wire lobby actions (list/create/delete/select-and-enter-world) to LSB login-server requests, extending `LsbLoginClient`.
- [ ] **Loading Screen:** identify whether retail uses a DAT-sourced loading-screen asset (background art, progress indicator) and implement a loading/transition UI state for character-select → zone-in and for zone-to-zone transitions — `PerformZoneTransitionAsync` currently has no visual loading state at all.
- [ ] Integrate the lobby ahead of the existing `ProxyStager`/Named Pipe handoff flow without breaking the current direct-to-zone "saved profile" fast path used by `Local (Cybin)`-style launches.

## What the lobby is made of

Research only (2026-09-28, [#31](https://github.com/jimmy58663/GordianXI/issues/31)); nothing reads these files yet. Sources: xi-tools `docs/title/` (`README.md`, `ui_chrome.md`, `main_menu.md`) and `docs/dats/ROM_0_23.md`, checked against the retail install. One xi-tools claim is corrected below (the captions).

- **Title background: `ROM/0/23.DAT`** (magic `titl`, 73,872 bytes). A live 3D fly-through of real zones, not an image; the file holds no geometry or textures of its own. Sections: 317 `0x06` camera routes (`DatSectionType.Route`; decoded for cutscenes by `EventSceneResource`, the title DAT's routes are not read yet), 24 `0x07` routines, 3 `0x2F` environments. It has 22 zone segments, each with its own camera family (`cgu*` = North Gustaberg, `cqf*` = Qufim...) and per-segment weather and fog; there is no time-of-day field. Segment 12 always plays first; later segments are picked at run time by a rule that is not in the file. A `0x0210` record carries a hold/duration value whose unit is unproven. Playing it needs the zone renderer plus a camera-route player.
- **Lobby UI: `ROM/119/50.DAT`** (magic `lobb`, 1,358,496 bytes; English. JP `ROM/91/16`, DE `ROM/176/74`, FR `ROM/178/13` per xi-tools). It holds:
  - 9 `0x20` textures: `chmk` (character-make font), `titl` (title art), `abxy` (controller glyphs), `ex1u`/`ex2u`/`ex5u` (expansion logos), `b1n`, `otp`, `ward` (wardrobe badge).
  - 56 `0x30` menus, **all of which decode with `UiMenuDecoder`** (same layout as the in-game menu DAT): the main strip `loby2win` (Select / Create / Delete / Config / Back) with keyboard (`chswin`) and controller (`chs360`) variants; character creation `chmkrace`, `chmkface`, `chmkjobs`, `chmkhair`, `chmksize`, `chmktown`, `chmkname`, `chmkserv`, `chmkpass` with hit areas `race1`-`race8` and `nation1`-`nation3`; name-entry dialogs `hn*`; progress, warnings and confirmations `ptc*` (delete: `ptc9dele`); world select `worldsel`; the character list `lobycwin` / `lobyc360`; `dbaccwin`, `lobyhelp`.
  - One large `0x31` element group, `menu/lobbywin`.
- **Captions are sprites, not text.** Each `loby2win` button references a label image in `lobbywin` (Select Character = kind 0 image 126, with its alternate look as kind 4 image 127; Create 128/129, Delete 130/131, then 132 and 183). xi-tools reads these numbers as text ids with a missing string table; they are shape references, the same mechanism as the in-game `windowps` labels. The character-creation menus draw their labels from the in-game `windowps` set directly (`chmkrace` buttons: images 112-119; `chmktown`: 584-586, from ROM/119/51). So the lobby chrome can render through the Tier 2 stock UI pipeline (`UiResourceLibrary`, `StockUiMenuWindow`; see [ui/stock-ui.md](../ui/stock-ui.md)).
- **Not in these files:** the client-composed content (character list rows, name-entry text, the preview model, which is an ordinary PC model built from race, face and equipment), the lobby wire protocol ([#35](https://github.com/jimmy58663/GordianXI/issues/35)), loading-screen art ([#36](https://github.com/jimmy58663/GordianXI/issues/36)), and the title segment picker.
