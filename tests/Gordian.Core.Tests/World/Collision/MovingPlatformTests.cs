using System.Numerics;
using Gordian.Core.Resources;
using Gordian.Core.World;
using Gordian.Core.World.Collision;
using Xunit.Abstractions;

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
                foreach (var p in platforms) _output.WriteLine($"{p.Id}: {p.Min}..{p.Max} authored={p.AuthoredHeight} upper={p.UpperHeight} lower={p.LowerHeight}");

                // Windower capture: the @6l0 lift carries riders between 1.962 and -9.983.
                var lift = platforms.Single(p => p.Id == "@6l0");
                Assert.InRange(lift.LowerHeight, 1.9f, 2.05f);
                Assert.InRange(lift.UpperHeight, -10.05f, -9.95f);
                Assert.True(lift.Contains(-56.3f, -12.1f));
                var other = platforms.Single(p => p.Id == "@6l1");
                Assert.True(other.Contains(-56.0f, 12.0f));
                Assert.InRange(other.UpperHeight - other.LowerHeight, -12.1f, -11.8f);
            }

            // Its parts are drawn separately from the static scenery.
            Assert.True(zone.MovingPlatformGroups.ContainsKey("@6l0"));
            Assert.DoesNotContain(zone.MeshGroups, g => g.Name.StartsWith("liftall", StringComparison.OrdinalIgnoreCase));
        }

        private static readonly MovingPlatform Lift = new("@6l0", new Vector2(-59, -15), new Vector2(-53, -9), -10.0f, -10.0f, 2.0f);

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
