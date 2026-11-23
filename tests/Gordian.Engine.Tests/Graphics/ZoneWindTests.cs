// tests/Gordian.Engine.Tests/Graphics/ZoneWindTests.cs
using Gordian.Engine.Graphics;
using Xunit;

namespace Gordian.Engine.Tests.Graphics
{
    /// <summary>
    /// The zone wind factor (#606): retail's 5.15-second gust (the Valkurm Dunes palms, round 2), from rest to a full lean, a
    /// half relax, a second lean and back to rest.
    /// </summary>
    public class ZoneWindTests
    {
        [Theory]
        [InlineData(0.0, 0.0f)]
        [InlineData(ZoneWind.PeakSeconds, 1.0f)]
        [InlineData(ZoneWind.PeriodSeconds, 0.0f)]
        [InlineData(ZoneWind.PeriodSeconds + ZoneWind.PeakSeconds, 1.0f)]
        [InlineData(-ZoneWind.PeriodSeconds, 0.0f)]
        public void Factor_RestsAtTheCycleStartAndLeansFullyAtThePeak(double seconds, float expected)
        {
            Assert.Equal(expected, ZoneWind.Factor(seconds), 2);
        }

        [Fact]
        public void Factor_RelaxesHalfWayBetweenTheTwoLeans()
        {
            // Measured: about 0.52 at 0.55 of the cycle, then a second lean to about 0.87 at 0.7.
            Assert.InRange(ZoneWind.Factor(0.55 * ZoneWind.PeriodSeconds), 0.45f, 0.6f);
            Assert.InRange(ZoneWind.Factor(0.7 * ZoneWind.PeriodSeconds), 0.8f, 0.95f);
        }

        [Fact]
        public void Factor_StaysWithinZeroAndOne()
        {
            for (int i = 0; i <= 1000; i++)
            {
                float f = ZoneWind.Factor(i * ZoneWind.PeriodSeconds / 1000.0);
                Assert.InRange(f, 0.0f, 1.0f);
            }
        }

        [Fact]
        public void Factor_IsZeroForAClockThatIsNotANumber() => Assert.Equal(0.0f, ZoneWind.Factor(double.NaN));
    }
}
