using System.Numerics;
using Gordian.Core.Graphics;
using Gordian.Core.Resources;
using Gordian.Core.Resources.Graphics;

namespace Gordian.Core.Tests.Resources
{
    public class ZoneSubEnvironmentTests
    {
        [Fact]
        public void SubEnvironmentKeyframes_InterpolateSeparatelyFromTheOutdoorWeather()
        {
            var data = new ZoneEnvironmentData();
            data.AddKeyframe("fine", new EnvironmentKeyframe { Hour = 12, TerrainSunColor = new Vector4(0.8f) });
            data.AddSubEnvironmentKeyframe("ev01", "fine", new EnvironmentKeyframe { Hour = 0, Indoors = true, TerrainAmbientColor = new Vector4(0.4f) });
            data.AddSubEnvironmentKeyframe("ev01", "fine", new EnvironmentKeyframe { Hour = 12, Indoors = true, TerrainAmbientColor = new Vector4(0.6f) });

            Assert.Equal(new[] { "ev01" }, data.SubEnvironmentIds);
            var noon = data.InterpolateSubEnvironment("EV01", 12.0f, "fine");
            var dawn = data.InterpolateSubEnvironment("ev01", 6.0f, "rain"); // unknown weather falls back like outdoors
            Assert.True(noon!.Indoors);
            Assert.Equal(0.6f, noon.TerrainAmbientColor.X, 3);
            Assert.Equal(0.5f, dawn!.TerrainAmbientColor.X, 3);
            Assert.Null(data.InterpolateSubEnvironment("ev02", 12.0f, "fine"));
            Assert.Equal(0.8f, data.Interpolate(12.0f, "fine")!.TerrainSunColor.X, 3);
        }

        [Fact]
        public void IndoorKeyframe_TakesItsLightDirectionFromTheMoonSlot()
        {
            // Metalworks' interiors: moon RGB = (0x00, 0x7F, 0x00), light shining straight down (FFXI +Y is down).
            var slot = new Vector4(0.0f, 127.0f / 255.0f, 0.0f, 0.5f);
            var up = ZoneEnvironmentSettings.IndoorLightDirection(slot);
            Assert.Equal(1.0f, up.Y, 3);

            var settings = new ZoneEnvironmentSettings();
            settings.ApplyKeyframe(new EnvironmentKeyframe { Indoors = true, TerrainMoonColor = slot, TerrainSunColor = new Vector4(0.27f) });
            Assert.Equal(Vector3.Zero, settings.MoonColor);
            Assert.Equal(1.0f, settings.SunDirection.Y, 3);
        }

        [Fact]
        public void Metalworks_LoadsItsIndoorSubEnvironments()
        {
            const string gameDirectory = @"G:\Program Files (x86)\PlayOnline\SquareEnix\FINAL FANTASY XI";
            if (!Directory.Exists(gameDirectory)) return;
            var rm = new ResourceManager(gameDirectory);
            rm.InitializeFileTable();
            if (!rm.TryLoadZone(237, out var zone, out _) || zone?.EnvironmentData == null) return;

            var data = zone.EnvironmentData;
            Assert.Contains("ev01", data.SubEnvironmentIds, StringComparer.OrdinalIgnoreCase);
            Assert.Contains("ev02", data.SubEnvironmentIds, StringComparer.OrdinalIgnoreCase);

            // ev01 is a dim indoor light (sun 0x44), far below the outdoor noon sun (0xCF).
            var indoor = data.InterpolateSubEnvironment("ev01", 12.0f, "fine")!;
            var outdoor = data.Interpolate(12.0f, "fine")!;
            Assert.True(indoor.Indoors);
            Assert.InRange(indoor.TerrainSunColor.X, 0.25f, 0.28f);
            Assert.True(outdoor.TerrainSunColor.X > 0.8f);
            Assert.True(zone.MeshGroups.Count(g => g.EnvironmentId == "ev01") > 100);
        }
    }
}
