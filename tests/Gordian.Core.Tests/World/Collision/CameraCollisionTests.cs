// tests/Gordian.Core.Tests/World/Collision/CameraCollisionTests.cs
using System.Numerics;
using Gordian.Core.Graphics;
using Gordian.Core.Resources.Graphics;
using Gordian.Core.World.Collision;
using Xunit;

namespace Gordian.Core.Tests.World.Collision
{
    public class CameraCollisionTests
    {
        // A 20 x 20 wall in the internal plane X = -5 (display X = +5), heights -10..10.
        private static ZoneCollisionMesh WallAtX5(bool cameraTransparent = false)
        {
            var a = new Vector3(-5, -10, -10);
            var b = new Vector3(-5, 10, -10);
            var c = new Vector3(-5, -10, 10);
            var d = new Vector3(-5, 10, 10);
            var n = new Vector3(1, 0, 0);
            return new ZoneCollisionMesh(new[]
            {
                new CollisionTriangle(a, b, c, n, true, default, cameraTransparent),
                new CollisionTriangle(b, d, c, n, true, default, cameraTransparent),
            });
        }

        [Fact]
        public void Raycast_FindsTheWallFraction_AndSkipsCameraTransparent()
        {
            var wall = WallAtX5();
            Assert.True(wall.TryRaycast(new Vector3(0, 0, 0), new Vector3(-10, 0, 0), true, out float f));
            Assert.Equal(0.5f, f, 3);
            Assert.False(wall.TryRaycast(new Vector3(0, 0, 0), new Vector3(-4, 0, 0), true, out _));
            Assert.False(WallAtX5(cameraTransparent: true).TryRaycast(new Vector3(0, 0, 0), new Vector3(-10, 0, 0), true, out _));
        }

        [Fact]
        public void OrbitalCamera_PullsInFrontOfWall_ThenEasesBackOut()
        {
            var camera = new ViewportCamera { EyeOffset = Vector3.Zero, Collision = WallAtX5() };
            // Yaw 0 places the orbit toward display +X (internal -X), straight at the wall 5 yalms away.
            camera.Update(Vector3.Zero, 0f, 0f, 10f, 1f, 0.016f);
            float first = Vector3.Distance(camera.Position, camera.Target);
            Assert.InRange(first, 4.5f, 4.8f); // wall at 5, minus the 0.3 margin

            // Wall gone: the camera eases back (10 yalms/s) instead of jumping to 10.
            camera.Collision = null;
            camera.Update(Vector3.Zero, 0f, 0f, 10f, 1f, 0.016f);
            Assert.Equal(10f, Vector3.Distance(camera.Position, camera.Target), 3); // no mesh: unrestricted

            camera.Collision = new ZoneCollisionMesh(System.Array.Empty<CollisionTriangle>());
            camera.Update(Vector3.Zero, 0f, 0f, 10f, 1f, 0.016f);
            Assert.Equal(10f, Vector3.Distance(camera.Position, camera.Target), 3);

            camera.Collision = WallAtX5();
            camera.Update(Vector3.Zero, 0f, 0f, 10f, 1f, 0.016f);
            camera.Collision = new ZoneCollisionMesh(new[] { new CollisionTriangle(new Vector3(50, 0, 50), new Vector3(51, 0, 50), new Vector3(50, 0, 51), -Vector3.UnitY, false, default, false) });
            camera.Update(Vector3.Zero, 0f, 0f, 10f, 1f, 0.1f);
            float eased = Vector3.Distance(camera.Position, camera.Target);
            Assert.InRange(eased, 5.5f, 6.0f); // 4.7 + 10 * 0.1
        }

        [Fact]
        public void MirroredPlacement_ReversesTriangleWinding()
        {
            Assert.True(ZoneDefDecoder.IsMirrored(Matrix4x4.CreateScale(-1, 1, 1)));
            Assert.False(ZoneDefDecoder.IsMirrored(Matrix4x4.CreateScale(-1, -1, 1)));
            Assert.False(ZoneDefDecoder.IsMirrored(Matrix4x4.CreateRotationY(1.0f)));
        }
    }
}
