using System.Numerics;
using Gordian.Core.World;
using Gordian.Engine.Graphics;
using Gordian.Core.Resources;
using Gordian.Core.Resources.Graphics;

namespace Gordian.Engine.Tests.Resources
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
        public void IndoorKeyframe_KeepsItsLightDirectionWhenTheClockMoves()
        {
            // #612: the viewport applies the keyframe, then sets the time of day; indoors the clock must not turn the
            // keyframe's light back into the celestial sun.
            var slot = new Vector4(0.0f, 127.0f / 255.0f, 0.0f, 0.5f);
            var settings = new ZoneEnvironmentSettings();
            settings.ApplyKeyframe(new EnvironmentKeyframe { Indoors = true, TerrainMoonColor = slot });
            settings.SetTimeOfDay(18.0f);
            Assert.Equal(1.0f, settings.SunDirection.Y, 3);
            Assert.Equal(18.0f, settings.TimeOfDayHours, 3);

            settings.ApplyKeyframe(new EnvironmentKeyframe { Indoors = false });
            settings.SetTimeOfDay(18.0f);
            Assert.Equal(VanaTime.GetSunDirection(18.0f), settings.SunDirection);
        }

        [Fact]
        public void IndoorOnlyZone_IgnoresTheWeather_OutdoorZoneDoesNot()
        {
            // #612 round 2: Eldieme's dust keyframe (brown 0-39.5 yalm fog) is not shown inside; indoor zones keep fine.
            var indoor = new ZoneEnvironmentData();
            indoor.AddKeyframe("fine", new EnvironmentKeyframe { Indoors = true, TerrainFogEnd = 84f });
            indoor.AddKeyframe("dust", new EnvironmentKeyframe { Indoors = true, TerrainFogEnd = 39.5f });
            Assert.True(indoor.IsIndoorOnly);
            Assert.Equal(84f, indoor.Interpolate(12f, "dust")!.TerrainFogEnd);

            var outdoor = new ZoneEnvironmentData();
            outdoor.AddKeyframe("fine", new EnvironmentKeyframe { TerrainFogEnd = 300f });
            outdoor.AddKeyframe("dust", new EnvironmentKeyframe { TerrainFogEnd = 200f });
            Assert.False(outdoor.IsIndoorOnly);
            Assert.Equal(200f, outdoor.Interpolate(12f, "dust")!.TerrainFogEnd);
        }

        [Fact]
        public void IndoorKeyframe_TakesTheIndoorLightConversion_AndScaledFog()
        {
            // #612 round 2: indoor ambient byte/255 with the dark lift, directional light without the lift, fog x multiplier.
            var ambient = new Vector4(0x41 / 255f, 0x45 / 255f, 0x49 / 255f, 1f); // Garlaige Citadel
            var sun = new Vector4(0x36 / 255f, 0x3E / 255f, 0x45 / 255f, 1f);
            var settings = new ZoneEnvironmentSettings();
            settings.ApplyKeyframe(new EnvironmentKeyframe
            {
                Indoors = true, TerrainAmbientColor = ambient, TerrainSunColor = sun, TerrainDiffuseMult = 1.98f,
                TerrainFogStart = 0f, TerrainFogEnd = 61.7f,
            });
            Assert.Equal(2f * ZoneEnvironmentSettings.AmbientToLight(ambient).X, settings.AmbientColor.X, 4);
            Assert.Equal(0x36 / 255f * 1.98f, settings.SunColor.X, 4);
            Assert.Equal(0f, settings.FogStart);
            Assert.Equal(61.7f * settings.DrawDistanceMultiplier, settings.FogEnd, 3);
        }

        [Fact]
        public void LowerDelkfuttsTower_IsLitByItsIndoorWeatherKeyframes()
        {
            // #612: every 0x2F of zone 184 (weat/<weather>/0000) carries the indoor flag; they are the zone's own
            // blue-violet light (terrain sun 0x32 0x37 0x58, ambient 0x28 0x29 0x37), not dropped. Stage zone 540 is green.
            const string gameDirectory = @"G:\Program Files (x86)\PlayOnline\SquareEnix\FINAL FANTASY XI";
            if (!Directory.Exists(gameDirectory)) return;
            var rm = new ResourceManager(gameDirectory);
            rm.InitializeFileTable();
            if (!rm.TryLoadZone(184, out var zone, out _)) return;

            var keyframe = zone.EnvironmentData?.Interpolate(12.0f, "fine");
            Assert.NotNull(keyframe);
            Assert.True(keyframe!.Indoors);
            Assert.Equal(0x58, (int)MathF.Round(keyframe.TerrainSunColor.Z * 255));
            Assert.True(keyframe.TerrainSunColor.Z > keyframe.TerrainSunColor.X);

            Assert.True(rm.TryLoadZone(540, out var stage, out _));
            var green = stage.EnvironmentData!.Interpolate(12.0f, "fine")!;
            Assert.True(green.TerrainSunColor.Y > green.TerrainSunColor.X && green.TerrainSunColor.Y > green.TerrainSunColor.Z);

            // A zone with outdoor keyframes keeps them (East Ronfaure).
            Assert.True(rm.TryLoadZone(101, out var outdoor, out _));
            Assert.False(outdoor.EnvironmentData!.Interpolate(12.0f, "fine")!.Indoors);
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
