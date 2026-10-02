# Event schedulers and motions

The named resources an event script refers to: the scheduler tasks (camera shots, fades, effects) that 0x45 and its family start, the scene resource DATs they live in, the motion banks and packages of 0x5B / 0x66, the emote slots of 0x6E and the clip names a script or the client plays. How the VM runs these opcodes is in [vm.md](vm.md#cutscene-schedulers); each opcode's length and status is in [opcodes.md](opcodes.md); the models and clips themselves are in [world/entities-and-animation.md](../world/entities-and-animation.md).

Column key for every table: **name / id** | **what it is** | **where it lives** (file id; ROM path where it helps) | **used by** (opcode) | **GordianXI** (type, status) | **source**.

**Corpus counts** ("uses") come from a one-off scan of the retail install on 2026-10-01: the event DATs of 299 zones (209,144 entries, the same corpus as `EventOpcodeCorpusTests`), each entry stepped with `EventOpcodeTable`'s lengths. Only operands that read the block's immediate data are counted; a resource number held in a work value is listed as "from a work value". File ids map to paths through the root and `ROMn` VTABLE / FTABLE pairs.

## Scheduler opcodes and their file bands

Every member reads the same operands: `op p:work actor:u32 target:u32 routine:FourCC` (+ `value:work` on the start opcode). The start opcode is 17 bytes, wait and stop 15. The task's file is a fixed base plus `p`; only the 0x45 family remaps `p` (see the next table).

| name / id | what it is | where it lives | used by | GordianXI | source |
|---|---|---|---|---|---|
| main scene tasks | camera shots, fades, overlays of a cutscene | `30704 + remap(p)` | 0x45 start, 0x55 wait, 0x52 stop | run: `EventVm.ExecStartTask` / `ExecWaitTask` / `ExecEndTask`, `EventSceneResource.GetFileId`; 161,392 uses, 782 files, 290 zones | XiEvents OpCodes/0x0045, 0x0052, 0x0055 |
| 5012 band | short tasks, mostly `main` (files 5013, 5014), `kone` / `kon1` (5034) | `5012 + p` | 0x62 start, 0xA0 wait; XiEvents gives 0xA1 (stop) the 30704 base, not 5012 | run (`EventSceneResource.GetBandFileId`, #192, [#199](https://github.com/jimmy58663/GordianXI/issues/199)); 0xA1 stepped (`EventOpcodeTable`); 4,597 uses, 91 files, 245 zones | XiEvents OpCodes/0x0062, 0x00A0, 0x00A1 |
| 51183 band | effect tasks: `main`, `kill`, `str0`, `stop`; the eyelid opening `bl00` / `open` / `clos` / `kill` (51402) and the sky flash `mai1` / `mai2` / `stop` (51327) of Port Jeuno event 324 | `51183 + p` | 0x9F start, 0xA2 wait, 0xA3 stop | run: `EventSceneResource.GetSecondFileId`, effects by `SceneEffectPlayer` ([#192](https://github.com/jimmy58663/GordianXI/issues/192)); 5,348 uses, 245 files, 222 zones | XiEvents OpCodes/0x009F, 0x00A2, 0x00A3 |
| 56685 band | effect tasks (`in00`, `mai1`, `stp1`...) | `56685 + p` | 0xBB, 0xBC wait, 0xBD stop | run (`EventSceneResource.GetBandFileId`, #192, [#199](https://github.com/jimmy58663/GordianXI/issues/199)); 4,115 uses | XiEvents OpCodes/0x00BB-0x00BD |
| 67355 band | effect tasks (`s000`, `kil0`, `b000`...) | `67355 + p` | 0xC5, 0xC6 wait, 0xC7 stop | run (`EventSceneResource.GetBandFileId`, #192, [#199](https://github.com/jimmy58663/GordianXI/issues/199)); 2,597 uses | XiEvents OpCodes/0x00C5-0x00C7 |
| 70435 band | effect tasks (`main`...) | `70435 + p` | 0xCD, 0xCE wait, 0xCF stop | run (`EventSceneResource.GetBandFileId`, #192, [#199](https://github.com/jimmy58663/GordianXI/issues/199)); 3,789 uses | XiEvents OpCodes/0x00CD-0x00CF |
| 70691 band | effect tasks (`s000`, `gnh1`, `star`...) | `70691 + p` | 0xD0, 0xD1 wait, 0xD2 stop | run (`EventSceneResource.GetBandFileId`, #192, [#199](https://github.com/jimmy58663/GordianXI/issues/199)); 2,523 uses | XiEvents OpCodes/0x00D0-0x00D2 |
| 102449 band | effect tasks (`s001`, `kl01`, `tama`...) | `102449 + p` | 0xD5, 0xD6 wait, 0xD7 stop | run (`EventSceneResource.GetBandFileId`, #192, [#199](https://github.com/jimmy58663/GordianXI/issues/199)); 269 uses | XiEvents OpCodes/0x00D5-0x00D7 |
| zone schedulers | zone-wide tasks | not mapped | 0x2D / 0x51 / 0x54 | stepped over | XiEvents OpCodes/0x002D, 0x0051, 0x0054 |
| server schedulers | actor, map and magic animation scripts sent by the server | not mapped | S2C 0x038 / 0x039 / 0x03A | not decoded, [#109](https://github.com/jimmy58663/GordianXI/issues/109) | XiPackets |

**Beyond XiEvents:** the use counts, and that 0xD5-0xD7 (base 102449) belong to the family; the task names per band were read from the retail scripts.

### 0x45 remap of `p`

| name / id | what it is | where it lives | used by | GordianXI | source |
|---|---|---|---|---|---|
| `p` 0-299 | retail scene resources | 30704-31003 | 0x45 / 0x52 / 0x55 | `EventSceneResource.GetFileId`; 86,565 uses | XiEvents OpCodes/0x0045 (`FUNC_DatIdHelper`), xi-tools docs/events/camera_scene_ids.md |
| `p` 300-599 | scene resources | `56641 + p` = 56941-57240 | same | same; 39,059 uses | same |
| `p` 600 and up | scene resources | `70347 + p` = 70947 and up | same | same; 35,660 uses | same |
| `p` from a work value | resolved at run time | - | same | resolved by `GetWork`; 108 uses | - |

**Differs from xi-tools:** camera_scene_ids.md marks the 600+ band as crashing the client. That is about custom files registered there: retail scripts use the band 35,660 times.

## Shared scene resources

Two files hold the tasks most scripts share. Frame counts are 60 Hz frames (routine header +0x1C); colours are B, G, R, A bytes, 0x80 = unchanged. Command meanings: [vm.md](vm.md#cutscene-schedulers).

**30904** (`p` 200, ROM/62/110.DAT): 56 routines, 28 Routes.

| name / id | what it is | where it lives | used by | GordianXI | source |
|---|---|---|---|---|---|
| `fdo0` / `fdo1` / `fdo2` | 3D scene fade to black over 30 / 60 / 120 frames (op 0x0F to 00 00 00) | 30904 | 0x45 (1,680 / 11,672 / 745 uses) | played: `SceneCommandKind.SceneFade`, `EventPresentation` | retail DAT, 2026-09-30 / 10-01 |
| `fdi0` / `fdi1` / `fdi2` | 3D scene fade back to as drawn (0x0F to 80 80 80), 30 / 60 / 120 frames | 30904 | 0x45 (1,733 / 9,669 / 5,058) | played | same |
| `fao1` / `fao2` | 2D interface fade out (op 0x51 to 00 00 00), 60 / 120 frames | 30904 | 0x45 (9 / 0) | played: `SceneCommandKind.InterfaceFade` scales `StockUiRenderer.Opacity` | same |
| `fai1` / `fai2` | 2D interface fade back (0x51 to 80 80 80), 60 / 120 frames | 30904 | 0x45 (9 / 0) | played | same |
| `fdos` / `fdis` | despite the `fd` prefix, interface fades (0x51) out and back over 60 frames | 30904 | 0x45 (654 each) | played as interface fades | same |
| `fdol` / `fdil` | interface fade to black / to white (FF FF FF) over 300 frames | 30904 | not used by any script | would play as interface fades | same |
| `fdof` / `fdif` / `fdop` / `fdip` | named by 654 script uses each (with `fdos` / `fdis`), but not in the file | none | 0x45 | task lasts 0 frames ("no such routine" debug line) | retail DAT and scripts, 2026-10-01 |
| `ovl1` / `ovl2` | cross-dissolve between shots (op 0x10), 60 / 120 frames | 30904 (30812 has `olp1` / `olp2`, 60 / 90) | 0x45 (2,255 / 317) | played: the frame before is held and fades out (`ScenePostProcess`, #205) | same |
| `blon` / `blof` | blur on / off (op 0x0E: A0 A0 A0 30 / 0.98, then 80 80 80 00 / 1.0), 15 frames | 30904 (also 30812, 30905, 57139, 70973) | 0x45 (2,308 / 1,304) | played as the feedback motion blur (*inferred*, #205) | same |
| `c00i`-`c0bi` | twelve-direction orbit shots, 60 frames; Routes `c00i`-`c0bi` and `c00c`-`c0bc` with flag bit 0 (placed at the task's first actor). `c05i` plays Route `c05c` | 30904 | 0x45 (`c00i` 59, `c05i` 9) | played (`CameraRoute.IsActorRelative`) | same |
| `ati0` / `ati1` / `ato0` / `ato1` | short camera moves on Routes of the same names, with a blur | 30904 | 0x45 (1-2 uses) | played | same |
| `0dkn` / `1dkn` | blur pulse (0x0E to 2D / 0.92 at once, back over 40-50 frames) and two other commands (0x02, 0x1E) | 30904 | 0x45 (23 for `0dkn`) | played (0x02 / 0x1E when the file has the generator) | same |
| `fan1`-`fan5`, `hrp1` / `hrp2`, `s021`-`s038`, `6109` / `6121` / `8160` | single op 0x60 command, 0 frames (meaning not known) | 30904 | 0x45 (`fan1` 22, `hrp1` 1) | not played; task ends at once | same |

**30905** (`p` 201, ROM/62/111.DAT): 9 routines, no Routes.

| name / id | what it is | where it lives | used by | GordianXI | source |
|---|---|---|---|---|---|
| `who0`-`who3` | fade to white (op 0x72 to FF FF FF), 15 / 60 / 120 / 180 frames; `who0` / `who1` also name a sound (op 0x60 `8159` / `8158`) | 30905 (`who1` also 57004, 57163) | 0x45 (`who#` 3,786) | played: `SceneCommandKind.ScreenFlash`, white added over the scene (`EventPresentation.SceneFlash`); the sound is not ([#167](https://github.com/jimmy58663/GordianXI/issues/167)) | retail DAT, 2026-10-01; #192 |
| `whi0` / `whi1` | back from white (0x72 to 00 00 00), 15 / 60 frames | 30905 | 0x45 (`whi#` 2,987) | played | same |
| `qstc` | single op 0x60 command; xi-tools reads it as the quest-complete cue that closes a quest scene | 30905 | 0x45 (1,656) | not played | xi-tools docs/events/maat_93_study.md |
| `chco` | op 0x60, 40 frames | 30905 | 0x45 (68) | not played | retail DAT |
| `blon` | op 0x60 only (unlike the 30904 `blon`) | 30905 | 0x45 | not played | retail DAT |

**Port Jeuno event 324** (Abyssea "A Journey Begins"; read 2026-10-02 for [#192](https://github.com/jimmy58663/GordianXI/issues/192), timings from the maintainer's retail recording of 2026-10-02). Generators named in the routines live in the same file.

| name / id | what it is | where it lives | used by | GordianXI | source |
|---|---|---|---|---|---|
| `bl00` | spawn the black card `bk00` (never expires): the eyes closed, from the scene's start | 51402 (`p` 219) | 0x9F on the player | played: the screen goes black | retail DAT |
| `open` | 0x3F `bk00` -> `bk01` (black, 150 frames, fades out), spawn `md00` and the eye-shaped mask `mb00`: the eyes opening on Joachim's legs (recording about 1:48-1:50) | 51402 | 0x9F | played: the eye opens (weighted mesh `mb`, #204); the black cards stay opaque longer than retail ([#208](https://github.com/jimmy58663/GordianXI/issues/208)) | retail DAT; recording |
| `clos` | spawn `bk02` (fades to black over 160 frames), 0x3F `mb00` -> `mb02` (the eye closing), then `bk00` again at 152 | 51402 | 0x9F | played | retail DAT |
| `kill` | 0x1E `bk00`: the black card goes | 51402 | 0x9F | played | retail DAT |
| `mai1` | at 242 starts `strt`, at 322 starts `loop` repeating (0x73): the magenta cloud swelling in the sky (recording about 1:13-1:28) | 51327 (`p` 144) | 0x9F on the invisible marker 0x010F608F, which 0x59 sub 5 keeps about 50 yalms up | played at the marker (it was drawn on the street, under the floor, until 0x59 sub 5 ran) | retail DAT; recording |
| `mai2` | starts `cas1` (scene colours, not played) and `kie0`, stops `loop` at 370, starts `edxx`, then `tama` repeating: the white burst, the rings and the beam (about 1:28-1:38) | 51327 | 0x9F | played except `cas1`'s colours | retail DAT; recording |
| `stop` | 0x05 plays clip `ban2` on the actor, 0x5F stops `loop`, and `tama` after 160 frames | 51327 | 0x9F | routine stops played; the clip is not | retail DAT |
| `fall` | dozens of generators about 229 above the director (light glows, a falling beam), the black card `bl00` in front of the camera, two blur commands (0x0E) and a 0x72 flash (44 30 3A, 6 frames, back over 20) | 51328 (`p` 145) | 0x9F on the director, twice | played (the blur since #205, `wa01`'s weighted mesh `uw` since #204) | retail DAT |
| `s002` / `kil2` | spawn the sparkles `tub5` / `tub6` (auto-running, in front of the camera, 0.5 out) / kill them: they float through the scene from 15.6 s to 48.4 s | 70443 (0xCD `p` 8) | 0xCD on the player | played: an auto-running generator of a scene file emits from its spawn until it is killed | retail DAT; script trace |
| `se00`, `0pro`, `kpro`, `0rak`, `krak`, `ke00`, `0dkn` | spawn and kill the file's seven generators, all sounds (link type 0x3D: `1080`, `8238`, `4053`, `2088`, `6041`, `7124`, `7a24`) on the player and the marker | 57129 (0x45 `p` 488) | 0x45 | nothing to draw; the sounds wait for [#167](https://github.com/jimmy58663/GordianXI/issues/167) | retail DAT |

## Task names

The routine FourCC is a literal in the opcode (not a work reference). Shapes over all 0x45 uses:

| name / id | what it is | where it lives | used by | GordianXI | source |
|---|---|---|---|---|---|
| `sNNN` (`s000`, `s001`, ... `s00s`, `s01n`, `sscb`) | a camera shot: its op 0x04 plays Route `cNNN` of the same file (e.g. `s002` -> `c002`, 1080 frames in 30840) | the cutscene's own scene DAT (Port Bastok intro: 30840 = `p` 136, ROM/62/88) | 0x45 (58,596) | played: `CameraRoute`, `ViewportCamera.SetEventView` | retail DATs, 2026-09-30; xi-tools docs/events/scene_dat_writer.md |
| `wNNN`, `xNNN`, `yNNN`, `zNNN`, `qNNN`, `rNNN` | other shot series (Maat's test uses `w005`-`w014`) | the scene's DAT | 0x45 (`z###` 1,621, `y###` 1,434, `r###` 1,264, `x###` 963, `w###` 895) | played when the routine has op 0x04 | xi-tools docs/events/maat_93_study.md |
| `fdo?` / `fdi?` / `fao?` / `fai?` / `ovl?` / `blon` / `blof` / `who?` / `whi?` / `qstc` | the shared fades and cues | 30904, 30905 | 0x45 | see the tables above | - |
| `main`, `kill`, `stop`, `str0`, `star`, `mai1` / `mai2`, `kil0`... | effect tasks (generator spawns, op 0x02, and others) | the 5012 / 51183 / 56685 / 67355 / 70435 / 70691 / 102449 bands, and some 0x45 files | 0x62, 0x9F, 0xBB, 0xC5, 0xCD, 0xD0, 0xD5 (and 0x45) | played for every band (`SceneEffectPlayer`, [#192](https://github.com/jimmy58663/GordianXI/issues/192)) | retail DATs; #192 |
| `xxxx`, 0 | "no routine": the motion opcodes start nothing | - | 0x2C / 0x5B / 0x66 | skipped (`ExecEntityMotion`) | XiEvents OpCodes/0x005B |

The scene DAT layout (`evte`, Route sections 0x06, routine sections 0x07, generators, `end`) and the Route fields are in [vm.md](vm.md#cutscene-schedulers) (`EventSceneResource`, `CameraRoute`, `SceneRoutine`).

## Motion resources

| name / id | what it is | where it lives | used by | GordianXI | source |
|---|---|---|---|---|---|
| own routines | a routine of the actor's loaded model (`dead` 675 uses, `corp` 594, `tlk0` 469, `clp0` 405, `bind` 353, `tlk1` 332, `hap0` 260, `cabk` 222...) | the model's DATs | 0x2C (8,340) | `EventMotionSource.Own`, `ActionMotion.EventMotion` | XiEvents OpCodes/0x002C; xi-tools docs/cutscene_authoring.md |
| event motion bank `n` (`mot_`) | gesture routines and their clips loaded onto the actor first | `32104 + n` (n < 512), `49135 + n` (< 1024), `56345 + n` (< 2048), `59739 + n` (< 3072), `66339 + n` | 0x5B (57,189 uses of 2,505 banks: 20,522 / 7,327 / 9,509 / 12,194 / 7,557 per band; 80 from a work value) | `EventVm.MotionBankFileId`, `EventMotionBank`, `EventMotionSource.Bank` | XiEvents OpCodes/0x005B (ReadEventMotionRes) |
| bank 0 | `tlk0` / `tlk1`, `thk1` / `thk2`, `pas0` | 32104 (ROM/68/76) | 0x5B | as above | retail DAT, 2026-10-01 |
| bank 70 | `tlk0` / `tlk1`, `tla0-1`, `tlb0-1`, `tlc0-1`, `thk0-2`, `oti0-1`, `oro0-1`, `pas0`, `std0` | 32174 (ROM/69/18) | 0x5B | as above | retail DAT |
| motion package `n` | the player-model gesture set of 0x66: with the waist part | `32360 + 2n` (twin `32361 + 2n`, presumably for robe bodies) | 0x66 (35,801 uses of 194 numbers; 1,960 from a work value) | `EventMotionBank.PackageFileIds`, `EventDialogController.LoadMotionPackage`, `EventMotionSource.Package` | XiEvents OpCodes/0x005B (ReadTpcEventMotionRes); file ids found in the retail DATs, 2026-10-01 |
| motion package `n`, no waist | the same set without part 2; used when the first file has no routines (package 29: 32418 empty, 32741 holds it) | `32712 + n` | 0x66 | fallback in `LoadMotionPackage` | retail DATs |
| package 0 | the default humanoid talk set: `tlk0` (848 uses), `ten0` (523), `tlk1`, `thk1`, `thk2`, `ten1`, `dis0`, `pas0`, `tlk2`, `ten2` | 32360 (ROM/70/73), 32712 | 0x66 (2,648) | loaded | xi-tools docs/cutscene_authoring.md; retail DAT |
| packages 20, 21, 29 | San d'Oria talk, salute and thought sets, Elvaan skeleton | `32360 + 2n` / `32712 + n` | 0x66 (1,743 / 252 / 3,329) | loaded | [vm.md](vm.md#cutscene-schedulers), #176 |
| package 12 | Cornelia's `kka0` | `32384` / `32724` | 0x66 (101) | loaded | xi-tools docs/cutscene_authoring.md |
| packages 176 and up | 51 numbers from 176 to 2404, 1,067 uses | not located (the two tables hold 176 packages) | 0x66 | not loaded: `LoadMotionPackage` accepts 0-175, the gesture falls back to the entity's own motions | corpus scan, 2026-10-01 |
| package -1 | `sha0` / `sha1` (kneeling) for Joachim and the player in Port Jeuno event 324; never an immediate, it comes from a work value | not known | 0x66 | not loaded, the actors stand, [#193](https://github.com/jimmy58663/GordianXI/issues/193) | #193 |
| idle name | the idle the actor returns to (`idl0` 3,283 / 4,320 uses, `dft0` 23 / 45, `chi0`, `id10`, `1tl0`) | the actor's model | 0x5E / 0x6B | name not used; the entity's own idle plays (`ResetMotion`) | XiEvents OpCodes/0x005E, 0x006B |
| stop / wait by tag | ends or waits for the gesture of that FourCC | - | 0x50 / 0x53 | `Scene.EndEntityAction`, `ActionMotion.EventMotionStop` | XiEvents OpCodes/0x0050, 0x0053 |

**Beyond XiEvents:** the package file ids (XiEvents names ReadTpcEventMotionRes but not its files), the default talk set of package 0 confirmed in the retail file, and the packages above 175.

## Clip and routine names

Routines (Section 0x07) play clips (Section 0x2B). A routine names a clip by its three-letter stem and a wildcard (`tl1?`); a clip's last digit is its body-region part on fixed NPC models, emotes and packages: 0 legs, 1 upper body with the weapon joints, 2 waist. `EventMotionBank` and `EntityModelLoader.JoinBodyRegionParts` join the parts under the stem ([world/entities-and-animation.md](../world/entities-and-animation.md)).

| name / id | what it is | where it lives | used by | GordianXI | source |
|---|---|---|---|---|---|
| `idl0` / `idl`, `std0`, `1dl` / `2dl`, `1tl` / `2tl`, `gid`, `gud`, `btl`, `cmb` | idle and combat stances (by weapon count) | the model's DATs | client (no opcode); 0x5E / 0x6B name `idl0` | `NpcStanceResolver.TryGetClip` fallback lists | retail DATs |
| `wlk0`, `run0`, `mvb`, `mvl`, `mvr`, `cwlk`, `crun` | locomotion | the model's DATs | client; event walks 0x1F | `NpcStanceResolver` | retail DATs |
| `dft0` | an idle name some scripts give 0x5E / 0x6B | - | 0x5E / 0x6B (23 / 45 uses) | not used | corpus scan |
| `tlk0` / `tlk1` / `tlk2` | talk gesture (legs / upper body / waist) | banks and packages | 0x5B / 0x66 / 0x2C | played | retail DATs |
| `thk0`-`thk2`, `ten0`-`ten2`, `dis0`, `pas0`, `tla0`-`tlc1`, `oti0-1`, `oro0-1` | think, other talk and gesture variants of the talk sets | package 0, bank 70 | 0x5B / 0x66 | played | retail DATs |
| `sha0` / `sha1` | kneel (package -1) | not located | 0x66 | not played, [#193](https://github.com/jimmy58663/GordianXI/issues/193) | #193 |
| `kka0` | Cornelia's gesture | package 12 | 0x66 | played | xi-tools docs/cutscene_authoring.md |
| `ati0`-`ati9`, `atf0` / `atb0` / `atl0` / `atr0`, `cni0`..., `ca??` -> `sh??`, `cm0` | combat routines | the model's DATs | S2C 0x028 (not events) | `ActionMotion` | [world/entities-and-animation.md](../world/entities-and-animation.md) |
| `mou4` | talking mouth: the mouth joint over 31 frames at 15 fps (2 s), once per spoken line | every humanoid skeleton (Hume male joint 54, Hume female 32, Elvaan male 34, Elvaan female 63, Tarutaru 11, Mithra 46, Galka 44) | client, on each line an actor speaks (0x1D / 0x2B) | `FaceMotion.MouthClip` | retail skeletons, 2026-10-01 (#185) |
| `eye3` | blink: the eyelid joints close and open in 4-11 frames | every humanoid skeleton (Elvaan male 33 and 42, Tarutaru 14-17) | client, at random 2-6 s (not measured) | `FaceMotion.BlinkClip` | same |
| `em00`-`em07` | the eight emote routines of one emote file | the race's emote files (next section) | 0x6E | renamed `emote<id>` by `EmoteMotion.LoadBank` | retail DATs, 2026-10-01 (#176) |
| `clp0` / `clp1` / `clp2`, `bow?`, `kne?`, `sl1?`-`sl3?`... | emote clips by part | emote files (+6 for part 2) | 0x6E; 0x2C (`clp0` 405 own uses) | joined by stem | same |

## Emotes (0x6E)

0x6E reads an actor and a work value: low byte = emote id (the ids of C2S 0x05D and LandSandBoat's `Emote` enum), high byte = a variant. `EmoteMotion.Slot` maps the id to a slot; slot `s` is routine `em0(s % 8)` of emote file `base + s / 8`, its waist part in `base + 6 + s / 8`.

**Emote files per race** (`EmoteMotion.FileNumber`, folder * 1000 + file; source xi-tools src/xi/entity/anim/xi_export.py `_EMOTE_MOTION_FILE`):

| name / id | what it is | where it lives | used by | GordianXI | source |
|---|---|---|---|---|---|
| Hume male | emote files | 32040 = ROM/32/40 (waist ROM/32/46) | 0x6E | `EmoteMotion` | xi-tools; retail DAT (`em00`-`em07`, clips `bow`, `poi`, `sl1`-`sl3`, `kne`, `lau`, `wee` in ROM/32/40) |
| Hume female | emote files | 37013 | 0x6E | same | xi-tools |
| Elvaan male / female | emote files | 41114 / 46075 | 0x6E | same | xi-tools |
| Tarutaru male / female | emote files | 51037 / 51071 | 0x6E | same | xi-tools |
| Mithra | emote files | 56041 | 0x6E | same | xi-tools |
| Galka | emote files | 61008 | 0x6E | same | xi-tools |
| robe bodies | said to use +12 for part 2 | - | - | not done | xi-tools |

**Slot table.** Clips read from the Hume male files (2026-10-01, [world/entities-and-animation.md](../world/entities-and-animation.md)); "uses" = 0x6E immediates over all zones; names from LandSandBoat src/map/enums/emote.h.

| id | name | slot | file / routine | clip | uses | GordianXI |
|---|---|---|---|---|---|---|
| 0 | Point | 1 | +0 `em01` | `poi` | 571 | played |
| 1 | Bow | 0 | +0 `em00` | `bow` | 352 | played |
| 2 | Salute | 2-4 by variant (0-2) | +0 `em02`-`em04` | `sl1`-`sl3` | 446 (variant 0: 225, 1: 114, 2: 107) | played; variant as the slot offset (presumed nation, not verified) |
| 3 | Kneel | 5 | +0 `em05` | `kne` | 30 | played |
| 4 | Laugh | 6 | +0 `em06` | `lau` | 340 | played |
| 5 | Cry | 7 | +0 `em07` | `wee` | 152 | played |
| 6 | No | 8 | +1 `em00` | `den` | 1,220 | played |
| 7 | Yes | 9 | +1 `em01` | `nod` | 2,324 | played |
| 8, 9 | Wave, Goodbye | 10, 11 | +1 `em02`, `em03` | `wav` | 269 / 42 | played |
| 10 | Welcome | 12 | +1 `em04` | `wel` | 248 | played |
| 11 | Joy | 13 | +1 `em05` | `gla` | 456 | played |
| 12 | Cheer | 14 | +1 `em06` | `che` | 286 | played |
| 13, 14 | Clap, Praise | 15, 16 | +1 `em07`, +2 `em00` | `clp` | 428 / 123 | played |
| 15 | Smile | 17 | +2 `em01` | none (face only) | 4 | nothing plays |
| 16, 17 | Poke, Slap | 18, 19 | +2 `em02`, `em03` | `pok` | 153 / 1 | played |
| 18 | Stagger | 20 | +2 `em04` | `sta` | 55 | played |
| 19 | Sigh | 21 | +2 `em05` | `sig` | 307 | played |
| 20 | Comfort | 22 | +2 `em06` | `cmf` | 90 | played |
| 21 | Surprised | 23 | +2 `em07` | `sur` | 988 | played |
| 22 | Amazed | 24 | +3 `em00` | `why` | 527 | played |
| 23 | Stare | 25 | +3 `em01` | none | 0 | nothing plays |
| 24 | Blush | 26 | +3 `em02` | `blu` | 108 | played |
| 25 | Angry | 27 | +3 `em03` | `ang` | 341 | played |
| 26 | Disgusted | 28 | +3 `em04` | `ups` | 347 | played |
| 27, 28 | Muted, Doze | 29, 30 | +3 `em05`, `em06` | none | 0 / 1 | nothing plays |
| 29 | Panic | 31 | +3 `em07` | `pan` | 461 | played |
| 30, 31 | Grin, Dance | 32, 33 | +4 `em00`, `em01` | none | 3 / 0 | nothing plays |
| 32 | Think | 34 | +4 `em02` | `thk` | 455 | played |
| 33 | Fume | 35 | +4 `em03` | `fum` | 93 | played |
| 34, 35 | Doubt, Sulk | 36, 37 | +4 `em04`, `em05` | none | 87 / 78 | nothing plays |
| 36 | Psych | 38 | +4 `em06` | `gut` | 456 | played |
| 37 | Huh | 39 | +4 `em07` | `rx0` / `rs0` / `rx2` | 39 | played |
| 38 | Shocked | 40 | +5 `em00` | `def` | 96 | played |
| 39-45 | (39), Logging, Excavation, Harvesting, Hurray, Toss, (45) | 41-47 | +5 `em01`-`em07` | none | 39: 101, 40-42: 35 / 57 / 27, Hurray 311 (variants 0-6), Toss 41, 45: 78 | nothing plays |
| 46-75 | 46 and 48-57 (no name), Dance1-Dance4 (65-68), Bell (73), Job (74), 75 | none | - | - | 46: 56, 48-57: 68, 65-68: 118, 73-75: 8 | not mapped (`Slot` returns -1) |

**Beyond LandSandBoat / xi-tools:** the slot order (point and bow swapped, three salute slots, then id + 2) and the clips per slot were read from the retail files; the variant values seen for Salute (0-2), Hurray (0-6) and Job support a variant byte, but its meaning is not verified.

## Not done

- 0xA1 (0x62's stop by its place; never used in retail, base disputed) and what file 5012 + n holds ([#199](https://github.com/jimmy58663/GordianXI/issues/199)).
- Scene routine ops 0x60 (sound, [#167](https://github.com/jimmy58663/GordianXI/issues/167)), 0x05 (motion clip on the actor), 0x22 / 0x7F and 0x29 / 0x43 / 0x46 / 0x48 / 0x54 (colours and values, meaning not known; [#206](https://github.com/jimmy58663/GordianXI/issues/206)).
- Motion packages above 175 and package -1 ([#193](https://github.com/jimmy58663/GordianXI/issues/193)).
- Emote ids from 39 on and the dances; robe-body emote waist parts.
