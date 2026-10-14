// src/Gordian.Core/Macros/MacroController.cs
using System;
using Gordian.Core.Input;

namespace Gordian.Core.Macros
{
    /// <summary>
    /// The macro palettes in play (#339): which palette shows, the macro picked on it, and the keys that run macros and
    /// change the set. Fed each input tick by the locomotion controller.
    /// <list type="bullet">
    /// <item>Keyboard (BG-Wiki "Macro" hotkeys): holding Ctrl or Alt shows that palette; Ctrl/Alt + 1..0 runs a macro;
    /// Ctrl/Alt + Up/Down cycles the set; Ctrl/Alt + Shift + 1..0 changes to set 1..10; Ctrl/Alt + Left/Right picks a macro on
    /// the palette and Ctrl/Alt + Enter runs it.</item>
    /// <item>Gamepad (the maintainer's retail check, 2026-10-13): holding LT / RT (<see cref="InputAction.MacroPaletteCtrl"/> /
    /// <see cref="InputAction.MacroPaletteAlt"/>) shows the palette; the d-pad moves as the arrow keys do (left / right pick a
    /// macro, up / down change the set) and the Confirm button runs the picked macro. No other button touches a macro.</item>
    /// </list>
    /// </summary>
    public sealed class MacroController
    {
        private MacroLibrary _library;

        public MacroController(MacroLibrary? library = null)
        {
            _library = library ?? new MacroLibrary();
            Runner = new MacroRunner();
        }

        /// <summary>The character's macros.</summary>
        public MacroLibrary Library
        {
            get => _library;
            set => _library = value ?? throw new ArgumentNullException(nameof(value));
        }

        public MacroRunner Runner { get; }

        /// <summary>The palette shown now (None while no palette key is held).</summary>
        public MacroPalette ShownPalette { get; private set; }

        /// <summary>The macro picked on the shown palette with Left/Right (0-9), shown highlighted; Enter runs it.</summary>
        public int SelectedKey { get; private set; }

        /// <summary>True while a gamepad trigger holds the palette.</summary>
        public bool HeldByGamepad { get; private set; }

        /// <summary>Set while the macro editor is open: the keys then edit rather than run (the editor draws the palettes).</summary>
        public bool Suspended { get; set; }

        /// <summary>Raised when a macro runs, with its slot (for a palette flash or a log line).</summary>
        public event Action<MacroSlot>? MacroStarted;


        /// <summary>
        /// Reads this tick's input. <paramref name="blocked"/> (a menu, the chat line, the editor) hides the palette and runs
        /// nothing. Returns true when the palette took the input (the caller then skips the keys it would otherwise act on).
        /// </summary>
        public bool Update(InputState input, bool blocked, InputProfile? profile = null)
        {
            ArgumentNullException.ThrowIfNull(input);
            var mods = input.CurrentModifiers;
            bool ctrl = (mods & InputModifiers.Control) != 0, alt = (mods & InputModifiers.Alt) != 0;
            bool padCtrl = input.IsActionHeld(InputAction.MacroPaletteCtrl), padAlt = input.IsActionHeld(InputAction.MacroPaletteAlt);

            MacroPalette palette = padCtrl && !padAlt ? MacroPalette.Ctrl
                : padAlt && !padCtrl ? MacroPalette.Alt
                : ctrl && !alt ? MacroPalette.Ctrl
                : alt && !ctrl ? MacroPalette.Alt
                : MacroPalette.None;
            bool byPad = palette != MacroPalette.None && (padCtrl || padAlt);

            if (blocked || Suspended)
            {
                ShownPalette = MacroPalette.None;
                HeldByGamepad = false;
                return false;
            }
            if (palette != ShownPalette) SelectedKey = 0;
            ShownPalette = palette;
            HeldByGamepad = byPad;

            // The bound macro keys (Ctrl+1 .. Alt+0 by default) run whatever is held.
            for (int key = 0; key < MacroLibrary.KeysPerPalette; key++)
            {
                if (input.WasActionTriggered((InputAction)((int)InputAction.MacroCtrl1 + key))) RunKey(MacroPalette.Ctrl, key);
                if (input.WasActionTriggered((InputAction)((int)InputAction.MacroAlt1 + key))) RunKey(MacroPalette.Alt, key);
            }
            if (palette == MacroPalette.None) return false;

            if (byPad)
            {
                var pressed = input.GamepadButtonsPressed;
                if ((pressed & GamepadButton.DPadUp) != 0) Library.CycleSet(-1);
                if ((pressed & GamepadButton.DPadDown) != 0) Library.CycleSet(1);
                if ((pressed & GamepadButton.DPadLeft) != 0) SelectedKey = (SelectedKey + MacroLibrary.KeysPerPalette - 1) % MacroLibrary.KeysPerPalette;
                if ((pressed & GamepadButton.DPadRight) != 0) SelectedKey = (SelectedKey + 1) % MacroLibrary.KeysPerPalette;
                if ((pressed & ConfirmButtons(profile)) != 0) RunKey(palette, SelectedKey);
                return true;
            }

            bool shift = (mods & InputModifiers.Shift) != 0;
            for (int key = 0; key < MacroLibrary.KeysPerPalette; key++)
            {
                if (!input.WasKeyPressed(DigitKey(key))) continue;
                // Ctrl/Alt + Shift + digit: change to that set (the digit 0 is set 10).
                if (shift) Library.SelectSet(Library.CurrentBook, key);
            }
            if (input.WasKeyPressed(GordianKey.Up)) Library.CycleSet(-1);
            if (input.WasKeyPressed(GordianKey.Down)) Library.CycleSet(1);
            if (input.WasKeyPressed(GordianKey.Left)) SelectedKey = (SelectedKey + MacroLibrary.KeysPerPalette - 1) % MacroLibrary.KeysPerPalette;
            if (input.WasKeyPressed(GordianKey.Right)) SelectedKey = (SelectedKey + 1) % MacroLibrary.KeysPerPalette;
            if (input.WasKeyPressed(GordianKey.Enter) || input.WasKeyPressed(GordianKey.NumPadEnter)) RunKey(palette, SelectedKey);
            return true;
        }

        /// <summary>The pad buttons bound to Confirm (A when the profile has none).</summary>
        private static GamepadButton ConfirmButtons(InputProfile? profile)
        {
            var buttons = GamepadButton.None;
            if (profile != null)
            {
                foreach (var chord in profile.GetChords(InputAction.Confirm)) buttons |= chord.GamepadButton;
            }
            return buttons == GamepadButton.None ? GamepadButton.A : buttons;
        }

        /// <summary>The number row's key for a macro key: 1..9 then 0.</summary>
        public static GordianKey DigitKey(int key) => key == 9 ? GordianKey.D0 : (GordianKey)((int)GordianKey.D1 + key);

        /// <summary>Runs a key of the current set.</summary>
        public void RunKey(MacroPalette palette, int key)
        {
            if (palette == MacroPalette.None) return;
            var slot = Library.CurrentSlot(palette, key);
            var macro = Library.Get(slot);
            if (macro.IsEmpty) return;
            MacroStarted?.Invoke(slot);
            _ = Runner.Run(macro);
        }
    }
}
