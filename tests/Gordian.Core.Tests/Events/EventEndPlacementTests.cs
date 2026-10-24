// tests/Gordian.Core.Tests/Events/EventEndPlacementTests.cs
using System;
using System.Numerics;
using Gordian.Core.Events;
using Gordian.Core.Input;
using Gordian.Core.World;
using Gordian.Core.World.Collision;
using Xunit;

namespace Gordian.Core.Tests.Events
{
    /// <summary>
    /// #338: the player stays where an event's walk left it (Port Jeuno's customs door walks the player through with opcode
    /// 0x1F), rather than reappearing where it stood when the event began.
    /// </summary>
    public class EventEndPlacementTests
    {
        private static readonly Vector3 Up = new(0.0f, -1.0f, 0.0f);

        private static ZoneCollisionMesh FlatFloor(float y) => new(new[]
        {
            new CollisionTriangle(new(-20, y, -20), new(20, y, -20), new(20, y, 20), Up, false, CollisionTerrain.Stone, false),
            new CollisionTriangle(new(-20, y, -20), new(20, y, 20), new(-20, y, 20), Up, false, CollisionTerrain.Stone, false),
        });

        [Fact]
        public void EventWalk_MovesThePlayerAndReportsIt()
        {
            var world = new WorldState { Collision = FlatFloor(0.0f) };
            var player = new LocalPlayerState { ServerId = 0x1001 };
            var self = new PlayerEntity(player.ServerId, 1) { Position = new Vector3(1.0f, 0.0f, 1.0f), IsSpawned = true };
            world.UpsertEntity(self);
            var controller = new PlayerLocomotionController(new InputState(), InputProfile.CreateCompact(), world, player);
            Vector3? reported = null;
            byte reportedDirection = 0;
            controller.LocomotionUpdated += (position, direction, _) => (reported, reportedDirection) = (position, direction);

            // The walk ended 6 yalms on, a little above the floor, facing north (wire 192 = 3/4 turn).
            self.EventPose = new EventPose(new Vector3(7.0f, -0.4f, 1.0f), MathF.PI * 1.5f, 0f);
            player.IsMovementLocked = true;
            EventDialogController.KeepEventPlacement(player, self);
            self.EventPose = null;
            controller.Update(TimeSpan.FromSeconds(1.0 / 60.0));

            Assert.Equal(7.0f, self.Position.X, 3);
            Assert.Equal(0.0f, self.Position.Y, 3); // settled onto the floor
            Assert.Equal(192, self.Direction);
            Assert.Equal(self.Position, reported); // what C2S 0x015 sends
            Assert.Equal(192, reportedDirection);
        }

        [Fact]
        public void NoEventPose_LeavesThePlayerAlone()
        {
            var player = new LocalPlayerState { ServerId = 0x1001 };
            var self = new PlayerEntity(player.ServerId, 1) { Position = new Vector3(1.0f, 0.0f, 1.0f), Direction = 10 };
            EventDialogController.KeepEventPlacement(player, self);
            Assert.False(player.TryTakePositionCorrection(out _, out _));

            // A pose where the player already stands, with its own facing: nothing to do either.
            self.EventPose = new EventPose(self.Position, self.HeadingRadians, 0f);
            EventDialogController.KeepEventPlacement(player, self);
            Assert.False(player.TryTakePositionCorrection(out _, out _));
        }

        [Theory]
        [InlineData(0f, 0)]
        [InlineData(MathF.PI / 2, 64)]
        [InlineData(-MathF.PI / 2, 192)]
        [InlineData(MathF.PI * 2, 0)]
        public void DirectionOf_WrapsToTheWireCircle(float heading, byte expected)
        {
            Assert.Equal(expected, EventDialogController.DirectionOf(heading));
        }
    }
}
