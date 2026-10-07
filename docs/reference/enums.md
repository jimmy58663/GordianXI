# Wire Enums

> Scope: the numeric ids that travel on the wire or in retail data and that more than one GordianXI subsystem reads: emotes, action kinds and action results, cutscene mode flags, party kinds, weather, the two different "status" enumerations, sub-animation values, entity kinds, position modes, races and jobs. Each table gives the value, its name, the reference it comes from and the GordianXI type that holds it; every type was checked in `src/`. Flag words of the entity updates are in [entity-flags.md](entity-flags.md); units and clocks in [conventions.md](conventions.md). Date of this pass: 2026-10-01.

## Emotes (C2S 0x05D `Number`, event opcode 0x6E)

GordianXI type: `EmoteId` (`Network/Packets/CombatPackets.cs`), written by `CombatPacketBuilder.BuildEmoteRequest` (payload 6 `Number`, 7 `Mode`, 8 `Param`). Ids from XiPackets `world/client/0x005D` and LandSandBoat `src/map/enums/emote.h` (the two agree on every id below). Before [#178](https://github.com/jimmy58663/GordianXI/issues/178) the client numbered them from its own list.

`Mode`: 0 text and motion, 1 text only, 2 motion only (LandSandBoat `EmoteMode`; GordianXI `EmoteMode`, which the S2C 0x05A echo also carries). There is no "none" id. The echo's log line is the emote table's message `2 * id` (with a target) or `2 * id + 1` (file id 7025, [dat-files.md](../world/dat-files.md#client-message-tables)).

The motion slot is where the race's emote motion DATs keep the emote (`EmoteMotion.Slot`; file n / 8, routine `em0(n % 8)`), read from the Hume male DATs on 2026-10-01 ([world/entities-and-animation.md](../world/entities-and-animation.md)). **Beyond XiPackets / LandSandBoat:** neither gives the id-to-slot mapping; it is slot = id + 2 from Kneel on, with Point and Bow swapped and Salute taking one of three slots.

| Id | Name | Motion slot | Notes |
|---|---|---|---|
| 0 | Point | 1 | |
| 1 | Bow | 0 | |
| 2 | Salute | 2-4 | the variant (high byte of 0x6E's value, or `Param`) is presumed to pick the nation's salute; not verified |
| 3 | Kneel | 5 | |
| 4 | Laugh | 6 | |
| 5 | Cry | 7 | |
| 6 | No | 8 | Northern San d'Oria 0x010E70C5 after "But, I ramble. Many pardons!" |
| 7 | Yes | 9 | |
| 8 | Wave | 10 | |
| 9 | Goodbye | 11 | |
| 10 | Welcome | 12 | |
| 11 | Joy | 13 | |
| 12 | Cheer | 14 | |
| 13 | Clap | 15 | Rahal in the Southern San d'Oria intro |
| 14 | Praise | 16 | |
| 15 | Smile | 17 | face only: the slot plays nothing |
| 16 | Poke | 18 | |
| 17 | Slap | 19 | |
| 18 | Stagger | 20 | |
| 19 | Sigh | 21 | |
| 20 | Comfort | 22 | |
| 21 | Surprised | 23 | |
| 22 | Amazed | 24 | |
| 23 | Stare | 25 | slot plays nothing |
| 24 | Blush | 26 | |
| 25 | Angry | 27 | |
| 26 | Disgusted | 28 | |
| 27 | Muted | 29 | slot plays nothing |
| 28 | Doze | 30 | slot plays nothing |
| 29 | Panic | 31 | |
| 30 | Grin | 32 | slot plays nothing |
| 31 | Dance | 33 | slot plays nothing |
| 32 | Think | 34 | |
| 33 | Fume | 35 | |
| 34 | Doubt | 36 | slot plays nothing |
| 35 | Sulk | 37 | slot plays nothing |
| 36 | Psych | 38 | |
| 37 | Huh | 39 | |
| 38 | Shocked | 40 | |
| 40-42 | Logging, Excavation, Harvesting | (42-44) | HELM motions; the server sends them, the client never requests them |
| 43 | Hurray | (45) | sent with `Param` 1 |
| 44 | Toss | (46) | |
| 65-68 | Dance1-Dance4 | none | `Param` 2-5; not routed by `ChatCommandRouter` |
| 73 | Bell | none | `Param` is the note; not routed |
| 74 | Job | none | `/jobemote`, `Param` = job id + 30; not routed |
| 96 | Aim | none | `Param` 53; not routed |

Slots in parentheses are what `EmoteMotion.Slot` computes (id + 2 for ids 3-45); the slot survey lists clips only up to slot 40, so these are not checked against the DATs. `/sit` is not an emote (C2S 0x0EA, [#111](https://github.com/jimmy58663/GordianXI/issues/111)); the S2C 0x05A echo ([#110](https://github.com/jimmy58663/GordianXI/issues/110)) plays the same slots on the caster and prints the emote table's line.

## Action kinds (C2S 0x01A `ActionID`)

GordianXI type: `CliActionId`; every request goes through `CombatPacketBuilder.BuildAction` and can be sent with `CombatPacketModule.RequestActionAsync`. Values from LandSandBoat `src/map/packets/c2s/0x01a_action.h` and XiPackets `world/client/0x001A` ([network/session-and-packets.md](../network/session-and-packets.md#c2s-0x01a-action-kinds)). None of the kinds has been checked against a retail capture.

| Value | Name | GordianXI caller |
|---|---|---|
| 0x00 | Talk | Confirm on an NPC (`PlayerActionService.TalkToTargetAsync`); `/refa` releases Trusts with it |
| 0x02 | Attack | `/attack`, command menu Attack |
| 0x03 | CastMagic | `/magic` (adds the ground-target offset) |
| 0x04 | AttackOff | `/attackoff` |
| 0x05 | Help | `/callforhelp` (`/cfh`) |
| 0x07 | Weaponskill | `/ws` |
| 0x09 | JobAbility | `/ja` |
| 0x0B | HomepointMenu | the dead window's Back to Home Point, `/homepoint`; `ActionBuf[0]` = `HomepointMenuChoice` 0 home point, 1 / 2 Monstrosity Cancel / Retry ([#103](https://github.com/jimmy58663/GordianXI/issues/103)) |
| 0x0C | Assist | `/assist` |
| 0x0D | RaiseMenu | the Raise prompt, `/acceptraise [decline]`; `ActionBuf[0]` = `ReviveMenuAnswer` 0 accept, 1 decline (#103) |
| 0x0E | Fish | `/fish` |
| 0x0F | ChangeTarget | `/attack` on another target while engaged |
| 0x10 | Shoot | `/shoot` |
| 0x11 | ChocoboDig | `/dig` (then S2C 0x02F and C2S 0x063) |
| 0x12 | Dismount | builder only |
| 0x13 | TractorMenu | the Tractor prompt, `/accepttractor [decline]`; `ActionBuf[0]` = `ReviveMenuAnswer` 0 accept, 1 decline (#103) |
| 0x14 | SendResRdy | no named caller |
| 0x15 | Quarry | no named caller |
| 0x16 | Sprint | `/sprint` (LandSandBoat does nothing with it) |
| 0x17 | Scout | no named caller |
| 0x18 | Blockaid | `/blockaid [on|off]`; `ActionBuf[0]` = `BlockaidMode` 0 off, 1 on, 2 toggle |
| 0x19 | MonsterSkill | `/monsterskill` (`/ms`); the id is sent as given (retail sends the ability id less 1536) |
| 0x1A | Mount | builder only |

## Action results (S2C 0x028)

GordianXI types in `CombatPackets.cs`, values from LandSandBoat `src/map/enums/action/*.h`, read by `ActionPlaybackQueue` and `CombatLogFormatter`.

`ActionCategory` (`cmd_no`):

| Value | Name | Value | Name |
|---|---|---|---|
| 0 | None | 8 | MagicStart |
| 1 | BasicAttack | 9 | ItemStart |
| 2 | RangedFinish | 10 | AbilityStart |
| 3 | SkillFinish | 11 | MobSkillFinish |
| 4 | MagicFinish | 12 | RangedStart |
| 5 | ItemFinish | 13 | PetSkillFinish |
| 6 | AbilityFinish | 14 | Dancer |
| 7 | SkillStart | 15 | RuneFencer |

`ActionResolution` (`miss`): 0 Hit, 1 Miss, 2 Guard, 3 Parry, 4 Block. `ActionReactKind` (`react_kind`): 0 none, 1-10 the spikes (Blaze, Ice, Dread, Curse, Shock, Reprisal, Wind, Earth, Water, Death), 63 Counter. `ActionProcAddEffect` (`proc_kind`): 0 none, 1-8 elemental damage (Fire ... Dark), 9 Sleep ... 19 Death, 20 Shield, 21 HP drain, 22 MP drain. Result `scale`: bits 0-1 hit distortion (0, 0.25, 0.5, 1), bits 2-4 knockback level 1-7 (`ActionPlaybackQueue.DistortionOf`, `KnockbackLevelOf`).

## Cutscene flags (event mode)

The `Mode` of S2C 0x032 / 0x033 / 0x034 and the zone-in `EventMode` of S2C 0x00A. GordianXI type: `CutsceneFlags` (`Events/CutsceneFlags.cs`). Values from LandSandBoat `scripts/enum/cutscene_flag.lua`.

| Value | LandSandBoat name | Meaning | In `CutsceneFlags` | Read by GordianXI |
|---|---|---|---|---|
| 0x0001 | RESET_CAMERA | at the end, the player back at the server's position and the camera behind | `ResetCamera` | no |
| 0x0002 | NO_PCS | other players not in the event are not drawn | `NoPcs` | yes: `EventDialogController` hides them, late arrivals too |
| 0x0004 | SEND_POSITION | keep sending the player's position while the event moves it | `SendPosition` | no |
| 0x0008 | UNKNOWN_0008 | often set, unused by the client | absent | |
| 0x0010 | NO_NPCS | NPCs and monsters not in the event are not drawn | `NoNpcs` | yes, as NO_PCS |
| 0x0020 | NO_PARTICIPANT_ANIM | drop scheduler packets whose caster or target is in the event | `NoParticipantAnimation` | no |
| 0x0040 | NO_DIALOGUE | suppress the server's zone messages (TalkNum family) | `NoDialogue` | no |
| 0x0080 | OPENING_MODE | zone-in only: opening cutscene mode | `OpeningMode` | no |
| 0x0100 | NO_IDLE_WAIT | start at once without waiting for the actors to be idle | `NoIdleWait` | no |
| 0x0200 | KEEP_ACTOR_COLOR | do not reset actor colour at the start | absent | |
| 0x0400 | NO_BATTLE_ANIM | drop battle actions of event participants | absent | |
| 0x0800 | NO_MAGIC_ANIM | drop all scheduler packets during the event | absent | |
| 0x1000 | ALLOW_OWN_ACTION | let the player's own action animations play | absent | |
| 0x2000 | IGNORE_UNLOCK | ignore the server's event-unlock message | absent | |
| 0x4000 | UNUSED_4000 | never read by the client | absent | |
| 0x8000 | GROUND_SNAP_ON_END | put actors back on the terrain when the event ends | absent | |
| 0x10000 | HIDE_TARGET_WINDOW | no target window during the event | `HideTargetWindow` | no |

**Differs from LandSandBoat:** `CutsceneFlags` declares 10 of LandSandBoat's 17 bits. The new-character intros use RESET_CAMERA | NO_PCS | OPENING_MODE, the Windurst Waters and Woods intros add NO_NPCS (`CutsceneFlags` doc comment). The event's own mode mask set by opcode 0x38 (`EventScene.EventModeLocal`, retail `CliEventModeLocal`) is a separate word whose bits are not known ([ui/stock-ui.md](../ui/stock-ui.md#dialog-text-chunk-6)).

## Party kinds (C2S 0x06E, S2C 0x0DC)

GordianXI type: `PartyKind` (`Network/Packets/PartyPackets.cs`), from LandSandBoat `src/map/enums/party_kind.h` and XiPackets `world/client/0x006E`.

| Value | Name | Notes |
|---|---|---|
| 0 | Party | |
| 5 | Alliance | not 1: the server rejects any other value ([#98](https://github.com/jimmy58663/GordianXI/issues/98)) |

## Weather

Server weather numbers (S2C 0x00A login, S2C 0x057, event opcode 0x77) are held as `WorldState.WeatherId` and turned into the zone's weather directory by `VanaTime.GetWeatherId` / `VanaTime.WeatherNames`; `VanaTime.GetCanonicalWeatherCategory` folds a directory that a zone does not author into one of the four sky categories ([rendering/sky-and-weather.md](../rendering/sky-and-weather.md)). Names from LandSandBoat `data/enums/weather.yaml`.

| Id | LandSandBoat name | DAT directory | Sky fallback |
|---|---|---|---|
| 0 | none | `fine` | fine |
| 1 | sunshine | `suny` | suny |
| 2 | clouds | `clod` | clod |
| 3 | fog | `mist` | mist |
| 4 | hot_spell | `dryw` | suny |
| 5 | heat_wave | `heat` | suny |
| 6 | rain | `rain` | clod |
| 7 | squall | `squl` | clod |
| 8 | dust_storm | `dust` | clod |
| 9 | sand_storm | `sand` | clod |
| 10 | wind | `wind` | fine |
| 11 | gales | `stom` | clod |
| 12 | snow | `snow` | clod |
| 13 | blizzards | `bliz` | clod |
| 14 | thunder | `thdr` | clod |
| 15 | thunderstorms | `bolt` | clod |
| 16 | auroras | `aura` | fine |
| 17 | stellar_glare | `ligt` | fine |
| 18 | gloom | `fogd` | clod |
| 19 | darkness | `dark` | clod |

Ids above 19 fall back to `fine`. The fallback is used only when a zone authors no directory for the exact weather.

The client's English weather name table (`ROM/165/79`, `DMsgCategory.WeatherNames`, read for the forecast lines, [#125](https://github.com/jimmy58663/GordianXI/issues/125)) lists the same ids in order, each row a noun and an adjective: 0 "fine patches" / "fine", 1 "sunshine" / "sunny", 2 "clouds" / "cloudy", 3 "fog" / "foggy", 4 "hot spells" / "hot", 6 "rain" / "rainy", 10 "winds" / "windy" ... 19 "darkness" / "dark" (some adjectives repeat the noun: "sand storms", "gales", "thunderstorms", "auroras", "stellar glare"). **Differs from LandSandBoat / xi-tools:** they call id 0 none; the client's table names it fine weather, which fits GordianXI's `fine` directory for 0 (the Japanese table `ROM/165/65` has 40 rows, the 20 names followed by rows starting `fine`, `suny`). The weather forecast files ([events/vm.md](../events/vm.md#weather-forecast-0x72)) use ids 1-19 and 0xFF for an empty slot.

## Server animation status

Two LandSandBoat enumerations are both called "status"; they travel in different bytes.

**Animation status** (LandSandBoat `data/enums/animation.yaml`): the `server_status` byte of 0x00D / 0x00E (payload 27, packet 0x1F) and of 0x037 (payload 44). GordianXI keeps it as `WorldEntity.AnimationState` and reads these values:

| Value | LandSandBoat name | GordianXI use |
|---|---|---|
| 0 | none | |
| 1 | attack (engaged) | `PlayerActionService.StatusEngaged`: leaving it ends the local engagement (#67); `EntityRenderer` / `WorldEntity` treat 1 as engaged for the battle stance |
| 2 | despawn | `PlayerActionService.StatusDespawning`: the engaged target is dropped |
| 3 | death | `PlayerActionService.StatusDead`; `AnimationStateClassifier` plays death |
| 4 | event | |
| 5 | chocobo | |
| 6 | fishing | |
| 8 / 9 | open_door / close_door | `ZoneDoors.StatusOpen` / `StatusClosed`: `DoorAnimator` plays the door's routines and `ZoneDoors` blocks the doorway while not 8 (#15); events set them as `WorldEntity.EventStatus` (0x4C / 0x4D, #200) |
| 10 / 11 | elevator_up / elevator_down | `MovingPlatforms.AnimationUp` / `AnimationDown` pick the leg direction |
| 33 | healing | |
| 44 | synth | |
| 47 | sit | |
| 48 | ranged | |
| 63-73 | sitchair_0-10 | |
| 85 | mount | |
| 90 | trust (spawn-in) | |

Values not listed (the fishing family 38-62, numbered unknowns) are not read.

**Entity status** (LandSandBoat `data/enums/status.yaml`: 0 normal, 1 update, 2 disappear, 3 invisible, 4, 5, 6 cutscene_only, 7, 18, 20 shutdown) is the low byte of Flags1, so it shows up as MonsterFlag / HideFlag / SleepFlag bits: see [entity-flags.md](entity-flags.md#landsandboat-status-in-flags1). GordianXI has no enum for it.

## Sub-animation (MonStat)

0x00E Flags3 bits 16-18 (`S2C_0x00E_CharNpc.AnimationSub`), interpreted per monster family by `NpcStanceResolver.ResolveEffectiveStance`, which picks stance 0, 1 or 2 of the model's clip families. Family values from LandSandBoat family mixins (cited in the resolver).

| Family (detected by clip names) | Value → stance | Source |
|---|---|---|
| Uragnite (`shel`, `1tl`) | 4 out of shell → 0, 5 in shell → 1 | LandSandBoat `mixins/families/uragnite.lua` |
| Adamantoise / tortoise | 1 in shell → 1; 0 or 2 → 0 | LandSandBoat `Aspidochelone.lua` |
| Gargouille (`garg`) | 4 or 0 standing → 0, 5 flying → 1, 2 roosting → 2 | `mixins/families/gargouille.lua` |
| Imp (`imp`) | 4 or 0 horn intact → 0, 5 or 1 horn broken → 1 | `mixins/families/imp.lua` |
| Hpemde (`hebi`, or `2dl0` / `2tl0` with `sp20` / `sp30`) | 0 or 6 surfaced → 0, 3 open mouth → 1, 5 diving → 2 | `mixins/families/hpemde.lua` |
| Omega (`omeg`, or `1dl0` / `1lk0` / `1un0`) | 0 quadruped → 0, 1 biped → 1 | LandSandBoat Apollyon / Proto-Omega skill lists |
| Any model with mode-1 / mode-2 clips | 1 → 1, 2 → 2 | GordianXI convention, not from a reference |

## Entity kinds and looks

`EntitySubKind` (0x00E payload 0x2C bits 0-2, LandSandBoat `entity_update.h` look sizes, XiPackets 0x000E `SubKind`) and the `EntityType` GordianXI derives from it in `EntityPacketModule`:

| SubKind | Name | `EntityType` | Notes |
|---|---|---|---|
| 0 | Standard | Npc below index 1024, Monster at 1024 and up; MonsterFlag makes an index below 1024 a Monster, TrustFlag makes one at 1024 and up a Trust | model id at payload 0x2E (`GetModelId`) |
| 1 | Equipped | as 0 | 20-byte look (race, face, eight gear slots) |
| 2 | Door | Door | no model, not drawn |
| 3 | Elevator | Elevator | FourCC, leg start, travel time (`TryGetTransport`) |
| 4 | Ship | Ship | FourCC, leg start |
| 5 | Unknown5 | as 0 | simple model id |
| 6 | Automaton | Pet | simple model id |
| 7 | Chocobo | as 0 | full look |

`EntityType` itself (Player 0, Npc 1, Monster 2, Pet 3, Trust 4, Elevator 5, Ship 6, Door 7) is internal, not a wire value.

## Position modes (S2C 0x05B / 0x065 WPOS)

`PosMode` (`Network/Packets/LifecyclePackets.cs`), behaviour from XiPackets `world/server/0x005B`, `0x0065` ([world/collision-and-physics.md](../world/collision-and-physics.md), [#101](https://github.com/jimmy58663/GordianXI/issues/101)):

| Value | Name | GordianXI |
|---|---|---|
| 0x00 | Normal | places the entity |
| 0x01 | Event | places |
| 0x02 | Clear | clears flags only |
| 0x03 | PlaceAndDeletePop | places, then removes another entity (pop effect not played); the local player is only placed |
| 0x05 | Reset | places; also releases a 0x08 lock |
| 0x06 | PlaceAndDeleteMaterialize | as 0x03 |
| 0x07 | OpenIndoor | places; the indoor area is not modelled |
| 0x08 | Lock | locks movement until 0x09 / 0x05 or a zone change |
| 0x09 | Unlock | |
| 0x0A | Rotate | turns only |

## Logout states (S2C 0x00B)

`LogoutState`, from LandSandBoat `GP_GAME_LOGOUT_STATE`: 0 None, 1 Logout, 2 ZoneChange, 3 MyRoom, 4 Cancel, 5 PolExit, 6 JobExit, 7 PolExitMyRoom, 8 Timeout, 9 GmLogout, 10 End. The zone transition states 1-3 are the ones described in [network/session-and-packets.md](../network/session-and-packets.md#packet-engine-phase-3); LandSandBoat sends state 1 for both Log Out and Shut Down (`charutils::SendDisconnect`), so the client tells them apart by its own C2S 0x0E7 kind (1 logout, 3 shutdown). Every state but 0 and 4 suspends C2S 0x015; 1, 5 and 10 end the session. How retail handles the rest is not checked (XiPackets: the client checks only 1, 4 and 8).

## Races, look slots and jobs

`CharacterRace` (the look's race byte; `Resources/Tables/CharacterEquipmentResolver.cs`): 1 Hume male, 2 Hume female, 3 Elvaan male, 4 Elvaan female, 5 Tarutaru male, 6 Tarutaru female, 7 Mithra, 8 Galka; NPC-only children 29 Mithra kitten, 30 girl, 31 boy. Female races (2, 4, 6, 7) choose the `[his/her]` words of event text.

`CharacterSlot` (grap table index, 0x00D / 0x051 / 0x00A): 0 race and face, 1 head, 2 body, 3 hands, 4 legs, 5 feet, 6 main, 7 sub, 8 ranged.

`JobId` (`EntityPackets.cs`): 0 none, 1 WAR, 2 MNK, 3 WHM, 4 BLM, 5 RDM, 6 THF, 7 PLD, 8 DRK, 9 BST, 10 BRD, 11 RNG, 12 SAM, 13 NIN, 14 DRG, 15 SMN, 16 BLU, 17 COR, 18 PUP, 19 DNC, 20 SCH, 21 GEO, 22 RUN.
