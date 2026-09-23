// tests/Gordian.Core.Tests/World/VanaTimeTests.cs
using System;
using Gordian.Core.World;
using Xunit;

namespace Gordian.Core.Tests.World
{
    public class VanaTimeTests
    {
        [Fact]
        public void VanaTime_EpochReturnsZeroHour()
        {
            // Epoch is 2002-01-01 00:00:00 JST, which is 2001-12-31 15:00:00 UTC (1009810800 Unix timestamp)
            var epoch = DateTimeOffset.FromUnixTimeSeconds(1009810800L).UtcDateTime;
            float hour = VanaTime.GetTimeOfDayHours(epoch);

            Assert.Equal(0.0f, hour, 4);
        }

        [Fact]
        public void VanaTime_AdvancesAtTwentyFiveTimesSpeed()
        {
            var epoch = DateTimeOffset.FromUnixTimeSeconds(1009810800L).UtcDateTime;

            // 1 real hour = 3600 real seconds = 90,000 Vana seconds = 25 Vana hours = 1 Vana day + 1 Vana hour
            var oneHourLater = epoch.AddHours(1);
            float hour = VanaTime.GetTimeOfDayHours(oneHourLater);

            Assert.Equal(1.0f, hour, 2);

            // 24 real hours = 600 Vana hours = 25 Vana days + 0 hours
            var oneDayLater = epoch.AddDays(1);
            float dayHour = VanaTime.GetTimeOfDayHours(oneDayLater);

            Assert.Equal(0.0f, dayHour, 2);
        }

        [Fact]
        public void VanaTime_MapsWeatherNumbersToWeatherIds()
        {
            Assert.Equal("fine", VanaTime.GetWeatherId(0));
            Assert.Equal("suny", VanaTime.GetWeatherId(1));
            Assert.Equal("clod", VanaTime.GetWeatherId(2));
            Assert.Equal("mist", VanaTime.GetWeatherId(3));
            Assert.Equal("dryw", VanaTime.GetWeatherId(4));
            Assert.Equal("heat", VanaTime.GetWeatherId(5));
            Assert.Equal("rain", VanaTime.GetWeatherId(6));
            Assert.Equal("squl", VanaTime.GetWeatherId(7));
            Assert.Equal("dust", VanaTime.GetWeatherId(8));
            Assert.Equal("sand", VanaTime.GetWeatherId(9));
            Assert.Equal("wind", VanaTime.GetWeatherId(10));
            Assert.Equal("snow", VanaTime.GetWeatherId(12));
            Assert.Equal("thdr", VanaTime.GetWeatherId(14));
            Assert.Equal("aura", VanaTime.GetWeatherId(16));
            Assert.Equal("fine", VanaTime.GetWeatherId(99)); // Unknown fallback
        }

        [Fact]
        public void WorldState_UpdatesWeatherAndFiresEvent()
        {
            var world = new WorldState();
            Assert.Equal(0, world.WeatherNumber);
            Assert.Equal("fine", world.WeatherId);

            string reportedWeather = string.Empty;
            world.WeatherChanged += w => reportedWeather = w;

            world.UpdateWeather(2); // Cloudy ("clod")

            Assert.Equal(2, world.WeatherNumber);
            Assert.Equal("clod", world.WeatherId);
            Assert.Equal("clod", reportedWeather);

            // Identical weather should not re-fire event
            reportedWeather = string.Empty;
            world.UpdateWeather(2);
            Assert.Equal(string.Empty, reportedWeather);
        }

        [Fact]
        public void VanaTime_CanonicalWeatherCategory_MapsElementalWeathersToCanonicalFour()
        {
            // Clear sky category
            Assert.Equal("fine", VanaTime.GetCanonicalWeatherCategory("fine"));
            Assert.Equal("fine", VanaTime.GetCanonicalWeatherCategory("wind"));
            Assert.Equal("fine", VanaTime.GetCanonicalWeatherCategory("aura"));
            Assert.Equal("fine", VanaTime.GetCanonicalWeatherCategory("ligt"));
            Assert.Equal("fine", VanaTime.GetCanonicalWeatherCategory(null));
            Assert.Equal("fine", VanaTime.GetCanonicalWeatherCategory("unknown_weather"));

            // Sunshine category
            Assert.Equal("suny", VanaTime.GetCanonicalWeatherCategory("suny"));
            Assert.Equal("suny", VanaTime.GetCanonicalWeatherCategory("dryw"));
            Assert.Equal("suny", VanaTime.GetCanonicalWeatherCategory("heat"));

            // Overcast / Storm category
            Assert.Equal("clod", VanaTime.GetCanonicalWeatherCategory("clod"));
            Assert.Equal("clod", VanaTime.GetCanonicalWeatherCategory("rain"));
            Assert.Equal("clod", VanaTime.GetCanonicalWeatherCategory("squl"));
            Assert.Equal("clod", VanaTime.GetCanonicalWeatherCategory("snow"));
            Assert.Equal("clod", VanaTime.GetCanonicalWeatherCategory("bliz"));
            Assert.Equal("clod", VanaTime.GetCanonicalWeatherCategory("thdr"));
            Assert.Equal("clod", VanaTime.GetCanonicalWeatherCategory("bolt"));
            Assert.Equal("clod", VanaTime.GetCanonicalWeatherCategory("dust"));
            Assert.Equal("clod", VanaTime.GetCanonicalWeatherCategory("sand"));
            Assert.Equal("clod", VanaTime.GetCanonicalWeatherCategory("stom"));
            Assert.Equal("clod", VanaTime.GetCanonicalWeatherCategory("dark"));
            Assert.Equal("clod", VanaTime.GetCanonicalWeatherCategory("fogd"));

            // Mist / Fog category
            Assert.Equal("mist", VanaTime.GetCanonicalWeatherCategory("mist"));
        }

        [Fact]
        public void VanaTime_MoonPhase_CalculatesValidPhaseAndDirection()
        {
            var testTime = new DateTime(2026, 9, 23, 12, 0, 0, DateTimeKind.Utc);
            int phase = VanaTime.GetMoonPhase(testTime);
            int direction = VanaTime.GetMoonDirection(testTime);
            int phaseIndex = VanaTime.GetMoonPhaseIndex(testTime);

            Assert.InRange(phase, 0, 100);
            Assert.InRange(direction, 0, 2);
            Assert.InRange(phaseIndex, 0, 11);

            // Over an entire 84-day Vana cycle (84 real days / 25 = 3.36 real days = 80.64 hours),
            // phase should cycle smoothly from near 0% to 100% and back
            bool sawNearNew = false;
            bool sawNearFull = false;
            for (int h = 0; h < 84; h++)
            {
                var time = testTime.AddHours(h);
                int p = VanaTime.GetMoonPhase(time);
                if (p <= 10) sawNearNew = true;
                if (p >= 90) sawNearFull = true;
            }

            Assert.True(sawNearNew, "Moon phase should reach near New Moon (<= 10%) over an 84-day lunar cycle.");
            Assert.True(sawNearFull, "Moon phase should reach near Full Moon (>= 90%) over an 84-day lunar cycle.");
        }

        [Fact]
        public void VanaTime_GetSunDirection_ProducesExpectedDiurnalCycle()
        {
            // Noon (12:00): Sun at peak elevation (+Y > 0)
            var noon = VanaTime.GetSunDirection(12.0f);
            Assert.True(noon.Y > 0.9f, "Noon sun elevation should be near zenith (+Y)");

            // Midnight (00:00 / 24:00): Sun at nadir (-Y < -0.9)
            var midnight = VanaTime.GetSunDirection(0.0f);
            Assert.True(midnight.Y < -0.9f, "Midnight sun elevation should be near nadir (-Y)");

            // Dawn (06:00): Sun rising in east (+X)
            var dawn = VanaTime.GetSunDirection(6.0f);
            Assert.True(dawn.X > 0.9f, "Dawn sun should point towards east (+X)");
            Assert.InRange(dawn.Y, -0.2f, 0.2f);

            // Dusk (18:00): Sun setting in west (-X)
            var dusk = VanaTime.GetSunDirection(18.0f);
            Assert.True(dusk.X < -0.9f, "Dusk sun should point towards west (-X)");
            Assert.InRange(dusk.Y, -0.2f, 0.2f);
        }

        [Fact]
        public void VanaTime_SynchronizeServerTime_AdjustsClockOffsetAndVanadielSeconds()
        {
            VanaTime.ResetClockOffset();
            Assert.Equal(0, VanaTime.ServerClockOffsetSeconds);

            var nowUtc = DateTime.UtcNow;
            long baselineVanaSeconds = VanaTime.GetVanadielSeconds(nowUtc);

            // Simulate server clock 60 Earth seconds ahead
            long clientDeltaEarthSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - VanaTime.VanadielEpochUnixSeconds;
            uint simulatedServerGameTime = (uint)(clientDeltaEarthSeconds + 60);

            VanaTime.SynchronizeServerTime(simulatedServerGameTime);
            Assert.Equal(60, VanaTime.ServerClockOffsetSeconds);

            // 60 Earth seconds * 25 multiplier = 1,500 Vana'diel seconds ahead
            long adjustedVanaSeconds = VanaTime.GetVanadielSeconds(nowUtc);
            Assert.Equal(baselineVanaSeconds + 1500, adjustedVanaSeconds);

            // Reset restores baseline
            VanaTime.ResetClockOffset();
            Assert.Equal(0, VanaTime.ServerClockOffsetSeconds);
            Assert.Equal(baselineVanaSeconds, VanaTime.GetVanadielSeconds(nowUtc));
        }
    }
}
