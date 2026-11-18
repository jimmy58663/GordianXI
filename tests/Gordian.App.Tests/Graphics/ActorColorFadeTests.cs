// tests/Gordian.App.Tests/Graphics/ActorColorFadeTests.cs
using System.Numerics;
using Gordian.App.Graphics;
using Gordian.Core.World;
using Xunit;

namespace Gordian.App.Tests.Graphics
{
    /// <summary>The RGB of routine actor colour fades (#590): level down darkens the character to black and back.</summary>
    public class ActorColorFadeTests
    {
        private static ZoneSceneUniform Lit() => new()
        {
            SunColor = new Vector4(0.4f, 0.3f, 0.2f, 1f),
            MoonColor = new Vector4(0.1f, 0.1f, 0.2f, 1f),
            AmbientColor = new Vector4(0.2f, 0.2f, 0.2f, 1f),
            WeatherParams = new Vector4(0f, 0f, 0.75f, 0f),
        };

        [Fact]
        public void AsDrawn_LeavesTheLightsAlone()
        {
            var uniform = Lit();
            EntityRenderer.ApplyActorColor(ref uniform, Vector3.One);
            Assert.Equal(Lit().SunColor, uniform.SunColor);
            Assert.Equal(0.75f, uniform.WeatherParams.Z);
        }

        [Fact]
        public void GreyFade_ScalesEveryLightAndTheCap_SoTheColourIsMultiplied()
        {
            var uniform = Lit();
            EntityRenderer.ApplyActorColor(ref uniform, new Vector3(0.5f));
            Assert.Equal(0.2f, uniform.SunColor.X, 5);
            Assert.Equal(0.05f, uniform.MoonColor.Y, 5);
            Assert.Equal(0.1f, uniform.AmbientColor.Z, 5);
            Assert.Equal(0.375f, uniform.WeatherParams.Z, 5);
            Assert.Equal(1f, uniform.SunColor.W);
        }

        [Fact]
        public void LevelDown_BlackFade_LeavesNoLight_ButACapTheShaderStillReadsAsACap()
        {
            // File 3309's main: 0x29 to 80000000 at frame 0 over 40 (black, opaque), back to 80808080 at 80.
            var track = new ActorColorTrack();
            track.FadeTo(0, 40 / 60.0, ActorColorTrack.ColorOf(0x80000000));
            track.FadeTo(80 / 60.0, 40 / 60.0, ActorColorTrack.ColorOf(0x80808080));
            var dark = track.Sample(60 / 60.0);
            Assert.Equal(Vector3.Zero, new Vector3(dark.X, dark.Y, dark.Z));
            Assert.Equal(1f, dark.W);

            var uniform = Lit();
            EntityRenderer.ApplyActorColor(ref uniform, new Vector3(dark.X, dark.Y, dark.Z));
            Assert.Equal(Vector4.UnitW, uniform.SunColor);
            Assert.Equal(Vector4.UnitW, uniform.AmbientColor);
            Assert.True(uniform.WeatherParams.Z > 0f && uniform.WeatherParams.Z < 0.001f);

            var back = track.Sample(130 / 60.0);
            Assert.Equal(Vector3.One, new Vector3(back.X, back.Y, back.Z));
        }
    }
}
