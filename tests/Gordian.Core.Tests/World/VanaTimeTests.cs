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
    }
}
