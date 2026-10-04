# Audio Engine

> Phase 5H: the sound backend, the retail sound files, and what plays when. Status and open work: [ROADMAP.md](../../ROADMAP.md) and GitHub Issues.

## Backend (#37): OpenAL Soft

**Decision (maintainer, 2026-10-04): OpenAL Soft through Silk.NET.OpenAL 2.23.0, with the native library from `Silk.NET.OpenAL.Soft.Native` 1.23.1.** OpenAL is the output device only: our managed `AudioMixer` makes the mix, and `OpenAlAudioOutput` streams it.

Constraints it had to meet (issue #37): ATRAC3 comes from a separate decoder (still to be decided; the `IAtrac3Decoder` seam stays unregistered); 44.1 / 48 kHz sources; loops restart at a loop point; positional sound with near / far ranges; streaming of long tracks; Windows, Linux and macOS with no Windows-only APIs.

Why OpenAL Soft (the options compared were OpenAL Soft, miniaudio through a .NET binding, SDL3 through SDL3-CS, and the SDL2 already shipped):

- Ready-made native builds for every target in one maintained package, from the Silk.NET family the project already uses.
- A mature cross-platform output (WASAPI / PipeWire / PulseAudio / ALSA / Core Audio) with low latency, which takes our PCM from a managed thread (`alBufferData` + buffer queue), so no native thread calls into managed code.
- Native 3D (HRTF, doppler, distance models) is available for a later opt-in without changing backend.
- Cost: OpenAL Soft is LGPL. It stays an unmodified, dynamically loaded library that a user can replace (`THIRD_PARTY_NOTICES.md`).

**The default path is legacy parity.** Retail's positional sound is distance volume and stereo panning, so `AudioMixer` does that (and the loop points, fades, buses, event and server volume changes). OpenAL only receives the finished stereo mix: one listener-relative source at the origin, fed with 16-bit stereo buffers, which OpenAL never spatialises. OpenAL's 3D sources, HRTF and doppler are **not** used. They are a possible opt-in enhancement later (positional voices as mono OpenAL sources with `AL_LINEAR_DISTANCE_CLAMPED` for near / far, HRTF through `ALC_HRTF_SOFT`), behind a setting that defaults off.

`OpenAlAudioOutput`:

- Opens the default device with `ALC_FREQUENCY` 48000 (OpenAL Soft resamples to the hardware), one source, and 16 buffers.
- `Queue` reclaims the processed buffers, fills a free one with the mixer's 512-frame chunk and queues it, and restarts the source after an underrun. When all 16 are in flight the chunk is dropped; the engine keeps about 2048 frames queued, so that does not happen in practice.
- Loading tries Silk.NET's lookup (`ALContext.GetApi(true)` / `AL.GetApi(true)`), then the bundled `runtimes/<rid>/native` copy beside the app. If the library or a device cannot be opened (CI, headless Linux) it logs and `AudioEngine` falls back to `NullAudioOutput`: the game runs silent and no test needs a device.

Native libraries copied to the output (from the package): `runtimes/win-x64|win-x86|win-arm64/native/soft_oal.dll`, `runtimes/linux-x64|linux-arm64|linux-arm/native/libopenal.so`, `runtimes/osx-x64|osx-arm64/native/libopenal.dylib`. Checked on Windows x64 (2026-10-04): Silk.NET's lookup found the library, the device opened at 48 kHz, and `music023.bgw` (ADPCM) played through it, the device pulling 4.05 s of audio in 4.0 s (`OpenAlAudioOutputTests.Smoke_PlaysAnAdpcmTrack`, opt-in with `GORDIAN_AUDIO_SMOKE=1` because it is audible). Linux and macOS are not yet checked on hardware.
Layout (layer rules from AGENTS.md):

- `Gordian.Core/Audio`: backend-neutral pieces with no device code. `IPcmSource` (a stream of interleaved 16-bit PCM that does its own looping), `PcmClip` (a decoded sound shared by voices), and the retail file decoders (#38).
- `Gordian.App/Audio`:
  - `AudioMixer`: voices resampled (linear) to the output rate, gains (voice x fade x category x script fade x master), constant-power pan, distance falloff, 16-bit clip. Commands from the game thread are queued and applied by the mixing thread, so `Mix` neither locks nor allocates. Up to 96 voices; when full the quietest non-music voice is dropped.
  - `IAudioOutput`: the device seam: open stereo S16 near 48 kHz (the device may pick its own rate; the mixer follows it), report queued frames, accept PCM. `OpenAlAudioOutput` is the device; `NullAudioOutput` is the silent fallback.
  - `AudioEngine`: the output plus the mixing thread, which keeps 2048 frames (about 43 ms) queued and mixes 512 frames at a time.

Buses (`AudioCategory`) follow the retail categories that the event VM's volume opcodes address by mask (XiEvents `OpCodes/0x0069`: 0x01 effects, 0x02 system, 0x04 zone, 0x08 master): **Music**, **Effects**, **System**, **Zone**, plus the master gain.
Provisional: the distance curve (full volume inside `near`, linear to silence at `far`) and the 0.8 pan width of positional voices are not yet compared with retail.

## Sound files (#38)

Retail audio lives outside the DAT tree, in seven sound roots under the install: `sound`, `sound2`-`sound6`, `sound9` (`FfxiSoundLocator`; every name lower case on disk). Format reference: xi-tools `docs/audio/format.md` (which credits the Windower pol-utils reader); implemented from that description, not from its code.

| Kind | Path | Marker | Count (this install) |
|---|---|---|---|
| Music | `<root>/win/music/data/music{id:000}.bgw` | `BGMStream\0\0\0` (12 B), then `format`, `size` | 224: 143 ADPCM, 81 ATRAC3 |
| Sound effect | `<root>/win/se/se{id/1000:000}/se{id:000000}.spw` | `SeWave\0` + flag byte (8 B), then `size`, `format` | 11,862: 9,700 ADPCM, 1,141 PCM, 1,006 ATRAC3, 15 encrypted |

Header (`FfxiSoundHeader`), after the marker and the two fields above: `int32 id`, `int32 blocks` (per channel), `int32 loopStart` (block; negative = one-shot), two `int32` whose signed sum is the sample rate, `int32 0x30`, then bytes `?, ?, channels, blockSize`. Data starts at 0x30; each block holds one frame per channel, in channel order.

- **ADPCM** (`FfxiAdpcm`): a frame is a header byte (high nibble filter 0-4, low nibble range) plus packed 4-bit samples, low nibble first: `sample = (nibble << ((12 - range) & 31)) + ((h0*F0[f] + h1*F1[f]) >> 8)`, clamped, with F0 = {0, 240, 460, 392, 488}, F1 = {0, 0, -208, -220, -240}. The prediction uses an arithmetic shift (a truncating divide drifts); a filter of 5 or more is a silent block that keeps the history. Frame geometry is derived from the size (`(size - 0x30) / (blocks * channels)`), since a few effects declare 16 samples per block with smaller frames (`se018154`: 5-byte frames).
- **PCM**: raw interleaved 16-bit little endian; the loop block is scaled by the `blockSize` byte (provisional, no looped PCM checked).
- **ATRAC3** (format 3): not decoded by GordianXI yet. `FfxiSoundDecoder` hands it to a registered `IAtrac3Decoder`; none is registered yet, so about a third of the music (81 tracks) and 8.5 % of the effects play as silence and are logged once. The clean-room decoding spec is [audio/atrac3.md](../audio/atrac3.md): every FFXI ATRAC3 file uses 192-byte frames per channel without joint stereo, the frames are XOR-obfuscated with a key derived from the first block, the `blocks` field is the total sample count and `loopStart` a sample index (music: as is; effects: 1024 earlier, provisional), so the `loopStart x blockSize` loop frame `DecodeClip` uses today is wrong for ATRAC3.
- **Encrypted** `.spw` (byte 7 not zero, `se039211`-`se039225`): skipped, as every public tool does.
- Looping: after the last block playback jumps to `loopStart` and restores the ADPCM history it had when it first decoded that block (`FfxiSoundStream`).

Checked against the install (`FfxiSoundDecodeTests`): `music023.bgw` is 44.1 kHz stereo, 65-byte frames (128 samples), 170 s with the loop at 10.3 s, and its decoded channels correlate (0.64); `se002060.spw` is 48 kHz mono, 9-byte frames; every one of the 12,086 files parses and a sample of 400 effects decodes to the expected length.

Sound pointers (`SoundEffectPointer`, DAT section 0x3D, xi-tools `docs/audio/refs.md`): `"SeSep  "` then `u32` sound effect id. `ZoneSoundTable` groups a zone model DAT's pointers (zone 230 Southern San d'Oria: 848 pointers, zone 4 Bibiki Bay: 878, all resolving to files):

| Directory | Holds | Example |
|---|---|---|
| `weat/<weather>` | Ambient loop per weather, keyed by the Vana'diel time it starts (`HHMM`) | Bibiki Bay `suny`: `0600` → 1013, `1800` → 1015; Southern San d'Oria: `0000` → 1081. Stereo, looped ADPCM |
| `weat/<weather>/indo` | An indoor set (when it applies is unknown) | Bibiki Bay: four `1064` → 1064 |
| `<area>/fses`, `<area>/fser` | Footsteps, walking and running, named `0<terrain><move><shake+1>` (xi-tools `docs/sounds/footsteps.md`) | `0111` → 100001 (walk) / 100011 (run) |
| `door/<door>` | A door's open / close sounds | `_6e1`: 9021, 9022 |
| `effe/...` | Effect sounds the zone's routines use | `seso`: 2060, 2003, 2175, 2197 |

**Beyond xi-tools:** the `fser` running set, and the `HHMM` time keys of the weather ambient loops (our reading: it matches the 06:00 / 18:00 switch; provisional).

`SoundLibrary` (App) decodes effects whole once and caches them (about 96 MB cap), and streams music from the file bytes, both through `FfxiSoundDecoder` (ADPCM / PCM managed, ATRAC3 through the registered decoder).

## Music and ambience (#42, #114)

**Server music state** (`ZoneMusicState` on `WorldState.Music`, Core): eight slots (XiPackets `world/server/0x005F`): 0 zone day, 1 zone night, 2 solo battle, 3 party battle, 4 mount, 5 dead, 6 Mog House, 7 fishing.

- S2C 0x00A `MusicNum[5]` (wire 0x56, payload 0x52) fills slots 0-4 on every zone-in and resets the server volume to 127; slots 5-7 are kept.
- S2C 0x05F (`u16 Slot`, `u16 MusicNum`) sets one slot. LandSandBoat sends it from Lua `changeMusic` and zone-wide battle music changes.
- S2C 0x060 (`u16 time`, `u16 volume` 0-127) eases the music bus to `volume / 127`. XiPackets says the client lerps over `time` without a unit; we read 1/60 s frames (provisional). LandSandBoat never sends it.

**Track choice** (`MusicDirector`, App), from the local player each frame. The status is the player's own S2C 0x037 server status (`LocalPlayerState.ServerStatus`), or engaged while the client has engaged a target; the player's `WorldEntity.AnimationState` is not used, because the server never sends the player its own 0x00D (that left battle music silent in the first in-game round). In towns LandSandBoat sets the battle slots to the town track (`data/zones/*/zone.yaml`, e.g. Windurst Woods 151 / 151 / 151 / 151), so battle music only changes outside them (East Ronfaure: 109 day, 101 solo, 103 party). Each track change is logged (`AUDIO Music: slot … → track …`), and an ATRAC3 track logs `ATRAC3 track N, no decoder: playing silence.` once.

| Situation | Slot |
|---|---|
| status 3 (dead) | 5 dead |
| status 1 (engaged) | 3 party battle when in a party of two or more, else 2 solo battle |
| status 5 (chocobo) or 85 (mount) | 4 mount |
| status 6 or 38-62 (fishing) | 7 fishing |
| otherwise | 1 night from 18:00 to 06:00 Vana'diel time, else 0 day |

A slot holding 0 falls back to the zone's day / night track (night 0 falls back to day). When the track changes, the old one fades out over 1.5 s and the new one starts from the top; when two slots hold the same track it keeps playing. An event can override the choice (#167). Tracks loop at their header loop point. Provisional, not yet compared with retail: the 18:00 / 06:00 switch, battle music only while the player is engaged (not while a party member fights or a monster claims the player), the fade length, and the Mog House slot (not picked yet: nothing tells the client it is in the Mog House; #116's MyRoom flags would).

**Ambient loops** (`GameAudioService`): on each zone change the zone model DAT's sound pointers are read on a worker (`ZoneSoundTable`). The loop for the current weather (`WorldState.WeatherId`, falling back to its sky category, then `fine`, then any authored weather) and Vana'diel minute plays on the Zone bus, looped, crossfading over 2 s (provisional) when weather or time selects another. The indoor (`indo`) sets are not used.

**Who is heard:** one device for the app. The viewport whose window was last activated owns the sound (multi-boxing plays only that character); its render loop calls `GameAudioService.Update` each frame. The listener is the camera: position, and the view matrix's screen-right axis for panning.

## Volume (#44)

The retail config page has two sliders, music and sound effects (`StockUiSettingKey.MusicVolume` / `SoundEffectsVolume`, 0-100 in steps of 5, saved per character in `ui_settings/<name>.json`). `VolumeMix` maps them to the buses: music → Music; sound effects → Effects, System and Zone. Gain is `value / 100`, linear (provisional: retail's curve is not measured). `GameAudioService` re-reads them every frame, so a slider move is heard at once. Per-bus gain order: voice x voice fade x slider x script fade (0x060, event opcodes 0x69 / 0x6A) x master. No GordianXI-only master or per-bus sliders exist yet; they would be an opt-in enhancement.

## UI sound cues (#43)

System sounds live in `se000` (ids and names from the Windower pol-utils list bundled in xi-tools `src/xi/audio/data/SFXInfo.xml`, Apache-2.0). `StockUiSoundCue` values are those ids; Core raises them as neutral cues and `GameAudioService.PlayCue` plays them centred on the System bus.

| Cue | Id | Raised by |
|---|---|---|
| Menu Movement | 1 | `StockUiMenuController.ProcessInput`: a direction that changed the selection, slider or count |
| Menu Selection | 2 | Confirm in a menu |
| Main Menu Page Switch | 13 | the menu key on an open paged menu |
| Main Menu Open | 14 | the menu key opening the main menu |
| Close Menu | 15 | Cancel in a menu |
| Target Selection | 9 | `PlayerActionService.TargetChanged` from no target |
| Target Switch | 10 | `TargetChanged` from one target to another |
| Message Arrival | 39 | an incoming tell (`ChatMessageType.Tell` not from the player) |

Not wired yet: Dialog Confirmation (3) and Unavailable Action (4), the target menu open (11), level-up (7) and quest complete (8), the `<call>` sounds (17-38), and mouse clicks in menus. Which action plays which id is our reading of the names (provisional until compared with retail).

## Footsteps (#40)

`FootstepTracker` (App), each frame, for every drawn actor within 30 yalms of the listener whose animation is a gait (walk, run, combat and strafe moves) and not an action:

1. **When:** a step at the start and at the middle of each gait clip cycle (`EntityAnimationState.CurrentClip` phase crossing 0 or 0.5). Provisional: retail fires on the model's foot-landing state (xi-tools `docs/sounds/footsteps.md`, after xim), which GordianXI does not detect yet.
2. **Surface:** the collision terrain under the actor (`ZoneCollisionMesh.TryGetGround(position, 1, 2)`, `GroundHit.Terrain`), `Object` when there is no ground.
3. **Footwear:** `FootwearInfo` reads the first Info section (0x45) of the feet item's DAT (a character: race from the face word, `CharacterEquipmentResolver` feet file) or of the creature's own model DAT: byte 1 is the move digit (base 36, 0xFF = `0`), byte 2 the shake. Read on a worker and cached per file; the default `1`, 0 until then. Checked: all 360 feet DATs of Hume male, Taru female and Galka 0-119 read, digits `11` x99, `12` x97, `10` x94, `21`, `20`, `22`, and every one names a pointer Southern San d'Oria has (`FootwearInfoTests`).
4. **Sound:** the zone's pointer `0<terrain hex><move><shake + 1>` from `fser` while running, else `fses` (`ZoneSoundTable.FootstepSound`), played positionally on the Effects bus (full volume within 4 yalms, silent at 30; provisional).

Not done: sand / snow footprints (the global `fmrk` decal from `ROM/0/0.DAT` and the zone's `fses/fefs` foot effects are rendering work), and what the config page's "Footstep effects" toggle (`StockUiSettingKey.FootstepEffects`) controls in retail (sound, footprints or both); it is not applied yet.

## Zone effect audio (#39)

Zone sound sources are Section 0x05 generators whose linked data is a 0x3D sound pointer: 5,868 in 298 zone DATs, 5,227 of them auto-run, 184 on paths, 1,686 with a time-of-day volume (`ZoneSoundEmitterTests`). Op names from xi-tools `docs/reference/ps2_beta_2001.md`; argument layouts read from the retail data (**Beyond xi-tools**):

| Op | Name (PS2) | Layout | Example |
|---|---|---|---|
| init 0x01 | standard setup | linked data id = the 0x3D section name, resolved in the generator's directory then its parents; base position (internal space) | San d'Oria `seso/se01` → `2003` |
| init 0x4C | `InitSoundElemParam` (audio range) | `f32 far, f32 near, f32 ?` | Bibiki Bay `mina` 60 / 10, `gake` 80 / 40, `hama` 60 / 25; San d'Oria 8-30 / 0 |
| init 0x6B | `InitPathSound` | arg 0: 0x4A path section name | `mina`, `gake`, `hama`, `kiji`, `choc` |
| init 0x68 | `InitCorrectKeyTimeVolume` | arg 1: 0x19 key curve name | `tmvo` |
| update 0x43 | `IdleCorrectKeyTimeVolume` | applies the curve over the day fraction | San d'Oria `tmvo`: 0 until 06:00, 1 from 06:36 to 18:36, 0 from 19:12 |

Path sections (0x4A): `"RAB\0"`, u32 7, ..., u32 point count at +0x30, then points of 0x20 bytes from +0x40, each `f32 x, y, z, w` (w is 10 in Bibiki Bay, 1 in San d'Oria; unknown). A path whose points all have height 0 takes the generator's height.

Playback (`ZoneEmitterAudio`, App): each auto-run source starts when the listener is within `far` and its time volume is above 0, plays on the Zone bus with full volume inside `near` and linear falloff to `far`, follows the nearest point of its path to the listener, and stops with a 0.5 s fade 5 yalms past `far` or when its time volume reaches 0. Looped files loop; one-shot files replay when they end.

Provisional: the nearest-point reading of path sounds, the replay of one-shot files (retail re-emits on the generator's own timing), the flat-path height, and the unknown third 0x4C float and path `w`. Not done: generators that routines spawn on demand (doors, scheduler effects: not auto-run), sounds of actor-attached effects.

## Event music and volumes (#167)

Event opcodes 0x5C / 0x5D / 0x69 / 0x6A / 0x9A run in `EventVm.Sound.cs` (layouts: [events/opcodes.md](../events/opcodes.md#0x5c-0x5d)), through `IEventVmHost` methods with default bodies, so `EventVm.cs` only gained one `case` group:

- 0x5C subs 0-7 / 0x80-0x87 set music slots in an event layer of `ZoneMusicState` that overrides the server's slots; 0x5C 0xA0 / 0xA1 and 0x5D set its music volume (0-127). When the event ends (`EventDialogController` finish or drop) the layer and the volume are dropped and the zone's music returns (the director crossfades back).
- 0x69 / 0x6A set or ease the category volumes in `WorldState.EventSoundVolumes`; `GameAudioService` applies them as script fades: effects → Effects bus, system → System, zone → Zone, master → those three. Reset when the event ends. The master is the sound elements' master (XiEvents `YmSoundElem_SetMasterVolume`), not the music, which the music server owns: Lufaise Meadows `!cs 117` mutes mask 0x1F (master included) for the whole scene while it plays track 900, so the scene has its music and no effects. Applying master to the music silenced it completely (in-game round 3).
- 0x9A yields until `MusicDirector.IsSettled` (no fade-out pending, no track loading). With no audio device it never waits, so a silent client cannot hang a scene.
- **A slot write of the track already playing changes nothing**: the music goes on where it is. Checked by the maintainer on retail (2026-10-05): cutscenes do not restart the zone music; they keep it playing or start their own. **Differs from XiEvents:** its 0x5C pseudo-code sets the current music number to -1, which reads as "restart"; a restart rule built on that (round 1) was wrong in retail and has been removed. The new-character intros set slots 0 / 1 to the track the town already plays (Windurst 151, San d'Oria 107, Bastok 152), so their music simply continues.
- **Start volume:** 0x5C gives the slot's song a start volume of 127 (`PTR_MusicStartVolumes[slot] = 127`), while 0x5D / 0x5C 0xA0 only move the volume of the song playing. So when a slot write leads to a different track, that track starts at 127 (`ZoneMusicState.TrackStarted`). Port Jeuno `!cs 324` (traced, `PortJeuno324_SetsItsOwnMusicThenGivesTheZoneMusicBack`): slots 110 (the zone track: no change), slots 51 (its own track), `5D` to 0 over 120, then slots 110 again mid-scene, which retail plays at full volume when the player wakes and talks to Joachim, and 110 once more at the end. Before this the 0 volume stuck until the event ended (in-game round 2).

Provisional: the event time unit (read as 1/60 s frames like 0x060), the ignored 0x8n start volume, and the 1 s / 0.5 s restore fades at the event end.

## Cutscene sound effects

Scene resource DATs (the files scheduler opcodes play) carry their sounds as 0x3D sections, played two ways (read from Port Jeuno 324's files, 2026-10-05):

- **Sound commands** in their routines: op 0x60 (global) in every case seen, with the 0x3D section name at +8; xi-tools `docs/fx/effect_system.md` names 0x0A (source), 0x0B (target), 0x4A / 0x53 / 0x60 variants. `SceneRoutine` decodes them as `SceneCommandKind.Sound`. 57129 `se00` → `1060` → 41060; 30905 `who1` → 8158, `blon` 8160; 57129 `0pro` 34125 at frame 10 and 34126 at frame 150.
- **Sound generators** that routines spawn (op 0x02 / 0x3F): 57129 `2088`, `6041`, `8238`, `7124` / `7a24`, `1080`, `4053`; 30904 `4026`, `4007`, `7124`.

`EventSceneResource` maps both to sound ids, and each sound generator to its own range (init op 0x4C `f32 far, f32 near`); `EventPresentation.Play` schedules them for the task (following 0x03 / 0x73 routine starts, four deep; loops one pass), with the kills (0x1E, and the first name of 0x3F) of sound generators, and `GameAudioService` plays them as they come due on the Effects bus: sound commands 0x60 / 0x4A / 0x53 centred, 0x0A / 0x0B at the task's actor (15 / 60 yalms, provisional); generators at the task's actor with their own range. A generator whose file loops plays until it is killed, its spawn duration ends, or the event ends (`EventPresentation.SoundResets`). Port Jeuno 324 schedules 12 sounds.

The lightning of Port Jeuno 324: the task `0rak` (57129) runs on the invisible sky-flash marker 0x010F608F and spawns `6041` (36041, the strike) at frame 1 and `2088` (2088, a looping rumble) at frame 46; `krak` kills `2088`, `0dkn` adds `7124` / `7a24` (17124) on the player. `6041` and `2088` are authored with far = 3000 yalms, near 0, so they are heard across the zone; with the fixed 60-yalm range they were scheduled but inaudible from the marker (in-game round 3). None of them is ATRAC3; the ATRAC3 effects in 324 are 41060 (`se00`) and 36109 (`in01`).

Still missing in cutscenes (not built):
- Sounds of **motions** (emotes, gestures, actor motion routines that carry 0x0A / 0x0B), which need the motion routine player to raise sound commands; `MotionRoutineDecoder` drops them today.
- Sounds of **zone routines** an event starts (0x2D / 0x51 / 0x54 / 0x60 sub 2, `ZoneRoutinePlayer`) and of **actor-attached effects**: their generators are filtered out before playback (audio generators have no draw layer).
- **Event-zone ambience**: in an event zone (0x34 / 0x35) the ambient loop and sound generators stay those of the current zone.
- Scene tasks whose routine name reads empty (Lufaise Meadows 117 starts tasks on 51257, 30904, 30905 and 51343 with no name: "no such routine") play nothing, sounds included; that is the event VM's task opcode reading, not audio.

## Debug commands

Temporary, client-only commands for listening to files directly; never sent to the server (`ChatCommandRouter` → `PlayerActionService.DebugAudioCommand` → `GameAudioService.HandleDebugCommand`), listed under "Debug" in `/help`:

- `/playsound <id>` plays `seNNNNNN.spw` centred on the Effects bus and replies with its format and whether it loops. A looped file loops until `/playsound stop`, so its seam can be checked (the 13 looped ATRAC3 effects: 36108 36124 36125 36128 36138 41017 41031 41035 41044 41045 41046 41052 41057).
- `/playmusic <n>` plays `musicNNN.bgw` in place of the zone's music (the director's override, as an event's would); `/playmusic stop` returns to the zone music. Unlike LandSandBoat's `!setmusic` it involves no server.
## Combat and action sounds (#41): findings, deferred

Not implemented. What the retail data shows (probed in `ROM/0/0.DAT` and the Hume battle pack `ROM/32/13`):

- Sound commands in effect routines are ops 0x0A (at the source) and 0x0B (at the target), 32 bytes: +0x08 the 0x3D section name (`5045`, `7129`...), +0x14 f32 60 in most (a range). xi-tools `docs/fx/effect_system.md` also lists 0x4A / 0x53 / 0x60 variants.
- The battle pack carries no sound pointers; the hit sounds live in `ROM/0/0.DAT` (98 pointers, `se005xxx` combat sounds), in the hit routines `hit1/hi10`-`hi19`, which spawn generators (`g10s`...) whose linked data is a sound.
- The motions link `dada` at the hit moment; `dada` runs `atpr`, `crtl` and `dam0`, which pick the hit routine (`hit3`, `hit5`, `hi14`...) and the damage reaction (`sb00`-`sb05`) through the conditional ops 0x64 / 0x67 / 0x69 / 0x6A / 0x6B on registers the action result sets.

So combat sounds need the effect-routine conditional interpreter and the action-result registers (and the same routine player would draw the hit sparks), plus spell / ability effect DATs played from S2C 0x028, which nothing plays yet. That is effect-routine work rather than audio work; the audio side (`GameAudioService.PlayEffect` with a positional emitter) is ready for it.

## Phase 5H plan

- [x] Zone effect audio: the auto-run sound generators with range, path and time-of-day volume (#39, above).
- [x] Cross-platform audio backend: OpenAL Soft as the output device (#37, above).
- [x] Clean-room decode of the retail sound files (#38, above; ATRAC3 open).
- [x] Footstep sounds from the gait, collision terrain and footwear (#40, above; footprints open).
- [ ] Combat and action sounds (#41: deferred, findings above).
- [x] Event music and volume opcodes (#167, above).
- [x] Ambient zone loops & BGM playback (#42, #114, above).
- [x] UI/menu sound cues (#43, above).
- [x] Master/category volume mixing from the config sliders (#44, above).
