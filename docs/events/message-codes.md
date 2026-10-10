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
| `05 n` | a skill name from number `n`; once per zone table, "<player>'s `05 00` has..." (xi-tools: SKILL_TEXT) | 294 | `ActionName` (code `05`), 2 bytes | empty in zone lines (no context names it); named in battle lines ([basic-message table](#basic-message-table-335)) | corpus; xi-tools code table; file 7027 |
| `07` | line break | 4,949,614 | `LineBreak` | handled (new log line / screen line) | xi-tools |
| `08` | the player's name | 12,292 | `PlayerName` | handled | xi-tools |
| `09` | the speaking NPC's name | 467 | `NpcName` | handled (the speaker of 0x1D / 0x2B, or the 0x036 / 0x02A entity) | xi-tools |
| `0A n` | number parameter `n` | 2,276,195 | `Number` (code `0A`) | handled | xi-tools |
| `0B` | the choice list of a query starts | 30,064 | `ChoicesStart` | handled (`FormatQuery` options) | xi-tools |
| `0C n [a/b/...]` | the alternative number parameter `n` picks, from the next list on the line ("in `0A 00` `0C 01` [days/hours/hour or less]") | 858,330 | `Selector` (code `0C`) | handled (index clamped); alternatives made of `EF` icons (the weather list) are decoded as text | xi-tools; corpus for the gap |
| `0E n` | a sound effect ("`0E 00`eventSE: No.", xi-tools: EVENT_SOUND_EFFECT) | 37 | `Unknown`, 2 bytes | dropped (no sound) | corpus; xi-tools code table |
| `11 n` | "You learned Trust: `11 01`!" (xi-tools: EVENT_SPELL_NAME, a spell name from parameter `n`) | 294 | `Unknown`, 2 bytes | dropped | corpus; xi-tools code table |
| `12 n` | number parameter `n` (xi-tools: NUMBER), the system table's form of `0A n` ("Executing logout in `12 00` seconds.", "The compass reads: X:`12 00` Y:`12 01`...") | not counted in the zone tables | `Number` (code `12`) | handled | client system table (ROM/27/76); xi-tools code table |
| `18 n` | the name of the entity whose server id is number parameter `n` ("The synergy furnace is currently in use by `18 01`.", the debug line "ID:`0A 00` --> `18 00`"; xi-tools: PARTY_MEMBER_NAME_BY_ID) | 2,953 | `EntityName` (code `18`) | handled: the player, a zone entity or a party member with that id; empty when not known | corpus; xi-tools code table |
| `19 n` | party / alliance member `n`'s name ("Whose Mog House will you visit?" lists `19 01`-`19 11`) | 238 | `EntityName` (code `19`) | handled: the event VM's 18 party slots (0 the player, 1-5 the player's party, 6-11 / 12-17 the other alliance parties, as XiEvents InitEvent2 / GetActorNum numbers them); empty for an empty slot | xi-tools (PARTY_MEMBER_NAME); XiEvents for the slot order |
| `1C n` | string parameter `n`: an event's S2C 0x033 strings ("If you would like to customize `1C 00`, ...": LandSandBoat's Tateeya sends the automaton's name in all four), replaced by S2C 0x05D | 4,955 | `EventString` | handled for event lines (0x033, then 0x05D); in 0x027 lines String1 / String2 are `1C 0` / `1C 1` and in 0x043 lines the name is `1C 0` (provisional, [#110](https://github.com/jimmy58663/GordianXI/issues/110)); empty in 0x036 / 0x02A lines (their string is not known to be `1C`) and for 0x034 events | corpus; LandSandBoat Tateeya.lua; xi-tools (EVENT_STRING) |
| `1D` | the heading (xi-tools: HEADING, one argument byte): only in the emote table's untargeted `/point` line, "`{caster}` points `1D`." | 0 in the zone tables | `Heading`, **1 byte** | handled: the caster's compass point ("north", eight points; provisional wording) | client emote table (ROM/27/70) |
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
| `7F 84 n` / `7F 87 n` / `7F 8C n` / `7F 8F n` / `7F 97 n` / `7F B0 n` / `7F B1 n` | three bytes (xi-tools names: ABILITY_MODIFIERS, NPC_PLURAL_SELECT, -, ABILITY_NAME2, ACTION_HEX_VALUE, -, -) | 0 | `Unknown`, 3 bytes | dropped | xi-tools code table; XiEvents OpCodes/0x0024 |
| `7F 86 n [a/b]` | singular or plural by number `n`, as `7F 92` (xi-tools: ABILITY_PLURAL_SELECT): "`12 00` `7F 86 00`[second/seconds] until position reset." | 0 in the zone tables | `Selector` (code `86`) | handled | client system table |
| `7F 88 n [the /]` | the article of message entity `n` (xi-tools: NPC_PROPER_SELECT): "waves to `7F 88 01`[the /]`01 01 11`." | 0 in the zone tables | `ArticleSelector` | handled: the first word for a monster, the second for anyone else (provisional) | client emote and system tables |
| `7F 90 [a/b]` / `7F 91 [a/b]` | message entity 0's / 1's sex (xi-tools: NPC0_GENDER / NPC1_GENDER): "claps `7F 90`[his/her] hands." | 0 in the zone tables | `EntityGenderSelector` | handled from the entity's race look; the list shown raw when not known (fixed models, monsters) | client emote table |
| `7F 85 [a/b]` | one of two words by the player's sex ("[his/her]", "[sir/madam]"), from the next list on the line | 4,951 | `GenderSelector` | handled (look's race byte: 2, 4, 6, 7 female); left as `[a/b]` when the look is not known | retail recording 2026-09-30 |
| `7F 92 n [a/b]` | singular or plural by number parameter `n` ("[monster/monsters]", "[/s]", "[time/times]"): the first when the number is 1 | 1,089,548 | `Selector` (code `92`) | handled | corpus; xi-tools docs/events/authoring.md (`{plural:n}`) |
| `7F 93` | an entity's name ("I'm `7F 93`, your friendly neighborhood...", "`7F 93` has entered the hostel.", Ballista lines; xi-tools: SPECIAL_NAME) | 24 | `EntityName` (code `93`) | dropped: whose name it is is not known (perhaps the String of S2C 0x02A, which retail copies aside when Flag is 0: XiPackets 0x002A) | corpus |
| `7F 94 n` | number `n` with two digits ("`0A 00`:`7F 94 01`" clock times, the elemental balance figures) | 4,754 | `Number` (code `94`) | handled (`D2`) | corpus; xi-tools (TWO_DIGIT_VALUE) |
| `7F 95 n` / `7F 96 n` | number `n` in hexadecimal / binary ("Base-16 (hexadecimal): `7F 95 00`", "Base-2 (binary): `7F 96 00`", debug lines) | 310 / 15 | `Number` (codes `95`, `96`) | handled (upper-case hex, no padding; binary digits) | corpus; xi-tools (HEX_VALUE, BINARY_VALUE) |
| `7F 99 n` | number `n` with four digits ("Your registration number ... is `7F 99 03`.") | 7 | `Number` (code `99`) | handled (`D4`) | corpus; xi-tools (FOUR_DIGIT_VALUE) |
| `7F A0 n`-`7F AA n` | a field of the date in number parameter `n`, seconds since the Vana'diel epoch 2001-12-31 15:00 UTC, shown in the player's **local** time zone: `A0` year, `A1` month, `A2` day, `A3` hour, `A4` minute, `A5` second, unpadded; `A6`-`AA` month, day, hour, minute, second with two digits. The order is the message's own: the Mog Garden lease line and the Assist Channel line write "`A1`/`A2`/`A0` at `A3`:`A9`..." (month/day/year), the city lease lines (`MOG_LOCKER_OFFSET`) "`A0`/`A1`/`A2` `A3`:`A9`:`AA`" (year/month/day) | A0 432, A1 689, A2 689, A3 695, A9 691, AA 123, others under 5 | `DateField` | handled in local time (`IEventMessageContext.TimeZone`, default `TimeZoneInfo.Local`). **Confirmed against retail 2026-10-03:** Mog Garden (zone 280) message 7538, which LandSandBoat's Green Thumb Moogle sends (`MOGLOCKER_MESSAGE_OFFSET` + 1; raw `Your Mog Locker may be used until:` `07` `7F A1 00`/`7F A2 00`/`7F A0 00` at `7F A3 00`:`7F A9 00`:`7F AA 00` (Earth Time).), with `mog-locker-expiry-timestamp` 781790400 (2026-10-10 12:00 JST) reads "10/9/2026 at 20:00:00" on a US Pacific (UTC-7) machine: `A0`-`A3`, `A9`, `AA` and local time confirmed; the city lease line (Port Jeuno, `MOG_LOCKER_OFFSET`, `A0`/`A1`/`A2`) reads year/month/day in retail as here, so each message's own code order is confirmed; `A4`-`A8` are corpus readings only | retail (maintainer, 2026-10-03); corpus; LandSandBoat moghouse.lua, Mog_Garden/IDs.lua (the epoch, the message) |
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
| `17` / `18` | a weather by weather id in the forecast lines: `17` its adjective ("will be <`17`>" = "will be sunny"), `18` its noun ("with a chance of <`18`>" = "rain"; "is for <`18`> with occasional <`18`>") | 2,499 / 4,943 | handled: the d_msg weather table `ROM/165/79` (`DMsgCategory.WeatherNames`), whose rows hold the noun then the adjective ("rain" / "rainy"); `ResourceManager.TryGetWeatherName` ([#125](https://github.com/jimmy58663/GordianXI/issues/125)) | Maleme's forecast lines (Southern San d'Oria event 632, messages 6563-6574) with [0x72](vm.md#weather-forecast-0x72), checked in game against retail (2026-10-04: "will be sunny with a chance of rain") |
| `20` | not known (mostly Japanese lines) | 2,308 | placeholder | corpus |
| `23` `#` | item name ("Obtained: `7F 80 01` <`23`>.", "You cannot obtain the <`23`>.") | 93,799 | handled: the item's name ("Fire Crystal") | xi-tools; LandSandBoat ITEM_OBTAINED |
| `24` `$`, `26`, `27`, `28` | item log name, singular (after `01 01 01`: "a fire crystal") | 16,867 / 0 / 1 / 1 | handled: log name | xi-tools |
| `25` `%` | item log name, plural ("You cannot obtain the <`25`>.", "`0A 01` <`25`> have been successfully synergized!") | 4,957 | handled: plural log name | xi-tools; corpus |
| `29` `)` / `2A` | an item counted by another parameter: values (count parameter, item parameter); "You obtain `0A 01` <`29` p1 p0>!" | 151,428 / 0 | handled: singular log name when the count is 1, else plural (the count itself is printed by the code before the tag) | xi-tools docs/events/authoring.md (`{qtyitem:c:i}`); corpus |
| `30`, `40` | not known (`40`: Unity "Wanted" objective names) | 928 / 776 | placeholder | corpus |
| `10` / `11` (`01 01 10`, `01 01 11`, no value) | message entity 0 / 1: the emote's caster and target, the dropper of "You find ... on `7F 88 01`[the /]`01 01 11`." | not in the zone tables with no value | handled: the entity's name ([client tables](#client-message-tables-110)) | client emote and system tables |
| `33` `3` | key item name | 16,341 | handled: `TryGetKeyItemName` (#89) | xi-tools |
| `35` `5` | key item, plural name ("You cannot carry any more <`35`>.", "Obtained key item: `0A 01` <`35`>!", "How many <`35`> will you use?") | 1,557 | handled: the key item's `plural` field | corpus |
| `36` `6` | key item name ("Possession of <`36`> is required", menu rows "`7F 80 01`<`36`> (`0A 02` gil)") | 4,853 | handled as `33` (whether retail adds an article here is not known) | xi-tools (seen there in menu rows) |
| `37` `7` | zone name | 323 | handled as `38` | xi-tools |
| `38` `8` | zone name | 8,155 | handled: d_msg zone names (ROM/165/84) | xi-tools |
| `84` | a chocobo's name in the chocobo raising lines ("Lately, <`84`> has been making an awful fuss at night.") | 4,107 | placeholder | corpus |
| `85` | picks from the next "[male/female]" list by a parameter (the raised chocobo's sex: "your chocobo is...a <`85`>[male/female]!") | 37 | placeholder, the list shown raw | corpus |
| `81`, `82`, `83`, `86`, `87` | not known (`81` / `83` 8 bytes long, `82` 9) | 97 / 23 / 725 / 360 / 21 | placeholder | corpus |
| others (`02`, `10`, `13`-`16`, `60`, `B3`) | single uses | under 10 each | placeholder | corpus |

**Differs from xi-tools:** `docs/events/weather.md` names weather id 0 "None"; the client's weather name table (`ROM/165/79`) has "fine patches" / "fine" in row 0 (LandSandBoat's `xi.weather` also calls 0 NONE). The forecast files never hold 0.

**Beyond xi-tools:** `01 01 01` as the "a" / "an" of the next item tag; kind `35` as a key item's plural name; `84` as the chocobo name and `85` as a selector. From the corpus contexts, not checked against retail screens.

## Where the codes are acted on

| path | codes it uses | GordianXI | source |
|---|---|---|---|
| event line in the log (0x1D / 0x2B / 0x48 / 0x49 / 0xB0) | text, names, numbers, selectors, prompt / timer | `EventDialogController.PrintMessage` -> `PrintLines` (speaker prefixed) | [vm.md](vm.md) |
| event line in event message mode (0x67 on) | as above plus `02` / `03` position | `EventScreenText`, `StockUiHud.DrawEventText` | [vm.md](vm.md#event-message-mode-and-late-arrivals) |
| query (0x24 / 0xD4) | `0B` splits the question from the options | `FormatQuery`, `StockUiMenuController.OpenQuery` | [ui/stock-ui.md](../ui/stock-ui.md#dialog-text-chunk-6) |
| zone message S2C 0x036 / 0x02A | text, names, numbers (packet `Num[0..3]`); bit 15 of the message id hides the name | `EventDialogController.OnDialogMessage` | xi-tools docs/events/chat-messages.md |
| zone message S2C 0x027 / 0x043 / 0x03B | as 0x02A; 0x027's twelve numbers (`Num1`, `Num2`) and its strings as `1C 0` / `1C 1`, 0x043's name as `1C 0` | `EventDialogController.OnDialogMessage` ([#110](https://github.com/jimmy58663/GordianXI/issues/110)) | xi-tools docs/events/chat-messages.md; XiPackets 0x0027 / 0x0043 / 0x003B |
| system message S2C 0x053 | the system table's codes, `para` / `para2` as numbers 0 / 1 | `ClientMessageController.FormatSystemMessage` | XiPackets 0x0053 |
| emote S2C 0x05A | the emote table's codes, caster / target as entities 0 / 1 | `ClientMessageController.FormatEmote` | [client tables](#client-message-tables-110) |
| battle message S2C 0x029 | the basic-message table's codes, Data / Data2 as numbers 0 / 1, caster / target as entities 0 / 1 | `CombatLogFormatter.FormatBattleMessage` | [basic-message table](#basic-message-table-335) |

**Differs from xi-tools:** chat-messages.md says the chat log cuts a line at the first `0x7F`. GordianXI does not cut zone messages there; the prompt is dropped and the rest is formatted. Not checked against retail.

## Client message tables (#110)

The client keeps its own messages in the same container and codes: the system messages (English file id 7031 = `ROM/27/76`, Japanese 7030; 326 messages, ids = LandSandBoat's `MsgStd`), read by S2C 0x053, and the emote lines (English 7025 = `ROM/27/70`, Japanese 7024; two per emote id, `2 * id` with a target and `2 * id + 1` without), read by S2C 0x05A (`ClientMessageTables`). Their lines open with `1F c` (the colour; in the system table the argument is often a printable byte, `1F {` or `1F y`) and use codes the zone tables do not: `12 n` numbers, `7F 86 n` plurals, `0x01` kinds `10` / `11` with no value for the message's entities (the emote's caster and target), `7F 88 n` their article, `7F 90` / `7F 91` their sex, `1D` the heading, `7F FC` ... `7F FB` around a name. `IEventMessageContext.GetMessageEntity` and `Heading` supply them; `SimpleMessageContext.Entities` / `HeadingText` set them.

**Beyond xi-tools and XiPackets:** the emote file and its `2 * id (+ 1)` layout (found by searching the retail DATs for the emote text, checked against LandSandBoat's emote ids, 2026-10-03); the entity meanings of `01 01 10` / `01 01 11`, `7F 88` and `7F 90` / `7F 91` (from the lines that use them). **Differs from xi-tools:** `1D` takes no argument byte (xi-tools gives it one; its only English use is followed by the line's full stop). Not checked against retail screens: who takes "the" (monsters only here), the heading words, and the `[his/her]` of a monster or fixed-model caster (left raw).

## Basic-message table (#335)

The battle messages (English file id 7027 = `ROM/27/72`, Japanese 7026 = `ROM/27/71`; 1024 messages, ids = S2C 0x029 `MessageNum`, LandSandBoat `MsgBasic`) use the client tables' container and entity codes (`7F 88 n`, `01 01 10` / `01 01 11`, `12 n`, `7F 86 n`, `1F c`) plus the codes below. `CombatLogFormatter.FormatBattleMessage` formats them with a `SimpleMessageContext`: the packet's Data / Data2 as numbers 0 / 1, caster / target as entities 0 / 1 (a monster takes "the"), `ResolveActionName` for the name codes. Read from the retail table 2026-10-10 (a census of every code: `7F 31` 1023, `7F 88` 790, `7F 87` 642, `01 01 11` 475, `12` 404, `01 01 10` 386, `7F 86` 131, `7F 84` 118, `7F 8F` 116, `10` 60, `16` 59, `1F` 47, 0x01 kind `13` 42, `05` 15, `7F B4` 4, `14` 3, `7F 9B` 1, `7F B7` 1; others under 5).

| bytes | meaning | count | `EventMessageDecoder` | GordianXI | source |
|---|---|---|---|---|---|
| `7F 87 n [a/b]` | verb form by message entity `n` ("`7F 88 00`[The /]`01 01 10` `7F 87 00`[hits/hit]"; xi-tools NPC_PLURAL_SELECT) | 642 | `Selector` (code `87`) | handled: always the first (singular) form | file 7027 |
| `05 n` | skill name from number `n` (xi-tools SKILL_TEXT): "`05 00` skill rises" | 15 | `ActionName` (code `05`) | handled in battle lines (`CombatLogFormatter.SkillName`, lower case as in retail); empty elsewhere. Also "seems `05 01`." in the check messages 170-178, where it is not a skill: those stay hand-written | file 7027; retail capture 2026-10-07 |
| `10 n` | spell name from number `n` (xi-tools SPELL_NAME): "casts `10 00`." | 60 | `ActionName` (code `10`) | handled in battle lines | file 7027 |
| `16 n` | weapon skill / monster ability name (xi-tools ABILITY_NAME): "readies `16 01`.", "uses `16 00`." | 59 | `ActionName` (code `16`) | handled in battle lines (`ResolveWeaponSkillName`) | file 7027 |
| `7F 8F n` | job ability name (xi-tools ABILITY_NAME2): "uses `7F 8F 00`." | 116 | `ActionName` (code `8F`) | handled in battle lines (`ResolveAbilityName`) | file 7027 |
| `7F 9B n` | number `n` in tenths: "skill rises `7F 9B 01` points." = "0.1" | 1 | `Number` (code `9B`), **3 bytes** | handled | file 7027; retail capture "rises 0.1 points" (2026-10-07) |
| `7F B4 n` | an amount of gil from number `n`: "obtains `7F B4 00`.", "mugs `7F B4 01` from" | 4 (and the zone tables' debug "GIL:" line) | `Number` (code `B4`) | handled as "N gil" (provisional: the word is not checked against a retail screen) | file 7027 |
| `7F 84 n` | before the second line of damage messages ("`07` `7F 84 00` [The /]... takes"); xi-tools ABILITY_MODIFIERS | 118 | `Unknown` (code `84`), 3 bytes | handled for S2C 0x028: the result's `bit` flags as XiPackets names them ("Cover! ", "Resist! ", "Magic Burst! ", "Immunobreak! ", "Critical Hit! "; `CombatLogFormatter.ModifierText`, provisional spacing); empty for 0x029 | file 7027; XiPackets 0x0028 `bit` |
| `14 n` | xi-tools TIME: "Time left until next use: (`14 00`)" | 3 | `Unknown`, 2 bytes | dropped (format not known) | file 7027 |
| `7F B7 n` | "`7F B7 00` attribute increased to `12 01`." | 1 | `Unknown`, **3 bytes** | dropped | file 7027 |
| 0x01 kinds `13` / `14` | status name / adjective from number `n` ("gains the effect of <`13`>", "is no longer <`14`>") | 42 / 21 | `Name` | handled: d_msg `ROM/180/102` name / adjective (`EventMessageNames`) | file 7027 |

**Differs from xi-tools:** its code table gives `7F 9B` and `7F B7` no argument; both take one (message 38 "`7F 9B 01` points", 828 "`7F B7 00` attribute"). Read as no argument, the `01` after `7F 9B` would open a 0x01 tag. **Beyond xi-tools / XiPackets:** the file and the meanings of `7F 87`, `7F 9B` and `7F B4`, from the table's lines. Numbers 2 and up (spikes 44 "`12 03` points", counters 14 / 33) are not in S2C 0x029; those lines keep the hand-written text there.

**S2C 0x028 reads the same table** (XiPackets 0x0028 names file 7027 for `message`, `proc_message` and `react_message`): `CombatLogFormatter.FormatActionLines` formats each result's message, added effect and reaction with number 0 the action id (`cmd_arg`: spell, ability, weapon skill), 1 the result value, 2 the added effect's value, 3 the reaction's value, and the actor / target as entities 0 / 1. **Beyond XiPackets:** that numbering, read from the messages (1 "for `12 01` points", 2 "casts `10 00`", 163 "Additional effect: `12 02` points", 44 spikes "`12 03` points", 536 "retaliates ... `12 03`"). Every line break of a message is its own log line, as in retail: a critical hit (67) is "Gemini scores a critical hit!" then "The Wild Rabbit takes 25 points of damage.", a spell with an effect (2, 7, 230, 236, 237 ...) prints the cast and the effect on two lines. A message id 0 and a missing table keep the hand-written text, which now splits the same way.

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
| date fields | local time; `A0` year, `A1` month, `A2` day (confirmed on the Mog Garden lease line 280:7538) | the city lease line's year/month/day order is confirmed in retail (Port Jeuno, 2026-10-03); check whether the Assist Channel line, which says "(JST)", stays in JST in retail; `A4`-`A8` not checked |
| `11 n` (Trust name), `05 n`, `7F 81`, `7F B4`, `7F B5` | dropped | resolve from the spell table (`11`) and find the others' sources |
| 0x01 kinds `12`, `20`, `30`, `40`, `84` (chocobo name), `85` (selector), `81`-`87` | placeholders | find their tables |
| `1C n` in 0x02A / 0x027 lines | empty | which packet string `1C` reads |

## Chunk 6 summary

The decoder summary as first written for chunk 6 (2026-09-28), moved from [ui/stock-ui.md](../ui/stock-ui.md#dialog-text-chunk-6); the tables above supersede it where they differ.

0x07 line break, 0x0B starts the choice list, 0x0A n number parameter n, 0x0C n `[a/b/c]` picks by parameter n, 0x08/0x09 player/NPC name, 0x19 n / 0x7F 0x93 another entity, 0x1F c colour, 0x01 len type ... a named thing (type '#' item, '3' key item, '8' zone; sub-blocks of (length ^ 0x80) value bytes ^ 0x80 and a closing byte; the first value is the index of the parameter holding the id), 0x7F 0x31 0x00 the prompt (the event waits for Confirm), other 0x7F codes two bytes (0x34-36/80/84/86/8C/92 three, 0x38 four, per the retail query handler), 0xEF n icon, 0xFD...0xFD auto-translate, the rest Shift-JIS. Sub-block values are read from the number parameters (a packet's num[], an event's zone work values from slot 2); which array an event's text really reads is unconfirmed.
