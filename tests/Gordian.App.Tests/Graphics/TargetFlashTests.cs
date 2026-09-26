// tests/Gordian.App.Tests/Graphics/TargetFlashTests.cs
using System.Numerics;
using Gordian.App.Graphics;
using Xunit;

namespace Gordian.App.Tests.Graphics
{
    public class TargetFlashTests
    {
        [Fact]
        public void Flash_PlaysThreeTriangularPulsesThenStops()
        {
            Assert.Equal(0.0f, TargetFlash.Intensity(0.0));
            Assert.Equal(TargetFlash.PeakLight, TargetFlash.Intensity(TargetFlash.PulseSeconds / 2), 3);
            Assert.Equal(TargetFlash.PeakLight / 2, TargetFlash.Intensity(TargetFlash.PulseSeconds / 4), 3);

            // Each pulse returns to no flash before the next starts.
            Assert.Equal(0.0f, TargetFlash.Intensity(TargetFlash.PulseSeconds), 3);
            Assert.Equal(TargetFlash.PeakLight, TargetFlash.Intensity(TargetFlash.PulseSeconds * 2.5), 3);

            Assert.Equal(0.0f, TargetFlash.Intensity(TargetFlash.DurationSeconds));
            Assert.Equal(0.0f, TargetFlash.Intensity(10.0));
            Assert.Equal(0.0f, TargetFlash.Intensity(-1.0));
        }

        [Fact]
        public void ProjectToScreen_MapsClipSpaceToPixels()
        {
            // Identity view-projection: clip space is the display point itself.
            var center = VeldridViewportControl.ProjectToScreen(Vector3.Zero, Matrix4x4.Identity, 800, 600, clipSpaceYInverted: false);
            Assert.Equal(new Vector2(400, 300), center);

            // +Y is up on screen (smaller pixel row) unless the backend's clip space is inverted.
            Assert.Equal(new Vector2(600, 150), VeldridViewportControl.ProjectToScreen(new Vector3(0.5f, 0.5f, 0), Matrix4x4.Identity, 800, 600, false));
            Assert.Equal(new Vector2(600, 450), VeldridViewportControl.ProjectToScreen(new Vector3(0.5f, 0.5f, 0), Matrix4x4.Identity, 800, 600, true));

            // Behind the camera (w <= 0): nothing to point at.
            var behind = new Matrix4x4(1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, -1);
            Assert.Null(VeldridViewportControl.ProjectToScreen(Vector3.Zero, behind, 800, 600, false));
        }
    }
}
