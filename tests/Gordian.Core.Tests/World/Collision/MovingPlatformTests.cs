using System.Numerics;
using Gordian.Core.Resources;
using Gordian.Core.Resources.Graphics;
using Gordian.Core.World;
using Gordian.Core.World.Collision;

namespace Gordian.Core.Tests.World.Collision
{
    public class MovingPlatformTests
    {
        private const string GameDirectory = @"G:\Program Files (x86)\PlayOnline\SquareEnix\FINAL FANTASY XI";
        private readonly ITestOutputHelper _output;

        public MovingPlatformTests(ITestOutputHelper output) => _output = output;

        [Fact]
        public void Metalworks_LiftsSpanTheirShafts_FromBothLoaders()
        {
            if (!Directory.Exists(GameDirectory)) return;

            // Collision-only (headless sessions) first, then the full load, which shares the cached collision.
            var collisionOnly = new ResourceManager(GameDirectory);
            collisionOnly.InitializeFileTable();
            var headless = collisionOnly.TryLoadZoneCollision(237);
            var full = new ResourceManager(GameDirectory);
            full.InitializeFileTable();
            Assert.True(full.TryLoadZone(237, out var zone, out _));

            foreach (var platforms in new[] { headless!.MovingPlatforms, zone!.Collision!.MovingPlatforms })
            {
                Assert.Equal(2, platforms.Count);
                foreach (var p in platforms) _output.WriteLine($"{p.Id}: {p.Min}..{p.Max} authored={p.AuthoredHeight} upper={p.UpperHeight} lower={p.LowerHeight} record={p.FromRecord}");

                // Windower capture: the @6l0 lift carries riders between 1.962 and -9.983, the record's two stops
                // (3856 / 798 on Y -13.1). Both shafts share the stops and run antiphase.
                var lift = platforms.Single(p => p.Id == "@6l0");
                Assert.True(lift.FromRecord);
                Assert.Equal(1.962f, lift.LowerHeight, 3);
                Assert.Equal(-9.983f, lift.UpperHeight, 3);
                Assert.True(lift.Contains(-56.3f, -12.1f));
                var other = platforms.Single(p => p.Id == "@6l1");
                Assert.True(other.Contains(-56.0f, 12.0f));
                Assert.Equal((lift.UpperHeight, lift.LowerHeight), (other.UpperHeight, other.LowerHeight));
            }

            // Its parts are drawn separately from the static scenery.
            Assert.True(zone.MovingPlatformGroups.ContainsKey("@6l0"));
            Assert.DoesNotContain(zone.MeshGroups, g => g.Name.StartsWith("liftall", StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Every zone with <c>@</c> records (a scan of all zone DATs, #66): each car takes both stops from its record,
        /// from both loaders. Expected stops are s16 / 256 + record Y; the landings beside each shaft (collision floors)
        /// sit within 0.25 yalms of them.
        /// </summary>
        [Theory]
        [InlineData(141, "@3x0", -52.186f, -28.007f)] // Fort Ghelsba (lever lift)
        [InlineData(143, "@3z0", -32.117f, 1.028f)]   // Palborough Mines (lever lift)
        [InlineData(149, "@450", -9.168f, -1.969f)]   // Davoi (lever lift; the car is authored at -8.19, between its stops)
        [InlineData(9, "@090", -0.334f, 32.147f)]     // Pso'Xja (11 lifts)
        [InlineData(9, "@093", -0.348f, 48.141f)]
        [InlineData(9, "@097", -0.066f, 15.898f)]     // a lift room the landing heuristic sent to 48.0
        public void ZoneLifts_TakeTheirStopsFromTheRecord(int zoneId, string id, float upper, float lower)
        {
            if (!Directory.Exists(GameDirectory)) return;

            var collisionOnly = new ResourceManager(GameDirectory);
            collisionOnly.InitializeFileTable();
            var headless = collisionOnly.TryLoadZoneCollision(zoneId)!;
            var full = new ResourceManager(GameDirectory);
            full.InitializeFileTable();
            Assert.True(full.TryLoadZone(zoneId, out var zone, out _));

            foreach (var platforms in new[] { headless.MovingPlatforms, zone!.Collision!.MovingPlatforms })
            {
                Assert.All(platforms, p => Assert.True(p.FromRecord, $"{p.Id} fell back to the landing heuristic"));
                var lift = platforms.Single(p => p.Id == id);
                _output.WriteLine($"{zoneId} {lift.Id}: authored={lift.AuthoredHeight:F3} upper={lift.UpperHeight:F3} lower={lift.LowerHeight:F3} rest={lift.RestHeight:F3}");
                Assert.Equal(upper, lift.UpperHeight, 2);
                Assert.Equal(lower, lift.LowerHeight, 2);
            }
            if (zoneId == 9) Assert.Equal(11, headless.MovingPlatforms.Count);
        }

        [Fact]
        public void Create_TakesTheStopsFromTheLiftRecord_ElseTheLandings()
        {
            var empty = new ZoneCollisionMesh(Array.Empty<CollisionTriangle>());
            var min = new Vector2(-59, -15);
            var max = new Vector2(-53, -9);
            var record = new ZoneInteraction("@6l0", new Vector3(-56f, -13.1f, -12f), 0f, new Vector3(5.15f, 31.475f, 5.15f), 3856, 798);
            Assert.True(record.TryGetLiftFloors(out float floor0, out float floor1));
            Assert.Equal(1.9625f, floor0, 3);
            Assert.Equal(-9.983f, floor1, 3);

            var lift = MovingPlatforms.Create("@6l0", min, max, -9.98f, empty, record)!;
            Assert.True(lift.FromRecord);
            Assert.Equal(-9.983f, lift.UpperHeight, 3);
            Assert.Equal(1.9625f, lift.LowerHeight, 3);

            // No record, a record with no travel, or a record that is not a lift: the landing heuristic (here no landings).
            Assert.Null(MovingPlatforms.Create("@6l0", min, max, -9.98f, empty));
            Assert.Null(MovingPlatforms.Create("@6l0", min, max, -9.98f, empty, record with { LiftFloor0 = 798 }));
            Assert.Null(MovingPlatforms.Create("@6l0", min, max, -9.98f, empty, record with { Id = "_6l0" }));
            Assert.False((record with { Center = new Vector3(0, float.NaN, 0) }).TryGetLiftFloors(out _, out _));
        }

        [Fact]
        public void RestHeight_IsTheStopNearestTheAuthoredCar()
        {
            // Davoi's car is authored at -8.19, a yalm below its upper stop: with no elevator entity it rests at the stop.
            var davoi = new MovingPlatform("@450", new Vector2(20.9f, -149.5f), new Vector2(27.8f, -142.9f), -8.19f, -9.17f, -1.97f, FromRecord: true);
            Assert.Equal(-9.17f, davoi.RestHeight);
            Assert.Equal(-9.17f, MovingPlatforms.HeightAt(davoi, null, 0.0));
            Assert.Equal(-1.97f, (davoi with { AuthoredHeight = -2.5f }).RestHeight);
            Assert.Equal(-9.17f + 8.19f, new PlatformHeight(davoi, davoi.RestHeight).Offset, 3); // the parts move to the stop
        }

        private static MovingPlatform RecordLift(string id, Vector2 min, Vector2 max, float authored, float floor0, float floor1) =>
            new(id, min, max, authored, MathF.Min(floor0, floor1), MathF.Max(floor0, floor1), FromRecord: true, Floor0: floor0, Floor1: floor1);

        private static WorldEntity ElevatorEntity(uint id, string fourCc, Vector3 position, byte animation, double legStart) =>
            new(id, (ushort)id, EntityType.Elevator)
            {
                TransportId = fourCc,
                Position = position,
                AnimationState = animation,
                TransportStartSeconds = (uint)legStart,
                TransportTravelSeconds = 8,
                IsSpawned = true,
            };

        [Fact]
        public void LeverLift_Animation11GoesToFloor1_TheTop()
        {
            // Palborough Mines @3z0: floor 0 = 1.028 (bottom), floor 1 = -32.117 (top, internal -Y up). LSB spawns it with
            // animation 11 and, its lever lifts being "reversed", at the top; Davoi's (-1.969 / -9.168) likewise.
            var palborough = RecordLift("@3z0", new Vector2(176, 59.4f), new Vector2(182, 65), 0.85f, 1.028f, -32.117f);
            var arrived = ElevatorEntity(1, "@3z0", new Vector3(179.03f, -19.22f, 62.61f), MovingPlatforms.AnimationDown, 0);
            Assert.Equal(-32.117f, MovingPlatforms.HeightAt(palborough, arrived, 1000.0), 3);
            arrived.AnimationState = MovingPlatforms.AnimationUp; // LSB's "up" on a reversed lift: it descends
            Assert.Equal(1.028f, MovingPlatforms.HeightAt(palborough, arrived, 1000.0), 3);

            var davoi = RecordLift("@450", new Vector2(20.9f, -149.5f), new Vector2(27.8f, -142.9f), -8.19f, -1.969f, -9.168f);
            Assert.Equal(-9.168f, MovingPlatforms.HeightAt(davoi, ElevatorEntity(2, "@450", new Vector3(24.3f, -8.7f, -146.2f), MovingPlatforms.AnimationDown, 0), 1000.0), 3);
        }

        [Fact]
        public void Metalworks_PairedByFourCc_PlaysAsBefore()
        {
            // Both shafts share the stops (floor 0 = 1.962 bottom, floor 1 = -9.983 top); LSB puts entity "@6l0" in the
            // north shaft (z +12, car @6l1) and "@6l1" in the south one (z -12, car @6l0), always on opposite legs.
            var south = RecordLift("@6l0", new Vector2(-59, -15), new Vector2(-53, -9), -9.98f, 1.962f, -9.983f);
            var north = RecordLift("@6l1", new Vector2(-59, 9), new Vector2(-53, 15), 1.97f, 1.962f, -9.983f);
            var world = new WorldState { Collision = new ZoneCollisionMesh(Array.Empty<CollisionTriangle>()) { MovingPlatforms = new[] { south, north } } };
            world.UpsertEntity(ElevatorEntity(0x01000001, "@6l0", new Vector3(-56.0f, -13.1f, 12.0f), MovingPlatforms.AnimationUp, 1000));
            world.UpsertEntity(ElevatorEntity(0x01000002, "@6l1", new Vector3(-56.0f, -13.1f, -12.0f), MovingPlatforms.AnimationDown, 1000));

            // Before: the entity's own shaft went up on 10 (north up, south down). Now: car @6l0 (south) goes to floor 0
            // on 10 and car @6l1 (north) to floor 1 on 11: the same heights.
            var done = MovingPlatforms.Evaluate(world.Collision, world, 1010.0);
            Assert.Equal(1.962f, done.Single(h => h.Platform.Id == "@6l0").Height, 3);
            Assert.Equal(-9.983f, done.Single(h => h.Platform.Id == "@6l1").Height, 3);
            var half = MovingPlatforms.Evaluate(world.Collision, world, 1000.0 + (11.945 / MovingPlatforms.LiftSpeed / 2));
            Assert.Equal(half[0].Height, half[1].Height, 2); // crossing in the middle
        }

        [Fact]
        public void EventPose_OnALift_StandsOnTheLift_NotTheShaftFloor()
        {
            // The lever question while riding Palborough Mines' lift at its bottom stop: the event places the rider on the
            // floor under its event position, which was the pit far below the car (#66, also before #66).
            var pit = new ZoneCollisionMesh(new[]
            {
                new CollisionTriangle(new Vector3(170, 5.0f, 50), new Vector3(190, 5.0f, 50), new Vector3(170, 5.0f, 70), -Vector3.UnitY, false, default, false),
                new CollisionTriangle(new Vector3(190, 5.0f, 50), new Vector3(190, 5.0f, 70), new Vector3(170, 5.0f, 70), -Vector3.UnitY, false, default, false),
            });
            var lift = RecordLift("@3z0", new Vector2(176, 59.4f), new Vector2(182, 65), 0.85f, 1.028f, -32.117f);
            var heights = new[] { new PlatformHeight(lift, 1.028f) };
            var rider = new Vector3(179.6f, 1.028f, 63.6f);

            Assert.Equal(5.0f, EntityGrounding.GetEventDisplayHeight(rider, pit), 3);           // without platforms: the pit
            Assert.Equal(1.028f, EntityGrounding.GetEventDisplayHeight(rider, pit, heights), 3); // on the car
            Assert.Equal(5.0f, EntityGrounding.GetEventDisplayHeight(rider with { X = 185f }, pit, heights), 3); // off the car

            // A rider stays on the car it is riding while the car moves away from the event position's height.
            var moved = new[] { new PlatformHeight(lift, -10.0f) };
            Assert.Equal(-10.0f, EntityGrounding.GetEventDisplayHeight(rider, pit, moved, "@3z0"), 3);
        }

        private static readonly MovingPlatform Lift = new("@6l0",new Vector2(-59, -15), new Vector2(-53, -9), -10.0f, -10.0f, 2.0f);

        [Fact]
        public void HeightAt_FollowsTheElevatorLegAtConstantSpeed()
        {
            var elevator = new WorldEntity(1, 1, EntityType.Elevator)
            {
                TransportId = "@6l0",
                TransportStartSeconds = 1000,
                TransportTravelSeconds = 8,
                AnimationState = MovingPlatforms.AnimationUp,
            };

            // 12 yalms at the retail 1.99 yalms/s: a 6.03-second climb inside the 8-second leg.
            Assert.Equal(2.0f, MovingPlatforms.HeightAt(Lift, elevator, 990.0), 3);                     // before the leg
            Assert.Equal(2.0f - (1.99f * 3.0f), MovingPlatforms.HeightAt(Lift, elevator, 1003.0), 2);   // 3 s in
            Assert.Equal(-10.0f, MovingPlatforms.HeightAt(Lift, elevator, 1006.1), 3);                  // arrived at 6 s

            elevator.AnimationState = MovingPlatforms.AnimationDown;
            Assert.Equal(-10.0f + (1.99f * 2.0f), MovingPlatforms.HeightAt(Lift, elevator, 1002.0), 2);
            Assert.Equal(-10.0f, MovingPlatforms.HeightAt(Lift, null, 1002.0), 3); // no elevator: authored pose
        }

        [Fact]
        public void PlatformUnderTheFeet_WinsOverTheShaftFloor()
        {
            var platforms = new[] { new PlatformHeight(Lift, -6.0f) };
            var shaftFloor = new GroundHit(2.0f, new Vector3(0, -1, 0), CollisionTerrain.Stone, 0);

            var ground = shaftFloor;
            Assert.True(MovingPlatforms.TryOverride(platforms, new Vector3(-56, -6.2f, -12), 0.5f, 60f, true, ref ground));
            Assert.Equal(-6.0f, ground.Height, 3);

            ground = shaftFloor; // outside the footprint, or too far above the feet: the static floor stands
            Assert.False(MovingPlatforms.TryOverride(platforms, new Vector3(-50, -6.2f, -12), 0.5f, 60f, true, ref ground));
            Assert.False(MovingPlatforms.TryOverride(platforms, new Vector3(-56, 2.0f, -12), 0.5f, 60f, true, ref ground));
        }

        private static readonly Vector3 FloorUp = new(0.0f, -1.0f, 0.0f);

        private static IEnumerable<CollisionTriangle> Floor(float x0, float z0, float x1, float z1, float y)
        {
            yield return new CollisionTriangle(new(x0, y, z0), new(x1, y, z0), new(x1, y, z1), FloorUp, false, CollisionTerrain.Stone, false);
            yield return new CollisionTriangle(new(x0, y, z0), new(x1, y, z1), new(x0, y, z1), FloorUp, false, CollisionTerrain.Stone, false);
        }

        /// <summary>
        /// A shaft floor at y = 2 under the lift (x -59..-53, z -15..-9) and a floor around it, with the @6l0 lift
        /// travelling 2 -> -10 and its elevator entity currently at <paramref name="height"/> on an upward leg.
        /// </summary>
        private static (WorldState World, WorldEntity Elevator) LiftWorld(float height)
        {
            var mesh = new ZoneCollisionMesh(Floor(-80, -40, 0, 20, 2.0f).ToList());
            mesh.MovingPlatforms = new[] { Lift with { AuthoredHeight = -10.0f, UpperHeight = -10.0f, LowerHeight = 2.0f } };
            var world = new WorldState { Collision = mesh };
            var elevator = new WorldEntity(0x999, 999, EntityType.Elevator)
            {
                TransportId = "@6l0",
                TransportTravelSeconds = 8,
                AnimationState = MovingPlatforms.AnimationUp,
                IsSpawned = true,
            };
            SetLeg(elevator, height);
            world.UpsertEntity(elevator);
            return (world, elevator);
        }

        /// <summary>
        /// Starts the elevator's upward leg so the platform is at <paramref name="height"/> now.
        /// </summary>
        private static void SetLeg(WorldEntity elevator, float height)
        {
            double now = VanaTime.GetEarthSecondsSinceEpoch(DateTime.UtcNow);
            double progress = (2.0 - height) / 12.0; // 2 -> -10 over the leg
            elevator.TransportStartSeconds = (uint)Math.Round(now - (progress * 8.0));
        }

        [Fact]
        public void Locomotion_StandingOnTheLiftRidesItUp()
        {
            var (world, elevator) = LiftWorld(2.0f);
            var player = new LocalPlayerState { ServerId = 0x1001 };
            var localEnt = new PlayerEntity(player.ServerId, 1) { Position = new Vector3(-56, 2.0f, -12), IsSpawned = true };
            world.UpsertEntity(localEnt);
            var controller = new Gordian.Core.Input.PlayerLocomotionController(new Gordian.Core.Input.InputState(),
                Gordian.Core.Input.InputProfile.CreateCompact(), world, player);

            // Play the 8-second leg at 60 ticks per second from a fixed start, standing still the whole time.
            elevator.TransportStartSeconds = 1000;
            double clock = 999.0;
            controller.PlatformClock = () => clock;
            for (int tick = 0; tick < 600; tick++)
            {
                clock += 1.0 / 60.0;
                controller.Update(TimeSpan.FromSeconds(1.0 / 60.0));
                if (tick == 240) Assert.InRange(localEnt.Position.Y, -4.1f, -3.8f); // 3 s into the climb
            }
            Assert.Equal(-10.0f, localEnt.Position.Y, 2);
        }

        [Fact]
        public void RemoteCharacterOnTheLift_IsDrawnRidingIt()
        {
            var (world, elevator) = LiftWorld(2.0f);
            // Its client never moves it: the reported height stays at the bottom the whole ride.
            var rider = new PlayerEntity(0x2002, 2) { Position = new Vector3(-56, 2.0f, -12), IsSpawned = true };
            var mesh = world.Collision!;
            elevator.TransportStartSeconds = 1000;

            Assert.Equal(2.0f, EntityGrounding.GetDisplayHeight(rider, mesh, MovingPlatforms.Evaluate(mesh, world, 999.0)), 2);
            Assert.Equal(2.0f - (1.99f * 3.0f), EntityGrounding.GetDisplayHeight(rider, mesh, MovingPlatforms.Evaluate(mesh, world, 1003.0)), 2);
            Assert.Equal(-10.0f, EntityGrounding.GetDisplayHeight(rider, mesh, MovingPlatforms.Evaluate(mesh, world, 1010.0)), 2);

            // Stepping off the footprint ends the ride.
            rider.Position = new Vector3(-40, 2.0f, -12);
            Assert.Equal(2.0f, EntityGrounding.GetDisplayHeight(rider, mesh, MovingPlatforms.Evaluate(mesh, world, 1010.0)), 2);
            Assert.Equal(string.Empty, rider.RidingPlatformId);
        }

        [Fact]
        public void ElevatorEntity_DrivesTheLiftBesideIt_NotTheOneItsIdNames()
        {
            // Metalworks: LSB's "@6l0" elevator stands at z = +12, beside the lift whose BlockID is @6l1.
            var north = Lift with { Id = "@6l1", Min = new Vector2(-59, 9), Max = new Vector2(-53, 15) };
            var south = Lift;
            var elevator = new WorldEntity(1, 1, EntityType.Elevator) { TransportId = "@6l0", Position = new Vector3(-56.0f, -13.1f, 12.0f) };
            Assert.Same(north, MovingPlatforms.PlatformOf(new[] { south, north }, elevator));

            // Far from every lift (no position yet): the id decides.
            elevator.Position = Vector3.Zero;
            Assert.Same(south, MovingPlatforms.PlatformOf(new[] { south, north }, elevator));
        }

        [Fact]
        public void FreshLeg_StartsFromItsArrival_WithoutAJump()
        {
            var elevator = new WorldEntity(1, 1, EntityType.Elevator)
            {
                TransportStartSeconds = 1000,
                TransportTravelSeconds = 8,
                AnimationState = MovingPlatforms.AnimationUp,
                TransportObservedSeconds = 1000.9, // the update arrived 0.9 s after the whole-second stamp
            };
            Assert.Equal(2.0f, MovingPlatforms.HeightAt(Lift, elevator, 1000.9), 3);   // no opening jump
            Assert.Equal(-10.0f, MovingPlatforms.HeightAt(Lift, elevator, 1007.0), 3); // the full climb from arrival
            Assert.True(MovingPlatforms.HeightAt(Lift, elevator, 1006.7) > -10.0f);

            // A leg seen arriving plays from its arrival however late the stamp says it is (a capture: 1.5-2.9 s).
            elevator.TransportObservedSeconds = 1002.9;
            Assert.Equal(2.0f, MovingPlatforms.HeightAt(Lift, elevator, 1002.9), 3);

            // Zoning in mid-leg (no arrival seen): the stamp, shifted by the learned clock gap.
            elevator.TransportObservedSeconds = 0.0;
            double skew = MovingPlatforms.RefineClockSkew(0.0, 1002.5, 1000);
            Assert.Equal(2.5, skew, 6);
            Assert.Equal(1002.5, MovingPlatforms.LegStart(elevator, skew), 6);
            Assert.Equal(skew, MovingPlatforms.RefineClockSkew(skew, 1030.0, 1000), 6); // a delayed update is ignored
        }

        [Fact]
        public void EnteringAShaft_NeedsThePlatformAtYourLevel()
        {
            var atTop = new[] { new PlatformHeight(Lift, -10.0f) };
            var outside = new Vector3(-50, 2.0f, -12);
            var inside = new Vector3(-56, 2.0f, -12);

            Assert.True(MovingPlatforms.EntersEmptyShaft(atTop, outside, inside, 0.5f));  // platform is up top
            var atBottom = new[] { new PlatformHeight(Lift, 1.962f) };
            Assert.False(MovingPlatforms.EntersEmptyShaft(atBottom, outside, inside, 0.5f)); // platform is here
            Assert.False(MovingPlatforms.EntersEmptyShaft(atTop, inside, inside + new Vector3(0.2f, 0, 0), 0.5f)); // already on it
        }
    }
}
