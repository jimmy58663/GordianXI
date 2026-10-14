# Macros

> The macro system ([#339](https://github.com/jimmy58663/GordianXI/issues/339)) and the macro commands and chat placeholders ([#362](https://github.com/jimmy58663/GordianXI/issues/362)): the books, sets and palettes, where GordianXI keeps them, the retail macro files and their import, running a macro, the keys, the stock macro windows, and what is still to check against retail. Status: built 2026-10-12 on `ui/339-362-macros`; round 1 of the in-game test (2026-10-13) passed the keyboard, waits, storage, import and most windows, and its fixes (below) await round 2.

Sources: the maintainer's retail captures of 2026-10-13 (book list, book options, palette editor, play palette in combat, `/recast` and `/echo` lines; cited as "maintainer retail capture 2026-10-13"), BG-Wiki "Macro" (structure, hotkeys, commands, placeholders; fetched 2026-10-12), the maintainer's two BG-Wiki screenshots of the palette editor and the macro editor, the retail menu DAT (ROM/119/51) and string tables (ROM/165/75-76), and the maintainer's retail `USER` folders (read only; 16 characters, 2026-10-12).

## Model (`Gordian.Core.Macros`)

- **`MacroLibrary`**: 40 books, each with a title and 10 sets; a set holds 20 macros, Ctrl 1..0 then Alt 1..0 (the retail file's order); one book and set is current. Thread-safe; `Changed` fires once per change (a whole import is one change).
- **`Macro`**: a title of up to 8 characters (shown on the palette key) and 6 lines of up to 60 characters. Book titles take up to 15. The limits are the retail files' fields (below); control characters are dropped.
- An unnamed book is shown as `Book01` .. `Book40`, as retail's title files hold for books never renamed (`mcr_2.ttl` of character `e50480`: `Book23`, `Book24` ...); such default names are not stored.

## Storage: client-side per character

Retail keeps macros on the client, per character: `mcr*.dat`, `mcr.ttl`, `mcr_2.ttl` and `mcr.sys` in `USER/<character id>/` (the server-side copy is only an optional backup made at the character select screen). GordianXI does the same with its own file, `macros/<character name>.json` under `GordianStorage.RootDataDirectory` (`MacroStore`), next to `ui_settings/` and `ui_layouts/`, written on every change (through a temporary file), listing only the books, sets and macros that hold something:

```json
{ "Book": 23, "Set": 3, "Books": [ { "Number": 1, "Title": "WAR", "Sets": [ { "Number": 1, "Macros": [
  { "Key": "Ctrl1", "Title": "Provoke", "Lines": [ "/ja \"Provoke\" <t>", "/recast \"Provoke\"" ] } ] } ] } ] }
```

Why not write the retail files: GordianXI never modifies the game folder outside `ProxyStager`'s ephemeral swap (AGENTS.md), and a retail client running beside GordianXI would overwrite or be overwritten. The JSON is readable and diffable. The retail files are imported on request instead.

**Import:** `/importretail` lists the `USER` folders, marking those with macro files "(macros)"; `/importretail <folder> macros` replaces this character's macros with that folder's: every set file, both title files and the current set (`RetailMacroFiles.Import`). A set without a file is cleared, as retail shows it empty. `/importretail <folder>` alone still imports only `cnf.dat` ([retail-user-files.md](retail-user-files.md)).

## Retail macro files (`RetailMacroFiles`)

Read from the maintainer's 16 `USER` folders: all 911 set files are 7624 bytes and decode, and the checksum matches on every file of character `e50480` (`RetailMacroFilesTests.RetailInstall_SetFilesDecodeWhenPresent`, which also imports that folder). **Beyond xi-tools / LandSandBoat:** neither documents these files.

- **Header (all four kinds), 24 bytes:** u32 version (1); u32 whose value varies from file to file (`0x00000200`, `0x0029E664` ...; ignored, written 0); the MD5 of the rest of the file (bytes 8-23; checked against every file).
- **`mcr.dat`, `mcr1.dat` .. `mcr399.dat`** (7624 bytes): one set each, file number = book x 10 + set, 0-based (`mcr.dat` is book 1 set 1, `mcr222.dat` book 23 set 3). A set never saved has no file. After the header, 20 records of 380 bytes, Ctrl 1..0 then Alt 1..0:
  - +0 four zero bytes (zero in all 18,220 records of the 911 files);
  - +4 six lines of 61 bytes: up to 60 characters and a NUL (the 61st byte is always 0; the longest line seen is 51);
  - +370 the title, 10 bytes: up to 8 characters, NUL-terminated; bytes after the NUL are leftovers of an older title ("Ichi\0rk").
  - Text is Shift-JIS. Auto-translate phrases appear as 0xFD, 4 bytes, 0xFD (26 in the maintainer's files); GordianXI's lines are plain ASCII, so the import leaves them out and says how many.
- **`mcr.ttl`** (books 1-20) and **`mcr_2.ttl`** (21-40), 344 bytes: 20 titles of 16 bytes, NUL-terminated (retail fills unnamed books with `BookNN`).
- **`mcr.sys`** (28 bytes): one u32, the current set as book x 10 + set. Confirmed by the in-game test (round 1, A2: the imported palette opened on the set retail had). First inferred from the values (0, 40, 60, 106, 122, 142, 180, 222, 320: all in range, mostly set 1, some pointing at sets with no file); to confirm, compare the palette retail opens with the value.

## Running a macro (`MacroRunner`)

Each line goes through the chat's own submit (`StockUiChat.SubmitAsync` with the current chat mode), so a macro line acts exactly as the same line typed: commands, chat on the default channel, the echo of your own chat, the placeholders below.

- `/wait n` on a line of its own pauses n seconds; `<wait n>` (also `<waitn>`) in a line runs the line without it and then pauses (BG-Wiki). Decimals are accepted; pauses are capped at 60 s (**PROVISIONAL**, retail's limit is not documented); `/wait` alone is 1 s.
- A line that opens the sub-target cursor (`<st>`, `<stpc>` ...) holds the next line until the target is chosen or the cursor cancelled (BG-Wiki: "the line after the subtarget runs once the player confirms").
- Starting a macro while another runs stops the first (passed the maintainer's in-game comparison, round 1, A8).
- Retail runs macro lines on a tick of about 0.42 s, which snaps waits to that tick (BG-Wiki, third-party timing: `<wait1>` lands at 0.83-1.25 s). GordianXI runs lines back to back and waits exactly the time given; not modelled.

## Keys (`MacroController`, fed by `PlayerLocomotionController` each tick)

- **Keyboard** (BG-Wiki hotkeys): holding Ctrl or Alt shows that palette; Ctrl/Alt + 1..0 run a macro (`InputAction.MacroCtrl1..10` / `MacroAlt1..10`, the existing bindings); Ctrl/Alt + Up / Down cycle the set (wrapping); Ctrl/Alt + Shift + 1..0 change to set 1..10; Ctrl/Alt + Left / Right pick a macro on the palette (highlighted orange) and Ctrl/Alt + Enter runs it. With Ctrl or Alt held, the digits, arrows and Enter no longer fall back to their unmodified bindings (`InputState.IsMacroPaletteKey`), so Ctrl+Up does not also turn the camera.
- **Gamepad** (the maintainer's retail check, 2026-10-13): holding LT / RT (`MacroPaletteCtrl` / `MacroPaletteAlt`) shows the palette and the other buttons drive nothing else; the d-pad moves as the arrow keys do (left / right pick a macro, up / down change the set) and the Confirm button (A by default, whatever Confirm is bound to) runs the picked macro. No other button touches a macro. (Round 1 mapped ten buttons to the ten keys; retail does not.)
- Nothing runs, and no palette shows, while a menu is open, the chat line is open or a log window, status icon or the full-screen log is selected; the target command menu alone does not count, since it stays up while engaged (Attack re-opens it as the engaged list). One rule serves the input tick and the HUD (`MacroController.BlockReason`), and a blocked palette logs why once per hold (`[MACRO] Ctrl palette held but blocked: ...`); every macro that runs logs `[MACRO] Running Ctrl1 "Title" (book n, set n)`.
- *Combat (rounds 1-2, A16 / A22):* round 1 blocked the palettes behind any open menu, so the engaged command menu stopped them; round 2 let the input tick through but the HUD still drew no palette while any menu was open, so in combat nothing showed. Both now share `BlockReason`. Also fixed: a status icon selection (keypad + / Y twice) whose icons all wore off stayed pending and came back with the next icon, often one gained in combat, silently taking the arrows / d-pad and blocking the palettes. The engaged path (target, command menu, Attack, engaged, locked on; keyboard and gamepad) is covered end to end by `MacroCombatTests` and `MacroSessionCombatTests`.

## Windows (stock UI, Tier 2)

Found in the English menu DAT by name and read with `GORDIAN_UI_DUMP_MENUS=mcr*,macro` (help ids and titles from ROM/165/75-76). `StockUiMacroMenus` (Core) names them and composes the palette editor; `StockUiMenuController.Macros.cs` drives them; `StockUiMacroWindow` (App) draws their client content.

| Window | Frame | Buttons | Use |
|---|---|---|---|
| `mcrmenu` | 384,48 112x42 TopRight, title 80 "MacroPalette" | 1 Edit Macro (help 829), 2 Edit Equip. Set (830) | the main menu's Macros (page 2) |
| `mcrselec` | 384,48 112x330 TopRight | 1-20 book rows (`keytops3` #86 capsules, client text), 21 / 22 page arrows at x -16 / 113 | the 40 books, 20 to a page; left / right turn the page; the current book has a red underline (maintainer retail capture 2026-10-13) |
| `mcrselop` | 0,0 112x90 | 1 Confirm (661), 2 Change Name (662), 3 Copy (663), 4 Paste (664), 5 Delete (665) | a book's options, left of the list beside the book's row (maintainer retail capture 2026-10-13); Confirm makes the book current |
| `mcr1edlo` + `mcr2edlo` | 16,48 862x42 each, title 75 "Edit Macros" | 10 keys each, 80x32 (`keytops3` #474-#493 key art) | the set's Ctrl and Alt rows (Compact: `mcr1edit` / `mcr2edit`, 480x42, keys 40x32) |
| `macro` | 16,48 366x248, title 75 | 1 Prev, 2 Next, 3 title field, 4-9 lines | one macro (`frames` #61 draws the key box 18..94 x 30..58, the title box 110..234, the line boxes 18..358 x 66+30n) |
| `mcrbedit` | 384,134 112x77 | 1 Copy (659), 2 Paste (318), 3 Delete (317) under "+ : Button Edit" | a palette key's options, shown at the top right whenever the palette editor has the cursor (maintainer retail capture 2026-10-13) |
| `mcrledit` | 384,48 112x77 | 1 Copy Line (660), 2 Paste Line (320), 3 Delete Line (319) under "+ : Line Edit" | a line's options, shown at the top right whenever the macro editor has the cursor |
| `mcr1long` / `mcr2long` | 16,48 862x42, title 80 | 10 keys | the palette in play, Full size |
| `mcr1pall` / `mcr2pall` | 16,48 480x42, title 80 | 10 keys | the palette in play, Compact |

`mcresmn`, `mcresed`, `mcres20` (title 172 "Equipment Sets") and `mcrmogc` (title 169 "Storage Options") are the equipment set and storage windows, not part of the macros.

- **Flow:** Macros, Edit Macro, a book, Confirm makes the book the current one (keeping the set number, as `/macro book` does) and opens the palette editor on the current set. Up / Down move between the Ctrl and Alt rows; up from the Ctrl row goes to the previous set's Alt row and down from the Alt row to the next set's Ctrl row (GordianXI's way to reach the ten sets; retail's is not captured). Confirm on a key opens the macro editor; Prev / Next walk the set's twenty macros (Ctrl 1 .. Alt 0, wrapping), the palette's cursor following. Confirm on the title or a line types in it: the field takes the keyboard as the chat line does (`StockUiTextField`; the viewport feeds it keys and text), Enter, Escape and the gamepad's Confirm / Cancel all keep the text (retail keeps a line on Escape: the maintainer's check, 2026-10-13), Up / Down keep it and go on typing in the field above / below; Ctrl+V pastes. Button Edit and Line Edit are always shown beside the palette editor and the editor; + (keypad +, gamepad Y, as their "+ :" headers say) puts the cursor on them and back (confirmed as the wanted way by the maintainer, round 2, A25). Copy keeps the macro or line in a clipboard, Paste writes it, Delete clears it. Change Name types the book's name in its row; Paste and Delete on a book ask yes / no first.
- **Layout (maintainer retail capture 2026-10-13, 1:1):** the editor shows both rows stacked (the Ctrl row at the authored 16,48 and the Alt row 43 px lower, `StockUiMacroMenus.AltRowOffset`: the 42-px frames 1 px apart, measured to about a pixel), composed into one window so the cursor, mouse and Cancel work as on any menu. The help bar at the top (`titlewin` + `helpwind`) shows "Edit Macros" and "Macro Set Book01 (#10)" as the screenshot and the maintainer retail capture 2026-10-13 do; over a window whose entry has a help line (the book options, Button / Line Edit) that line.
- **In play:** while a palette is held, only that palette's row is drawn, at the authored 16,48 (both rows share the spot; the maintainer's note). Misc. 2 "Macro Palette Position" shifts it right by 128 px (`StockUiMacroWindow.CombatShift`) while engaged (Dynamic) or always (Static) (help 688 "Shift macro palette to the right during combat.", 689 "Lock macro palette to the right at all times."): in combat retail draws your own HP / MP / TP bars at its left and the palette sits just right of them (maintainer retail capture 2026-10-13, 1:1; the offset measured to about 4 px on a cropped capture). GordianXI does not draw those bars yet ([#391](https://github.com/jimmy58663/GordianXI/issues/391)). "Macro Palette Size" picks Full or Compact (help 687). Each key shows its macro's title in `fontshp` at 7/8 from its top left; the key picked with Ctrl/Alt + Left / Right is tinted as a selected menu entry. The help bar shows "MacroPalette" and "Macro Set Book09 (#1)" in play too (maintainer retail capture 2026-10-13).
- Drawn checks: `StockUiMacroWindowRenderTests` (D3D11; `GORDIAN_UI_DUMP` writes `gpu_macro_palette.png`, `gpu_macro_palette_editor.png`, `gpu_macro_editor.png`, `gpu_macro_books.png`).

## Commands (#362)

Routed by `ChatCommandRouter` and run in `PlayerActionService.Macros.cs`; they work typed and in macros.

| Command | GordianXI |
|---|---|
| `/macro book n`, `/macro set n` | change the current book (1-40, keeping the set) or set (1-10) |
| `/wait [n]` | a macro's pause; typed, nothing |
| `/recast "name"` | a spell's or job ability's time to ready, as retail prints it: `[Provoke] 0:00` (maintainer retail capture 2026-10-13), in the echo colour |
| `/targetnpc`, `/targetbnpc` | target the closest NPC (or door) / the closest living monster among what the targeting keys could pick, within 50 yalms |
| `/lastsynth` | the last synthesis again with the same items, found anew in the inventory (the crystal and each ingredient from the first slots holding enough) |
| `/pcmd add name`, `/invite name` | a player in this zone by their entity; anyone else by character id: C2S 0x06E carries no name, only `UniqueNo` and, in the same zone only, `ActIndex` (XiPackets `world/client/0x006E`); with ActIndex 0 LandSandBoat finds the character by id in its zone or forwards the invite through the message server (`packets/c2s/0x06e_group_solicit_req.cpp`). The id comes from a name search on the search server (`/sea all <name>`); "<name> is not online." when it finds no one. `/acmd add` does the same with kind 5 |
| `/pcmd leader name` | passes party leadership: C2S 0x077 kind 0, change kind 0 SetPartyLeader (LandSandBoat `0x077_group_change2.h`). The rest of `/pcmd` (add, leave, breakup, kick, accept, decline) was there already; LandSandBoat's other change kinds (quartermaster, lottery, level sync) have no `/pcmd` word we know of |
| `/acmd add\|leave\|breakup\|kick\|leader [name]` | alliance commands: C2S 0x06E / 0x06F / 0x070 / 0x071 with kind 5, and 0x077 kind 5 change kind 1 (SetAllianceLeader); kinds from LandSandBoat `packets/c2s/0x071_group_strike.h`, `0x077_group_change2.h` |
| `/equipset` | not here: equipment sets are [#92](https://github.com/jimmy58663/GordianXI/issues/92) |

## Chat placeholders (#362, `ChatTextTokens`)

`/echo` lines (and `/recast` replies) are drawn pale yellow, the timestamps' colour: glyph bodies FFFFE0-FFFFF0 with yellower edges (FFFF9A-FFFFC5) in the maintainer retail capture 2026-10-13 of `/echo 9999/9999` and `[Provoke] 0:00`, matched by rendering the same text through the log font (`ChatLogChannel.Echo`, `StockUiFontColors.FixedColor`). No Font Colors row (`cnf.dat`) has this colour, so it is fixed. (Round 2 sampled the lavender of the help bar behind the text by mistake.)

Chat text (say, party, tell ..., `/echo`) has its placeholders filled before it is sent and echoed, as retail does: `<hp>` / `<mp>` "current/max", `<hpp>` / `<mpp>` "n%", `<tp>` the number, `<job>` "WAR75/NIN37", `<mjob>` "WAR75", `<sjob>` "NIN37", `<pethpp>` / `<petmpp>` "n%" and `<pettp>` from the pet (S2C 0x068), `<recast=Name>` "m:ss", and the target placeholders (`<t>`, `<me>`, `<bt>`, `<p0>`-`<p5>`, `<a10>`-`<a25>`, `<pet>`, `<r>`, `<ht>`, `<scan>`, `<lastst>`) as the entity's name. `<wait n>` is removed. A placeholder nothing can fill (no pet, nothing targeted, an unknown word) and the sub-target ones (`<st>` ...) stay as typed. **PROVISIONAL:** every output format above is ours (BG-Wiki shows usage, not output); to check in retail with `/echo <hp> <hpp> <tp> <job> <mjob> <sjob> <recast=Provoke>`.

Not done: `<pos>` (needs the map grid), `<call>` / `<ncall>` / `<scall>` (call sounds on the wire), `<fthpp>` (adventuring fellows): [#387](https://github.com/jimmy58663/GordianXI/issues/387); auto-translate phrases in macros and chat: [#388](https://github.com/jimmy58663/GordianXI/issues/388).

## Working notes

- Read the macro menus: `GORDIAN_UI_DUMP=<dir> GORDIAN_UI_DUMP_MENUS=mcr*,macro dotnet test tests/Gordian.Core.Tests --filter DumpMenuComposites`.
- Read a retail set file without the client: 24-byte header, then 20 x 380 bytes; lines at +4 + 61 n, the title at +370.
