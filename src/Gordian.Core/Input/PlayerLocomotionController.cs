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
        private DoorBlocker[] _closedDoors = Array.Empty<DoorBlocker>();

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

        private readonly object _knockbackLock = new();
        private (Vector2 Direction, int Level)? _pendingKnockback;
        private Vector2 _knockbackDirection;
        private float _knockbackPush;
        private float _knockbackDamper;
        private float _knockbackTicksLeft;

        /// <summary>Whether a knockback slide is moving the player.</summary>
        public bool IsKnockedBack => _knockbackTicksLeft > 0f;

        /// <summary>
        /// Knocks the player back (thread-safe; the slide starts on the next tick): pushed along the ground-plane (X, Z)
        /// direction by the level's slide (<see cref="KnockbackSettings.ProfileOf"/>), stopped by walls and ledges like
        /// walking. The new position goes to the server in the usual 0x015 reports; LandSandBoat trusts it.
        /// </summary>
        public void ApplyKnockback(Vector2 direction, int level)
        {
            if (level <= 0 || direction.LengthSquared() < 1e-6f) return;
            lock (_knockbackLock) _pendingKnockback = (Vector2.Normalize(direction), level);
        }

        /// <summary>
        /// Advances the knockback slide by one update: the push each 60 Hz tick shrinks by the damper, summed over the ticks
        /// this update spans.
        /// </summary>
        private void StepKnockback(WorldEntity localEnt, float dt)
        {
            lock (_knockbackLock)
            {
                if (_pendingKnockback is { } pending)
                {
                    var slide = KnockbackSettings.ProfileOf(pending.Level);
                    _knockbackDirection = pending.Direction;
                    _knockbackPush = slide.PushPerTick;
                    _knockbackDamper = slide.Damper;
                    _knockbackTicksLeft = slide.Ticks;
                    _pendingKnockback = null;
                }
            }
            if (_knockbackTicksLeft <= 0f) return;

            float ticks = MathF.Min(dt * 60.0f, _knockbackTicksLeft);
            float keep = 1.0f - _knockbackDamper;
            float keepPow = MathF.Pow(keep, ticks);
            float distance = _knockbackDamper > 0f ? _knockbackPush * (1.0f - keepPow) / _knockbackDamper : _knockbackPush * ticks;
            _knockbackPush *= keepPow;
            _knockbackTicksLeft -= ticks;

            MoveHorizontally(localEnt, _knockbackDirection.X * distance, _knockbackDirection.Y * distance);
        }

        private void CancelKnockback()
        {
            lock (_knockbackLock) _pendingKnockback = null;
            _knockbackTicksLeft = 0f;
        }

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

            // 1. Evaluate physical keys and mouse against active profile. While a stock menu is open the menu
            //    navigation bindings win over the camera/party-targeting keys they share (retail behaviour).
            var menus = _actionService?.Menus;
            var chat = Chat;
            bool menuOpen = menus?.IsOpen ?? false;
            // A selected log window takes the menu navigation keys (Up/Down scroll it) as an open menu does.
            _inputState.MenuContext = menuOpen || (chat?.SelectedLogWindow ?? 0) != 0;
            _inputState.Update(_profile, elapsed);

            // 1b. The stock chat. While the input line is open the keyboard is its own (the window feeds it directly):
            //     the gamepad's cancel button drops it as Escape does, and Tab / Shift+Tab still cycle targets. The
            //     chat button opens it; keypad + (gamepad Y) cycles the log window selected for scrolling.
            if (chat != null)
            {
                if (chat.Input.IsOpen)
                {
                    if (_inputState.WasActionTriggered(InputAction.Cancel)) chat.Input.Cancel();
                    UpdateTargetCycling();
                    UpdateCamera(elapsed);
                    UpdateLocomotion(elapsed);
                    return;
                }
                if (_inputState.WasActionTriggered(InputAction.OpenChat) && !menuOpen)
                {
                    chat.Input.Open();
                    return;
                }
                if (_inputState.WasActionTriggered(InputAction.CycleLogWindow) && !menuOpen) chat.CycleLogWindow();
                if (chat.SelectedLogWindow != 0 && !menuOpen)
                {
                    UpdateLogScroll(chat, elapsed);
                    UpdateCamera(elapsed);
                    UpdateLocomotion(elapsed);
                    return;
                }
            }

            // 1c. A running event: its dialog advances on the game tick; a waiting line takes Confirm/Cancel. Its
            //     query window is an open menu and takes the menu input below.
            var events = Events;
            bool inEvent = false;
            if (events != null)
            {
                events.Tick(elapsed);
                inEvent = !menuOpen && events.ProcessInput(_inputState);
            }

            // 2. Feed the stock menus: with one open, Confirm/Cancel and targeting belong to it (movement keys and
            //    the left stick still move the character, as in retail).
            _menuOpen = (menus != null && menus.ProcessInput(_inputState, elapsed)) || inEvent;

            // 3. Update Camera from keyboard & mouse impulses
            UpdateCamera(elapsed);

            // 4. Update Locomotion (movement, strafing, turning)
            UpdateLocomotion(elapsed);

            // 4b. Lock-on works with a stock menu open too (the command menu stays up while engaged in retail, 2026-10-03
            //     recording); before #137 it sat behind the menu gate below, so T / NumPad * did nothing while it was open.
            UpdateLockOnToggle();

            // 5. Evaluate Action Triggers (Targeting, Selection); a menu takes Confirm/Cancel and targeting keys.
            if (!_menuOpen) UpdateActionTriggers();
        }

        /// <summary>The session's stock chat (null outside a session).</summary>
        public Ui.StockUiChat? Chat { get; set; }

        /// <summary>The session's event dialog (null outside a session); ticked here so the dialog runs with the input.</summary>
        public Events.EventDialogController? Events { get; set; }

        // Held Up/Down on a selected log window repeat as menu cursors do: after 0.4 s, then every 60 ms.
        private const double ScrollRepeatDelay = 0.4, ScrollRepeatInterval = 0.06;
        private double _scrollHeld;
        private int _scrollDirection;

        /// <summary>Scrolls the selected log window with Up/Down (repeating while held); Cancel or Confirm releases it.</summary>
        private void UpdateLogScroll(Ui.StockUiChat chat, TimeSpan elapsed)
        {
            if (_inputState.WasActionTriggered(InputAction.Cancel) || _inputState.WasActionTriggered(InputAction.Confirm))
            {
                chat.ReleaseLogWindow();
                return;
            }
            int direction = _inputState.IsActionHeld(InputAction.MenuUp) ? 1 : _inputState.IsActionHeld(InputAction.MenuDown) ? -1 : 0;
            if (direction == 0)
            {
                _scrollDirection = 0;
                return;
            }
            if (direction != _scrollDirection)
            {
                _scrollDirection = direction;
                _scrollHeld = 0;
                chat.Log.Scroll(chat.SelectedLogWindow, direction);
                return;
            }
            double before = _scrollHeld;
            _scrollHeld += elapsed.TotalSeconds;
            if (_scrollHeld < ScrollRepeatDelay) return;
            int steps = (int)((_scrollHeld - ScrollRepeatDelay) / ScrollRepeatInterval) - (int)(Math.Max(0, before - ScrollRepeatDelay) / ScrollRepeatInterval);
            if (before < ScrollRepeatDelay) steps++;
            if (steps > 0) chat.Log.Scroll(chat.SelectedLogWindow, direction * steps);
        }

        /// <summary>True while a stock menu took this tick's input (movement keys and stick still work, as in retail).</summary>
        private bool _menuOpen;

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

            if (ClampLockOnCamera()) cameraChanged = true;

            // Update underlying ViewportCamera matrices and frustum
            var targetPos = Vector3.Zero;
            uint targetServerId = GetOrResolveLocalServerId();
            if (targetServerId != 0 && _world.TryGetByServerId(targetServerId, out var targetEnt) && targetEnt != null)
            {
                targetPos = targetEnt.Position;
            }
            float lockDistance = LockOnZoomedDistance(dt);
            // The view re-aims at the target on the same eased blend as the zoom (retail recording 2026-10-03).
            var aimTarget = _camera.Mode == CameraMode.ThirdPersonOrbital ? GetLockOnTarget() : null;
            if (aimTarget is { IsSpawned: true }) _lastAimPoint = aimTarget.Position; // kept while the aim eases back out
            _camera.AimPoint = _lastAimPoint;
            _camera.AimBlend = _lastAimPoint != null ? LockOnAimWeight * _lockZoomBlend * _lockZoomBlend * (3f - (2f * _lockZoomBlend)) : 0f;
            if (_lockZoomBlend <= 0f) _lastAimPoint = null;
            _camera.Update(targetPos, CameraPitch, CameraYaw, lockDistance, _camera.AspectRatio);

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
                CancelKnockback(); // a server placement ends any slide
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
            _closedDoors = ZoneDoors.EvaluateClosed(_world.Collision, _world);
            LogPlatformJumps(previousPlatforms, platformClock);
            RideMovingPlatform(localEnt);

            // A fall, once started by stepping off a height, continues whether or not the player keeps moving.
            if (_airborne) FallStep(localEnt, dt);

            // A knockback slides the player on top of whatever it does itself.
            StepKnockback(localEnt, dt);

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
            WorldEntity? lockTgt = GetLockOnTarget();

            if (lockTgt != null && lockTgt.IsSpawned)
            {
                float toTgtX = lockTgt.Position.X - localEnt.Position.X;
                float toTgtZ = lockTgt.Position.Z - localEnt.Position.Z;
                float distSq = (toTgtX * toTgtX) + (toTgtZ * toTgtZ);

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

                    // Facing toward the target starts once the player moves (never on the engage or lock frame), at the
                    // stick-turn rate rather than as a snap (#137, provisional: see docs/input/console-and-input.md).
                    if (distSq > 0.0001f) FaceTarget(localEnt, toTgtX, toTgtZ, dt);

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
                    if ((toTgtX * toTgtX) + (toTgtZ * toTgtZ) > 0.0001f) FaceTarget(localEnt, toTgtX, toTgtZ, dt);
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

                // Closed doors fill their doorway (the collision soup has none of their leaves).
                target = ZoneDoors.Resolve(_closedDoors, start, target, BodyRadius, StepUpHeight, BodyHeight);

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

        /// <summary>
        /// Turns the locked-on character toward the target at <see cref="FacingTurnSpeedDegreesPerSec"/>, so locking on
        /// (including the automatic lock when engaging) never snaps the heading in one frame (#137). PROVISIONAL: whether
        /// retail's lock-on turns gradually or at once is unconfirmed.
        /// </summary>
        private void FaceTarget(WorldEntity localEnt, float toTgtX, float toTgtZ, float dt)
        {
            float toTargetDeg = WorldEntity.HeadingOf(toTgtX, toTgtZ) * (180.0f / MathF.PI);
            TurnTowards(localEnt, NormalizeDegrees(toTargetDeg), dt);
            localEnt.RenderHeadingRadians = localEnt.HeadingRadians;
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

        /// <summary>
        /// Tab / Shift+Tab (and the triggers) and the d-pad's left / right target cursor (see <see cref="TargetCycling"/>).
        /// </summary>
        private void UpdateTargetCycling()
        {
            if (_inputState.WasActionTriggered(InputAction.TargetNearest)) CycleTarget(TargetCycleMode.TabRight);
            else if (_inputState.WasActionTriggered(InputAction.TargetPrevious)) CycleTarget(TargetCycleMode.TabLeft);
            else if (_inputState.WasActionTriggered(InputAction.TargetCursorRight)) CycleTarget(TargetCycleMode.CursorRight);
            else if (_inputState.WasActionTriggered(InputAction.TargetCursorLeft)) CycleTarget(TargetCycleMode.CursorLeft);
        }

        /// <summary>Targets what <see cref="TargetCycling.Pick"/> chooses for <paramref name="mode"/>.</summary>
        private void CycleTarget(TargetCycleMode mode)
        {
            if (_actionService == null || _localPlayer.ServerId == 0 || !_world.TryGetByServerId(_localPlayer.ServerId, out var localEnt) || localEnt == null) return;

            uint current = _actionService.CurrentTarget?.ServerId ?? 0;
            var nearby = _world.GetEntitiesInRadius(localEnt.Position, TargetCycling.Range);
            // A current target out of range still counts, past the screen edge it left by.
            if (_actionService.CurrentTarget is { } target && !nearby.Contains(target)) nearby.Add(target);
            var candidates = TargetCycling.Gather(nearby, _localPlayer.ServerId, current, localEnt.Position, _camera);
            uint pick = TargetCycling.Pick(candidates, _localPlayer.ServerId, current, mode);
            if (pick != 0) _actionService.SetTargetByServerId(pick);
        }

        private bool _lockKeyLogged;

        private void UpdateLockOnToggle()
        {
            if (_actionService == null) return;
            bool keyHeld = _inputState.IsKeyHeld(GordianKey.T) || _inputState.IsKeyHeld(GordianKey.NumPadMultiply);
            if (keyHeld && !_lockKeyLogged)
            {
                // Diagnosis of a key that does nothing: it reached InputState; is it bound, and did the action fire?
                _lockKeyLogged = true;
                Gordian.Core.Diagnostics.GordianLog.Info("LockOn", $"Lock-on key held (action bound and held={_inputState.IsActionHeld(InputAction.ToggleLockOn)}, triggered={_inputState.WasActionTriggered(InputAction.ToggleLockOn)}, menuOpen={_menuOpen})");
            }
            else if (!keyHeld) _lockKeyLogged = false;
            if (!_inputState.WasActionTriggered(InputAction.ToggleLockOn)) return;
            _actionService.ToggleLockOn();
            Gordian.Core.Diagnostics.GordianLog.Info("LockOn", $"Lock-on toggled by key: locked={_actionService.IsLockedOn}, target={_actionService.CurrentTarget?.Name ?? "none"}");
        }

        private void UpdateActionTriggers()
        {
            if (_actionService == null) return;

            UpdateTargetCycling();

            // Target Self (F1) and the other members of your party (F2-F6, in party window order)
            if (_inputState.WasActionTriggered(InputAction.TargetSelf))
            {
                _actionService.SetTargetByServerId(_localPlayer.ServerId);
            }
            for (int slot = 1; slot <= 5; slot++)
            {
                if (_inputState.WasActionTriggered((InputAction)((int)InputAction.TargetParty1 + slot - 1))) _actionService.SetTargetByPartySlot(slot);
            }

            // Confirm on a targeted NPC or door talks to it; on yourself, another player, a monster, a pet or a trust
            // it opens the target command menu (retail). With nothing targeted, the gamepad's Confirm (A) targets the
            // closest thing; the keyboard's does not (Tab does).
            if (_inputState.WasActionTriggered(InputAction.Confirm))
            {
                if (_actionService.CurrentTarget == null)
                {
                    if (_inputState.WasActionTriggeredByGamepad(InputAction.Confirm)) CycleTarget(TargetCycleMode.Closest);
                }
                else if (_actionService.CanTalkToTarget) _ = _actionService.TalkToTargetAsync();
                else _actionService.OpenTargetCommandMenu();
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
        /// <summary>
        /// The one place that decides whether the character and camera are tied to the target (#137). PROVISIONAL: only
        /// an explicit player lock-on (<see cref="PlayerActionService.IsLockedOn"/>) does; being engaged on its own
        /// leaves movement and heading free (retail recording 2026-10-03: engaging does not turn you).
        /// </summary>
        private WorldEntity? GetLockOnTarget()
        {
            if (_actionService == null || !_actionService.IsLockedOn) return null;
            var target = _actionService.CurrentTarget;
            if (target == null && _actionService.Combat != null && _actionService.Combat.TargetServerId != 0)
            {
                _world.TryGetByServerId(_actionService.Combat.TargetServerId, out target);
            }
            return target;
        }

        // Locked-on camera, measured from the maintainer's retail recording of 2026-10-03 (1438p, 10 fps frames t001-t287,
        // where tNNN is (NNN - 1) / 10 s). Method: with the stock 60 degree vertical field of view (16:9, 91 degree
        // horizontal) the character's screen offset from the view centre at each limit gives the angle between the view
        // and the character, and the camera-to-character parallax (target 10 yalms away, camera about 3 yalms behind) adds
        // about 6 degrees to get the camera's yaw against the character-to-target line.

        /// <summary>
        /// Furthest the locked-on camera may swing RIGHT of the character-to-target line (degrees). Retail recording
        /// 2026-10-03, about 10.2-10.6 s: the camera sits at its right limit with the character at x = 175 of 640
        /// (145 px left of centre, 24.9 degrees of view, about 32 degrees of yaw with parallax).
        /// </summary>
        public const float LockOnCameraArcRightDegrees = 32.0f;

        /// <summary>
        /// Furthest the locked-on camera may swing LEFT of the line (degrees). Retail recording 2026-10-03, about
        /// 11.8-12.6 s: left limit with the character at x = 460 of 640 (140 px right of centre, 24.2 degrees of view,
        /// about 31 degrees of yaw). Left and right agree within the measuring error.
        /// </summary>
        public const float LockOnCameraArcLeftDegrees = 31.0f;

        /// <summary>
        /// Lowest the locked-on camera pitch may go (degrees above the horizon looking down; 0 = level). Retail recording
        /// 2026-10-03, 17.8-18.0 s: the horizon sits on the view centre (262 of 540 rows), a level view.
        /// </summary>
        public const float LockOnCameraPitchMinDegrees = 0.0f;

        /// <summary>
        /// Highest the locked-on camera pitch may go (degrees looking down). Retail recording 2026-10-03, 16.7-17.3 s:
        /// the distant horizon sits 120 of 540 rows above the view centre, atan(120 / 270 x tan 30 degrees) = 14.4 degrees
        /// down. Estimated from the horizon of far hills, so good to a few degrees.
        /// </summary>
        public const float LockOnCameraPitchMaxDegrees = 14.5f;

        /// <summary>
        /// Locking on pulls the camera in to this share of its distance. Retail recording 2026-10-03, 4.2-4.5 s (engage):
        /// the character's on-screen height grows from about 125 px to about 250-275 px of 1438, 2.0-2.2 times, so the
        /// distance drops to about 0.48. One sample, so it is not known whether retail uses a share or a fixed distance.
        /// </summary>
        public const float LockOnZoomFactor = 0.48f;

        /// <summary>
        /// How far the locked-on view turns toward the target's bearing (1 = faces it exactly). Retail recording
        /// 2026-10-03, 10-18 s: the Rarab stays at x = 290-350 of 640 (about plus or minus 5 degrees of the centre) while
        /// the character moves across the whole screen, with the pitch left to the camera (the Rarab is on the view's
        /// centre row when level, 17.8-18.0 s, and 45 rows of 540 above it when pitched down, 16.7-17.3 s). So the view
        /// faces the target in yaw only. The plus or minus 5 degrees is not modelled (likely lag in the aim).
        /// </summary>
        public const float LockOnAimWeight = 1.0f;

        /// <summary>
        /// Time the lock-on zoom takes, eased (smoothstep). Retail recording 2026-10-03: nothing at 4.1-4.2 s, 40% done at
        /// 4.3 s, 95% at 4.4 s, done at 4.5 s, so about 0.3 s; turning lock-on off was not recorded and eases back at the
        /// same speed.
        /// </summary>
        public const float LockOnZoomSeconds = 0.3f;

        private float LockOnCameraDiff(float yawDeg, WorldEntity localEnt, WorldEntity target)
        {
            float dx = target.Position.X - localEnt.Position.X;
            float dz = target.Position.Z - localEnt.Position.Z;
            if ((dx * dx) + (dz * dz) < 0.0001f) return 0f;
            return WrapDegrees(yawDeg - (WorldEntity.HeadingOf(dx, dz) * (180.0f / MathF.PI)));
        }

        private bool _hasLockCameraDiff;
        private float _lockCameraDiff;
        private float _lockCameraPitch;
        private float _lockZoomBlend;
        private Vector3? _lastAimPoint;

        /// <summary>
        /// Holds the locked-on camera inside the measured yaw arc (<see cref="LockOnCameraArcRightDegrees"/> /
        /// <see cref="LockOnCameraArcLeftDegrees"/>) and pitch range, whether the player pushes it past a limit or the
        /// target drifts there as the player runs around it: the camera stays at the limit (no ease, no return behind the
        /// player; retail recording 2026-10-03, 12-16 s: starting from the left limit the camera does not turn on its
        /// own while the player runs left, the character crosses the screen until the right limit is reached and the
        /// camera is then carried along there). A camera already outside the range (locked on from behind) is not moved
        /// by this, it just cannot go further out. Returns true when it moved the camera.
        /// </summary>
        private bool ClampLockOnCamera()
        {
            var target = _camera.Mode == CameraMode.ThirdPersonOrbital ? GetLockOnTarget() : null;
            uint localId = GetOrResolveLocalServerId();
            if (target == null || !target.IsSpawned || localId == 0 || !_world.TryGetByServerId(localId, out var me) || me == null)
            {
                _hasLockCameraDiff = false;
                return false;
            }

            float diff = LockOnCameraDiff(CameraYaw, me, target);
            bool moved = false;
            if (_hasLockCameraDiff)
            {
                float limit = diff >= 0 ? LockOnCameraArcRightDegrees : LockOnCameraArcLeftDegrees;
                if (MathF.Abs(diff) > limit && MathF.Abs(diff) > MathF.Abs(_lockCameraDiff))
                {
                    float oldMag = MathF.Abs(_lockCameraDiff);
                    float allowed = MathF.Max(limit, oldMag);
                    CameraYaw = NormalizeDegrees(CameraYaw - diff + (MathF.Sign(diff) * allowed));
                    diff = MathF.Sign(diff) * allowed;
                    moved = true;
                }

                float lo = MathF.Min(LockOnCameraPitchMinDegrees, _lockCameraPitch);
                float hi = MathF.Max(LockOnCameraPitchMaxDegrees, _lockCameraPitch);
                float pitch = Math.Clamp(CameraPitch, lo, hi);
                if (pitch != CameraPitch)
                {
                    CameraPitch = pitch;
                    moved = true;
                }
            }
            _lockCameraDiff = diff;
            _lockCameraPitch = CameraPitch;
            _hasLockCameraDiff = true;
            return moved;
        }

        /// <summary>
        /// The camera distance to draw with: <see cref="CameraDistance"/> scaled toward <see cref="LockOnZoomFactor"/>
        /// while locked on, eased over <see cref="LockOnZoomSeconds"/> (smoothstep) both ways. The user's own distance is
        /// never changed, so it is back where it was when lock-on ends.
        /// </summary>
        private float LockOnZoomedDistance(float dt)
        {
            bool zoomed = _camera.Mode == CameraMode.ThirdPersonOrbital && (_actionService?.IsLockedOn ?? false);
            float step = dt / LockOnZoomSeconds;
            _lockZoomBlend = Math.Clamp(_lockZoomBlend + (zoomed ? step : -step), 0f, 1f);
            float eased = _lockZoomBlend * _lockZoomBlend * (3f - (2f * _lockZoomBlend));
            return CameraDistance * (1f + ((LockOnZoomFactor - 1f) * eased));
        }
    }
}
