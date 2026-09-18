// tests/Gordian.Core.Tests/Input/PlayerLocomotionControllerTests.cs
using System;
using System.Numerics;
using System.Threading.Tasks;
using Gordian.Core.Actions;
using Gordian.Core.Config;
using Gordian.Core.Graphics;
using Gordian.Core.Input;
using Gordian.Core.Network;
using Gordian.Core.World;
using Xunit;

namespace Gordian.Core.Tests.Input
{
    public sealed class PlayerLocomotionControllerTests
    {
        private (PlayerLocomotionController controller, InputState input, WorldState world, LocalPlayerState player, PlayerEntity localEnt) CreateTestHarness()
        {
            var world = new WorldState();
            var player = new LocalPlayerState { ServerId = 0x12345678 };
            var localEnt = new PlayerEntity(player.ServerId, 1)
            {
                Name = "TestPlayer",
                Position = Vector3.Zero,
                Direction = 0, // Facing East (+X)
                Speed = 0,
                IsSpawned = true
            };
            world.UpsertEntity(localEnt);

            var profile = InputProfile.CreateCompact();
            var input = new InputState();
            var controller = new PlayerLocomotionController(input, profile, world, player);

            return (controller, input, world, player, localEnt);
        }

        [Fact]
        public void Update_WhenHoldingMoveForward_MovesPlayerAlongHeadingAtRunSpeed()
        {
            var (controller, input, world, player, localEnt) = CreateTestHarness();

            // Heading 0 = East (+X)
            localEnt.Direction = 0;
            localEnt.Position = Vector3.Zero;

            // Press W (MoveForward)
            input.SetKeyDown(GordianKey.W);

            // Update for 1.0 second
            controller.Update(TimeSpan.FromSeconds(1.0));

            // Standard run speed = 50 => 5.0 yalms/sec along +X
            Assert.Equal(50, localEnt.Speed);
            Assert.InRange(localEnt.Position.X, 4.99f, 5.01f);
            Assert.InRange(localEnt.Position.Y, -0.01f, 0.01f);
            Assert.InRange(localEnt.Position.Z, -0.01f, 0.01f);
        }

        [Fact]
        public void Update_WhenFacingSouth_MovesPlayerAlongZAxisAndKeepsElevationConstant()
        {
            var (controller, input, world, player, localEnt) = CreateTestHarness();

            // Heading 64 = South (+Z in 3D, keeping elevation Y constant)
            localEnt.Direction = 64;
            localEnt.Position = new Vector3(10f, 15f, 20f);

            input.SetKeyDown(GordianKey.W);
            controller.Update(TimeSpan.FromSeconds(1.0));

            // Standard run speed = 50 => 5.0 yalms/sec along +Z
            Assert.Equal(50, localEnt.Speed);
            Assert.InRange(localEnt.Position.X, 9.99f, 10.01f);
            Assert.Equal(15.0f, localEnt.Position.Y); // Elevation unchanged
            Assert.InRange(localEnt.Position.Z, 24.99f, 25.01f); // 20 + 5 = 25 along +Z
        }

        [Fact]
        public void Update_WhenWalking_MovesPlayerAtWalkSpeed()
        {
            var (controller, input, world, player, localEnt) = CreateTestHarness();

            localEnt.Direction = 0;
            localEnt.Position = Vector3.Zero;

            input.SetKeyDown(GordianKey.W);
            input.IsWalking = true;

            controller.Update(TimeSpan.FromSeconds(1.0));

            // Walk speed = 25 => 2.5 yalms/sec along +X
            Assert.Equal(25, localEnt.Speed);
            Assert.InRange(localEnt.Position.X, 2.49f, 2.51f);
        }

        [Fact]
        public void Update_WhenTurning_UpdatesPlayerDirection()
        {
            var (controller, input, world, player, localEnt) = CreateTestHarness();

            localEnt.Direction = 0; // 0 degrees
            input.SetKeyDown(GordianKey.D); // TurnRight (180 deg/sec)

            // 0.5s turn => 90 degrees clockwise (South in FFXI = 64)
            controller.Update(TimeSpan.FromSeconds(0.5));

            Assert.Equal(64, localEnt.Direction);
        }

        [Fact]
        public void Update_CameraMouseDrag_UpdatesPitchAndYaw()
        {
            var (controller, input, world, player, localEnt) = CreateTestHarness();

            controller.CameraPitch = 15.0f;
            controller.CameraYaw = 0.0f;

            input.AddMouseDelta(100.0f, -50.0f);
            controller.Update(TimeSpan.FromMilliseconds(16));

            Assert.True(controller.CameraYaw > 0.0f);
            Assert.True(controller.CameraPitch < 15.0f);
        }

        [Fact]
        public void Update_ResetCamera_AlignsWithPlayerHeading()
        {
            var (controller, input, world, player, localEnt) = CreateTestHarness();

            // Set player facing South (64 = 90 degrees)
            localEnt.Direction = 64;
            controller.CameraYaw = 270.0f;

            input.SetKeyDown(GordianKey.End); // ResetCamera
            controller.Update(TimeSpan.FromMilliseconds(16));

            Assert.InRange(controller.CameraYaw, 89.0f, 91.0f);
            Assert.Equal(15.0f, controller.CameraPitch);
        }

        [Fact]
        public void ToggleCameraMode_CyclesModesCorrectly()
        {
            var (controller, _, _, _, _) = CreateTestHarness();

            Assert.Equal(CameraMode.ThirdPersonOrbital, controller.CameraMode);

            controller.ToggleCameraMode();
            Assert.Equal(CameraMode.FirstPerson, controller.CameraMode);

            controller.ToggleCameraMode();
            Assert.Equal(CameraMode.FreeCam, controller.CameraMode);

            controller.ToggleCameraMode();
            Assert.Equal(CameraMode.ThirdPersonOrbital, controller.CameraMode);
        }

        [Fact]
        public void Update_InFreeCam_DoesNotMovePlayerEntity()
        {
            var (controller, input, world, player, localEnt) = CreateTestHarness();

            controller.CameraMode = CameraMode.FreeCam;
            var initialPlayerPos = localEnt.Position;

            input.SetKeyDown(GordianKey.W); // MoveForward in FreeCam
            controller.Update(TimeSpan.FromSeconds(1.0));

            // Player entity must remain stationary in FreeCam
            Assert.Equal(initialPlayerPos, localEnt.Position);
            Assert.Equal(0, localEnt.Speed);

            // But Camera eye position should have moved
            Assert.NotEqual(Vector3.Zero, controller.Camera.Position);
        }
    }
}
