# Audio Engine

> Phase 5H: the sound backend, the retail sound files, and what plays when. Status and open work: [ROADMAP.md](../../ROADMAP.md) and GitHub Issues.

## Backend (#37): recommendation, not yet decided

The maintainer makes the call; nothing backend-specific is in the code yet. Everything below the device is backend-neutral and already works silently on `NullAudioOutput`: decoding (#38), the mixing model, track, cue and footstep choice.

Constraints (issue #37): ATRAC3 is unavoidable (81 of 224 tracks, 1,006 effects) and comes from FFmpeg as a decoder only; music is mostly 44.1 kHz, effects 48 kHz; loops restart at a loop point, not at 0; positional sound with near / far ranges around a listener; long tracks are streamed; Windows, Linux and macOS, no Windows-only APIs. We feed the backend decoded PCM in every case.

| | OpenAL Soft (Silk.NET.OpenAL + `Silk.NET.OpenAL.Soft.Native`) | miniaudio (.NET binding) | SDL3 audio (SDL3-CS) | SDL2 audio (the Silk.NET.SDL 2.23 already shipped) |
|---|---|---|---|---|
| Licence | LGPL-2.1 native (dynamic link only; ship the .so/.dylib/.dll unmodified, notice + relink right), MIT bindings | public domain / MIT-0 native; binding licences vary (MIT for the maintained ones) | zlib native, MIT bindings | zlib native, MIT bindings; already in `THIRD_PARTY_NOTICES.md` |
| Native shipping | Silk.NET's native package covers win-x64/x86/arm64, linux-x64/arm64, osx (universal) | single C file; few bindings ship natives for all three OSes, so we would likely build it in CI (Phase 9) | packages with natives for all three exist (e.g. ppy.SDL3-CS); a second SDL beside the gamepad's SDL2 unless the gamepad moves too | nothing new: the gamepad driver already loads it from `runtimes/<rid>/native` |
| 3D / spatial | built in: listener and sources, distance models (linear clamped fits near / far), HRTF opt-in; only mono sources are spatialised (stereo effects need a downmix) | built in: spatializer per sound (attenuation models, pan, doppler), node graph for buses | none: ours (pan + distance gain, which retail's ADPCM voices need anyway) | none: ours |
| Resampling 44.1 / 48 kHz | built in per source | built in | built in (`SDL_AudioStream`) | ours (linear, in `AudioMixer`) |
| Seamless loop points | `AL_SOFT_loop_points` on static buffers; streamed music loops by our decoder refilling the queue | `ma_data_source_set_loop_point_in_pcm_frames`, or our source | ours (decoder loops) | ours (decoder loops, done: `FfxiSoundStream`) |
| Streaming | buffer queue | custom `ma_data_source` (a callback into managed code) | push into an audio stream | push queue (`SDL_QueueAudio`) |
| Latency | low (device period, configurable) | low | low | ~43 ms queue + device buffer in the prototype |
| Taking our PCM | `alBufferData` from managed buffers | through a data-source callback from a native thread (needs care: no GC allocations, pinned delegates) | push from a managed thread | push from a managed thread |
| Risk | LGPL obligations; one more native stack | binding upkeep and native builds are on us | SDL2/SDL3 duplication until the gamepad driver moves | SDL2 is in maintenance mode; SDL3 is the future |

**Recommendation: a managed mixer over a push-style device, starting on SDL2 audio from the Silk.NET.SDL already shipped, and moving to SDL3 together with the gamepad driver.** Reasons:

- Retail parity is easier when we own the mix: FFXI's positional sound is distance gain and panning, not HRTF; loop points, fades, the category buses the event VM scripts (0x69 / 0x6A) and the server music fade (0x060) are all ours either way. The mixer for this is written and tested (`AudioMixer`).
- No new native dependency or licence for the device. Only FFmpeg (LGPL, dynamic) is added for ATRAC3, and it is needed whatever the device is.
- The push model never calls managed code from a native audio thread.
- A prototype `SdlAudioOutput` over the existing Silk.NET.SDL opened a 48 kHz stereo WASAPI device on the maintainer's machine (2026-10-04) before it was taken out pending this decision.

Choose **OpenAL Soft** instead if true 3D (HRTF, doppler, many hardware-style sources) is wanted later; the mixer's buses then map to per-source gains and its distance model to `AL_LINEAR_DISTANCE_CLAMPED`. **miniaudio** is the most capable single library but the weakest .NET packaging today.

Layout (layer rules from AGENTS.md):

- `Gordian.Core/Audio`: backend-neutral pieces with no device code. `IPcmSource` (a stream of interleaved 16-bit PCM that does its own looping), `PcmClip` (a decoded sound shared by voices), and the retail file decoders (#38).
- `Gordian.App/Audio`:
  - `AudioMixer`: voices resampled (linear) to the output rate, gains (voice x fade x category x script fade x master), constant-power pan, distance falloff, 16-bit clip. Commands from the game thread are queued and applied by the mixing thread, so `Mix` neither locks nor allocates. Up to 96 voices; when full the quietest non-music voice is dropped.
  - `IAudioOutput`: the device seam: open stereo S16 near 48 kHz (the device may pick its own rate; the mixer follows it), report queued frames, accept PCM. `NullAudioOutput` is the only implementation until #37 is decided: the game runs silent.
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
- **ATRAC3** (format 3): not decoded by GordianXI. `FfxiSoundDecoder` hands it to a registered `IAtrac3Decoder` (planned: FFmpeg's decoder, LGPL, dynamically linked, decoder only, issue #37); none is registered yet, so about a third of the music (81 tracks) and 8.5 % of the effects play as silence and are logged once. For a clip the loop frame is taken as `loopStart x blockSize` (provisional, unchecked for ATRAC3).
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

## Phase 5H plan

- [ ] Zone effect audio: ~5.9k Section 0x05 generators link a sound (`0x3D`) with near/far range (`0x4C`), time-of-day volume (`0x43`) and path-following emitters (`0x6B`, shoreline waves); they run on the existing zone particle runtime and need only the sound backend.
- [ ] Select a cross-platform audio backend (#37: recommendation above, decision pending).
- [x] Clean-room decode of the retail sound files (#38, above; ATRAC3 open).
- [ ] Footstep & movement SFX tied to `PlayerLocomotionController`/animation state; the surface under each foot comes from the decoded collision terrain type (`CollisionTriangle.Terrain`: object, path, grass, sand, snow, stone, metal, wood, shallow/deep water), and sand/snow leave footprints. (FFXI has no swimming: water edges are ordinary collision barriers.)
- [ ] Combat/action SFX tied to `CombatPacketModule` action/effect events (`0x028`/`0x030`/`0x0AA`).
- [x] Ambient zone loops & BGM playback (#42, #114, above).
- [ ] UI/menu sound cues (target, cursor move, confirm, cancel).
- [x] Master/category volume mixing from the config sliders (#44, above).
