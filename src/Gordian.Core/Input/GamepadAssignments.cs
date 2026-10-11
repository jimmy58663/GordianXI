// src/Gordian.Core/Input/GamepadAssignments.cs
using System;
using System.Collections.Generic;
using System.Linq;

namespace Gordian.Core.Input
{
    /// <summary>
    /// One assignable gamepad function of the key-assignment list (#21): its label and the actions one button drives.
    /// A function can drive several actions that never compete because they live in different contexts (the d-pad's
    /// "Menu/Target Cursor Left" moves the menu cursor while a menu is open and the target cursor otherwise, as retail's
    /// function of that name does).
    /// </summary>
    /// <param name="Label">English label, used when the config row table is not read.</param>
    /// <param name="TextIndex">The config row table (ROM/165/74) row with retail's wording, or -1 for GordianXI functions.</param>
    /// <param name="Actions">The actions the assigned button drives.</param>
    public sealed record GamepadFunction(string Label, int TextIndex, params InputAction[] Actions)
    {
        /// <summary>Whether the function is used while a stock menu is open (menu navigation).</summary>
        public bool HasMenuContext => Actions.Any(InputProfile.IsMenuNavigationAction);

        /// <summary>Whether the function is used outside menus.</summary>
        public bool HasGameContext => Actions.Any(a => !InputProfile.IsMenuNavigationAction(a));

        /// <summary>Two functions compete for a button when they are used in the same context.</summary>
        public bool SharesContextWith(GamepadFunction other) =>
            (HasMenuContext && other.HasMenuContext) || (HasGameContext && other.HasGameContext);
    }

    /// <summary>
    /// The remappable gamepad binding model behind the gamepad key-assignment list (#21): the functions a button can be
    /// assigned to, which buttons each one has in a profile, and assignment with swapping so a button never drives two
    /// functions of the same context. Only plain gamepad chords (no keyboard modifier) are touched; keyboard and mouse
    /// bindings are never changed.
    /// <para>
    /// Retail's PC client has no in-game gamepad editor: the config list's Gamepad entry boots the separate FINAL FANTASY
    /// XI Gamepad Config program (help text 1092 in ROM/165/75), which assigns buttons to functions. The wording of the
    /// retail functions here is the config row table's (ROM/165/74 rows 89-142, the keyboard and gamepad assignment
    /// names); Lock On, Cycle Target, Reset Camera, Open Chat, the macro palettes, Log Out and Next Character use GordianXI's wording.
    /// </para>
    /// </summary>
    public static class GamepadAssignments
    {
        /// <summary>The functions, in list order.</summary>
        public static IReadOnlyList<GamepadFunction> Functions { get; } = new[]
        {
            new GamepadFunction("Confirm", 134, InputAction.Confirm),
            new GamepadFunction("Cancel", 135, InputAction.Cancel),
            new GamepadFunction("Open Main Menu", 140, InputAction.OpenMenu),
            new GamepadFunction("Select Window", 139, InputAction.CycleLogWindow),
            new GamepadFunction("Open Chat", -1, InputAction.OpenChat),
            // Two-purpose functions name both purposes, menu first, as retail's own "Menu/Target Cursor Left" does.
            new GamepadFunction("Menu Up / Target Party Up", -1, InputAction.MenuUp, InputAction.TargetParty1),
            new GamepadFunction("Menu Down / Target Party Down", -1, InputAction.MenuDown, InputAction.TargetParty2),
            new GamepadFunction("Menu/Target Cursor Left", 100, InputAction.MenuLeft, InputAction.TargetCursorLeft),
            new GamepadFunction("Menu/Target Cursor Right", 99, InputAction.MenuRight, InputAction.TargetCursorRight),
            // Cycle Target Left / Right (Tab / Shift+Tab) and plain Lock On (T, keypad *) stay keyboard-only: the
            // combined rows cover them on the pad (round 3, B24); InputProfile.EnsureNoHiddenGamepadBindings takes a pad
            // button off any action without a row so none is left bound where the list cannot show it.
            new GamepadFunction("Lock On / Heal (no target)", -1, InputAction.LockOnOrHeal),
            new GamepadFunction("Autorun", 108, InputAction.ToggleAutorun),
            new GamepadFunction("Toggle Walk/Run", 138, InputAction.ToggleWalkRun),
            new GamepadFunction("Change View (3rd/1st/Free)", -1, InputAction.ToggleCameraMode),
            new GamepadFunction("Reset Camera", -1, InputAction.ResetCamera),
            new GamepadFunction("Zoom In", 137, InputAction.CameraZoomIn),
            new GamepadFunction("Zoom Out", 136, InputAction.CameraZoomOut),
            new GamepadFunction("Macro Palette (Ctrl)", -1, InputAction.MacroPaletteCtrl),
            new GamepadFunction("Macro Palette (Alt)", -1, InputAction.MacroPaletteAlt),
            new GamepadFunction("Log Out", -1, InputAction.Logout),
            new GamepadFunction("Next Character", -1, InputAction.CycleCharacter),
        };

        /// <summary>Whether some function of the list drives <paramref name="action"/>.</summary>
        public static bool IsListed(InputAction action)
        {
            foreach (var function in Functions)
            {
                if (Array.IndexOf(function.Actions, action) >= 0) return true;
            }
            return false;
        }

        /// <summary>The buttons a function has in <paramref name="profile"/> (those of its first action), in bit order.</summary>
        public static IReadOnlyList<GamepadButton> ButtonsOf(InputProfile profile, GamepadFunction function)
        {
            ArgumentNullException.ThrowIfNull(profile);
            ArgumentNullException.ThrowIfNull(function);
            var buttons = new List<GamepadButton>();
            foreach (var chord in profile.GetChords(function.Actions[0]))
            {
                if (IsPlainGamepad(chord) && !buttons.Contains(chord.GamepadButton)) buttons.Add(chord.GamepadButton);
            }
            buttons.Sort();
            return buttons;
        }

        /// <summary>The function's buttons as the list shows them ("X, Start"), or "-" when it has none.</summary>
        public static string Describe(InputProfile profile, GamepadFunction function)
        {
            var buttons = ButtonsOf(profile, function);
            return buttons.Count == 0 ? "-" : string.Join(", ", buttons.Select(ButtonName));
        }

        /// <summary>
        /// Assigns <paramref name="button"/> to <paramref name="function"/>: the function's actions lose their other
        /// gamepad buttons and get this one. Any other function of the same context that had the button gets the
        /// function's previous button instead (a swap; nothing when it had none), and actions outside the list lose it,
        /// so the button drives one function per context.
        /// </summary>
        public static void Assign(InputProfile profile, GamepadFunction function, GamepadButton button)
        {
            ArgumentNullException.ThrowIfNull(profile);
            ArgumentNullException.ThrowIfNull(function);
            if (button == GamepadButton.None || ((uint)button & ((uint)button - 1)) != 0)
            {
                throw new ArgumentException("Assign one gamepad button.", nameof(button));
            }

            var previous = ButtonsOf(profile, function);
            GamepadButton swapIn = previous.Count > 0 && previous[0] != button ? previous[0] : GamepadButton.None;
            var chord = new InputChord(button);

            foreach (var other in Functions)
            {
                if (ReferenceEquals(other, function) || !other.SharesContextWith(function)) continue;
                if (!other.Actions.Any(a => profile.GetChords(a).Contains(chord))) continue;
                foreach (var action in other.Actions)
                {
                    profile.Unbind(action, chord);
                    if (swapIn != GamepadButton.None) profile.Bind(action, new InputChord(swapIn));
                }
            }

            // Actions no function lists (macros, the other party targets) let the button go too.
            var listed = Functions.SelectMany(f => f.Actions).ToHashSet();
            foreach (var action in profile.Bindings.Keys.ToList())
            {
                if (listed.Contains(action) || function.Actions.Contains(action)) continue;
                bool menu = InputProfile.IsMenuNavigationAction(action);
                if ((menu && function.HasMenuContext) || (!menu && function.HasGameContext)) profile.Unbind(action, chord);
            }

            foreach (var action in function.Actions)
            {
                if (profile.Bindings.TryGetValue(action, out var chords)) chords.RemoveAll(IsPlainGamepad);
                profile.Bind(action, chord);
            }
        }

        /// <summary>Puts every gamepad button back on its default function; keyboard bindings and gamepad settings stay.</summary>
        public static void ResetDefaults(InputProfile profile)
        {
            ArgumentNullException.ThrowIfNull(profile);
            foreach (var chords in profile.Bindings.Values) chords.RemoveAll(c => c.IsGamepadChord);
            InputProfile.BindDefaultGamepadButtons(profile); // the d-pad's menu functions included
        }

        /// <summary>The lowest gamepad button set in <paramref name="buttons"/>, or None.</summary>
        public static GamepadButton FirstButton(GamepadButton buttons)
        {
            uint bits = (uint)buttons;
            return (GamepadButton)(bits & (~bits + 1));
        }

        /// <summary>A button's short name (Xbox layout names, as the Controls &amp; Input page shows them).</summary>
        public static string ButtonName(GamepadButton button) => button switch
        {
            GamepadButton.DPadUp => "D-Pad Up",
            GamepadButton.DPadDown => "D-Pad Down",
            GamepadButton.DPadLeft => "D-Pad Left",
            GamepadButton.DPadRight => "D-Pad Right",
            GamepadButton.LeftThumb => "L3",
            GamepadButton.RightThumb => "R3",
            GamepadButton.LeftShoulder => "LB",
            GamepadButton.RightShoulder => "RB",
            GamepadButton.LeftTrigger => "LT",
            GamepadButton.RightTrigger => "RT",
            _ => button.ToString(),
        };

        private static bool IsPlainGamepad(InputChord chord) => chord.IsGamepadChord && chord.Modifiers == InputModifiers.None;
    }
}
