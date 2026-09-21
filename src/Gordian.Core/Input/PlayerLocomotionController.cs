// src/Gordian.Core/Input/PlayerLocomotionController.cs
// Clean-room player locomotion and camera control loop for GordianXI.
// FFXI coordinate space specifications: 0=East (+X), 64=South (+Z), 128=West (-X), 192=North (-Z).

using System;
using System.Numerics;
using Gordian.Core.Actions;
using Gordian.Core.Config;
using Gordian.Core.Graphics;
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
        private readonly ViewportCamera _camera = new();
        private InputProfile _profile;

        public ViewportCamera Camera => _camera;

        public CameraMode CameraMode
        {
            get => _camera.Mode;
            set => _camera.Mode = value;
        }

        public void ToggleCameraMode()
        {
            CameraMode = CameraMode switch
            {
                CameraMode.ThirdPersonOrbital => CameraMode.FirstPerson,
                CameraMode.FirstPerson => CameraMode.FreeCam,
                CameraMode.FreeCam => CameraMode.ThirdPersonOrbital,
                _ => CameraMode.ThirdPersonOrbital
            };
        }

        public void ToggleFreeCam()
        {
            CameraMode = CameraMode == CameraMode.FreeCam
                ? CameraMode.ThirdPersonOrbital
                : CameraMode.FreeCam;
        }

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

        /// <summary>
        /// Optional client-side speed multiplier (e.g. set by addons, GM commands, or custom modes). Default is 1.0f.
        /// </summary>
        public float SpeedMultiplier { get; set; } = 1.0f;

        /// <summary>
        /// Calculates the effective run speed for the local player entity.
        /// Respects server-transmitted speed buffs (Flee, Chocobo, equipment mods in SpeedBase)
        /// and client-side profile or addon speed multipliers.
        /// </summary>
        public byte GetEffectiveRunSpeed(WorldEntity? localEnt)
        {
            byte baseRun = (localEnt != null && localEnt.SpeedBase > 0) ? localEnt.SpeedBase : _profile.RunSpeed;
            if (Math.Abs(SpeedMultiplier - 1.0f) > 0.001f)
            {
                return (byte)Math.Clamp((int)MathF.Round(baseRun * SpeedMultiplier), 1, 255);
            }
            return baseRun;
        }

        /// <summary>
        /// Calculates the effective walk speed for the local player entity.
        /// Scales proportionally with the effective run speed (retail 50% walk ratio).
        /// </summary>
        public byte GetEffectiveWalkSpeed(WorldEntity? localEnt)
        {
            byte runSpeed = GetEffectiveRunSpeed(localEnt);
            return (byte)Math.Max(1, runSpeed / 2);
        }

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

            // Mouse Look (Right Mouse Drag or raw delta) and Mouse Wheel Zoom.
            // VeldridViewportControl's own OnPointerMoved/OnPointerWheelChanged overrides never
            // actually fire on Windows: the viewport surface is a real native Win32 child window
            // (see Win32ChildWindowHelper), so the OS delivers its mouse messages directly to that
            // child HWND, not through Avalonia's routed-event tree. The only reliable path is via
            // this InputState bus, fed by the viewport window's own pointer handlers.
            _inputState.ConsumeMouseDeltas(out float mouseDx, out float mouseDy, out float mouseWheel);
            if (mouseWheel != 0)
            {
                zoomDelta -= mouseWheel * _profile.MouseWheelZoomStep;
            }
            if (mouseDx != 0 || mouseDy != 0)
            {
                float mx = mouseDx * _profile.MouseSensitivityX * 0.15f;
                yawDelta += _profile.InvertMouseX ? -mx : mx;
                float my = mouseDy * _profile.MouseSensitivityY * 0.15f;
                pitchDelta += _profile.InvertMouseY ? -my : my;
            }

            // Gamepad Analog Camera Look (Right Thumbstick)
            var pad = _inputState.CurrentGamepad;
            if (pad.IsConnected)
            {
                var padSettings = _profile.GamepadSettings ?? new GamepadSettings();
                var filteredRightStick = GamepadState.ApplyRadialDeadzone(pad.RightThumb, padSettings.RightStickDeadzone);
                if (filteredRightStick != Vector2.Zero)
                {
                    float padYaw = filteredRightStick.X * 180.0f * padSettings.CameraSensitivityX * dt;
                    if (padSettings.InvertCameraX) padYaw = -padYaw;
                    yawDelta += padYaw;

                    float padPitch = -filteredRightStick.Y * 120.0f * padSettings.CameraSensitivityY * dt;
                    if (padSettings.InvertCameraY) padPitch = -padPitch;
                    pitchDelta += padPitch;
                }
            }

            // Mode toggling shortcuts
            if (_inputState.WasActionTriggered(InputAction.ToggleCameraMode))
            {
                ToggleCameraMode();
                cameraChanged = true;
            }

            if (_inputState.WasActionTriggered(InputAction.ToggleFreeCam))
            {
                ToggleFreeCam();
                cameraChanged = true;
            }

            // FreeCam Handling
            if (_camera.Mode == CameraMode.FreeCam)
            {
                float flySpeed = _inputState.IsWalking ? 6.0f : 18.0f; // yalms/sec
                var moveDelta = Vector3.Zero;
                if (_inputState.IsActionHeld(InputAction.MoveForward)) moveDelta.Z += flySpeed * dt;
                if (_inputState.IsActionHeld(InputAction.MoveBackward)) moveDelta.Z -= flySpeed * dt;
                if (_inputState.IsActionHeld(InputAction.StrafeLeft) || _inputState.IsActionHeld(InputAction.TurnLeft)) moveDelta.X -= flySpeed * dt;
                if (_inputState.IsActionHeld(InputAction.StrafeRight) || _inputState.IsActionHeld(InputAction.TurnRight)) moveDelta.X += flySpeed * dt;

                if (pad.IsConnected)
                {
                    var padSettings = _profile.GamepadSettings ?? new GamepadSettings();
                    var leftStick = GamepadState.ApplyRadialDeadzone(pad.LeftThumb, padSettings.LeftStickDeadzone);
                    moveDelta.X += leftStick.X * flySpeed * dt;
                    moveDelta.Z += leftStick.Y * flySpeed * dt;
                }

                _camera.MoveFreeCam(moveDelta, pitchDelta, yawDelta);
                CameraPitch = _camera.Pitch;
                CameraYaw = _camera.Yaw;
                CameraUpdated?.Invoke(CameraPitch, CameraYaw, CameraDistance);
                return;
            }

            // Reset Camera shortcut
            if (_inputState.WasActionTriggered(InputAction.ResetCamera))
            {
                uint localId = GetOrResolveLocalServerId();
                if (localId != 0 && _world.TryGetByServerId(localId, out var localEnt) && localEnt != null)
                {
                    CameraYaw = (localEnt.Direction / 256.0f) * 360.0f;
                }
                CameraPitch = 15.0f;
                cameraChanged = true;
            }

            if (pitchDelta != 0 || yawDelta != 0 || zoomDelta != 0)
            {
                float minPitch = _camera.Mode == CameraMode.ThirdPersonOrbital ? -15.0f : -80.0f;
                CameraPitch = Math.Clamp(CameraPitch + pitchDelta, minPitch, 80.0f);
                CameraYaw = NormalizeDegrees(CameraYaw + yawDelta);

                // Smooth First-Person / Orbital transition on zoom
                if (_camera.Mode == CameraMode.FirstPerson && zoomDelta > 0)
                {
                    _camera.Mode = CameraMode.ThirdPersonOrbital;
                    CameraDistance = 2.0f;
                }
                else if (_camera.Mode == CameraMode.ThirdPersonOrbital && CameraDistance <= 1.6f && zoomDelta < 0)
                {
                    _camera.Mode = CameraMode.FirstPerson;
                    CameraDistance = 0.5f;
                }
                else
                {
                    CameraDistance = Math.Clamp(CameraDistance + zoomDelta, 0.5f, 30.0f);
                }

                cameraChanged = true;
            }
            else if (_camera.Mode == CameraMode.ThirdPersonOrbital)
            {
                // Smooth camera tracking to keep locked-on target in view
                WorldEntity? lockTgt = null;
                if (_actionService != null && (_actionService.IsLockedOn || (_actionService.Combat?.IsEngaged ?? false)))
                {
                    lockTgt = _actionService.CurrentTarget;
                    if (lockTgt == null && _actionService.Combat != null && _actionService.Combat.TargetServerId != 0)
                    {
                        _world.TryGetByServerId(_actionService.Combat.TargetServerId, out lockTgt);
                    }
                }

                if (lockTgt != null && lockTgt.IsSpawned)
                {
                    uint localId = GetOrResolveLocalServerId();
                    if (localId != 0 && _world.TryGetByServerId(localId, out var localEnt) && localEnt != null)
                    {
                        float toTgtX = lockTgt.Position.X - localEnt.Position.X;
                        float toTgtZ = lockTgt.Position.Z - localEnt.Position.Z;
                        if ((toTgtX * toTgtX) + (toTgtZ * toTgtZ) > 0.001f)
                        {
                            float targetHeadingDeg = (localEnt.Direction / 256.0f) * 360.0f;
                            float yawDiff = targetHeadingDeg - CameraYaw;
                            while (yawDiff > 180.0f) yawDiff -= 360.0f;
                            while (yawDiff < -180.0f) yawDiff += 360.0f;
                            if (MathF.Abs(yawDiff) > 0.1f)
                            {
                                CameraYaw = NormalizeDegrees(CameraYaw + (yawDiff * MathF.Min(1.0f, dt * 5.0f)));
                                cameraChanged = true;
                            }
                        }
                    }
                }
            }

            // Update underlying ViewportCamera matrices and frustum
            var targetPos = Vector3.Zero;
            uint targetServerId = GetOrResolveLocalServerId();
            if (targetServerId != 0 && _world.TryGetByServerId(targetServerId, out var targetEnt) && targetEnt != null)
            {
                targetPos = targetEnt.Position;
            }
            _camera.Update(targetPos, CameraPitch, CameraYaw, CameraDistance, _camera.AspectRatio);

            if (cameraChanged)
            {
                CameraUpdated?.Invoke(CameraPitch, CameraYaw, CameraDistance);
            }
        }

        private uint GetOrResolveLocalServerId()
        {
            if (_localPlayer.ServerId != 0) return _localPlayer.ServerId;

            foreach (var ent in _world.Entities)
            {
                if (ent.Type == EntityType.Player)
                {
                    _localPlayer.ServerId = ent.ServerId;
                    return ent.ServerId;
                }
            }
            return 0;
        }

        private void UpdateLocomotion(TimeSpan elapsed)
        {
            if (_camera.Mode == CameraMode.FreeCam)
            {
                // In FreeCam, player entity remains stationary while camera flies
                return;
            }

            uint localServerId = GetOrResolveLocalServerId();
            if (localServerId == 0) return;
            if (!_world.TryGetByServerId(localServerId, out var localEnt) || localEnt == null)
            {
                localEnt = new PlayerEntity(localServerId, 0)
                {
                    IsSpawned = true
                };
                _world.UpsertEntity(localEnt);
            }

            float dt = (float)elapsed.TotalSeconds;

            // Check gamepad analog left stick
            var pad = _inputState.CurrentGamepad;
            var padSettings = _profile.GamepadSettings ?? new GamepadSettings();
            Vector2 leftStick = pad.IsConnected
                ? GamepadState.ApplyRadialDeadzone(pad.LeftThumb, padSettings.LeftStickDeadzone)
                : Vector2.Zero;

            // Lock-On Locomotion:
            // When locked onto a target, the character continuously faces the target directly.
            // Locomotion moves the character forward/backward or strafes left/right relative to the target line,
            // assigning LocomotionDirection accordingly without rotating character away from target.
            WorldEntity? lockTgt = null;
            if (_actionService != null && (_actionService.IsLockedOn || (_actionService.Combat?.IsEngaged ?? false)))
            {
                lockTgt = _actionService.CurrentTarget;
                if (lockTgt == null && _actionService.Combat != null && _actionService.Combat.TargetServerId != 0)
                {
                    _world.TryGetByServerId(_actionService.Combat.TargetServerId, out lockTgt);
                }
            }

            if (lockTgt != null && lockTgt.IsSpawned)
            {
                float toTgtX = lockTgt.Position.X - localEnt.Position.X;
                float toTgtZ = lockTgt.Position.Z - localEnt.Position.Z;
                float distSq = (toTgtX * toTgtX) + (toTgtZ * toTgtZ);
                if (distSq > 0.0001f)
                {
                    float toTargetRad = MathF.Atan2(toTgtZ, toTgtX);
                    if (toTargetRad < 0f) toTargetRad += MathF.PI * 2.0f;
                    localEnt.Direction = (byte)Math.Round((toTargetRad / (MathF.PI * 2.0f)) * 256.0f);
                    localEnt.RenderHeadingRadians = toTargetRad;
                }

                float lockFwd = 0f;
                if (_inputState.IsActionHeld(InputAction.MoveForward) || _inputState.AutorunActive) lockFwd += 1.0f;
                if (_inputState.IsActionHeld(InputAction.MoveBackward)) lockFwd -= 1.0f;

                float lockStrafe = 0f;
                if (_inputState.IsActionHeld(InputAction.StrafeRight) || _inputState.IsActionHeld(InputAction.TurnRight)) lockStrafe += 1.0f;
                if (_inputState.IsActionHeld(InputAction.StrafeLeft) || _inputState.IsActionHeld(InputAction.TurnLeft)) lockStrafe -= 1.0f;

                if (leftStick != Vector2.Zero)
                {
                    lockFwd += leftStick.Y;
                    lockStrafe += leftStick.X;
                }

                float inputLen = MathF.Sqrt(lockFwd * lockFwd + lockStrafe * lockStrafe);
                if (inputLen > 0.001f)
                {
                    if (inputLen > 1.0f)
                    {
                        lockFwd /= inputLen;
                        lockStrafe /= inputLen;
                    }

                    float inputAngle = MathF.Atan2(lockStrafe, lockFwd);
                    if (MathF.Abs(inputAngle) <= (MathF.PI / 4.0f))
                    {
                        localEnt.LocomotionDirection = LocomotionDirection.Forward;
                    }
                    else if (MathF.Abs(inputAngle) >= (3.0f * MathF.PI / 4.0f))
                    {
                        localEnt.LocomotionDirection = LocomotionDirection.Backward;
                    }
                    else if (inputAngle > 0f)
                    {
                        localEnt.LocomotionDirection = LocomotionDirection.Right;
                    }
                    else
                    {
                        localEnt.LocomotionDirection = LocomotionDirection.Left;
                    }

                    byte effectiveRun = GetEffectiveRunSpeed(localEnt);
                    byte effectiveWalk = GetEffectiveWalkSpeed(localEnt);
                    byte moveSpeed = _inputState.IsWalking ? effectiveWalk : effectiveRun;
                    if (leftStick != Vector2.Zero && leftStick.Length() < padSettings.WalkTiltThreshold)
                    {
                        moveSpeed = effectiveWalk;
                    }

                    localEnt.Speed = moveSpeed;
                    float speedYalmsPerSec = moveSpeed * 0.1f;
                    float distance = speedYalmsPerSec * dt;

                    float headingRad = localEnt.HeadingRadians;
                    // FFXI coordinate math:
                    // Heading 0 = East (+X), 64 = South (+Z), 128 = West (-X), 192 = North (-Z)
                    // Forward vector = (cos(theta), sin(theta))
                    // Strafe right vector = (-sin(theta), cos(theta))
                    float dx = (MathF.Cos(headingRad) * lockFwd - MathF.Sin(headingRad) * lockStrafe) * distance;
                    float dz = (MathF.Sin(headingRad) * lockFwd + MathF.Cos(headingRad) * lockStrafe) * distance;

                    localEnt.Position = new Vector3(localEnt.Position.X + dx, localEnt.Position.Y, localEnt.Position.Z + dz);

                    // Re-align facing to target after displacement
                    toTgtX = lockTgt.Position.X - localEnt.Position.X;
                    toTgtZ = lockTgt.Position.Z - localEnt.Position.Z;
                    if ((toTgtX * toTgtX) + (toTgtZ * toTgtZ) > 0.0001f)
                    {
                        float toTargetRad = MathF.Atan2(toTgtZ, toTgtX);
                        if (toTargetRad < 0f) toTargetRad += MathF.PI * 2.0f;
                        localEnt.Direction = (byte)Math.Round((toTargetRad / (MathF.PI * 2.0f)) * 256.0f);
                        localEnt.RenderHeadingRadians = toTargetRad;
                    }
                }
                else
                {
                    localEnt.Speed = 0;
                    localEnt.LocomotionDirection = LocomotionDirection.Forward;
                }

                LocomotionUpdated?.Invoke(localEnt.Position, localEnt.Direction, localEnt.Speed);
                return;
            }

            // Camera-Relative 3D Locomotion (Standard FFXI Type A)
            if (leftStick != Vector2.Zero && padSettings.LocomotionMode == GamepadLocomotionMode.CameraRelative)
            {
                // Angle relative to Camera Yaw: stick Up (0, 1) is 0 offset, Right (1, 0) is +90, Down is +180, Left is -90.
                // Subtracted (not added) because the renderer displays the world at a mirrored X
                // coordinate (see ViewportCamera/EntityRenderer), which flips the handedness of
                // "camera right": in world-heading terms, camera-right is CameraYaw - 90, not + 90.
                float stickAngleDeg = MathF.Atan2(leftStick.X, leftStick.Y) * (180.0f / MathF.PI);
                float targetHeadingDeg = NormalizeDegrees(CameraYaw - stickAngleDeg);
                localEnt.Direction = (byte)Math.Round((targetHeadingDeg / 360.0f) * 256.0f);
                localEnt.LocomotionDirection = LocomotionDirection.Forward;

                float stickMagnitude = leftStick.Length();
                byte effectiveRun = GetEffectiveRunSpeed(localEnt);
                byte effectiveWalk = GetEffectiveWalkSpeed(localEnt);
                byte padSpeed = (stickMagnitude < padSettings.WalkTiltThreshold || _inputState.IsWalking)
                    ? effectiveWalk
                    : effectiveRun;

                localEnt.Speed = padSpeed;
                float speedYalmsPerSec = padSpeed * 0.1f;
                float distance = speedYalmsPerSec * dt;

                float headingRad = localEnt.HeadingRadians;
                float dx = MathF.Cos(headingRad) * distance;
                float dz = MathF.Sin(headingRad) * distance;

                localEnt.Position = new Vector3(localEnt.Position.X + dx, localEnt.Position.Y, localEnt.Position.Z + dz);
                LocomotionUpdated?.Invoke(localEnt.Position, localEnt.Direction, localEnt.Speed);
                return;
            }

            // Keyboard camera-relative facing + movement (mirrors the gamepad CameraRelative
            // stick above): W/S/A/D are treated as a virtual analog stick (W/S = forward/back,
            // A/D = left/right) and always face the character relative to the camera before
            // moving - like pushing a controller stick - instead of turning in place or walking
            // along whatever direction the character happened to already be facing.
            if (leftStick == Vector2.Zero)
            {
                float keyX = 0f;
                if (_inputState.IsActionHeld(InputAction.TurnRight)) keyX += 1.0f;
                if (_inputState.IsActionHeld(InputAction.TurnLeft)) keyX -= 1.0f;

                float keyY = 0f;
                if (_inputState.IsActionHeld(InputAction.MoveForward) || _inputState.AutorunActive) keyY += 1.0f;
                if (_inputState.IsActionHeld(InputAction.MoveBackward)) keyY -= 1.0f;

                if (keyX != 0f || keyY != 0f)
                {
                    // Same formula and mirrored-render reasoning as the gamepad stick above.
                    float stickAngleDeg = MathF.Atan2(keyX, keyY) * (180.0f / MathF.PI);
                    float targetHeadingDeg = NormalizeDegrees(CameraYaw - stickAngleDeg);
                    localEnt.Direction = (byte)Math.Round((targetHeadingDeg / 360.0f) * 256.0f);
                    localEnt.LocomotionDirection = LocomotionDirection.Forward;

                    byte effectiveRun = GetEffectiveRunSpeed(localEnt);
                    byte effectiveWalk = GetEffectiveWalkSpeed(localEnt);
                    byte keySpeed = _inputState.IsWalking ? effectiveWalk : effectiveRun;

                    localEnt.Speed = keySpeed;
                    float speedYalmsPerSec = keySpeed * 0.1f;
                    float distance = speedYalmsPerSec * dt;

                    float headingRad = localEnt.HeadingRadians;
                    float dx = MathF.Cos(headingRad) * distance;
                    float dz = MathF.Sin(headingRad) * distance;

                    localEnt.Position = new Vector3(localEnt.Position.X + dx, localEnt.Position.Y, localEnt.Position.Z + dz);
                    LocomotionUpdated?.Invoke(localEnt.Position, localEnt.Direction, localEnt.Speed);
                    return;
                }
            }

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

            // Character-relative analog stick injection
            if (leftStick != Vector2.Zero && padSettings.LocomotionMode == GamepadLocomotionMode.CharacterRelative)
            {
                forwardInput += leftStick.Y;
            }

            // 2. Determine Strafe intent, and Turn intent from the gamepad's tank-style mode
            // (keyboard Turn keys are handled above via camera-relative facing, not here).
            float turnInput = 0;
            if (leftStick != Vector2.Zero && padSettings.LocomotionMode == GamepadLocomotionMode.CharacterRelative)
            {
                turnInput += leftStick.X;
            }

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
                if (forwardInput < 0 && MathF.Abs(forwardInput) >= MathF.Abs(strafeInput))
                {
                    localEnt.LocomotionDirection = LocomotionDirection.Backward;
                }
                else if (strafeInput > 0 && MathF.Abs(strafeInput) > MathF.Abs(forwardInput))
                {
                    localEnt.LocomotionDirection = LocomotionDirection.Right;
                }
                else if (strafeInput < 0 && MathF.Abs(strafeInput) > MathF.Abs(forwardInput))
                {
                    localEnt.LocomotionDirection = LocomotionDirection.Left;
                }
                else
                {
                    localEnt.LocomotionDirection = LocomotionDirection.Forward;
                }

                byte effectiveRun = GetEffectiveRunSpeed(localEnt);
                byte effectiveWalk = GetEffectiveWalkSpeed(localEnt);
                currentSpeed = _inputState.IsWalking ? effectiveWalk : effectiveRun;
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
                // Heading 0 = East (+X), 64 = South (+Z), 128 = West (-X), 192 = North (-Z)
                // Forward vector = (cos(theta), sin(theta)) on (X, Z) ground plane
                // Strafe right vector = (-sin(theta), cos(theta)) on (X, Z) ground plane
                float dx = (MathF.Cos(headingRad) * forwardInput - MathF.Sin(headingRad) * strafeInput) * distance;
                float dz = (MathF.Sin(headingRad) * forwardInput + MathF.Cos(headingRad) * strafeInput) * distance;

                localEnt.Position = new Vector3(localEnt.Position.X + dx, localEnt.Position.Y, localEnt.Position.Z + dz);
            }
            else
            {
                localEnt.Speed = 0;
                localEnt.LocomotionDirection = LocomotionDirection.Forward;
            }

            LocomotionUpdated?.Invoke(localEnt.Position, localEnt.Direction, localEnt.Speed);
        }

        private void UpdateActionTriggers()
        {
            if (_actionService == null) return;

            // Toggle Lock-On
            if (_inputState.WasActionTriggered(InputAction.ToggleLockOn))
            {
                _actionService.ToggleLockOn();
            }

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

            // Cancel / Clear Target (releases lock-on first if active, then clears target on subsequent cancel)
            if (_inputState.WasActionTriggered(InputAction.Cancel))
            {
                if (_actionService.IsLockedOn)
                {
                    _actionService.SetLockOn(false);
                }
                else if (_actionService.CurrentTarget != null)
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
