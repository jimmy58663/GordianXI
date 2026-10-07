# Entity Flags and Bit Fields

> Scope: every entity flag word GordianXI knows. The first half is the wire: the send flags and the four flag words of the entity updates S2C 0x00D (players) and 0x00E (NPCs, monsters, pets, trusts), the local player's S2C 0x037, and the values LandSandBoat writes into them. The second half is the retail client's in-memory `Render.Flags0`-`Flags7` words, which the event VM sets and tests (named by XiEvents), with the event opcodes that touch each bit. Many event opcodes GordianXI does not run yet only change one of these bits, so this is also the map of what such an opcode would need. Bit numbering and offsets follow [conventions.md](conventions.md#byte-and-bit-order): bit n of a word is `(W >> n) & 1` of the u32 read little-endian; "payload" offsets are packet offsets minus 4. Every "GordianXI use" entry was checked in `src/`. Date of this pass: 2026-10-01.

Sources: XiPackets `world/server/0x000D`, `0x000E`, `0x0037` (bit names and documented effects), LandSandBoat `src/map/packets/entity_update.cpp` (what the server writes) and `data/enums/{status,entity_flags,name_vis}.yaml`, XiEvents `OpCodes/*.md`, "Event VM Functions.md" and "Event VM Structures.md" (render flags). Readings marked *inference* are ours, from the pattern of uses; the sources do not name them.

## Send flags (0x00D / 0x00E payload 6, packet 0x0A)

| Bit | Name | Meaning | GordianXI use | Source |
|---|---|---|---|---|
| 0 | Position | position, heading, speed fields are valid | `EntityUpdateFlags.Position`; `EntityPacketModule` moves the entity only with it, and reads GroundFlag only then | XiPackets 0x000D / 0x000E |
| 1 | ClaimStatus | the claim / battle target field is valid | `ClaimServerId` taken only with it | same |
| 2 | General | HP, status and the flag words are valid | name plate flags and linkshell colour are read only with it (a position-only update made a running player's pearl vanish) | same |
| 3 | Name | the name is present | `GetName` | same |
| 4 | Model | 0x00D: the grap id table is present | `TryGetGrapIdTable` | same |
| 5 | Despawn | the entity leaves | `WorldState.RemoveEntity` | same |
| 6 | Name2 | 0x00E only: the name is updated, read from payload 0x40 for SubKind 1 | `HasName2` | XiPackets 0x000E (0x000D marks bits 6-7 unused) |

## Flags0 (0x00D / 0x00E payload 20, packet 0x18)

| Bits | XiPackets name | Meaning | GordianXI use |
|---|---|---|---|
| 0-12 | MovTime | movement counter, 60 per second since the move began | 0x00D: `S2C_0x00D_CharPc.MovTime` drives remote movement playback (`WorldEntity.AddServerSample`). 0x00E: ignored; LandSandBoat writes NPC database flags in the low bits (`EntityPacketModule`) |
| 13 | RunMode | used by events | not decoded |
| 14 | (PS2 TargetMode) | unknown | not decoded |
| 15 | GroundFlag | the entity ignores world collision | `IgnoresWorldCollision`: `EntityGrounding` keeps the reported height instead of the floor |
| 16 | KingFlag | the client waits at zone-in until the 0x00A `SendCount` number of "king" entities have arrived | not decoded |
| 17-31 | facetarget | target index the entity's head turns toward | not decoded. **Gap:** a server-driven head turn exists; `HeadLook` only turns heads for events |

## Flags1 (0x00D / 0x00E payload 28, packet 0x20)

LandSandBoat writes the entity's status (`data/enums/status.yaml`) as the whole low byte of this word for every entity, so status values land on the first bits (see [LandSandBoat status in Flags1](#landsandboat-status-in-flags1)). For NPCs and monsters it then writes its database `m_flags` as a u32 at packet 0x21, which covers bits 8-31 of this word and bits 0-7 of Flags2 (see [LandSandBoat database flags](#landsandboat-database-flags-and-name-visibility)).

| Bit | XiPackets name | Meaning (XiPackets; 0x00D unless noted) | GordianXI use |
|---|---|---|---|
| 0 | MonsterFlag | monster: yellow name, attackable (else NPC, green) | 0x00E `IsMonster` makes a 0-1023 index a monster, kept once seen (`EntityPacketModule`) |
| 1 | HideFlag | fully hidden and untargetable | `IsHidden`: not drawn unless the entity takes part in the running event (`WorldEntity.IsDrawn`), skipped by targeting (`TargetCycling`) and entity bump (`EntityBumpCollision`), no name plate |
| 2 | SleepFlag | the entity's scheduler is suspended; not rendered, not targetable | not decoded |
| 3 | (PS2 MonStat) | unused | |
| 4 | | unknown | |
| 5-7 | ChocoboIndex | special chocobo type | 0x00D decoded (`ChocoboIndex`), not used |
| 8 | CliPosInitFlag | position being initialised | not decoded |
| 9-10 | GraphSize | model size 0 small, 1 medium, 2 large | `GraphSize`: entity bump radius 0.35 / 0.7 / 1.4 (`EntityBumpCollision.RadiusOf`) |
| 11 | LfgFlag | 0x00D: seeking party. 0x00E: language-specific use | 0x00D `NamePlateFlags.SeekingParty` |
| 12 | AnonymousFlag | 0x00D: `/anon`. 0x00E: language-specific | 0x00D `NamePlateFlags.Anonymous` |
| 13 | YellFlag | called for help: orange name | `NamePlateFlags.CalledForHelp` (both packets) |
| 14 | AwayFlag | 0x00D: `/away` | 0x00D `NamePlateFlags.Away` |
| 15 | Gender | 0 female, 1 male; used for "sir / ma'am" text | 0x00D decoded (`Gender`), not used: `[his/her]` picks by the look's race byte ([ui/stock-ui.md](../ui/stock-ui.md#dialog-text-chunk-6)) |
| 16 | PlayOnelineFlag | 0x00D: PlayOnline icon. 0x00E: the HP bar is hidden when targeted | 0x00D `NamePlateFlags.PlayOnline`; 0x00E `NamePlateFlags.HealthBarHidden` → `NamePlateStyle.ShowsTargetHealthBar` (no HP gauge in the target window, #259) |
| 17 | LinkShellFlag | 0x00D: wears a linkshell (pearl icon) | 0x00D `NamePlateFlags.Linkshell` |
| 18 | LinkDeadFlag | 0x00D: disconnecting | 0x00D `NamePlateFlags.LinkDead` |
| 19 | TargetOffFlag | cannot be targeted by normal means | not decoded |
| 20 | TalkUcoffFlag | used by events | not decoded |
| 21-23 | (PS2 party leader, alliance leader, debug client) | unused | |
| 24-26 | GmLevel | 1-2 trial arrow, 3 PlayOnline icon, 4-7 GM icons | 0x00D `GmLevel` → `NamePlateStyle.Icon` |
| 27 | HackMove | unused | |
| 28 | (PS2 GMInvisFlag) | unknown | |
| 29 | InvisFlag | unknown; not drawn on the compass | `IsInvisible`: still drawn, but no name plate and skipped by targeting and entity bump |
| 30 | TurnFlag | ease the heading over time instead of snapping | not decoded (GordianXI always eases: `WorldEntity.UpdateHeading`) |
| 31 | BazaarFlag | bazaar icon | 0x00D `NamePlateFlags.Bazaar` |

## Flags2 (0x00D / 0x00E payload 32, packet 0x24)

| Bits | XiPackets name | Meaning | GordianXI use |
|---|---|---|---|
| 0-7 | r | 0x00D: linkshell colour red. 0x00E: Ballista name flags in PvP, else the mount id | 0x00D `LsColorR` (name plate pearl tint) |
| 8-15 | g | 0x00D: linkshell green. 0x00E: hitbox size x 10 | 0x00D `LsColorG`; the 0x00E hitbox is not decoded (#133 found it is not a living-mob flag) |
| 16-23 | b | 0x00D: linkshell blue. 0x00E: low 4 bits GEO Indi element, rest unknown | 0x00D `LsColorB` |
| 24 | PvPFlag | Gate Breach in PvP | not decoded |
| 25 | ShadowFlag | the shadow is hidden | not decoded |
| 26 | ShipStartMode | unused | |
| 27 | CharmFlag | charmed | 0x00D `IsCharmed`: for the local player, input is ignored and the server's positions are taken (`EntityPacketModule`, `PlayerLocomotionController`) |
| 28 | GmIconFlag | a GM hiding the GM icon | 0x00D `NamePlateFlags.GmIconHidden` |
| 29 | NamedFlag | the name takes no "The" | not decoded |
| 30 | SingleFlag | referred to in the plural | not decoded |
| 31 | AutoPartyFlag | 0x00D: auto-party icon. 0x00E: invisible and untargetable, auto-target still works | 0x00D `NamePlateFlags.AutoParty`; the 0x00E meaning is not applied |

LandSandBoat writes packet 0x27 (bits 24-31) from the NPC's or mob's name prefix and ORs in 0x08 (bit 27, CharmFlag) for a mob whose master is a player (`entity_update.cpp`).

## Flags3 (0x00D / 0x00E payload 36, packet 0x28)

| Bits | XiPackets name | Meaning | LandSandBoat writes | GordianXI use |
|---|---|---|---|---|
| 0 | TrustFlag | a Trust: clicking opens the target menu | 0x45 (bits 0, 2, 6) for every Trust | 0x00E `IsTrust` → `EntityType.Trust`; 0x00D decoded, not used |
| 1 | LfgMasterFlag | 0x00D: seeking a master party; 0x00E unused | | 0x00D `NamePlateFlags.SeekingMasterParty` |
| 2 | PetNewFlag | a pet being spawned (spawn animation) | part of the Trust 0x45 | not decoded |
| 3 | (PS2 PetKillFlag) | unknown | 0x08 for a mob at death animation with HP left | not decoded |
| 4 | MotStopFlag | freeze the motion (petrify, terror) | 0x10 under Terror | not decoded |
| 5 | CliPriorityFlag | always draw, past the client's entity limit | 0x20 for Pso'Xja mobs and `priorityRender` | not decoded |
| 6 | PetFlag | a pet | 0x40 for a triggerable NPC and for a mob at status Normal; part of the Trust 0x45 | 0x00D `IsPet` decoded, not used |
| 7 | OcclusionoffFlag | skip entity occlusion tests | | not decoded |
| 8-15 | BallistaTeam | Ballista team (nation flags, Wyverns, Griffons) | the entity's allegiance (packet 0x29) | not decoded |
| 16-18 | MonStat | sub-animation (2 or 3 bits depending on the entity) | `animationsub` (packet 0x2A) | 0x00E `AnimationSub` (payload 0x26 low 3 bits) → `NpcStanceResolver` |
| 19 | | language-specific | | |
| 20 | | unused | | |
| 21 | SilenceFlag | no footstep sounds | | 0x00D decodes this bit as `IsSneak`, not used. **Differs from XiPackets:** XiPackets names it SilenceFlag (footsteps), not sneak |
| 22 | | unknown | | |
| 23 | NewCharacterFlag | 0x00D: new player "?" | | 0x00D `NamePlateFlags.NewPlayer` |
| 24 | MentorFlag | 0x00D: mentor "M". 0x00E: A.M.A.N. Liaison "i" | name visibility 0x01 (icon) lands here | 0x00D `NamePlateFlags.Mentor`, 0x00E `NamePlateFlags.InfoNpc` |
| 25 | | alternative animations (for example how the entity sits) | | |
| 26 | | sub-animation is 3 bits; also used at spawn | | |
| 27 | | entity-dependent: untargetable, off the compass, name hidden | name visibility 0x08 (`hide_name`) lands here | not applied ([ui/stock-ui.md](../ui/stock-ui.md#name-plates-28) lists it as open) |
| 28 | | non-blocking: skipped by the client's actor contact check | | `IsNonBlocking`: skipped by `EntityBumpCollision` |
| 29 | | the HP bar and the overhead name are not drawn | name visibility 0x20 lands here (Port Jeuno's Synthesis Focuser II has name_vis 0x60, bits 29 and 30) | 0x00E `NamePlateFlags.NameHidden` → `NamePlateStyle.ShowsName` and `ShowsTargetHealthBar` (no name plate, no target HP gauge; retail check 2026-10-04, #259) |
| 30 | | off the compass | | |
| 31 | | half-transparent (alpha 0.5 by distance) | name visibility 0x80 (`ghost_phase`) lands here | not decoded |

**Differs from LandSandBoat:** LandSandBoat's `hide_name` name visibility bit reaches the client as Flags3 bit 27, while GordianXI hides names on bit 29 (the XiPackets "no HP bar, no name" bit). An NPC that LandSandBoat marks `hide_name` still shows its name in GordianXI. Whether retail hides the name for bit 27 on every entity type is open (XiPackets says it depends on the entity).

## 0x00D Flags4 and 0x00E SubKind

| Field | Bits | Meaning | GordianXI use | Source |
|---|---|---|---|---|
| 0x00D Flags4 (payload 0x2F) | 1 | TrialFlag (trial arrow) | `NamePlateFlags.Trial` | XiPackets 0x000D flags4_t |
| | 6 | JobMasterFlag (three stars) | `NamePlateFlags.JobMaster` | same |
| 0x00E u16 at payload 0x2C | 0-2 | SubKind (look size): 0 standard, 1 equipped, 2 door, 3 elevator, 4 ship, 5 unknown, 6 automaton, 7 chocobo | `EntitySubKind` → `EntityType` (see [enums.md](enums.md#entity-kinds-and-looks)) | XiPackets 0x000E, LandSandBoat `entity_update.h` |
| | 3-15 | Status | unused | XiPackets 0x000E |

## 0x037 flag words (local player)

The local player's own flags use other bit positions than 0x00D. GordianXI decodes these (`S2C_0x037_CharStatus`, payload offsets):

| Word | Bits | XiPackets name | GordianXI use |
|---|---|---|---|
| Flags0 (36) | 4 / 5 / 6 / 7 | LfgFlag / AnonymousFlag / CfhFlag / AwayFlag | `NamePlate` (seeking, anonymous, called for help, away) |
| | 16-23 | hpp | `Hpp` |
| | 24 / 25 / 26 | PlayOnelineFlag / LinkShellFlag / LinkDeadFlag | `NamePlate` |
| | 29-31 | GmLevel | `GmLevel` |
| Flags1 (40) | 0-11 | Speed | `Speed` (no longer copied into the local entity's current speed: [ui/stock-ui.md](../ui/stock-ui.md#live-hud-chunk-3)) |
| | 15 | InvisFlag | `IsInvisible` |
| | 17-24 | SpeedBase | `SpeedBase` |
| | 29 / 30 / 31 | BazaarFlag / CharmFlag / GmIconFlag | `HasBazaar`, `IsCharmed`, `NamePlate` |
| Flags2 (48) | 2 | AutoPartyFlag | `NamePlate` |
| | 3-18 | PetIndex | `PetActorIndex` |
| Flags3 (52) | 0 / 1 / 3 / 4 | LfgMasterFlag / TrialFlag / NewCharacterFlag / MentorFlag | `NamePlate` |
| Flags4 (0x54) | 7 | JobMasterFlag | `NamePlate` |

XiPackets' 0x0037 flags0 also starts with HideFlag (bit 0), SleepFlag (1) and GroundFlag (2); GordianXI does not read them for the local player.

## LandSandBoat status in Flags1

LandSandBoat writes its entity status as packet byte 0x20, the low byte of Flags1 (`entity_update.cpp`; a player-allegiance entity at status Update is sent as Normal). The values therefore decompose into the first Flags1 bits. The decomposition is *inference* from the two sources; the names are LandSandBoat's.

| Value | LandSandBoat name | Bits set | Effect in GordianXI |
|---|---|---|---|
| 0 | normal | none | NPC (no MonsterFlag) |
| 1 | update | 0 MonsterFlag | monster (mob allegiance entities spawn so) |
| 2 | disappear | 1 HideFlag | hidden (a dying mob fades to it, which also clears MonsterFlag; `EntityPacketModule` keeps a known monster a monster) |
| 3 | invisible | 0, 1 | hidden monster |
| 4 | status_4 | 2 SleepFlag | drawn (SleepFlag is not decoded) |
| 5 | status_5 | 0, 2 | drawn monster |
| 6 | cutscene_only | 1 HideFlag, 2 SleepFlag | hidden unless it takes part in the running event ([ui/stock-ui.md](../ui/stock-ui.md#dialog-text-chunk-6), cutscene-only NPCs) |
| 7 | status_7 | 0, 1, 2 | hidden monster |
| 18 | status_18 | 1, 4 | hidden |
| 20 | shutdown | 2, 4 | drawn |

The server status byte at payload 27 (packet 0x1F) is a different enumeration, LandSandBoat's `animation` (engaged, dead, elevator up / down...): see [enums.md](enums.md#server-animation-status).

## LandSandBoat database flags and name visibility

For NPCs and mobs LandSandBoat writes `m_flags` (`data/enums/entity_flags.yaml`) as a u32 at packet 0x21, so its bit k lands on Flags1 bit 8 + k; and `namevis` (`data/enums/name_vis.yaml`) as packet byte 0x2B, Flags3 bits 24-31. Comparing the landing bits with XiPackets' names (*inference*; the LandSandBoat file itself notes that bits 0x002-0x008 act differently per entity):

| LandSandBoat flag | Value | Lands on | XiPackets name there | Agree? |
|---|---|---|---|---|
| `entity_flags.info_icon` | 0x001 | Flags1 bit 8 | CliPosInitFlag | no |
| `entity_flags.hide_name` | 0x008 | Flags1 bit 11 | LfgFlag (language-specific on 0x00E) | no |
| `entity_flags.call_for_help` | 0x020 | Flags1 bit 13 | YellFlag | yes |
| `entity_flags.hide_model` | 0x080 | Flags1 bit 15 | Gender | no |
| `entity_flags.hide_hp` | 0x100 | Flags1 bit 16 | PlayOnelineFlag (hides the HP bar on 0x00E) | yes |
| `entity_flags.untargetable` | 0x800 | Flags1 bit 19 | TargetOffFlag | yes |
| `name_vis.icon` | 0x01 | Flags3 bit 24 | MentorFlag ("i" icon) | yes |
| `name_vis.hide_name` | 0x08 | Flags3 bit 27 | unknown_3_3 (name hidden on some entities) | yes |
| `name_vis.ghost_phase` | 0x80 | Flags3 bit 31 | unknown_3_7 (half-transparent) | yes |

**Beyond XiPackets / LandSandBoat:** neither source states where these database flags land; the table joins LandSandBoat's write offsets to XiPackets' bit names. The disagreeing rows are worth a retail check before GordianXI acts on them.

## Retail render flags (client memory, set by the event VM)

These are not wire fields. They are the retail client's per-entity `Render.Flags0`-`Flags7` words, which the event opcodes set and test (XiEvents OpCodes and "Event VM Functions.md"). GordianXI has no such words: a bit with a known effect has its own `WorldEntity` property, and the bits the event opcodes set without a known effect are kept together in `WorldEntity.EventRenderFlags` (`World/EventRenderFlags.cs`, #198), all cleared when the event ends. The "GordianXI" column says what stands in for a bit, if anything. "Own" is the event's own entity, "actor" an entity named by an operand.

### Render.Flags0

| Bit | Mask | Set / cleared by | Tested by | Reading | GordianXI |
|---|---|---|---|---|---|
| 0 | 0x1 | 0xB6 when a look sub-case changes the race | | look must be rebuilt (*inference*) | 0xB6 not run |
| 1 | 0x2 | 0xAB sub 1 sets, sub 2 clears (own) | | unknown | kept as `EventRenderFlags.Flags0Bit1` (#198), nothing reads it |
| 2 | 0x4 | 0xAB sub 3 sets, sub 4 clears (own; sub 4 waits until the entity is in an event status or not animating) | 0x4C / 0x4D (door open / close status) and 0x4F, 0x8E, 0x8F (event status 45 / 46) act only while clear; 0x7E chocobo cases; XiEventInit sets up the event status only while clear | the event status is locked (*inference*) | kept as `EventRenderFlags.Flags0Bit2` (#198); 0x4C / 0x4D set the door status only while it is clear (#200); sub 4 waits while the entity plays an event action. 0x4F / 0x8E / 0x8F are not run |
| 3 | 0x8 | 0xAB sub 5 sets, sub 6 clears | | unknown | kept as `EventRenderFlags.Flags0Bit3` (#198), nothing reads it |
| 6 | 0x40 | 0xAB sub 0x0B sets, 0x0C clears | | unknown | kept as `EventRenderFlags.Flags0Bit6` (#198), nothing reads it |
| 7 | 0x80 | | 0x27-0x2A requests (both entities), 0x1E / 0x4A / 0x4B / 0x3A / 0x3B / 0x65 (use the event position when set, else the world position), 0x80, 0xC1, 0x5B | the entity takes part in the event (has an event object) (*inference*) | `EventScene.FindActor` / `WorldEntity.IsInEvent`: `EventVm.TryGetActorPosition` takes the event position of a participant, else the world's |
| 8 | 0x100 | set on each entity InitEvent2 finds for the event | | joined the event | the scene's participant list (`EventScene`) |
| 9 | 0x200 | | most actor opcodes: 0x23, 0x2C, 0x2D, 0x45, 0x46 (player), 0x50-0x55, 0x5B, 0x5E, 0x6B, 0x6C, 0x6E, 0x73, 0x76, 0x79, 0x7B-0x7E, 0x80, 0x81, 0x86, 0x95, 0x99, 0xAD, 0xB6, 0xC1, 0xD3; InitEvent2 fails without it | the actor's model is ready (the reading in [world/entities-and-animation.md](../world/entities-and-animation.md)) | no flag; how each opcode treats a model not yet loaded is not checked |
| 10 | 0x400 | | 0x7E sub 2, with an attachment present | mount or chocobo attached (*inference*) | not run |
| 14, 15 | 0x4000, 0x8000 | | InitEvent2 skips its locked-status checks when either is set | unknown | |
| 17 | 0x20000 | 0x22 (own), 0x4E (actor), 0x90 (own, also Flags1 bit 12) | the renderer | event hide | `WorldEntity.IsEventHidden` from 0x22 / 0x4E / 0x90 (`IsDrawn`) |
| 19 | 0x80000 | 0x2F (`op value actor`) | | unknown; the scripts pair it with the 0x4E hide | stepped over without a diagnostic (`EventVm`) |
| 20 | 0x100000 | | 0x36, 0x37, 0x1F, 0x31, 0xBA: the placement is put on the floor (VCalibrate) only while bits 20 and 21 and Flags2 bit 14 are all clear | keep the scripted height (*inference*) | |
| 21 | 0x200000 | 0x33 (own), 0x59 sub 5 (actor) | same as bit 20 | keep the scripted height (*inference*) | 0x33 and 0x59 sub 5 run: `WorldEntity.KeepsEventHeight` (held until the entity arrives, cleared at the event's end), and `EntityRenderer` then draws the event pose at its placed height (#192, Port Jeuno 324's sky marker). **Beyond XiEvents:** this is the switch between floor-snapped and floating event placements; without the bit GordianXI draws event poses on the floor below (`EntityGrounding.GetDisplayHeight`, up to `EventStepUpHeight`); only the head look reads the placed height of a target more than 2 yalms above its drawn place (`EntityRenderer`, see [world/entities-and-animation.md](../world/entities-and-animation.md)). Which scripts set the bit is not checked |
| 30 | 0x40000000 | 0xB6 subs 0x0D / 0x0E (set for some look kinds, bit 31 cleared at the same time) | | unknown | not run |

### Render.Flags1

| Bit | Mask | Set / cleared by | Tested by | Reading | GordianXI |
|---|---|---|---|---|---|
| 12 | 0x1000 | 0x90 | InitEvent2 asks the server for the entity again (C2S 0x016) while set | entity data must be re-requested (*inference*) | not kept (0x90 runs only its hide part); GordianXI requests missing participants at the start (`EventDialogController`) |
| 17 | 0x20000 | the event walks 0x1F, 0x31, 0x5A when they move the entity; cleared before each request runs | | moved by the event this tick (*inference*) | |
| 25 | | | entity facts 0x7F0B / 0x7F8B return it (XiEvents writes the word as `Flags01`; which word is meant is not clear) | unknown | `IEventVmHost.GetEntityValue`; value not checked |
| 29 | 0x20000000 | 0x5F subs 0 / 1 (the sub-case is the value) | | unknown | not run |
| 30 | 0x40000000 | 0x60 subs 0 / 1 | | unknown | not run |
| 31 | 0x80000000 | 0x74 | | unknown | not run |

### Render.Flags2

| Bit | Mask | Set / cleared by | Tested by | GordianXI |
|---|---|---|---|---|
| 0 | 0x1 | 0x61 | | not run |
| 1 | 0x2 | 0xAB sub 8 sets, sub 7 clears | | kept as `EventRenderFlags.Flags2Bit1` (#198), nothing reads it |
| 4 | 0x10 | 0xB6 sub 0x13 sets / 0x12 clears (own); 0x15 sets / 0x14 clears (actor) | | not run |
| 14 | 0x4000 | (no opcode file sets it) | placement floor snap, as Flags0 bit 20 | |
| 17 | 0x20000 | 0x7C (actor with a ready model; operand non-zero sets, zero clears) | | stepped over silently. **Beyond XiEvents:** 55% of the events that use 0x81 (blink) also use 0x7C, and short talk events clear both before a facial gesture and set both after it (Cacaroon, Aht Urhgan Whitegate event 3036), so the bit may be another face switch, perhaps a face switch (*inference*, #198, #217) |
| 24 | 0x1000000 | 0xAB sub 0x12 sets, 0x13 clears | | kept as `EventRenderFlags.Flags2Bit24` (#198), nothing reads it |

### Render.Flags3

| Bit | Mask | Set / cleared by | Tested by | Reading | GordianXI |
|---|---|---|---|---|---|
| 0 | 0x1 | 0x84 sets | | unknown | not run |
| 1 | 0x2 | turns | 0x70 (yields while set, else cancels the movement), 0x76 (yields while set) | turning | `EventScene.StartTurn` / `IsTurning`: a turn counts as running for the viewport ease's time, `ln(angle / 0.05) / 8` s |
| 3 | 0x8 | 0x86 (actor with a ready model) | | unknown | not run |
| 8-9 | 0x300 | lookatone (0x1E, 0x4A, 0x79 sub 0 / 1) sets a look mode; 0x79 sub 2 sets mode 2; 0x7B clears both bits; XiEventInit adjusts the word | | look mode | `WorldEntity.EventLook` (target or fixed axis) and `HeadLook`; 0x7B clears it |
| 11 | 0x800 | 0xA5 | | unknown | not run |
| 12 | 0x1000 | 0xC0 (value from a work value) | | unknown; set by cutscene-only story actors and summons on themselves at an event's start (674 entries are only `C0 value`; [events/opcodes.md](../events/opcodes.md#0xc0)) | kept as `EventRenderFlags.Flags3Bit12` (#198), nothing reads it; #217 |
| 16 | 0x10000 | 0x92 (`op value actor`) | | no name plate (*inference*, #191: Port Jeuno 324 sets it on every NPC it places and on Joachim but not on the player, and the maintainer's retail recording shows only the player's plate; the Southern San d'Oria intro never sets it and shows every plate). 8,288 of the 8,349 retail events that use 0x92 set it and 412 clear it; most leave it to the event's end | `WorldEntity.HidesEventName` → `NamePlateStyle.ShowsName`; cleared when the event ends. **Beyond XiEvents:** the name plate reading |
| 17 | 0x20000 | 0x94 (`op value actor`) | | unknown; Port Jeuno 324 sets it on the player too, whose plate stays, so it is not the plate. 14,815 retail uses set it, 140 clear it, mostly beside 0x92 at the event's start | kept as `EventRenderFlags.Flags3Bit17` (#198), nothing reads it; #217 |
| 19 | 0x80000 | 0x95 sets (sets the entity up as an event NPC), 0x96 clears | | event-based NPC | not run |
| 20-21 | 0x300000 | 0x95 (a 2-bit parameter) | | unknown | not run |
| 26 | 0x4000000 | 0xA4 | | unknown | not run |

### Render.Flags4 to Flags7

| Word | Bit | Set / cleared by | GordianXI |
|---|---|---|---|
| Flags4 | 1 | 0xAB sub 0x0D sets, 0x0E clears | kept as `EventRenderFlags.Flags4Bit1` (#198), nothing reads it |
| Flags4 | 17 | 0xB6 look sub-cases set it after changing a look field | not run |
| Flags5 | (unspecified) | 0x95's attachment clean-up | not run |
| Flags6 | 31 | 0xAC sub 2 sets, sub 3 clears; while clear and the actor is missing, the client asks for it (C2S 0x016) and yields | not run |
| Flags7 | 0-1 | 0xAC sub 4 ORs in a work value's low 2 bits | not run |
| Flags7 | 19 | 0xAB subs 0x19 sets / 0x1A clears (own), 0x1B sets / 0x1C clears (actor) | kept as `EventRenderFlags.Flags7Bit19` (#198), nothing reads it |

Not a render flag but in the same family: 0x81 (`op value actor`) sets or clears the actor's blink switch (`is_blinkeye` on its skeleton actor; any non-zero value turns it on). GordianXI keeps a zero as `EventRenderFlags.NoBlink` and `EntityRenderer` then starts no blink until 0x81 turns it on or the event ends (#198). Whether retail keeps the switch after the event is open ([events/opcodes.md](../events/opcodes.md#0x81)). Open: [#217](https://github.com/jimmy58663/GordianXI/issues/217).

## Event opcodes that only change these bits

Every opcode below changes only render-flag bits (or the blink switch) in retail, per its XiEvents file. "Diagnostic" means `EventVm` steps over it by its table length and logs `Skipped opcode` through `IEventVmHost.OnSkippedOpcode` (Debug level); "silent" means a dedicated case steps over it without a log line.

| Opcode | Retail effect | GordianXI |
|---|---|---|
| 0x2F | Flags0 bit 19 on an actor | silent |
| 0x33 | Flags0 bit 21 on the own entity (no floor snap) | runs: `WorldEntity.KeepsEventHeight` (#192) |
| 0x4C / 0x4D | door status 8 / 9 unless Flags0 bit 2 is set | runs: `WorldEntity.EventStatus` (#200), which opens and closes the door (#15) |
| 0x4F | event status unless Flags0 bit 2 is set | diagnostic |
| 0x59 sub 5 | Flags0 bit 21 on an actor | runs: `WorldEntity.KeepsEventHeight` (#192; subs 0-4 and 6 run too: turn speeds, walk speed, emote wait, [#197](https://github.com/jimmy58663/GordianXI/issues/197)) |
| 0x5F subs 0 / 1 | Flags1 bit 29 | diagnostic |
| 0x60 subs 0 / 1 | Flags1 bit 30 | diagnostic |
| 0x61 | Flags2 bit 0 | diagnostic |
| 0x74 | Flags1 bit 31 | diagnostic |
| 0x7C | Flags2 bit 17 on an actor | silent |
| 0x81 | blink on / off | runs: `EventRenderFlags.NoBlink` pauses `FaceMotion`'s blink |
| 0x84 | Flags3 bit 0 | diagnostic |
| 0x86 | Flags3 bit 3 on an actor | diagnostic |
| 0x8E / 0x8F | event status 45 / 46 unless Flags0 bit 2 is set | diagnostic |
| 0x90 | event hide (Flags0 bit 17) and Flags1 bit 12 on the own entity | runs the hide, as 0x22 01 (`IsEventHidden`); bit 12 not kept |
| 0x92 | Flags3 bit 16 on an actor: no name plate (#191) | runs: `WorldEntity.HidesEventName` (Northern San d'Oria event 878: seven NPC blocks only toggle it, [ui/stock-ui.md](../ui/stock-ui.md#dialog-text-chunk-6)) |
| 0x94 | Flags3 bit 17 on an actor | runs: kept, effect unknown |
| 0x95 / 0x96 | event NPC setup / tear-down (Flags3 bits 19-21, attachments) | diagnostic |
| 0xA4 / 0xA5 | Flags3 bit 26 / bit 11 | diagnostic |
| 0xAB | many sub-cases over Flags0, Flags2, Flags4, Flags7 (tables above) | partial: the entity bits are kept (effect unknown); the client-wide subs are stepped silently |
| 0xAC subs 2-4 | Flags6 bit 31, Flags7 bits 0-1 (subs 0 / 1 set server and event status) | diagnostic |
| 0xC0 | Flags3 bit 12 | runs: kept, effect unknown |

## Other GordianXI flag words

| Word | Bits | Where |
|---|---|---|
| `NamePlateFlags` | GordianXI's own union of the name plate bits from 0x00D, 0x00E and 0x037 (bit positions are internal, not wire) | `World/NamePlateFlags.cs`, read by `NamePlateStyle` |
| `CutsceneFlags` | the event mode word of S2C 0x032-0x034 and 0x00A | [enums.md](enums.md#cutscene-flags-event-mode) |
| `CollisionLayers` | ground, walls, entities toggles | `CollisionSettings` ([world/collision-and-physics.md](../world/collision-and-physics.md)) |
| `FeatureRestrictions` | the server kill-switch word (movement, wall / entity collision overrides, knockback override bit 8...) | `Config/SessionConfig.cs` (see `AGENTS.md`) |
