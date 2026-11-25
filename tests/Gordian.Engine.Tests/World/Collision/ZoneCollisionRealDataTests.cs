using Gordian.Engine.Input;
using System.Numerics;
using Gordian.Core.Resources;
using Gordian.Core.Resources.Models;
using Gordian.Core.World.Collision;

namespace Gordian.Engine.Tests.World.Collision
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
                // The risers are short vertical faces (#632), but each is topped by a level tread, a floor.
                Assert.False(collision.CrossesLowWall(feet, moved, PlayerLocomotionController.StepUpHeight, PlayerLocomotionController.FootRadius, 60.0f, out _, out _), $"riser blocks at {feet}");
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

        /// <summary>
        /// #632: Southern San d'Oria's ramp strip (zone 230, the 1-yalm strip at x 7.09-7.91, 0.15 above the ramp along its
        /// east edge). Walking west from the gutter east of it, retail climbs onto the strip at (8.216, 1.858, 3.863), where
        /// its side bevel is 59.9 degrees and meets the gutter directly, and is blocked from y = 4.000 on, where a short
        /// vertical riser at x = 8 carries a 60.0-degree bevel; at the strip's north end retail walks round it at y = 27.878
        /// and is blocked where the strip starts at y = 28. The rule must hold at any locomotion tick rate.
        /// Server (x, height, y) = internal (X, Y, Z).
        /// </summary>
        [Theory]
        [InlineData(3.863f, 1.9f, true, 60)]
        [InlineData(3.863f, 1.9f, true, 30)]
        [InlineData(3.863f, 1.9f, true, 20)]
        [InlineData(4.1f, 1.9f, false, 60)]
        [InlineData(4.1f, 1.9f, false, 30)]
        [InlineData(4.1f, 1.9f, false, 20)]
        [InlineData(4.5f, 1.9f, false, 30)]
        [InlineData(27.878f, 0.0f, true, 60)]
        [InlineData(27.878f, 0.0f, true, 20)]
        [InlineData(28.1f, 0.0f, false, 60)]
        [InlineData(28.1f, 0.0f, false, 30)]
        [InlineData(28.1f, 0.0f, false, 20)]
        public void LowWalls_SanDoriaRampStrip_StopsWhereRetailDoes(float z, float height, bool crosses, int hertz)
        {
            if (!Directory.Exists(GameDirectory)) return;
            var rm = new ResourceManager(GameDirectory);
            rm.InitializeFileTable();
            var collision = rm.TryLoadZoneCollision(230);
            if (collision == null) return;

            var start = new Vector3(8.4f, height, z);
            Assert.True(collision.TryGetGround(start with { Y = start.Y - 1.0f }, 0.0f, 2.0f, out var ground));
            var world = new Gordian.Core.World.WorldState { Collision = collision };
            var player = new Gordian.Core.World.LocalPlayerState { ServerId = 1 };
            var me = new Gordian.Core.World.PlayerEntity(1, 1) { Position = start with { Y = ground.Height }, Direction = 128, IsSpawned = true };
            world.UpsertEntity(me);
            var input = new InputState();
            var controller = new PlayerLocomotionController(input, InputProfile.CreateCompact(), world, player) { CameraYaw = 180.0f };
            input.SetKeyDown(GordianKey.W); // camera yaw 180: runs toward -X
            // Blocked means never onto the strip while level with its 60-degree side (from y 4) or its end (from y 28);
            // sliding south along the face and climbing on south of that is retail's boundary, not a crossing.
            float boundary = z < 10.0f ? 4.0f : 28.0f;
            bool onStripPastBoundary = false;
            for (int i = 0; i < hertz; i++)
            {
                controller.Update(TimeSpan.FromSeconds(1.0 / hertz));
                onStripPastBoundary |= me.Position.X < 7.95f && me.Position.Z >= boundary;
            }

            _output.WriteLine($"z {z} at {hertz} Hz: from x 8.4 (height {ground.Height:F3}) to {me.Position}");
            if (crosses) Assert.True(me.Position.X < 7.5f, $"stopped at {me.Position}");
            else Assert.False(onStripPastBoundary, $"crossed the face, now at {me.Position}");
        }

        /// <summary>
        /// #632 round 1 (2026-11-22): the first short-wall rule blocked places retail walks freely. Each walk holds forward
        /// for 1.5 s at 60 Hz from a server (x, height, y) position with the given camera yaw (0 = +X, 180 = -X, 270 = +Z)
        /// and must end at least <paramref name="minDistance"/> yalms away; a non-zero <paramref name="minDrop"/> or
        /// <paramref name="minRise"/> also requires the walk to end that far below or above its start.
        /// </summary>
        [Theory]
        [InlineData(234, -159.69f, -2.69f, -28.52f, 0.0f, 3.0f, 2.0f, 0.0f)]   // Bastok Mines: walk off the ledge down a 64-79 degree bank
        [InlineData(230, 8.6f, 2.1f, 5.0f, 180.0f, 0.5f, 0.0f, 0.0f)]          // Southern San d'Oria: 0.1 curb from the paving onto the gutter
        [InlineData(230, -40.1f, 2.09f, -3.3f, 135.0f, 4.0f, 0.0f, 0.0f)]      // the same at the west ramp
        [InlineData(230, 6.5f, 1.0f, 10.0f, 0.0f, 5.0f, 1.0f, 0.0f)]           // from the ramp east over the strip, down into the gutter
        [InlineData(231, -170.11f, 4.0f, 216.95f, 270.0f, 5.0f, 0.0f, 3.5f)]   // Northern San d'Oria: back up the stairs at (-170.11, 4, 216.95)
        public void LowWalls_RoundOneSpots_WalkAsRetail(int zone, float x, float height, float y, float yaw, float minDistance, float minDrop, float minRise)
        {
            if (!Directory.Exists(GameDirectory)) return;
            var rm = new ResourceManager(GameDirectory);
            rm.InitializeFileTable();
            var collision = rm.TryLoadZoneCollision(zone);
            if (collision == null) return;

            var start = new Vector3(x, height, y);
            Assert.True(collision.TryGetGround(start with { Y = start.Y - 1.0f }, 0.0f, 2.0f, out var ground));
            start.Y = ground.Height;
            var world = new Gordian.Core.World.WorldState { Collision = collision };
            var player = new Gordian.Core.World.LocalPlayerState { ServerId = 1 };
            var me = new Gordian.Core.World.PlayerEntity(1, 1) { Position = start, Direction = (byte)MathF.Round(yaw / 360.0f * 256.0f), IsSpawned = true };
            world.UpsertEntity(me);
            var input = new InputState();
            var controller = new PlayerLocomotionController(input, InputProfile.CreateCompact(), world, player) { CameraYaw = yaw };
            input.SetKeyDown(GordianKey.W);
            for (int i = 0; i < 90; i++) controller.Update(TimeSpan.FromSeconds(1.0 / 60));

            var end = me.Position;
            _output.WriteLine($"zone {zone} from {start} yaw {yaw}: to {end}");
            Assert.True(new Vector2(end.X - start.X, end.Z - start.Z).Length() >= minDistance, $"stopped at {end}");
            if (minDrop > 0) Assert.True(end.Y - start.Y >= minDrop, $"did not drop: {end}"); // internal -Y is up
            if (minRise > 0) Assert.True(start.Y - end.Y >= minRise, $"did not climb: {end}");
        }

        /// <summary>Starts a 60 Hz walk with W held from the floor under a server (x, height, y) position.</summary>
        private static (PlayerLocomotionController Controller, Gordian.Core.World.PlayerEntity Me) StartWalk(ZoneCollisionMesh collision, float x, float height, float y, float yaw)
        {
            var start = new Vector3(x, height, y);
            Assert.True(collision.TryGetGround(start with { Y = start.Y - 1.0f }, 0.0f, 2.0f, out var ground));
            var world = new Gordian.Core.World.WorldState { Collision = collision };
            var player = new Gordian.Core.World.LocalPlayerState { ServerId = 1 };
            var me = new Gordian.Core.World.PlayerEntity(1, 1) { Position = start with { Y = ground.Height }, Direction = (byte)MathF.Round(yaw / 360.0f * 256.0f), IsSpawned = true };
            world.UpsertEntity(me);
            var input = new InputState();
            var controller = new PlayerLocomotionController(input, InputProfile.CreateCompact(), world, player) { CameraYaw = yaw };
            input.SetKeyDown(GordianKey.W);
            return (controller, me);
        }

        /// <summary>
        /// #632 round 2 (2026-11-22): going up Northern San d'Oria's stairs at (-168.88, 1.5, 198.06), heading 84 degrees,
        /// the player jumped up every other step. The steps are exactly 0.5 (the step-up height) at heights such as 1.499999
        /// and 0.999999, and float rounding left the next tread just out of reach (<see cref="ZoneCollisionMesh.StepTolerance"/>).
        /// Every step must now ease up over the foot rounding: no tick rises more than 0.15.
        /// </summary>
        [Fact]
        public void Ground_NorthernSanDoriaStairs_EaseUpEveryStep()
        {
            if (!Directory.Exists(GameDirectory)) return;
            var rm = new ResourceManager(GameDirectory);
            rm.InitializeFileTable();
            var collision = rm.TryLoadZoneCollision(231);
            if (collision == null) return;

            var (controller, me) = StartWalk(collision, -169.0f, 2.0f, 199.6f, 84.4f);
            controller.Update(TimeSpan.FromSeconds(1.0 / 60)); // settles on the first tread
            float startHeight = me.Position.Y, biggest = 0.0f;
            for (int i = 0; i < 48; i++)
            {
                float before = me.Position.Y;
                controller.Update(TimeSpan.FromSeconds(1.0 / 60));
                biggest = MathF.Max(biggest, before - me.Position.Y); // internal -Y is up
            }

            _output.WriteLine($"from {startHeight:F3} to {me.Position.Y:F3}; biggest rise in a tick {biggest:F3}");
            Assert.True(startHeight - me.Position.Y >= 1.4f, $"did not climb: {me.Position}");
            Assert.True(biggest <= 0.15f, $"jumped {biggest:F3} in one tick");
        }

        /// <summary>
        /// #632 round 2: Southern San d'Oria's small stairs at (-114.7, -3.0, 33.0), whose end posts stand on a stone base
        /// with 63-degree chamfers (0.1 and 0.8 square-yalm pieces): retail walks onto them from any side, so no move toward
        /// the spot meets a short wall. A crate in Bastok Mines (88.1, 6.63, -33.3), a 2-yalm wall over a 64-degree bank,
        /// still stops moves into it (retail cannot get on it).
        /// </summary>
        [Theory]
        [InlineData(230, -114.7f, -3.0f, 33.0f, false)]
        [InlineData(234, 88.1f, 6.63f, -33.3f, true)]
        public void LowWalls_RoundTwoSpots_BlockAsRetail(int zone, float x, float height, float y, bool blocks)
        {
            if (!Directory.Exists(GameDirectory)) return;
            var rm = new ResourceManager(GameDirectory);
            rm.InitializeFileTable();
            var collision = rm.TryLoadZoneCollision(zone);
            if (collision == null) return;

            var spot = new Vector3(x, height, y);
            Assert.True(collision.TryGetGround(spot with { Y = spot.Y - 1.0f }, 0.0f, 3.0f, out var ground));
            spot.Y = ground.Height;
            int blocked = 0;
            for (int i = 0; i < 32; i++)
            {
                float angle = i * MathF.PI / 16.0f;
                var direction = new Vector3(MathF.Cos(angle), 0, MathF.Sin(angle));
                var origin = spot - (direction * 1.5f);
                if (!collision.TryGetGround(origin with { Y = origin.Y - 3.0f }, 0.0f, 6.0f, out var start)) continue;
                origin.Y = start.Height;
                if (collision.CrossesLowWall(origin, origin + (direction * 3.0f), PlayerLocomotionController.StepUpHeight, PlayerLocomotionController.FootRadius, 60.0f, out var normal, out float distance))
                {
                    blocked++;
                    _output.WriteLine($"toward {angle * 180 / MathF.PI:F0} deg: riser n={normal} after {distance:F3}");
                }
            }
            if (blocks) Assert.True(blocked > 0);
            else Assert.Equal(0, blocked);
        }

        /// <summary>
        /// #632 round 2: the basket of apples in Bastok Markets at (-222.8, -6.62, -94.3) stands 0.57 above the street, more
        /// than a step; retail cannot walk onto it, and no walk at it from the street ends up on it.
        /// </summary>
        [Fact]
        public void Ground_BastokMarketsAppleBasket_IsNotClimbed()
        {
            if (!Directory.Exists(GameDirectory)) return;
            var rm = new ResourceManager(GameDirectory);
            rm.InitializeFileTable();
            var collision = rm.TryLoadZoneCollision(235);
            if (collision == null) return;

            for (int i = 0; i < 16; i++)
            {
                float angle = i * MathF.PI / 8.0f;
                float x = -222.8f + (MathF.Cos(angle) * 2.5f), y = -94.3f + (MathF.Sin(angle) * 2.5f);
                if (!collision.TryGetGround(new Vector3(x, -9.0f, y), 0.0f, 6.0f, out var street) || MathF.Abs(street.Height + 6.0f) > 0.1f) continue;
                float yaw = MathF.Atan2(MathF.Sin(angle), -MathF.Cos(angle)) * 180.0f / MathF.PI; // toward the basket
                var (controller, me) = StartWalk(collision, x, -6.0f, y, yaw < 0 ? yaw + 360.0f : yaw);
                for (int k = 0; k < 120; k++)
                {
                    controller.Update(TimeSpan.FromSeconds(1.0 / 60));
                    Assert.True(me.Position.Y > -6.3f, $"from angle {angle * 180 / MathF.PI:F0}: on the basket at {me.Position}");
                }
            }
        }

        /// <summary>
        /// #632 round 1: stair risers in Northern San d'Oria the maintainer could walk down but not back up. Each riser is
        /// topped by a floor, so no direction from these spots meets a short wall.
        /// </summary>
        [Theory]
        [InlineData(-133.86f, 6.0f, 144.49f)]
        [InlineData(-140.03f, 12.0f, 186.60f)]
        [InlineData(-170.11f, 4.0f, 216.95f)]
        public void LowWalls_NorthernSanDoriaStairs_AreNotShortWalls(float x, float height, float y)
        {
            if (!Directory.Exists(GameDirectory)) return;
            var rm = new ResourceManager(GameDirectory);
            rm.InitializeFileTable();
            var collision = rm.TryLoadZoneCollision(231);
            if (collision == null) return;

            Assert.True(collision.TryGetGround(new Vector3(x, height - 1.0f, y), 0.0f, 2.0f, out var ground));
            var feet = new Vector3(x, ground.Height, y);
            for (int i = 0; i < 32; i++)
            {
                float angle = i * MathF.PI / 16.0f;
                var end = feet + (new Vector3(MathF.Cos(angle), 0, MathF.Sin(angle)) * 0.5f);
                Assert.False(collision.CrossesLowWall(feet, end, PlayerLocomotionController.StepUpHeight, PlayerLocomotionController.FootRadius, 60.0f, out var normal, out _),
                    $"blocked toward {angle * 180 / MathF.PI:F0} deg by n={normal}");
            }
        }

        /// <summary>
        /// #610: the maintainer climbed a rock in Valkurm Dunes from server position (86.78, -0.98, -103.80); retail stops
        /// them where its 40-42 degree sand triangles meet the stone ones above (round 3: the edges drawn on their
        /// screenshot), about a yalm up. With Stone's own limit runs from around the spot stay on the sand (they reached 5.3
        /// yalms with one 46-degree limit, 10.9 with 55); its stopping faces are in no plane, so the plane exemption (round 5)
        /// leaves them alone. Bastok Mines' Path ramp (zone 234) is still walked up onto its landing, 4.35 yalms up, which its
        /// 45-degree top face kept a 46-degree limit from. City stairs modelled as smooth Stone planes are walked up (round 5):
        /// Bastok Mines' 45-degree flight at server (-40, 7.6, -112), a plane of 17.7 square yalms, 4 yalms up, and Northern
        /// San d'Oria's 27-degree ramp at server (-197, -8, 153), 26.6-degree faces under Stone's 27, 5 yalms up.
        /// </summary>
        [Theory]
        [InlineData(103, 86.78f, -0.98f, -103.80f, 0.0f, 3.0f)]
        [InlineData(234, -146.0f, -1.9f, -44.0f, 4.0f, 100.0f)]
        [InlineData(234, -40.0f, -7.6f, -112.0f, 3.5f, 100.0f)]
        [InlineData(231, -196.9f, 8.0f, 152.8f, 4.5f, 100.0f)]
        public void ClimbLimit_StopsTheValkurmRock_AndKeepsCityStairs(int zoneId, float x, float height, float z, float minRise, float maxRise)
        {
            if (!Directory.Exists(GameDirectory)) return;
            var rm = new ResourceManager(GameDirectory);
            rm.InitializeFileTable();
            var collision = rm.TryLoadZoneCollision(zoneId);
            if (collision == null) return;

            float best = 0.0f;
            for (float dx = -2.0f; dx <= 2.0f; dx += 2.0f)
            {
                for (float dz = -2.0f; dz <= 2.0f; dz += 2.0f)
                {
                    var start = new Vector3(x + dx, height, z + dz);
                    if (!collision.TryGetGround(start with { Y = start.Y - 6.0f }, 0.0f, 12.0f, out var ground)) continue;
                    for (int h = 0; h < 16; h++)
                    {
                        var world = new Gordian.Core.World.WorldState { Collision = collision };
                        var player = new Gordian.Core.World.LocalPlayerState { ServerId = 1 };
                        var me = new Gordian.Core.World.PlayerEntity(1, 1) { Position = start with { Y = ground.Height }, Direction = (byte)(h * 16), IsSpawned = true };
                        world.UpsertEntity(me);
                        var input = new InputState();
                        var controller = new PlayerLocomotionController(input, InputProfile.CreateCompact(), world, player) { CameraYaw = h * 22.5f };
                        input.SetKeyDown(GordianKey.W);
                        float highest = me.Position.Y;
                        for (int i = 0; i < 300; i++)
                        {
                            controller.Update(TimeSpan.FromSeconds(1.0 / 60.0));
                            highest = MathF.Min(highest, me.Position.Y); // internal -Y is up
                        }
                        best = MathF.Max(best, ground.Height - highest);
                    }
                }
            }
            _output.WriteLine($"Zone {zoneId}: highest climb {best:F2} yalms");
            Assert.InRange(best, minRise, maxRise);
        }
    }
}
