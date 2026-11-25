// tests/Gordian.App.Tests/Graphics/PositionOverlayTests.cs
using System.Numerics;
using Gordian.App.Graphics;
using Xunit;

namespace Gordian.App.Tests.Graphics
{
    /// <summary>The viewport position overlay's command, heading readout and target distance.</summary>
    // The overlay switch is process-wide (ViewportRenderSettings): run with the other tests of it, not in parallel.
    [Collection(ViewportRenderSettingsCollection.Name)]
    public class PositionOverlayTests
    {
        [Fact]
        public void Command_TogglesOrSetsTheProcessWideSwitch()
        {
            bool before = ViewportRenderSettings.PositionOverlay;
            try
            {
                ViewportRenderSettings.PositionOverlay = false;
                Assert.Contains("shown", PositionOverlay.HandleCommand(""));
                Assert.True(ViewportRenderSettings.PositionOverlay);
                Assert.Contains("hidden", PositionOverlay.HandleCommand("toggle"));
                Assert.False(ViewportRenderSettings.PositionOverlay);
                PositionOverlay.HandleCommand("on");
                PositionOverlay.HandleCommand("ON");
                Assert.True(ViewportRenderSettings.PositionOverlay);
                PositionOverlay.HandleCommand("off");
                Assert.False(ViewportRenderSettings.PositionOverlay);
                Assert.StartsWith("Usage", PositionOverlay.HandleCommand("maybe"));
                Assert.False(ViewportRenderSettings.PositionOverlay);
            }
            finally
            {
                ViewportRenderSettings.PositionOverlay = before;
            }
        }

        [Theory]
        [InlineData(0, 0f, "E")]
        [InlineData(64, 90f, "S")]
        [InlineData(128, 180f, "W")]
        [InlineData(192, 270f, "N")]
        [InlineData(224, 315f, "NE")]
        [InlineData(250, 351.5625f, "E")]
        public void Heading_IsTheWireByteClockwiseFromEast(byte heading, float degrees, string compass)
        {
            Assert.Equal(degrees, PositionOverlay.HeadingDegrees(heading), 3);
            Assert.Equal(compass, PositionOverlay.Compass(heading));
        }

        [Fact]
        public void Distance_IsStraightLine()
        {
            var info = new PositionOverlayInfo(true, new Vector3(0, 0, 0), 0, true, new Vector3(3, 0, 4), "Bee");
            Assert.Equal(5f, info.Distance, 4);
            Assert.False(PositionOverlayInfo.From(null).Shown);
        }
    }
}
