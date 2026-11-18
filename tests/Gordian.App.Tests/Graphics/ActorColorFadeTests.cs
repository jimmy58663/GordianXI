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
        public void GreyFade_ScalesEveryLightAndTheCap_DownToTheFloor()
        {
            var uniform = Lit();
            EntityRenderer.ApplyActorColor(ref uniform, new Vector3(0.5f));
            float m = EntityRenderer.ActorColorDarkFloor + (1f - EntityRenderer.ActorColorDarkFloor) * 0.5f;
            Assert.Equal(0.4f * m, uniform.SunColor.X, 5);
            Assert.Equal(0.1f * m, uniform.MoonColor.Y, 5);
            Assert.Equal(0.2f * m, uniform.AmbientColor.Z, 5);
            Assert.Equal(0.75f * m, uniform.WeatherParams.Z, 5);
            Assert.Equal(1f, uniform.SunColor.W);
        }

        [Fact]
        public void LevelDown_BlackFade_DarkensToTheFloor_AsRetailDoes()
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
            float floor = EntityRenderer.ActorColorDarkFloor;
            Assert.Equal(0.4f * floor, uniform.SunColor.X, 5);
            Assert.Equal(0.2f * floor, uniform.AmbientColor.Y, 5);
            Assert.Equal(0.75f * floor, uniform.WeatherParams.Z, 5);

            var back = track.Sample(130 / 60.0);
            Assert.Equal(Vector3.One, new Vector3(back.X, back.Y, back.Z));
        }
    }
}
