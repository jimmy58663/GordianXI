# Retail USER files

> What the retail client keeps per character in `FINAL FANTASY XI/USER/<id>/`, as far as GordianXI reads it. GordianXI never writes there (AGENTS.md: no permanent game-folder changes); importing is an explicit action ([#51](https://github.com/jimmy58663/GordianXI/issues/51)). Status and open work: [ROADMAP.md](../../ROADMAP.md) and GitHub Issues.

**Beyond xi-tools / LandSandBoat:** none of the public references documents these files; everything here is our reading of the maintainer's retail install (2026-09 / 10), so each field says how sure it is.

## Import (`/importretail`, [#51](https://github.com/jimmy58663/GordianXI/issues/51))

- Typed on the stock chat line (2026-10-04, awaiting the in-game test). `/importretail` lists the USER folders, newest first, with each `cnf.dat`'s last-modified time, marking the folder named after this character's id in hex ("<- this character?"; a guess, see below) and those without a `cnf.dat`. `/importretail <folder>` reads that folder's `cnf.dat` and overwrites the character's GordianXI settings with what it understands: every Font Colors row and the Log page's routing (`RetailUserSettings`, `RetailCnf`), then prints what it imported. Nothing is imported automatically and nothing is ever written to the game folder (maintainer's decision, 2026-09-27); file and folder names are looked up in their exact case (`USER`, `cnf.dat`).
- The guess uses `CharacterSession.CharacterId` (the server's character id), which equals the content id on LSB; the lobby's content id (`LsbCharacterInfo.ContentId`) is not carried into the session yet, nor is whatever the xiloader / pol handoff passes.
- Not imported yet: the window settings (log lines, timestamps, multi-window), volumes and the other config fields (not decoded, below) and macros.

## Folders

- One folder per character, named like the character's content id in lowercase hex without leading zeros (`167dc34`, `c3cbbf`); the LSB characters 1-6 on the maintainer's install have folders `1`..`6`. To confirm with a known character (see [#51](https://github.com/jimmy58663/GordianXI/issues/51)'s comment); GordianXI's import lists every folder with its last-modified time.
- `tig.dat` (24 bytes) sits beside the folders; not decoded.
- Evidence set: 15 `cnf.dat` files, all 744 bytes. `USER/3`, `4`, `5` and `6` are characters created in 2026-09 and never configured: their files are identical (md5 `04b8ae60...`), so they are the retail defaults.

## cnf.dat (744 bytes): client config

Little-endian. Values below are "fresh" (the four never-configured characters) unless noted.

### Font Colors ([#53](https://github.com/jimmy58663/GordianXI/issues/53))

- **0x50: 23 colour entries of 4 bytes, then five more at 0x22C, 0x26C, 0x278, 0x2D8, 0x2DC.** Each entry is **B, G, R, 0x80** (a Direct3D colour in memory), in the client's 0x80 half scale: the log glyphs (`moji`) are white, so 0x80 draws white and a channel above 0x80 only brightens the glyphs' grey shading (the renderer's `min(texel x colour x 2, 1)`). `StockUiFontColors.ReadCnf` reads them.
- **Format: settled (2026-10-05).** The maintainer set Say to R 255, G 128 (the default), B 0 in retail and committed `cnf.dat` before and after (integration branch `test/integration-lanes-r2`, commits 9fae025 and 678568d): the only bytes that changed are 0x50 (0x80 → 0x00) and 0x52 (0x80 → 0xFF). So entry 0 at 0x50 is Say, each entry is **B, G, R, then a constant 0x80**, and the editor's sliders are the bytes themselves, 0-255, with 0x80 the default (white). (The first reading of the byte order, from Shout peach / Party cyan / Linkshell green, agrees.)
- **Which entry is which row: confirmed (retail check 2026-10-05).** The 23-entry table is the original rows in page groups, **0-7 chat** (Say, Shout, Tell, Party, Linkshell, Emotes, Messages, NPC conversations), **8-13 For Self** and **14-19 For Others** (each in list order), **20-22 System** (Standard battle, Calls for help, Basic system); the five later entries are the chat types added since: **Yell 0x22C, Unity 0x26C, Linkshell 2 0x278, Assist J 0x2D8, Assist E 0x2DC**. Evidence: the rows with a colour of their own match their entries in the maintainer's screenshots of retail's R/G/B editor (integration folder `image-9.png`..`image-27.png`); the rows whose defaults are not unique were settled by one diff: the maintainer set eight rows to unique colours in retail and committed `cnf.dat` before and after (integration commits 8dbf80e defaults, cea3e5c changed). The changed entries (B, G, R) were exactly the predicted ones: 0x6C `00 00 ff` NPC conversations (red), 0x78 `00 ff 00` Beneficial effects you are granted (green), 0x7C `ff 00 00` Detrimental effects you receive (blue), 0x84 `00 ff ff` Actions you evade (yellow), 0x90 `ff ff 00` Beneficial effects others are granted (cyan), 0x94 `ff 00 ff` Detrimental effects others receive (magenta), 0x22C `60 50 40` Yell (64, 80, 96), 0x2D8 `30 20 10` Assist J (16, 32, 48); Assist E is 0x2DC, the one left. All 28 entries are accounted for. (Two earlier readings from the config row table's order and the round-1 colours were wrong for most rows.)
- One older character (`167dc34` and three siblings) has entry 8 (0x70, HP/MP you recover) changed from `c0 90 60` to `38 c4 2c`.

| Entry | Offset | Default bytes (B G R) | Row | Drawn (R, G, B) | Evidence (2026-10-05) |
|---|---|---|---|---|---|
| 0 | 0x50 | 80 80 80 | Say | white | the Say diff (9fae025 / 678568d) |
| 1 | 0x54 | 40 50 a0 | Shout | (255, 160, 128) | (round 1: peach) |
| 2 | 0x58 | a0 40 a0 | Tell | (255, 128, 255) | (capture: pink) |
| 3 | 0x5C | a0 c0 20 | Party | (64, 255, 255) | (round 1: cyan) |
| 4 | 0x60 | 60 ff 50 | Linkshell | (160, 255, 192) | (round 1: green) |
| 5 | 0x64 | a0 50 60 | Emotes | (192, 160, 255) | purple, matches |
| 6 | 0x68 | d0 d0 a0 | Messages | white | white, matches (R a0, G/B d0) |
| 7 | 0x6C | 80 80 80 | NPC conversations | white | confirmed by the diff |
| 8 | 0x70 | c0 90 60 | HP/MP you recover | (192, 255, 255) light blue | light blue, matches |
| 9 | 0x74 | 40 40 a0 | HP/MP you lose | (255, 128, 128) | red, matches |
| 10 | 0x78 | 80 80 80 | Beneficial effects you are granted | white | confirmed by the diff |
| 11 | 0x7C | 80 80 80 | Detrimental effects you receive | white | confirmed by the diff |
| 12 | 0x80 | 50 80 80 | Effects you resist | (255, 255, 160) pale yellow | pale yellow, matches |
| 13 | 0x84 | 80 80 80 | Actions you evade | white | confirmed by the diff |
| 14 | 0x88 | f0 c0 90 | HP/MP others recover | (255, 255, 255), bluish shading | light blue / white, matches |
| 15 | 0x8C | 80 80 c0 | HP/MP others lose | (255, 255, 255), reddish shading | light red / pink, matches |
| 16 | 0x90 | 80 80 80 | Beneficial effects others are granted | white | confirmed by the diff |
| 17 | 0x94 | 80 80 80 | Detrimental effects others receive | white | confirmed by the diff |
| 18 | 0x98 | 40 80 a0 | Effects others resist | (255, 255, 128) yellow | yellow, matches |
| 19 | 0x9C | 70 70 70 | Actions others evade | (224, 224, 224) | white, matches (all three at 0x70) |
| 20 | 0xA0 | 10 80 80 | Standard battle messages | (255, 255, 32) yellow | (round 1: casting sample yellow) |
| 21 | 0xA4 | d0 60 c0 | Calls for help | (255, 192, 255) | purple / pink, matches |
| 22 | 0xA8 | 50 c0 c0 | Basic system messages | (255, 255, 160) | yellow, matches |
| - | 0x22C | 30 40 a0 | Yell | (255, 128, 96) | confirmed by the diff |
| - | 0x26C | 3f af ff | Unity | (255, 255, 126) pale yellow | pale yellow, matches (R ff, G af, B 3f) |
| - | 0x278 | 00 cc 00 | Linkshell 2 | (0, 255, 0) | bright green, matches (G cc) |
| - | 0x2D8 | ff 50 00 | Assist J | (0, 160, 255) | confirmed by the diff |
| - | 0x2DC | ff 70 00 | Assist E | (0, 224, 255) | by elimination |

### Log page routing ([#49](https://github.com/jimmy58663/GordianXI/issues/49))

- **0x290-0x29C: four u32.** Fresh `00000000, ffffffff, ffffffff, 00000000`; every configured character `00003fdf, ffffe001, ffffc020, 00001ffe`. The pairs (0x290, 0x298) and (0x294, 0x29C) are bitwise complements in every file, so each bit is in exactly one of two sets: read as **Window 1 = (0x290, 0x294), Window 2 = (0x298, 0x29C)**, one bit per Log page message type, in two words. Fresh: Window 2 has all of word A and Window 1 all of word B, which is the default split (battle in Window 2). The configured characters moved word A bits 0-4 and 6-13 to Window 1 and word B bits 1-12 to Window 2: 14 and 14 used bits, matching 14 battle rows and 13 chat rows plus one system row.
- **Bit order: provisional** (`RetailCnf.LogWindow2Types`, used by the import): word A bits 0-5 For Self (config rows 48-53), 6-11 For Others (54-59), 12 standard battle messages, 13 calls for help; word B bit 0 basic system messages, 1-12 the chat rows 36-47 (Say ... NPC conversations), 13 Yell (196, added later). To settle by moving one type in retail's Log page and diffing.

### Other fields (seen, not decoded)

Differences between the fresh files and the configured ones, for the next diffing round:

- 0x00 u32 5 (version?); 0x14 / 0x18 u32 100 / 100 fresh, 3 / 3, 3 / 14 and 34 / 20 on configured characters (the Gameplay volumes?); 0x1C u32 60.
- 0x2C u32 10 fresh, 8 on the older characters.
- 0x290-0x29C: see [Log page routing](#log-page-routing-49) below.
- 0x100-0x1EF: a list of small numbers, the same in every file (key assignments?).
- 0x1F8 u32 1000-2000, different per character.
- 0x244 "stak", 0x24C "nihs" (four-character tags), the same everywhere.

### Not read yet

`mcr*.dat` (7624 bytes) and `mcr.ttl` / `mcr_2.ttl` (macro books and their titles), the 4240-byte files (`b2`, `bs`, `ca`, `cl`, `is`, `mb`, `sb`, `sk`, `ti`, `wr`, `wr_2`..`wr_8`), `aix` / `eix` / `mix` / `moix` / `acq` / `pec.dat`, `gst`, `mcr.sys`, `timestamp.dat`, `ffxiusr.msg`, `AUCSORT.DAT`, `M*.MRK`.
