using Gordian.Core.Input;
using System.Numerics;
using Gordian.Core.Resources;
using Gordian.Core.Resources.Models;
using Gordian.Core.World.Collision;
using Xunit.Abstractions;

namespace Gordian.Core.Tests.World.Collision
{
    /// <summary>
    /// Checks the decoded collision soup against a retail zone, when a local FFXI install is present.
    /// </summary>
    public class ZoneCollisionRealDataTests
    {
        private const string GameDirectory = @"G:\Program Files (x86)\PlayOnline\SquareEnix\FINAL FANTASY XI";
        private readonly ITestOutputHelper _output;

        public ZoneCollisionRealDataTests(ITestOutputHelper output) => _output = output;

        [Theory]
        [InlineData(4)]   // Bibiki Bay
        [InlineData(230)] // Southern San d'Oria
        public void CollisionFloors_LineUpWithVisibleTerrain(int zoneId)
        {
            if (!Directory.Exists(GameDirectory)) return;
            var rm = new ResourceManager(GameDirectory);
            rm.InitializeFileTable();
            if (!rm.TryLoadZone(zoneId, out var zone, out _) || zone == null) return;

            var collision = zone.Collision;
            Assert.NotNull(collision);
            int walls = 0;
            foreach (var t in collision!.Triangles) if (t.IsWall) walls++;
            _output.WriteLine($"Zone {zoneId}: {collision.TriangleCount} collision tris ({walls} walls), bounds {collision.MinBounds} .. {collision.MaxBounds}");

            // Sample the top collision floor at points spread over the collision bounds and compare with the top visible
            // surface there (the render mesh is in display space: (-X, -Y, Z)).
            var random = new Random(1234);
            var differences = new List<float>();
            for (int i = 0; i < 4000 && differences.Count < 300; i++)
            {
                float x = Lerp(collision.MinBounds.X, collision.MaxBounds.X, random.NextSingle());
                float z = Lerp(collision.MinBounds.Z, collision.MaxBounds.Z, random.NextSingle());
                if (!collision.TryGetGround(new Vector3(x, -5000.0f, z), 0.0f, 10000.0f, out var ground)) continue;
                if (!TryVisibleTop(zone, x, z, out float visible)) continue;
                differences.Add(MathF.Abs(visible - ground.Height));
            }

            differences.Sort();
            Assert.True(differences.Count >= 50, $"only {differences.Count} comparable samples");
            float median = differences[differences.Count / 2];
            int close = differences.Count(d => d < 1.0f);
            _output.WriteLine($"Zone {zoneId}: {differences.Count} samples, median |visible - collision| = {median:F3}, within 1 yalm: {close}");
            Assert.True(median < 0.5f, $"median height difference {median}");
        }

        /// <summary>
        /// Heights Windower reported while running up Southern San d'Oria's stairs toward -X at Z = -37.2 (internal
        /// space: X, height, retail tick samples from a video capture, 2026-09-25).
        /// </summary>
        private static readonly (float X, float Height)[] RetailStairClimb =
        {
            (26.811f, 2.000f), (26.301f, 1.832f), (26.131f, 1.750f), (25.961f, 1.582f), (25.791f, 1.500f),
            (25.616f, 1.500f), (25.446f, 1.250f), (25.107f, 1.250f), (24.937f, 1.000f), (24.597f, 1.000f),
            (24.257f, 0.750f), (23.917f, 0.582f), (23.577f, 0.500f), (23.407f, 0.332f), (23.232f, 0.250f),
            (22.892f, 0.082f), (22.722f, 0.000f), (22.553f, 0.000f), (22.208f, -0.250f), (21.868f, -0.418f),
            (21.698f, -0.500f), (21.358f, -0.668f), (21.019f, -0.750f), (20.679f, -1.000f), (20.164f, -1.250f),
        };

        [Fact]
        public void SteppedGround_ClimbsSouthernSandoriaStairsLikeRetail()
        {
            if (!Directory.Exists(GameDirectory)) return;
            var rm = new ResourceManager(GameDirectory);
            rm.InitializeFileTable();
            var collision = rm.TryLoadZoneCollision(230);
            if (collision == null) return;

            // Walk the stairs in small steps the way the locomotion controller does, carrying the height along.
            var feet = new Vector3(27.2f, 2.0f, -37.2f);
            float totalError = 0.0f, snapError = 0.0f;
            int next = 0;
            while (next < RetailStairClimb.Length)
            {
                // Walls on, as in play: the stairwell's railings and side walls must not get in the way.
                var moved = collision.ResolveWalls(feet, feet with { X = feet.X - 0.01f }, PlayerLocomotionController.BodyRadius, PlayerLocomotionController.StepUpHeight, PlayerLocomotionController.BodyHeight);
                Assert.True(MathF.Abs(moved.Z - feet.Z) < 0.05f && moved.X < feet.X, $"blocked at {feet}");
                feet = moved;
                Assert.True(collision.TryGetSteppedGround(feet, PlayerLocomotionController.StepUpHeight, 60.0f, PlayerLocomotionController.FootRadius, out var ground));
                feet.Y = ground.Height;
                if (feet.X > RetailStairClimb[next].X) continue;

                collision.TryGetGround(feet with { Y = feet.Y - 0.5f }, 0.75f, 60.0f, out var snapped);
                totalError += MathF.Abs(feet.Y - RetailStairClimb[next].Height);
                snapError += MathF.Abs(snapped.Height - RetailStairClimb[next].Height);
                next++;
            }

            float meanError = totalError / RetailStairClimb.Length;
            _output.WriteLine($"mean |retail - stepped| = {meanError:F3}, mean |retail - snapped| = {snapError / RetailStairClimb.Length:F3}");
            Assert.True(meanError < 0.06f, $"mean stair height error {meanError}");
        }

        [Theory]
        [InlineData(230)] // Southern San d'Oria
        [InlineData(4)]   // Bibiki Bay
        public void Walls_StopAPlayerRunningStraightAtThem(int zoneId)
        {
            if (!Directory.Exists(GameDirectory)) return;
            var rm = new ResourceManager(GameDirectory);
            rm.InitializeFileTable();
            var collision = rm.TryLoadZoneCollision(zoneId);
            if (collision == null) return;

            // Pick tall walls standing on a floor, start 2 yalms in front of each and run 3 yalms straight at it.
            var random = new Random(99);
            var triangles = collision.Triangles.ToArray();
            int tried = 0, crossed = 0;
            for (int i = 0; i < 200000 && tried < 300; i++)
            {
                var wall = triangles[random.Next(triangles.Length)];
                if (MathF.Abs(wall.Normal.Y) > 0.05f) continue;
                var center = (wall.A + wall.B + wall.C) / 3.0f;
                var facing = Vector3.Normalize(new Vector3(wall.Normal.X, 0, wall.Normal.Z));
                var start = center + (facing * 2.0f);
                if (!collision.TryGetNearestGround(start.X, start.Z, center.Y, out var floor)) continue;
                start.Y = floor.Height;

                // The wall must cover the body there: from a step above the feet to above the head (internal -Y up).
                float top = MathF.Min(wall.A.Y, MathF.Min(wall.B.Y, wall.C.Y));
                float bottom = MathF.Max(wall.A.Y, MathF.Max(wall.B.Y, wall.C.Y));
                if (bottom < start.Y - 0.8f || top > start.Y - 1.7f) continue;
                if (Vector3.Dot(start - wall.A, wall.Normal) <= 0.0f) continue;

                // The body's path must actually meet this triangle (at the height of the wall-test spheres).
                var chest = start with { Y = start.Y - 1.15f };
                float hit = DownHit(chest + new Vector3(0, 0, 0), wall.A, wall.B, wall.C, -facing);
                if (hit < 0.0f || hit > 2.5f) continue;

                tried++;
                // Sliding off the edge of a narrow face (a post, a wall end) is fine; passing through the face is not.
                var position = start;
                bool passedThrough = false;
                for (int step = 0; step < 30 && !passedThrough; step++)
                {
                    var next = collision.ResolveWalls(position, position - (facing * 0.1f), PlayerLocomotionController.BodyRadius, PlayerLocomotionController.StepUpHeight, PlayerLocomotionController.BodyHeight);
                    var from = position with { Y = position.Y - 1.15f };
                    var move = (next with { Y = next.Y - 1.15f }) - from;
                    float length = move.Length();
                    if (length > 1e-5f)
                    {
                        float t = DownHit(from, wall.A, wall.B, wall.C, move / length);
                        passedThrough = t >= 0.0f && t <= length;
                    }
                    position = next;
                }
                if (passedThrough)
                {
                    crossed++;
                    _output.WriteLine($"  passed through wall {wall.A} {wall.B} {wall.C} n={wall.Normal} from {start}");
                }
            }

            _output.WriteLine($"Zone {zoneId}: {tried} walls tried, {crossed} crossed");
            Assert.True(tried >= 50, $"only {tried} usable walls");
            Assert.Equal(0, crossed);
        }

        private static float Lerp(float a, float b, float t) => a + ((b - a) * t);

        private static bool TryVisibleTop(ZoneGeometry zone, float x, float z, out float internalHeight)
        {
            internalHeight = 0.0f;
            var origin = new Vector3(-x, 5000.0f, z);
            float best = float.MaxValue;
            foreach (var group in zone.MeshGroups)
            {
                if (group.IsWater || group.IsBlend || group.IsFoliage) continue;
                if (origin.X < group.MinBounds.X || origin.X > group.MaxBounds.X ||
                    origin.Z < group.MinBounds.Z || origin.Z > group.MaxBounds.Z) continue;
                var v = group.Vertices;
                var idx = group.Indices;
                for (int i = 0; i + 2 < idx.Length; i += 3)
                {
                    float t = DownHit(origin, v[idx[i]].Position, v[idx[i + 1]].Position, v[idx[i + 2]].Position);
                    if (t >= 0.0f && t < best) best = t;
                }
            }
            if (best == float.MaxValue) return false;
            internalHeight = -(origin.Y - best);
            return true;
        }

        private static float DownHit(Vector3 origin, Vector3 a, Vector3 b, Vector3 c) => DownHit(origin, a, b, c, -Vector3.UnitY);

        private static float DownHit(Vector3 origin, Vector3 a, Vector3 b, Vector3 c, Vector3 direction)
        {
            Vector3 e1 = b - a, e2 = c - a;
            Vector3 p = Vector3.Cross(direction, e2);
            float det = Vector3.Dot(e1, p);
            if (MathF.Abs(det) < 1e-8f) return -1.0f;
            float inv = 1.0f / det;
            Vector3 s = origin - a;
            float u = Vector3.Dot(s, p) * inv;
            if (u < 0.0f || u > 1.0f) return -1.0f;
            Vector3 q = Vector3.Cross(s, e1);
            float w = Vector3.Dot(direction, q) * inv;
            if (w < 0.0f || u + w > 1.0f) return -1.0f;
            return Vector3.Dot(e2, q) * inv;
        }
    }
}
