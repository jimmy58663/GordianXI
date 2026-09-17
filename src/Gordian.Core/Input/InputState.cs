// src/Gordian.Core/Input/InputState.cs
// Clean-room input aggregator and frame state evaluator for GordianXI.

using System;
using System.Collections.Generic;

namespace Gordian.Core.Input
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
        private InputModifiers _modifiers = InputModifiers.None;
        private MouseButton _heldMouseButtons = MouseButton.None;

        // Mouse motion impulses
        private float _mouseDeltaX;
        private float _mouseDeltaY;
        private float _mouseWheelDelta;

        // Logical actions evaluated against active profile
        private readonly HashSet<InputAction> _previousActions = new HashSet<InputAction>();
        private readonly HashSet<InputAction> _heldActions = new HashSet<InputAction>();
        private readonly HashSet<InputAction> _triggeredActions = new HashSet<InputAction>();
        private readonly HashSet<InputAction> _releasedActions = new HashSet<InputAction>();

        // High-level movement state toggles
        public bool AutorunActive { get; set; }
        public bool IsWalking { get; set; }

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

        public void Reset()
        {
            lock (_lock)
            {
                _heldKeys.Clear();
                _modifiers = InputModifiers.None;
                _heldMouseButtons = MouseButton.None;
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
                _previousActions.Clear();
                foreach (var a in _heldActions)
                {
                    _previousActions.Add(a);
                }

                _heldActions.Clear();
                _triggeredActions.Clear();
                _releasedActions.Clear();

                // 1. Evaluate Keyboard key chords
                foreach (var key in _heldKeys)
                {
                    // Chord with exact modifiers
                    var chord = new InputChord(key, _modifiers);
                    if (profile.TryGetAction(chord, out var act))
                    {
                        _heldActions.Add(act);
                    }
                    // If no modifier match and modifiers were pressed, also check modifier-free chord
                    else if (_modifiers != InputModifiers.None)
                    {
                        var plainChord = new InputChord(key, InputModifiers.None);
                        if (profile.TryGetAction(plainChord, out var plainAct))
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
                            if (profile.TryGetAction(chord, out var act))
                            {
                                _heldActions.Add(act);
                            }
                            else if (_modifiers != InputModifiers.None)
                            {
                                var plainChord = new InputChord(btn, InputModifiers.None);
                                if (profile.TryGetAction(plainChord, out var plainAct))
                                {
                                    _heldActions.Add(plainAct);
                                }
                            }
                        }
                    }
                }

                // 3. Compute Triggered (just pressed this frame) and Released
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
            lock (_lock) return _heldActions.Contains(action);
        }

        public bool WasActionTriggered(InputAction action)
        {
            lock (_lock) return _triggeredActions.Contains(action);
        }

        public bool WasActionReleased(InputAction action)
        {
            lock (_lock) return _releasedActions.Contains(action);
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
