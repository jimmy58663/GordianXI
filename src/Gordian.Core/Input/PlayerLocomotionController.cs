// src/Gordian.Core/Input/PlayerLocomotionController.cs
// Clean-room player locomotion and camera control loop for GordianXI.
// FFXI coordinate space specifications: 0=East (+X), 64=South (+Z), 128=West (-X), 192=North (-Z).

using System;
using System.Numerics;
using Gordian.Core.Actions;
using Gordian.Core.Config;
using Gordian.Core.World;

namespace Gordian.Core.Input
{
    /// <summary>
    /// Coordinates continuous keyboard and mouse input evaluation, updating player
    /// locomotion, orientation, speed, and 3D camera angles in real time.
    /// </summary>
    public sealed class PlayerLocomotionController
    {
        private readonly InputState _inputState;
        private readonly WorldState _world;
        private readonly LocalPlayerState _localPlayer;
        private readonly PlayerActionService? _actionService;
        private InputProfile _profile;

        // Camera Spherical Angles (in degrees and yalms)
        public float CameraPitch { get; set; } = 15.0f; // degrees (-80 to +80)
        public float CameraYaw { get; set; } = 0.0f;    // degrees (0 to 360)
        public float CameraDistance { get; set; } = 6.0f; // yalms (1.5 to 25.0)

        public InputProfile Profile
        {
            get => _profile;
            set => _profile = value ?? throw new ArgumentNullException(nameof(value));
        }

        public InputState InputState => _inputState;

        public event Action<Vector3, byte, byte>? LocomotionUpdated; // position, direction, speed
        public event Action<float, float, float>? CameraUpdated;     // pitch, yaw, distance

        public PlayerLocomotionController(
            InputState inputState,
            InputProfile profile,
            WorldState world,
            LocalPlayerState localPlayer,
            PlayerActionService? actionService = null)
        {
            _inputState = inputState ?? throw new ArgumentNullException(nameof(inputState));
            _profile = profile ?? throw new ArgumentNullException(nameof(profile));
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _localPlayer = localPlayer ?? throw new ArgumentNullException(nameof(localPlayer));
            _actionService = actionService;
        }

        /// <summary>
        /// Executes a single input evaluation and locomotion update tick.
        /// </summary>
        /// <param name="elapsed">The delta time elapsed since the last update tick.</param>
        public void Update(TimeSpan elapsed)
        {
            if (elapsed <= TimeSpan.Zero) return;

            // 1. Evaluate physical keys and mouse against active profile
            _inputState.Update(_profile, elapsed);

            // 2. Update Camera from keyboard & mouse impulses
            UpdateCamera(elapsed);

            // 3. Update Locomotion (movement, strafing, turning)
            UpdateLocomotion(elapsed);

            // 4. Evaluate Action Triggers (Targeting, Selection)
            UpdateActionTriggers();
        }

        private void UpdateCamera(TimeSpan elapsed)
        {
            float dt = (float)elapsed.TotalSeconds;
            bool cameraChanged = false;

            // Keyboard Camera Controls
            float pitchDelta = 0;
            float yawDelta = 0;
            float zoomDelta = 0;

            if (_inputState.IsActionHeld(InputAction.CameraPitchUp)) pitchDelta -= 60.0f * dt;
            if (_inputState.IsActionHeld(InputAction.CameraPitchDown)) pitchDelta += 60.0f * dt;
            if (_inputState.IsActionHeld(InputAction.CameraYawLeft)) yawDelta -= 120.0f * dt;
            if (_inputState.IsActionHeld(InputAction.CameraYawRight)) yawDelta += 120.0f * dt;
            if (_inputState.IsActionHeld(InputAction.CameraZoomIn)) zoomDelta -= 10.0f * dt;
            if (_inputState.IsActionHeld(InputAction.CameraZoomOut)) zoomDelta += 10.0f * dt;

            // Mouse Look (Right Mouse Drag or raw delta)
            _inputState.ConsumeMouseDeltas(out float mouseDx, out float mouseDy, out float mouseWheel);
            if (mouseDx != 0 || mouseDy != 0)
            {
                yawDelta += mouseDx * _profile.MouseSensitivityX * 0.15f;
                float my = mouseDy * _profile.MouseSensitivityY * 0.15f;
                pitchDelta += _profile.InvertMouseY ? -my : my;
            }

            if (mouseWheel != 0)
            {
                zoomDelta -= mouseWheel * _profile.MouseWheelZoomStep;
            }

            // Reset Camera shortcut
            if (_inputState.WasActionTriggered(InputAction.ResetCamera))
            {
                if (_localPlayer.ServerId != 0 && _world.TryGetByServerId(_localPlayer.ServerId, out var localEnt) && localEnt != null)
                {
                    CameraYaw = (localEnt.Direction / 256.0f) * 360.0f;
                }
                CameraPitch = 15.0f;
                cameraChanged = true;
            }

            if (pitchDelta != 0 || yawDelta != 0 || zoomDelta != 0)
            {
                CameraPitch = Math.Clamp(CameraPitch + pitchDelta, -80.0f, 80.0f);
                CameraYaw = NormalizeDegrees(CameraYaw + yawDelta);
                CameraDistance = Math.Clamp(CameraDistance + zoomDelta, 1.5f, 25.0f);
                cameraChanged = true;
            }

            if (cameraChanged)
            {
                CameraUpdated?.Invoke(CameraPitch, CameraYaw, CameraDistance);
            }
        }

        private void UpdateLocomotion(TimeSpan elapsed)
        {
            if (_localPlayer.ServerId == 0) return;
            if (!_world.TryGetByServerId(_localPlayer.ServerId, out var localEnt) || localEnt == null)
            {
                localEnt = new PlayerEntity(_localPlayer.ServerId, 0)
                {
                    IsSpawned = true
                };
                _world.UpsertEntity(localEnt);
            }

            float dt = (float)elapsed.TotalSeconds;

            // 1. Determine Forward/Backward intent
            float forwardInput = 0;
            if (_inputState.IsActionHeld(InputAction.MoveForward) || _inputState.AutorunActive)
            {
                forwardInput += 1.0f;
            }
            if (_inputState.IsActionHeld(InputAction.MoveBackward))
            {
                forwardInput -= 1.0f;
            }

            // 2. Determine Turn and Strafe intent
            float turnInput = 0;
            if (_inputState.IsActionHeld(InputAction.TurnLeft)) turnInput -= 1.0f;
            if (_inputState.IsActionHeld(InputAction.TurnRight)) turnInput += 1.0f;

            float strafeInput = 0;
            if (_inputState.IsActionHeld(InputAction.StrafeLeft)) strafeInput -= 1.0f;
            if (_inputState.IsActionHeld(InputAction.StrafeRight)) strafeInput += 1.0f;

            // 3. Update Heading if turning
            if (turnInput != 0)
            {
                float headingDeg = (localEnt.Direction / 256.0f) * 360.0f;
                headingDeg = NormalizeDegrees(headingDeg + (turnInput * _profile.TurnSpeedDegreesPerSec * dt));
                localEnt.Direction = (byte)Math.Round((headingDeg / 360.0f) * 256.0f);
            }

            // 4. Calculate displacement
            bool isMoving = forwardInput != 0 || strafeInput != 0;
            byte currentSpeed = 0;

            if (isMoving)
            {
                currentSpeed = _inputState.IsWalking ? _profile.WalkSpeed : _profile.RunSpeed;
                localEnt.Speed = currentSpeed;

                float speedYalmsPerSec = currentSpeed * 0.1f;
                float distance = speedYalmsPerSec * dt;

                // Normalize diagonal movement
                if (forwardInput != 0 && strafeInput != 0)
                {
                    distance /= MathF.Sqrt(2.0f);
                }

                float headingRad = localEnt.HeadingRadians;

                // FFXI coordinate math:
                // Heading 0 = East (+X), 64 = South (+Y), 128 = West (-X), 192 = North (-Y)
                // Forward vector = (cos(theta), sin(theta))
                // Strafe right vector = (-sin(theta), cos(theta))
                float dx = (MathF.Cos(headingRad) * forwardInput - MathF.Sin(headingRad) * strafeInput) * distance;
                float dy = (MathF.Sin(headingRad) * forwardInput + MathF.Cos(headingRad) * strafeInput) * distance;

                localEnt.Position = new Vector3(localEnt.Position.X + dx, localEnt.Position.Y + dy, localEnt.Position.Z);
            }
            else
            {
                localEnt.Speed = 0;
            }

            LocomotionUpdated?.Invoke(localEnt.Position, localEnt.Direction, localEnt.Speed);
        }

        private void UpdateActionTriggers()
        {
            if (_actionService == null) return;

            // Target Nearest
            if (_inputState.WasActionTriggered(InputAction.TargetNearest))
            {
                if (_localPlayer.ServerId != 0 && _world.TryGetByServerId(_localPlayer.ServerId, out var localEnt) && localEnt != null)
                {
                    var candidates = _world.GetEntitiesInRadius(localEnt.Position, 50.0f);
                    WorldEntity? nearest = null;
                    float nearestDistSq = float.MaxValue;

                    foreach (var candidate in candidates)
                    {
                        if (candidate.ServerId == _localPlayer.ServerId || !candidate.IsSpawned) continue;
                        float distSq = Vector3.DistanceSquared(localEnt.Position, candidate.Position);
                        if (distSq < nearestDistSq)
                        {
                            nearestDistSq = distSq;
                            nearest = candidate;
                        }
                    }

                    if (nearest != null)
                    {
                        _actionService.SetTarget(nearest);
                    }
                }
            }

            // Target Self
            if (_inputState.WasActionTriggered(InputAction.TargetSelf))
            {
                _actionService.SetTargetByServerId(_localPlayer.ServerId);
            }

            // Cancel / Clear Target
            if (_inputState.WasActionTriggered(InputAction.Cancel))
            {
                if (_actionService.CurrentTarget != null)
                {
                    _actionService.ClearTarget();
                }
            }
        }

        private static float NormalizeDegrees(float deg)
        {
            deg %= 360.0f;
            if (deg < 0) deg += 360.0f;
            return deg;
        }
    }
}
