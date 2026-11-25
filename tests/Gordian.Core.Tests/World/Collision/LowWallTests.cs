using System;
using System.Collections.Generic;
using System.Numerics;
using Gordian.Core.World.Collision;
using Xunit;

namespace Gordian.Core.Tests.World.Collision
{
    /// <summary>
    /// #632: a riser lower than a step is not stepped up onto a face of 60 degrees or steeper (Southern San d'Oria's ramp
    /// strip), while curbs onto floors, stair risers and 60-degree bevels met directly are unchanged
    /// (<see cref="ZoneCollisionMesh.CrossesLowWall"/>). Internal space: -Y is up.
    /// </summary>
    public class LowWallTests
    {
        private const float StepUp = 0.5f, FootRadius = 0.9f, MaxDrop = 60.0f;

        /// <summary>How far the test meshes run along z (yalms): the strip's bevel then makes a 2.1 square-yalm plane, as in retail.</summary>
        private const float Length = 12.0f;

        /// <summary>A triangle facing up (-Y) whatever its winding.</summary>
        private static CollisionTriangle Up(Vector3 a, Vector3 b, Vector3 c)
        {
            var normal = Vector3.Normalize(Vector3.Cross(b - a, c - a));
            if (normal.Y > 0) normal = -normal;
            return new CollisionTriangle(a, b, c, normal, false, CollisionTerrain.Stone, false);
        }

        private static IEnumerable<CollisionTriangle> Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            yield return Up(a, b, c);
            yield return Up(a, c, d);
        }

        private static IEnumerable<CollisionTriangle> Riser(float x, float bottom, float top, float length = Length)
        {
            var normal = -Vector3.UnitX; // facing the floor it rises from
            yield return new CollisionTriangle(new(x, bottom, 0), new(x, top, 0), new(x, top, length), normal, false, CollisionTerrain.Stone, false);
            yield return new CollisionTriangle(new(x, bottom, 0), new(x, top, length), new(x, bottom, length), normal, false, CollisionTerrain.Stone, false);
        }

        /// <summary>A floor at height 0 for x 0-1, a 0.15 riser at x = 1, and an upper floor from x 1 to 3.</summary>
        private static ZoneCollisionMesh Curb(float upperFarHeight)
        {
            const float top = -0.15f;
            var triangles = new List<CollisionTriangle>();
            triangles.AddRange(Quad(new(0, 0, 0), new(1, 0, 0), new(1, 0, Length), new(0, 0, Length)));
            triangles.AddRange(Riser(1, 0, top));
            triangles.AddRange(Quad(new(1, top, 0), new(3, upperFarHeight, 0), new(3, upperFarHeight, Length), new(1, top, Length)));
            return new ZoneCollisionMesh(triangles);
        }

        /// <summary>
        /// The strip's east side north of y 4: a floor at height 0 for x 0-1, a riser of <paramref name="riser"/> at x = 1,
        /// a bevel of 60.01 degrees (retail's 60.002, normal Y -0.49997) rising 0.15, and the strip top beyond, rising with the ramp.
        /// Over <paramref name="length"/> 12 the bevel is a 2.1 square-yalm plane like the strip's; over 1 it is a 0.17 piece,
        /// like the chamfer on the base of a stair's end post.
        /// </summary>
        private static ZoneCollisionMesh Strip(float riser, float length = Length)
        {
            float bevelTop = -riser - 0.15f, bevelEnd = 1.0f + (0.15f / MathF.Tan(60.01f * MathF.PI / 180.0f));
            var triangles = new List<CollisionTriangle>();
            triangles.AddRange(Quad(new(0, 0, 0), new(1, 0, 0), new(1, 0, Length), new(0, 0, Length)));
            if (riser > 0) triangles.AddRange(Riser(1, 0, -riser, length));
            triangles.AddRange(Quad(new(1, -riser, 0), new(bevelEnd, bevelTop, 0), new(bevelEnd, bevelTop, length), new(1, -riser, length)));
            triangles.AddRange(Quad(new(bevelEnd, bevelTop, 0), new(3, bevelTop - 0.2f, 0), new(3, bevelTop - 0.2f, length), new(bevelEnd, bevelTop, length))); // rising on, like the ramp: not a level tread
            return new ZoneCollisionMesh(triangles);
        }

        [Fact]
        public void ARiser_UnderASixtyDegreeBevel_StopsTheFeet()
        {
            var mesh = Strip(riser: 0.08f);
            Assert.True(mesh.CrossesLowWall(new Vector3(0.95f, 0, 1), new Vector3(1.05f, 0, 1), StepUp, FootRadius, MaxDrop, out var normal, out float distance));
            Assert.Equal(-1.0f, normal.X, 3);
            Assert.Equal(0.05f, distance, 3);
        }

        [Fact]
        public void ARiser_UnderASmallSteepPiece_IsSteppedUp()
        {
            // Round 2 (2026-11-22): the 63-degree chamfers on the base of the end post of Southern San d'Oria's small stairs
            // are 0.1-0.8 square-yalm pieces, not a built bevel; retail walks onto them.
            var mesh = Strip(riser: 0.08f, length: 1.0f);
            Assert.True(ZoneCollisionMesh.MinBevelPlaneArea > mesh.GetPlaneArea(4));
            Assert.False(mesh.CrossesLowWall(new Vector3(0.95f, 0, 0.5f), new Vector3(1.05f, 0, 0.5f), StepUp, FootRadius, MaxDrop, out _, out _));
        }

        [Fact]
        public void ARiser_UnderASlopedFloor_IsSteppedUp()
        {
            // Southern San d'Oria's 0.1-yalm curbs from the paving onto the 8.6-degree gutters: retail steps up.
            Assert.False(Curb(upperFarHeight: -0.45f).CrossesLowWall(new Vector3(0.95f, 0, 1), new Vector3(1.05f, 0, 1), StepUp, FootRadius, MaxDrop, out _, out _));
        }

        [Fact]
        public void ARiser_UnderALevelTread_IsSteppedUp()
        {
            Assert.False(Curb(upperFarHeight: -0.15f).CrossesLowWall(new Vector3(0.95f, 0, 1), new Vector3(1.05f, 0, 1), StepUp, FootRadius, MaxDrop, out _, out _));
        }

        [Fact]
        public void ASixtyDegreeBevel_WithNoRiser_IsGround()
        {
            // The strip's bevel met from the ramp: retail walks over it, so it is ground, not a riser.
            var mesh = Strip(riser: 0.0f);
            Assert.False(mesh.CrossesLowWall(new Vector3(0.95f, 0, 1), new Vector3(1.05f, 0, 1), StepUp, FootRadius, MaxDrop, out _, out _));
            Assert.True(mesh.TryGetGround(new Vector3(1.04f, 0, 1), StepUp, MaxDrop, out var hit));
            Assert.True(MathF.Abs(hit.Normal.Y) < ZoneCollisionMesh.MaxWallNormalY);
        }

        [Fact]
        public void TheRiser_IsTestedWhateverTheMoveLength()
        {
            var mesh = Strip(riser: 0.08f);
            foreach (float length in new[] { 0.02f, 0.083f, 0.17f, 0.5f, 1.5f })
            {
                var start = new Vector3(1.0f - (length / 2), 0, 1);
                Assert.True(mesh.CrossesLowWall(start, start + new Vector3(length, 0, 0), StepUp, FootRadius, MaxDrop, out _, out _), $"move of {length}");
            }
        }

        [Fact]
        public void MovingAwayFromTheFace_OrAlongIt_IsFree()
        {
            var mesh = Strip(riser: 0.08f);
            Assert.False(mesh.CrossesLowWall(new Vector3(0.95f, 0, 1), new Vector3(0.5f, 0, 1), StepUp, FootRadius, MaxDrop, out _, out _));
            Assert.False(mesh.CrossesLowWall(new Vector3(0.95f, 0, 0.5f), new Vector3(0.95f, 0, 1.5f), StepUp, FootRadius, MaxDrop, out _, out _));
        }

        [Fact]
        public void SteppingDownOffTheStrip_IsFree()
        {
            var mesh = Strip(riser: 0.08f);
            Assert.False(mesh.CrossesLowWall(new Vector3(1.5f, -0.23f, 1), new Vector3(0.9f, -0.23f, 1), StepUp, FootRadius, MaxDrop, out _, out _));
        }
    }
}
