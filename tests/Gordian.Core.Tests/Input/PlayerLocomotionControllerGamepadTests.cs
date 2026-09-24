// tests/Gordian.Core.Tests/Input/PlayerLocomotionControllerGamepadTests.cs
using System;
using System.Numerics;
using Gordian.Core.Input;
using Gordian.Core.Network;
using Gordian.Core.World;
using Xunit;

namespace Gordian.Core.Tests.Input
{
    public sealed class PlayerLocomotionControllerGamepadTests
    {
        private readonly WorldState _world;
        private readonly LocalPlayerState _localPlayer;
        private readonly InputState _inputState;
        private readonly InputProfile _profile;
        private readonly PlayerLocomotionController _controller;

        public PlayerLocomotionControllerGamepadTests()
        {
            _world = new WorldState();
            _localPlayer = new LocalPlayerState { ServerId = 1001 };
            _inputState = new InputState();
            _profile = InputProfile.CreateGamepadDefault();
            _controller = new PlayerLocomotionController(_inputState, _profile, _world, _localPlayer);

            var ent = new PlayerEntity(1001, 0)
            {
                Position = new Vector3(100f, 200f, 0f),
                Direction = 0, // East (+X)
                Speed = 0,
                IsSpawned = true
            };
            _world.UpsertEntity(ent);
        }

        [Fact]
        public void CameraRelativeLocomotion_FacesAndMovesInCameraForwardDirection()
        {
            // Camera is facing East (Yaw = 0°)
            _controller.CameraYaw = 0.0f;

            // Push LeftStick UP (0, 1) -> should face East (Direction 0) and advance +X
            var padState = new GamepadState(
                isConnected: true,
                buttons: GamepadButton.None,
                leftThumb: new Vector2(0.0f, 1.0f),
                rightThumb: Vector2.Zero,
                leftTrigger: 0f,
                rightTrigger: 0f);

            _inputState.SetGamepadState(padState);
            _controller.Update(TimeSpan.FromSeconds(1.0));

            Assert.True(_world.TryGetByServerId(1001, out var ent));
            Assert.NotNull(ent);
            Assert.Equal(0, ent.Direction); // Facing East
            Assert.Equal(50, ent.Speed);     // 5.0 yalms/sec run
            Assert.Equal(105.0f, ent.Position.X, 2); // 100 + 5.0 = 105
            Assert.Equal(200.0f, ent.Position.Y, 2);
        }

        [Fact]
        public void CameraRelativeLocomotion_RotatesWithCameraYaw()
        {
            // Camera is facing South (wire Yaw = 270°)
            _controller.CameraYaw = 270.0f;

            // Push LeftStick UP (0, 1) -> should face South (wire Direction 192) and advance +Z
            var padState = new GamepadState(
                isConnected: true,
                buttons: GamepadButton.None,
                leftThumb: new Vector2(0.0f, 1.0f),
                rightThumb: Vector2.Zero,
                leftTrigger: 0f,
                rightTrigger: 0f);

            _inputState.SetGamepadState(padState);
            _controller.Update(TimeSpan.FromSeconds(1.0));

            Assert.True(_world.TryGetByServerId(1001, out var ent));
            Assert.NotNull(ent);
            Assert.Equal(192, ent.Direction); // Facing South (192 in FFXI wire byte)
            Assert.Equal(50, ent.Speed);
            Assert.Equal(100.0f, ent.Position.X, 2);
            Assert.Equal(200.0f, ent.Position.Y, 2); // Elevation remains 200.0f
            Assert.Equal(5.0f, ent.Position.Z, 2);   // 0 + 5.0 = 5.0 along South (+Z)
        }

        [Fact]
        public void CameraRelativeLocomotion_StrafeRightMovesToTheCamerasActualScreenRight()
        {
            // The renderer displays entities at a mirrored X coordinate (see EntityRenderer /
            // ViewportCamera), which flips the handedness of "camera right" relative to naive
            // compass intuition: with the camera facing world East (Yaw = 0), the direction
            // that actually renders on the right side of the screen is world North (-Z),
            // i.e. wire Direction 64 - not world South (+Z resp. Direction 192), and definitely not
            // world East/West. This was reported by a player as "pressing right moves left"
            // before the sign of the strafe contribution was fixed.
            _controller.CameraYaw = 0.0f;

            var padState = new GamepadState(
                isConnected: true,
                buttons: GamepadButton.None,
                leftThumb: new Vector2(1.0f, 0.0f),
                rightThumb: Vector2.Zero,
                leftTrigger: 0f,
                rightTrigger: 0f);

            _inputState.SetGamepadState(padState);
            _controller.Update(TimeSpan.FromSeconds(1.0));

            Assert.True(_world.TryGetByServerId(1001, out var ent));
            Assert.NotNull(ent);
            Assert.Equal(64, ent.Direction); // North (-Z) - the camera's true rendered right side
            Assert.Equal(50, ent.Speed);
            Assert.Equal(100.0f, ent.Position.X, 2);
            Assert.Equal(-5.0f, ent.Position.Z, 2);
        }

        [Fact]
        public void CameraRelativeLocomotion_StrafeLeftMovesToTheCamerasActualScreenLeft()
        {
            // Mirror image of the strafe-right case: pressing left must move to the opposite
            // world direction (South/+Z, Direction 192) from pressing right (North/-Z, Direction 64).
            _controller.CameraYaw = 0.0f;

            var padState = new GamepadState(
                isConnected: true,
                buttons: GamepadButton.None,
                leftThumb: new Vector2(-1.0f, 0.0f),
                rightThumb: Vector2.Zero,
                leftTrigger: 0f,
                rightTrigger: 0f);

            _inputState.SetGamepadState(padState);
            _controller.Update(TimeSpan.FromSeconds(1.0));

            Assert.True(_world.TryGetByServerId(1001, out var ent));
            Assert.NotNull(ent);
            Assert.Equal(192, ent.Direction); // South (+Z) - opposite of strafe-right
            Assert.Equal(50, ent.Speed);
            Assert.Equal(100.0f, ent.Position.X, 2);
            Assert.Equal(5.0f, ent.Position.Z, 2);
        }

        [Fact]
        public void AnalogSpeedGating_SwitchesBetweenWalkAndRunBasedOnTiltThreshold()
        {
            _controller.CameraYaw = 0.0f;
            _profile.GamepadSettings.WalkTiltThreshold = 0.55f;

            // 1. Deflection below 0.55 (e.g. 0.40) -> WalkSpeed (25, 2.5 yalms/sec)
            var walkPad = new GamepadState(
                isConnected: true,
                buttons: GamepadButton.None,
                leftThumb: new Vector2(0.0f, 0.40f),
                rightThumb: Vector2.Zero,
                leftTrigger: 0f,
                rightTrigger: 0f);

            _inputState.SetGamepadState(walkPad);
            _controller.Update(TimeSpan.FromSeconds(1.0));

            Assert.True(_world.TryGetByServerId(1001, out var ent));
            Assert.NotNull(ent);
            Assert.Equal(25, ent.Speed); // 2.5 y/s
            Assert.Equal(102.5f, ent.Position.X, 2);

            // 2. Deflection above 0.55 (e.g. 0.90) -> RunSpeed (50, 5.0 yalms/sec)
            var runPad = new GamepadState(
                isConnected: true,
                buttons: GamepadButton.None,
                leftThumb: new Vector2(0.0f, 0.90f),
                rightThumb: Vector2.Zero,
                leftTrigger: 0f,
                rightTrigger: 0f);

            _inputState.SetGamepadState(runPad);
            _controller.Update(TimeSpan.FromSeconds(1.0));

            Assert.Equal(50, ent.Speed);
            Assert.Equal(107.5f, ent.Position.X, 2); // 102.5 + 5.0 = 107.5
        }

        [Fact]
        public void DeadzoneFiltering_KeepsPlayerStationaryWhenStickInsideDeadzone()
        {
            _profile.GamepadSettings.LeftStickDeadzone = 0.20f;

            var stickInsideDeadzone = new GamepadState(
                isConnected: true,
                buttons: GamepadButton.None,
                leftThumb: new Vector2(0.10f, 0.10f), // magnitude = 0.141 < 0.20
                rightThumb: Vector2.Zero,
                leftTrigger: 0f,
                rightTrigger: 0f);

            _inputState.SetGamepadState(stickInsideDeadzone);
            _controller.Update(TimeSpan.FromSeconds(1.0));

            Assert.True(_world.TryGetByServerId(1001, out var ent));
            Assert.NotNull(ent);
            Assert.Equal(0, ent.Speed);
            Assert.Equal(new Vector3(100f, 200f, 0f), ent.Position);
        }

        [Fact]
        public void AnalogRightStick_OrbitsCameraYawAndPitch()
        {
            float initialPitch = _controller.CameraPitch;
            float initialYaw = _controller.CameraYaw;

            // Push Right Stick Right (+X) and Up (+Y)
            var padState = new GamepadState(
                isConnected: true,
                buttons: GamepadButton.None,
                leftThumb: Vector2.Zero,
                rightThumb: new Vector2(1.0f, 1.0f),
                leftTrigger: 0f,
                rightTrigger: 0f);

            _inputState.SetGamepadState(padState);
            _controller.Update(TimeSpan.FromSeconds(1.0));

            // Camera yaw should have increased
            Assert.True(_controller.CameraYaw > initialYaw);
            // Camera pitch should have altered
            Assert.NotEqual(initialPitch, _controller.CameraPitch);
        }

        [Fact]
        public void AnalogRightStick_InvertCameraX_InvertsYawDelta()
        {
            _controller.CameraYaw = 180.0f;
            _profile.GamepadSettings.InvertCameraX = true;

            var padState = new GamepadState(
                isConnected: true,
                buttons: GamepadButton.None,
                leftThumb: Vector2.Zero,
                rightThumb: new Vector2(1.0f, 0.0f),
                leftTrigger: 0f,
                rightTrigger: 0f);

            _inputState.SetGamepadState(padState);
            _controller.Update(TimeSpan.FromSeconds(1.0));

            // With InvertCameraX = true, pushing stick right (+X) should decrease yaw
            Assert.True(_controller.CameraYaw < 180.0f);
        }

        [Fact]
        public void MouseLook_InvertMouseX_InvertsYawDelta()
        {
            _controller.CameraYaw = 180.0f;
            _profile.InvertMouseX = true;

            // Simulate right mouse button held + positive mouse X delta
            _inputState.SetMouseButtonDown(MouseButton.Right);
            _inputState.AddMouseDelta(100.0f, 0.0f);
            _controller.Update(TimeSpan.FromSeconds(1.0));

            // Dragging right normally swings the camera right, which decreases the counter-clockwise
            // wire-convention yaw; with InvertMouseX = true it must increase instead.
            Assert.True(_controller.CameraYaw > 180.0f);
        }

        [Fact]
        public void CharacterRelativeLocomotion_DirectTankMovement()
        {
            _profile.GamepadSettings.LocomotionMode = GamepadLocomotionMode.CharacterRelative;
            Assert.True(_world.TryGetByServerId(1001, out var ent));
            Assert.NotNull(ent);
            ent.Direction = 0; // East (+X)

            // Push stick Y forward (1.0) and stick X zero
            var padState = new GamepadState(
                isConnected: true,
                buttons: GamepadButton.None,
                leftThumb: new Vector2(0.0f, 1.0f),
                rightThumb: Vector2.Zero,
                leftTrigger: 0f,
                rightTrigger: 0f);

            _inputState.SetGamepadState(padState);
            _controller.Update(TimeSpan.FromSeconds(1.0));

            Assert.Equal(50, ent.Speed);
            Assert.Equal(105.0f, ent.Position.X, 2);
        }

        [Fact]
        public void SpeedBuff_ServerSpeedBaseScalesBothControllerAndKeyboardLocomotion()
        {
            Assert.True(_world.TryGetByServerId(1001, out var ent));
            Assert.NotNull(ent);

            // Simulate server transmitting a speed buff (e.g. Flee / Chocobo: SpeedBase = 100 -> 10.0 yalms/sec)
            ent.SpeedBase = 100;
            ent.Position = new Vector3(0f, 0f, 0f);
            ent.Direction = 0; // East (+X)
            _controller.CameraYaw = 0.0f;

            // 1. Controller Run: LeftStick Up (0, 1) -> should move at 10.0 yalms/sec (Speed 100)
            var runPad = new GamepadState(
                isConnected: true,
                buttons: GamepadButton.None,
                leftThumb: new Vector2(0.0f, 1.0f),
                rightThumb: Vector2.Zero,
                leftTrigger: 0f,
                rightTrigger: 0f);

            _inputState.SetGamepadState(runPad);
            _controller.Update(TimeSpan.FromSeconds(1.0));

            Assert.Equal(100, ent.Speed);
            Assert.Equal(10.0f, ent.Position.X, 2);

            // 2. Controller Walk: LeftStick tilt below threshold -> should move at 5.0 yalms/sec (Speed 50)
            var walkPad = new GamepadState(
                isConnected: true,
                buttons: GamepadButton.None,
                leftThumb: new Vector2(0.0f, 0.40f),
                rightThumb: Vector2.Zero,
                leftTrigger: 0f,
                rightTrigger: 0f);

            _inputState.SetGamepadState(walkPad);
            _controller.Update(TimeSpan.FromSeconds(1.0));

            Assert.Equal(50, ent.Speed);
            Assert.Equal(15.0f, ent.Position.X, 2); // 10.0 + 5.0 = 15.0

            // 3. Disconnect pad, use Keyboard MoveForward (W) -> should also run at 10.0 yalms/sec
            _inputState.SetGamepadState(GamepadState.Disconnected);
            _inputState.SetKeyDown(GordianKey.W);
            _controller.Update(TimeSpan.FromSeconds(1.0));

            Assert.Equal(100, ent.Speed);
            Assert.Equal(25.0f, ent.Position.X, 2); // 15.0 + 10.0 = 25.0

            // 4. Keyboard with Walk toggle active -> should walk at 5.0 yalms/sec
            _inputState.IsWalking = true;
            _controller.Update(TimeSpan.FromSeconds(1.0));

            Assert.Equal(50, ent.Speed);
            Assert.Equal(30.0f, ent.Position.X, 2); // 25.0 + 5.0 = 30.0
        }

        [Fact]
        public void SpeedMultiplier_AddonOrCustomMultiplierScalesMovement()
        {
            Assert.True(_world.TryGetByServerId(1001, out var ent));
            Assert.NotNull(ent);

            ent.SpeedBase = 50; // standard 5.0 y/s
            ent.Position = new Vector3(0f, 0f, 0f);
            ent.Direction = 0;
            _controller.CameraYaw = 0.0f;

            // Set client speed multiplier to 2.0x (e.g. from an addon or GM boost)
            _controller.SpeedMultiplier = 2.0f;

            var runPad = new GamepadState(
                isConnected: true,
                buttons: GamepadButton.None,
                leftThumb: new Vector2(0.0f, 1.0f),
                rightThumb: Vector2.Zero,
                leftTrigger: 0f,
                rightTrigger: 0f);

            _inputState.SetGamepadState(runPad);
            _controller.Update(TimeSpan.FromSeconds(1.0));

            Assert.Equal(100, ent.Speed); // 50 * 2.0 = 100 (10.0 y/s)
            Assert.Equal(10.0f, ent.Position.X, 2);
        }

        [Fact]
        public void MultiBox_OnlyPrimary3DRenderingClientReceivesGamepad()
        {
            // Setup two distinct character sessions in the world
            var world1 = new WorldState();
            var local1 = new LocalPlayerState { ServerId = 1001 };
            var ent1 = new PlayerEntity(1001, 0) { Position = Vector3.Zero, Direction = 0, SpeedBase = 50, IsSpawned = true };
            world1.UpsertEntity(ent1);

            var net1 = new SessionNetworkManager("127.0.0.1", 54001) { CurrentState = SessionState.ActiveInWorld };
            var session1 = new CharacterSession("MainTank", 1001, "acc1", net1);
            session1.IsRendering3D = true; // Main 3D client

            var world2 = new WorldState();
            var local2 = new LocalPlayerState { ServerId = 2002 };
            var ent2 = new PlayerEntity(2002, 0) { Position = Vector3.Zero, Direction = 0, SpeedBase = 50, IsSpawned = true };
            world2.UpsertEntity(ent2);

            var net2 = new SessionNetworkManager("127.0.0.1", 54002) { CurrentState = SessionState.ActiveInWorld };
            var session2 = new CharacterSession("AltMage", 2002, "acc2", net2);
            session2.IsRendering3D = false; // Background / headless multi-box client

            // Create controller for session1 with world1 and session2 with world2
            var ctrl1 = new PlayerLocomotionController(session1.InputState, InputProfile.CreateGamepadDefault(), world1, local1);
            var ctrl2 = new PlayerLocomotionController(session2.InputState, InputProfile.CreateGamepadDefault(), world2, local2);

            // Active stick deflection
            var stickPad = new GamepadState(
                isConnected: true,
                buttons: GamepadButton.None,
                leftThumb: new Vector2(0.0f, 1.0f),
                rightThumb: Vector2.Zero,
                leftTrigger: 0f,
                rightTrigger: 0f);

            // Dispatch loop honoring 3D rendering client rule:
            var sessions = new[] { session1, session2 };
            foreach (var s in sessions)
            {
                if (s.IsRendering3D)
                {
                    s.InputState.SetGamepadState(stickPad);
                }
                else
                {
                    s.InputState.SetGamepadState(GamepadState.Disconnected);
                }
            }

            ctrl1.Update(TimeSpan.FromSeconds(1.0));
            ctrl2.Update(TimeSpan.FromSeconds(1.0));

            // Main client (rendering 3D) moved at 5.0 yalms/sec
            Assert.Equal(50, ent1.Speed);
            Assert.Equal(5.0f, ent1.Position.X, 2);

            // Background client (headless) did NOT move at all
            Assert.Equal(0, ent2.Speed);
            Assert.Equal(Vector3.Zero, ent2.Position);
        }

        [Fact]
        public void GamepadPolling_DisabledOrUnfocused_DoesNotMoveEntity()
        {
            Assert.True(_world.TryGetByServerId(1001, out var ent));
            Assert.NotNull(ent);
            ent.Position = Vector3.Zero;
            ent.Direction = 0;

            var stickPad = new GamepadState(
                isConnected: true,
                buttons: GamepadButton.None,
                leftThumb: new Vector2(0.0f, 1.0f),
                rightThumb: Vector2.Zero,
                leftTrigger: 0f,
                rightTrigger: 0f);

            // 1. When GamepadEnabled is false, polled state is Disconnected
            _profile.GamepadSettings.GamepadEnabled = false;
            bool shouldPoll = _profile.GamepadSettings.GamepadEnabled;
            var padState = shouldPoll ? stickPad : GamepadState.Disconnected;
            _inputState.SetGamepadState(padState);
            _controller.Update(TimeSpan.FromSeconds(1.0));

            Assert.Equal(0, ent.Speed);
            Assert.Equal(Vector3.Zero, ent.Position);

            // 2. When GamepadEnabled is true, but Window is unfocused and AlwaysEnableGamepad is false
            _profile.GamepadSettings.GamepadEnabled = true;
            _profile.GamepadSettings.AlwaysEnableGamepad = false;
            bool windowFocused = false;
            shouldPoll = _profile.GamepadSettings.GamepadEnabled && (windowFocused || _profile.GamepadSettings.AlwaysEnableGamepad);
            padState = shouldPoll ? stickPad : GamepadState.Disconnected;
            _inputState.SetGamepadState(padState);
            _controller.Update(TimeSpan.FromSeconds(1.0));

            Assert.Equal(0, ent.Speed);
            Assert.Equal(Vector3.Zero, ent.Position);

            // 3. When Window is unfocused, but AlwaysEnableGamepad is TRUE -> inputs are accepted!
            _profile.GamepadSettings.AlwaysEnableGamepad = true;
            shouldPoll = _profile.GamepadSettings.GamepadEnabled && (windowFocused || _profile.GamepadSettings.AlwaysEnableGamepad);
            padState = shouldPoll ? stickPad : GamepadState.Disconnected;
            _inputState.SetGamepadState(padState);
            _controller.Update(TimeSpan.FromSeconds(1.0));

            Assert.Equal(50, ent.Speed);
            Assert.Equal(5.0f, ent.Position.X, 2);
        }
    }
}
