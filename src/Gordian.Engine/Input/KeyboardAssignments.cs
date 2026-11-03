// src/Gordian.Engine/Input/KeyboardAssignments.cs
using System;
using System.Collections.Generic;
using System.Linq;

namespace Gordian.Engine.Input
{
    /// <summary>
    /// One assignable keyboard function of the key-assignment windows (#337): its label and the actions one key drives. As
    /// with <see cref="GamepadFunction"/>, a function can drive actions of both contexts ("Menu Cursor &amp; Camera Up" moves
    /// the menu cursor while a menu is open and the camera otherwise).
    /// </summary>
    /// <param name="Label">English label, used when the config row table is not read.</param>
    /// <param name="TextIndex">The config row table (ROM/165/74) row with retail's wording, or -1 for GordianXI functions.</param>
    /// <param name="Actions">The actions the assigned key drives.</param>
    public sealed record KeyboardFunction(string Label, int TextIndex, params InputAction[] Actions)
    {
        /// <summary>Whether the function is used while a stock menu is open (menu navigation).</summary>
        public bool HasMenuContext => Actions.Any(InputProfile.IsMenuNavigationAction);

        /// <summary>Whether the function is used outside menus.</summary>
        public bool HasGameContext => Actions.Any(a => !InputProfile.IsMenuNavigationAction(a));

        /// <summary>Two functions compete for a key when they are used in the same context.</summary>
        public bool SharesContextWith(KeyboardFunction other) =>
            (HasMenuContext && other.HasMenuContext) || (HasGameContext && other.HasGameContext);
    }

    /// <summary>
    /// The keyboard's key assignment (#337), behind retail's two windows opened from Config &gt; Misc. 2's Key Assignment row:
    /// <c>k1assign</c>, "Movement" (help 151 / 277: movement, cursor and camera keys) and <c>k2assign</c>, "Commands" (help
    /// 152 / 278: shortcut commands); the row's Default (help 153) puts the keys back. Both windows are titled "Keyboard"
    /// (title 69, ROM/165/76).
    /// <para>
    /// The function names are retail's, from the config row table (ROM/165/74 rows 89-142: 89-104 movement, camera and the
    /// cursors, 105-111 Confirm ... Open Main Menu, 112-133 the shortcut commands; read 2026-11-02). Only the functions
    /// GordianXI has an action for are listed; retail's other shortcut rows (Attack, Items, Magic, Abilities, Weapon Skills,
    /// Ranged Attack, Call For Help, Equipment, Heal, Check, the Chat: rows, Pet Commands, Take Screenshot, Hide/Show
    /// Window) have none yet. GordianXI's own keyboard functions (strafing, lock-on, target cycling, the chat line) are
    /// listed after the retail rows of the Commands window, with GordianXI wording. Which rows retail lists in each window,
    /// and in what order, is PROVISIONAL (no capture of either window yet).
    /// </para>
    /// <para>
    /// Assigning works as the gamepad list's does (<see cref="GamepadAssignments.Assign"/>): a function gets one key, and
    /// another function of the same context that had the key gets the function's previous key (a swap). What retail does
    /// with a key already in use is not known. Gamepad and mouse bindings are never touched.
    /// </para>
    /// </summary>
    public static class KeyboardAssignments
    {
        /// <summary>The Movement window's functions (<c>k1assign</c>), in list order.</summary>
        public static IReadOnlyList<KeyboardFunction> Movement { get; } = new[]
        {
            new KeyboardFunction("Move Up", 89, InputAction.MoveForward),
            new KeyboardFunction("Move Down", 90, InputAction.MoveBackward),
            new KeyboardFunction("Move Right", 91, InputAction.TurnRight),
            new KeyboardFunction("Move Left", 92, InputAction.TurnLeft),
            new KeyboardFunction("Move Camera Up", 93, InputAction.CameraPitchUp),
            new KeyboardFunction("Move Camera Down", 94, InputAction.CameraPitchDown),
            new KeyboardFunction("Move Camera Right", 95, InputAction.CameraYawRight),
            new KeyboardFunction("Move Camera Left", 96, InputAction.CameraYawLeft),
            new KeyboardFunction("Menu/Target Cursor Right", 99, InputAction.MenuRight, InputAction.TargetCursorRight),
            new KeyboardFunction("Menu/Target Cursor Left", 100, InputAction.MenuLeft, InputAction.TargetCursorLeft),
            new KeyboardFunction("Menu Cursor & Camera Up", 101, InputAction.MenuUp, InputAction.CameraPitchUp),
            new KeyboardFunction("Menu Cursor & Camera Down", 102, InputAction.MenuDown, InputAction.CameraPitchDown),
            new KeyboardFunction("Confirm", 105, InputAction.Confirm),
            new KeyboardFunction("Cancel", 106, InputAction.Cancel),
            new KeyboardFunction("Change View", 107, InputAction.ToggleCameraMode),
            new KeyboardFunction("Autorun", 108, InputAction.ToggleAutorun),
            new KeyboardFunction("Select Window", 110, InputAction.CycleLogWindow),
            new KeyboardFunction("Open Main Menu", 111, InputAction.OpenMenu),
        };

        /// <summary>The Commands window's functions (<c>k2assign</c>), in list order.</summary>
        public static IReadOnlyList<KeyboardFunction> Commands { get; } = new[]
        {
            new KeyboardFunction("Zoom Out", 131, InputAction.CameraZoomOut),
            new KeyboardFunction("Zoom In", 132, InputAction.CameraZoomIn),
            new KeyboardFunction("Toggle Walk/Run", 138, InputAction.ToggleWalkRun),
            // GordianXI's own keyboard functions.
            new KeyboardFunction("Strafe Left", -1, InputAction.StrafeLeft),
            new KeyboardFunction("Strafe Right", -1, InputAction.StrafeRight),
            new KeyboardFunction("Reset Camera", -1, InputAction.ResetCamera),
            new KeyboardFunction("Lock On", -1, InputAction.ToggleLockOn),
            new KeyboardFunction("Cycle Target Right", -1, InputAction.TargetNearest),
            new KeyboardFunction("Cycle Target Left", -1, InputAction.TargetPrevious),
            new KeyboardFunction("Target Self", -1, InputAction.TargetSelf),
            new KeyboardFunction("Open Chat", -1, InputAction.OpenChat),
        };

        /// <summary>Both windows' functions: a key is taken from any of them when assigned.</summary>
        public static IEnumerable<KeyboardFunction> All => Movement.Concat(Commands);

        /// <summary>
        /// The keys a function has in <paramref name="profile"/>: the keyboard chords every one of its actions has (a
        /// two-context function shows only the keys that drive both), in the order the first action lists them.
        /// </summary>
        public static IReadOnlyList<InputChord> KeysOf(InputProfile profile, KeyboardFunction function)
        {
            ArgumentNullException.ThrowIfNull(profile);
            ArgumentNullException.ThrowIfNull(function);
            var keys = new List<InputChord>();
            foreach (var chord in profile.GetChords(function.Actions[0]))
            {
                if (!IsKeyboard(chord) || keys.Contains(chord)) continue;
                if (function.Actions.Skip(1).All(a => profile.GetChords(a).Contains(chord))) keys.Add(chord);
            }
            return keys;
        }

        /// <summary>The function's keys as the list shows them ("W, Up"), or "-" when it has none.</summary>
        public static string Describe(InputProfile profile, KeyboardFunction function)
        {
            var keys = KeysOf(profile, function);
            return keys.Count == 0 ? "-" : string.Join(", ", keys.Select(KeyName));
        }

        /// <summary>
        /// Assigns <paramref name="key"/> to <paramref name="function"/>: the function's actions lose their other keyboard
        /// keys and get this one. Any other function of the same context that had the key gets the function's previous key
        /// instead (a swap; nothing when it had none), and actions outside both windows let it go, so the key drives one
        /// function per context.
        /// </summary>
        public static void Assign(InputProfile profile, KeyboardFunction function, InputChord key)
        {
            ArgumentNullException.ThrowIfNull(profile);
            ArgumentNullException.ThrowIfNull(function);
            if (!IsKeyboard(key)) throw new ArgumentException("Assign one keyboard key.", nameof(key));

            var previous = KeysOf(profile, function);
            InputChord? swapIn = previous.Count > 0 && previous[0] != key ? previous[0] : null;

            foreach (var other in All)
            {
                if (ReferenceEquals(other, function) || !other.SharesContextWith(function)) continue;
                if (!other.Actions.Any(a => profile.GetChords(a).Contains(key))) continue;
                foreach (var action in other.Actions)
                {
                    if (!profile.GetChords(action).Contains(key)) continue;
                    profile.Unbind(action, key);
                    if (swapIn is { } swap && !profile.GetChords(action).Contains(swap)) profile.Bind(action, swap);
                }
            }

            // Actions neither window lists (macros, the party targets) let the key go too.
            var listed = All.SelectMany(f => f.Actions).ToHashSet();
            foreach (var action in profile.Bindings.Keys.ToList())
            {
                if (listed.Contains(action)) continue;
                bool menu = InputProfile.IsMenuNavigationAction(action);
                if ((menu && function.HasMenuContext) || (!menu && function.HasGameContext)) profile.Unbind(action, key);
            }

            foreach (var action in function.Actions)
            {
                if (profile.Bindings.TryGetValue(action, out var chords)) chords.RemoveAll(c => previous.Contains(c));
                profile.Bind(action, key);
            }
        }

        /// <summary>
        /// Puts every keyboard key back on its default (the Compact or Full layout the profile is named after, Compact
        /// otherwise); gamepad and mouse bindings and the other settings stay.
        /// </summary>
        public static void ResetDefaults(InputProfile profile)
        {
            ArgumentNullException.ThrowIfNull(profile);
            var defaults = profile.Name.StartsWith("Full", StringComparison.OrdinalIgnoreCase)
                ? InputProfile.CreateFullNumpad() : InputProfile.CreateCompact();
            foreach (var chords in profile.Bindings.Values) chords.RemoveAll(IsKeyboard);
            foreach (var (action, chords) in defaults.Bindings)
            {
                foreach (var chord in chords)
                {
                    if (IsKeyboard(chord)) profile.Bind(action, chord);
                }
            }
        }

        /// <summary>Whether a key can be assigned: not a modifier on its own, and not Escape (which gives up the wait).</summary>
        public static bool IsAssignable(GordianKey key) => key is not (GordianKey.None or GordianKey.Escape
            or GordianKey.LeftShift or GordianKey.RightShift or GordianKey.LeftCtrl or GordianKey.RightCtrl
            or GordianKey.LeftAlt or GordianKey.RightAlt or GordianKey.LeftSuper or GordianKey.RightSuper);

        /// <summary>A key's short name for the list ("Ctrl+1", "Num 8", "Page Up").</summary>
        public static string KeyName(InputChord chord)
        {
            string prefix = (chord.Modifiers.HasFlag(InputModifiers.Control) ? "Ctrl+" : string.Empty)
                + (chord.Modifiers.HasFlag(InputModifiers.Alt) ? "Alt+" : string.Empty)
                + (chord.Modifiers.HasFlag(InputModifiers.Shift) ? "Shift+" : string.Empty);
            var key = chord.Key;
            string name = key switch
            {
                >= GordianKey.D0 and <= GordianKey.D9 => ((int)(key - GordianKey.D0)).ToString(System.Globalization.CultureInfo.InvariantCulture),
                >= GordianKey.NumPad0 and <= GordianKey.NumPad9 => "Num " + (int)(key - GordianKey.NumPad0),
                GordianKey.NumPadAdd => "Num +",
                GordianKey.NumPadSubtract => "Num -",
                GordianKey.NumPadMultiply => "Num *",
                GordianKey.NumPadDivide => "Num /",
                GordianKey.NumPadDecimal => "Num .",
                GordianKey.NumPadEnter => "Num Enter",
                GordianKey.PageUp => "Page Up",
                GordianKey.PageDown => "Page Down",
                GordianKey.OemMinus => "-",
                GordianKey.OemSlash => "/",
                _ => key.ToString(),
            };
            return prefix + name;
        }

        private static bool IsKeyboard(InputChord chord) => !chord.IsEmpty && !chord.IsGamepadChord && !chord.IsMouseChord && chord.Key != GordianKey.None;
    }
}
