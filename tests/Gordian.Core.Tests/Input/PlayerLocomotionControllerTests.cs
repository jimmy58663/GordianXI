// tests/Gordian.Core.Tests/Input/PlayerLocomotionControllerTests.cs
using System;
using System.Numerics;
using System.Threading.Tasks;
using Gordian.Core.Actions;
using Gordian.Core.Config;
using Gordian.Core.Graphics;
using Gordian.Core.Input;
using Gordian.Core.Network;
using Gordian.Core.Network.Packets;
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

            // W is camera-relative (see Update_WhenHoldingTurnRight_... below): facing follows the
            // camera, not whatever the character happened to already be facing. Point the camera
            // South (heading 64 = 90 degrees) so pressing W faces and moves that way.
            controller.CameraYaw = 90.0f;
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
        public void Update_WhenHoldingTurnRight_FacesAndMovesToCameraRelativeRight()
        {
            var (controller, input, world, player, localEnt) = CreateTestHarness();

            localEnt.Direction = 0;
            localEnt.Position = Vector3.Zero;
            controller.CameraYaw = 0.0f; // Camera facing East

            // D (TurnRight) now faces the character camera-relative-right and runs, like a
            // gamepad stick pushed right, instead of turning in place.
            input.SetKeyDown(GordianKey.D);
            controller.Update(TimeSpan.FromSeconds(1.0));

            // Mirrors the tested gamepad CameraRelative "strafe right" behavior: with the camera
            // facing world East, the camera's true rendered right side is world North (Direction
            // 192), because the renderer displays entities at a mirrored X coordinate.
            Assert.Equal(192, localEnt.Direction);
            Assert.Equal(50, localEnt.Speed);
            Assert.InRange(localEnt.Position.X, -0.01f, 0.01f);
            Assert.InRange(localEnt.Position.Z, -5.01f, -4.99f);
        }

        [Fact]
        public void Update_WhenHoldingTurnLeft_FacesAndMovesToCameraRelativeLeft()
        {
            var (controller, input, world, player, localEnt) = CreateTestHarness();

            localEnt.Direction = 0;
            localEnt.Position = Vector3.Zero;
            controller.CameraYaw = 0.0f;

            // A (TurnLeft) mirrors D: faces camera-relative-left and runs.
            input.SetKeyDown(GordianKey.A);
            controller.Update(TimeSpan.FromSeconds(1.0));

            Assert.Equal(64, localEnt.Direction);
            Assert.Equal(50, localEnt.Speed);
            Assert.InRange(localEnt.Position.X, -0.01f, 0.01f);
            Assert.InRange(localEnt.Position.Z, 4.99f, 5.01f);
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
        public void Update_MouseWheel_ZoomsCameraDistance()
        {
            // Regression guard: the rendering viewport's own OnPointerWheelChanged override never
            // fires on Windows (its surface is a real native child window, bypassing Avalonia's
            // routed-event tree), so wheel zoom must be driven through InputState.AddMouseWheel,
            // consumed here.
            var (controller, input, world, player, localEnt) = CreateTestHarness();

            controller.CameraDistance = 6.0f;

            input.AddMouseWheel(1.0f);
            controller.Update(TimeSpan.FromMilliseconds(16));

            Assert.True(controller.CameraDistance < 6.0f);
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

        private (PlayerLocomotionController controller, InputState input, WorldState world, LocalPlayerState player, PlayerEntity localEnt, PlayerActionService actionService) CreateTestHarnessWithActionService()
        {
            var world = new WorldState();
            var player = new LocalPlayerState { ServerId = 0x12345678 };
            var localEnt = new PlayerEntity(player.ServerId, 1)
            {
                Name = "TestPlayer",
                Position = Vector3.Zero,
                Direction = 0,
                Speed = 0,
                IsSpawned = true
            };
            world.UpsertEntity(localEnt);

            var profile = InputProfile.CreateCompact();
            var input = new InputState();
            var combatState = new CombatState();
            var partyState = new PartyState();
            Task CaptureChunk(ReadOnlyMemory<byte> m, bool u) => Task.CompletedTask;
            var combatModule = new CombatPacketModule(combatState, player, CaptureChunk);
            var chatModule = new ChatPacketModule(CaptureChunk);
            var partyModule = new PartyPacketModule(partyState, CaptureChunk);
            var entityModule = new EntityPacketModule(world, player, CaptureChunk);
            var lifecycleModule = new LifecyclePacketModule(new SessionProfile(), CaptureChunk);

            var actionService = new PlayerActionService(
                new SessionProfile(),
                world,
                player,
                combatModule,
                chatModule,
                partyModule,
                entityModule,
                lifecycleModule,
                CaptureChunk);

            var controller = new PlayerLocomotionController(input, profile, world, player, actionService);
            return (controller, input, world, player, localEnt, actionService);
        }

        [Fact]
        public void Update_WhenLockedOn_FacesTargetDirectly()
        {
            var (controller, input, world, player, localEnt, actionService) = CreateTestHarnessWithActionService();

            var target = new WorldEntity(0x9999, 2, EntityType.Monster)
            {
                Position = new Vector3(0f, 0f, 10f), // South (+Z)
                IsSpawned = true
            };
            world.UpsertEntity(target);

            localEnt.Direction = 0; // East
            actionService.SetTarget(target);
            actionService.SetLockOn(true);

            controller.Update(TimeSpan.FromMilliseconds(16));

            // Heading towards (0, 0, 10) from (0, 0, 0) is South (Direction = 64)
            Assert.Equal(64, localEnt.Direction);
            Assert.InRange(localEnt.RenderHeadingRadians, MathF.PI / 2.0f - 0.05f, MathF.PI / 2.0f + 0.05f);
        }

        [Fact]
        public void Update_WhenLockedOn_StrafingMovesPerpendicularToTargetWithoutChangingFacing()
        {
            var (controller, input, world, player, localEnt, actionService) = CreateTestHarnessWithActionService();

            var target = new WorldEntity(0x9999, 2, EntityType.Monster)
            {
                Position = new Vector3(100f, 0f, 0f), // East (+X)
                IsSpawned = true
            };
            world.UpsertEntity(target);

            localEnt.Direction = 0; // East
            actionService.SetTarget(target);
            actionService.SetLockOn(true);

            // Strafe Right (E)
            input.SetKeyDown(GordianKey.E);
            controller.Update(TimeSpan.FromSeconds(1.0));

            // Facing East (+X), strafing right moves towards South (+Z)
            Assert.Equal(50, localEnt.Speed);
            Assert.Equal(LocomotionDirection.Right, localEnt.LocomotionDirection);
            Assert.InRange(localEnt.Position.Z, 4.9f, 5.1f);

            // Facing should remain oriented towards target (within ~3 degrees of 0 / East)
            Assert.True(localEnt.Direction is <= 2 or >= 254);
        }

        [Fact]
        public void Update_WhenLockedOn_MoveBackwardMovesAwayAndSetsBackwardLocomotion()
        {
            var (controller, input, world, player, localEnt, actionService) = CreateTestHarnessWithActionService();

            var target = new WorldEntity(0x9999, 2, EntityType.Monster)
            {
                Position = new Vector3(100f, 0f, 0f), // East (+X)
                IsSpawned = true
            };
            world.UpsertEntity(target);

            actionService.SetTarget(target);
            actionService.SetLockOn(true);

            // Move Backward (S)
            input.SetKeyDown(GordianKey.S);
            controller.Update(TimeSpan.FromSeconds(1.0));

            // Moves backward along -X away from target
            Assert.Equal(50, localEnt.Speed);
            Assert.Equal(LocomotionDirection.Backward, localEnt.LocomotionDirection);
            Assert.InRange(localEnt.Position.X, -5.1f, -4.9f);

            // Facing still faces East towards target
            Assert.Equal(0, localEnt.Direction);
        }

        [Fact]
        public void Update_WhenTriggeringToggleLockOn_TogglesLockOn()
        {
            var (controller, input, world, player, localEnt, actionService) = CreateTestHarnessWithActionService();

            var target = new WorldEntity(0x9999, 2, EntityType.Monster)
            {
                Position = new Vector3(10f, 0f, 0f),
                IsSpawned = true
            };
            world.UpsertEntity(target);

            actionService.SetTarget(target);
            Assert.False(actionService.IsLockedOn);

            // Press T (ToggleLockOn)
            input.SetKeyDown(GordianKey.T);
            controller.Update(TimeSpan.FromMilliseconds(16));
            Assert.True(actionService.IsLockedOn);

            // Release and press again
            input.SetKeyUp(GordianKey.T);
            controller.Update(TimeSpan.FromMilliseconds(16));
            input.SetKeyDown(GordianKey.T);
            controller.Update(TimeSpan.FromMilliseconds(16));
            Assert.False(actionService.IsLockedOn);
        }

        [Fact]
        public void Update_WhenCancelTriggeredWhileLockedOn_ClearsLockOnBeforeTarget()
        {
            var (controller, input, world, player, localEnt, actionService) = CreateTestHarnessWithActionService();

            var target = new WorldEntity(0x9999, 2, EntityType.Monster)
            {
                Position = new Vector3(10f, 0f, 0f),
                IsSpawned = true
            };
            world.UpsertEntity(target);

            actionService.SetTarget(target);
            actionService.SetLockOn(true);

            // First Cancel press: clears LockOn but retains CurrentTarget
            input.SetKeyDown(GordianKey.Escape);
            controller.Update(TimeSpan.FromMilliseconds(16));
            Assert.False(actionService.IsLockedOn);
            Assert.Same(target, actionService.CurrentTarget);

            // Second Cancel press: clears CurrentTarget
            input.SetKeyUp(GordianKey.Escape);
            controller.Update(TimeSpan.FromMilliseconds(16));
            input.SetKeyDown(GordianKey.Escape);
            controller.Update(TimeSpan.FromMilliseconds(16));
            Assert.Null(actionService.CurrentTarget);
        }

        [Fact]
        public void Update_WhenLocalPlayerSpeedIsSetToMount_MovesAtMountSpeed()
        {
            var (controller, input, world, player, localEnt) = CreateTestHarness();

            localEnt.Direction = 0;
            localEnt.Position = Vector3.Zero;
            player.SetSpeed(80); // Mount speed: 80 => 8.0 yalms/sec

            input.SetKeyDown(GordianKey.W);
            controller.Update(TimeSpan.FromSeconds(1.0));

            Assert.Equal(80, localEnt.Speed);
            Assert.InRange(localEnt.Position.X, 7.99f, 8.01f);
        }

        [Fact]
        public void Update_WhenSpeedMultiplierApplied_ScalesRunSpeed()
        {
            var (controller, input, world, player, localEnt) = CreateTestHarness();

            localEnt.Direction = 0;
            localEnt.Position = Vector3.Zero;
            controller.SpeedMultiplier = 1.5f; // 50 * 1.5 = 75 => 7.5 yalms/sec

            input.SetKeyDown(GordianKey.W);
            controller.Update(TimeSpan.FromSeconds(1.0));

            Assert.Equal(75, localEnt.Speed);
            Assert.InRange(localEnt.Position.X, 7.49f, 7.51f);
        }

        [Fact]
        public void Update_WhenSpeedOverrideApplied_OverridesRunSpeed()
        {
            var (controller, input, world, player, localEnt) = CreateTestHarness();

            localEnt.Direction = 0;
            localEnt.Position = Vector3.Zero;
            controller.SpeedOverride = 100; // 10.0 yalms/sec

            input.SetKeyDown(GordianKey.W);
            controller.Update(TimeSpan.FromSeconds(1.0));

            Assert.Equal(100, localEnt.Speed);
            Assert.InRange(localEnt.Position.X, 9.99f, 10.01f);
        }

        [Fact]
        public void Update_WhenSpeedOverrideRestricted_IgnoresSpeedTampering()
        {
            var (controller, input, world, player, localEnt, actionService) = CreateTestHarnessWithActionService();

            actionService.Profile.FeatureRestrictions = FeatureRestrictions.SpeedOverride;
            controller.SpeedMultiplier = 2.0f;
            controller.SpeedOverride = 120;

            localEnt.Direction = 0;
            localEnt.Position = Vector3.Zero;

            input.SetKeyDown(GordianKey.W);
            controller.Update(TimeSpan.FromSeconds(1.0));

            // Must strictly adhere to base server speed (50) when SpeedOverride is restricted
            Assert.Equal(50, localEnt.Speed);
            Assert.InRange(localEnt.Position.X, 4.99f, 5.01f);
        }
    }
}
