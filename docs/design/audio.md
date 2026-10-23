# Audio Engine

> Phase 5H: the sound backend, the retail sound files, and what plays when. Status and open work: [ROADMAP.md](../../ROADMAP.md) and GitHub Issues.

## Backend (#37): OpenAL Soft

**Decision (maintainer, 2026-10-04): OpenAL Soft through Silk.NET.OpenAL 2.23.0, with the native library from `Silk.NET.OpenAL.Soft.Native` 1.23.1.** OpenAL is the output device only: our managed `AudioMixer` makes the mix, and `OpenAlAudioOutput` streams it.

Constraints it had to meet (issue #37): ATRAC3 comes from a separate decoder behind the `IAtrac3Decoder` seam (now GordianXI's own managed decoder, #38); 44.1 / 48 kHz sources; loops restart at a loop point; positional sound with near / far ranges; streaming of long tracks; Windows, Linux and macOS with no Windows-only APIs.

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

Buses (`AudioCategory`) follow the retail categories that the event VM's volume opcodes address by mask (XiEvents `OpCodes/0x0069`: 0x01 effects, 0x02 system, 0x04 zone, 0x08 master): **Music**, **Effects**, **System**, **Zone**, plus the master gain. A fifth bus, **Notification** (the incoming tell cue), follows System exactly (effects slider, event system volume); it exists only so the sound controls (#265, below) can let tells through on their own.
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
- **ATRAC3** (format 3): decoded by GordianXI's managed clean-room decoder (`Gordian.Core.Audio.Atrac3`: `Atrac3Stream`, `Atrac3Channel`, `Atrac3Imdct`, `Atrac3Tables`), implemented from the spec [audio/atrac3.md](../audio/atrac3.md) alone and registered by default as `FfxiSoundDecoder.Atrac3`. Every FFXI ATRAC3 file uses 192-byte frames per channel per 1024 samples without joint stereo (a unit that is not a single-channel unit is silenced and logged once), XOR-obfuscated with a key derived from the first block; the unobfuscated last blocks of `music069/071/900` are decoded raw. For ATRAC3 `FfxiSoundHeader` reads `blocks` as the total sample count (`SampleBlocks` = frames from the size) and `loopStart` as a sample index: `LoopStartFrame` = `loopStart` for music, `loopStart - 1024` for effects (provisional, spec section 2; **Differs from vgmstream**). Music streams frame by frame; loops restore a state snapshot taken before the loop frame, so the seam replays the first pass exactly. No start trim (frame 0 is a silent key frame, about 50 ms; vgmstream trims 2186 samples, retail unconfirmed). Verified (`Atrac3DecoderTests`): IMDCT against its direct sum, a hand-built unit against the spec's arithmetic, and, with the install and FFmpeg reference PCM (`GORDIAN_ATRAC3_REF`), `music040/041/069/900`, `se036124`, `se041035`, `se041044` at 134 dB SNR, 16-bit output within 1 LSB (0.01-0.09 % of samples differ by 1), loop restarts matching the reference loop; all 1,087 ATRAC3 files of the install decode with no malformed frame in 49 s (`GORDIAN_ATRAC3_ALL=1`, Release).
- **Encrypted** `.spw` (byte 7 not zero, `se039211`-`se039225`): skipped, as every public tool does.
- **Silent tails before a loop are authored** (in-game round 2: Aht Urhgan Whitegate, `music178`, ~24 s of silence at the loop). Tracks that repeat from the top (loop within 0.5 s of the start) fade out and then carry digitally silent frames up to the header's total: 9 of the 77 looped ATRAC3 tracks (`music141` 32.2 s, `178` 23.7 s, `149` 18.0 s, `253` 15.8 s, `252` 11.5 s, `054` 10.7 s, `179` 10.0 s, `066` 3.5 s, `063` 2.7 s) and 13 of the 122 looped ADPCM tracks (up to 55 s, `music213`); no track with a mid-song loop has one (survey of every looped `.bgw`, silence = |sample| <= 4). The header has no loop-end field (only total and `loopStart`; byte 0x2C ranges 0x50-0x7F with no relation to the tail), and the ATRAC3 decode equals FFmpeg's, so the gap is in the data and is played as written. **Confirmed retail behaviour**: the maintainer's retail check of `music178` in Aht Urhgan Whitegate (2026-10-05) pauses before the restart too, so there is no loop-end rule (`Atrac3DecoderTests.Retail_Music178_*`).
- Looping: after the last block playback jumps to `loopStart` and restores the ADPCM history it had when it first decoded that block (`FfxiSoundStream`).

Checked against the install (`FfxiSoundDecodeTests`): `music023.bgw` is 44.1 kHz stereo, 65-byte frames (128 samples), 170 s with the loop at 10.3 s, and its decoded channels correlate (0.64); `se002060.spw` is 48 kHz mono, 9-byte frames; every one of the 12,086 files parses and a sample of 400 effects decodes to the expected length.

Sound pointers (`SoundEffectPointer`, DAT section 0x3D, xi-tools `docs/audio/refs.md`): `"SeSep  "` then `u32` sound effect id. `ZoneSoundTable` groups a zone model DAT's pointers (zone 230 Southern San d'Oria: 848 pointers, zone 4 Bibiki Bay: 878, all resolving to files):

| Directory | Holds | Example |
|---|---|---|
| `weat/<weather>` | Ambient loop per weather, keyed by the Vana'diel time it starts (`HHMM`) | Bibiki Bay `suny`: `0600` → 1013, `1800` → 1015; Southern San d'Oria: `0000` → 1081. Stereo, looped ADPCM |
| `weat/<weather>/indo` | An indoor set (when it applies is unknown) | Bibiki Bay: four `1064` → 1064 |
| `<area>/fses`, `<area>/fser` | Footsteps on dry ground and in the rain, named `0<terrain><move><shake+1>` (xi-tools `docs/sounds/footsteps.md`); their `fefs` / `fefr` sub-directories hold the foot effects ([particles.md](../rendering/particles.md#footprints-and-foot-effects-40)) | `0111` → 100001 (dry) / 100011 (rain) |
| `door/<door>` | A door's open / close sounds | `_6e1`: 9021, 9022 |
| `effe/...` | Effect sounds the zone's routines use | `seso`: 2060, 2003, 2175, 2197 |

**Beyond xi-tools:** the `fser` rain set (#342, [Footsteps](#footsteps-40)), and the `HHMM` time keys of the weather ambient loops (our reading: it matches the 06:00 / 18:00 switch; provisional).

`SoundLibrary` (App) decodes effects whole once and caches them (about 96 MB cap), and streams music from the file bytes, both through `FfxiSoundDecoder` (ADPCM / PCM / ATRAC3, all managed).

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

**Who is heard:** one device for the app. By default the viewport whose window was last activated owns the sound (multi-boxing plays only that character); the opt-in multi-box policy (#265, below) can prefer another character. The owner's render loop calls `GameAudioService.Update` each frame. The listener is the camera: position, and the view matrix's screen-right axis for panning.

**Changing who is heard (#368, #377):** when the heard session changes (a Ctrl+Tab character switch, another window taking the sound) or becomes none, `GameAudioService.SwitchSession` fades out everything the last one played except its music (below): ambience, zone sounds, cutscene scene loops and the Effects bus (0.5 s). The combat / action sound and footstep trackers forget their actors, and the new session's already-due scene sounds are dropped: each session has its own world, so the same server id there is another object with other action serials, and the first frame after a switch used to replay every nearby actor's last action (a Trust's summon, the last swing). Only what happens after the switch plays. The character lobby and the black screen of a Log Out show no world: their frames call `GameAudioService.UpdateWithoutWorldIfCreated`, which hears no session, so a Log Out to the lobby fades the zone's music and sounds out instead of letting them play on in character select (before, those frames skipped the audio update). Every character's music fades to silence there, and a character that logged out lets its track go. Since #378 the lobby then plays the title music ([Title music](#title-music-378)).

**Music across character switches (round 2 of the #368 / #377 tests, the maintainer's request; GordianXI behaviour, retail has one character):** every logged-in character has its own `MusicDirector`, updated every frame from its own situation (zone table, status, party, Vana'diel hour) whether it is heard or not (`GameAudioService.UpdateMusic`). The directors hold their tracks in one shared `MusicVoicePool`, one mixer voice per track: characters that want the same track (the same zone and slot) share a voice, and every held track keeps playing, the ones not heard faded to silence (`AudioMixer.FadeVoice`, which fades without stopping, so the position keeps advancing). Only the heard character's director is audible and sets the music bus volume (server 0x060 volume, event master).
- Switching between characters on the same track does not touch the music: it plays on without a restart.
- Switching to a character on another track crossfades over 0.75 s (`MusicVoicePool.CrossfadeSeconds`, provisional) to that character's track where it is now, not from the top: its music kept playing silently while it was not shown.
- A character's own track change works as before (the old track fades out over 1.5 s, then the next starts from the top), except that a next track another character already plays is joined where it is, fading in over 0.75 s, so the two stay one voice (a character engaging and disengaging in a zone another character is in comes back into the shared zone track mid-way rather than from the top).
- The last character to let a track go stops it; a character that logs out lets its track go. Every held track is decoded in real time while silent (two to six characters: a few voices).
- `/playmusic` overrides the heard character's director only, and is no longer cleared by a switch.

## Volume (#44)

The retail config page has two sliders, music and sound effects (`StockUiSettingKey.MusicVolume` / `SoundEffectsVolume`, 0-100 in steps of 5, saved per character in `ui_settings/<name>.json`). `VolumeMix` maps them to the buses: music → Music; sound effects → Effects, System and Zone. Gain is `value / 100`, linear (provisional: retail's curve is not measured). `GameAudioService` re-reads them every frame, so a slider move is heard at once. Per-bus gain order: voice x voice fade x slider x script fade (0x060, event opcodes 0x69 / 0x6A) x control gain (#265) x master. The control gain is 1 unless a GordianXI sound control is switched on.

## Sound controls (#265)

GordianXI-only switches that retail does not have, on the desktop shell's **Sound** tab (`SoundSettingsViewModel`, `MainWindow.axaml`), saved for the whole app in `%LocalAppData%/GordianXI/sound_settings.json` (`SoundControlSettings`). **Every default keeps the retail mix**: sound on, playing with or without focus, the focused window's character heard. They act through a third per-bus gain in the mixer (`AudioMixer.FadeControl`), separate from the retail sliders and the event fades, eased over `FadeSeconds` (0.5 s). The decisions are pure functions in `SoundControls` (tests: `SoundControlsTests`).

| Control | Default | Effect |
|---|---|---|
| Sound on | on | Off sets every bus' control gain to 0, exceptions included; the volume sliders are not moved. |
| Play only while active | off | When GordianXI loses focus, buses fade to 0, except those marked "also while inactive". "Active" is any GordianXI window (default) or only a viewport window (`SoundActiveScope`). Focus comes from the app's one window-focus tracker (`ViewportWindowManager.WindowFocus`, `WindowFocusTracker`, shared with the gamepad since #304), fed by Avalonia window activation through `WindowFocusHook` and recomputed after activation events settle so moving between two GordianXI windows never counts as inactive. |
| Multi-box policy | FocusedWindow | `FocusedWindow`: the last activated viewport window (as before). `PrimaryViewport`: the main viewport's character (`SessionRegistry.PrimaryRenderingSession`), even while a pop-out has focus. The main viewport takes the sound, and activating a pop-out does not take it away; while it shows no character, the focused window is heard. A named-character policy was tried and removed after in-game round 1 (did not work, not wanted); a saved `NamedCharacter` reads as `FocusedWindow`. |
| Tell sound from every character | off | The tell cue is raised for every session (`SessionRegistry`), but plays for a character other than the heard one only when this is on. |
| Per category: on / also while inactive | on / off | Music, Effects, System, Zone, Notification. E.g. music off with tells still on, or every bus muted while inactive except Notification. |

The settings persist across runs, so a bus switched off while testing stays off: each change of the muted buses is logged (`AUDIO Sound controls: muted buses: Music`). #309 (no music in Yahse Hunting Grounds, 2026-10-07) was most likely that: `sound_settings.json` had Music off, while tracks 60 and 57 (in `sound9`, ATRAC3, 48 kHz) resolve, decode and mix audibly offline.

Not done: hearing a character that no viewport shows (a background tab has no camera for a listener), and mixing several characters' sound at once ("all of them" in the issue): one device plays one listener's mix, so only the tell cue crosses characters.

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
| Tell (`TellArrival`) | 40 | an incoming tell (`ChatMessageType.Tell` not from the player), on the Notification bus. Confirmed as the retail tell sound by the maintainer's retail check, 2026-10-08 (via `/playsound 40`); it is entry `0040` of the client's system sound table (`ROM/0/0` `/syst/soun`). Until round 2 this played 39, "Message Arrival", which the maintainer identified in game (2026-10-07) as PlayOnline's message sound (a friend's message through POL), not the tell sound; nothing plays 39 now |

Not wired yet: Dialog Confirmation (3) and Unavailable Action (4), the target menu open (11), level-up (7) and quest complete (8), the `<call>` sounds (17-38), and mouse clicks in menus. Which action plays which id is our reading of the names (provisional until compared with retail).

## Footsteps (#40)

`FootstepTracker` (App), each frame, for every drawn actor within 30 yalms of the listener whose animation is a gait (walk, run, combat and strafe moves) and not an action:

1. **When:** the footfall hook `FootfallDetector` (Core), which the footprints share ([particles.md](../rendering/particles.md#footprints-and-foot-effects-40)): a step at the start and at the middle of each gait clip cycle (`EntityAnimationState.CurrentClip` phase crossing 0 or 0.5). Provisional: retail fires on the model's foot-landing state (xi-tools `docs/sounds/footsteps.md`, after xim), which GordianXI does not detect yet. A change to when a foot lands (a mount's gait, a real landing rule) goes there once for both.
2. **Surface:** the collision terrain under the actor (`ZoneCollisionMesh.TryGetGround(position, 1, 2)`, `GroundHit.Terrain`), `Object` when there is no ground.
3. **Footwear:** `FootwearInfo` reads the first Info section (0x45) of the feet item's DAT (a character: race from the face word, `CharacterEquipmentResolver` feet file) or of the creature's own model DAT: byte 0 the movement type (for the footprints), byte 1 the move digit (base 36, 0xFF = `0`), byte 2 the shake. Read on a worker and cached per file in `FootwearCache` (Core, shared with the footprints); the default `1`, 0 until then. Checked: all 360 feet DATs of Hume male, Taru female and Galka 0-119 read, digits `11` x99, `12` x97, `10` x94, `21`, `20`, `22`, and every one names a pointer Southern San d'Oria has (`FootwearInfoTests`).
4. **Sound:** the zone's pointer `0<terrain hex><move><shake + 1>` from `fser` in rain or squall, else `fses` (`ZoneSoundTable.FootstepSound`, `ZoneSoundTable.IsWetWeather`), played positionally on the Effects bus (full volume within 4 yalms, silent at 30; provisional). Walking and running play the same pointer: the sound id has no gait digit (xi-tools §3).

**`fser` is the rain set, not a running set (#342, 2026-10-20).** South Gustaberg's dirt sounded like a marsh in the 2026-10-10 in-game test: its floors are Path and Stone (floor area by terrain, `SoundPointerRetailTests.Retail_SouthGustabergDirt_PlaysTheDryPathStep`; no shallow water), and the Path step running played `fser`'s 100011 instead of `fses`'s 100001. The data shows what `fser` is: its foot effects (`fser/fefr`) are wet ground where `fses/fefs` has dust (the path root `0100` is the flat ripple mesh `asi2` with the water splash `c804` in `fefr`, a dust puff `hi11` in `fefs`; `fefr` has a wet-grass `0200` in 281 zones, `fefs` in 11), and its water steps equal the dry set's. The decoded clips fit: 100001 (dry path) is a 130 ms thud, the shallow-water 100004 a 1.4 s splash. Retail picks the set by weather; GordianXI takes rain (6) and squall (7) as wet. Provisional: thunder (14) and thunderstorms (15) count as dry (Carpenters' Landing's `weat/thdr` has lightning and fireflies, no rain generator), and whether an indoor area or a roof keeps the dry set is not known. **Beyond xi-tools:** the rain set (xim reads only `fses`).

**Surface-to-sound mapping** (default footwear `11`, the same ids in every zone that has the terrain; census of zones 0-299, 2026-10-20; other footwear moves the block, `0<t><m><s>` → 100xxx-115xxx):

| Terrain | Dry (`fses`) | Rain (`fser`) | Zones | Foot effect (`fefs` / `fefr` root) |
|---|---|---|---|---|
| 0 Object | none | none | | none |
| 1 Path | 100001 | 100011 | 280 | dust in 70 / ripple in 288 |
| 2 Grass | 100006 | 100015 | 280 (+18 with 141001 / 141004) | 11 / 281 |
| 3 Sand | 100008 | 100016 | 28 | puff in 299 / 297, plus the footprint |
| 4 Snow | 100009 | 100017 | 24 | powder in 299 / 297, plus the footprint |
| 5 Stone | 100005 | 100014 | 298 | none |
| 6 Metal | 100003 | 100013 | 282 (+16 with 141002 / 141003) | 16 / 16 |
| 7 Wood | 100002 | 100012 | 298 | none |
| 8 Shallow water | 100004 | 100004 | 289 | 292 / 290 |
| 9 Deep water | 100007 | 100007 | 289 | 299 / 297 |
| 10 | 100010 | 100018 | 298 | none |

An Object floor (130k of South Gustaberg's 347k floor triangles) has no pointer in any zone, so it is silent, as in xim. Not compared with Windower zone by zone yet: the in-game checks in the lane report cover dirt, stone, sand, snow, wood and rain.

Footprints and foot effects (sand and snow prints, the dust and water splashes) are drawn by the renderer from the same footfalls: [particles.md](../rendering/particles.md#footprints-and-foot-effects-40). The config page's "Footstep effects" toggle (`StockUiSettingKey.FootstepEffects`) turns those off, not the step sounds (the maintainer's retail answer on #40, 2026-10-07: turning it off draws less, for low-end graphics).

## Title music (#378)

The character lobby (title menu, character list, creation) plays the title music on the Music bus: `VeldridViewportControl`'s lobby frames call `GameAudioService.UpdateWithoutWorld(..., titleMusic: true)`, which creates the audio device on a first launch, holds the title track in the `MusicVoicePool` as the heard track while no character is, and fades it in over 1 s; when a character is chosen (`LobbyScreen.Entering`), on the black screen of a Log Out and when the lobby closes it fades out over 1.5 s (both provisional), and the character's zone music follows. The track comes from the lobby Config window's Title Music choice (`GameAudioService.TitleMusicChoice`, `TitleMusic`; labels read from the retail `ROM/97/36` entries 251-257 and 270, 2026-10-20):

| Choice (ROM/97/36) | Track |
|---|---|
| 251 most recent version installed (default) | 58 "A New Direction" (all expansions installed) |
| 252 "Vana'diel March" (base game, Rise of the Zilart) | 108 |
| 253 "Unity" (Chains of Promathia) | 242 |
| 254 "Vana'diel March #4" (Treasures of Aht Urhgan) | 179 |
| 255 "Wings of the Goddess" | 140 |
| 256 "A New Direction" (Seekers of Adoulin) | 58 |
| 257 at random among the installed versions' songs | one of the five, drawn once per lobby visit |
| 270 "We Are Vana'diel" | not offered: no reference gives its track id |

Track ids referenced from LandSandBoat `docs/MusicIDs.txt` ("Title Screen 1 (Original+Zilart)" 108 ... "Title Screen 5 (Seekers)" 58); xi-tools found no sound reference in the title scene `ROM/0/23` or the title UI DATs, so the client picks it. Provisional, for a retail check: which choice is the default and what "most recent version" plays with every expansion installed (listen in the lobby, compare with `/playmusic 58` and the others). Open: the lobby Config window that sets the choice and the title volume (`lobycwin`, [character-lobby.md](character-lobby.md)), and the character-creation race themes and nation-selection march (LandSandBoat lists 130-133, 165-168 and 120 as creation music; not checked in retail).

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
## Combat and action sounds (#41)

Retail keeps an action's sounds in the actor's own motion routines (Section 0x07), so they play with the motion that the action playback (Phase 5D.1, S2C 0x028) starts. Sound commands are ops 0x0A (at the source), 0x0B (at the target) and the 0x4A / 0x53 / 0x60 variants, naming a 0x3D pointer at +0x08 (xi-tools `docs/fx/effect_system.md`); links are 0x03 / 0x3B / 0x57 / 0x3C on the actor and 0x09 on the other actor; 0x3D ... 0x3E is a random choice of one child. What the retail data holds (read 2026-10-07 from monster model 10, the Hume male base motion `ROM/27/82`, battle pack `ROM/32/13` and main weapon DATs 8393-8404; a scan of monster models 1-1499 finds sound commands in 900 of 1,493, mostly `vdam`, `vatk`, `vded`, `vswy`, `sdam`, `chit`, `ati0`-`ati2`, `atf0`):

| Routine | Sounds | Example |
|---|---|---|
| swing `ati0`-`ati2`, `atf0`... | the whoosh `skaz`: a monster's own pointer, a character's battle pack links its weapon DAT's routine `skaz` (0x57 at tick 34) | model 10 `skaz` 6062; Hume male + main weapon 1: 6030 |
| `atk0` (every melee attack) | links the cry `vatk`: a random choice of cries and silent 0x50 entries | model 10: four cries, six silent (40 %); Pinetorum Mandragora (model 301): four cries, three silent (57 %, retail cries on about 60-70 % of its rounds, maintainer 2026-10-07) |
| hit reaction `damg` (on the target) | 0x09 `chit` on the attacker, its own `sdam` and cry `vdam` (random, with silent entries) | a monster's `chit` plays its `shit` (model 10: 6063); a character's `chit` (`ROM/27/82`) links its weapon's `se h`, which plays the weapon's `shit` (main weapon 1: 6031) |
| draw `out0` / sheathe `in 0` | the weapon's `sotr` / `sinr` (0x57 at tick 36) | main weapon 1: 6072 / 6071 |
| `dead` | the death cry `vded` | |

**Beyond xi-tools:** 0x50 is the silent member of a random choice (our reading of `vatk` / `vdam`), and a routine's sound pointer resolves by name within its own DAT. **Dual wield:** a weapon DAT for the sub slot names its routines for the left hand, `skal` (swing), `sehl` (hit, sound pointer `shil`), `efhl`, `sotl` / `sinl` (Hume male sub weapons 64-79; shields carry none), so the main weapon's `skaz` / `se h` are never replaced. No battle pack routine links the left-hand names, so an off-hand action (0x028 `sub_kind` 1) collects its sounds with `skaz` / `se h` / `ef h` read as `skal` / `sehl` / `efhl` (`RoutineSoundCollector.OffHandRoutine`): main-hand hits sound like the main weapon, off-hand hits like the sub (the maintainer's retail answer: a hit from each weapon). Round 1 claimed the sub replaced the main; that was wrong. **Beyond xi-tools.** Since #308 a dual-wielding character plays the dual-wield packs ([entities-and-animation.md](../world/entities-and-animation.md#transient-combat-and-action-animation-phase-5d1)), whose off-hand routines link the left-hand names themselves: `bti*` / `btf0`... link `skal` (at the off-hand swing's own tick, so the whoosh follows the off-hand motion), the off-hand draw / sheathe `in2N` / `ot2N` link `sinl` / `sotl`, the main-hand ones `sinr` / `sotr`. The hit sound still goes through `chit` → `se h`, read as `sehl` for an off-hand hit.

Implementation: `MotionRoutineDecoder` also records these commands in `RawMotionRoutine.SoundCommands` (the playback `Commands` are unchanged); `EntityModelLoader.ParseDatContainer` resolves their names against the DAT's 0x3D pointers; `RoutineSoundCollector.Collect` flattens a routine's sounds through the actor's links (blocking links shift the rest, as for the clips), cached per model (`EntityModel.GetRoutineSounds`). `ActionSoundTracker` (App) follows each drawn actor within 30 yalms each frame on its routine clock (`EntityAnimationState.ActiveRoutine` / `ActionTicks` / `ActionSerial`): it plays a routine's cues as the clock passes them, and when a melee swing or counter starts (`ActionRequest.Motion`) also the actor's `atk0` sounds: retail runs `atk0` for each melee attack (it links `vatk` and picks the swing with op 0x24), while our playback starts the swing directly, so in round 1 no attack cry was ever heard; a new hit reaction (`ReactionSerial`) plays `damg` (hit), `gurd` (guard), `pary` (parry) or `gur1` (block) and the attacker's `chit`; the switch to the death animation plays `dead`. Sounds play at the actor on the Effects bus, full volume within 5 yalms (provisional), fading linearly to silence at 30: the maintainer's retail check (2026-10-07) is that combat sounds fade with distance and are silent by 25-30 yalms, and 30 was chosen (round 1 used 15 / 60). The commands' range fields read 0. A weapon skill, monster move, job ability or spell presentation (#132, [entities-and-animation.md](../world/entities-and-animation.md#action-presentations-132)) plays the sound commands of its DAT's `main`, flattened through the DAT's routines and then the actor's (`EventMotionBank.GetSoundsOnModel`, `ActorSoundState.Cues`); the audio generators that carry most spell and ability sounds are not played yet. Tests: `RoutineSoundCollectorTests` (synthetic and retail), `ActionSoundTrackerTests`.

**Shared hit routines (#328).** Every swing links `dada` (`ROM/0/0`) at its hit, which links `atpr` (additional effect), `crtl` (critical hit) and `dam0` (reaction, spikes). They choose with conditional ops on action-result registers; `SharedHitRoutines` evaluates them for the hit's result and collects the sounds they reach, which `ActionSoundTracker` plays at the target once per melee reaction (`HitReaction.Result`, `Category` basic attack), beside the reaction's own `damg` / `chit`. `ROM/0/0` is read on the thread pool on first use (`ResourceManager.GetSharedHitRoutines`).
- *Ops.* 0x64 / 0x67 / 0x69 / 0x6A / 0x6B are the PS2 client's `IdleIf` / `IdleElse` / `IdleNestIn` / `IdleNestOut` / `IdleInstruction` (xi-tools `docs/reference/ps2_beta_2001.md`). 0x6B layout, read from the retail file (2026-10-18, **Beyond xi-tools**): 16 bytes `u16 0x1C, u16 kind, u32 value` push a register (kind 3) or a constant (kind 1); 12 bytes `u32 operator` pop two and push the result: 0x0C equal, 0x11 bit test, 0x0F (used once, `atpr`'s first test against 0) read as less or equal. 0x64 pops a condition and runs the 0x69 ... 0x6A block after it, else the block after the following 0x67. Only five routines use them (`atpr`, `crtl`, `dam0`, `daml`, `nrm0`).
- *Registers* (**Beyond XiPackets**, matched by the values the routines test against the 0x028 result fields): 0x28 `miss` (`dam0`: 0 `damg`, 1 `sway`, 2 `gurd`, 3 `pary`, 4 `gur1`), 0x2B `info` (`crtl` tests bit 2: info 2 / 3 is a critical hit), 0x2F `proc_kind` (`atpr`: 0 `wapr`, 1-23 `hit3`, `hit5`, `hit9`, `hit8`, `hit4`, `hi10`... in XiPackets' additional-effect order, Enfire first), 0x33 `react_kind` (`dam0`: 1-10 the spikes `sb00`-`sb09`, run on the other actor with op 0x09; 63 `cnt0`, LandSandBoat's counter). Unknown, read as 0: 0x38 (`crtl` picks `hi14` when set, else `hi29`), 0x3B (`damh`, which links `chih`, instead of `damg`), 0x3D (`nrm0`).
- *Sounds* (retail, `SharedHitRoutinesTests`): a normal hit none (`wapr` has none), a critical hit 5031 (`hi29`'s sound generator), Enfire 5033 (`hit3`'s generator `g03s`), Blaze Spikes 5045 (`sb00`'s sound command) and 5033 (`sb00` links `hit3`). Sound generators count as their sound (a 0x05 generator whose linked data is a 0x3D pointer, as for the cutscene files). The routines' ticks are ignored: the sounds play when the reaction lands (all at tick 0 for these).
- *Not done:* the hit sparks themselves (the routines' particle generators: effect-routine work), `sway` / `cnt0` / `damh` (the reaction already plays `gurd` / `pary` / `gur1` / `damg`; a miss plays nothing), the ranged `ldad` and magic `mdam` routines.

**Spell, ability and weapon skill sounds (#328).** A presentation's sounds are its `main`'s sound commands (above) plus its **audio generators**: the generators `main` and the routines it starts (0x03, one pass of 0x73, four deep) spawn or swap in whose linked data is a sound pointer, each at its start tick (`ActionAsset.GeneratorSounds`, merged by tick in `ActionAsset.GetSoundsOnModel`). Most spells and job abilities sound this way: Fire (spell 144, file 2944) `gs00` → 3032 at tick 1, Berserk (job ability 0, file 4412) `g00s` → 7001; Cure (file 2801) has no generator and plays its pointer `0303` (3037) as a sound command (`Retail_SpellsAndAbilities_SoundThroughTheirGeneratorsOrCommands`). A presentation gives the body back before its routine ends (Cure: 121 of 245 ticks), so `ActionSoundTracker` keeps the presentation's clock on wall time after that (a per-actor tail) and still plays its late cues. Generators play one pass (`PlayEffect`), whatever their file's loop flag.

**Frozen targets still sound their hits (#328, round 4 test D29).** A hit on a Macro Test dummy, 0 damage included, played only the swing's `skaz` whoosh. LandSandBoat sends it as a normal hit (`miss` 0, message 1, hit distortion 0), and the dummy's `damg` (model 431, file 1731) does run the attacker's `chit` through its `sdam` (with sound 6005 and the cry `vdam`). But the dummies are frozen: LandSandBoat gives them a permanent Terror (`scripts/globals/macro_test.lua`), which is the motion-stop flag, and the renderer does not advance a frozen entity, where the hit reactions were taken, so no reaction ever reached them. A frozen entity now takes its reactions without moving (`EntityAnimationState.TakeReactionsWhileStopped`), so their sounds play. Checked on the retail data: an Elvaan male monk's hit on model 431 plays the weapon's hit sound 6031, the dummy's 6005 and a cry (`ActionSoundTrackerRealDataTests`). Round 3 had added a fallback for targets "without a chit link", on a misreading of the dummy's routines; it was removed.

**Which results play a reaction (#328, in-game test D8).** The target's `damg` / `gurd` / `pary` / `gur1` and the attacker's `chit` play only for melee and ranged results (`ActionSoundTracker.PlaysReactionRoutine`): `dada` (through `dam0`) and `ldad` (through `daml`) pick them by `miss`, while `mdam`, the result routine of spells, job abilities and weapon skills, runs none (retail `ROM/0/0`: its only commands are 0x01 and 0x2B). Round 1 played a hit's sounds when a Cure landed.

**Target sound variants (D8).** 0x0B plays a sound at the target; 0x4A (PS2 `IdlePlayerTargetSound`) and 0x53 (`IdleNearTargetSound`) are variants of it for different viewers (xi-tools `docs/reference/ps2_beta_2001.md`). Which one retail picks for which viewer is not known, so the variants of one pointer at one tick play once (`RoutineSoundCollector.DropTargetSoundVariants`): Cure's `main` carries 0x53 and 0x0B on `0303` at tick 2, which round 1 played twice.

Provisional: a random choice is uniform; reactions play their routine from its start when applied; the 0x0B "at the target" sounds play at the actor. Not done:
- Ranged attack sounds (`ldad`).

## Phase 5H plan

- [x] Zone effect audio: the auto-run sound generators with range, path and time-of-day volume (#39, above).
- [x] Cross-platform audio backend: OpenAL Soft as the output device (#37, above).
- [x] Clean-room decode of the retail sound files (#38, above, ATRAC3 included).
- [x] Footstep sounds from the gait, collision terrain, footwear and weather (#40, #342, above); footprints and foot effects in [particles.md](../rendering/particles.md#footprints-and-foot-effects-40).
- [x] Lobby title music (#378, above; the lobby Config window's choice open).
- [x] Combat and action sounds from the actors' motion routines (#41, above), the shared `ROM/0/0` hit routines and the presentations' audio generators (#328).
- [x] Event music and volume opcodes (#167, above).
- [x] Ambient zone loops & BGM playback (#42, #114, above).
- [x] UI/menu sound cues (#43, above).
- [x] Master/category volume mixing from the config sliders (#44, above).
- [x] GordianXI sound controls: master off, mute while inactive, multi-box policy, per-category exceptions (#265, above).
