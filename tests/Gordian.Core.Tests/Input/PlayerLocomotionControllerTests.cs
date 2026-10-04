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
        public void Update_WhenFacingNorth_MovesPlayerAlongZAxisAndKeepsElevationConstant()
        {
            var (controller, input, world, player, localEnt) = CreateTestHarness();

            // W is camera-relative (see Update_WhenHoldingTurnRight_... below): facing follows the
            // camera, not whatever the character happened to already be facing. Point the camera
            // North (wire heading 192 = 270 degrees) so pressing W faces and moves that way.
            controller.CameraYaw = 270.0f;
            localEnt.Direction = 192;
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
            // facing world East, the right side of the screen is world South (wire Direction 64).
            Assert.Equal(64, localEnt.Direction);
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

            Assert.Equal(192, localEnt.Direction);
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
        public void Update_WhenHoldingStrafeRight_MovesToTheSameSideAsCameraRelativeRight()
        {
            var (controller, input, world, player, localEnt) = CreateTestHarness();

            localEnt.Direction = 0;
            localEnt.Position = Vector3.Zero;
            controller.CameraYaw = 0.0f;

            // E strafes without turning; it must land on the side D (camera-relative right) runs to: -Z.
            input.SetKeyDown(GordianKey.E);
            controller.Update(TimeSpan.FromSeconds(1.0));

            Assert.Equal(0, localEnt.Direction);
            Assert.Equal(LocomotionDirection.Right, localEnt.LocomotionDirection);
            Assert.InRange(localEnt.Position.X, -0.01f, 0.01f);
            Assert.InRange(localEnt.Position.Z, -5.01f, -4.99f);
        }

        [Fact]
        public void Update_WhenHoldingForwardAndLeft_RunsInACircleToTheLeft()
        {
            var (controller, input, world, player, localEnt) = CreateTestHarness();

            localEnt.Direction = 0;
            localEnt.Position = Vector3.Zero;
            controller.CameraYaw = 0.0f;

            input.SetKeyDown(GordianKey.W);
            input.SetKeyDown(GordianKey.A);

            // Retail reference: a held W+A run completes a full circle in about 11.75 seconds (60 Hz frames).
            float totalTurnDeg = 0f;
            float maxDistance = 0f;
            float previousDeg = 0f;
            for (int frame = 0; frame < (int)(11.75f * 60); frame++)
            {
                controller.Update(TimeSpan.FromSeconds(1.0 / 60.0));
                float headingDeg = localEnt.Direction / 256.0f * 360.0f;
                float step = headingDeg - previousDeg;
                if (step > 180f) step -= 360f;
                if (step < -180f) step += 360f;
                totalTurnDeg += step;
                previousDeg = headingDeg;
                maxDistance = MathF.Max(maxDistance, new Vector2(localEnt.Position.X, localEnt.Position.Z).Length());
            }

            // The camera swings in behind the character, so the camera-relative 45-degree offset keeps turning it left
            // (decreasing heading). Beyond the initial 45-degree turn, it sweeps one full circle and arrives back at the
            // start, having reached the far side of a circle about 19 yalms across.
            Assert.InRange(totalTurnDeg + 45f, -380f, -340f);
            float endDistance = new Vector2(localEnt.Position.X, localEnt.Position.Z).Length();
            Assert.True(endDistance < 2.0f, $"Ended {endDistance:F1} yalms from the start");
            Assert.InRange(maxDistance, 16f, 22f);
        }

        [Fact]
        public void Update_WhenHoldingForward_TurnsTowardCameraGraduallyInsteadOfSnapping()
        {
            var (controller, input, world, player, localEnt) = CreateTestHarness();

            localEnt.Direction = 128; // Facing the camera
            localEnt.Position = Vector3.Zero;
            controller.CameraYaw = 0.0f;

            input.SetKeyDown(GordianKey.W);
            controller.Update(TimeSpan.FromSeconds(1.0 / 60.0));

            // One frame at 720 degrees/sec turns 12 degrees (about 8.5 heading steps), not the full half turn.
            int turned = Math.Abs(localEnt.Direction - 128);
            Assert.InRange(turned, 7, 10);
        }

        [Fact]
        public void Update_ResetCamera_AlignsWithPlayerHeading()
        {
            var (controller, input, world, player, localEnt) = CreateTestHarness();

            // Set player facing South (wire 64 = 90 degrees)
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

        private static (WorldEntity left, WorldEntity right) PlaceTwoMobs(PlayerLocomotionController controller, WorldState world)
        {
            var leftMob = new WorldEntity(0x30, 0x30, EntityType.Monster) { Name = "Left", Position = TargetCyclingTests.InView(controller.Camera, Vector3.Zero, 10, -4), IsSpawned = true };
            var rightMob = new WorldEntity(0x31, 0x31, EntityType.Monster) { Name = "Right", Position = TargetCyclingTests.InView(controller.Camera, Vector3.Zero, 30, 6), IsSpawned = true };
            world.UpsertEntity(leftMob);
            world.UpsertEntity(rightMob);
            return (leftMob, rightMob);
        }

        private static GamepadState Pad(GamepadButton buttons) => new(true, buttons, Vector2.Zero, Vector2.Zero, 0f, 0f, 1);

        [Fact]
        public void Update_TabWithNoTarget_TargetsClosest()
        {
            var (controller, input, world, _, _, actionService) = CreateTestHarnessWithActionService();
            controller.Update(TimeSpan.FromMilliseconds(16));
            PlaceTwoMobs(controller, world);

            input.SetKeyDown(GordianKey.Tab);
            controller.Update(TimeSpan.FromMilliseconds(16));

            Assert.Equal(0x30u, actionService.CurrentTarget?.ServerId);
        }

        [Fact]
        public void Update_TabFromLeftMob_SkipsSelfToTheRightMob()
        {
            var (controller, input, world, _, _, actionService) = CreateTestHarnessWithActionService();
            controller.Update(TimeSpan.FromMilliseconds(16));
            var (leftMob, _) = PlaceTwoMobs(controller, world);
            actionService.SetTarget(leftMob);

            input.SetKeyDown(GordianKey.Tab);
            controller.Update(TimeSpan.FromMilliseconds(16));

            Assert.Equal(0x31u, actionService.CurrentTarget?.ServerId);
        }

        [Fact]
        public void Update_KeyboardConfirmWithNoTarget_DoesNotTarget()
        {
            var (controller, input, world, _, _, actionService) = CreateTestHarnessWithActionService();
            controller.Update(TimeSpan.FromMilliseconds(16));
            PlaceTwoMobs(controller, world);

            input.SetKeyDown(GordianKey.Space);
            controller.Update(TimeSpan.FromMilliseconds(16));

            Assert.Null(actionService.CurrentTarget);
        }

        [Fact]
        public void Update_GamepadAWithNoTarget_TargetsClosest()
        {
            var (controller, input, world, _, _, actionService) = CreateTestHarnessWithActionService();
            controller.Update(TimeSpan.FromMilliseconds(16));
            PlaceTwoMobs(controller, world);

            input.SetGamepadState(Pad(GamepadButton.A));
            controller.Update(TimeSpan.FromMilliseconds(16));

            Assert.Equal(0x30u, actionService.CurrentTarget?.ServerId);
        }

        [Fact]
        public void Update_DPadWithNoTarget_TargetsSelf_ThenStepsFromSelf()
        {
            var (controller, input, world, player, _, actionService) = CreateTestHarnessWithActionService();
            controller.Update(TimeSpan.FromMilliseconds(16));
            PlaceTwoMobs(controller, world);

            input.SetGamepadState(Pad(GamepadButton.DPadRight));
            controller.Update(TimeSpan.FromMilliseconds(16));
            Assert.Equal(player.ServerId, actionService.CurrentTarget?.ServerId);

            input.SetGamepadState(Pad(GamepadButton.None));
            controller.Update(TimeSpan.FromMilliseconds(16));
            input.SetGamepadState(Pad(GamepadButton.DPadLeft));
            controller.Update(TimeSpan.FromMilliseconds(16));
            Assert.Equal(0x30u, actionService.CurrentTarget?.ServerId);
        }

        // Retail recording 2026-10-03 (docs/input/console-and-input.md): lock-on neither turns the character nor
        // moves the camera; the character runs in its input direction and the camera stays where the player put it.
        private (PlayerLocomotionController c, InputState i, WorldState w, WorldEntity me, PlayerActionService a, WorldEntity mob) EngagedHarness(bool lockOn)
        {
            var (controller, input, world, player, localEnt, actionService) = CreateTestHarnessWithActionService();
            var target = new WorldEntity(0x9999, 2, EntityType.Monster) { Position = new Vector3(0f, 0f, 10f), IsSpawned = true };
            world.UpsertEntity(target);
            localEnt.Direction = 0;
            actionService.SetTarget(target);
            actionService.UiSettings.SetValue(Gordian.Core.Ui.StockUiSettingKey.AutoLockOnEngage, 0);
            actionService.Combat!.Engage(target.ServerId, target.TargetIndex);
            if (lockOn) actionService.SetLockOn(true);
            return (controller, input, world, localEnt, actionService, target);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Update_WhenEngagedWithOrWithoutLockOn_DoesNotTurnTowardTarget(bool lockOn)
        {
            var (controller, input, world, me, actionService, mob) = EngagedHarness(lockOn);
            for (int i = 0; i < 10; i++) controller.Update(TimeSpan.FromMilliseconds(16));
            Assert.Equal(0, me.Direction);
        }

        [Fact]
        public void Update_WhenEngagedAndLockedOnAndMoving_RunsInInputDirectionWithCameraUntouched()
        {
            var (controller, input, world, me, actionService, mob) = EngagedHarness(lockOn: true);
            controller.Update(TimeSpan.FromMilliseconds(16));
            float yaw = controller.CameraYaw;

            input.SetKeyDown(GordianKey.W);
            for (int i = 0; i < 30; i++) controller.Update(TimeSpan.FromMilliseconds(33));

            Assert.True(actionService.IsLockedOn);
            Assert.Equal(LocomotionDirection.Forward, me.LocomotionDirection);
            Assert.True(me.Position.Z < 1f, "Runs along the camera-relative heading, not toward the target at +Z");
            Assert.Equal(yaw, controller.CameraYaw, 3);
        }

        [Fact]
        public void Update_WhenLockedOn_DisengagingKeepsPlayersLockChoice()
        {
            var (controller, input, world, me, actionService, mob) = EngagedHarness(lockOn: true);
            actionService.Combat!.Disengage();
            Assert.True(actionService.IsLockedOn);
        }

        [Theory]
        [InlineData(GordianKey.T)]
        [InlineData(GordianKey.NumPadMultiply)]
        public void Update_LockOnKeyTogglesLock_EvenWithAStockMenuOpen(GordianKey key)
        {
            var (controller, input, world, me, actionService, mob) = EngagedHarness(lockOn: false);
            actionService.Menus.Library = Gordian.Core.Tests.Ui.StockUiMenuControllerTests.SyntheticLibrary();
            Assert.True(actionService.Menus.OpenMainMenu());
            Assert.True(actionService.Menus.IsOpen);
            controller.Update(TimeSpan.FromMilliseconds(16));

            input.SetKeyDown(key);
            controller.Update(TimeSpan.FromMilliseconds(16));
            input.SetKeyUp(key);
            controller.Update(TimeSpan.FromMilliseconds(16));
            Assert.True(actionService.IsLockedOn);

            input.SetKeyDown(key);
            controller.Update(TimeSpan.FromMilliseconds(16));
            input.SetKeyUp(key);
            controller.Update(TimeSpan.FromMilliseconds(16));
            Assert.False(actionService.IsLockedOn);
        }

        [Fact]
        public void Engage_WithAutoLockOnEngageDefault_LocksOnWithoutTurning()
        {
            var (controller, input, world, player, localEnt, actionService) = CreateTestHarnessWithActionService();
            var target = new WorldEntity(0x9999, 2, EntityType.Monster) { Position = new Vector3(0f, 0f, 10f), IsSpawned = true };
            world.UpsertEntity(target);
            localEnt.Direction = 0;
            actionService.SetTarget(target);
            Assert.True(actionService.UiSettings.IsOn(Gordian.Core.Ui.StockUiSettingKey.AutoLockOnEngage));

            actionService.Combat!.Engage(target.ServerId, target.TargetIndex);
            Assert.True(actionService.IsLockedOn);
            for (int i = 0; i < 10; i++) controller.Update(TimeSpan.FromMilliseconds(16));
            Assert.Equal(0, localEnt.Direction);
        }

        [Fact]
        public void Engage_WithAutoLockOnEngageOff_DoesNotLockOn()
        {
            var (controller, input, world, player, localEnt, actionService) = CreateTestHarnessWithActionService();
            var target = new WorldEntity(0x9999, 2, EntityType.Monster) { Position = new Vector3(0f, 0f, 10f), IsSpawned = true };
            world.UpsertEntity(target);
            actionService.SetTarget(target);
            actionService.UiSettings.SetValue(Gordian.Core.Ui.StockUiSettingKey.AutoLockOnEngage, 0);
            actionService.Combat!.Engage(target.ServerId, target.TargetIndex);
            Assert.False(actionService.IsLockedOn);
        }

        [Fact]
        public void ToggleLockOn_EngagedByServerWithNothingSelected_LocksOntoTheFight()
        {
            var (controller, input, world, player, localEnt, actionService) = CreateTestHarnessWithActionService();
            var target = new WorldEntity(0x9999, 2, EntityType.Monster) { Position = new Vector3(0f, 0f, 10f), IsSpawned = true };
            world.UpsertEntity(target);
            actionService.UiSettings.SetValue(Gordian.Core.Ui.StockUiSettingKey.AutoLockOnEngage, 0);
            actionService.Combat!.Engage(target.ServerId, target.TargetIndex);
            Assert.Null(actionService.CurrentTarget);

            actionService.ToggleLockOn();

            Assert.True(actionService.IsLockedOn);
            Assert.Same(target, actionService.CurrentTarget);
        }

        [Fact]
        public void FromJson_ProfileWithoutLockOnKeys_GetsTheDefaults()
        {
            var p = InputProfile.CreateCompact();
            p.Bindings.Remove(InputAction.ToggleLockOn);
            var loaded = InputProfile.FromJson(p.SaveToJson());
            Assert.True(loaded.TryGetAction(new InputChord(GordianKey.T), out var a) && a == InputAction.ToggleLockOn);
            Assert.True(loaded.TryGetAction(new InputChord(GordianKey.NumPadMultiply), out var b) && b == InputAction.ToggleLockOn);
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
