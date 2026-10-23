// tests/Gordian.App.Tests/Audio/TitleMusicTests.cs
using System;
using System.Linq;
using Gordian.App.Audio;
using Xunit;

namespace Gordian.App.Tests.Audio
{
    /// <summary>The lobby's title music choices (#378).</summary>
    public class TitleMusicTests
    {
        [Theory]
        [InlineData(TitleMusicOption.Latest, 58)]
        [InlineData(TitleMusicOption.VanadielMarch, 108)]
        [InlineData(TitleMusicOption.Unity, 242)]
        [InlineData(TitleMusicOption.VanadielMarch4, 179)]
        [InlineData(TitleMusicOption.WingsOfTheGoddess, 140)]
        [InlineData(TitleMusicOption.ANewDirection, 58)]
        public void EachOption_PlaysItsTrack(TitleMusicOption option, int track) =>
            Assert.Equal(track, TitleMusic.Track(option, new Random(1)));

        [Fact]
        public void Random_PicksAmongTheTitleSongs()
        {
            var random = new Random(7);
            var picked = Enumerable.Range(0, 200).Select(_ => TitleMusic.Track(TitleMusicOption.Random, random)).Distinct().OrderBy(t => t).ToArray();
            Assert.Equal(TitleMusic.Tracks.OrderBy(t => t).ToArray(), picked);
        }

        [Fact]
        public void TheDefaultIsTheLatestTitle() => Assert.Equal(TitleMusicOption.Latest, GameAudioService.TitleMusicChoice);
    }
}
