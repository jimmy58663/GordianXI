# Dialog message codes

The control codes inside the strings of a zone's dialog table (English file 6420 + zone, Japanese 6120 + zone; zones 256-299 at 85591 / 85291 + (zone - 256): `ZoneDialogTable.GetFileId`). Event scripts print these strings (0x1D, 0x2B, 0x48 / 0x49, 0xB0, queries 0x24 / 0xD4) and so do the zone message packets S2C 0x036 and 0x02A. The table's container (the 0x10 flag, XOR 0x80, the offset table) is in [vm.md](vm.md#event-files); how a printed line is shown (log, event message mode, query window) is in [vm.md](vm.md#event-message-mode-and-late-arrivals) and [ui/stock-ui.md](../ui/stock-ui.md#dialog-text-chunk-6).

Code: `EventMessageDecoder` (Core `Resources/Tables`) turns a string into segments (`EventMessageSegmentKind`); `EventMessageFormatter` (Core `Events`) turns the segments into lines with an `IEventMessageContext`: `WorkZoneContext` for event lines (parameters from `EventWorkZone.GetMessageParameter`, strings from the event's S2C 0x033), `SimpleMessageContext` for 0x036 / 0x02A (the packet's four numbers). Names of a 0x01 tag go through `EventDialogController.NameResolver` (set in `App.axaml.cs` to `EventMessageNames.Resolve`); party and entity names (`19 n`, `18 n`) through `EventDialogController.PartyMemberName` / `EntityNameById`.

**Code lengths:** the decoder follows xi-tools' event Shift-JIS table (`src/xi/dialog/_sjis_data.py`, generated from Shining Fantasia: the parameter count of each code), the XiEvents query handler (OpCodes/0x0024) and the corpus. The one disagreement is `7F 8C`: xi-tools gives it no parameter, the query handler three bytes; the decoder keeps three (no line uses it).

**Selectors** (`0C n`, `7F 92 n`, `7F 85`) apply to the next `[a/b/...]` list on the same line, which need not follow the code at once: "`0A 02` `7F 92 02`credit[/s]", "`7F 85` [Lord/Lady]" (a space before the list, kept as text), "`0C 00``0C 00`[...]" (the later code wins). No selector in the English tables reaches past a line break, so a line break (or `0B`) drops a waiting selector; a list without a selector before it stays text ("Participation status altered to [Eff, yeah/Heck, no].", zones 26 / 51 / 247).

**Status words:** *handled* = substituted or acted on; *placeholder* = printed as `<` + the kind byte as a character + the id + `>` (`<` + hex kind + `:` + id + `>` when the kind is not printable); *shown raw* = the code's own text stays in the line; *dropped* = decoded and skipped; *misread* = the decoder takes the wrong length, so the bytes after the code are read wrongly.

**Corpus counts** come from a one-off scan of the retail English tables on 2026-10-01: 294 zones, 2,744,020 messages (the other zones have no table). Counts are occurrences; the tables repeat shared blocks in every zone (the objective lines of 0x7F 0x92 and 0x01 kind 0x29 above all), so those are over-weighted.

## Single-byte codes

| bytes | meaning | count | `EventMessageDecoder` | GordianXI | source |
|---|---|---|---|---|---|
| `00` | end of the string (bytes after it are padding or a second sub-string) | - | ends the message | handled | xi-tools docs/dialog/format.md |
| `01 len kind ...` | a named thing from a message parameter (item, key item, zone...) | see [0x01 tags](#0x01-tags) | `Name` | see below | xi-tinkerer (format only), xi-tools docs/dialog/format.md |
| `02 x:u16 03 y:u16` | screen position of an event message mode line | 68, always with `03` three bytes on | `Position` (6 bytes) | handled in the event message mode (`EventScreenText` at x, y); ignored in the log | xi-tools docs/dialog/format.md (`set_x` / `set_y`); retail recording 2026-09-30 |
| `02 x:u16` / `03 y:u16` alone | `set_x` / `set_y` alone | 0 (the one lone `02` the first scan found was another code's argument, read with a wrong length) | `Unknown`, 3 bytes each | dropped (the event message mode needs both) | xi-tools docs/dialog/format.md and its code table |
| `05 n` | not known; once per zone table, "<player>'s `05 00` has..." (xi-tools: SKILL_TEXT) | 294 | `Unknown`, 2 bytes | dropped | corpus; xi-tools code table |
| `07` | line break | 4,949,614 | `LineBreak` | handled (new log line / screen line) | xi-tools |
| `08` | the player's name | 12,292 | `PlayerName` | handled | xi-tools |
| `09` | the speaking NPC's name | 467 | `NpcName` | handled (the speaker of 0x1D / 0x2B, or the 0x036 / 0x02A entity) | xi-tools |
| `0A n` | number parameter `n` | 2,276,195 | `Number` (code `0A`) | handled | xi-tools |
| `0B` | the choice list of a query starts | 30,064 | `ChoicesStart` | handled (`FormatQuery` options) | xi-tools |
| `0C n [a/b/...]` | the alternative number parameter `n` picks, from the next list on the line ("in `0A 00` `0C 01` [days/hours/hour or less]") | 858,330 | `Selector` (code `0C`) | handled (index clamped); alternatives made of `EF` icons (the weather list) are decoded as text | xi-tools; corpus for the gap |
| `0E n` | a sound effect ("`0E 00`eventSE: No.", xi-tools: EVENT_SOUND_EFFECT) | 37 | `Unknown`, 2 bytes | dropped (no sound) | corpus; xi-tools code table |
| `11 n` | "You learned Trust: `11 01`!" (xi-tools: EVENT_SPELL_NAME, a spell name from parameter `n`) | 294 | `Unknown`, 2 bytes | dropped | corpus; xi-tools code table |
| `18 n` | the name of the entity whose server id is number parameter `n` ("The synergy furnace is currently in use by `18 01`.", the debug line "ID:`0A 00` --> `18 00`"; xi-tools: PARTY_MEMBER_NAME_BY_ID) | 2,953 | `EntityName` (code `18`) | handled: the player, a zone entity or a party member with that id; empty when not known | corpus; xi-tools code table |
| `19 n` | party / alliance member `n`'s name ("Whose Mog House will you visit?" lists `19 01`-`19 11`) | 238 | `EntityName` (code `19`) | handled: the event VM's 18 party slots (0 the player, 1-5 the player's party, 6-11 / 12-17 the other alliance parties, as XiEvents InitEvent2 / GetActorNum numbers them); empty for an empty slot | xi-tools (PARTY_MEMBER_NAME); XiEvents for the slot order |
| `1C n` | string parameter `n`: an event's S2C 0x033 strings ("If you would like to customize `1C 00`, ...": LandSandBoat's Tateeya sends the automaton's name in all four) | 4,955 | `EventString` | handled for event lines; empty in 0x036 / 0x02A lines (their string is not known to be `1C`) and for 0x034 events; the fishing lines of 0x027 / 0x043 are not handled | corpus; LandSandBoat Tateeya.lua; xi-tools (EVENT_STRING) |
| `1E n` | not known; stands before headings ("`1E 05`Visitant Light Intensity"), probably a colour | 4,661 | `Unknown`, 2 bytes | dropped | corpus |
| `1F c` | text colour `c` | 35,896 | `Colour` | dropped (lines are drawn in one colour) | decoder notes |
| other below `20` | one argument byte (xi-tools gives `04`, `06`, `0F`, `15`, `1B` none; no English line uses them) | - | `Unknown`, 2 bytes | dropped | decoder notes |
| `7F xx ...` | extended codes | see [0x7F codes](#0x7f-codes) | - | - | - |
| `EF n` | inline icon; `EF 1F`-`EF 26` stand for the eight elements in the weather list and an elemental balance line | 19,068 | `Icon` | dropped (not drawn) | corpus; xi-tools calls the range gauge-bar glyphs |
| `FD .. FD` | auto-translate phrase (6 bytes) | 0 | `Unknown`, 6 bytes | dropped | decoder notes |
| `20`-`FF` else | Shift-JIS text (`cp932`; a lead byte takes its trail byte) | - | `Text` | handled; the 85 FFXI glyphs that differ from `cp932` are not mapped | xi-tools docs/dialog/format.md |

**Differs from xi-tools:** `EF 1F`-`EF 26` are read here as element icons (they are the eight entries of the `0C` weather selector), not gauge glyphs.

**Beyond xi-tools:** the selectors' list can stand after text (`7F 92 02`credit[/s]); `18 n` reads a server id from number parameter `n`; `1C n` is the event's 0x033 string `n`. All from the corpus contexts (and LandSandBoat for `1C`), not checked against retail screens.

## 0x7F codes

Lengths: three bytes (one argument) for `0x34`-`0x36`, `0x80`, `0x81`, `0x84`, `0x86`-`0x88`, `0x8C`, `0x8F`, `0x92`, `0x94`-`0x97`, `0x99`, `0xA0`-`0xAC`, `0xB0`, `0xB1`, `0xB4`, `0xB5`; four for `0x38`; two for all others. The first scan (2026-10-01) found the extra three-byte codes in the corpus; xi-tools' code table gives the same list, and XiEvents OpCodes/0x0024 (the query handler's skip lengths) the subset `0x34`-`0x36`, `0x38`, `0x80`, `0x84`, `0x86`, `0x8C`, `0x92`.

| bytes | meaning | count | `EventMessageDecoder` | GordianXI | source |
|---|---|---|---|---|---|
| `7F 31` | prompt: wait for Confirm (the arrow) | 2,668,717 | `Prompt`; the `00` after it ends the string | handled: `PrintMessage` holds the line (`PromptOpenSeconds` = infinite) | xi-tools docs/dialog/format.md; retail recordings 2026-09-28 |
| `7F 32` / `7F 33` / `7F 37` | other manual prompts per xi-tools | 0 / 3 (Japanese) / 0 | `Prompt` | handled as `7F 31` (which arrow each draws is not known) | xi-tools |
| `7F 34 n` | the line closes by itself after `n` seconds | 11,400 (`n` = 1: 10,776; 0: 291; 2-12: 333; 180: 1) | `AutoClose` | handled: `PrintMessage` returns `n` (the Southern San d'Oria crawl: 9 and 5, retail 9.2-9.3 s and 5.1 s); `7F 34 00` closes at once (not checked) | xi-tools; retail recording 2026-09-30 |
| `7F 35 n` / `7F 36 n` | the text waits `n` seconds: `36` stands inside lines ("Canst thou...`7F 36 02` ...hear me...`7F 36 03` ...Promathia?`7F 31`") and at the end of lines with no prompt ("Shhh! Be quiet!`7F 36 01`"); `35` only right before `7F 31` (`n` 1-8) | 1,251 / 673 | `Pause` | a line without a prompt stays up for the sum of its pauses, then closes (`AutoCloseSeconds`); in a prompted line the pauses change nothing (the line is shown whole and waits for Confirm) | corpus; xi-tools calls both "auto-advance after `n` s" |
| `7F 38 a b` | two parameter bytes, at the end of prompt-less cutscene lines of Empyreal Paradox (zone 36) (`B4 00` = 180, `C8 00` = 200, `F0 00` = 240): probably a display time in frames | 84 | `Unknown`, 4 bytes (`Argument` = the u16) | dropped: the line closes at once | xi-tools; corpus |
| `7F 80 n` | the case of the next substitution: `01` before the names that open a sentence or a menu row ("Obtained key item: `7F 80 01`<key item>.", "`7F 80 01``01 01 01` <item> will be used to...", "`7F 80 01`<plural item> are not suitable..."), `02` in all-capital lines ("`7F 80 02`<item> CONFIRMED! LIGHTING UP!", "SEARCHING FOR `7F 80 02`<key item>...") | 31,706 | `CaseMode` | `01` handled: the next substitution (name, number, player...) gets a capital first letter; `02` (all capitals or title case: "Mercy: `7F 80 02`<key items> and <key items>" fits title case better) dropped | corpus |
| `7F 81 n` | a string parameter ("LS0: `7F 81 00`.", linkshell names in a debug menu) | 18 | `Unknown`, 3 bytes | dropped | corpus; xi-tools code table |
| `7F 84 n` / `7F 86 n` / `7F 87 n` / `7F 88 n` / `7F 8C n` / `7F 8F n` / `7F 97 n` / `7F B0 n` / `7F B1 n` | three bytes (xi-tools names: ABILITY_MODIFIERS, ABILITY_PLURAL_SELECT, NPC_PLURAL_SELECT, NPC_PROPER_SELECT, -, ABILITY_NAME2, ACTION_HEX_VALUE, -, -) | 0 | `Unknown`, 3 bytes | dropped | xi-tools code table; XiEvents OpCodes/0x0024 |
| `7F 85 [a/b]` | one of two words by the player's sex ("[his/her]", "[sir/madam]"), from the next list on the line | 4,951 | `GenderSelector` | handled (look's race byte: 2, 4, 6, 7 female); left as `[a/b]` when the look is not known | retail recording 2026-09-30 |
| `7F 92 n [a/b]` | singular or plural by number parameter `n` ("[monster/monsters]", "[/s]", "[time/times]"): the first when the number is 1 | 1,089,548 | `Selector` (code `92`) | handled | corpus; xi-tools docs/events/authoring.md (`{plural:n}`) |
| `7F 93` | an entity's name ("I'm `7F 93`, your friendly neighborhood...", "`7F 93` has entered the hostel.", Ballista lines; xi-tools: SPECIAL_NAME) | 24 | `EntityName` (code `93`) | dropped: whose name it is is not known (perhaps the String of S2C 0x02A, which retail copies aside when Flag is 0: XiPackets 0x002A) | corpus |
| `7F 94 n` | number `n` with two digits ("`0A 00`:`7F 94 01`" clock times, the elemental balance figures) | 4,754 | `Number` (code `94`) | handled (`D2`) | corpus; xi-tools (TWO_DIGIT_VALUE) |
| `7F 95 n` / `7F 96 n` | number `n` in hexadecimal / binary ("Base-16 (hexadecimal): `7F 95 00`", "Base-2 (binary): `7F 96 00`", debug lines) | 310 / 15 | `Number` (codes `95`, `96`) | handled (upper-case hex, no padding; binary digits) | corpus; xi-tools (HEX_VALUE, BINARY_VALUE) |
| `7F 99 n` | number `n` with four digits ("Your registration number ... is `7F 99 03`.") | 7 | `Number` (code `99`) | handled (`D4`) | corpus; xi-tools (FOUR_DIGIT_VALUE) |
| `7F A0 n`-`7F AA n` | a field of the date in number parameter `n`, seconds since the Vana'diel epoch 2001-12-31 15:00 UTC, shown in the player's **local** time zone: `A0` month, `A1` day, `A2` year (English client order), `A3` hour, `A4` minute, `A5` second, unpadded; `A6`-`AA` month, day, hour, minute, second with two digits (the Mog Locker lease "`A0`/`A1`/`A2` `A3`:`A9`:`AA`"; the Assist Channel line orders its codes "`A1`/`A2`/`A0` at `A3`:`A9` (JST)", which this reading prints day/year/month) | A0 432, A1 689, A2 689, A3 695, A9 691, AA 123, others under 5 | `DateField` | handled in local time (`IEventMessageContext.TimeZone`, default `TimeZoneInfo.Local`). **Confirmed against retail 2026-10-03:** Southern San d'Oria 6702 with `mog-locker-expiry-timestamp` 781790400 (2026-10-10 12:00 JST) reads "Your Mog Locker lease is valid until 10/9/2026 20:00:00, kupo." on a US Pacific (UTC-7) machine, so `A0`-`A2`, `A3`, `A9`, `AA` are confirmed; `A4`-`A8` are corpus readings only | retail (maintainer, 2026-10-03); corpus; LandSandBoat moghouse.lua (the epoch) |
| `7F AB n` / `7F AC n` | a whole Earth / Vana'diel date and time from parameter `n` ("until `AC 00` (`AB 00` Earth time)") | 547 / 123 | `Unknown`, 3 bytes | dropped: the format is not known | corpus |
| `7F B4 n` / `7F B5 n` | `B4`: "GIL:`7F B4 00`" (a debug line); `B5`: a Pankration feral skill name ("The feral skill `7F B5 03` is now level...") | 1 / 19 | `Unknown`, 3 bytes | dropped | corpus |
| `7F FB` / `7F FC` | entity name wrap markers | - | skipped | dropped | decoder notes |

**Beyond xi-tools and XiEvents:** `7F 80 01` as a capital letter for the next substitution; `7F 36 n` as a pause inside a line (and a hold at the end of a prompt-less line) rather than an auto-advance; `7F A0`-`7F AA` as the fields of a date counted from the Vana'diel epoch in local time (`A0`-`A3`, `A9`, `AA` confirmed against retail 2026-10-03, the others corpus readings); `7F 38`'s observed values. From the corpus contexts above (and LandSandBoat for the epoch); apart from the date fields named, not checked against retail screens.

## 0x01 tags

Layout: `01 len kind`, then one sub-block per value: a byte whose value XOR 0x80 is the value's byte count (always 2 in the tables), the value bytes each XOR 0x80 (little-endian), and a closing byte. `len` counts the bytes after itself: `01 05 23 82 85 80 80` is seven bytes, item name from parameter 5. Each value is a message parameter index; `EventMessageFormatter` reads the parameter (`GetNumber(values[n])`), maps the tag kind onto one of the kinds `EventMessageNames.Resolve` knows (`23` item name, `24` item log name, `25` item plural log name, `33` key item name, `35` key item plural, `38` zone name) and prints a placeholder when the name is not known: `<` + the kind as a character + the id + `>`, or `<` + the kind in hex + `:` + the id + `>` for a kind that is not a printable character (`<17:4>`).

Item names (`ItemRecord`): `Name` "Fire Crystal", log name "fire crystal" / "chunk of copper ore", plural log name "fire crystals" / "chunks of copper ore" (`StockUiShop.LongName` / `PluralName`, which fall back to the name). Key items (d_msg key item table): `name` "blue acidity tester", `plural` "blue acidity testers".

**Differs from xi-tools:** docs/dialog/format.md counts `len` as the whole tag including the `01`; its own example (`01 05 25 82 81 80 80`) and every tag in the corpus (lengths 5 and 9 on 7- and 11-byte tags) count it from the kind byte.

| kind | meaning | count | GordianXI | source |
|---|---|---|---|---|
| `01` (`01 01 01`, no value) | the article ("a" / "an") of the item tag after it ("You have been rewarded `01 01 01` <`24` item> as compensation.") | 16,869 | handled: "an" when the next item tag's text starts with a vowel, else "a" (the rule of retail's shop lines, `StockUiShop.DescribeCount`); "a" when no item tag follows | corpus; xi-tools mentions an article slot |
| `03` / `04` | a number ("You obtain `01 05 03 p1` <`29` item>!"; `04` the count a plural item tag goes by) | 510 / 0 | handled: the parameter's number | xi-tools; corpus |
| `12` | a name ("<`12`>, a hero's story!") | 727 | placeholder | corpus |
| `17` / `18` | weather names in the forecast lines ("will be <`17`> with occasional <`18`>") | 2,499 / 4,943 | placeholder (no weather name table is loaded) | corpus |
| `20` | not known (mostly Japanese lines) | 2,308 | placeholder | corpus |
| `23` `#` | item name ("Obtained: `7F 80 01` <`23`>.", "You cannot obtain the <`23`>.") | 93,799 | handled: the item's name ("Fire Crystal") | xi-tools; LandSandBoat ITEM_OBTAINED |
| `24` `$`, `26`, `27`, `28` | item log name, singular (after `01 01 01`: "a fire crystal") | 16,867 / 0 / 1 / 1 | handled: log name | xi-tools |
| `25` `%` | item log name, plural ("You cannot obtain the <`25`>.", "`0A 01` <`25`> have been successfully synergized!") | 4,957 | handled: plural log name | xi-tools; corpus |
| `29` `)` / `2A` | an item counted by another parameter: values (count parameter, item parameter); "You obtain `0A 01` <`29` p1 p0>!" | 151,428 / 0 | handled: singular log name when the count is 1, else plural (the count itself is printed by the code before the tag) | xi-tools docs/events/authoring.md (`{qtyitem:c:i}`); corpus |
| `30`, `40` | not known (`40`: Unity "Wanted" objective names) | 928 / 776 | placeholder | corpus |
| `33` `3` | key item name | 16,341 | handled: `TryGetKeyItemName` (#89) | xi-tools |
| `35` `5` | key item, plural name ("You cannot carry any more <`35`>.", "Obtained key item: `0A 01` <`35`>!", "How many <`35`> will you use?") | 1,557 | handled: the key item's `plural` field | corpus |
| `36` `6` | key item name ("Possession of <`36`> is required", menu rows "`7F 80 01`<`36`> (`0A 02` gil)") | 4,853 | handled as `33` (whether retail adds an article here is not known) | xi-tools (seen there in menu rows) |
| `37` `7` | zone name | 323 | handled as `38` | xi-tools |
| `38` `8` | zone name | 8,155 | handled: d_msg zone names (ROM/165/84) | xi-tools |
| `84` | a chocobo's name in the chocobo raising lines ("Lately, <`84`> has been making an awful fuss at night.") | 4,107 | placeholder | corpus |
| `85` | picks from the next "[male/female]" list by a parameter (the raised chocobo's sex: "your chocobo is...a <`85`>[male/female]!") | 37 | placeholder, the list shown raw | corpus |
| `81`, `82`, `83`, `86`, `87` | not known (`81` / `83` 8 bytes long, `82` 9) | 97 / 23 / 725 / 360 / 21 | placeholder | corpus |
| others (`02`, `10`, `13`-`16`, `60`, `B3`) | single uses | under 10 each | placeholder | corpus |

**Beyond xi-tools:** `01 01 01` as the "a" / "an" of the next item tag; kind `35` as a key item's plural name; `84` as the chocobo name and `85` as a selector. From the corpus contexts, not checked against retail screens.

## Where the codes are acted on

| path | codes it uses | GordianXI | source |
|---|---|---|---|
| event line in the log (0x1D / 0x2B / 0x48 / 0x49 / 0xB0) | text, names, numbers, selectors, prompt / timer | `EventDialogController.PrintMessage` -> `PrintLines` (speaker prefixed) | [vm.md](vm.md) |
| event line in event message mode (0x67 on) | as above plus `02` / `03` position | `EventScreenText`, `StockUiHud.DrawEventText` | [vm.md](vm.md#event-message-mode-and-late-arrivals) |
| query (0x24 / 0xD4) | `0B` splits the question from the options | `FormatQuery`, `StockUiMenuController.OpenQuery` | [ui/stock-ui.md](../ui/stock-ui.md#dialog-text-chunk-6) |
| zone message S2C 0x036 / 0x02A | text, names, numbers (packet `Num[0..3]`); bit 15 of the message id hides the name | `EventDialogController.OnDialogMessage` | xi-tools docs/events/chat-messages.md |
| zone message S2C 0x027 / 0x043 | the fishing lines (`Num1`, `String1`; "`1C 00` caught..." reads the name as string `1C 00`?) | not handled | xi-tools docs/events/chat-messages.md |

**Differs from xi-tools:** chat-messages.md says the chat log cuts a line at the first `0x7F`. GordianXI does not cut zone messages there; the prompt is dropped and the rest is formatted. Not checked against retail.

## Open gaps ([#74](https://github.com/jimmy58663/GordianXI/issues/74), [#202](https://github.com/jimmy58663/GordianXI/issues/202))

Done on branch `ui/202-74-dialog-codes` (2026-10-03, awaiting the in-game test): a lone `02` / `03` is 3 bytes; `7F 32` / `33` / `37` are prompts; `7F 35` / `7F 36` pause, and hold a prompt-less line; the `00` after `7F 31` ends the string; `7F 92` plurals; the three-byte `7F` codes; number forms `7F 94` / `95` / `96` / `99`; date fields `7F A0`-`AA`; `7F 80 01` capitals; `18 n`, `19 n` and `1C n` names and strings; 0x01 kinds `01`, `03` / `04`, `24`-`2A`, `35`, `36`, `37`. Checked against the corpus (every English line decodes with no raw selector list left except the `85` tags and three lists with no code before them) and the retail-data test `EventRealDataTests.RetailLines_FormatPluralsItemFormsDatesAndCase`; not yet against retail screens.

| gap | state in the code | to do |
|---|---|---|
| `02` / `03` in the log | the pair places event message mode lines; the log ignores it | decide whether the log does anything with a position |
| prompt arrows and timings | `7F 32` / `33` / `37` wait like `7F 31`; `7F 34 00` (291 lines) closes at once; `7F 35` / `36` before a prompt do not shorten the wait | check the arrow of each prompt, `7F 34 00`, and whether `7F 35 n` + `7F 31` closes after `n` s like `7F 34`, against a retail recording |
| `7F 38 a b` | dropped, the line closes at once | find the unit (frames?) from a recording of Empyreal Paradox (zone 36) |
| `7F 80 02` | dropped | all capitals or title case |
| `7F 93` | prints nothing | whose name (0x02A String?) |
| `7F AB` / `7F AC` | print nothing | the Earth / Vana'diel date formats (Ballista, Kokba Hostel lines) |
| date fields | local time, M/D/Y in the English client (confirmed on the lease line) | `A4`-`A8` and the Assist Channel line's `A1`/`A2`/`A0` order are not checked; the Japanese client's order (Y/M/D?) is not handled |
| `11 n` (Trust name), `05 n`, `7F 81`, `7F B4`, `7F B5` | dropped | resolve from the spell table (`11`) and find the others' sources |
| 0x01 kinds `12`, `17` / `18` (weather), `20`, `30`, `40`, `84` (chocobo name), `85` (selector), `81`-`87` | placeholders | find their tables |
| `1C n` in 0x02A / 0x027 lines | empty | which packet string `1C` reads |

## Chunk 6 summary

The decoder summary as first written for chunk 6 (2026-09-28), moved from [ui/stock-ui.md](../ui/stock-ui.md#dialog-text-chunk-6); the tables above supersede it where they differ.

0x07 line break, 0x0B starts the choice list, 0x0A n number parameter n, 0x0C n `[a/b/c]` picks by parameter n, 0x08/0x09 player/NPC name, 0x19 n / 0x7F 0x93 another entity, 0x1F c colour, 0x01 len type ... a named thing (type '#' item, '3' key item, '8' zone; sub-blocks of (length ^ 0x80) value bytes ^ 0x80 and a closing byte; the first value is the index of the parameter holding the id), 0x7F 0x31 0x00 the prompt (the event waits for Confirm), other 0x7F codes two bytes (0x34-36/80/84/86/8C/92 three, 0x38 four, per the retail query handler), 0xEF n icon, 0xFD...0xFD auto-translate, the rest Shift-JIS. Sub-block values are read from the number parameters (a packet's num[], an event's zone work values from slot 2); which array an event's text really reads is unconfirmed.
