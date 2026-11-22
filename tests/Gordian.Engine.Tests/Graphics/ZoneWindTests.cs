// tests/Gordian.Engine.Tests/Graphics/ZoneWindTests.cs
using Gordian.Engine.Graphics;
using Xunit;

namespace Gordian.Engine.Tests.Graphics
{
    /// <summary>The zone wind factor (#606): a 0-1-0 triangle wave, two seconds each way (xim's WindFactor).</summary>
    public class ZoneWindTests
    {
        [Theory]
        [InlineData(0.0, 0.0f)]
        [InlineData(1.0, 0.5f)]
        [InlineData(2.0, 1.0f)]
        [InlineData(3.0, 0.5f)]
        [InlineData(4.0, 0.0f)]
        [InlineData(5.0, 0.5f)]
        [InlineData(-1.0, 0.5f)]
        public void Factor_IsATriangleWaveOfTwoSecondLegs(double seconds, float expected)
        {
            Assert.Equal(expected, ZoneWind.Factor(seconds), 4);
        }

        [Fact]
        public void Factor_IsZeroForAClockThatIsNotANumber() => Assert.Equal(0.0f, ZoneWind.Factor(double.NaN));
    }
}
