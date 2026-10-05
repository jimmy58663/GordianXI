// tests/Gordian.App.Tests/Audio/MusicDirectorTests.cs
using System.Collections.Generic;
using System.Linq;
using Gordian.App.Audio;
using Gordian.Core.Audio;
using Gordian.Core.World;
using Xunit;

namespace Gordian.App.Tests.Audio
{
    /// <summary>Music slot choice and track switching (#42, #114).</summary>
    public class MusicDirectorTests
    {
        private static ZoneMusicState Zone(params ushort[] slots)
        {
            var music = new ZoneMusicState();
            for (int i = 0; i < slots.Length; i++)
            {
                music.SetSlot(i, slots[i]);
            }

            return music;
        }

        private static (MusicDirector Director, List<int> Opened, AudioMixer Mixer) Make()
        {
            var mixer = new AudioMixer(1000);
            var opened = new List<int>();
            var director = new MusicDirector(mixer, id =>
            {
                opened.Add(id);
                return new PcmClip(Enumerable.Repeat((short)1000, 100).ToArray(), 1, 1000, 0, id).Open();
            }, work => work());
            return (director, opened, mixer);
        }

        [Theory]
        [InlineData(0, false, 12f, MusicSlot.ZoneDay)]
        [InlineData(0, false, 19f, MusicSlot.ZoneNight)]
        [InlineData(0, false, 5.5f, MusicSlot.ZoneNight)]
        [InlineData(1, false, 12f, MusicSlot.BattleSolo)]
        [InlineData(1, true, 12f, MusicSlot.BattleParty)]
        [InlineData(3, true, 12f, MusicSlot.Dead)]
        [InlineData(5, false, 12f, MusicSlot.Mount)]
        [InlineData(85, false, 12f, MusicSlot.Mount)]
        [InlineData(6, false, 12f, MusicSlot.Fishing)]
        [InlineData(50, false, 12f, MusicSlot.Fishing)]
        public void ChooseSlot_FollowsThePlayersSituation(byte status, bool party, float hour, MusicSlot expected) =>
            Assert.Equal(expected, MusicDirector.ChooseSlot(new MusicContext(status, party, hour)));

        [Theory]
        [InlineData(0, false, 0)]
        [InlineData(1, false, 1)]  // S2C 0x037 says engaged
        [InlineData(0, true, 1)]   // engaged locally before the server's status arrives
        [InlineData(3, true, 3)]   // dead wins
        public void MusicStatus_ComesFromTheLocalPlayersServerStatus(byte server, bool engaged, byte expected) =>
            Assert.Equal(expected, GameAudioService.MusicStatus(server, engaged));

        [Fact]
        public void EmptySlot_FallsBackToTheZoneMusic()
        {
            ZoneMusicState music = Zone(107, 108, 101, 0, 0);
            Assert.Equal(107, MusicDirector.ChooseTrack(music, new MusicContext(1, true, 12f), out MusicSlot slot));
            Assert.Equal(MusicSlot.ZoneDay, slot);
            Assert.Equal(101, MusicDirector.ChooseTrack(music, new MusicContext(1, false, 12f), out _));
            Assert.Equal(107, MusicDirector.ChooseTrack(Zone(107, 0), new MusicContext(0, false, 22f), out _));
        }

        [Fact]
        public void Engaging_FadesTheZoneTrackThenStartsBattle()
        {
            var (director, opened, mixer) = Make();
            ZoneMusicState music = Zone(107, 108, 101, 103);
            director.Update(music, new MusicContext(0, false, 12f), 0.016);
            Assert.Equal(new[] { 107 }, opened);
            mixer.Mix(new short[64]);

            director.Update(music, new MusicContext(1, false, 12f), 0.016);
            Assert.Equal(new[] { 107 }, opened); // still fading out
            director.Update(music, new MusicContext(1, false, 12f), MusicDirector.TrackFadeSeconds + 0.1);
            Assert.Equal(new[] { 107, 101 }, opened);
        }

        [Fact]
        public void SameTrackInTheNewSlot_KeepsPlaying()
        {
            var (director, opened, _) = Make();
            ZoneMusicState music = Zone(229, 229);
            director.Update(music, new MusicContext(0, false, 12f), 0.016);
            director.Update(music, new MusicContext(0, false, 20f), 0.016);
            Assert.Equal(new[] { 229 }, opened);
        }

        [Fact]
        public void EventSlotWrite_SameTrackKeepsPlaying()
        {
            // Retail (maintainer's check, round 2): a cutscene that sets the track already playing does not restart it.
            var (director, opened, mixer) = Make();
            ZoneMusicState music = Zone(151, 151);
            director.Update(music, new MusicContext(0, false, 12f), 0.016);
            mixer.Mix(new short[64]);
            music.SetEventSlot(0, 151);
            music.SetEventSlot(1, 151);
            director.Update(music, new MusicContext(0, false, 12f), 0.016);
            director.Update(music, new MusicContext(0, false, 12f), 2);
            Assert.Equal(new[] { 151 }, opened);
        }

        [Fact]
        public void PortJeuno324_ZoneMusicComesBackAtFullVolumeMidEvent()
        {
            // The script's order (traced): slots = 110 (zone), slots = 51, music volume 0 over 120, ..., slots = 110.
            var (director, opened, mixer) = Make();
            ZoneMusicState music = Zone(110, 110);
            var day = new MusicContext(0, false, 12f);
            director.Update(music, day, 0.016);
            music.SetEventSlot(0, 110);
            director.Update(music, day, 0.016);
            music.SetEventSlot(0, 51);
            mixer.Mix(new short[64]);
            director.Update(music, day, 0.016);
            director.Update(music, day, 2);
            Assert.Equal(new[] { 110, 51 }, opened);
            music.SetEventVolume(120, 0);
            director.Update(music, day, 0.016);
            Assert.Equal(0, music.Volume);

            music.SetEventSlot(0, 110);
            mixer.Mix(new short[64]);
            director.Update(music, day, 0.016);
            director.Update(music, day, 2);
            Assert.Equal(new[] { 110, 51, 110 }, opened);
            Assert.Equal(ZoneMusicState.MaxVolume, music.Volume); // the new track starts at its start volume

            music.EndEvent(); // the same track: it simply continues
            mixer.Mix(new short[64]);
            director.Update(music, day, 0.016);
            Assert.Equal(new[] { 110, 51, 110 }, opened);
            Assert.Equal(ZoneMusicState.MaxVolume, music.Volume);
        }

        [Fact]
        public void Override_ReplacesAndRestoresTheZoneMusic()
        {
            var (director, opened, mixer) = Make();
            ZoneMusicState music = Zone(107, 108);
            director.Update(music, new MusicContext(0, false, 12f), 0.016);
            director.SetOverride(1);
            mixer.Mix(new short[64]);
            director.Update(music, new MusicContext(0, false, 12f), 0.016);
            director.Update(music, new MusicContext(0, false, 12f), 2);
            director.ClearOverride();
            mixer.Mix(new short[64]);
            director.Update(music, new MusicContext(0, false, 12f), 0.016);
            director.Update(music, new MusicContext(0, false, 12f), 2);
            Assert.Equal(new[] { 107, 1, 107 }, opened);
        }
    }
}
