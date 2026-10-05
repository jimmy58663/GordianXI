// tests/Gordian.App.Tests/Audio/OpenAlAudioOutputTests.cs
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Gordian.App.Audio;
using Gordian.Core.Audio;
using Xunit;
using Xunit.Abstractions;

namespace Gordian.App.Tests.Audio
{
    /// <summary>
    /// The OpenAL Soft output (#37). Nothing here needs an audio device: without one (CI, headless Linux) the output
    /// reports that it did not open and the engine falls back to silence.
    /// </summary>
    public class OpenAlAudioOutputTests
    {
        private const string GameDirectory = @"G:\Program Files (x86)\PlayOnline\SquareEnix\FINAL FANTASY XI";
        private readonly ITestOutputHelper _output;

        public OpenAlAudioOutputTests(ITestOutputHelper output) => _output = output;

        [Fact]
        public void BundledNativeLibrary_IsBesideTheApp()
        {
            // win-x64 / linux-x64 / osx-x64 / osx-arm64 copies come from Silk.NET.OpenAL.Soft.Native.
            if (Environment.Is64BitProcess)
            {
                string? path = OpenAlAudioOutput.FindBundledLibrary();
                _output.WriteLine(path ?? "(none)");
                Assert.NotNull(path);
            }
        }

        [Fact]
        public void Open_SucceedsOrFallsBackWithoutThrowing()
        {
            using var output = new OpenAlAudioOutput();
            bool opened = output.Open(48000);
            _output.WriteLine(opened ? $"opened at {output.SampleRate} Hz" : "no audio device");
            if (opened)
            {
                output.Queue(new short[2 * 512]); // silence
                Assert.InRange(output.QueuedFrames, 0, 512);
            }

            using var engine = new AudioEngine(new FailingOutput());
            Assert.False(engine.IsAvailable);
            engine.Mixer.Mix(new short[64]); // still usable
        }

        /// <summary>
        /// Manual check: plays the first seconds of music023 through OpenAL. Opt-in (set GORDIAN_AUDIO_SMOKE=1), since
        /// it is audible; it also checks the device consumes the queue at about real time.
        /// </summary>
        [Fact]
        public void Smoke_PlaysAnAdpcmTrack()
        {
            if (Environment.GetEnvironmentVariable("GORDIAN_AUDIO_SMOKE") != "1" || !Directory.Exists(GameDirectory))
            {
                return;
            }

            var locator = new FfxiSoundLocator(GameDirectory);
            IPcmSource track = FfxiSoundDecoder.Open(File.ReadAllBytes(locator.FindMusic(23)!))!;
            using var engine = new AudioEngine();
            Assert.True(engine.IsAvailable, "no audio device opened");
            engine.Mixer.Play(track, AudioCategory.Music, 0.5f);
            var clock = Stopwatch.StartNew();
            Thread.Sleep(4000);
            _output.WriteLine($"played {clock.Elapsed.TotalSeconds:F1} s, {engine.Mixer.ActiveVoices} voice(s) active, {engine.MixedFrames} frames mixed");
            Assert.Equal(1, engine.Mixer.ActiveVoices);
            // The device pulled about 4 s of audio (the mixer only runs ahead by the queue target).
            Assert.InRange(engine.MixedFrames, 3.5 * engine.Mixer.OutputRate, 4.5 * engine.Mixer.OutputRate);
        }

        private sealed class FailingOutput : IAudioOutput
        {
            public int SampleRate => 48000;
            public int QueuedFrames => 0;
            public bool Open(int requestedRate) => false;
            public void Queue(ReadOnlySpan<short> interleaved) { }
            public void Dispose() { }
        }
    }
}
