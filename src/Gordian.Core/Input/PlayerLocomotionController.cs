// src/Gordian.Core/Input/PlayerLocomotionController.cs
// Clean-room player locomotion and camera control loop for GordianXI.
// FFXI coordinate space specifications: 0=East (+X), 64=South (-Z), 128=West (-X), 192=North (+Z).

using System;
using System.Numerics;
using Gordian.Core.Actions;
using Gordian.Core.Config;
using Gordian.Core.Graphics;
using Gordian.Core.World;
using Gordian.Core.World.Collision;

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

        /// <summary>
        /// Rate at which camera-relative input turns the character toward the input direction, instead of snapping.
        /// </summary>
        private const float FacingTurnSpeedDegreesPerSec = 720.0f;

        /// <summary>
        /// Fraction per second of the remaining gap by which the orbital camera swings in behind a character running with
        /// forward input. Because the input direction is camera-relative, this curves diagonal input (e.g. W+A) into an
        /// arc that closes into a full circle when held, as in the legacy client. Calibrated against retail, where a held W+A
        /// run completes a circle in about 11.75 seconds: W+A holds a 45-degree offset, so 45 * rate = 360 / 11.75 degrees/sec.
        /// </summary>
        private const float CameraFollowRate = 0.68f;

        private bool _cameraYawInputThisFrame;

        /// <summary>
        /// <see cref="System.Diagnostics.Stopwatch.GetTimestamp"/> of the latest tick, so a renderer can interpolate the
        /// tick-driven position and camera angles by when they were actually simulated.
        /// </summary>
        public long LastUpdateTimestamp { get; private set; }

        /// <summary>
        /// Highest floor rise, in yalms, the player steps onto without being blocked (stairs, curbs, steep slopes).
        /// Slightly more forgiving than the legacy client, which will not let the player up the low side walls of
        /// Southern San d'Oria's ramps.
        /// </summary>
        public const float StepUpHeight = 0.5f;

        /// <summary>
        /// Radius, in yalms, of the rounded foot the player rests on, which turns stairs into a ramp as in the legacy
        /// client (see <see cref="ZoneCollisionMesh.TryGetSteppedGround"/>).
        /// </summary>
        public const float FootRadius = 0.9f;

        /// <summary>
        /// Deepest drop, in yalms, the player falls to a floor below; beyond it the height is left alone.
        /// </summary>
        public const float MaxGroundDrop = 60.0f;

        private readonly CollisionSettings _ownCollision = new();
        private readonly EntityBumpCollision _entityBump = new();
        private float _tickSeconds;
        private bool _airborne;
        private float _fallSpeed;
        private PlatformHeight[] _platforms = Array.Empty<PlatformHeight>();

        /// <summary>
        /// Test hook: the Earth seconds since the Vana'diel epoch used to place moving platforms (defaults to now).
        /// </summary>
        internal Func<double>? PlatformClock { get; set; }

        /// <summary>
        /// Downward acceleration of a falling player, in yalms per second squared. Fitted to a Windower capture of three
        /// falls (12 and 20 yalms) in Southern San d'Oria (2026-09-25): the legacy client falls far faster than real
        /// gravity, reaching <see cref="MaxFallSpeed"/> in under half a second.
        /// </summary>
        public const float Gravity = 66.0f;

        /// <summary>
        /// Fastest a falling player descends, in yalms per second: the same capture shows a steady 1.001 yalms per
        /// 1/30-second frame once up to speed.
        /// </summary>
        public const float MaxFallSpeed = 30.0f;

        /// <summary>
        /// A floor further than this below the feet (yalms) is fallen to under gravity instead of settled on at once.
        /// </summary>
        public const float FallThreshold = 0.5f;

        /// <summary>
        /// Deepest floor, in yalms, a player falls to.
        /// </summary>
        public const float MaxFallDistance = 500.0f;

        /// <summary>
        /// True while the player is falling.
        /// </summary>
        public bool IsFalling => _airborne;

        /// <summary>
        /// Radius, in yalms, of the player's body against walls. Measured from a Windower capture of a character pressed
        /// into a Southern San d'Oria wall corner: 0.529 and 0.535 yalms from the two walls.
        /// </summary>
        public const float BodyRadius = 0.53f;

        /// <summary>
        /// Height, in yalms, of the player's body against walls (overhangs above it do not block).
        /// </summary>
        public const float BodyHeight = 1.6f;

        /// <summary>
        /// The session's collision toggles (shared with <see cref="PlayerActionService.Collision"/> when there is one).
        /// </summary>
        public CollisionSettings Collision => _actionService?.Collision ?? _ownCollision;

        /// <summary>
        /// The collision layers local movement obeys right now, after server policy.
        /// </summary>
        public CollisionLayers EffectiveCollision => Collision.GetEffective(_actionService?.Profile);

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
        /// Gated by FeatureRestrictions.
        /// </summary>
        public float SpeedMultiplier { get; set; } = 1.0f;

        /// <summary>
        /// Optional explicit client-side speed override (e.g. set by addons or GM tools).
        /// Null by default. Gated by FeatureRestrictions.
        /// </summary>
        public byte? SpeedOverride { get; set; }

        /// <summary>
        /// Calculates the effective run speed for the local player entity.
        /// Priority:
        /// 1. Authoritative server speed from LocalPlayerState (updated via 0x037 / 0x00A)
        /// 2. Active speed from localEnt if moving
        /// 3. Profile default (RunSpeed = 50)
        /// Speed manipulation (SpeedOverride, SpeedMultiplier) is permitted unless the server has set the
        /// FeatureRestrictions.SpeedOverride restriction.
        /// </summary>
        public byte GetEffectiveRunSpeed(WorldEntity? localEnt)
        {
            byte baseRun;
            if (_localPlayer.Speed > 0)
            {
                baseRun = (byte)Math.Clamp((int)_localPlayer.Speed, 1, 255);
            }
            else if (localEnt != null && localEnt.SpeedBase > 0)
            {
                baseRun = localEnt.SpeedBase;
            }
            else
            {
                baseRun = _profile.RunSpeed;
            }

            // Gated by FeatureRestrictions: speed tampering is blocked when SpeedOverride is restricted
            bool policyAllowsOverride = _actionService == null || !_actionService.Profile.IsRestricted(FeatureRestrictions.SpeedOverride);
            if (policyAllowsOverride)
            {
                if (SpeedOverride.HasValue && SpeedOverride.Value > 0)
                {
                    return SpeedOverride.Value;
                }

                if (Math.Abs(SpeedMultiplier - 1.0f) > 0.001f)
                {
                    return (byte)Math.Clamp((int)MathF.Round(baseRun * SpeedMultiplier), 1, 255);
                }
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
            LastUpdateTimestamp = System.Diagnostics.Stopwatch.GetTimestamp();

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

            // Camera yaw shares the wire heading convention (increasing yaw turns the view right); positive
            // input yaw deltas orbit the camera the other way, as the camera controls always have.
            yawDelta = -yawDelta;
            _cameraYawInputThisFrame = yawDelta != 0;

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
            _tickSeconds = dt;

            // Server placements (warps, draw-ins, charm) win over local movement: apply them before moving.
            if (_localPlayer.TryTakePositionCorrection(out var correctedPosition, out byte correctedDirection))
            {
                if (correctedPosition is { } placed)
                {
                    localEnt.Position = placed;
                    _airborne = false; // a placement in mid-air holds until the player moves
                    _fallSpeed = 0.0f;
                }
                localEnt.Direction = correctedDirection;
            }

            // Locked by the server (an event) or charmed (the server drives the character): no input movement.
            if (_localPlayer.IsMovementLocked || (localEnt is PlayerEntity { IsCharmed: true }))
            {
                localEnt.Speed = 0;
                LocomotionUpdated?.Invoke(localEnt.Position, localEnt.Direction, localEnt.Speed);
                return;
            }

            var previousPlatforms = _platforms;
            double platformClock = PlatformClock?.Invoke() ?? VanaTime.GetEarthSecondsSinceEpoch(DateTime.UtcNow);
            _platforms = MovingPlatforms.Evaluate(_world.Collision, _world, platformClock);
            LogPlatformJumps(previousPlatforms, platformClock);
            RideMovingPlatform(localEnt);

            // A fall, once started by stepping off a height, continues whether or not the player keeps moving.
            if (_airborne) FallStep(localEnt, dt);

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
                    float toTargetRad = WorldEntity.HeadingOf(toTgtX, toTgtZ);
                    localEnt.Direction = WorldEntity.DirectionFromRadians(toTargetRad);
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

                    var fwd = WorldEntity.ForwardOf(localEnt.HeadingRadians);
                    var right = WorldEntity.RightOf(localEnt.HeadingRadians);
                    float dx = (fwd.X * lockFwd + right.X * lockStrafe) * distance;
                    float dz = (fwd.Y * lockFwd + right.Y * lockStrafe) * distance;

                    MoveHorizontally(localEnt, dx, dz);

                    // Re-align facing to target after displacement
                    toTgtX = lockTgt.Position.X - localEnt.Position.X;
                    toTgtZ = lockTgt.Position.Z - localEnt.Position.Z;
                    if ((toTgtX * toTgtX) + (toTgtZ * toTgtZ) > 0.0001f)
                    {
                        float toTargetRad = WorldEntity.HeadingOf(toTgtX, toTgtZ);
                        localEnt.Direction = WorldEntity.DirectionFromRadians(toTargetRad);
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
                // Increasing heading turns right on screen, so camera-right is CameraYaw + 90.
                float stickAngleDeg = MathF.Atan2(leftStick.X, leftStick.Y) * (180.0f / MathF.PI);
                TurnTowards(localEnt, NormalizeDegrees(CameraYaw + stickAngleDeg), dt);
                localEnt.LocomotionDirection = LocomotionDirection.Forward;
                if (leftStick.Y > 0.1f) FollowHeadingWithCamera(localEnt, dt);

                float stickMagnitude = leftStick.Length();
                byte effectiveRun = GetEffectiveRunSpeed(localEnt);
                byte effectiveWalk = GetEffectiveWalkSpeed(localEnt);
                byte padSpeed = (stickMagnitude < padSettings.WalkTiltThreshold || _inputState.IsWalking)
                    ? effectiveWalk
                    : effectiveRun;

                localEnt.Speed = padSpeed;
                float speedYalmsPerSec = padSpeed * 0.1f;
                float distance = speedYalmsPerSec * dt;

                var fwd = WorldEntity.ForwardOf(localEnt.HeadingRadians);
                MoveHorizontally(localEnt, fwd.X * distance, fwd.Y * distance);
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
                    // Same formula as the gamepad stick above.
                    float stickAngleDeg = MathF.Atan2(keyX, keyY) * (180.0f / MathF.PI);
                    TurnTowards(localEnt, NormalizeDegrees(CameraYaw + stickAngleDeg), dt);
                    localEnt.LocomotionDirection = LocomotionDirection.Forward;
                    if (keyY > 0f) FollowHeadingWithCamera(localEnt, dt);

                    byte effectiveRun = GetEffectiveRunSpeed(localEnt);
                    byte effectiveWalk = GetEffectiveWalkSpeed(localEnt);
                    byte keySpeed = _inputState.IsWalking ? effectiveWalk : effectiveRun;

                    localEnt.Speed = keySpeed;
                    float speedYalmsPerSec = keySpeed * 0.1f;
                    float distance = speedYalmsPerSec * dt;

                    var fwd = WorldEntity.ForwardOf(localEnt.HeadingRadians);
                    MoveHorizontally(localEnt, fwd.X * distance, fwd.Y * distance);
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
                // Positive turn input (stick right) turns right, which is an increasing wire heading.
                float headingDeg = (localEnt.Direction / 256.0f) * 360.0f;
                headingDeg = NormalizeDegrees(headingDeg + (turnInput * _profile.TurnSpeedDegreesPerSec * dt));
                localEnt.Direction = WorldEntity.DirectionFromDegrees(headingDeg);
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

                var fwd = WorldEntity.ForwardOf(localEnt.HeadingRadians);
                var right = WorldEntity.RightOf(localEnt.HeadingRadians);
                float dx = (fwd.X * forwardInput + right.X * strafeInput) * distance;
                float dz = (fwd.Y * forwardInput + right.Y * strafeInput) * distance;

                MoveHorizontally(localEnt, dx, dz);
            }
            else
            {
                localEnt.Speed = 0;
                localEnt.LocomotionDirection = LocomotionDirection.Forward;
            }

            LocomotionUpdated?.Invoke(localEnt.Position, localEnt.Direction, localEnt.Speed);
        }

        /// <summary>
        /// Moves the player across the ground by a horizontal displacement and settles it onto the floor there. Height is
        /// only settled when the player moves: a placement in mid-air (<c>/moveto</c> or <c>!pos</c> with a height) holds
        /// until the next step, which drops the player onto whatever is below, e.g. a ledge it was lifted above.
        /// </summary>
        private void MoveHorizontally(WorldEntity localEnt, float dx, float dz)
        {
            var layers = EffectiveCollision;
            var start = localEnt.Position;
            var target = new Vector3(start.X + dx, start.Y, start.Z + dz);

            // Characters stop the player briefly (the legacy soft bump), then let it through.
            if ((layers & CollisionLayers.Entities) != 0 && !_entityBump.TryMove(_world, localEnt, start, target, _tickSeconds))
            {
                target = start;
            }

            var collision = _world.Collision;
            if (collision != null && (layers & CollisionLayers.Walls) != 0 && target != start)
            {
                target = collision.ResolveWalls(start, target, BodyRadius, StepUpHeight, BodyHeight);

                // The shaft doors keep the player out of an elevator shaft unless its platform is at the player's level.
                if (MovingPlatforms.EntersEmptyShaft(_platforms, start, target, StepUpHeight)) target = start;

                // Never step off the collision mesh onto nothing (a gap, a cliff top out of reach): stay put instead.
                if ((layers & CollisionLayers.Ground) != 0 &&
                    TryFindGround(collision, start, 0.0f, out _) &&
                    !TryFindGround(collision, target, 0.0f, out _))
                {
                    target = start;
                }
            }

            localEnt.Position = target;
            if (!_airborne) SnapToGround(localEnt); // an airborne player's height is owned by FallStep
        }

        /// <summary>
        /// Sets the player's height to the walkable surface under it: the highest floor at most
        /// <see cref="StepUpHeight"/> above its feet, rounded over tread edges by <see cref="FootRadius"/>, so it
        /// climbs stairs and slopes, descends them, and drops off
        /// ledges, but never pops up onto a bridge or upper floor overhead. Leaves the height alone where the zone has
        /// no collision (not yet loaded, or a gap in the mesh) or when ground collision is off.
        /// </summary>
        private void SnapToGround(WorldEntity localEnt)
        {
            if ((EffectiveCollision & CollisionLayers.Ground) == 0) return;
            var collision = _world.Collision;
            if (collision == null) return;
            var position = localEnt.Position;
            if (!TryFindGround(collision, position, FootRadius, out var ground)) return;

            // Stairs, slopes and small ledges settle at once; anything deeper is a fall (internal +Y is down).
            if (ground.Height - position.Y > FallThreshold)
            {
                _airborne = true;
                _fallSpeed = 0.0f;
                return;
            }
            localEnt.Position = new Vector3(position.X, ground.Height, position.Z);
        }

        /// <summary>
        /// Advances a fall by one tick under <see cref="Gravity"/> and lands on the floor below. Horizontal input keeps
        /// moving the player meanwhile, so the higher the drop the further forward it carries, as in the legacy client.
        /// </summary>
        private void FallStep(WorldEntity localEnt, float dt)
        {
            var collision = _world.Collision;
            if (collision == null || (EffectiveCollision & CollisionLayers.Ground) == 0)
            {
                _airborne = false;
                return;
            }

            var position = localEnt.Position;
            if (!TryFindGround(collision, position, FootRadius, out var ground))
            {
                _airborne = false; // nothing below at all: hold the height rather than fall forever
                return;
            }

            _fallSpeed = MathF.Min(_fallSpeed + (Gravity * dt), MaxFallSpeed);
            float height = position.Y + (_fallSpeed * dt);
            if (height >= ground.Height)
            {
                height = ground.Height;
                _airborne = false;
                _fallSpeed = 0.0f;
            }
            localEnt.Position = new Vector3(position.X, height, position.Z);
        }

        /// <summary>
        /// The floor under <paramref name="feet"/>: the zone's static collision (rounded over tread edges when
        /// <paramref name="footRadius"/> is positive), or a moving platform's floor above it.
        /// </summary>
        private bool TryFindGround(ZoneCollisionMesh collision, Vector3 feet, float footRadius, out GroundHit ground)
        {
            bool found = footRadius > 0.0f
                ? collision.TryGetSteppedGround(feet, StepUpHeight, MaxFallDistance, footRadius, out ground)
                : collision.TryGetGround(feet, StepUpHeight, MaxFallDistance, out ground);
            return MovingPlatforms.TryOverride(_platforms, feet, StepUpHeight, MaxFallDistance, found, ref ground) || found;
        }

        /// <summary>
        /// Carries a player standing on a moving platform with it, even while it stands still: the legacy client moves
        /// riders itself, and the server keeps whatever position the client reports.
        /// </summary>
        private void RideMovingPlatform(WorldEntity localEnt)
        {
            if (_platforms.Length == 0 || _airborne || (EffectiveCollision & CollisionLayers.Ground) == 0) return;
            var feet = localEnt.Position;
            string riding = string.Empty;
            if (MovingPlatforms.TryGetPlatformUnder(_platforms, feet, StepUpHeight, StepUpHeight, out var platform))
            {
                localEnt.Position = feet with { Y = platform.Height };
                riding = platform.Platform.Id;
            }
            if (riding != _ridingPlatformId)
            {
                string nearby = string.Empty;
                foreach (var candidate in _platforms)
                {
                    if (candidate.Platform.Contains(feet.X, feet.Z)) nearby = $" (over {candidate.Platform.Id} at {candidate.Height:F3})";
                }
                Gordian.Core.Diagnostics.GordianLog.Info("Elevator", riding.Length > 0
                    ? $"Riding {riding}: feet {feet.Y:F3} -> {platform.Height:F3}"
                    : $"Stopped riding {_ridingPlatformId}: feet ({feet.X:F2},{feet.Y:F3},{feet.Z:F2}){nearby}");
                _ridingPlatformId = riding;
            }
        }

        private string _ridingPlatformId = string.Empty;

        /// <summary>
        /// The moving platform the player is riding (empty when none), so a renderer can draw the player on the
        /// platform's live height instead of the last tick's.
        /// </summary>
        public string RidingPlatformId => _ridingPlatformId;

        /// <summary>
        /// Diagnostics: logs a platform that moved further in one tick than any leg could (an elevator glitch).
        /// </summary>
        private void LogPlatformJumps(PlatformHeight[] previous, double clock)
        {
            if (previous.Length != _platforms.Length) return;
            for (int i = 0; i < _platforms.Length; i++)
            {
                float moved = MathF.Abs(_platforms[i].Height - previous[i].Height);
                if (moved <= 0.3f) continue;
                string detail = "no elevator";
                foreach (var entity in _world.Entities)
                {
                    if (entity.Type != EntityType.Elevator) continue;
                    if (!ReferenceEquals(MovingPlatforms.PlatformOf(_world.Collision!.MovingPlatforms, entity), _platforms[i].Platform)) continue;
                    detail = $"elevator 0x{entity.ServerId:X8} anim={entity.AnimationState} stamp={entity.TransportStartSeconds} observed={entity.TransportObservedSeconds:F3} legStart={MovingPlatforms.LegStart(entity, _world.TransportClockSkewSeconds):F3}";
                }
                Gordian.Core.Diagnostics.GordianLog.Info("Elevator", $"Platform {_platforms[i].Platform.Id} jumped {previous[i].Height:F3} -> {_platforms[i].Height:F3} at clock {clock:F3}; {detail}");
            }
        }

        private void TurnTowards(WorldEntity localEnt, float targetHeadingDeg, float dt)
        {
            float headingDeg = (localEnt.Direction / 256.0f) * 360.0f;
            float diff = WrapDegrees(targetHeadingDeg - headingDeg);
            float maxStep = FacingTurnSpeedDegreesPerSec * dt;
            headingDeg = MathF.Abs(diff) <= maxStep ? targetHeadingDeg : headingDeg + (MathF.Sign(diff) * maxStep);
            localEnt.Direction = WorldEntity.DirectionFromDegrees(NormalizeDegrees(headingDeg));
        }

        private void FollowHeadingWithCamera(WorldEntity localEnt, float dt)
        {
            if (_cameraYawInputThisFrame || _camera.Mode != CameraMode.ThirdPersonOrbital) return;

            float headingDeg = (localEnt.Direction / 256.0f) * 360.0f;
            float diff = WrapDegrees(headingDeg - CameraYaw);
            CameraYaw = NormalizeDegrees(CameraYaw + (diff * MathF.Min(1.0f, dt * CameraFollowRate)));
        }

        private static float WrapDegrees(float deg)
        {
            deg = NormalizeDegrees(deg);
            return deg > 180.0f ? deg - 360.0f : deg;
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
