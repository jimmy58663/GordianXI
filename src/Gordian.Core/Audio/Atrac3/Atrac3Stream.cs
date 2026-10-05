// src/Gordian.Core/Audio/Atrac3/Atrac3Stream.cs
using System;
using Gordian.Core.Diagnostics;

namespace Gordian.Core.Audio.Atrac3
{
    /// <summary>
    /// Streams the PCM of one ATRAC3 <c>.bgw</c> / <c>.spw</c> file frame by frame: de-obfuscates each block, decodes each
    /// channel's 192-byte sound unit with its own <see cref="Atrac3Channel"/>, interleaves the channels, and loops back to
    /// the loop sample with an exact state snapshot.
    /// </summary>
    /// <remarks>
    /// <para>Implemented from the clean-room specification <c>docs/audio/atrac3.md</c>. The FFXI wrapping (section 2) is
    /// from vgmstream's description (https://github.com/vgmstream/vgmstream, <c>src/meta/bgw.c</c>,
    /// <c>bgw_streamfile.h</c>, which credits Moogle Toolbox): the XOR key is the first block with <c>A0 02 4E 9F</c> mixed
    /// into each channel's first four bytes, and a channel frame whose de-obfuscated unit id is wrong but whose raw id is
    /// right is decoded raw (the unobfuscated last blocks of <c>music069</c>, <c>music071</c>, <c>music900</c>).</para>
    /// <para>The timeline is untrimmed: sample 0 is the first sample of frame 0 (a silent key frame), so the audible start
    /// comes about 50 ms late. vgmstream trims 2186 samples; whether retail trims is unconfirmed (spec section 18), so
    /// nothing is trimmed here.</para>
    /// <para>Loops (section 12): just before frame <c>F = S / 1024</c> is first decoded the channel states are copied; at
    /// the end they are restored, frame <c>F</c> is decoded again and its first <c>S mod 1024</c> samples are skipped, so
    /// the loop replays the first pass sample for sample. Joint stereo is not supported (FFXI never uses it; section 14):
    /// such a block's second unit fails the unit id check and is silenced with a warning.</para>
    /// </remarks>
    public sealed class Atrac3Stream : IPcmSource
    {
        /// <summary>Bytes of one channel's sound unit in every FFXI ATRAC3 file.</summary>
        public const int FrameBytes = 192;

        /// <summary>Samples per channel per frame.</summary>
        public const int FrameSamples = Atrac3Tables.FrameSamples;

        private static ReadOnlySpan<byte> KeyMask => [0xA0, 0x02, 0x4E, 0x9F];

        private readonly byte[] _file;
        private readonly int _id;
        private readonly bool _loop;
        private readonly byte[] _key;
        private readonly byte[] _unit = new byte[FrameBytes];
        private readonly Atrac3Channel[] _decoders;
        private readonly Atrac3ChannelState[] _snapshot;
        private readonly float[] _channelOut = new float[FrameSamples];
        private readonly float[] _frame;
        private bool _snapshotTaken;
        private int _nextFrame;
        private int _framePosition;
        private int _frameLength;
        private bool _warned;

        private Atrac3Stream(byte[] file, in FfxiSoundHeader header, bool loop)
        {
            _file = file;
            _id = header.Id;
            Channels = header.Channels;
            SampleRate = header.SampleRate;
            FrameCount = header.SampleBlocks;
            LoopStartSample = header.IsLooped ? header.LoopStartFrame : -1;
            _loop = loop;

            int block = FrameBytes * Channels;
            _key = new byte[block];
            file.AsSpan(FfxiSoundHeader.DataOffset, block).CopyTo(_key);
            for (int c = 0; c < Channels; c++)
            {
                for (int i = 0; i < 4; i++)
                {
                    _key[c * FrameBytes + i] ^= KeyMask[i];
                }
            }

            _decoders = new Atrac3Channel[Channels];
            _snapshot = new Atrac3ChannelState[Channels];
            for (int c = 0; c < Channels; c++)
            {
                _decoders[c] = new Atrac3Channel();
                _snapshot[c] = new Atrac3ChannelState();
            }

            _frame = new float[FrameSamples * Channels];
        }

        /// <inheritdoc />
        public int Channels { get; }

        /// <inheritdoc />
        public int SampleRate { get; }

        /// <inheritdoc />
        public bool IsLooped => _loop;

        /// <summary>Frames (of 1024 samples per channel) in the file.</summary>
        public int FrameCount { get; }

        /// <summary>Samples per channel in one pass (frames x 1024, the header's total).</summary>
        public long TotalSamples => (long)FrameCount * FrameSamples;

        /// <summary>The sample the loop restarts at (<see cref="FfxiSoundHeader.LoopStartFrame"/>), or -1 when the file is one-shot.</summary>
        public long LoopStartSample { get; }

        /// <summary>Channel frames that failed to parse so far (decoded as silence).</summary>
        public int MalformedFrames { get; private set; }

        /// <summary>Channel frames that were decoded without the XOR (the unobfuscated tail blocks).</summary>
        public int RawFrames { get; private set; }

        /// <summary>Opens a stream over an ATRAC3 file. Null when the file is not FFXI ATRAC3 this decoder can read.</summary>
        /// <param name="file">The whole file.</param>
        /// <param name="header">Its parsed header.</param>
        /// <param name="loop">Whether to loop (to the loop sample, or to the start of a one-shot file).</param>
        public static Atrac3Stream? Open(byte[] file, in FfxiSoundHeader header, bool loop)
        {
            if (header.Format != FfxiSampleFormat.Atrac3 || header.IsEncrypted || header.Channels is not (1 or 2)
                || header.SampleRate <= 0 || header.SampleBlocks <= 0
                || file.Length < FfxiSoundHeader.DataOffset + FrameBytes * header.Channels)
            {
                return null;
            }

            return new Atrac3Stream(file, header, loop);
        }

        /// <inheritdoc />
        public int Read(Span<short> buffer)
        {
            int whole = buffer.Length - buffer.Length % Channels;
            int written = 0;
            while (written < whole)
            {
                if (_framePosition >= _frameLength && !NextFrame())
                {
                    break;
                }

                int count = Math.Min(whole - written, _frameLength - _framePosition);
                ReadOnlySpan<float> source = _frame.AsSpan(_framePosition, count);
                Span<short> target = buffer.Slice(written, count);
                for (int i = 0; i < count; i++)
                {
                    target[i] = ToInt16(source[i]);
                }

                _framePosition += count;
                written += count;
            }

            return written;
        }

        /// <summary>
        /// Like <see cref="Read"/> but in float, in 16-bit units (divide by 32768 for full scale); unclamped. For tests
        /// against reference output.
        /// </summary>
        internal int ReadFloat(Span<float> buffer)
        {
            int whole = buffer.Length - buffer.Length % Channels;
            int written = 0;
            while (written < whole)
            {
                if (_framePosition >= _frameLength && !NextFrame())
                {
                    break;
                }

                int count = Math.Min(whole - written, _frameLength - _framePosition);
                _frame.AsSpan(_framePosition, count).CopyTo(buffer.Slice(written));
                _framePosition += count;
                written += count;
            }

            return written;
        }

        /// <summary>Rounds half to even and clamps to 16 bits (spec section 11).</summary>
        internal static short ToInt16(float value)
        {
            float r = MathF.Round(value);
            return r >= short.MaxValue ? short.MaxValue : r <= short.MinValue ? short.MinValue : (short)r;
        }

        private bool NextFrame()
        {
            if (_nextFrame >= FrameCount || !FrameInFile(_nextFrame))
            {
                if (!_loop)
                {
                    return false;
                }

                long restart = Math.Max(0, LoopStartSample);
                int loopFrame = (int)(restart / FrameSamples);
                if (_snapshotTaken)
                {
                    for (int c = 0; c < Channels; c++)
                    {
                        _snapshot[c].CopyTo(_decoders[c].State);
                    }
                }
                else
                {
                    // No snapshot (a frame past the end of a truncated file): pre-roll two frames from a fresh state.
                    for (int c = 0; c < Channels; c++)
                    {
                        _decoders[c].State.Reset();
                    }

                    for (int f = Math.Max(0, loopFrame - 2); f < loopFrame; f++)
                    {
                        DecodeFrame(f);
                    }
                }

                if (!FrameInFile(loopFrame))
                {
                    return false;
                }

                DecodeFrame(loopFrame);
                _nextFrame = loopFrame + 1;
                _framePosition = (int)(restart % FrameSamples) * Channels;
                return true;
            }

            if (_loop && !_snapshotTaken && _nextFrame == Math.Max(0, LoopStartSample) / FrameSamples)
            {
                for (int c = 0; c < Channels; c++)
                {
                    _decoders[c].State.CopyTo(_snapshot[c]);
                }

                _snapshotTaken = true;
            }

            DecodeFrame(_nextFrame);
            _nextFrame++;
            _framePosition = 0;
            return true;
        }

        /// <summary>De-obfuscates and decodes block <paramref name="index"/> into <see cref="_frame"/>.</summary>
        private void DecodeFrame(int index)
        {
            int channels = Channels;
            int offset = FfxiSoundHeader.DataOffset + index * FrameBytes * channels;
            for (int c = 0; c < channels; c++)
            {
                ReadOnlySpan<byte> raw = _file.AsSpan(offset + c * FrameBytes, FrameBytes);
                ReadOnlySpan<byte> key = _key.AsSpan(c * FrameBytes, FrameBytes);
                Span<byte> unit = _unit;
                for (int i = 0; i < FrameBytes; i++)
                {
                    unit[i] = (byte)(raw[i] ^ key[i]);
                }

                // Unobfuscated tail frames: prefer the XORed unit, fall back to the raw one when only it is valid.
                ReadOnlySpan<byte> source = unit;
                if (unit[0] >> 2 != 0x28 && raw[0] >> 2 == 0x28)
                {
                    source = raw;
                    RawFrames++;
                }

                if (!_decoders[c].Decode(source, _channelOut))
                {
                    MalformedFrames++;
                    if (!_warned)
                    {
                        _warned = true;
                        GordianLog.Warn("AUDIO", $"ATRAC3 sound {_id}: frame {index} channel {c} is not a valid single-channel sound unit "
                            + "(joint stereo and other ATRAC3 variants are not supported); decoded as silence.");
                    }
                }

                Span<float> frame = _frame;
                ReadOnlySpan<float> samples = _channelOut;
                for (int i = 0; i < FrameSamples; i++)
                {
                    frame[i * channels + c] = samples[i];
                }
            }

            _frameLength = FrameSamples * channels;
        }

        private bool FrameInFile(int index) =>
            FfxiSoundHeader.DataOffset + (long)(index + 1) * FrameBytes * Channels <= _file.Length;
    }

    /// <summary>
    /// The managed ATRAC3 decoder GordianXI registers as <see cref="FfxiSoundDecoder.Atrac3"/>: opens an
    /// <see cref="Atrac3Stream"/>. Clean-room implementation of <c>docs/audio/atrac3.md</c>.
    /// </summary>
    public sealed class Atrac3Decoder : IAtrac3Decoder
    {
        /// <inheritdoc />
        public IPcmSource? Open(byte[] file, in FfxiSoundHeader header, bool loop) => Atrac3Stream.Open(file, header, loop);
    }
}
