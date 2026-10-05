// tests/Gordian.App.Tests/Audio/ZoneEmitterAudioTests.cs
using System;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Gordian.App.Audio;
using Gordian.Core.Audio;
using Gordian.Core.Resources.Graphics;
using Xunit;

namespace Gordian.App.Tests.Audio
{
    /// <summary>Zone sound generators start, follow and stop with the listener (#39).</summary>
    public class ZoneEmitterAudioTests
    {
        private static (ZoneEmitterAudio Player, AudioMixer Mixer) Make(params ZoneSoundEmitter[] emitters)
        {
            var mixer = new AudioMixer(1000);
            var clip = new PcmClip(Enumerable.Repeat((short)1000, 1000).ToArray(), 1, 1000, 0);
            var player = new ZoneEmitterAudio(mixer, _ => Task.FromResult<PcmClip?>(clip));
            player.SetEmitters(emitters);
            return (player, mixer);
        }

        private static ZoneSoundEmitter Fountain(KeyFrameCurve? curve = null) =>
            new("se01", 2003, new Vector3(100, 0, 0), 0, 30, Array.Empty<Vector3>(), curve, true, 1);

        [Fact]
        public void StartsInRange_StopsOutOfRange()
        {
            var (player, mixer) = Make(Fountain());
            player.Update(new Vector3(0, 0, 0), 0.5f);
            mixer.Mix(new short[64]);
            Assert.Equal(0, mixer.ActiveVoices);

            player.Update(new Vector3(80, 0, 0), 0.5f);
            mixer.Mix(new short[64]);
            Assert.Equal(1, mixer.ActiveVoices);
            Assert.Equal(1, player.PlayingCount);

            player.Update(new Vector3(60, 0, 0), 0.5f); // 40 away: past the range and its 5-yalm stop margin
            mixer.Mix(new short[2 * 1000]);
            Assert.Equal(0, mixer.ActiveVoices);
        }

        [Fact]
        public void TimeOfDayVolume_SilencesTheSourceAtNight()
        {
            var curve = new KeyFrameCurve("tmvo", new[]
            {
                new KeyFrameEntry(0f, 0f), new KeyFrameEntry(0.25f, 0f), new KeyFrameEntry(0.275f, 1f),
                new KeyFrameEntry(0.775f, 1f), new KeyFrameEntry(0.8f, 0f), new KeyFrameEntry(1f, 0f),
            });
            var (player, mixer) = Make(Fountain(curve));
            player.Update(new Vector3(90, 0, 0), 0.1f);
            mixer.Mix(new short[64]);
            Assert.Equal(0, mixer.ActiveVoices);
            player.Update(new Vector3(90, 0, 0), 0.5f);
            mixer.Mix(new short[64]);
            Assert.Equal(1, mixer.ActiveVoices);
        }
    }
}
