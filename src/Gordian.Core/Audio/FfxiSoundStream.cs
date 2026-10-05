// src/Gordian.Core/Audio/FfxiSoundStream.cs
using System;
using System.Buffers.Binary;

namespace Gordian.Core.Audio
{
    /// <summary>
    /// Streams the PCM of one retail <c>.bgw</c> / <c>.spw</c> file block by block (ADPCM or PCM), looping back to the
    /// header's loop block after the last one. Music is streamed this way rather than decoded whole: a 15 MB ADPCM track
    /// is about 50 MB of PCM.
    /// </summary>
    /// <remarks>
    /// Format referenced from xi-tools <c>docs/audio/format.md</c> (https://github.com/vekien/xi-tools); see
    /// <see cref="FfxiSoundHeader"/> and <see cref="FfxiAdpcm"/>. On a loop the ADPCM history is restored to what it was
    /// when the loop block was first decoded, so the restart sounds as it did the first time.
    /// </remarks>
    public sealed class FfxiSoundStream : IPcmSource
    {
        private readonly byte[] _file;
        private readonly FfxiSoundHeader _header;
        private readonly bool _loop;
        private readonly short[] _block;
        private readonly int[] _h0;
        private readonly int[] _h1;
        private readonly int[] _loopH0;
        private readonly int[] _loopH1;
        private bool _loopHistorySaved;
        private int _nextBlock;
        private int _blockSamples;
        private int _blockPosition;

        private FfxiSoundStream(byte[] file, FfxiSoundHeader header, bool loop)
        {
            _file = file;
            _header = header;
            _loop = loop;
            int channels = header.Channels;
            _block = new short[Math.Max(1, header.SamplesPerBlock) * channels * (header.Format == FfxiSampleFormat.Pcm ? 1024 : 1)];
            _h0 = new int[channels];
            _h1 = new int[channels];
            _loopH0 = new int[channels];
            _loopH1 = new int[channels];
        }

        /// <summary>The parsed header.</summary>
        public FfxiSoundHeader Header => _header;

        /// <inheritdoc />
        public int Channels => _header.Channels;

        /// <inheritdoc />
        public int SampleRate => _header.SampleRate;

        /// <inheritdoc />
        public bool IsLooped => _loop;

        /// <summary>Opens a stream over a whole file's bytes.</summary>
        /// <param name="file">The file contents.</param>
        /// <param name="loop">Null keeps the header's looping; true or false forces it.</param>
        /// <returns>Null when the file is not a decodable sound (unknown marker, ATRAC3, encrypted).</returns>
        public static FfxiSoundStream? Open(byte[] file, bool? loop = null)
        {
            if (!FfxiSoundHeader.TryParse(file, out FfxiSoundHeader header) || !header.IsDecodable)
            {
                return null;
            }

            return new FfxiSoundStream(file, header, loop ?? header.IsLooped);
        }

        /// <summary>Decodes a whole file (one pass) into a clip. Null when the file is not decodable.</summary>
        public static PcmClip? DecodeClip(byte[] file)
        {
            FfxiSoundStream? stream = Open(file, loop: false);
            if (stream is null)
            {
                return null;
            }

            FfxiSoundHeader h = stream._header;
            long total = h.TotalFrames * h.Channels;
            if (total > 64L * 1024 * 1024)
            {
                return null;
            }

            var samples = new short[total];
            int written = 0;
            while (written < samples.Length)
            {
                int n = stream.Read(samples.AsSpan(written));
                if (n == 0)
                {
                    break;
                }

                written += n;
            }

            if (written < samples.Length)
            {
                Array.Resize(ref samples, written);
            }

            return new PcmClip(samples, h.Channels, h.SampleRate, h.IsLooped ? h.LoopStartFrame : -1, h.Id);
        }

        /// <inheritdoc />
        public int Read(Span<short> buffer)
        {
            int channels = _header.Channels;
            int whole = buffer.Length - buffer.Length % channels;
            int written = 0;
            while (written < whole)
            {
                if (_blockPosition >= _blockSamples && !DecodeNextBlock())
                {
                    break;
                }

                int count = Math.Min(whole - written, _blockSamples - _blockPosition);
                _block.AsSpan(_blockPosition, count).CopyTo(buffer.Slice(written));
                _blockPosition += count;
                written += count;
            }

            return written;
        }

        private bool DecodeNextBlock()
        {
            if (_nextBlock >= _header.SampleBlocks || !BlockInFile(_nextBlock))
            {
                if (!_loop)
                {
                    return false;
                }

                int restart = _header.IsLooped ? _header.LoopStartBlock : 0;
                if (_header.Format == FfxiSampleFormat.Adpcm)
                {
                    if (_loopHistorySaved && restart == _header.LoopStartBlock)
                    {
                        _loopH0.CopyTo(_h0, 0);
                        _loopH1.CopyTo(_h1, 0);
                    }
                    else
                    {
                        Array.Clear(_h0);
                        Array.Clear(_h1);
                    }
                }

                _nextBlock = restart;
                if (!BlockInFile(_nextBlock))
                {
                    return false;
                }
            }

            if (_header.Format == FfxiSampleFormat.Pcm)
            {
                return DecodePcmRun();
            }

            if (_nextBlock == _header.LoopStartBlock && !_loopHistorySaved)
            {
                _h0.CopyTo(_loopH0, 0);
                _h1.CopyTo(_loopH1, 0);
                _loopHistorySaved = true;
            }

            int channels = _header.Channels;
            int frame = _header.FrameSize;
            int offset = FfxiSoundHeader.DataOffset + _nextBlock * frame * channels;
            int produced = 0;
            for (int c = 0; c < channels; c++)
            {
                ReadOnlySpan<byte> bytes = _file.AsSpan(offset + c * frame, frame);
                produced = FfxiAdpcm.DecodeFrame(bytes, _block.AsSpan(c), channels, ref _h0[c], ref _h1[c]);
            }

            _blockSamples = produced * channels;
            _blockPosition = 0;
            _nextBlock++;
            return true;
        }

        /// <summary>PCM: copies up to 1024 frames starting at frame <see cref="_nextBlock"/> (one "block" is one frame).</summary>
        private bool DecodePcmRun()
        {
            int channels = _header.Channels;
            int frames = Math.Min(_block.Length / channels, _header.SampleBlocks - _nextBlock);
            int offset = FfxiSoundHeader.DataOffset + _nextBlock * 2 * channels;
            int available = (_file.Length - offset) / (2 * channels);
            frames = Math.Min(frames, available);
            if (frames <= 0)
            {
                return false;
            }

            ReadOnlySpan<byte> bytes = _file.AsSpan(offset, frames * 2 * channels);
            for (int i = 0; i < frames * channels; i++)
            {
                _block[i] = BinaryPrimitives.ReadInt16LittleEndian(bytes.Slice(i * 2));
            }

            _blockSamples = frames * channels;
            _blockPosition = 0;
            _nextBlock += frames;
            return true;
        }

        private bool BlockInFile(int block)
        {
            long end = _header.Format == FfxiSampleFormat.Pcm
                ? FfxiSoundHeader.DataOffset + (long)(block + 1) * 2 * _header.Channels
                : FfxiSoundHeader.DataOffset + (long)(block + 1) * _header.FrameSize * _header.Channels;
            return end <= _file.Length;
        }
    }
}
