// tests/Gordian.Engine.Tests/Graphics/WeatherCrossFadeTests.cs
using System.Numerics;
using Gordian.Core.Resources.Graphics;
using Gordian.Engine.Graphics;
using Xunit;

namespace Gordian.Engine.Tests.Graphics
{
    public class WeatherCrossFadeTests
    {
        private static ZoneEnvironmentData TwoWeathers()
        {
            var data = new ZoneEnvironmentData();
            data.AddKeyframe("clod", new EnvironmentKeyframe { Hour = 0, TerrainAmbientColor = new Vector4(0.2f, 0.2f, 0.2f, 1f) });
            data.AddKeyframe("suny", new EnvironmentKeyframe { Hour = 0, TerrainAmbientColor = new Vector4(0.6f, 0.6f, 0.6f, 1f) });
            return data;
        }

        [Fact]
        public void FirstWeather_ShowsAtOnce()
        {
            var fade = new WeatherCrossFade();
            Assert.True(fade.Advance("clod", 0.016f));
            Assert.Equal("clod", fade.Weather);
            Assert.Null(fade.PreviousWeather);
            Assert.Equal(1.0f, fade.Blend);
            Assert.False(fade.Advance("clod", 0.016f));
        }

        [Fact]
        public void WeatherChange_BlendsKeyframesOverTheDuration()
        {
            var data = TwoWeathers();
            var fade = new WeatherCrossFade();
            fade.Reset("clod");

            Assert.True(fade.Advance("suny", 0.016f));
            Assert.Equal("clod", fade.PreviousWeather);
            Assert.Equal(0.0f, fade.Blend);
            Assert.Equal(0.2f, fade.Resolve(data, 22.0f)!.TerrainAmbientColor.X, 4);

            Assert.True(fade.Advance("suny", WeatherCrossFade.DurationSeconds / 2));
            Assert.Equal(0.4f, fade.Resolve(data, 22.0f)!.TerrainAmbientColor.X, 4);

            var environment = new ZoneEnvironmentSettings();
            fade.ApplyTo(environment);
            Assert.Equal("suny", environment.WeatherId);
            Assert.Equal("clod", environment.PreviousWeatherId);
            Assert.Equal(0.5f, environment.WeatherBlend, 4);

            Assert.True(fade.Advance("suny", WeatherCrossFade.DurationSeconds));
            Assert.Null(fade.PreviousWeather);
            Assert.Equal(0.6f, fade.Resolve(data, 22.0f)!.TerrainAmbientColor.X, 4);
            Assert.False(fade.Advance("suny", 0.016f));
        }

        [Fact]
        public void Reset_EndsAFade()
        {
            var fade = new WeatherCrossFade();
            fade.Reset("clod");
            fade.Advance("suny", 1.0f);
            fade.Reset("mist");
            Assert.Equal("mist", fade.Weather);
            Assert.Null(fade.PreviousWeather);
            Assert.Equal(1.0f, fade.Blend);
        }
    }
}
