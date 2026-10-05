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
    /// <para>The device is OpenAL Soft (<see cref="OpenAlAudioOutput"/>, #37); when it cannot open (no native library,
    /// no device: CI, headless Linux) the engine falls back to <see cref="NullAudioOutput"/>.</para>
    /// </summary>
    public sealed class AudioEngine : IDisposable
    {
        /// <summary>Frames mixed per pass.</summary>
        public const int ChunkFrames = 512;

        /// <summary>Frames the thread keeps queued on the device (about 43 ms at 48 kHz).</summary>
        public const int TargetLatencyFrames = 2048;

        private IAudioOutput _output;
        private readonly Thread? _thread;
        private volatile bool _running;
        private long _mixedFrames;

        /// <summary>Frames mixed and handed to the device so far (diagnostics).</summary>
        public long MixedFrames => Interlocked.Read(ref _mixedFrames);

        /// <summary>Opens <paramref name="output"/> (OpenAL Soft by default, the silent <see cref="NullAudioOutput"/> when it fails) and starts mixing.</summary>
        public AudioEngine(IAudioOutput? output = null, int requestedRate = 48000)
        {
            _output = output ?? new OpenAlAudioOutput();
            IsAvailable = _output.Open(requestedRate);
            if (!IsAvailable && _output is not NullAudioOutput)
            {
                _output.Dispose();
                _output = new NullAudioOutput();
                _output.Open(requestedRate);
            }
            Mixer = new AudioMixer(IsAvailable ? _output.SampleRate : requestedRate);
            if (!IsAvailable)
            {
                GordianLog.Info("AUDIO", "No audio output device; falling back to silent output.");
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
                        Interlocked.Add(ref _mixedFrames, ChunkFrames);
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
