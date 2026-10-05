// tests/Gordian.App.Tests/Audio/VolumeMixTests.cs
using System.Linq;
using Gordian.App.Audio;
using Xunit;

namespace Gordian.App.Tests.Audio
{
    /// <summary>The config page sliders to mixer buses (#44).</summary>
    public class VolumeMixTests
    {
        [Fact]
        public void MusicSliderDrivesMusic_EffectsSliderDrivesTheRest()
        {
            var gains = VolumeMix.CategoryGains(50, 20).ToDictionary(g => g.Category, g => g.Gain);
            Assert.Equal(0.5f, gains[AudioCategory.Music]);
            Assert.Equal(0.2f, gains[AudioCategory.Effects]);
            Assert.Equal(0.2f, gains[AudioCategory.System]);
            Assert.Equal(0.2f, gains[AudioCategory.Zone]);
        }

        [Fact]
        public void SliderGain_Clamps()
        {
            Assert.Equal(0f, VolumeMix.SliderGain(-5));
            Assert.Equal(1f, VolumeMix.SliderGain(150));
        }
    }
}
