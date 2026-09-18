// tests/Gordian.Core.Tests/Graphics/ViewportCameraTests.cs
using System;
using System.Numerics;
using Gordian.Core.Graphics;
using Xunit;

namespace Gordian.Core.Tests.Graphics
{
    public class ViewportCameraTests
    {
        [Fact]
        public void ViewportCamera_ThirdPersonOrbital_PositionsEyeBehindTarget()
        {
            var camera = new ViewportCamera
            {
                Mode = CameraMode.ThirdPersonOrbital,
                EyeOffset = Vector3.Zero
            };

            // Target at (0, 0, 0), looking forward (Pitch = 0, Yaw = 0) at distance 10
            camera.Update(Vector3.Zero, pitch: 0.0f, yaw: 0.0f, distance: 10.0f, aspectRatio: 16.0f / 9.0f);

            Assert.Equal(Vector3.Zero, camera.Target);
            // In our spherical coordinates (sinY * cosP, sinP, cosY * cosP):
            // yaw = 0 => (0, 0, 1), eye = target - forward * dist = (0, 0, -10)
            Assert.InRange(camera.Position.X, -0.01f, 0.01f);
            Assert.InRange(camera.Position.Y, -0.01f, 0.01f);
            Assert.InRange(camera.Position.Z, -10.01f, -9.99f);
        }

        [Fact]
        public void ViewportCamera_ThirdPersonOrbital_PositivePitchElevatesEyeAboveTarget()
        {
            var camera = new ViewportCamera
            {
                Mode = CameraMode.ThirdPersonOrbital,
                EyeOffset = Vector3.Zero
            };

            // Target at (0, 0, 0), looking down at target with positive elevation (Pitch = +30 degrees, Yaw = 0) at distance 10
            camera.Update(Vector3.Zero, pitch: 30.0f, yaw: 0.0f, distance: 10.0f, aspectRatio: 16.0f / 9.0f);

            Assert.Equal(Vector3.Zero, camera.Target);
            // sin(30 deg) = 0.5 => eye.Y = 10 * 0.5 = 5.0f
            // cos(30 deg) = 0.866 => eye.Z = -10 * 0.866 = -8.66f
            Assert.InRange(camera.Position.X, -0.01f, 0.01f);
            Assert.InRange(camera.Position.Y, 4.99f, 5.01f);
            Assert.InRange(camera.Position.Z, -8.70f, -8.60f);
        }

        [Fact]
        public void ViewportCamera_FirstPerson_PositionsEyeAtTargetEyeLevel()
        {
            var camera = new ViewportCamera
            {
                Mode = CameraMode.FirstPerson,
                EyeOffset = new Vector3(0, 1.5f, 0)
            };

            var playerPos = new Vector3(10, 0, 20);
            camera.Update(playerPos, pitch: 10.0f, yaw: 90.0f, distance: 6.0f, aspectRatio: 16.0f / 9.0f);

            // Eye position should equal playerPos + eyeOffset exactly
            Assert.Equal(new Vector3(10, 1.5f, 20), camera.Position);
            // Target should be in front of the eye
            Assert.True(camera.Target.X > camera.Position.X); // Yaw 90 degrees points along +X
        }

        [Fact]
        public void ViewportCamera_FreeCam_MovesIndependentOfPlayer()
        {
            var camera = new ViewportCamera
            {
                Mode = CameraMode.FreeCam
            };

            camera.SetLookAt(new Vector3(0, 10, 0), new Vector3(0, 10, 10));

            // Move forward (Z +5)
            camera.MoveFreeCam(new Vector3(0, 0, 5.0f), pitchDelta: 0.0f, yawDelta: 0.0f);

            // Eye position should have translated forward
            Assert.True(camera.Position.Z > 0.0f);
        }

        [Fact]
        public void BoundingFrustum_IntersectsBox_CorrectlyIdentifiesVisibility()
        {
            var camera = new ViewportCamera();
            // Position camera at (0, 0, -10) looking toward origin (0, 0, 0)
            camera.SetLookAt(new Vector3(0, 0, -10), Vector3.Zero);

            var frustum = camera.Frustum;

            // Box directly at origin (inside view)
            var boxInsideMin = new Vector3(-1, -1, -1);
            var boxInsideMax = new Vector3(1, 1, 1);
            Assert.True(frustum.IntersectsBox(boxInsideMin, boxInsideMax));

            // Box far behind camera at (0, 0, -50)
            var boxBehindMin = new Vector3(-2, -2, -60);
            var boxBehindMax = new Vector3(2, 2, -50);
            Assert.False(frustum.IntersectsBox(boxBehindMin, boxBehindMax));

            // Box far to the right beyond horizontal field of view
            var boxFarRightMin = new Vector3(500, -2, 0);
            var boxFarRightMax = new Vector3(510, 2, 5);
            Assert.False(frustum.IntersectsBox(boxFarRightMin, boxFarRightMax));
        }

        [Fact]
        public void ZoneEnvironmentSettings_Presets_HaveValidRanges()
        {
            var day = ZoneEnvironmentSettings.CreateDay();
            var night = ZoneEnvironmentSettings.CreateNight();
            var dusk = ZoneEnvironmentSettings.CreateDusk();

            Assert.True(day.FogEnd > day.FogStart);
            Assert.True(night.FogEnd > night.FogStart);
            Assert.True(dusk.FogEnd > dusk.FogStart);

            Assert.InRange(day.SunDirection.Length(), 0.99f, 1.01f);
            Assert.InRange(night.SunDirection.Length(), 0.99f, 1.01f);
        }
    }
}
