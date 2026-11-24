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

        [Fact]
        public void CyclePhase_WrapsTheClockIntoOneCycle()
        {
            Assert.Equal(0.0f, ZoneWind.CyclePhase(0.0));
            Assert.Equal(0.25f, ZoneWind.CyclePhase(1.25 * ZoneWind.PeriodSeconds), 4);
            Assert.Equal(0.75f, ZoneWind.CyclePhase(-0.25 * ZoneWind.PeriodSeconds), 4);
            Assert.Equal(ZoneWind.Factor(3.3), ZoneWind.FactorAtPhase(ZoneWind.CyclePhase(3.3)), 4);
        }

        [Fact]
        public void PlacementPhase_IsStablePerPlacementAndSpreadOverTheCycle()
        {
            // Round 3: neighbouring palms sway at unrelated points of the gust, so the offsets of a grove of placements
            // a few yalms apart should cover the cycle rather than bunch together.
            var a = new System.Numerics.Vector3(84.17f, -0.31f, -68.96f);
            Assert.Equal(ZoneWind.PlacementPhase(a), ZoneWind.PlacementPhase(a));
            var bins = new int[4];
            for (int x = 0; x < 20; x++)
            for (int z = 0; z < 20; z++)
            {
                float phase = ZoneWind.PlacementPhase(new System.Numerics.Vector3(80.0f + x * 3.1f, -0.5f, -70.0f + z * 2.7f));
                Assert.InRange(phase, 0.0f, 0.99999f);
                bins[(int)(phase * 4)]++;
            }
            foreach (int count in bins) Assert.InRange(count, 70, 130);
        }

        [Fact]
        public void GlslFunction_IsTheSameCurve()
        {
            string glsl = ZoneWind.GlslFunction();
            Assert.Contains("float ZoneWindFactor(float phase)", glsl);
            Assert.Contains("clamp(f, 0.0, 1.0)", glsl);
            Assert.DoesNotContain(",", glsl.Replace("clamp(f, 0.0, 1.0)", ""));
        }
    }
}
