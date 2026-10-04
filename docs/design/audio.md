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

## Phase 5H plan

- [ ] Zone effect audio: ~5.9k Section 0x05 generators link a sound (`0x3D`) with near/far range (`0x4C`), time-of-day volume (`0x43`) and path-following emitters (`0x6B`, shoreline waves); they run on the existing zone particle runtime and need only the sound backend.
- [ ] Select a cross-platform audio backend (#37: recommendation above, decision pending).
- [ ] Clean-room decode of the retail sound files (BGM `.bgw`, sound effects `.spw`).
- [ ] Footstep & movement SFX tied to `PlayerLocomotionController`/animation state; the surface under each foot comes from the decoded collision terrain type (`CollisionTriangle.Terrain`: object, path, grass, sand, snow, stone, metal, wood, shallow/deep water), and sand/snow leave footprints. (FFXI has no swimming: water edges are ordinary collision barriers.)
- [ ] Combat/action SFX tied to `CombatPacketModule` action/effect events (`0x028`/`0x030`/`0x0AA`).
- [ ] Ambient zone loops & BGM playback tied to `WorldState.ZoneChanged`.
- [ ] UI/menu sound cues (target, cursor move, confirm, cancel).
- [ ] Master/category volume mixing (SFX/BGM/Ambient/UI) with persisted settings.
