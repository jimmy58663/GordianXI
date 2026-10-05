// src/Gordian.Core/Audio/Atrac3/Atrac3Channel.cs
using System;

namespace Gordian.Core.Audio.Atrac3
{
    /// <summary>
    /// The persistent per-channel state of the ATRAC3 decoder (spec <c>docs/audio/atrac3.md</c> section 12): the IMDCT
    /// overlap of each QMF band, the gain points of the previous frame, and the three QMF delay lines. Everything else is
    /// rebuilt from each frame, so copying this state is an exact loop snapshot.
    /// </summary>
    internal sealed class Atrac3ChannelState
    {
        /// <summary>Gain points a band can carry (u(3)).</summary>
        public const int MaxGainPoints = 7;

        /// <summary>QMF delay line length per stage.</summary>
        public const int QmfDelay = 46;

        /// <summary>The second half of each band's previous windowed IMDCT, 4 x 256.</summary>
        public readonly float[] Overlap = new float[4 * Atrac3Tables.BandSamples];

        /// <summary>Gain point count per band of the previous frame.</summary>
        public readonly byte[] PointCount = new byte[4];

        /// <summary>Gain levels of the previous frame, 4 x 8 (the 8th slot is unused).</summary>
        public readonly byte[] PointLevel = new byte[4 * 8];

        /// <summary>Gain locations of the previous frame, 4 x 8.</summary>
        public readonly byte[] PointLocation = new byte[4 * 8];

        /// <summary>The delay lines of the three QMF stages, 3 x 46.</summary>
        public readonly float[] Qmf = new float[3 * QmfDelay];

        /// <summary>Back to the fresh decoder state (no overlap, no gain points, silent filters).</summary>
        public void Reset()
        {
            Array.Clear(Overlap);
            Array.Clear(PointCount);
            Array.Clear(PointLevel);
            Array.Clear(PointLocation);
            Array.Clear(Qmf);
        }

        /// <summary>Copies this state into <paramref name="target"/>.</summary>
        public void CopyTo(Atrac3ChannelState target)
        {
            Overlap.CopyTo(target.Overlap, 0);
            PointCount.CopyTo(target.PointCount, 0);
            PointLevel.CopyTo(target.PointLevel, 0);
            PointLocation.CopyTo(target.PointLocation, 0);
            Qmf.CopyTo(target.Qmf, 0);
        }
    }

    /// <summary>
    /// Decodes one channel's ATRAC3 sound units (one 192-byte unit per 1024 samples, no joint stereo) into float samples
    /// in 16-bit units: unit parse (gain points, tonal components, spectral subbands), spectrum, per-band IMDCT and
    /// window, gain compensation with overlap-add, and the three-stage QMF synthesis.
    /// </summary>
    /// <remarks>
    /// Implemented from the clean-room specification <c>docs/audio/atrac3.md</c> (sections 4-13), which describes the
    /// format from FFmpeg's reverse-engineered ATRAC3 decoder (https://github.com/FFmpeg/FFmpeg, <c>libavcodec/atrac3.c</c>)
    /// and Sony's patents US 5,974,379 (gain control) and US 5,758,316 (tonal components). No decoder source was read.
    /// The hot path allocates nothing: every buffer is owned by the instance.
    /// </remarks>
    internal sealed class Atrac3Channel
    {
        private const int UnitId = 0x28;
        private const int MaxTonalComponents = 64;
        private const int MaxTonalValues = 8;

        private readonly Atrac3Imdct _imdct = new();

        // Per-frame scratch (never carried between frames).
        private readonly byte[] _curCount = new byte[4];
        private readonly byte[] _curLevel = new byte[4 * 8];
        private readonly byte[] _curLocation = new byte[4 * 8];
        private readonly short[] _tonalPosition = new short[MaxTonalComponents];
        private readonly byte[] _tonalLength = new byte[MaxTonalComponents];
        private readonly float[] _tonalValues = new float[MaxTonalComponents * MaxTonalValues];
        private readonly byte[] _selector = new byte[32];
        private readonly byte[] _scaleIndex = new byte[32];
        private readonly float[] _spectrum = new float[Atrac3Tables.FrameSamples];
        private readonly float[] _coefficients = new float[Atrac3Tables.BandSamples];
        private readonly float[] _transform = new float[2 * Atrac3Tables.BandSamples];
        private readonly float[] _bands = new float[Atrac3Tables.FrameSamples];
        private readonly float[] _stageA = new float[512];
        private readonly float[] _stageC = new float[512];
        private readonly float[] _qmfWork = new float[Atrac3ChannelState.QmfDelay + Atrac3Tables.FrameSamples];
        private int _tonalCount;

        /// <summary>The persistent state (overlap, previous gain points, QMF delays).</summary>
        public Atrac3ChannelState State { get; } = new();

        /// <summary>
        /// Decodes one sound unit into 1024 samples. On a malformed unit writes silence, leaves the state as it was
        /// before the unit and returns false (spec section 13).
        /// </summary>
        /// <param name="unit">The channel's de-obfuscated 192 bytes.</param>
        /// <param name="output">1024 samples in 16-bit units (not clamped).</param>
        public bool Decode(ReadOnlySpan<byte> unit, Span<float> output)
        {
            if (!Parse(unit, out int bandsCoded))
            {
                output.Slice(0, Atrac3Tables.FrameSamples).Clear();
                return false;
            }

            Synthesize(bandsCoded, output);
            return true;
        }

        /// <summary>Reads the unit into the scratch buffers; touches no persistent state.</summary>
        private bool Parse(ReadOnlySpan<byte> unit, out int bandsCoded)
        {
            var bits = new Atrac3BitReader(unit);
            bandsCoded = 0;
            if (bits.Read(6) != UnitId)
            {
                return false;
            }

            bandsCoded = bits.Read(2);

            // 5.2 Gain control points.
            for (int b = 0; b < 4; b++)
            {
                if (b > bandsCoded)
                {
                    _curCount[b] = 0;
                    continue;
                }

                int count = bits.Read(3);
                _curCount[b] = (byte)count;
                for (int i = 0; i < count; i++)
                {
                    int level = bits.Read(4);
                    int location = bits.Read(5);
                    if (i > 0 && location <= _curLocation[b * 8 + i - 1])
                    {
                        return false;
                    }

                    _curLevel[b * 8 + i] = (byte)level;
                    _curLocation[b * 8 + i] = (byte)location;
                }
            }

            // 5.3 Tonal components.
            _tonalCount = 0;
            int groups = bits.Read(5);
            if (groups > 0)
            {
                int modeSelector = bits.Read(2);
                if (modeSelector == 2)
                {
                    return false;
                }

                for (int g = 0; g < groups; g++)
                {
                    int mask = bits.Read(bandsCoded + 1);
                    int valuesPerComponent = bits.Read(3) + 1;
                    int quant = bits.Read(3);
                    if (quant < 2)
                    {
                        return false;
                    }

                    bool clc = (modeSelector == 3 ? bits.Read(1) : modeSelector) == 1;
                    float step = Atrac3Tables.InverseMaxQuant[quant];
                    int blocks = 4 * (bandsCoded + 1);
                    for (int k = 0; k < blocks; k++)
                    {
                        if (((mask >> (bandsCoded - (k >> 2))) & 1) == 0)
                        {
                            continue;
                        }

                        int components = bits.Read(3);
                        for (int c = 0; c < components; c++)
                        {
                            if (_tonalCount >= MaxTonalComponents)
                            {
                                return false;
                            }

                            int scaleIndex = bits.Read(6);
                            int position = 64 * k + bits.Read(6);
                            int n = Math.Min(valuesPerComponent, Atrac3Tables.FrameSamples - position);
                            float factor = Atrac3Tables.ScaleFactor[scaleIndex] * step;
                            Span<float> values = _tonalValues.AsSpan(_tonalCount * MaxTonalValues, n);
                            if (!ReadValues(ref bits, quant, clc, values, factor))
                            {
                                return false;
                            }

                            _tonalPosition[_tonalCount] = (short)position;
                            _tonalLength[_tonalCount] = (byte)n;
                            _tonalCount++;
                        }
                    }

                    if (bits.Overrun)
                    {
                        return false;
                    }
                }
            }

            // 5.4 Spectral subbands: all selectors, then the scale indices of the coded ones, then the values.
            int subbands = bits.Read(5) + 1;
            bool subbandClc = bits.Read(1) == 1;
            for (int i = 0; i < subbands; i++)
            {
                _selector[i] = (byte)bits.Read(3);
            }

            for (int i = 0; i < subbands; i++)
            {
                if (_selector[i] != 0)
                {
                    _scaleIndex[i] = (byte)bits.Read(6);
                }
            }

            // 7. The spectrum: subband coefficients, then the tonal components added on top.
            Span<float> spectrum = _spectrum;
            spectrum.Clear();
            ReadOnlySpan<short> edges = Atrac3Tables.SubbandStart;
            for (int i = 0; i < subbands; i++)
            {
                int selector = _selector[i];
                if (selector == 0)
                {
                    continue;
                }

                float factor = Atrac3Tables.ScaleFactor[_scaleIndex[i]] * Atrac3Tables.InverseMaxQuant[selector];
                if (!ReadValues(ref bits, selector, subbandClc, spectrum.Slice(edges[i], edges[i + 1] - edges[i]), factor))
                {
                    return false;
                }
            }

            if (bits.Overrun)
            {
                return false;
            }

            for (int c = 0; c < _tonalCount; c++)
            {
                Span<float> lines = spectrum.Slice(_tonalPosition[c], _tonalLength[c]);
                ReadOnlySpan<float> values = _tonalValues.AsSpan(c * MaxTonalValues, _tonalLength[c]);
                for (int i = 0; i < lines.Length; i++)
                {
                    lines[i] += values[i];
                }
            }

            return true;
        }

        /// <summary>
        /// Section 6: reads <c>destination.Length</c> values coded with <paramref name="selector"/> (1-7) and writes
        /// <c>value x factor</c>. Selector 1 codes pairs. False when a VLC prefix matches no code.
        /// </summary>
        private static bool ReadValues(ref Atrac3BitReader bits, int selector, bool clc, Span<float> destination, float factor)
        {
            if (selector == 1)
            {
                int pairs = destination.Length >> 1;
                for (int p = 0; p < pairs; p++)
                {
                    int first;
                    int second;
                    if (clc)
                    {
                        int code = bits.Read(4);
                        first = ((code >> 2) ^ 2) - 2;
                        second = ((code & 3) ^ 2) - 2;
                    }
                    else
                    {
                        int entry = Atrac3Tables.Vlc[1][bits.Peek(8)];
                        int length = Atrac3Tables.VlcLength(entry);
                        if (length == 0)
                        {
                            return false;
                        }

                        bits.Skip(length);
                        first = Atrac3Tables.VlcValue(entry);
                        second = Atrac3Tables.VlcPairSecond(entry);
                    }

                    destination[2 * p] = first * factor;
                    destination[2 * p + 1] = second * factor;
                }

                return true;
            }

            if (clc)
            {
                int width = Atrac3Tables.ClcBits[selector];
                for (int i = 0; i < destination.Length; i++)
                {
                    destination[i] = bits.ReadSigned(width) * factor;
                }

                return true;
            }

            int[] table = Atrac3Tables.Vlc[selector];
            for (int i = 0; i < destination.Length; i++)
            {
                int entry = table[bits.Peek(8)];
                int length = Atrac3Tables.VlcLength(entry);
                if (length == 0)
                {
                    return false;
                }

                bits.Skip(length);
                destination[i] = Atrac3Tables.VlcValue(entry) * factor;
            }

            return true;
        }

        /// <summary>Sections 8-10: IMDCT, window, gain compensation, overlap-add and QMF synthesis.</summary>
        private void Synthesize(int bandsCoded, Span<float> output)
        {
            Atrac3ChannelState state = State;
            ReadOnlySpan<float> window = Atrac3Tables.Window;
            const int B = Atrac3Tables.BandSamples;
            for (int b = 0; b < 4; b++)
            {
                ReadOnlySpan<float> lines = _spectrum.AsSpan(b * B, B);
                Span<float> y = _transform;
                if (lines.ContainsAnyExcept(0f))
                {
                    Span<float> x = _coefficients;
                    if ((b & 1) == 1)
                    {
                        for (int k = 0; k < B; k++)
                        {
                            x[k] = lines[B - 1 - k];
                        }

                        _imdct.Transform(x, y);
                    }
                    else
                    {
                        _imdct.Transform(lines, y);
                    }

                    for (int n = 0; n < 2 * B; n++)
                    {
                        y[n] *= window[n];
                    }
                }
                else
                {
                    y.Clear();
                }

                // 9. Overlap-add with the scale of this frame's first gain point.
                float g = _curCount[b] > 0 ? Atrac3Tables.GainLevel[_curLevel[b * 8]] : 1f;
                Span<float> overlap = state.Overlap.AsSpan(b * B, B);
                Span<float> band = _bands.AsSpan(b * B, B);
                for (int n = 0; n < B; n++)
                {
                    band[n] = y[n] * g + overlap[n];
                }

                // The previous frame's gain envelope.
                int points = state.PointCount[b];
                if (points > 0)
                {
                    int pos = 0;
                    for (int i = 0; i < points; i++)
                    {
                        int level = state.PointLevel[b * 8 + i];
                        int location = 8 * state.PointLocation[b * 8 + i];
                        int next = i + 1 < points ? state.PointLevel[b * 8 + i + 1] : 4;
                        float f = Atrac3Tables.GainLevel[level];
                        for (; pos < location; pos++)
                        {
                            band[pos] *= f;
                        }

                        float step = Atrac3Tables.GainRamp[level - next + 15];
                        for (; pos < location + 8; pos++)
                        {
                            band[pos] *= f;
                            f *= step;
                        }
                    }
                }

                // Save the unscaled second half and this frame's points for the next frame.
                y.Slice(B, B).CopyTo(overlap);
                state.PointCount[b] = _curCount[b];
                _curLevel.AsSpan(b * 8, 8).CopyTo(state.PointLevel.AsSpan(b * 8, 8));
                _curLocation.AsSpan(b * 8, 8).CopyTo(state.PointLocation.AsSpan(b * 8, 8));
            }

            // 10. QMF synthesis: (B0, B1) -> A, (B3, B2) -> C, (A, C) -> output.
            Span<float> qmf = state.Qmf;
            const int D = Atrac3ChannelState.QmfDelay;
            QmfStage(_bands.AsSpan(0, B), _bands.AsSpan(B, B), qmf.Slice(0, D), _stageA);
            QmfStage(_bands.AsSpan(3 * B, B), _bands.AsSpan(2 * B, B), qmf.Slice(D, D), _stageC);
            QmfStage(_stageA, _stageC, qmf.Slice(2 * D, D), output.Slice(0, Atrac3Tables.FrameSamples));
        }

        /// <summary>One two-band QMF synthesis stage (spec section 10): <c>2N</c> samples from two <c>N</c>-sample halves.</summary>
        private void QmfStage(ReadOnlySpan<float> low, ReadOnlySpan<float> high, Span<float> delay, Span<float> output)
        {
            const int D = Atrac3ChannelState.QmfDelay;
            int n = low.Length;
            Span<float> u = _qmfWork.AsSpan(0, D + 2 * n);
            delay.CopyTo(u);
            for (int i = 0; i < n; i++)
            {
                u[D + 2 * i] = low[i] + high[i];
                u[D + 2 * i + 1] = low[i] - high[i];
            }

            ReadOnlySpan<float> h = Atrac3Tables.QmfFilter;
            for (int j = 0; j < n; j++)
            {
                ReadOnlySpan<float> window = u.Slice(2 * j, 48);
                float odd = 0f;
                float even = 0f;
                for (int m = 0; m < 48; m += 2)
                {
                    even += window[m] * h[m];
                    odd += window[m + 1] * h[m + 1];
                }

                output[2 * j] = odd;
                output[2 * j + 1] = even;
            }

            u.Slice(2 * n, D).CopyTo(delay);
        }
    }
}
