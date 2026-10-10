// src/Gordian.Core/Input/InputProfile.cs
// Clean-room input profile and preset mapping engine for GordianXI.
// Standard FFXI Compact and Full keyboard layout specifications referenced.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Gordian.Core.Diagnostics;

namespace Gordian.Core.Input
{
    /// <summary>
    /// Encapsulates a complete, rebindable mapping configuration between physical input chords
    /// and logical gameplay actions, along with sensitivity and locomotion parameters.
    /// Supports JSON persistence and factory presets for FFXI Compact and Full Numpad layouts.
    /// </summary>
    public sealed class InputProfile
    {
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        [JsonPropertyName("name")]
        public string Name { get; set; } = "Custom";

        [JsonPropertyName("version")]
        public int Version { get; set; } = 1;

        [JsonPropertyName("description")]
        public string Description { get; set; } = string.Empty;

        [JsonPropertyName("bindings")]
        public Dictionary<InputAction, List<InputChord>> Bindings { get; set; } = new Dictionary<InputAction, List<InputChord>>();

        // Camera & Mouse Settings
        [JsonPropertyName("mouseSensitivityX")]
        public float MouseSensitivityX { get; set; } = 1.0f;

        [JsonPropertyName("mouseSensitivityY")]
        public float MouseSensitivityY { get; set; } = 1.0f;

        [JsonPropertyName("invertMouseX")]
        public bool InvertMouseX { get; set; } = false;

        [JsonPropertyName("invertMouseY")]
        public bool InvertMouseY { get; set; } = false;

        [JsonPropertyName("mouseWheelZoomStep")]
        public float MouseWheelZoomStep { get; set; } = 1.0f;

        /// <summary>
        /// When on, the first-person view uses its own mouse sensitivity and inversion below (#306); off (the default),
        /// first person uses the same values as third person, as before. Retail's Mouse/Cam. config page has separate
        /// first- and third-person axis rows; choosing a first-person row there turns this on for the session.
        /// </summary>
        [JsonPropertyName("separateFirstPersonMouse")]
        public bool SeparateFirstPersonMouse { get; set; } = false;

        [JsonPropertyName("firstPersonMouseSensitivityX")]
        public float FirstPersonMouseSensitivityX { get; set; } = 1.0f;

        [JsonPropertyName("firstPersonMouseSensitivityY")]
        public float FirstPersonMouseSensitivityY { get; set; } = 1.0f;

        [JsonPropertyName("firstPersonInvertMouseX")]
        public bool FirstPersonInvertMouseX { get; set; } = false;

        [JsonPropertyName("firstPersonInvertMouseY")]
        public bool FirstPersonInvertMouseY { get; set; } = false;

        /// <summary>The mouse camera settings for the view: the first-person set when it is separate, else the shared one.</summary>
        public CameraAxisSettings MouseCamera(bool firstPerson) => firstPerson && SeparateFirstPersonMouse
            ? new CameraAxisSettings(FirstPersonMouseSensitivityX, FirstPersonMouseSensitivityY, FirstPersonInvertMouseX, FirstPersonInvertMouseY)
            : new CameraAxisSettings(MouseSensitivityX, MouseSensitivityY, InvertMouseX, InvertMouseY);

        // Locomotion Settings
        [JsonPropertyName("walkSpeed")]
        public byte WalkSpeed { get; set; } = 25; // 2.5 yalms/sec

        [JsonPropertyName("runSpeed")]
        public byte RunSpeed { get; set; } = 50; // 5.0 yalms/sec

        [JsonPropertyName("turnSpeedDegreesPerSec")]
        public float TurnSpeedDegreesPerSec { get; set; } = 180.0f;

        // Gamepad Settings
        [JsonPropertyName("gamepadSettings")]
        public GamepadSettings GamepadSettings { get; set; } = new GamepadSettings();

        public InputProfile()
        {
        }

        public InputProfile(string name, string description)
        {
            Name = name;
            Description = description;
        }

        /// <summary>
        /// Binds an input chord to the specified gameplay action.
        /// </summary>
        public void Bind(InputAction action, InputChord chord)
        {
            if (action == InputAction.None || chord.IsEmpty) return;

            if (!Bindings.TryGetValue(action, out var chords))
            {
                chords = new List<InputChord>();
                Bindings[action] = chords;
            }

            if (!chords.Contains(chord))
            {
                chords.Add(chord);
            }
        }

        /// <summary>
        /// Removes a specific binding chord from an action.
        /// </summary>
        public bool Unbind(InputAction action, InputChord chord)
        {
            if (Bindings.TryGetValue(action, out var chords))
            {
                return chords.Remove(chord);
            }
            return false;
        }

        /// <summary>
        /// Clears all bindings for the specified action.
        /// </summary>
        public void ClearAction(InputAction action)
        {
            if (Bindings.TryGetValue(action, out var chords))
            {
                chords.Clear();
            }
        }

        /// <summary>
        /// Retrieves all assigned input chords for an action.
        /// </summary>
        public IReadOnlyList<InputChord> GetChords(InputAction action)
        {
            if (Bindings.TryGetValue(action, out var chords))
            {
                return chords;
            }
            return Array.Empty<InputChord>();
        }

        /// <summary>
        /// Resolves an input chord to its mapped action.
        /// Returns the first matching action if bound to multiple.
        /// </summary>
        public bool TryGetAction(InputChord chord, out InputAction action) => TryGetAction(chord, false, out action);

        /// <summary>
        /// Resolves an input chord to its mapped action for a context. The menu navigation actions
        /// (<see cref="InputAction.MenuUp"/>..<see cref="InputAction.MenuRight"/>) share their keys with the camera
        /// and party-targeting bindings, as in the retail client: while a stock menu is open
        /// (<paramref name="menuContext"/>) they win, otherwise they are skipped.
        /// </summary>
        public bool TryGetAction(InputChord chord, bool menuContext, out InputAction action)
        {
            InputAction fallback = InputAction.None;
            foreach (var kvp in Bindings)
            {
                if (!kvp.Value.Contains(chord)) continue;
                bool isMenu = IsMenuNavigationAction(kvp.Key);
                if (isMenu == menuContext)
                {
                    action = kvp.Key;
                    return true;
                }
                if (!isMenu && fallback == InputAction.None) fallback = kvp.Key;
            }
            action = fallback;
            return fallback != InputAction.None;
        }

        /// <summary>True for the actions that move the cursor of an open stock menu.</summary>
        public static bool IsMenuNavigationAction(InputAction action) =>
            action is InputAction.MenuUp or InputAction.MenuDown or InputAction.MenuLeft or InputAction.MenuRight;

        /// <summary>
        /// Binds the retail menu navigation keys (arrows, the numeric keypad's 8/2/4/6 and the gamepad d-pad) when
        /// the profile has none, so profiles saved before menus existed still navigate them.
        /// </summary>
        public void EnsureMenuNavigationBindings()
        {
            if (GetChords(InputAction.MenuUp).Count > 0 || GetChords(InputAction.MenuDown).Count > 0) return;
            BindMenuNavigationDefaults(this);
        }

        /// <summary>
        /// Gives a profile saved before lock-on had keys (or with them unbound) the lock-on keys, so the toggle works (#137).
        /// </summary>
        public void EnsureLockOnBindings()
        {
            if (GetChords(InputAction.ToggleLockOn).Count > 0) return;
            Bind(InputAction.ToggleLockOn, new InputChord(GordianKey.NumPadMultiply));
            Bind(InputAction.ToggleLockOn, new InputChord(GordianKey.T));
        }

        /// <summary>
        /// Moves the gamepad d-pad left / right of a profile saved before the target cursor actions existed from Target
        /// Previous / Target Nearest (Shift+Tab / Tab) to <see cref="InputAction.TargetCursorLeft"/> /
        /// <see cref="InputAction.TargetCursorRight"/>, which cycle through yourself as retail's d-pad does.
        /// </summary>
        public void EnsureTargetCursorBindings()
        {
            if (GetChords(InputAction.TargetCursorLeft).Count > 0 || GetChords(InputAction.TargetCursorRight).Count > 0) return;
            foreach (var (old, chord, action) in new[]
            {
                (InputAction.TargetPrevious, new InputChord(GamepadButton.DPadLeft), InputAction.TargetCursorLeft),
                (InputAction.TargetNearest, new InputChord(GamepadButton.DPadRight), InputAction.TargetCursorRight),
            })
            {
                if (Bindings.TryGetValue(old, out var chords) && chords.Remove(chord)) Bind(action, chord);
            }
        }

        /// <summary>
        /// Gives a profile saved before the log windows could be selected the retail keys for it (keypad +, gamepad Y),
        /// taking them from the actions the old presets gave them (Target Nearest, Autorun).
        /// </summary>
        public void EnsureLogWindowBindings()
        {
            if (GetChords(InputAction.CycleLogWindow).Count > 0) return;
            foreach (var (old, chord) in new[]
            {
                (InputAction.TargetNearest, new InputChord(GordianKey.NumPadAdd)),
                (InputAction.ToggleAutorun, new InputChord(GamepadButton.Y)),
            })
            {
                if (Bindings.TryGetValue(old, out var chords)) chords.Remove(chord);
                Bind(InputAction.CycleLogWindow, chord);
            }
        }

        /// <summary>
        /// Gives a profile saved before #326 the new gamepad shortcuts: L3 (was Toggle Walk/Run) toggles lock-on, R1 (was
        /// macro Alt+1) toggles auto-run and L1 (was macro Ctrl+1) cycles the character in the primary view. Only buttons
        /// still on their old default move; a profile that already has a <see cref="InputAction.CycleCharacter"/>
        /// binding (or chose to have none, which saves an empty list) is left alone.
        /// </summary>
        public void EnsureGamepadShortcutBindings()
        {
            if (Bindings.ContainsKey(InputAction.CycleCharacter)) return;
            foreach (var (old, button, action) in new[]
            {
                (InputAction.ToggleWalkRun, GamepadButton.LeftThumb, InputAction.ToggleLockOn),
                (InputAction.MacroAlt1, GamepadButton.RightShoulder, InputAction.ToggleAutorun),
                (InputAction.MacroCtrl1, GamepadButton.LeftShoulder, InputAction.CycleCharacter),
            })
            {
                var chord = new InputChord(button);
                if (Bindings.TryGetValue(old, out var chords) && chords.Remove(chord)) Bind(action, chord);
            }
            if (!Bindings.ContainsKey(InputAction.CycleCharacter)) Bindings[InputAction.CycleCharacter] = new List<InputChord>();
        }

        private static void BindMenuNavigationDefaults(InputProfile p)
        {
            p.Bind(InputAction.MenuUp, new InputChord(GordianKey.Up));
            p.Bind(InputAction.MenuDown, new InputChord(GordianKey.Down));
            p.Bind(InputAction.MenuLeft, new InputChord(GordianKey.Left));
            p.Bind(InputAction.MenuRight, new InputChord(GordianKey.Right));
            p.Bind(InputAction.MenuUp, new InputChord(GordianKey.NumPad8));
            p.Bind(InputAction.MenuDown, new InputChord(GordianKey.NumPad2));
            p.Bind(InputAction.MenuLeft, new InputChord(GordianKey.NumPad4));
            p.Bind(InputAction.MenuRight, new InputChord(GordianKey.NumPad6));
            p.Bind(InputAction.MenuUp, new InputChord(GamepadButton.DPadUp));
            p.Bind(InputAction.MenuDown, new InputChord(GamepadButton.DPadDown));
            p.Bind(InputAction.MenuLeft, new InputChord(GamepadButton.DPadLeft));
            p.Bind(InputAction.MenuRight, new InputChord(GamepadButton.DPadRight));
        }

        /// <summary>
        /// Replaces this profile's keyboard/mouse bindings and mouse/locomotion settings with
        /// those from <paramref name="source"/> (typically a fresh Compact/Full preset), while
        /// leaving this profile's current gamepad button bindings and <see cref="GamepadSettings"/>
        /// completely untouched. Switching keyboard layout must never disturb gamepad mappings.
        /// </summary>
        public void ReplaceKeyboardBindings(InputProfile source)
        {
            ArgumentNullException.ThrowIfNull(source);

            foreach (var chords in Bindings.Values)
            {
                chords.RemoveAll(chord => !chord.IsGamepadChord);
            }

            foreach (var kvp in source.Bindings)
            {
                foreach (var chord in kvp.Value)
                {
                    if (chord.IsGamepadChord) continue;
                    Bind(kvp.Key, chord);
                }
            }

            Name = source.Name;
            Description = source.Description;
            MouseSensitivityX = source.MouseSensitivityX;
            MouseSensitivityY = source.MouseSensitivityY;
            InvertMouseX = source.InvertMouseX;
            InvertMouseY = source.InvertMouseY;
            MouseWheelZoomStep = source.MouseWheelZoomStep;
            WalkSpeed = source.WalkSpeed;
            RunSpeed = source.RunSpeed;
            TurnSpeedDegreesPerSec = source.TurnSpeedDegreesPerSec;
        }

        /// <summary>
        /// Resets this profile's gamepad button bindings and <see cref="GamepadSettings"/> to the
        /// standard defaults, without touching any keyboard or mouse bindings. Gamepad mapping is
        /// a secondary, independent layer on top of whichever keyboard layout is active.
        /// </summary>
        public void ApplyGamepadDefaults()
        {
            foreach (var chords in Bindings.Values)
            {
                chords.RemoveAll(chord => chord.IsGamepadChord);
            }

            BindDefaultGamepadButtons(this);
            GamepadSettings = new GamepadSettings();
        }

        /// <summary>
        /// Deep clones this input profile: every binding and setting, the gamepad settings included (a JSON round trip, so
        /// settings added later are never left out). Each multi-boxed character runs on its own copy of the user's
        /// profile (#314): the session applies its server-stored config choices to its copy only.
        /// </summary>
        public InputProfile Clone()
        {
            return JsonSerializer.Deserialize<InputProfile>(SaveToJson(indented: false), JsonOptions)
                   ?? throw new InvalidOperationException("Failed to clone InputProfile.");
        }

        #region Standard FFXI Factory Presets

        /// <summary>
        /// Creates the standard FFXI Compact keyboard profile (WASD locomotion, Q/E strafe, IJKL camera).
        /// </summary>
        public static InputProfile CreateCompact()
        {
            var p = new InputProfile("Compact (WASD)", "Standard FFXI Compact keyboard layout with WASD movement and IJKL camera.");

            // Locomotion
            p.Bind(InputAction.MoveForward, new InputChord(GordianKey.W));
            p.Bind(InputAction.MoveBackward, new InputChord(GordianKey.S));
            p.Bind(InputAction.TurnLeft, new InputChord(GordianKey.A));
            p.Bind(InputAction.TurnRight, new InputChord(GordianKey.D));
            p.Bind(InputAction.StrafeLeft, new InputChord(GordianKey.Q));
            p.Bind(InputAction.StrafeRight, new InputChord(GordianKey.E));
            p.Bind(InputAction.ToggleAutorun, new InputChord(GordianKey.R));
            p.Bind(InputAction.ToggleWalkRun, new InputChord(GordianKey.NumPadDivide));

            // Camera
            p.Bind(InputAction.CameraPitchUp, new InputChord(GordianKey.I));
            p.Bind(InputAction.CameraPitchUp, new InputChord(GordianKey.Up));
            p.Bind(InputAction.CameraPitchDown, new InputChord(GordianKey.K));
            p.Bind(InputAction.CameraPitchDown, new InputChord(GordianKey.Down));
            p.Bind(InputAction.CameraYawLeft, new InputChord(GordianKey.J));
            p.Bind(InputAction.CameraYawLeft, new InputChord(GordianKey.Left));
            p.Bind(InputAction.CameraYawRight, new InputChord(GordianKey.L));
            p.Bind(InputAction.CameraYawRight, new InputChord(GordianKey.Right));
            p.Bind(InputAction.CameraZoomIn, new InputChord(GordianKey.PageUp));
            p.Bind(InputAction.CameraZoomOut, new InputChord(GordianKey.PageDown));
            p.Bind(InputAction.ResetCamera, new InputChord(GordianKey.End));
            p.Bind(InputAction.ResetCamera, new InputChord(GordianKey.H));

            // Targeting & Interaction
            p.Bind(InputAction.Confirm, new InputChord(GordianKey.Enter));
            p.Bind(InputAction.Confirm, new InputChord(GordianKey.Space));
            p.Bind(InputAction.Cancel, new InputChord(GordianKey.Escape));
            p.Bind(InputAction.TargetNearest, new InputChord(GordianKey.Tab));
            p.Bind(InputAction.TargetNearest, new InputChord(GordianKey.F));
            p.Bind(InputAction.TargetPrevious, new InputChord(GordianKey.Tab, InputModifiers.Shift));
            p.Bind(InputAction.ToggleLockOn, new InputChord(GordianKey.NumPadMultiply));
            p.Bind(InputAction.ToggleLockOn, new InputChord(GordianKey.T));
            p.Bind(InputAction.TargetSelf, new InputChord(GordianKey.F1));
            p.Bind(InputAction.TargetParty1, new InputChord(GordianKey.F2));
            p.Bind(InputAction.TargetParty2, new InputChord(GordianKey.F3));
            p.Bind(InputAction.TargetParty3, new InputChord(GordianKey.F4));
            p.Bind(InputAction.TargetParty4, new InputChord(GordianKey.F5));
            p.Bind(InputAction.TargetParty5, new InputChord(GordianKey.F6));
            p.Bind(InputAction.OpenMenu, new InputChord(GordianKey.OemMinus));
            p.Bind(InputAction.OpenMenu, new InputChord(GordianKey.M));
            p.Bind(InputAction.OpenChat, new InputChord(GordianKey.OemSlash));
            p.Bind(InputAction.CycleLogWindow, new InputChord(GordianKey.NumPadAdd));
            BindMenuNavigationDefaults(p);

            // Macro Palettes
            BindDefaultMacros(p);

            // Gamepad default bindings
            BindDefaultGamepadButtons(p);

            return p;
        }

        /// <summary>
        /// Creates the standard FFXI Full keyboard profile (Numeric Keypad locomotion and camera).
        /// </summary>
        public static InputProfile CreateFullNumpad()
        {
            var p = new InputProfile("Full (Numpad)", "Standard FFXI Full keyboard layout centered around the numeric keypad.");

            // Locomotion
            p.Bind(InputAction.MoveForward, new InputChord(GordianKey.NumPad8));
            p.Bind(InputAction.MoveBackward, new InputChord(GordianKey.NumPad2));
            p.Bind(InputAction.TurnLeft, new InputChord(GordianKey.NumPad4));
            p.Bind(InputAction.TurnRight, new InputChord(GordianKey.NumPad6));
            p.Bind(InputAction.StrafeLeft, new InputChord(GordianKey.NumPad7));
            p.Bind(InputAction.StrafeRight, new InputChord(GordianKey.NumPad9));
            p.Bind(InputAction.ToggleAutorun, new InputChord(GordianKey.R));
            p.Bind(InputAction.ToggleAutorun, new InputChord(GordianKey.NumPad7, InputModifiers.Control));
            p.Bind(InputAction.ToggleWalkRun, new InputChord(GordianKey.NumPadDivide));

            // Camera
            p.Bind(InputAction.CameraPitchUp, new InputChord(GordianKey.Up));
            p.Bind(InputAction.CameraPitchDown, new InputChord(GordianKey.Down));
            p.Bind(InputAction.CameraYawLeft, new InputChord(GordianKey.Left));
            p.Bind(InputAction.CameraYawRight, new InputChord(GordianKey.Right));
            p.Bind(InputAction.CameraZoomIn, new InputChord(GordianKey.PageUp));
            p.Bind(InputAction.CameraZoomOut, new InputChord(GordianKey.PageDown));
            p.Bind(InputAction.ResetCamera, new InputChord(GordianKey.End));

            // Targeting & Interaction
            p.Bind(InputAction.Confirm, new InputChord(GordianKey.NumPadEnter));
            p.Bind(InputAction.Confirm, new InputChord(GordianKey.Enter));
            p.Bind(InputAction.Confirm, new InputChord(GordianKey.NumPad5));
            p.Bind(InputAction.Cancel, new InputChord(GordianKey.NumPad0));
            p.Bind(InputAction.Cancel, new InputChord(GordianKey.Escape));
            p.Bind(InputAction.TargetNearest, new InputChord(GordianKey.Tab));
            p.Bind(InputAction.TargetPrevious, new InputChord(GordianKey.Tab, InputModifiers.Shift));
            p.Bind(InputAction.ToggleLockOn, new InputChord(GordianKey.NumPadMultiply));
            p.Bind(InputAction.TargetSelf, new InputChord(GordianKey.F1));
            p.Bind(InputAction.TargetParty1, new InputChord(GordianKey.F2));
            p.Bind(InputAction.TargetParty2, new InputChord(GordianKey.F3));
            p.Bind(InputAction.TargetParty3, new InputChord(GordianKey.F4));
            p.Bind(InputAction.TargetParty4, new InputChord(GordianKey.F5));
            p.Bind(InputAction.TargetParty5, new InputChord(GordianKey.F6));
            p.Bind(InputAction.OpenMenu, new InputChord(GordianKey.NumPadSubtract));
            p.Bind(InputAction.OpenMenu, new InputChord(GordianKey.NumPadDecimal));
            p.Bind(InputAction.OpenChat, new InputChord(GordianKey.Space));
            p.Bind(InputAction.OpenChat, new InputChord(GordianKey.OemSlash));
            p.Bind(InputAction.CycleLogWindow, new InputChord(GordianKey.NumPadAdd));
            BindMenuNavigationDefaults(p);

            // Macro Palettes
            BindDefaultMacros(p);

            // Gamepad default bindings
            BindDefaultGamepadButtons(p);

            return p;
        }

        private static void BindDefaultMacros(InputProfile p)
        {
            p.Bind(InputAction.MacroCtrl1, new InputChord(GordianKey.D1, InputModifiers.Control));
            p.Bind(InputAction.MacroCtrl2, new InputChord(GordianKey.D2, InputModifiers.Control));
            p.Bind(InputAction.MacroCtrl3, new InputChord(GordianKey.D3, InputModifiers.Control));
            p.Bind(InputAction.MacroCtrl4, new InputChord(GordianKey.D4, InputModifiers.Control));
            p.Bind(InputAction.MacroCtrl5, new InputChord(GordianKey.D5, InputModifiers.Control));
            p.Bind(InputAction.MacroCtrl6, new InputChord(GordianKey.D6, InputModifiers.Control));
            p.Bind(InputAction.MacroCtrl7, new InputChord(GordianKey.D7, InputModifiers.Control));
            p.Bind(InputAction.MacroCtrl8, new InputChord(GordianKey.D8, InputModifiers.Control));
            p.Bind(InputAction.MacroCtrl9, new InputChord(GordianKey.D9, InputModifiers.Control));
            p.Bind(InputAction.MacroCtrl10, new InputChord(GordianKey.D0, InputModifiers.Control));

            p.Bind(InputAction.MacroAlt1, new InputChord(GordianKey.D1, InputModifiers.Alt));
            p.Bind(InputAction.MacroAlt2, new InputChord(GordianKey.D2, InputModifiers.Alt));
            p.Bind(InputAction.MacroAlt3, new InputChord(GordianKey.D3, InputModifiers.Alt));
            p.Bind(InputAction.MacroAlt4, new InputChord(GordianKey.D4, InputModifiers.Alt));
            p.Bind(InputAction.MacroAlt5, new InputChord(GordianKey.D5, InputModifiers.Alt));
            p.Bind(InputAction.MacroAlt6, new InputChord(GordianKey.D6, InputModifiers.Alt));
            p.Bind(InputAction.MacroAlt7, new InputChord(GordianKey.D7, InputModifiers.Alt));
            p.Bind(InputAction.MacroAlt8, new InputChord(GordianKey.D8, InputModifiers.Alt));
            p.Bind(InputAction.MacroAlt9, new InputChord(GordianKey.D9, InputModifiers.Alt));
            p.Bind(InputAction.MacroAlt10, new InputChord(GordianKey.D0, InputModifiers.Alt));
        }

        /// <summary>
        /// Creates a profile configured with authentic FFXI gamepad button mappings and defaults.
        /// Keyboard/mouse bindings fall back to the full Compact (WASD) layout rather than a
        /// partial subset, since gamepad mapping is meant to be a secondary, additive layer that
        /// never leaves keyboard coverage incomplete.
        /// </summary>
        public static InputProfile CreateGamepadDefault()
        {
            var p = CreateCompact();
            p.Name = "Gamepad (Standard)";
            p.Description = "Standard FFXI Gamepad layout for Xbox, PlayStation, and generic dual-analog controllers, with the full Compact (WASD) keyboard layout kept as a fallback.";
            return p;
        }

        public static void BindDefaultGamepadButtons(InputProfile p)
        {
            p.Bind(InputAction.Confirm, new InputChord(GamepadButton.A));
            p.Bind(InputAction.Cancel, new InputChord(GamepadButton.B));
            p.Bind(InputAction.OpenMenu, new InputChord(GamepadButton.X));
            p.Bind(InputAction.CycleLogWindow, new InputChord(GamepadButton.Y));

            // L3 toggles lock-on and R1 auto-run (the maintainer's request, #326); L1 shows the next character in the
            // primary view (GordianXI multi-box, like Ctrl+Tab). See EnsureGamepadShortcutBindings for saved profiles.
            p.Bind(InputAction.ToggleLockOn, new InputChord(GamepadButton.LeftThumb));
            p.Bind(InputAction.ResetCamera, new InputChord(GamepadButton.RightThumb));

            p.Bind(InputAction.TargetPrevious, new InputChord(GamepadButton.LeftTrigger));
            p.Bind(InputAction.TargetNearest, new InputChord(GamepadButton.RightTrigger));

            p.Bind(InputAction.OpenMenu, new InputChord(GamepadButton.Start));
            p.Bind(InputAction.OpenChat, new InputChord(GamepadButton.Back));

            p.Bind(InputAction.TargetParty1, new InputChord(GamepadButton.DPadUp));
            p.Bind(InputAction.TargetParty2, new InputChord(GamepadButton.DPadDown));
            p.Bind(InputAction.TargetCursorLeft, new InputChord(GamepadButton.DPadLeft));
            p.Bind(InputAction.TargetCursorRight, new InputChord(GamepadButton.DPadRight));

            p.Bind(InputAction.CycleCharacter, new InputChord(GamepadButton.LeftShoulder));
            p.Bind(InputAction.ToggleAutorun, new InputChord(GamepadButton.RightShoulder));
        }

        #endregion

        #region JSON Persistence

        public string SaveToJson(bool indented = true)
        {
            var options = indented ? JsonOptions : new JsonSerializerOptions(JsonOptions) { WriteIndented = false };
            return JsonSerializer.Serialize(this, options);
        }

        public static InputProfile FromJson(string json)
        {
            ArgumentNullException.ThrowIfNull(json);
            var profile = JsonSerializer.Deserialize<InputProfile>(json, JsonOptions)
                   ?? throw new InvalidOperationException("Failed to deserialize InputProfile from JSON.");
            profile.EnsureMenuNavigationBindings();
            profile.EnsureLogWindowBindings();
            profile.EnsureTargetCursorBindings();
            profile.EnsureLockOnBindings();
            profile.EnsureGamepadShortcutBindings();
            return profile;
        }

        public void SaveToFile(string filePath)
        {
            ArgumentNullException.ThrowIfNull(filePath);
            string? dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            string json = SaveToJson(indented: true);
            File.WriteAllText(filePath, json);
        }

        public static InputProfile LoadOrCreate(string filePath)
        {
            ArgumentNullException.ThrowIfNull(filePath);

            if (File.Exists(filePath))
            {
                try
                {
                    string json = File.ReadAllText(filePath);
                    return FromJson(json);
                }
                catch (Exception ex)
                {
                    GordianLog.Error("INPUT", $"Failed to load input profile from '{filePath}': {ex.Message}. Falling back to default.", ex);
                }
            }

            var defaultProfile = CreateCompact();
            try
            {
                defaultProfile.SaveToFile(filePath);
            }
            catch (Exception ex)
            {
                GordianLog.Warn("INPUT", $"Could not save default input profile to '{filePath}': {ex.Message}");
            }
            return defaultProfile;
        }

        #endregion
    }
}
