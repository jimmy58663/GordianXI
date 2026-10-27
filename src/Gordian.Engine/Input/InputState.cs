// src/Gordian.Engine/Input/InputState.cs
// Clean-room input aggregator and frame state evaluator for GordianXI.

using System;
using System.Collections.Generic;

namespace Gordian.Engine.Input
{
    /// <summary>
    /// Thread-safe active input state tracking currently depressed keys, mouse buttons,
    /// modifiers, analog deltas, and derived logical game actions evaluated per frame/tick.
    /// </summary>
    public sealed class InputState
    {
        private readonly object _lock = new object();

        // Raw physical inputs
        private readonly HashSet<GordianKey> _heldKeys = new HashSet<GordianKey>();
        private readonly HashSet<GordianKey> _previousKeys = new HashSet<GordianKey>();
        private readonly HashSet<GordianKey> _pressedKeys = new HashSet<GordianKey>();
        private InputModifiers _modifiers = InputModifiers.None;
        private MouseButton _heldMouseButtons = MouseButton.None;
        private GamepadState _gamepadState = GamepadState.Disconnected;

        // Mouse motion impulses
        private float _mouseDeltaX;
        private float _mouseDeltaY;
        private float _mouseWheelDelta;

        // Logical actions evaluated against active profile
        private readonly HashSet<InputAction> _previousActions = new HashSet<InputAction>();
        private readonly HashSet<InputAction> _heldActions = new HashSet<InputAction>();
        private readonly HashSet<InputAction> _triggeredActions = new HashSet<InputAction>();
        private readonly HashSet<InputAction> _releasedActions = new HashSet<InputAction>();
        private readonly HashSet<InputAction> _gamepadActions = new HashSet<InputAction>();

        // High-level movement state toggles
        public bool AutorunActive { get; set; }
        public bool IsWalking { get; set; }

        /// <summary>
        /// True while a stock menu is open: the menu navigation bindings (arrows, keypad, d-pad) then win over the
        /// camera and party-targeting bindings that share their keys. Set by the menu controller before each tick's
        /// <see cref="Update"/>. The gamepad's left stick is untouched: it keeps moving the character, like WASD.
        /// </summary>
        public bool MenuContext { get; set; }

        private GamepadButton _padButtons;
        private GamepadButton _padButtonsPressed;

        /// <summary>
        /// The gamepad buttons (the triggers past their threshold included) that went down on the last
        /// <see cref="Update"/>, whatever they are bound to: the gamepad key-assignment list reads the button to assign
        /// from this (#21).
        /// </summary>
        public GamepadButton GamepadButtonsPressed
        {
            get { lock (_lock) return _padButtonsPressed; }
        }

        /// <summary>The gamepad buttons (triggers past their threshold included) held on the last <see cref="Update"/>.</summary>
        public GamepadButton GamepadButtonsHeld
        {
            get { lock (_lock) return _padButtons; }
        }

        /// <summary>
        /// True while the gamepad key-assignment list waits for a button (and until the pressed button is let go): the
        /// gamepad's buttons then drive no action, so the button being assigned does not also act (#21). Set by the
        /// locomotion controller before each <see cref="Update"/>.
        /// </summary>
        public bool SuppressGamepadActions { get; set; }

        /// <summary>
        /// True while the Map window is open (#23): the movement and camera actions and both sticks read as idle to the
        /// character and the camera (<see cref="IsActionHeld"/>, <see cref="CurrentGamepad"/>), and the map pans and zooms
        /// with them instead (<see cref="IsActionHeldRaw"/>, <see cref="RawGamepad"/>), as retail's map does (round 1 of
        /// the 2026-10-24 test). Set by the locomotion controller each tick.
        /// </summary>
        public bool MovementCaptured { get; set; }

        /// <summary>The actions <see cref="MovementCaptured"/> takes: moving, turning, strafing and the camera's pitch, yaw and zoom.</summary>
        private static bool IsCapturedAction(InputAction action) =>
            action is >= InputAction.MoveForward and <= InputAction.StrafeRight or >= InputAction.CameraPitchUp and <= InputAction.CameraZoomOut;

        private GamepadButton _padIgnored;

        /// <summary>
        /// Treats the gamepad buttons held now as already handled until they are let go: called when this character takes
        /// the pad from another (Next Character, a focus change), so a button still down from the other character, LB
        /// included, does not act again here (round 1: holding LB cycled through every character).
        /// </summary>
        public void IgnoreHeldGamepadButtons()
        {
            lock (_lock) _padIgnored = (GamepadButton)uint.MaxValue;
        }

        public GamepadState CurrentGamepad
        {
            get
            {
                lock (_lock)
                {
                    if (!MovementCaptured) return _gamepadState;
                    var pad = _gamepadState;
                    return new GamepadState(pad.IsConnected, pad.Buttons, System.Numerics.Vector2.Zero, System.Numerics.Vector2.Zero,
                        pad.LeftTrigger, pad.RightTrigger, pad.PacketNumber, pad.DeviceName);
                }
            }
        }

        /// <summary>The gamepad as read, sticks included, whatever <see cref="MovementCaptured"/> says.</summary>
        public GamepadState RawGamepad
        {
            get { lock (_lock) return _gamepadState; }
        }

        public float MouseDeltaX
        {
            get { lock (_lock) return _mouseDeltaX; }
        }

        public float MouseDeltaY
        {
            get { lock (_lock) return _mouseDeltaY; }
        }

        public float MouseWheelDelta
        {
            get { lock (_lock) return _mouseWheelDelta; }
        }

        public InputModifiers CurrentModifiers
        {
            get { lock (_lock) return _modifiers; }
        }

        public MouseButton CurrentMouseButtons
        {
            get { lock (_lock) return _heldMouseButtons; }
        }

        #region Physical Input Ingestion

        public void SetKeyDown(GordianKey key)
        {
            if (key == GordianKey.None) return;

            lock (_lock)
            {
                UsingGamepad = false;
                _heldKeys.Add(key);
                UpdateModifierForKey(key, true);
            }
        }

        public void SetKeyUp(GordianKey key)
        {
            if (key == GordianKey.None) return;

            lock (_lock)
            {
                _heldKeys.Remove(key);
                UpdateModifierForKey(key, false);
            }
        }

        public void SetModifiers(InputModifiers modifiers)
        {
            lock (_lock)
            {
                _modifiers = modifiers;
            }
        }

        public void SetMouseButtonDown(MouseButton button)
        {
            if (button == MouseButton.None) return;

            lock (_lock)
            {
                _heldMouseButtons |= button;
            }
        }

        public void SetMouseButtonUp(MouseButton button)
        {
            if (button == MouseButton.None) return;

            lock (_lock)
            {
                _heldMouseButtons &= ~button;
            }
        }

        public void AddMouseDelta(float dx, float dy)
        {
            lock (_lock)
            {
                _mouseDeltaX += dx;
                _mouseDeltaY += dy;
            }
        }

        public void AddMouseWheel(float delta)
        {
            lock (_lock)
            {
                _mouseWheelDelta += delta;
            }
        }

        /// <summary>
        /// Whether the gamepad was the last device used (a button pressed or a stick pushed), false once a key is pressed:
        /// the menus' button hints show the keyboard key or the pad button by it (#360).
        /// </summary>
        public bool UsingGamepad { get; private set; }

        public void SetGamepadState(GamepadState state)
        {
            lock (_lock)
            {
                // A button newly down or a stick pushed hard says the pad is the device in use (#360).
                if ((state.Buttons & ~_gamepadState.Buttons) != GamepadButton.None
                    || state.LeftThumb.LengthSquared() > 0.25f || state.RightThumb.LengthSquared() > 0.25f)
                {
                    UsingGamepad = true;
                }
                _gamepadState = state;
            }
        }

        public void Reset()
        {
            lock (_lock)
            {
                _heldKeys.Clear();
                _previousKeys.Clear();
                _pressedKeys.Clear();
                _modifiers = InputModifiers.None;
                _heldMouseButtons = MouseButton.None;
                _gamepadState = GamepadState.Disconnected;
                _mouseDeltaX = 0;
                _mouseDeltaY = 0;
                _mouseWheelDelta = 0;
                _heldActions.Clear();
                _triggeredActions.Clear();
                _releasedActions.Clear();
                _previousActions.Clear();
            }
        }

        private void UpdateModifierForKey(GordianKey key, bool pressed)
        {
            InputModifiers mod = key switch
            {
                GordianKey.LeftShift or GordianKey.RightShift => InputModifiers.Shift,
                GordianKey.LeftCtrl or GordianKey.RightCtrl => InputModifiers.Control,
                GordianKey.LeftAlt or GordianKey.RightAlt => InputModifiers.Alt,
                GordianKey.LeftSuper or GordianKey.RightSuper => InputModifiers.Super,
                _ => InputModifiers.None
            };

            if (mod != InputModifiers.None)
            {
                if (pressed) _modifiers |= mod;
                else _modifiers &= ~mod;
            }
        }

        #endregion

        #region Frame Evaluation

        /// <summary>
        /// Evaluates physical inputs against the specified <see cref="InputProfile"/>,
        /// resolving active, triggered, and released logical actions for the current tick.
        /// Resets transient mouse impulses.
        /// </summary>
        public void Update(InputProfile profile, TimeSpan elapsed)
        {
            ArgumentNullException.ThrowIfNull(profile);

            lock (_lock)
            {
                bool menuContext = MenuContext;
                _previousActions.Clear();
                foreach (var a in _heldActions)
                {
                    _previousActions.Add(a);
                }

                _heldActions.Clear();
                _triggeredActions.Clear();
                _releasedActions.Clear();
                _gamepadActions.Clear();

                // 0. Keys that went down since the last update (the macro palette's Ctrl/Alt + Shift + digit, arrows).
                _pressedKeys.Clear();
                foreach (var key in _heldKeys)
                {
                    if (!_previousKeys.Contains(key)) _pressedKeys.Add(key);
                }
                _previousKeys.Clear();
                foreach (var key in _heldKeys) _previousKeys.Add(key);

                // 1. Evaluate Keyboard key chords
                foreach (var key in _heldKeys)
                {
                    // Chord with exact modifiers
                    var chord = new InputChord(key, _modifiers);
                    if (profile.TryGetAction(chord, menuContext, out var act))
                    {
                        _heldActions.Add(act);
                    }
                    // If no modifier match and modifiers were pressed, also check modifier-free chord. Ctrl or Alt held
                    // with a macro palette key (digits, arrows, Enter) is the palette's (#339): Ctrl+Up changes the
                    // macro set rather than also turning the camera.
                    else if (_modifiers != InputModifiers.None && !IsMacroPaletteKey(key, _modifiers))
                    {
                        var plainChord = new InputChord(key, InputModifiers.None);
                        if (profile.TryGetAction(plainChord, menuContext, out var plainAct))
                        {
                            _heldActions.Add(plainAct);
                        }
                    }
                }

                // 2. Evaluate Mouse button chords
                if (_heldMouseButtons != MouseButton.None)
                {
                    Span<MouseButton> buttons = stackalloc MouseButton[]
                    {
                        MouseButton.Left, MouseButton.Right, MouseButton.Middle, MouseButton.XButton1, MouseButton.XButton2
                    };

                    for (int i = 0; i < buttons.Length; i++)
                    {
                        var btn = buttons[i];
                        if (_heldMouseButtons.HasFlag(btn))
                        {
                            var chord = new InputChord(btn, _modifiers);
                            if (profile.TryGetAction(chord, menuContext, out var act))
                            {
                                _heldActions.Add(act);
                            }
                            else if (_modifiers != InputModifiers.None)
                            {
                                var plainChord = new InputChord(btn, InputModifiers.None);
                                if (profile.TryGetAction(plainChord, menuContext, out var plainAct))
                                {
                                    _heldActions.Add(plainAct);
                                }
                            }
                        }
                    }
                }

                // 3. Evaluate Gamepad buttons & triggers
                GamepadButton padButtons = GamepadButton.None;
                if (_gamepadState.IsConnected)
                {
                    GamepadButton effectiveButtons = _gamepadState.Buttons;
                    float trigThresh = profile.GamepadSettings?.TriggerThreshold ?? 0.15f;
                    if (_gamepadState.LeftTrigger >= trigThresh)
                    {
                        effectiveButtons |= GamepadButton.LeftTrigger;
                    }
                    if (_gamepadState.RightTrigger >= trigThresh)
                    {
                        effectiveButtons |= GamepadButton.RightTrigger;
                    }

                    padButtons = effectiveButtons;
                    // Buttons held when this character took the pad stay quiet until let go (IgnoreHeldGamepadButtons).
                    _padIgnored &= effectiveButtons;
                    GamepadButton acting = effectiveButtons & ~_padIgnored;
                    if (acting != GamepadButton.None && !SuppressGamepadActions)
                    {
                        Span<InputAction> padActs = stackalloc InputAction[32];
                        int count = 0;
                        bool palette = false;
                        uint btnBits = (uint)acting;
                        for (int i = 0; i < 32; i++)
                        {
                            uint mask = 1u << i;
                            if ((btnBits & mask) == 0) continue;
                            var padBtn = (GamepadButton)mask;
                            if (profile.TryGetAction(new InputChord(padBtn, _modifiers), menuContext, out var act)
                                || (_modifiers != InputModifiers.None && profile.TryGetAction(new InputChord(padBtn, InputModifiers.None), menuContext, out act)))
                            {
                                padActs[count++] = act;
                                palette |= InputProfile.IsMacroPaletteAction(act);
                            }
                        }
                        // A held macro palette (retail: hold LT / RT, then a button) takes the pad's other buttons, so
                        // LT + A does not also confirm; the macro controller reads them from GamepadButtonsPressed (#339).
                        for (int i = 0; i < count; i++)
                        {
                            if (palette && !InputProfile.IsMacroPaletteAction(padActs[i])) continue;
                            _heldActions.Add(padActs[i]);
                            _gamepadActions.Add(padActs[i]);
                        }
                    }
                }
                else
                {
                    _padIgnored = GamepadButton.None;
                }

                _padButtonsPressed = padButtons & ~_padButtons;
                _padButtons = padButtons;

                // 4. Compute Triggered (just pressed this frame) and Released
                foreach (var a in _heldActions)
                {
                    if (!_previousActions.Contains(a))
                    {
                        _triggeredActions.Add(a);
                    }
                }

                foreach (var a in _previousActions)
                {
                    if (!_heldActions.Contains(a))
                    {
                        _releasedActions.Add(a);
                    }
                }

                // 4. Update Autorun and Walk/Run toggles
                if (_triggeredActions.Contains(InputAction.ToggleAutorun))
                {
                    AutorunActive = !AutorunActive;
                }

                // Backward movement cancels autorun
                if (_heldActions.Contains(InputAction.MoveBackward))
                {
                    AutorunActive = false;
                }

                if (_triggeredActions.Contains(InputAction.ToggleWalkRun))
                {
                    IsWalking = !IsWalking;
                }
            }
        }

        /// <summary>
        /// Whether a key held with these modifiers belongs to the macro palette (#339): with Ctrl or Alt held, the digits
        /// (run a macro, with Shift change the set), the arrows (change the set, pick a macro) and Enter (run the picked
        /// macro). Such keys do not fall back to their unmodified binding.
        /// </summary>
        public static bool IsMacroPaletteKey(GordianKey key, InputModifiers modifiers) =>
            (modifiers & (InputModifiers.Control | InputModifiers.Alt)) != 0
            && key is (>= GordianKey.D0 and <= GordianKey.D9) or GordianKey.Up or GordianKey.Down or GordianKey.Left or GordianKey.Right
                or GordianKey.Enter or GordianKey.NumPadEnter;

        /// <summary>
        /// Atomically retrieves the accumulated mouse delta and wheel offsets and resets them to zero.
        /// </summary>
        public void ConsumeMouseDeltas(out float dx, out float dy, out float wheel)
        {
            lock (_lock)
            {
                dx = _mouseDeltaX;
                dy = _mouseDeltaY;
                wheel = _mouseWheelDelta;
                _mouseDeltaX = 0;
                _mouseDeltaY = 0;
                _mouseWheelDelta = 0;
            }
        }

        #endregion

        #region Action Queries

        public bool IsActionHeld(InputAction action)
        {
            lock (_lock) return (!MovementCaptured || !IsCapturedAction(action)) && _heldActions.Contains(action);
        }

        /// <summary>Whether the action is held, whatever <see cref="MovementCaptured"/> says.</summary>
        public bool IsActionHeldRaw(InputAction action)
        {
            lock (_lock) return _heldActions.Contains(action);
        }

        public bool WasActionTriggered(InputAction action)
        {
            lock (_lock) return _triggeredActions.Contains(action);
        }

        /// <summary>
        /// Whether <paramref name="action"/> was triggered this frame and a gamepad button holds it (retail gives some
        /// actions an extra meaning on the pad, such as Confirm targeting the closest thing when nothing is targeted).
        /// </summary>
        public bool WasActionTriggeredByGamepad(InputAction action)
        {
            lock (_lock) return _triggeredActions.Contains(action) && _gamepadActions.Contains(action);
        }

        public bool WasActionReleased(InputAction action)
        {
            lock (_lock) return _releasedActions.Contains(action);
        }

        /// <summary>Whether a key went down since the previous <see cref="Update"/>.</summary>
        public bool WasKeyPressed(GordianKey key)
        {
            lock (_lock) return _pressedKeys.Contains(key);
        }

        public bool IsKeyHeld(GordianKey key)
        {
            lock (_lock) return _heldKeys.Contains(key);
        }

        public bool IsMouseButtonHeld(MouseButton button)
        {
            lock (_lock) return _heldMouseButtons.HasFlag(button);
        }

        public List<InputAction> GetCurrentHeldActions()
        {
            lock (_lock) return new List<InputAction>(_heldActions);
        }

        public List<GordianKey> GetCurrentHeldKeys()
        {
            lock (_lock) return new List<GordianKey>(_heldKeys);
        }

        #endregion
    }
}
