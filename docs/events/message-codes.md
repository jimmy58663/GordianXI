# Dialog message codes

The control codes inside the strings of a zone's dialog table (English file 6420 + zone, Japanese 6120 + zone; zones 256-299 at 85591 / 85291 + (zone - 256): `ZoneDialogTable.GetFileId`). Event scripts print these strings (0x1D, 0x2B, 0x48 / 0x49, 0xB0, queries 0x24 / 0xD4) and so do the zone message packets S2C 0x036 and 0x02A. The table's container (the 0x10 flag, XOR 0x80, the offset table) is in [vm.md](vm.md#event-files); how a printed line is shown (log, event message mode, query window) is in [vm.md](vm.md#event-message-mode-and-late-arrivals) and [ui/stock-ui.md](../ui/stock-ui.md#dialog-text-chunk-6).

Code: `EventMessageDecoder` (Core `Resources/Tables`) turns a string into segments (`EventMessageSegmentKind`); `EventMessageFormatter` (Core `Events`) turns the segments into lines with an `IEventMessageContext`: `WorkZoneContext` for event lines (parameters from `EventWorkZone.GetMessageParameter`), `SimpleMessageContext` for 0x036 / 0x02A (the packet's four numbers). Names of a 0x01 tag go through `EventDialogController.NameResolver` (set in `App.axaml.cs`).

**Status words:** *handled* = substituted or acted on; *placeholder* = printed as `<` + the kind byte as a character + the id + `>`; *shown raw* = the code's own text stays in the line; *dropped* = decoded and skipped; *misread* = the decoder takes the wrong length, so the bytes after the code are read wrongly.

**Corpus counts** come from a one-off scan of the retail English tables on 2026-10-01: 294 zones, 2,744,020 messages (the other zones have no table). Counts are occurrences; the tables repeat shared blocks in every zone (the objective lines of 0x7F 0x92 and 0x01 kind 0x29 above all), so those are over-weighted.

## Single-byte codes

| bytes | meaning | count | `EventMessageDecoder` | GordianXI | source |
|---|---|---|---|---|---|
| `00` | end of the string (bytes after it are padding or a second sub-string) | - | ends the message | handled | xi-tools docs/dialog/format.md |
| `01 len kind ...` | a named thing from a message parameter (item, key item, zone...) | see [0x01 tags](#0x01-tags) | `Name` | see below | xi-tinkerer (format only), xi-tools docs/dialog/format.md |
| `02 x:u16 03 y:u16` | screen position of an event message mode line | 68, always with `03` three bytes on | `Position` (6 bytes) | handled in the event message mode (`EventScreenText` at x, y); ignored in the log | xi-tools docs/dialog/format.md (`set_x` / `set_y`); retail recording 2026-09-30 |
| `02` not followed by `03` | `set_x` alone (3 bytes per xi-tools) | 1, probably another code's argument | `Unknown`, skips 5 bytes | misread ([#74](https://github.com/jimmy58663/GordianXI/issues/74)) | xi-tools docs/dialog/format.md |
| `05 n` | not known; once per zone table, "<player>'s `05 00` has..." (a pet's name?) | 294 | `Unknown`, 2 bytes | dropped | corpus |
| `07` | line break | 4,949,614 | `LineBreak` | handled (new log line / screen line) | xi-tools |
| `08` | the player's name | 12,292 | `PlayerName` | handled | xi-tools |
| `09` | the speaking NPC's name | 467 | `NpcName` | handled (the speaker of 0x1D / 0x2B, or the 0x036 / 0x02A entity) | xi-tools |
| `0A n` | number parameter `n` | 2,276,195 | `Number` | handled | xi-tools |
| `0B` | the choice list of a query starts | 30,064 | `ChoicesStart` | handled (`FormatQuery` options) | xi-tools |
| `0C n [a/b/...]` | the alternative number parameter `n` picks | 858,330 | `Selector` | handled (index clamped); alternatives made of `EF` icons (the weather list) are decoded as text | xi-tools |
| `0E n` | not known | 37 | `Unknown`, 2 bytes | dropped | corpus |
| `11 n` | not known; "Learned Trust: `11 01`!" (a trust name from parameter `n`?) | 294 | `Unknown`, 2 bytes | dropped | corpus |
| `18 n` | not known ("...by `18 01`.") | 2,953 | `Unknown`, 2 bytes | dropped | corpus |
| `19 n` | an entity's name (party member `n`) | 238 | `EntityName` | dropped: both contexts return no name | decoder notes |
| `1C n` | a string parameter, read here as the packet's name string ("`1C 00` caught a monster!", the fishing lines of 0x027 / 0x043) | 4,955 | `Unknown`, 2 bytes | dropped | corpus; xi-tools docs/events/chat-messages.md for the packets |
| `1E n` | not known; stands before headings ("`1E 05`Visitant Light Intensity"), probably a colour | 4,661 | `Unknown`, 2 bytes | dropped | corpus |
| `1F c` | text colour `c` | 35,896 | `Colour` | dropped (lines are drawn in one colour) | decoder notes |
| other below `20` | one argument byte | - | `Unknown`, 2 bytes | dropped | decoder notes |
| `7F xx ...` | extended codes | see [0x7F codes](#0x7f-codes) | - | - | - |
| `EF n` | inline icon; `EF 1F`-`EF 26` stand for the eight elements in the weather list and an elemental balance line | 19,068 | `Icon` | dropped (not drawn) | corpus; xi-tools calls the range gauge-bar glyphs |
| `FD .. FD` | auto-translate phrase (6 bytes) | 0 | `Unknown`, 6 bytes | dropped | decoder notes |
| `20`-`FF` else | Shift-JIS text (`cp932`; a lead byte takes its trail byte) | - | `Text` | handled; the 85 FFXI glyphs that differ from `cp932` are not mapped | xi-tools docs/dialog/format.md |

**Differs from xi-tools:** `EF 1F`-`EF 26` are read here as element icons (they are the eight entries of the `0C` weather selector), not gauge glyphs.

## 0x7F codes

Lengths: the decoder's list (XiEvents OpCodes/0x0024, the query handler's skip lengths) makes `0x34`-`0x36`, `0x80`, `0x84`, `0x86`, `0x8C`, `0x92` three bytes, `0x38` four, all others two. The corpus shows more three-byte codes (the "misread" rows): after each of them the byte that follows is an argument, often `00`, which the decoder then reads as the end of the string.

| bytes | meaning | count | `EventMessageDecoder` | GordianXI | source |
|---|---|---|---|---|---|
| `7F 31` | prompt: wait for Confirm (the arrow) | 2,668,717 | `Prompt`; also eats a following `00` and reads on | handled: `PrintMessage` holds the line (`PromptOpenSeconds` = infinite) | xi-tools docs/dialog/format.md; retail recordings 2026-09-28 |
| `7F 32` / `7F 33` / `7F 37` | other manual prompts per xi-tools | 0 / 3 (Japanese) / 0 | `Unknown`, 2 bytes | dropped: no wait | xi-tools; [#74](https://github.com/jimmy58663/GordianXI/issues/74) |
| `7F 34 n` | the line closes by itself after `n` seconds | 11,400 (`n` = 1: 10,776; 0: 291; 2-12: 333; 180: 1) | `AutoClose` | handled: `PrintMessage` returns `n` (the Southern San d'Oria crawl: 9 and 5, retail 9.2-9.3 s and 5.1 s) | xi-tools; retail recording 2026-09-30 |
| `7F 35 n` / `7F 36 n` | timed prompts per xi-tools (`n` 1-8 in the tables) | 1,251 / 673 | `Unknown`, 3 bytes | dropped: the line waits for its `7F 31` instead | xi-tools; [#74](https://github.com/jimmy58663/GordianXI/issues/74) |
| `7F 38 a b` | two parameter bytes (`7F 38 C8 00` = 200) | 84 | `Unknown`, 4 bytes | dropped | xi-tools |
| `7F 80 n` | stands before an item or key item tag ("Obtained: `7F 80 01` <item>."); meaning not known (an article or count of parameter `n`?) | 31,706 | `Unknown`, 3 bytes | dropped | corpus |
| `7F 81 n` | a string parameter ("LS0: `7F 81`...") | 18 | 2 bytes | misread | corpus |
| `7F 84 n` / `7F 86 n` / `7F 8C n` | three bytes per XiEvents | 0 | `Unknown`, 3 bytes | dropped | XiEvents OpCodes/0x0024 |
| `7F 85 [a/b]` | one of two words by the player's sex ("[his/her]", "[sir/madam]") | 4,951 | `GenderSelector` | handled (look's race byte: 2, 4, 6, 7 female); left as `[a/b]` when the look is not known | retail recording 2026-09-30 |
| `7F 92 n [a/b]` | singular or plural by number parameter `n` ("[monster/monsters]", "[/s]", "[time/times]") | 1,089,548 | `Unknown`, 3 bytes | shown raw: the bracket text stays in the line | corpus |
| `7F 93` | an entity's name ("I'm `7F 93`, your friendly neighborhood...") | 24 | `EntityName` | dropped: both contexts return no name | decoder notes |
| `7F 94 n` | a number from parameter `n` (the elemental balance figures after `EF` icons, "(Quest `7F 94 1F`)") | 4,754 | 2 bytes | misread | corpus |
| `7F 95 n` / `7F 96 n` | three bytes, meaning not known | 310 / 15 | 2 bytes | misread | corpus |
| `7F A0 n`-`7F AC n` | date and time fields of parameter `n` ("until `A1`/`A2`/`A0` at `A3`:`A9` (JST)") | A0 432, A1 689, A2 689, A3 695, A9 691, AA 123, AB 547, AC 123, others under 5 | 2 bytes | misread (the line ends at the `00` argument) | corpus |
| `7F B4 n` / `7F B5 n` | three bytes, meaning not known | 1 / 15 | 2 bytes | misread | corpus |
| `7F 99` | not known | 7 | 2 bytes | not checked | corpus |
| `7F FB` / `7F FC` | entity name wrap markers | - | skipped | dropped | decoder notes |

Fixes tracked in [#202](https://github.com/jimmy58663/GordianXI/issues/202). **Beyond xi-tools and XiEvents:** the three-byte codes `7F 81`, `7F 94`-`7F 96`, `7F A0`-`7F AC`, `7F B4` / `7F B5`, and the reading of `7F 92` as a plural selector and `7F A0`-`7F AC` as date fields, all from the corpus contexts above (not checked against retail screens).

## 0x01 tags

Layout: `01 len kind`, then one sub-block per value: a byte whose value XOR 0x80 is the value's byte count (always 2 in the tables), the value bytes each XOR 0x80 (little-endian), and a closing byte. `len` counts the bytes after itself: `01 05 23 82 85 80 80` is seven bytes, item name from parameter 5. Each value is a message parameter index; `EventMessageFormatter` resolves the first one (`GetNumber(values[0])`) and asks `NameResolver` for the name.

**Differs from xi-tools:** docs/dialog/format.md counts `len` as the whole tag including the `01`; its own example (`01 05 25 82 81 80 80`) and every tag in the corpus (lengths 5 and 9 on 7- and 11-byte tags) count it from the kind byte.

| kind | meaning | count | GordianXI | source |
|---|---|---|---|---|
| `01` (`01 01 01`, no value) | stands before an item tag ("rewarded `01 01 01` <item>"); presumably the article / count slot | 16,869 | placeholder (a control character and 0) | corpus; xi-tools mentions an article slot |
| `03` / `04` | a number (`04` the count a plural item tag goes by) | 510 / 0 | placeholder | xi-tools |
| `12` | a name ("<`12`>, a hero's story!") | 727 | placeholder | corpus |
| `17` / `18` | weather names in the forecast lines ("will be <`17`> with occasional <`18`>") | 2,499 / 4,943 | placeholder | corpus |
| `20` | not known (mostly Japanese lines) | 2,308 | placeholder | corpus |
| `23` `#` | item name | 93,799 | handled: `TryGetItem` | xi-tools; LandSandBoat ITEM_OBTAINED |
| `24` `$`, `25` `%`, `27`, `28` | item name forms (after "a", plural, after "the"...) | 16,867 / 4,957 / 1 / 1 | placeholder | xi-tools |
| `29` `)` | two values (most often parameters 0 and 5), in the objective lines; xi-tools reads it as a counted item | 151,428 | placeholder (first value only) | xi-tools; corpus |
| `30`, `35`, `40` | not known | 928 / 1,557 / 776 | placeholder | corpus |
| `33` `3` | key item name | 16,341 | handled: `TryGetKeyItemName` (#89) | xi-tools |
| `36` `6` | key item name ("obtained <`36`>!") | 4,853 | placeholder | xi-tools (seen there in menu rows) |
| `37` `7` | zone name | 323 | placeholder | xi-tools |
| `38` `8` | zone name | 8,155 | handled: d_msg zone names (ROM/165/84) | xi-tools |
| `84` | a person's name ("NAME: <`84`>", "Lately, <`84`> has been...") | 4,107 | placeholder | corpus |
| `85` | followed by a "[male/female]" pair in Japanese lines: a selector by that entity's sex? | 37 | placeholder, brackets shown raw | corpus |
| `81`, `82`, `83`, `86`, `87` | not known (`81` / `83` 8 bytes long, `82` 9) | 97 / 23 / 725 / 360 / 21 | placeholder | corpus |
| others (`02`, `10`, `13`-`16`, `60`, `B3`) | single uses | under 10 each | placeholder | corpus |

## Where the codes are acted on

| path | codes it uses | GordianXI | source |
|---|---|---|---|
| event line in the log (0x1D / 0x2B / 0x48 / 0x49 / 0xB0) | text, names, numbers, selectors, prompt / timer | `EventDialogController.PrintMessage` -> `PrintLines` (speaker prefixed) | [vm.md](vm.md) |
| event line in event message mode (0x67 on) | as above plus `02` / `03` position | `EventScreenText`, `StockUiHud.DrawEventText` | [vm.md](vm.md#event-message-mode-and-late-arrivals) |
| query (0x24 / 0xD4) | `0B` splits the question from the options | `FormatQuery`, `StockUiMenuController.OpenQuery` | [ui/stock-ui.md](../ui/stock-ui.md#dialog-text-chunk-6) |
| zone message S2C 0x036 / 0x02A | text, names, numbers (packet `Num[0..3]`); bit 15 of the message id hides the name | `EventDialogController.OnDialogMessage` | xi-tools docs/events/chat-messages.md |
| zone message S2C 0x027 / 0x043 | the fishing lines (`Num1`, `String1`; `1C n`?) | not handled | xi-tools docs/events/chat-messages.md |

**Differs from xi-tools:** chat-messages.md says the chat log cuts a line at the first `0x7F`. GordianXI does not cut zone messages there; the prompt is dropped and the rest is formatted. Not checked against retail.

## Open gaps ([#74](https://github.com/jimmy58663/GordianXI/issues/74))

| gap | state in the code | to do |
|---|---|---|
| `0x02` / `0x03` positioning | done for the `02 x 03 y` pair (6 bytes, `Position`, used by the event message mode); a lone `02` still skips 5 bytes | make a lone `02` and `03` 3 bytes each; decide what the log does with a position |
| timed and alternate prompts | `7F 31` waits, `7F 34 n` closes after `n` s (`AutoCloseSeconds`); `7F 35` / `7F 36` / `7F 38` and `7F 32` / `33` / `37` are dropped | find what `35` / `36` / `38` do (advance after `n` s or on a key) and the arrow of each; what `7F 34 00` (291 lines) means: today the line closes at once |
| remaining `0x01` kinds | `23`, `33`, `38` resolved | `24`-`2A` item forms, `36` key item, `37` zone, `03` / `04` numbers, `17` / `18` weather, `84` names; check against retail captures |
| after `7F 31` | the decoder eats a following `00` and reads on; retail ends the string | harmless in over 99.9% of lines (the rest is `07` padding) |

Found by the corpus scan and not in #74 (candidates for new issues): `7F 92` plural selectors shown raw (over a million uses, mostly objective lines); the three-byte `7F 81` / `94`-`96` / `A0`-`AC` / `B4` / `B5` codes misread, cutting date and number lines short; `19` and `7F 93` entity names always empty; `1C` string parameters and the S2C 0x027 / 0x043 messages.

## Chunk 6 summary

The decoder summary as first written for chunk 6 (2026-09-28), moved from [ui/stock-ui.md](../ui/stock-ui.md#dialog-text-chunk-6); the tables above supersede it where they differ.

0x07 line break, 0x0B starts the choice list, 0x0A n number parameter n, 0x0C n `[a/b/c]` picks by parameter n, 0x08/0x09 player/NPC name, 0x19 n / 0x7F 0x93 another entity, 0x1F c colour, 0x01 len type ... a named thing (type '#' item, '3' key item, '8' zone; sub-blocks of (length ^ 0x80) value bytes ^ 0x80 and a closing byte; the first value is the index of the parameter holding the id), 0x7F 0x31 0x00 the prompt (the event waits for Confirm), other 0x7F codes two bytes (0x34-36/80/84/86/8C/92 three, 0x38 four, per the retail query handler), 0xEF n icon, 0xFD...0xFD auto-translate, the rest Shift-JIS. Sub-block values are read from the number parameters (a packet's num[], an event's zone work values from slot 2); which array an event's text really reads is unconfirmed.
