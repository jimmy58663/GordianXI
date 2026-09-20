// tests/Gordian.App.Tests/Graphics/EntityRendererTests.cs
using System;
using System.Numerics;
using Gordian.Core.Graphics;
using Gordian.Core.World;
using Xunit;

namespace Gordian.App.Tests.Graphics
{
    public class EntityRendererTests
    {
        private static readonly Matrix4x4 EntityRotMatrix = new(
            1.0f,  0.0f,  0.0f, 0.0f,
            0.0f, -1.0f,  0.0f, 0.0f,
            0.0f,  0.0f, -1.0f, 0.0f,
            0.0f,  0.0f,  0.0f, 1.0f
        );

        [Fact]
        public void EntityTransformMatrix_HasPositiveDeterminant_NoReflection()
        {
            // Determinant must be +1.0 (proper rotation, not a reflection that flips handedness)
            float det = EntityRotMatrix.GetDeterminant();
            Assert.Equal(1.0f, det, 4);
        }

        [Fact]
        public void EntityTransformMatrix_MapsYDownModelToYUpWorld()
        {
            var entityPos = new Vector3(10f, 20f, 30f);
            var world = EntityRotMatrix * Matrix4x4.Identity * Matrix4x4.CreateTranslation(entityPos);

            // A point at (0, -1.5, 0) in FFXI model coordinates (1.5 yalms up from feet in Y-down)
            var modelHead = new Vector4(0f, -1.5f, 0f, 1.0f);
            var worldHead = Vector4.Transform(modelHead, world);

            // World Y should be 20 + 1.5 = 21.5
            Assert.Equal(10f, worldHead.X, 3);
            Assert.Equal(21.5f, worldHead.Y, 3);
            Assert.Equal(30f, worldHead.Z, 3);
        }

        [Fact]
        public void EntityFrustumCulling_AccuratelyCullsOffScreenEntities()
        {
            var camera = new ViewportCamera();
            camera.Update(
                targetPosition: Vector3.Zero,
                pitch: 0f,
                yaw: 0f,
                distance: 10f,
                aspectRatio: 16f / 9f);

            var frustum = camera.Frustum;

            // Entity right at target (0, 0, 0) should be visible
            var visibleMin = new Vector3(-1f, 0f, -1f);
            var visibleMax = new Vector3(1f, 2f, 1f);
            Assert.True(frustum.IntersectsBox(visibleMin, visibleMax));

            // Entity far behind camera (at Z = -200) should be culled
            var culledMin = new Vector3(-1f, 0f, -201f);
            var culledMax = new Vector3(1f, 2f, -199f);
            Assert.False(frustum.IntersectsBox(culledMin, culledMax));
        }

        [Fact]
        public void EntityHeading_RotatesAroundYAxis()
        {
            // Direction 64 = 90 degrees (quarter turn). Verified against live gamepad testing:
            // a -90 degree offset alone left the model's facing a constant 90 degrees off from
            // its actual direction of travel at every heading (confirmed by a player pushing each
            // of forward/back/left/right and reporting which way the model turned to face vs.
            // which way it actually moved); a -180 degree offset is what lines the two up.
            byte dir = 64;
            float headingRad = (dir / 256.0f) * MathF.PI * 2.0f;
            var rotY = Matrix4x4.CreateRotationY(headingRad - MathF.PI);

            var forward = new Vector4(0f, 0f, 1f, 0f);
            var turned = Vector4.Transform(forward, rotY);

            Assert.Equal(-1f, turned.X, 2);
            Assert.Equal(0f, turned.Y, 2);
            Assert.Equal(0f, turned.Z, 2);
        }

        [Fact]
        public void EntityHeading_RotationTracksHeadingDeltaConsistently()
        {
            // Regardless of the absolute phase offset, turning the heading by a given amount
            // must turn the rendered facing by that same amount, in a fixed, consistent
            // direction — otherwise facing would only line up with travel direction at some
            // headings and not others (the exact bug this offset fixes).
            float prevAngle = 0f;
            bool first = true;

            foreach (byte dir in new byte[] { 0, 32, 64, 96, 128, 160, 192, 224 })
            {
                float headingRad = (dir / 256.0f) * MathF.PI * 2.0f;
                var rotY = Matrix4x4.CreateRotationY(headingRad - MathF.PI);

                var forward = new Vector4(0f, 0f, 1f, 0f);
                var turned = Vector4.Transform(forward, rotY);
                float angle = MathF.Atan2(turned.X, turned.Z);

                if (!first)
                {
                    float delta = angle - prevAngle;
                    if (delta < -MathF.PI) delta += 2 * MathF.PI;
                    if (delta > MathF.PI) delta -= 2 * MathF.PI;

                    // Each step in the loop advances heading by 32/256 of a turn (45 degrees)
                    Assert.Equal(MathF.PI / 4.0f, delta, 3);
                }

                prevAngle = angle;
                first = false;
            }
        }
    }
}
