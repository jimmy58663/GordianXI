# ATRAC3 Decoding (clean-room specification)

> Issue #38 (ATRAC3 part). **Implemented in** `src/Gordian.Core/Audio/Atrac3/` (`Atrac3Stream`, tests `Atrac3DecoderTests`); see [design/audio.md](../design/audio.md#sound-files-38). The specification half of a two-team clean-room implementation: everything a C# implementer needs to decode the ATRAC3 that FFXI ships, without reading any decoder source. Status of the work: [ROADMAP.md](../../ROADMAP.md) and GitHub Issues. The container (`.bgw` / `.spw`) is described in [design/audio.md](../design/audio.md#sound-files-38).

**How this was written.** The bitstream, the arithmetic and the constant tables were studied in FFmpeg's ATRAC3 decoder (LGPL 2.1, `libavcodec/atrac3.c`, `atrac3data.h`, `atrac.c`; fetched 2026-10-04 to a scratch folder outside the repository) and checked against public descriptions (Sony patents, Wikipedia), then restated here in prose, formulas and step lists. Nothing here is FFmpeg code; the implementer must not read FFmpeg either. Wire formats and data tables are given for interoperability, each with its source. FFXI's obfuscation layer is from vgmstream's description (ISC licence, which credits Moogle Toolbox). Every step was then checked by a throw-away decoder written from this document (spec side only, kept outside the repository): it matches FFmpeg's output on FFXI files to float precision (SNR 134 dB; 16-bit output identical except for 0.07 % of samples that differ by 1).

## Contents

1. [Scope: what FFXI uses](#1-scope-what-ffxi-uses)
2. [How FFXI wraps ATRAC3](#2-how-ffxi-wraps-atrac3)
3. [Decoder overview](#3-decoder-overview)
4. [Bit reading](#4-bit-reading)
5. [Sound unit syntax](#5-sound-unit-syntax)
6. [Coefficient value coding](#6-coefficient-value-coding)
7. [Building the spectrum](#7-building-the-spectrum)
8. [Per-band inverse MDCT and window](#8-per-band-inverse-mdct-and-window)
9. [Gain compensation and overlap-add](#9-gain-compensation-and-overlap-add)
10. [QMF synthesis](#10-qmf-synthesis)
11. [Output scaling and sample order](#11-output-scaling-and-sample-order)
12. [Decoder state, reset and loops](#12-decoder-state-reset-and-loops)
13. [Errors and malformed frames](#13-errors-and-malformed-frames)
14. [Out of scope: joint stereo](#14-out-of-scope-joint-stereo)
15. [Tables](#15-tables)
16. [Verification](#16-verification)
17. [Where FFmpeg differs from the patents](#17-where-ffmpeg-differs-from-the-patents)
18. [Open questions](#18-open-questions)
19. [Sources](#19-sources)

## 1. Scope: what FFXI uses

Census of the install (2026-10-04, every ATRAC3 `.bgw` / `.spw` under the sound roots, 1,087 files):

| Kind | Channels | Rate | Files | Looped |
|---|---|---|---|---|
| Music `.bgw` | 2 | 44,100 Hz | 62 | 78 of the 81 music files |
| Music `.bgw` | 2 | 48,000 Hz | 19 | |
| Effect `.spw` | 1 | 48,000 Hz | 873 | 13 of the 1,006 effects |
| Effect `.spw` | 2 | 48,000 Hz | 133 | |

What every file has in common:

- **One frame size: 192 bytes per channel per 1024 samples** (66.15 kbit/s per channel at 44.1 kHz, the "LP2" rate; 72 kbit/s at 48 kHz). A stereo block is 384 bytes: the left channel's 192 bytes, then the right's.
- **No joint stereo.** Every channel's 192 bytes is an independent sound unit (checked on every frame of all 152 stereo files: after de-obfuscation both halves of every block start with the single-channel unit id). Joint stereo is documented in [section 14](#14-out-of-scope-joint-stereo) for completeness only.
- **Spectral coefficients always use variable-length codes** (the constant-length mode bit was 0 in all 1,367,551 channel frames of the install). Constant-length codes still appear inside tonal components, so both codings are needed.
- **Tonal coding mode selector is always 0 or 1** (never 3, never the invalid 2); tonal quantiser selectors seen are 3, 5 and 7, with 1-4 values per component. Half the frames carry tonal components.
- **Coded QMF bands** (`bandsCoded`, below): 0, 1 or 2 in music; 3 occurs in 3 effect frames only. Gain points per band: 0 to 7 all occur (7 in 5 bands); all 16 gain levels occur.
- Gain locations were strictly increasing in every frame; no frame read past its 192 bytes (some use all 1,536 bits); no frame failed to parse.

Out of scope: joint stereo, the RealMedia byte scrambling, the "ATRAC3 AL" variant, more than 2 channels, and other frame sizes (96 and 152 bytes per channel exist in other ATRAC3 files, but not in FFXI; nothing below depends on the frame size except where the frame ends).

## 2. How FFXI wraps ATRAC3

### Header fields for format 3

The common 0x30-byte header is in [design/audio.md](../design/audio.md#sound-files-38) (`FfxiSoundHeader`). For ATRAC3 some fields mean something different from ADPCM:

| Field | `.bgw` offset | `.spw` offset | ATRAC3 meaning |
|---|---|---|---|
| format | 0x0C | 0x0C | 3 |
| size | 0x10 | 0x08 | file size; the frames run from 0x30 to here |
| "blocks" | 0x18 | 0x14 | **total samples per channel**, always `frames x 1024` (checked on all 1,087 files) |
| loopStart | 0x1C | 0x18 | **a sample index** (see [Loops](#loop-points)); negative = one-shot; 0 never occurs |
| rate halves | 0x20, 0x24 | 0x1C, 0x20 | their sum is the sample rate, as for ADPCM |
| data offset | 0x28 | 0x24 | 0x30 |
| byte 0 of the last four | 0x2C | 0x28 | unknown (varies per file) |
| byte 1 | 0x2D | 0x29 | unknown (0x10 in music; mostly 0 or 0x80 in effects) |
| channels | 0x2E | 0x2A | 1 or 2 |
| blockSize | 0x2F | 0x2B | 0 in music, 16 in effects; **meaningless for ATRAC3** |

So `FfxiSoundHeader` as written today (which multiplies blocks by the blockSize byte) gives no usable length for ATRAC3: the ATRAC3 decoder should read total samples and the loop sample directly. Frame count = `(size - 0x30) / (192 x channels)`; it always divides exactly.

### Frame obfuscation

The frames are XOR-obfuscated. The key is the file's first block (`192 x channels` bytes at 0x30) with the first four bytes of each channel's 192-byte part additionally XORed with `A0 02 4E 9F`. Every block (including the first) is XORed byte for byte with that key:

```
key        = file[0x30 .. 0x30 + 192*channels)
for c in 0 .. channels-1:  key[192c + 0..3] ^= A0 02 4E 9F
plain[i]   = file[0x30 + i] ^ key[i mod (192*channels)]
```

Equivalently: the first block decodes to `A0 02 4E 9F 00 00 ... 00` per channel, which is a silent sound unit, and that known plaintext is what makes the scheme reversible. Format referenced from vgmstream `src/meta/bgw_streamfile.h` (ISC licence, "Encrypted ATRAC3 info from Moogle Toolbox"); checked on every file of the install: after XOR every channel frame starts with a valid sound unit header.

**Exception: unobfuscated tail frames.** `music069.bgw`, `music071.bgw` and `music900.bgw` (all one-shot) store their **last 3 blocks without the XOR**. Rule for the decoder, per channel frame: if the de-obfuscated frame's first 6 bits are not `101000` but the raw frame's are, decode the raw frame. Those frames are silent or near silent. No other file needs the rule (the check passes for all other frames, though for a few effects both the raw and the XORed frame start validly because the key's first byte happens to be 0-3; the XORed one is the right one, which is why the rule prefers it).

### Timeline

Decode every frame in order from the first; each frame yields 1024 samples per channel. **Sample 0 of the timeline is the first output sample of frame 0**; the header's total equals frame count x 1024, so the timeline covers exactly the file. Frame 0 is the silent key frame, and with the decoder's own latency the audible content starts later. vgmstream trims the first 2186 samples (`1024 x 2 + 69 x 2`, "observed") and shortens the total by the same amount; whether retail trims is unknown (see [Open questions](#18-open-questions)). The seam can trim or not; the loop arithmetic below is in the untrimmed timeline.

### Loop points

Loop end is always the end of the file (sample `total`). The loop restarts at:

- **Music (`.bgw`): sample `loopStart`.** Measured: for `music040` the 2048 samples before the end correlate 0.9994 with the 2048 before sample `loopStart` (offsets of +-69, +-1024, +-2186 give 0.02-0.22); `music041`: 0.99. A best-lag search on `music040` lands within 1 sample of `total - loopStart`.
- **Effects (`.spw`): sample `loopStart - 1024`** (provisional). Three of the 13 looped effects are self-similar enough to measure (`se036108`, `se036125`, `se041044`): each repeats with period `total - loopStart + 1024` exactly (correlation 0.92-0.999 at that lag, 0.65 or less 3 samples off), and the self-similarity holds right up to the last sample, so the end is `total` and the start is one frame earlier than the field says. The other 10 are not periodic at the seam and cannot decide it. **Differs from vgmstream**, which uses `loopStart` for both kinds.

### The `IAtrac3Decoder` seam

`FfxiSoundDecoder` passes the whole file, the parsed header and the loop flag to `IAtrac3Decoder.Open`, which returns an `IPcmSource` (interleaved 16-bit, `Read` loops by itself, returns 0 only at the end of a one-shot). An implementation:

1. Reads channels, rate, total samples and the loop sample from the file (the fields above), and the frame count from the size.
2. Builds the key and de-obfuscates frames lazily as it streams (music is streamed; effects are decoded whole by `DecodeClip`, whose loop frame should then be the loop sample of this section, not `loopStart x blockSize`).
3. Decodes each channel's 192 bytes per block with that channel's own decoder state (sections 5-11), interleaves L, R.
4. Implements looping as in [section 12](#loops).

## 3. Decoder overview

Per channel, per frame (192 bytes, 1024 output samples):

```
192 bytes ──► sound unit parse ─┬─ gain points (per QMF band)
                                ├─ tonal components (sparse spectral lines)
                                └─ spectral subbands (32 dequantised groups)
spectrum[1024] = subband coefficients + tonal components
for each QMF band b = 0..3:
    X = spectrum[256b .. 256b+255]          (reversed if b is odd)
    Y = window · IMDCT(X)                    (512 samples)
    band b output[256] = gain-compensated (Y first half + previous Y second half)
    keep Y second half for the next frame
1024 samples = QMF synthesis of the 4 band outputs (3 two-band stages)
```

Each QMF band covers a quarter of the audio band (at 44.1 kHz: 0-2.76, 2.76-5.51, 5.51-11.03, 11.03-22.05 kHz; Wikipedia, ATRAC). The bands are not recombined in plain ascending order; see [section 10](#10-qmf-synthesis).

## 4. Bit reading

- The 192 bytes are read as one bit string, **most significant bit of each byte first**, bytes in increasing address order.
- `u(n)`: the next n bits as an unsigned integer, first bit most significant.
- `s(n)`: the next n bits as a two's complement signed integer (top bit is the sign).
- Fields are packed with no alignment or padding between them. Bits after the last field of the unit are filler and are ignored.

## 5. Sound unit syntax

One sound unit fills one channel frame. Fields in order:

### 5.1 Header

| Field | Bits | Meaning |
|---|---|---|
| unit id | u(6) | must be `0x28` (binary `101000`) |
| bandsCoded | u(2) | highest QMF band (0-3) that carries gain points and may carry tonal components |

### 5.2 Gain control points

For each QMF band `b = 0 .. bandsCoded`, in order:

| Field | Bits | Meaning |
|---|---|---|
| pointCount | u(3) | 0-7 gain points for this band |
| then `pointCount` times: level | u(4) | gain level code `L` (0-15); the gain factor is `2^(4 - L)`, so 4 is unity |
| location | u(5) | location code `P` (0-31); the point is at sample `8P` of the band's 256-sample output |

Points are listed in increasing location; a location not greater than the previous one is malformed. Bands above `bandsCoded` have no points this frame. Gain points are stored per band and **used in the next frame** (section 9).

### 5.3 Tonal components

| Field | Bits | Meaning |
|---|---|---|
| groupCount | u(5) | number of tonal groups; if 0, the tonal section ends here |
| modeSelector | u(2) | 0: all groups use VLC; 1: all use CLC; 3: each group carries its own mode bit; 2: invalid |

Then for each group (`groupCount` times):

| Field | Bits | Meaning |
|---|---|---|
| bandMask | `bandsCoded + 1` bits, u(1) each | one flag per QMF band 0 .. bandsCoded: whether this group has components in that band |
| valuesPerComponent | u(3) | plus 1: 1-8 coefficients per component |
| quantSelector | u(3) | 2-7 (0 and 1 are invalid here); selects the value coding and the step (Table D) |
| mode | u(1), only when modeSelector is 3 | 0 VLC, 1 CLC for this group; otherwise the mode is `modeSelector` (0 or 1) |

Then, still inside the group, for each 64-coefficient block `k = 0 .. 4 x (bandsCoded + 1) - 1` (four blocks per QMF band) **whose band's flag (`bandMask` bit `k / 4`) is set**:

| Field | Bits | Meaning |
|---|---|---|
| componentCount | u(3) | 0-7 components in this block |
| then `componentCount` times: scaleIndex | u(6) | index into the scale factor table (Table C) |
| offset | u(6) | the component's first coefficient is at spectrum position `64k + offset` |
| values | see [section 6](#6-coefficient-value-coding) | `n = min(valuesPerComponent, 1024 - position)` values coded with `quantSelector` and the group's mode |

Each component's coefficients are `value x scale(scaleIndex) / maxQuant(quantSelector)` (Tables C, D), placed at `position, position+1, ... position+n-1`. A sound unit holds at most 64 components in total; more is malformed. Blocks of unflagged bands read nothing.

### 5.4 Spectral subbands

The 1024-line spectrum is split into 32 subbands of unequal width (Table A).

| Field | Bits | Meaning |
|---|---|---|
| subbandCount | u(5) | plus 1: subbands 0 .. subbandCount-1 are present (1-32) |
| codingMode | u(1) | 0: variable-length codes; 1: constant-length codes (FFXI: always 0) |
| selector[i] | u(3) each, for i = 0 .. subbandCount-1 | 0: subband not coded (all zero); 1-7: value coding and step (Table D) |
| scaleIndex[i] | u(6) each, **only** for subbands whose selector is not 0, in order | index into Table C |
| values | see section 6, for each subband with a non-zero selector, in order | `width(i)` values (Table A) |

Note the order: all selectors, then all scale indices, then all coefficient data. The coefficients of subband `i` are `value x scale(scaleIndex[i]) / maxQuant(selector[i])` at spectrum positions `start(i) .. start(i+1) - 1`. Subbands not coded, and every subband at or above `subbandCount`, are zero.

The subband count is independent of `bandsCoded`: spectral data may reach QMF bands above `bandsCoded`.

## 6. Coefficient value coding

Both the subbands and the tonal components code their integer values with a **selector** `s` (1-7) and a **mode** (VLC or CLC):

| Selector | CLC | VLC | Values |
|---|---|---|---|
| 1 | 4 bits per **pair**: the high 2 bits are the first value, the low 2 the second, each 2-bit two's complement (`00`=0, `01`=+1, `10`=-2, `11`=-1) | one code per **pair**, Table B1 | pairs; `count / 2` codes for `count` values |
| 2 | s(3) | Table B2 | single |
| 3 | s(3) | Table B3 | single |
| 4 | s(4) | Table B4 | single |
| 5 | s(4) | Table B5 | single |
| 6 | s(5) | Table B6 | single |
| 7 | s(6) | Table B7 | single |

CLC widths per selector 0-7: `0, 4 (per pair), 3, 3, 4, 4, 5, 6`.

Selector 1 is only used by subbands, whose widths are all multiples of 8, so pairs always fill a subband exactly. VLC codes are prefix codes; read bits one at a time (or by table lookup) until a code of Table B matches. The longest code is 8 bits.

## 7. Building the spectrum

1. Clear `spectrum[0..1023]`.
2. Write the dequantised subband coefficients (section 5.4).
3. **Add** every tonal component's coefficients into the spectrum at its positions (section 5.3). Tonal lines add to whatever the subband data put there.

Optional shortcut (not needed for correctness): QMF bands whose 256 spectral lines are all zero produce an all-zero IMDCT, so the transform may be skipped for them; their overlap-add and gain compensation (section 9) must still run, because the previous frame's overlap still contributes.

## 8. Per-band inverse MDCT and window

For each QMF band `b` (0-3), take `X[k] = spectrum[256b + k]`, `k = 0..255`.

1. **Odd bands are reversed:** for `b = 1` and `b = 3` use `X'[k] = X[255 - k]`. (The QMF's spectral inversion in odd bands.)
2. **Inverse MDCT, 256 coefficients to 512 samples**, with this exact sign and scale (verified against FFmpeg's output):

   `y[n] = - Σ_{k=0}^{255} X'[k] · cos( (π / 256) · (n + 1/2 + 128) · (k + 1/2) )`, for `n = 0..511`.

   With these units the final output is in 16-bit sample units (section 11). Any fast algorithm (FFT-based) is fine if it reproduces this formula.
3. **Window:** `Y[n] = y[n] · w[n]`, with the 512-point synthesis window of Table E.

## 9. Gain compensation and overlap-add

Per channel and band, the decoder keeps `overlap[256]` (the second half of the previous frame's `Y`) and `prevPoints` (the gain points decoded in the previous frame for this band; initially none).

Let `curPoints` be the points decoded in this frame for the band (section 5.2; empty if the band is above `bandsCoded`).

1. **Scale for the new half:** `g = 2^(4 - L₀)` where `L₀` is the level of the first point in `curPoints`, or `g = 1` if `curPoints` is empty.
2. **Overlap-add:** `base[n] = Y[n] · g + overlap[n]` for `n = 0..255`.
3. **Gain envelope from `prevPoints`.** If `prevPoints` is empty, `out[n] = base[n]`. Otherwise, with points `(L_i, P_i)`, `i = 0..N-1`, and `L_N = 4` (unity):
   - start at `pos = 0`; for each point `i` in order:
     - `f = 2^(4 - L_i)`; for `pos` up to (not including) `8·P_i`: `out[pos] = base[pos] · f`;
     - then for the 8 samples `pos = 8·P_i .. 8·P_i + 7`: `out[pos] = base[pos] · f`, then `f = f · 2^((L_i - L_{i+1}) / 8)` (a geometric ramp that reaches the next level's factor after 8 samples);
   - after the last point's ramp, to the end of the band (`pos` up to 255): `out[pos] = base[pos]` (factor 1).
4. **Save state:** `overlap = Y[256..511]` (the windowed IMDCT's second half, **without** the scale `g`), `prevPoints = curPoints`.

`out[0..255]` is the band's time signal for this frame. The largest location (31) ends its ramp at sample 255, so the envelope never runs past the band.

Meaning (for orientation, not needed to implement): the encoder amplified transient regions; the points sent in frame t describe the envelope over the region where frame t's window overlaps frame t+1, so they are applied a frame later, and `g` brings the new frame's first half to the same reference level.

## 10. QMF synthesis

Three two-band synthesis stages recombine the four band signals `B0..B3` (each 256 samples) into 1024 output samples. Each stage has its own persistent 46-sample delay line per channel.

**One stage** `Q(lo, hi)` with `N`-sample inputs, delay line `D[0..45]`, and the 48-tap filter `h` (Table F) produces `2N` samples:

1. Build `u` of length `46 + 2N`: `u[0..45] = D`; for `i = 0..N-1`: `u[46 + 2i] = lo[i] + hi[i]`, `u[47 + 2i] = lo[i] - hi[i]`.
2. For `j = 0..N-1`:
   - `out[2j]     = Σ_{m = 1, 3, 5, ..., 47} u[2j + m] · h[m]` (odd taps),
   - `out[2j + 1] = Σ_{m = 0, 2, 4, ..., 46} u[2j + m] · h[m]` (even taps).
3. `D = u[2N .. 2N + 45]` (the last 46 values).

**The three stages, in order:**

| Stage | lo input | hi input | N | Output |
|---|---|---|---|---|
| 1 | `B0` | `B1` | 256 | `A` (512) |
| 2 | **`B3`** | **`B2`** | 256 | `C` (512) |
| 3 | `A` | `C` | 512 | the frame's 1024 samples |

Stage 2 takes band 3 as its low input and band 2 as its high input (the upper half-band is spectrally inverted). Getting this or the odd-band reversal of section 8 wrong gives audible but plausible-sounding garbage, so test with the harness.

## 11. Output scaling and sample order

The QMF output is in 16-bit units. To match FFmpeg's float output divide by 32768; for 16-bit PCM use `clamp(round(x), -32768, 32767)` directly on the QMF output (round half to even). Expect at most 1 LSB difference from FFmpeg's 16-bit output (float rounding order differs).

Within a frame the 1024 samples are in time order. Stereo output interleaves left (the block's first 192 bytes) and right (its second 192).

## 12. Decoder state, reset and loops

Per channel, persistent across frames (everything else is rebuilt from each frame):

| State | Size | Initial value |
|---|---|---|
| `overlap` | 4 bands x 256 floats | 0 |
| `prevPoints` | 4 bands x up to 7 (level, location) | no points |
| QMF delay lines | 3 x 46 floats | 0 |

(Joint stereo would add per-pair history; section 14.) A fresh decoder starts with these values; that is also the state for decoding from frame 0 again.

### Loops

To loop to sample `S` (section 2: `loopStart` for music, `loopStart - 1024` for effects): let `F = S div 1024`, `r = S mod 1024`.

- **Recommended (exact): snapshot.** On the first pass, just before decoding frame `F`, copy the state of every channel (above table, about 4.7 KB per channel). At the end of the last frame, restore the snapshot, decode frame `F`, skip its first `r` samples, continue. The looped output is then sample-for-sample the first pass's samples `S, S+1, ...`, so the seam behaves as if the PCM had been looped (the harness's `loop2` reference is built that way).
- **Alternative (also exact): pre-roll.** Reset the state, decode from frame `max(0, F - 2)` and discard output up to sample `S`. The state before frame `F` depends only on frames `F - 1` and `F - 2`: `overlap` and `prevPoints` come from frame `F - 1` alone, and each QMF delay line holds only the last 46 values of its stage's input, which (N is at least 256) come entirely from frame `F - 1`'s band signals, which in turn need frame `F - 2`'s overlap and points. So two frames of pre-roll reproduce the snapshot state; it costs two extra frame decodes per loop. Checked with the spec-side decoder on `music041`: started fresh at frame 98, its output differs from a continuous decode in frames 98 and 99 and matches it to 3e-7 from frame 100 on.
- Never loop by jumping to frame `F` with the end-of-file state: the overlap from the last frame would be added to frame `F`, giving a click.

The same snapshot works for one-shot sounds restarted from the start: the state for frame 0 is the fresh state.

## 13. Errors and malformed frames

FFXI's data is clean (no parse errors anywhere in the install), but the decoder must not crash on bad input. Malformed conditions: unit id not `0x28` (after the tail-frame rule of section 2), gain locations not increasing, tonal mode selector 2, tonal quant selector 0 or 1, more than 64 tonal components, a VLC prefix that matches no code, reading past the 192 bytes. FFmpeg rejects the whole frame for these and outputs nothing for it. Recommended for GordianXI: output 1024 zero samples for that channel frame, keep the state as it was before the frame (or reset it), and log once per file.

## 14. Out of scope: joint stereo

Unused by FFXI, untested here (no FFXI file to check against), recorded so a future reader knows where it plugs in. Source: FFmpeg's decoder; not checked against Sony's documents.

- A stereo pair shares one block. Unit 1 is read from the block's start as above. Unit 2 is stored **byte-reversed from the block's end**: reverse the block's bytes, skip leading bytes equal to `0xF8`, then read: a 1-bit weighting flag and a 3-bit weighting index, four 2-bit matrix selectors (one per QMF band), then unit 2, whose id is `u(2) = 3` instead of `u(6) = 0x28`, continuing with `bandsCoded` as usual.
- Both units are synthesised per band (sections 7-9). Before QMF synthesis, each QMF band's pair of time signals is recombined with the matrix selector **from two frames earlier** (a three-deep history, initial value 3); where the selector changes between consecutive frames, the first 8 samples of the band crossfade between the two matrices. Then a weighting stage (history of three flag/index pairs, initial (0, 7)) scales bands 1-3 of each channel. FFmpeg's weighting interpolation reads the old weights' left and right values where the old and new weights of one channel would be expected, which looks like a bug; anyone implementing joint stereo should test against a real joint-stereo file.

## 15. Tables

All tables were taken from FFmpeg (`libavcodec/atrac3data.h` and `atrac.c`, 2026-10-04) unless marked otherwise, and re-checked by the spec-side decoder against FFmpeg's output.

### Table A: subband edges

Subband `i` covers spectrum lines `start(i) .. start(i+1) - 1`:

```
start = 0, 8, 16, 24, 32, 40, 48, 56,
        64, 80, 96, 112, 128, 144, 160, 176,
        192, 224, 256, 288, 320, 352, 384, 416,
        448, 480, 512, 576, 640, 704, 768, 896,
        1024
```

Widths: 8 (subbands 0-7), 16 (8-15), 32 (16-25), 64 (26-29), 128 (30-31).

### Table B: VLC codes

Codes are written first bit first. They are canonical prefix codes: assigned in the listed order with increasing code value and non-decreasing length (each table's Kraft sum is exactly 1). Value = the signed integer decoded.

**B1, selector 1 (pairs)**

| Code | Pair (first, second) |
|---|---|
| `0` | (0, 0) |
| `100` | (0, 1) |
| `101` | (0, -1) |
| `1100` | (1, 0) |
| `1101` | (-1, 0) |
| `11100` | (1, 1) |
| `11101` | (1, -1) |
| `11110` | (-1, 1) |
| `11111` | (-1, -1) |

**B2, selector 2** (values -2..2)

| Value | Code | Value | Code |
|---|---|---|---|
| 0 | `0` | | |
| +1 | `100` | -1 | `101` |
| +2 | `110` | -2 | `111` |

**B3, selector 3** (values -3..3)

| Value | Code | Value | Code |
|---|---|---|---|
| 0 | `0` | | |
| +1 | `100` | -1 | `101` |
| +2 | `1100` | -2 | `1101` |
| +3 | `1110` | -3 | `1111` |

**B4, selector 4** (values -4..4)

| Value | Code | Value | Code |
|---|---|---|---|
| 0 | `0` | | |
| +1 | `100` | -1 | `101` |
| +2 | `1100` | -2 | `1101` |
| +3 | `11100` | -3 | `11101` |
| +4 | `11110` | -4 | `11111` |

**B5, selector 5** (values -7..7)

| Value | Code | Value | Code |
|---|---|---|---|
| 0 | `00` | | |
| +1 | `010` | -1 | `011` |
| +2 | `1000` | -2 | `1001` |
| +3 | `1010` | -3 | `1011` |
| +7 | `1100` | -7 | `1101` |
| +4 | `11100` | -4 | `11101` |
| +5 | `111100` | -5 | `111101` |
| +6 | `111110` | -6 | `111111` |

**B6, selector 6** (values -15..15)

| Value | Code | Value | Code |
|---|---|---|---|
| 0 | `000` | | |
| +1 | `0010` | -1 | `0011` |
| +2 | `0100` | -2 | `0101` |
| +3 | `0110` | -3 | `0111` |
| +15 | `1000` | -15 | `1001` |
| +4 | `10100` | -4 | `10101` |
| +5 | `10110` | -5 | `10111` |
| +6 | `11000` | -6 | `11001` |
| +7 | `110100` | -7 | `110101` |
| +8 | `110110` | -8 | `110111` |
| +9 | `111000` | -9 | `111001` |
| +10 | `111010` | -10 | `111011` |
| +11 | `1111000` | -11 | `1111001` |
| +12 | `1111010` | -12 | `1111011` |
| +13 | `1111100` | -13 | `1111101` |
| +14 | `1111110` | -14 | `1111111` |

**B7, selector 7** (values -31..31)

| Value | Code | Value | Code |
|---|---|---|---|
| 0 | `000` | | |
| +31 | `0010` | -31 | `0011` |
| +1 | `01000` | -1 | `01001` |
| +2 | `01010` | -2 | `01011` |
| +3 | `01100` | -3 | `01101` |
| +4 | `01110` | -4 | `01111` |
| +5 | `10000` | -5 | `10001` |
| +6 | `100100` | -6 | `100101` |
| +7 | `100110` | -7 | `100111` |
| +8 | `101000` | -8 | `101001` |
| +9 | `101010` | -9 | `101011` |
| +10 | `101100` | -10 | `101101` |
| +11 | `101110` | -11 | `101111` |
| +12 | `110000` | -12 | `110001` |
| +13 | `110010` | -13 | `110011` |
| +14 | `1101000` | -14 | `1101001` |
| +15 | `1101010` | -15 | `1101011` |
| +16 | `1101100` | -16 | `1101101` |
| +17 | `1101110` | -17 | `1101111` |
| +18 | `1110000` | -18 | `1110001` |
| +19 | `1110010` | -19 | `1110011` |
| +20 | `1110100` | -20 | `1110101` |
| +21 | `11101100` | -21 | `11101101` |
| +22 | `11101110` | -22 | `11101111` |
| +23 | `11110000` | -23 | `11110001` |
| +24 | `11110010` | -24 | `11110011` |
| +25 | `11110100` | -25 | `11110101` |
| +26 | `11110110` | -26 | `11110111` |
| +27 | `11111000` | -27 | `11111001` |
| +28 | `11111010` | -28 | `11111011` |
| +29 | `11111100` | -29 | `11111101` |
| +30 | `11111110` | -30 | `11111111` |

### Table C: scale factors

`scale(i) = 2^((i - 15) / 3)` for `i = 0..63` (so `scale(15) = 1`, `scale(0) = 1/32`, `scale(63) = 65536`). Compute in double and store as float, as FFmpeg does.

### Table D: quantiser maxima per selector

| Selector | 0 | 1 | 2 | 3 | 4 | 5 | 6 | 7 |
|---|---|---|---|---|---|---|---|---|
| maxQuant | (not coded) | 1.5 | 2.5 | 3.5 | 4.5 | 7.5 | 15.5 | 31.5 |
| CLC bits | 0 | 4 per pair | 3 | 3 | 4 | 4 | 5 | 6 |

Coefficient = `value x scale(scaleIndex) / maxQuant(selector)` (FFmpeg multiplies by the stored reciprocal `1 / maxQuant`, a float; the difference is below the 1-LSB tolerance).

### Table E: IMDCT synthesis window (512 points)

With `a(n) = 1 + sin(π · ((n + 1/2) / 256 - 1/2))` for `n = 0..255`:

`w[n] = 2 · a(n) / (a(n)² + a(255 - n)²)` for `n = 0..255`, and `w[511 - n] = w[n]`.

Compute in double, store as float. Check values: `w[0] = 9.413e-06`, `w[1] = 8.4723e-05`, `w[64] = 0.198977423`, `w[127] = 0.993826699`, `w[128] = 1.006098006`, `w[200] = 1.108092771`, `w[255] = 1.000009412`. (The window exceeds 1: it is the synthesis half of a window pair normalised for perfect reconstruction with the encoder's analysis window. Formula also described on the MultimediaWiki RealAudio "atrc" page.)

### Table F: QMF filter (48 taps)

The first 24 taps `q[0..23]`; the filter is symmetric and doubled: `h[m] = h[47 - m] = 2 · q[m]` for `m = 0..23`.

```
q[ 0.. 5] = -0.00001461907, -0.00009205479, -0.000056157569,  0.00030117269,  0.0002422519,  -0.00085293897
q[ 6..11] = -0.0005205574,   0.0020340169,   0.00078333891,  -0.0042153862,  -0.00075614988,  0.0078402944
q[12..17] = -0.000061169922,-0.01344162,     0.0024626821,    0.021736089,   -0.007801671,   -0.034090221
q[18..23] =  0.01880949,     0.054326009,   -0.043596379,    -0.099384367,    0.13207909,     0.46424159
```

(Sum of all 48 `h` taps: 1.99989.) The same filter is ATRAC1's.

### Table G: gain levels and ramps

Level code `L` gives factor `2^(4 - L)`: 16, 8, 4, 2, 1, 1/2, ..., 2^-11 for `L = 0..15`. Ramp step for a change from level `L_a` to `L_b`: `2^((L_a - L_b) / 8)` per sample over 8 samples. Location code `P` is sample `8P` (8-sample resolution, 32 positions per 256-sample band).

### Table H: FFXI constants

| Constant | Value | Source |
|---|---|---|
| frame bytes per channel | 192 | install census; vgmstream |
| samples per frame per channel | 1024 | ATRAC3 |
| obfuscation mask on each channel's first 4 key bytes | `A0 02 4E 9F` | vgmstream (Moogle Toolbox) |
| loop start, music | `loopStart` | measured (section 2) |
| loop start, effects | `loopStart - 1024` | measured on 3 effects, provisional |
| vgmstream start trim | 2186 samples | vgmstream ("observed"); retail unknown |

## 16. Verification

### Reference output (outside the repository)

The harness lives in the session scratchpad, `C:\Users\jimmy\AppData\Local\Temp\claude\G--git-GordianXI\3e0b80ea-4984-4bef-948b-2d7afb6c7796\scratchpad\atrac3-ref\` (its `README.md` has the exact commands). It does not go in the repo: the inputs and outputs are Square Enix data. What it does:

1. De-obfuscates the frames (section 2, including the tail-frame rule).
2. Wraps them in a RIFF WAVE that FFmpeg's ATRAC3 decoder accepts: format tag `0x0270`, channels, rate, block align `192 x channels`, bits per sample 0, and a 14-byte extra block `u16 1, u32 2048 x channels, u16 0 (joint stereo off), u16 0, u16 1, u16 0`, then a `fact` chunk with the total samples and the `data` chunk.
3. Runs `ffmpeg -i in.wav -c:a pcm_f32le -f f32le out.f32` and `-c:a pcm_s16le out.s16.wav` (FFmpeg 9.0.2, `atrac3` decoder).
4. For looped files also writes `name.loop2.s16.wav`: the first pass followed by samples `[loopStart, total)` (raw `loopStart`, for both kinds; for effects the expected loop per section 2 starts 1024 samples earlier, so compare effect loops against the first pass directly).

Files: `music040`, `music041` (44.1 kHz stereo, looped), `music069` (48 kHz stereo, one-shot, unobfuscated tail), `music900` (44.1 kHz, one-shot, unobfuscated tail), `se036124` (48 kHz stereo effect, looped), `se041035`, `se041044` (48 kHz mono effects, looped).

### Acceptance for the C# decoder

- Same sample count as the header total; same timeline (no trimming inside the decoder).
- Against `name.f32`: max absolute error below `1e-5` (the spec-side check reached `5e-7`), or SNR above 100 dB.
- Against `name.s16.wav`: every sample within 1.
- Loop: decoding past the end with the snapshot method gives exactly the first-pass samples from the loop sample on.
- A test per variant: stereo 44.1 kHz music, stereo 48 kHz music, mono and stereo effects, a tail-frame file. Tests that need the install should skip without it, like `FfxiSoundDecodeTests`.

## 17. Where FFmpeg differs from the patents

The patents describe Sony's methods in general terms and leave most numbers open; FFmpeg (and FFXI's data) fix them. Where the two disagree or the patent gives a different choice:

1. **Gain ramp shape and length.** US 5,974,379 describes smooth (for example sine-shaped) gain transitions lasting on the order of milliseconds, over sub-blocks. The bitstream as FFmpeg decodes it uses a geometric (exponential) ramp of exactly 8 samples per gain point, with 8-sample location resolution; at 44.1 kHz an 8-sample ramp in a band sampled at 1/4 of the rate is about 0.7 ms.
2. **Where the gain is applied.** US 5,974,379 applies the reciprocal gain function over the whole overlapped transform block. FFmpeg applies the previous frame's gain points to the overlap-added output of the current frame and only a single constant (the first level of the current frame's points) to the current block's first half; the second half is saved unscaled.
3. **Tonal component position.** US 5,758,316 records a tonal component's position as its centre line or its lowest line; ATRAC3 (FFmpeg) codes the lowest line, as a 6-bit offset inside a 64-line block.
4. **Tonal components shared between channels.** US 5,758,316 lets plural channels share (sum and halve) tonal components. ATRAC3 as FFmpeg decodes it codes tonal components per channel, with no sharing, even in joint stereo.
5. **"Not coded" quantiser codes.** US 5,758,316 says a quantisation step of zero means "not encoded". In ATRAC3's tonal section FFmpeg treats quantiser selectors 0 and 1 as invalid (only the spectral subbands use 0 for "not coded").
6. **Joint stereo weighting.** FFmpeg's interpolation of the channel weights does not interpolate each channel from its old to its new weight (section 14); this is FFmpeg-specific and probably a bug, not a patent difference, but it is the one place where FFmpeg's behaviour is suspect.

Not checked: the joint stereo matrixing and the QMF band arrangement against any Sony document (no patent found that gives them in decoder terms).

## 18. Open questions

- **Start trim.** Does retail skip the decoder's start-up latency (vgmstream trims 2186 samples)? Frame 0 is always silent, so not trimming only delays the start by about 50 ms. Settle: a retail recording of a track start.
- **Effect loop offset** (`loopStart - 1024`) rests on 3 effects; a retail recording of a looped ATRAC3 effect (or a fourth periodic effect) would confirm it.
- **Header bytes 0x2C / 0x2D** (`.bgw`) and 0x28 / 0x29 (`.spw`): not needed to decode; meaning unknown.
- **Retail's handling of the unobfuscated tail frames** (`music069/071/900`): unknown; the rule in section 2 decodes them as the silence they contain.

## 19. Sources

- FFmpeg, `libavcodec/atrac3.c`, `libavcodec/atrac3data.h`, `libavcodec/atrac.c`, `libavcodec/atrac.h` (LGPL 2.1), https://github.com/FFmpeg/FFmpeg, master as of 2026-10-04. Bitstream syntax, tables, arithmetic. Studied by the spec author only; not copied.
- FFmpeg CLI 9.0.2 (`atrac3` decoder), as a black-box reference decoder.
- vgmstream, `src/meta/bgw.c` and `src/meta/bgw_streamfile.h` (ISC licence), https://github.com/vgmstream/vgmstream: FFXI's XOR obfuscation (credited there to Moogle Toolbox, https://sourceforge.net/projects/mogbox/), the 192-byte frame size, the 2186-sample start trim, the unobfuscated tail frames.
- xi-tools `docs/audio/format.md` (https://github.com/vekien/xi-tools): the `.bgw` / `.spw` header.
- Sony patents: US 5,974,379 (gain control ahead of attacks and in releases), US 5,825,320 (gain control method for audio encoding), US 5,758,316 (tonal components of plural channels); via Google Patents and the minidisc.org patent index (https://minidisc.org/patents).
- Wikipedia, "Adaptive Transform Acoustic Coding" (ATRAC3 band split, gain control, tonal components, LP2/LP4 rates), https://en.wikipedia.org/wiki/ATRAC.
- MultimediaWiki, "ATRAC3" and RealAudio "atrc" pages (window formula), https://wiki.multimedia.cx/index.php/ATRAC3.
- Retail install measurements (census, loop seams): this document, 2026-10-04.
