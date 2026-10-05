// tests/Gordian.App.Tests/Audio/AudioMixerTests.cs
using System;
using System.Linq;
using System.Numerics;
using Gordian.App.Audio;
using Gordian.Core.Audio;
using Xunit;

namespace Gordian.App.Tests.Audio
{
    /// <summary>The software mixer behind the SDL audio output (#37).</summary>
    public class AudioMixerTests
    {
        private static PcmClip Constant(short value, int frames, int rate = 48000, int channels = 1, long loop = -1) =>
            new(Enumerable.Repeat(value, frames * channels).ToArray(), channels, rate, loop);

        private static short[] Mix(AudioMixer mixer, int frames)
        {
            var output = new short[frames * 2];
            mixer.Mix(output);
            return output;
        }

        [Fact]
        public void CentredMonoVoice_PlaysAtUnityOnBothSides()
        {
            var mixer = new AudioMixer();
            mixer.Play(Constant(10000, 4800).Open(), AudioCategory.Effects);
            short[] output = Mix(mixer, 256);
            Assert.InRange(output[100], 9990, 10010);
            Assert.InRange(output[101], 9990, 10010);
        }

        [Fact]
        public void CategoryAndMasterGains_Multiply()
        {
            var mixer = new AudioMixer();
            mixer.SetCategoryVolume(AudioCategory.Music, 0.5f);
            mixer.SetMasterVolume(0.5f);
            mixer.Play(Constant(10000, 4800).Open(), AudioCategory.Music);
            short[] output = Mix(mixer, 256);
            Assert.InRange(output[100], 2490, 2510);
        }

        [Fact]
        public void FinishedVoice_IsRemoved()
        {
            var mixer = new AudioMixer();
            int handle = mixer.Play(Constant(1000, 100).Open(), AudioCategory.System);
            Assert.True(mixer.IsPlaying(handle));
            Mix(mixer, 512);
            Assert.False(mixer.IsPlaying(handle));
            Assert.Equal(0, mixer.ActiveVoices);
        }

        [Fact]
        public void LoopedClip_KeepsPlaying()
        {
            var mixer = new AudioMixer();
            int handle = mixer.Play(Constant(1000, 100, loop: 0).Open(), AudioCategory.Zone);
            Mix(mixer, 4096);
            Assert.True(mixer.IsPlaying(handle));
            mixer.Stop(handle);
            Mix(mixer, 16);
            Assert.False(mixer.IsPlaying(handle));
        }

        [Fact]
        public void StopWithFade_RampsDown()
        {
            var mixer = new AudioMixer(1000);
            int handle = mixer.Play(Constant(10000, 100000, rate: 1000, loop: 0).Open(), AudioCategory.Music);
            Mix(mixer, 10);
            mixer.Stop(handle, 1f);
            short[] output = Mix(mixer, 500);
            Assert.InRange(output[2 * 250], 7000, 8000); // a quarter of the way down
            Mix(mixer, 600);
            Assert.False(mixer.IsPlaying(handle));
        }

        [Fact]
        public void Resampling_HalfRateSourceLastsTwiceAsLong()
        {
            var mixer = new AudioMixer(48000);
            int handle = mixer.Play(Constant(1000, 1000, rate: 24000).Open(), AudioCategory.Effects);
            Mix(mixer, 1900);
            Assert.True(mixer.IsPlaying(handle));
            Mix(mixer, 200);
            Assert.False(mixer.IsPlaying(handle));
        }

        [Fact]
        public void PositionalVoice_FadesWithDistanceAndPans()
        {
            var mixer = new AudioMixer();
            mixer.SetListener(Vector3.Zero, Vector3.UnitX);
            int far = mixer.Play(Constant(10000, 48000, loop: 0).Open(), AudioCategory.Zone,
                emitter: new AudioEmitter(new Vector3(100, 0, 0), 5, 50));
            short[] output = Mix(mixer, 64);
            Assert.Equal(0, output[40]);
            mixer.SetEmitter(far, new AudioEmitter(new Vector3(10, 0, 0), 5, 50));
            output = Mix(mixer, 64);
            Assert.True(output[41] > output[40], "a source on the listener's right is louder on the right");
            Assert.True(output[41] > 0);
        }

        [Fact]
        public void Attenuation_IsFullInsideNearAndSilentPastFar()
        {
            Assert.Equal(1f, AudioMixer.Attenuation(3, 5, 20));
            Assert.Equal(0.5f, AudioMixer.Attenuation(12.5f, 5, 20), 3);
            Assert.Equal(0f, AudioMixer.Attenuation(25, 5, 20));
        }

        [Fact]
        public void Mix_ClipsInsteadOfWrapping()
        {
            var mixer = new AudioMixer();
            for (int i = 0; i < 5; i++)
            {
                mixer.Play(Constant(30000, 4800).Open(), AudioCategory.Effects);
            }

            short[] output = Mix(mixer, 64);
            Assert.Equal(short.MaxValue, output[40]);
        }

        [Fact]
        public void FadeCategory_EasesTheBus()
        {
            var mixer = new AudioMixer(1000);
            mixer.Play(Constant(10000, 100000, rate: 1000, loop: 0).Open(), AudioCategory.Music);
            mixer.FadeCategory(AudioCategory.Music, 0f, 1f);
            short[] early = Mix(mixer, 100);
            for (int i = 0; i < 10; i++)
            {
                Mix(mixer, 100);
            }

            short[] late = Mix(mixer, 100);
            Assert.True(early[0] > 8000);
            Assert.Equal(0, late[50]);
        }
    }
}
