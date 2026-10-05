// src/Gordian.App/Audio/IAudioOutput.cs
using System;

namespace Gordian.App.Audio
{
    /// <summary>
    /// An audio device the mixing thread pushes 16-bit interleaved stereo PCM into. The device keeps a small queue; the
    /// mixing thread tops it up whenever <see cref="QueuedFrames"/> drops below its target latency.
    /// </summary>
    public interface IAudioOutput : IDisposable
    {
        /// <summary>The device's sample rate in Hz (valid after <see cref="Open"/>).</summary>
        int SampleRate { get; }

        /// <summary>Opens the default output device for stereo at about <paramref name="requestedRate"/> Hz.</summary>
        /// <returns>False when no device is available; the engine then runs silent.</returns>
        bool Open(int requestedRate);

        /// <summary>Frames queued on the device and not yet played.</summary>
        int QueuedFrames { get; }

        /// <summary>Appends interleaved stereo frames to the device queue.</summary>
        void Queue(ReadOnlySpan<short> interleaved);
    }

    /// <summary>An output that discards everything, used when no device opens (and in tests).</summary>
    public sealed class NullAudioOutput : IAudioOutput
    {
        /// <inheritdoc />
        public int SampleRate { get; private set; } = 48000;

        /// <inheritdoc />
        public int QueuedFrames => 0;

        /// <inheritdoc />
        public bool Open(int requestedRate)
        {
            SampleRate = requestedRate;
            return false;
        }

        /// <inheritdoc />
        public void Queue(ReadOnlySpan<short> interleaved)
        {
        }

        /// <inheritdoc />
        public void Dispose()
        {
        }
    }
}
