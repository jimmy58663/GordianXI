// src/Gordian.Core/Audio/FfxiSoundHeader.cs
using System;
using System.Buffers.Binary;

namespace Gordian.Core.Audio
{
    /// <summary>Which container a retail sound file uses.</summary>
    public enum FfxiSoundKind : byte
    {
        /// <summary><c>.bgw</c>, marker <c>BGMStream</c>: background music under <c>sound*/win/music/data</c>.</summary>
        Music,

        /// <summary><c>.spw</c>, marker <c>SeWave</c>: sound effects under <c>sound*/win/se/seNNN</c>.</summary>
        Effect,
    }

    /// <summary>The sample coding of a retail sound file.</summary>
    public enum FfxiSampleFormat
    {
        /// <summary>4-bit, 5-filter block ADPCM (<see cref="FfxiAdpcm"/>). The usual coding.</summary>
        Adpcm = 0,

        /// <summary>Raw interleaved 16-bit little-endian PCM.</summary>
        Pcm = 1,

        /// <summary>Sony ATRAC3. Not decoded by GordianXI yet (about a third of the music).</summary>
        Atrac3 = 3,
    }

    /// <summary>
    /// The 0x30-byte header of a retail <c>.bgw</c> (music) or <c>.spw</c> (sound effect) file.
    /// </summary>
    /// <remarks>
    /// <para>Layout referenced from xi-tools <c>docs/audio/format.md</c> (https://github.com/vekien/xi-tools, which credits
    /// the Windower pol-utils audio reader) and checked against the retail files of the install:</para>
    /// <list type="bullet">
    /// <item><c>.bgw</c>: 12-byte marker <c>"BGMStream\0\0\0"</c>, then <c>int32 format</c>, <c>int32 size</c>.</item>
    /// <item><c>.spw</c>: 8-byte marker <c>"SeWave\0"</c> plus a flag byte at 7, then <c>int32 size</c>, <c>int32 format</c>.</item>
    /// <item>Both then: <c>int32 id</c>, <c>int32 blocks</c> (per channel), <c>int32 loopStart</c> (in blocks, &lt; 0 = not looped),
    /// two <c>int32</c> halves whose signed sum is the sample rate, <c>int32</c> (always 0x30, the data offset), then the bytes
    /// <c>unknown, unknown, channels, blockSize</c>.</item>
    /// <item>Sample data always starts at 0x30. Each block holds one frame per channel in channel order.</item>
    /// </list>
    /// <para>The <c>blockSize</c> byte is the decoded samples per block per channel, but a few sound effects declare 16 while
    /// their frames are smaller, so for ADPCM the frame geometry is derived from the data size instead (xi-tools format.md,
    /// "Gotcha 2"). Verified: <c>music023.bgw</c> (stereo, 65-byte frames, 128 samples), <c>se002060.spw</c> (mono, 9-byte frames,
    /// 16 samples).</para>
    /// <para>A <c>.spw</c> whose byte 7 is not zero (<c>se039211</c>-<c>se039225</c>) is an encrypted variant nobody decodes;
    /// <see cref="IsEncrypted"/> marks it.</para>
    /// </remarks>
    public readonly struct FfxiSoundHeader
    {
        /// <summary>Offset of the first sample byte in every file.</summary>
        public const int DataOffset = 0x30;

        private static ReadOnlySpan<byte> MusicMarker => "BGMStream"u8;
        private static ReadOnlySpan<byte> EffectMarker => "SeWave"u8;

        /// <summary>Music or sound effect.</summary>
        public FfxiSoundKind Kind { get; init; }

        /// <summary>The sample coding.</summary>
        public FfxiSampleFormat Format { get; init; }

        /// <summary>The file size the header declares; sample data runs from <see cref="DataOffset"/> to here.</summary>
        public int FileSize { get; init; }

        /// <summary>The sound's own id: the music number for <c>.bgw</c>, the sound effect id (the <c>0x3D</c> pointer value) for <c>.spw</c>.</summary>
        public int Id { get; init; }

        /// <summary>Number of blocks per channel.</summary>
        public int SampleBlocks { get; init; }

        /// <summary>The block playback loops back to after the last block, or a negative value when the sound does not loop.</summary>
        public int LoopStartBlock { get; init; }

        /// <summary>Sample rate in Hz (the sum of the two obfuscated halves).</summary>
        public int SampleRate { get; init; }

        /// <summary>Channel count (1 or 2).</summary>
        public int Channels { get; init; }

        /// <summary>The <c>blockSize</c> byte as declared (samples per block per channel; unreliable for a few ADPCM files).</summary>
        public int DeclaredBlockSamples { get; init; }

        /// <summary>Bytes of one channel's frame in a block (ADPCM: 1 header byte + packed nibbles; PCM: 2 bytes per sample).</summary>
        public int FrameSize { get; init; }

        /// <summary>Decoded samples per block per channel.</summary>
        public int SamplesPerBlock { get; init; }

        /// <summary>True for the encrypted <c>.spw</c> variant (byte 7 not zero), which cannot be decoded.</summary>
        public bool IsEncrypted { get; init; }

        /// <summary>Whether playback loops back to <see cref="LoopStartBlock"/> after the end.</summary>
        public bool IsLooped => LoopStartBlock >= 0 && LoopStartBlock < SampleBlocks;

        /// <summary>Sample frames (per channel) in one pass of the file.</summary>
        public long TotalFrames => (long)SampleBlocks * SamplesPerBlock;

        /// <summary>The sample frame the loop restarts at, or -1 when not looped.</summary>
        public long LoopStartFrame => IsLooped ? (long)LoopStartBlock * SamplesPerBlock : -1;

        /// <summary>Whether GordianXI can decode this file (ADPCM or PCM, not encrypted, sane geometry).</summary>
        public bool IsDecodable =>
            !IsEncrypted && (Format == FfxiSampleFormat.Adpcm || Format == FfxiSampleFormat.Pcm)
            && Channels is 1 or 2 && SampleRate > 0 && FrameSize > 0 && SamplesPerBlock > 0 && SampleBlocks > 0;

        /// <summary>Duration of one pass in seconds.</summary>
        public double DurationSeconds => SampleRate > 0 ? TotalFrames / (double)SampleRate : 0;

        /// <summary>Parses the header at the start of <paramref name="file"/>.</summary>
        /// <returns>False when the marker is neither <c>BGMStream</c> nor <c>SeWave</c> or the file is shorter than the header.</returns>
        public static bool TryParse(ReadOnlySpan<byte> file, out FfxiSoundHeader header)
        {
            header = default;
            if (file.Length < DataOffset)
            {
                return false;
            }

            FfxiSoundKind kind;
            int body;
            int format;
            int size;
            bool encrypted = false;
            if (file.StartsWith(MusicMarker))
            {
                kind = FfxiSoundKind.Music;
                format = BinaryPrimitives.ReadInt32LittleEndian(file.Slice(0x0C));
                size = BinaryPrimitives.ReadInt32LittleEndian(file.Slice(0x10));
                body = 0x14;
            }
            else if (file.StartsWith(EffectMarker))
            {
                kind = FfxiSoundKind.Effect;
                encrypted = file[7] != 0;
                size = BinaryPrimitives.ReadInt32LittleEndian(file.Slice(0x08));
                format = BinaryPrimitives.ReadInt32LittleEndian(file.Slice(0x0C));
                body = 0x10;
            }
            else
            {
                return false;
            }

            int id = BinaryPrimitives.ReadInt32LittleEndian(file.Slice(body));
            int blocks = BinaryPrimitives.ReadInt32LittleEndian(file.Slice(body + 0x04));
            int loop = BinaryPrimitives.ReadInt32LittleEndian(file.Slice(body + 0x08));
            int rateHigh = BinaryPrimitives.ReadInt32LittleEndian(file.Slice(body + 0x0C));
            int rateLow = BinaryPrimitives.ReadInt32LittleEndian(file.Slice(body + 0x10));
            int channels = file[body + 0x1A];
            int declaredBlock = file[body + 0x1B];
            int rate = unchecked(rateHigh + rateLow);

            // Geometry comes from the declared size, so parsing only the first 0x30 bytes gives the same answer; the
            // stream separately never reads past the end of a truncated file.
            int bodyBytes = Math.Max(0, size - DataOffset);

            int frameSize;
            int samplesPerBlock;
            if (format == (int)FfxiSampleFormat.Adpcm)
            {
                // Derive the geometry from the data: a handful of effects declare 16 samples per block with smaller frames.
                long perChannelBlocks = (long)blocks * Math.Max(1, channels);
                if (perChannelBlocks > 0 && bodyBytes % perChannelBlocks == 0 && bodyBytes / perChannelBlocks >= 2)
                {
                    frameSize = (int)(bodyBytes / perChannelBlocks);
                }
                else
                {
                    frameSize = 1 + declaredBlock / 2;
                }

                samplesPerBlock = (frameSize - 1) * 2;
            }
            else
            {
                samplesPerBlock = declaredBlock;
                frameSize = declaredBlock * 2;
                if (format == (int)FfxiSampleFormat.Pcm && channels > 0)
                {
                    // PCM has no block structure worth trusting: treat the whole body as one run of frames.
                    long frames = bodyBytes / (2L * channels);
                    blocks = (int)Math.Min(int.MaxValue, frames);
                    samplesPerBlock = 1;
                    frameSize = 2;
                    loop = loop >= 0 ? (int)Math.Min(frames, (long)loop * Math.Max(1, declaredBlock)) : loop;
                }
            }

            header = new FfxiSoundHeader
            {
                Kind = kind,
                Format = (FfxiSampleFormat)format,
                FileSize = size,
                Id = id,
                SampleBlocks = blocks,
                LoopStartBlock = loop,
                SampleRate = rate,
                Channels = channels,
                DeclaredBlockSamples = declaredBlock,
                FrameSize = frameSize,
                SamplesPerBlock = samplesPerBlock,
                IsEncrypted = encrypted,
            };
            return true;
        }
    }
}
