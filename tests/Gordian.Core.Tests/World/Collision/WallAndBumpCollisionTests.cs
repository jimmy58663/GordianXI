using System.Numerics;
using Gordian.Core.Input;
using Gordian.Core.World;
using Gordian.Core.World.Collision;

namespace Gordian.Core.Tests.World.Collision
{
    public class WallAndBumpCollisionTests
    {
        private static readonly Vector3 Up = new(0.0f, -1.0f, 0.0f);

        private static IEnumerable<CollisionTriangle> Floor(float x0, float z0, float x1, float z1, float y)
        {
            yield return new CollisionTriangle(new(x0, y, z0), new(x1, y, z0), new(x1, y, z1), Up, false, CollisionTerrain.Stone, false);
            yield return new CollisionTriangle(new(x0, y, z0), new(x1, y, z1), new(x0, y, z1), Up, false, CollisionTerrain.Stone, false);
        }

        /// <summary>
        /// A vertical wall in the plane x = <paramref name="x"/>, from the floor up <paramref name="height"/> yalms,
        /// facing -X (its solid side is +X).
        /// </summary>
        private static IEnumerable<CollisionTriangle> WallFacingMinusX(float x, float height)
        {
            var normal = new Vector3(-1, 0, 0);
            yield return new CollisionTriangle(new(x, 0, -20), new(x, -height, -20), new(x, -height, 20), normal, true, CollisionTerrain.Stone, false);
            yield return new CollisionTriangle(new(x, 0, -20), new(x, -height, 20), new(x, 0, 20), normal, true, CollisionTerrain.Stone, false);
        }

        private static ZoneCollisionMesh Room(float wallHeight)
        {
            var triangles = Floor(-20, -20, 20, 20, 0.0f).ToList();
            triangles.AddRange(WallFacingMinusX(5.0f, wallHeight));
            return new ZoneCollisionMesh(triangles);
        }

        [Fact]
        public void ResolveWalls_StopsAtAWallAndSlidesAlongIt()
        {
            var mesh = Room(4.0f);

            var stopped = mesh.ResolveWalls(new Vector3(4.0f, 0, 0), new Vector3(6.0f, 0, 0), 0.4f, 0.75f, 1.6f);
            Assert.InRange(stopped.X, 4.55f, 4.61f);

            var slid = mesh.ResolveWalls(new Vector3(4.5f, 0, 0), new Vector3(5.5f, 0, 1.0f), 0.4f, 0.75f, 1.6f);
            Assert.InRange(slid.X, 4.55f, 4.61f);
            Assert.Equal(1.0f, slid.Z, 3);
        }

        [Fact]
        public void ResolveWalls_LeavesCurbsToStepUpAndPassesBackFaces()
        {
            var curb = Room(0.5f);
            Assert.Equal(6.0f, curb.ResolveWalls(new Vector3(4.0f, 0, 0), new Vector3(6.0f, 0, 0), 0.4f, 0.75f, 1.6f).X, 3);

            var wall = Room(4.0f);
            Assert.True(wall.ResolveWalls(new Vector3(7.0f, 0, 0), new Vector3(3.0f, 0, 0), 0.4f, 0.75f, 1.6f).X <= 3.0f);
        }

        [Fact]
        public void Locomotion_WallsStopThePlayerUnlessToggledOff()
        {
            var world = new WorldState { Collision = Room(4.0f) };
            var player = new LocalPlayerState { ServerId = 0x1001 };
            var localEnt = new PlayerEntity(player.ServerId, 1) { Position = new Vector3(3.0f, 0, 0), IsSpawned = true };
            world.UpsertEntity(localEnt);
            var input = new InputState();
            var controller = new PlayerLocomotionController(input, InputProfile.CreateCompact(), world, player);

            input.SetKeyDown(GordianKey.W); // camera yaw 0: runs toward +X
            for (int i = 0; i < 60; i++) controller.Update(TimeSpan.FromSeconds(1.0 / 60.0));
            float stop = 5.0f - PlayerLocomotionController.BodyRadius;
            Assert.InRange(localEnt.Position.X, stop - 0.02f, stop + 0.01f);

            controller.Collision.Requested &= ~CollisionLayers.Walls;
            for (int i = 0; i < 60; i++) controller.Update(TimeSpan.FromSeconds(1.0 / 60.0));
            Assert.True(localEnt.Position.X > 5.5f);
        }

        private static (WorldState World, WorldEntity Self) Crowd(params Vector3[] others)
        {
            var world = new WorldState();
            var self = new PlayerEntity(1, 1) { Position = Vector3.Zero, IsSpawned = true };
            world.UpsertEntity(self);
            uint id = 100;
            foreach (var position in others)
            {
                world.UpsertEntity(new WorldEntity(id, (ushort)id, EntityType.Npc) { Position = position, IsSpawned = true, Hpp = 100 });
                id++;
            }
            return (world, self);
        }

        [Fact]
        public void Bump_StopsBrieflyThenPassesThroughWithGrace()
        {
            var (world, self) = Crowd(new Vector3(1.0f, 0, 0), new Vector3(2.4f, 0, 0));
            var bump = new EntityBumpCollision();
            const float Tick = 1.0f / 60.0f;
            var from = new Vector3(0.2f, 0, 0);
            var to = new Vector3(0.35f, 0, 0); // enters the first NPC's 0.7-yalm contact

            int blockedTicks = 0;
            while (!bump.TryMove(world, self, from, to, Tick)) blockedTicks++;
            Assert.InRange(blockedTicks * Tick, EntityBumpCollision.HoldSeconds - 0.05f, EntityBumpCollision.HoldSeconds + 0.02f);
            Assert.True(bump.GraceRemaining > 1.0f);

            // During the grace the next NPC in the crowd does not block.
            Assert.True(bump.TryMove(world, self, new Vector3(1.6f, 0, 0), new Vector3(1.75f, 0, 0), Tick));
        }

        [Fact]
        public void Bump_IgnoresOverlappedHiddenNonBlockingAndOtherFloors()
        {
            var (world, self) = Crowd(new Vector3(0.3f, 0, 0));
            Assert.True(new EntityBumpCollision().TryMove(world, self, Vector3.Zero, new Vector3(0.1f, 0, 0), 0.016f));

            var marks = new Action<WorldEntity>[]
            {
                e => e.IsHidden = true,
                e => e.IsInvisible = true,
                e => e.IsNonBlocking = true,
                e => e.Position = new Vector3(1, -5, 0),
            };
            foreach (var mark in marks)
            {
                var (w, s) = Crowd(new Vector3(1.0f, 0, 0));
                w.TryGetByServerId(100, out var npc);
                mark(npc!);
                Assert.True(new EntityBumpCollision().TryMove(w, s, new Vector3(0.2f, 0, 0), new Vector3(0.35f, 0, 0), 0.016f));
            }
        }

        [Fact]
        public void Locomotion_LiftedPlayerHoldsHeightThenWalksOntoALedge()
        {
            // A 2-yalm ledge at x >= 5 (internal -Y up): too high to step onto from the floor.
            var triangles = Floor(-20, -20, 5, 20, 0.0f).ToList();
            triangles.AddRange(Floor(5, -20, 20, 20, -2.0f));
            triangles.AddRange(WallFacingMinusX(5.0f, 2.0f));
            var world = new WorldState { Collision = new ZoneCollisionMesh(triangles) };
            var player = new LocalPlayerState { ServerId = 0x1001 };
            var localEnt = new PlayerEntity(player.ServerId, 1) { Position = new Vector3(4.0f, 0, 0), IsSpawned = true };
            world.UpsertEntity(localEnt);
            var input = new InputState();
            var controller = new PlayerLocomotionController(input, InputProfile.CreateCompact(), world, player);

            // From the floor the ledge blocks.
            input.SetKeyDown(GordianKey.W);
            for (int i = 0; i < 60; i++) controller.Update(TimeSpan.FromSeconds(1.0 / 60.0));
            Assert.True(localEnt.Position.X < 5.0f);
            input.SetKeyUp(GordianKey.W);

            // Lifted 3 yalms (a /moveto with a height): it floats while standing still...
            localEnt.Position = localEnt.Position with { Y = -3.0f };
            for (int i = 0; i < 30; i++) controller.Update(TimeSpan.FromSeconds(1.0 / 60.0));
            Assert.Equal(-3.0f, localEnt.Position.Y, 3);

            // ...and walking forward lands it on the ledge.
            input.SetKeyDown(GordianKey.W);
            for (int i = 0; i < 60; i++) controller.Update(TimeSpan.FromSeconds(1.0 / 60.0));
            Assert.True(localEnt.Position.X > 5.5f);
            Assert.Equal(-2.0f, localEnt.Position.Y, 3);
        }

        [Theory]
        [InlineData("!pos 124.1 -588.2 -5.6", "!pos 124.1 -5.6 -588.2")]
        [InlineData("!pos 1 2 3 230", "!pos 1 3 2 230")]
        [InlineData("!pos 25.6, 35, -15", "!pos 25.6 -15 35")]
        [InlineData("!pos (25.6, 35, -15) 230", "!pos 25.6 -15 35 230")]
        [InlineData("!position 1 2 3", "!position 1 2 3")]
        [InlineData("!pos", "!pos")]
        [InlineData("!pos Tarudrake", "!pos Tarudrake")]
        [InlineData("!zone 230", "!zone 230")]
        public void ServerPosCommand_TakesWindowerOrder(string typed, string sent)
        {
            Assert.Equal(sent, Gordian.Core.Network.ChatCommandRouter.ToServerCoordinateOrder(typed));
            Assert.Equal(sent, Gordian.Core.Network.ChatCommandRouter.Parse(typed, Gordian.Core.Network.Packets.ChatSendKind.Say, null).Message);
        }

        [Fact]
        public void Locomotion_SteppingOffAHeightFallsUnderGravityWhileMovingForward()
        {
            var world = new WorldState { Collision = new ZoneCollisionMesh(Floor(-50, -50, 50, 50, 0.0f).ToList()) };
            var player = new LocalPlayerState { ServerId = 0x1001 };
            var localEnt = new PlayerEntity(player.ServerId, 1) { Position = new Vector3(0, -10.0f, 0), IsSpawned = true };
            world.UpsertEntity(localEnt);
            var input = new InputState();
            var controller = new PlayerLocomotionController(input, InputProfile.CreateCompact(), world, player);

            input.SetKeyDown(GordianKey.W);
            float previous = localEnt.Position.Y;
            int ticks = 0;
            do
            {
                controller.Update(TimeSpan.FromSeconds(1.0 / 60.0));
                Assert.True(localEnt.Position.Y >= previous); // only ever descends (internal +Y is down)
                previous = localEnt.Position.Y;
                ticks++;
            } while (controller.IsFalling || ticks < 2);

            // A 10-yalm drop at 5 yalms/s: accelerate to the top fall speed, then fall steadily; carried forward throughout.
            const float G = PlayerLocomotionController.Gravity, VMax = PlayerLocomotionController.MaxFallSpeed;
            float accelerating = VMax / G, acceleratedDrop = 0.5f * G * accelerating * accelerating;
            float fallSeconds = acceleratedDrop >= 10.0f
                ? MathF.Sqrt(2.0f * 10.0f / G)
                : accelerating + ((10.0f - acceleratedDrop) / VMax);
            Assert.Equal(0.0f, localEnt.Position.Y, 3);
            Assert.InRange(ticks / 60.0f, fallSeconds - 0.04f, fallSeconds + 0.04f);
            Assert.InRange(localEnt.Position.X, (5.0f * fallSeconds) - 0.3f, (5.0f * fallSeconds) + 0.3f);
        }

        [Fact]
        public void Locomotion_FallContinuesAfterReleasingForward_LikeRetail()
        {
            // Windower capture: a single tap off a 20-yalm height kept falling with no further horizontal movement,
            // landing about 0.8 s later.
            var world = new WorldState { Collision = new ZoneCollisionMesh(Floor(-50, -50, 50, 50, 0.0f).ToList()) };
            var player = new LocalPlayerState { ServerId = 0x1001 };
            var localEnt = new PlayerEntity(player.ServerId, 1) { Position = new Vector3(0, -20.0f, 0), IsSpawned = true };
            world.UpsertEntity(localEnt);
            var input = new InputState();
            var controller = new PlayerLocomotionController(input, InputProfile.CreateCompact(), world, player);

            input.SetKeyDown(GordianKey.W);
            controller.Update(TimeSpan.FromSeconds(1.0 / 60.0));
            input.SetKeyUp(GordianKey.W);
            float x = localEnt.Position.X;

            int ticks = 0;
            while (controller.IsFalling && ticks < 600) { controller.Update(TimeSpan.FromSeconds(1.0 / 60.0)); ticks++; }

            Assert.Equal(0.0f, localEnt.Position.Y, 3);
            Assert.Equal(x, localEnt.Position.X, 3);
            Assert.InRange(ticks / 60.0f, 0.75f, 0.95f);
        }
    }
}
