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
        public bool TryGetAction(InputChord chord, out InputAction action)
        {
            foreach (var kvp in Bindings)
            {
                if (kvp.Value.Contains(chord))
                {
                    action = kvp.Key;
                    return true;
                }
            }
            action = InputAction.None;
            return false;
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
        /// Deep clones this input profile.
        /// </summary>
        public InputProfile Clone()
        {
            var clone = new InputProfile(Name, Description)
            {
                Version = Version,
                MouseSensitivityX = MouseSensitivityX,
                MouseSensitivityY = MouseSensitivityY,
                InvertMouseY = InvertMouseY,
                MouseWheelZoomStep = MouseWheelZoomStep,
                WalkSpeed = WalkSpeed,
                RunSpeed = RunSpeed,
                TurnSpeedDegreesPerSec = TurnSpeedDegreesPerSec
            };

            foreach (var kvp in Bindings)
            {
                clone.Bindings[kvp.Key] = new List<InputChord>(kvp.Value);
            }

            return clone;
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
            p.Bind(InputAction.ToggleWalkRun, new InputChord(GordianKey.OemSlash));

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
            p.Bind(InputAction.TargetSelf, new InputChord(GordianKey.F1));
            p.Bind(InputAction.TargetParty1, new InputChord(GordianKey.F2));
            p.Bind(InputAction.TargetParty2, new InputChord(GordianKey.F3));
            p.Bind(InputAction.TargetParty3, new InputChord(GordianKey.F4));
            p.Bind(InputAction.TargetParty4, new InputChord(GordianKey.F5));
            p.Bind(InputAction.TargetParty5, new InputChord(GordianKey.F6));
            p.Bind(InputAction.OpenMenu, new InputChord(GordianKey.OemMinus));
            p.Bind(InputAction.OpenMenu, new InputChord(GordianKey.M));
            p.Bind(InputAction.OpenChat, new InputChord(GordianKey.OemSlash));

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
            p.Bind(InputAction.ToggleWalkRun, new InputChord(GordianKey.OemSlash));

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
            p.Bind(InputAction.TargetNearest, new InputChord(GordianKey.NumPadAdd));
            p.Bind(InputAction.TargetPrevious, new InputChord(GordianKey.Tab, InputModifiers.Shift));
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
            p.Bind(InputAction.ToggleAutorun, new InputChord(GamepadButton.Y));

            p.Bind(InputAction.ToggleWalkRun, new InputChord(GamepadButton.LeftThumb));
            p.Bind(InputAction.ResetCamera, new InputChord(GamepadButton.RightThumb));

            p.Bind(InputAction.TargetPrevious, new InputChord(GamepadButton.LeftTrigger));
            p.Bind(InputAction.TargetNearest, new InputChord(GamepadButton.RightTrigger));

            p.Bind(InputAction.OpenMenu, new InputChord(GamepadButton.Start));
            p.Bind(InputAction.OpenChat, new InputChord(GamepadButton.Back));

            p.Bind(InputAction.TargetParty1, new InputChord(GamepadButton.DPadUp));
            p.Bind(InputAction.TargetParty2, new InputChord(GamepadButton.DPadDown));
            p.Bind(InputAction.TargetPrevious, new InputChord(GamepadButton.DPadLeft));
            p.Bind(InputAction.TargetNearest, new InputChord(GamepadButton.DPadRight));

            p.Bind(InputAction.MacroCtrl1, new InputChord(GamepadButton.LeftShoulder));
            p.Bind(InputAction.MacroAlt1, new InputChord(GamepadButton.RightShoulder));
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
            return JsonSerializer.Deserialize<InputProfile>(json, JsonOptions)
                   ?? throw new InvalidOperationException("Failed to deserialize InputProfile from JSON.");
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
