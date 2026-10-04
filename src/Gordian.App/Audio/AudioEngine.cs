// src/Gordian.App/Audio/AudioEngine.cs
using System;
using System.Threading;
using Gordian.Core.Diagnostics;

namespace Gordian.App.Audio
{
    /// <summary>
    /// Owns the audio device and the mixing thread. The thread keeps about <see cref="TargetLatencyFrames"/> frames queued
    /// on the device, mixing <see cref="ChunkFrames"/> at a time, so a sound started from the game thread is heard within
    /// roughly 50 ms. When no device opens the engine still accepts every call and simply stays silent.
    /// <para>No device backend is chosen yet (#37, the candidates are compared in <c>docs/design/audio.md</c>): until one
    /// is, the engine runs on <see cref="NullAudioOutput"/> and everything above it (decode, mixing, track and cue choice)
    /// works and is tested, silently.</para>
    /// </summary>
    public sealed class AudioEngine : IDisposable
    {
        /// <summary>Frames mixed per pass.</summary>
        public const int ChunkFrames = 512;

        /// <summary>Frames the thread keeps queued on the device (about 43 ms at 48 kHz).</summary>
        public const int TargetLatencyFrames = 2048;

        private readonly IAudioOutput _output;
        private readonly Thread? _thread;
        private volatile bool _running;

        /// <summary>Opens <paramref name="output"/> (the silent <see cref="NullAudioOutput"/> until a backend is chosen) and starts mixing.</summary>
        public AudioEngine(IAudioOutput? output = null, int requestedRate = 48000)
        {
            _output = output ?? new NullAudioOutput();
            IsAvailable = _output.Open(requestedRate);
            Mixer = new AudioMixer(IsAvailable ? _output.SampleRate : requestedRate);
            if (!IsAvailable)
            {
                GordianLog.Info("AUDIO", "No audio output device (backend not chosen yet, #37); sound is silent this session.");
                return;
            }

            _running = true;
            _thread = new Thread(MixLoop) { IsBackground = true, Name = "GordianXI Audio Mixer", Priority = ThreadPriority.AboveNormal };
            _thread.Start();
        }

        /// <summary>Whether a device opened and sound is heard.</summary>
        public bool IsAvailable { get; }

        /// <summary>The mixer every sound plays through.</summary>
        public AudioMixer Mixer { get; }

        private void MixLoop()
        {
            var chunk = new short[ChunkFrames * 2];
            while (_running)
            {
                try
                {
                    if (_output.QueuedFrames < TargetLatencyFrames)
                    {
                        Mixer.Mix(chunk);
                        _output.Queue(chunk);
                        continue;
                    }
                }
                catch (Exception ex)
                {
                    GordianLog.Warn("AUDIO", $"Mixing failed: {ex.Message}");
                }

                Thread.Sleep(3);
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _running = false;
            _thread?.Join(500);
            _output.Dispose();
        }
    }
}
