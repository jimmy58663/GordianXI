// src/Gordian.Core/Audio/IPcmSource.cs
using System;

namespace Gordian.Core.Audio
{
    /// <summary>
    /// A backend-neutral stream of interleaved 16-bit PCM, read by the audio mixer on its own thread. Implementations
    /// handle their own looping: <see cref="Read"/> returns 0 only when a non-looping source has ended.
    /// </summary>
    public interface IPcmSource
    {
        /// <summary>Channel count (1 or 2).</summary>
        int Channels { get; }

        /// <summary>Sample rate in Hz.</summary>
        int SampleRate { get; }

        /// <summary>Whether the source loops forever.</summary>
        bool IsLooped { get; }

        /// <summary>Fills <paramref name="buffer"/> with whole interleaved frames.</summary>
        /// <returns>The number of samples (not frames) written; 0 when a non-looping source has ended.</returns>
        int Read(Span<short> buffer);
    }

    /// <summary>A fully decoded sound held in memory, shared by every voice that plays it.</summary>
    public sealed class PcmClip
    {
        /// <summary>Wraps decoded samples.</summary>
        public PcmClip(short[] samples, int channels, int sampleRate, long loopStartFrame, int id = 0)
        {
            Samples = samples;
            Channels = channels;
            SampleRate = sampleRate;
            LoopStartFrame = loopStartFrame;
            Id = id;
        }

        /// <summary>The sound id the clip was decoded from (0 when synthetic).</summary>
        public int Id { get; }

        /// <summary>Interleaved samples of one pass.</summary>
        public short[] Samples { get; }

        /// <summary>Channel count.</summary>
        public int Channels { get; }

        /// <summary>Sample rate in Hz.</summary>
        public int SampleRate { get; }

        /// <summary>The frame a loop restarts at, or -1 when the clip plays once.</summary>
        public long LoopStartFrame { get; }

        /// <summary>Frames per channel.</summary>
        public long Frames => Channels > 0 ? Samples.Length / Channels : 0;

        /// <summary>Whether the clip loops.</summary>
        public bool IsLooped => LoopStartFrame >= 0 && LoopStartFrame < Frames;

        /// <summary>Duration of one pass in seconds.</summary>
        public double DurationSeconds => SampleRate > 0 ? Frames / (double)SampleRate : 0;

        /// <summary>A new reader positioned at the start. <paramref name="loop"/> null keeps the clip's own looping.</summary>
        public IPcmSource Open(bool? loop = null) => new Reader(this, loop ?? IsLooped);

        private sealed class Reader : IPcmSource
        {
            private readonly PcmClip _clip;
            private readonly bool _loop;
            private long _position;

            public Reader(PcmClip clip, bool loop)
            {
                _clip = clip;
                _loop = loop && clip.Frames > 0;
            }

            public int Channels => _clip.Channels;
            public int SampleRate => _clip.SampleRate;
            public bool IsLooped => _loop;

            public int Read(Span<short> buffer)
            {
                short[] samples = _clip.Samples;
                int written = 0;
                int whole = buffer.Length - buffer.Length % Math.Max(1, _clip.Channels);
                while (written < whole)
                {
                    if (_position >= samples.Length)
                    {
                        if (!_loop)
                        {
                            break;
                        }

                        _position = Math.Max(0, _clip.LoopStartFrame) * _clip.Channels;
                    }

                    int count = (int)Math.Min(whole - written, samples.Length - _position);
                    samples.AsSpan((int)_position, count).CopyTo(buffer.Slice(written));
                    written += count;
                    _position += count;
                }

                return written;
            }
        }
    }
}
