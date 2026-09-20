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
            // Forward is (-cosY * cosP, sinP, sinY * cosP): Yaw follows the FFXI world-heading
            // convention (0=East/+X, 90=South/+Z), negated on X because the renderer displays
            // entities at a mirrored X coordinate (see EntityRenderer: pos = (-x, -y, z)).
            // yaw = 0 => forward = (-1, 0, 0), eye = target - forward * dist = (10, 0, 0)
            Assert.InRange(camera.Position.X, 9.99f, 10.01f);
            Assert.InRange(camera.Position.Y, -0.01f, 0.01f);
            Assert.InRange(camera.Position.Z, -0.01f, 0.01f);
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
            // cos(30 deg) = 0.866 => eye.X = 10 * 0.866 = 8.66f (yaw = 0 => mirrored -X forward)
            Assert.InRange(camera.Position.X, 8.60f, 8.70f);
            Assert.InRange(camera.Position.Y, 4.99f, 5.01f);
            Assert.InRange(camera.Position.Z, -0.01f, 0.01f);
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
            Assert.True(camera.Target.Z > camera.Position.Z); // Yaw 90 degrees points along +Z (South)
        }

        [Fact]
        public void ViewportCamera_FreeCam_MovesIndependentOfPlayer()
        {
            var camera = new ViewportCamera
            {
                Mode = CameraMode.FreeCam
            };

            camera.SetLookAt(new Vector3(0, 10, 0), new Vector3(0, 10, 10));

            // Move forward; at Yaw = 0 (default), forward is mirrored East, i.e. -X
            camera.MoveFreeCam(new Vector3(0, 0, 5.0f), pitchDelta: 0.0f, yawDelta: 0.0f);

            // Eye position should have translated forward
            Assert.True(camera.Position.X < 0.0f);
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

        [Fact]
        public void ViewportCamera_ThirdPersonOrbital_ClampsPitchFloorToNegative15Degrees()
        {
            var camera = new ViewportCamera
            {
                Mode = CameraMode.ThirdPersonOrbital
            };

            camera.Pitch = -60.0f;
            Assert.Equal(-15.0f, camera.Pitch);

            camera.Update(new Vector3(0, 10, 0), pitch: -45.0f, yaw: 0.0f, distance: 10.0f, aspectRatio: 16f / 9f);
            Assert.Equal(-15.0f, camera.Pitch);
        }

        [Fact]
        public void ViewportCamera_ThirdPersonOrbital_EnforcesGroundFloorSafeguard()
        {
            var camera = new ViewportCamera
            {
                Mode = CameraMode.ThirdPersonOrbital,
                EyeOffset = new Vector3(0, 1.3f, 0)
            };

            // Player standing at Y = 5.0f. Ground is at Y = 5.0f.
            // With negative pitch (-15 deg) and maximum distance (30 yalms),
            // camera Y would naturally sink into the ground without safeguard.
            camera.Update(new Vector3(0, 5.0f, 0), pitch: -15.0f, yaw: 0.0f, distance: 30.0f, aspectRatio: 16f / 9f);

            // Position.Y must never drop below targetPosition.Y + 0.25f (5.25f)
            Assert.True(camera.Position.Y >= 5.25f, $"Camera Position.Y {camera.Position.Y} was below ground floor 5.25f");
        }
    }
}
