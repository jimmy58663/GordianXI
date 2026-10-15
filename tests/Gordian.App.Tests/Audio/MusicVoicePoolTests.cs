// tests/Gordian.App.Tests/Audio/MusicVoicePoolTests.cs
using System.Collections.Generic;
using System.Linq;
using Gordian.App.Audio;
using Gordian.Core.Audio;
using Gordian.Core.World;
using Xunit;

namespace Gordian.App.Tests.Audio
{
    /// <summary>
    /// Music continuity across multi-box character switches (round 2 of #368 / #377): every character's director holds its
    /// track in one shared pool; the ones not heard play silently, characters on the same track share a voice.
    /// </summary>
    public class MusicVoicePoolTests
    {
        private static readonly MusicContext Day = new(0, false, 12f);

        private static ZoneMusicState Zone(ushort day)
        {
            var music = new ZoneMusicState();
            music.SetSlot(0, day);
            music.SetSlot(1, day);
            return music;
        }

        private static (MusicVoicePool Pool, AudioMixer Mixer, List<int> Opened) Make()
        {
            var mixer = new AudioMixer(1000);
            var opened = new List<int>();
            var pool = new MusicVoicePool(mixer, id =>
            {
                opened.Add(id);
                return new PcmClip(Enumerable.Repeat((short)1000, 100).ToArray(), 1, 1000, 0, id).Open();
            }, work => work());
            return (pool, mixer, opened);
        }

        [Fact]
        public void SameZone_SharesOneVoice_AndASwitchKeepsItPlaying()
        {
            var (pool, mixer, opened) = Make();
            var a = new MusicDirector(mixer, pool) { Heard = true };
            var b = new MusicDirector(mixer, pool) { Heard = false };
            ZoneMusicState zoneA = Zone(230), zoneB = Zone(230);
            a.Update(zoneA, Day, 0.016);
            b.Update(zoneB, Day, 0.016);
            int handle = pool.HandleOf(230);
            Assert.NotEqual(0, handle);
            Assert.Equal(new[] { 230 }, opened);

            // Switch: B is heard now. The same voice goes on, nothing restarts.
            a.Heard = false;
            b.Heard = true;
            a.Update(zoneA, Day, 0.016);
            b.Update(zoneB, Day, 0.016);
            mixer.Mix(new short[64]);
            Assert.Equal(new[] { 230 }, opened);
            Assert.Equal(handle, pool.HandleOf(230));
            Assert.True(mixer.IsPlaying(handle));
            Assert.Equal(230, pool.AudibleTrack);
        }

        [Fact]
        public void DifferentZones_EachTrackKeepsPlaying_SoASwitchResumesIt()
        {
            var (pool, mixer, opened) = Make();
            var a = new MusicDirector(mixer, pool) { Heard = true };
            var b = new MusicDirector(mixer, pool) { Heard = false };
            ZoneMusicState zoneA = Zone(230), zoneB = Zone(107);
            a.Update(zoneA, Day, 0.016);
            b.Update(zoneB, Day, 0.016);
            Assert.Equal(new[] { 230, 107 }, opened);
            Assert.Equal(230, pool.AudibleTrack);
            int hidden = pool.HandleOf(107);

            a.Heard = false;
            b.Heard = true;
            a.Update(zoneA, Day, 0.016);
            b.Update(zoneB, Day, 0.016);
            mixer.Mix(new short[64]);
            Assert.Equal(new[] { 230, 107 }, opened); // resumed, not reopened from the top
            Assert.Equal(hidden, pool.HandleOf(107));
            Assert.Equal(107, pool.AudibleTrack);
            Assert.True(mixer.IsPlaying(pool.HandleOf(230))); // A's track goes on silently
        }

        [Fact]
        public void TheLastCharacterOnATrack_StopsIt()
        {
            var (pool, mixer, _) = Make();
            var a = new MusicDirector(mixer, pool) { Heard = true };
            var b = new MusicDirector(mixer, pool) { Heard = false };
            ZoneMusicState zone = Zone(230);
            a.Update(zone, Day, 0.016);
            b.Update(zone, Day, 0.016);
            int handle = pool.HandleOf(230);

            b.StopAll(0f);
            Assert.Equal(new[] { 230 }, pool.Tracks);
            a.StopAll(0f);
            mixer.Mix(new short[64]);
            Assert.Empty(pool.Tracks);
            Assert.False(mixer.IsPlaying(handle));
        }

        [Fact]
        public void HiddenCharactersTrackChange_JoinsTheTrackTheHeardOneAlreadyPlays()
        {
            var (pool, mixer, opened) = Make();
            var a = new MusicDirector(mixer, pool) { Heard = true };
            var b = new MusicDirector(mixer, pool) { Heard = false };
            ZoneMusicState zoneA = Zone(230), zoneB = Zone(107);
            a.Update(zoneA, Day, 0.016);
            b.Update(zoneB, Day, 0.016);

            // B zones into A's zone: its old track fades out, then it joins A's playing voice.
            zoneB.SetSlot(0, 230);
            zoneB.SetSlot(1, 230);
            b.Update(zoneB, Day, 0.016);
            b.Update(zoneB, Day, MusicDirector.TrackFadeSeconds + 0.1);
            Assert.Equal(new[] { 230, 107 }, opened);
            Assert.Equal(new[] { 230 }, pool.Tracks);
        }
    }
}
