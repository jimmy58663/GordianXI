# Retail USER files

> What the retail client keeps per character in `FINAL FANTASY XI/USER/<id>/`, as far as GordianXI reads it. GordianXI never writes there (AGENTS.md: no permanent game-folder changes); importing is an explicit action ([#51](https://github.com/jimmy58663/GordianXI/issues/51)). Status and open work: [ROADMAP.md](../../ROADMAP.md) and GitHub Issues.

**Beyond xi-tools / LandSandBoat:** none of the public references documents these files; everything here is our reading of the maintainer's retail install (2026-09 / 10), so each field says how sure it is.

## Folders

- One folder per character, named like the character's content id in lowercase hex without leading zeros (`167dc34`, `c3cbbf`); the LSB characters 1-6 on the maintainer's install have folders `1`..`6`. To confirm with a known character (see [#51](https://github.com/jimmy58663/GordianXI/issues/51)'s comment); GordianXI's import lists every folder with its last-modified time.
- `tig.dat` (24 bytes) sits beside the folders; not decoded.
- Evidence set: 15 `cnf.dat` files, all 744 bytes. `USER/3`, `4`, `5` and `6` are characters created in 2026-09 and never configured: their files are identical (md5 `04b8ae60...`), so they are the retail defaults.

## cnf.dat (744 bytes): client config

Little-endian. Values below are "fresh" (the four never-configured characters) unless noted.

### Font Colors ([#53](https://github.com/jimmy58663/GordianXI/issues/53))

- **0x50: 23 colour entries of 4 bytes, then five more at 0x22C, 0x26C, 0x278, 0x2D8, 0x2DC.** Each entry is **B, G, R, 0x80** (a Direct3D colour in memory), in the client's 0x80 half scale: the log glyphs (`moji`) are white, so 0x80 draws white and a channel above 0x80 only brightens the glyphs' grey shading (the renderer's `min(texel x colour x 2, 1)`). `StockUiFontColors.ReadCnf` reads them.
- **Byte order:** read from the colours. B, G, R makes Shout peach (`40 50 a0` = R 0xA0, G 0x50, B 0x40), Party cyan (`a0 c0 20`) and Linkshell green (`60 ff 50`), as retail draws them; R, G, B would make Party yellow-green and Shout light blue. The tell entry `a0 40 a0` (255, 128, 255) matches the captured tell pink either way (2026-09-27 capture, about (255, 150, 255)).
- **Which entry is which row: provisional.** Read as the page's rows in the config row table's order (ROM/165/74): the ten chat rows 63-72 at entries 0-9, the six For Self rows 73-78 at 10-15, the six For Others rows 79-84 at 16-21, "Player starts casting Spell." 85 at 22; the five later entries as the rows added since, by their colours: 0x22C calls for help (86, orange-red), 0x26C yell (197, yellow), 0x278 skill-up (87, green), 0x2D8 Assist J (204), 0x2DC Assist E (205) (two blues side by side). The order of the Chat list on the page (Yell after Shout, the Assists after Unity) is a guess too.
- **To settle (diff):** in retail, set one row to pure red (R 255, G 0, B 0), copy `cnf.dat` before and after, and note which 3 bytes changed; once per uncertain row (the five later entries, Message, NPC, Linkshell 2). One older character (`167dc34` and three siblings) has entry 8 (0x70, read as "Message: Friend List") changed from `c0 90 60` to `38 c4 2c`.

| Entry | Offset | Fresh bytes (B G R) | Row (provisional) | Drawn (R, G, B) |
|---|---|---|---|---|
| 0 | 0x50 | 80 80 80 | Say | white |
| 1 | 0x54 | 40 50 a0 | Shout | (255, 160, 128) |
| 2 | 0x58 | a0 40 a0 | Tell | (255, 128, 255) |
| 3 | 0x5C | a0 c0 20 | Party | (64, 255, 255) |
| 4 | 0x60 | 60 ff 50 | Linkshell | (160, 255, 192) |
| 5 | 0x64 | a0 50 60 | Linkshell 2 | (192, 160, 255) |
| 6 | 0x68 | d0 d0 a0 | Unity | white, grey shading brightened |
| 7 | 0x6C | 80 80 80 | Emote | white |
| 8 | 0x70 | c0 90 60 | Message (Friend List) | (192, 255, 255) |
| 9 | 0x74 | 40 40 a0 | NPC text | (255, 128, 128) |
| 10-15 | 0x78-0x8C | 80 80 80 / 80 80 80 / 50 80 80 / 80 80 80 / f0 c0 90 / 80 80 c0 | For Self: recover, damage, beneficial, detrimental, no effect, miss | |
| 16-21 | 0x90-0xA4 | 80 80 80 / 80 80 80 / 40 80 a0 / 70 70 70 / 10 80 80 / d0 60 c0 | For Others: same order | |
| 22 | 0xA8 | 50 c0 c0 | starts casting | (255, 255, 160) |
| - | 0x22C | 30 40 a0 | calls for help | (255, 128, 96) |
| - | 0x26C | 3f af ff | yell | (255, 255, 126) |
| - | 0x278 | 00 cc 00 | skill-up | (0, 255, 0) |
| - | 0x2D8 | ff 50 00 | Assist J | (0, 160, 255) |
| - | 0x2DC | ff 70 00 | Assist E | (0, 224, 255) |

### Other fields (seen, not decoded)

Differences between the fresh files and the configured ones, for the next diffing round:

- 0x00 u32 5 (version?); 0x14 / 0x18 u32 100 / 100 fresh, 3 / 3, 3 / 14 and 34 / 20 on configured characters (the Gameplay volumes?); 0x1C u32 60.
- 0x2C u32 10 fresh, 8 on the older characters.
- 0x290-0x29C four u32: fresh `0, ffffffff, ffffffff, 0`; configured `00003fdf, ffffe001, ffffc020, 00001ffe`. The pairs (0x290, 0x298) and (0x294, 0x29C) are bitwise complements in every file, so they look like the Log page's per-window message routing (each message type in exactly one window).
- 0x100-0x1EF: a list of small numbers, the same in every file (key assignments?).
- 0x1F8 u32 1000-2000, different per character.
- 0x244 "stak", 0x24C "nihs" (four-character tags), the same everywhere.

### Not read yet

`mcr*.dat` (7624 bytes) and `mcr.ttl` / `mcr_2.ttl` (macro books and their titles), the 4240-byte files (`b2`, `bs`, `ca`, `cl`, `is`, `mb`, `sb`, `sk`, `ti`, `wr`, `wr_2`..`wr_8`), `aix` / `eix` / `mix` / `moix` / `acq` / `pec.dat`, `gst`, `mcr.sys`, `timestamp.dat`, `ffxiusr.msg`, `AUCSORT.DAT`, `M*.MRK`.
