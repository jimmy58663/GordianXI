// src/Gordian.Engine/Ui/StockUiMenuController.Keyboard.cs
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Gordian.Core.Diagnostics;
using Gordian.Core.Resources.Ui;
using Gordian.Engine.Input;

namespace Gordian.Engine.Ui
{
    /// <summary>
    /// The keyboard's key-assignment windows (#337): Config -> Misc. 2 -> Key Assignment's Movement opens <c>k1assign</c>,
    /// Commands <c>k2assign</c> (their titles baked into the art), Default asks and puts every key back. Each row is a
    /// <see cref="KeyboardAssignments"/> function with its keys at the right; Confirm on a row waits for the next key (with
    /// its Ctrl / Alt / Shift) and assigns it, swapping with the function that had it; Escape or five seconds give up. The
    /// keyboard drives nothing while a row waits and until the assigned key is let go. Changes go to the user's shared input
    /// profile, so every multi-boxed character gets them (#314), and are saved to <c>input_profile.json</c>.
    /// </summary>
    public sealed partial class StockUiMenuController
    {
        /// <summary>The Movement window (movement, camera and cursor keys) and the Commands window (shortcut commands).</summary>
        public const string KeyAssignMovementMenu = "k1assign", KeyAssignCommandsMenu = "k2assign";

        /// <summary>Misc. 2's Key Assignment buttons: Movement, Commands, Default (help 151, 152, 153).</summary>
        public const int KeyAssignMovementButton = 8, KeyAssignCommandsButton = 9, KeyAssignDefaultButton = 10;

        /// <summary>The windows' rows (fourteen invisible 256 x 16 buttons from 34,21).</summary>
        public const int KeyAssignRows = 14;

        /// <summary>Shown in a row while the window waits for its key.</summary>
        public const string KeyCapturePrompt = "Press a key";

        /// <summary>
        /// The question Default asks. Retail's text is config row 143, "Reset keyboard assignment to default?", longer than
        /// the gamepad prompt that ran past the <c>comyn</c> window's right edge (#21 round 2); the window retail asks it in
        /// is not captured, so GordianXI asks the shorter form (PROVISIONAL).
        /// </summary>
        public const string KeyResetPrompt = "Reset keyboard to default?";

        private StockUiOpenMenu? _keyCaptureList;
        private int _keyCaptureEntry = -1;
        private TimeSpan _keyCaptureLeft;
        private bool _keyCaptureRelease;
        private int _pendingKeyReset;

        /// <summary>
        /// True while a keyboard window waits for a key, and after one was assigned until every key is let go: the keyboard
        /// then drives no action (<see cref="InputState.SuppressKeyboardActions"/>).
        /// </summary>
        public bool IsCapturingKeyboard => _keyCaptureList != null || _keyCaptureRelease;

        /// <summary>The functions a keyboard window lists.</summary>
        public static IReadOnlyList<KeyboardFunction> KeyFunctionsOf(StockUiOpenMenu menu) =>
            menu.Name.Equals(KeyAssignCommandsMenu, StringComparison.OrdinalIgnoreCase) ? KeyboardAssignments.Commands : KeyboardAssignments.Movement;

        /// <summary>Misc. 2's Movement, Commands and Default buttons; false for any other button.</summary>
        private bool TryActivateKeyAssignment(StockUiOpenMenu top, UiMenuButton button)
        {
            if (top.ConfigPage?.Menu != StockUiConfigPages.Misc2Page) return false;
            switch (button.ButtonId)
            {
                case KeyAssignMovementButton or KeyAssignCommandsButton:
                    bool opened;
                    lock (_sync) opened = OpenKeyAssignList(button.ButtonId == KeyAssignMovementButton ? KeyAssignMovementMenu : KeyAssignCommandsMenu, top) != null;
                    if (opened) Changed?.Invoke();
                    return true;
                case KeyAssignDefaultButton:
                    _ = ConfirmKeyResetAsync();
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>Opens a keyboard window over Misc. 2 (under the lock); null when its frame is missing.</summary>
        private StockUiOpenMenu? OpenKeyAssignList(string menuName, StockUiOpenMenu parent)
        {
            var library = _library;
            if (library == null || !library.TryGetMenu(menuName, out var definition))
            {
                GordianLog.Warn(LogCategories.Ui, $"Stock menu '{menuName}' is not available.");
                return null;
            }
            var menu = new StockUiOpenMenu(definition, parent, Array.Empty<string>(), null, listKind: StockUiListKind.KeyAssign);
            Refresh(menu);
            menu.SelectedButtonId = 1;
            var open = new StockUiOpenMenu[_open.Length + 1];
            Array.Copy(_open, open, _open.Length);
            open[^1] = menu;
            _open = open;
            return menu;
        }

        /// <summary>The rows: each function's label (retail wording from the config row table) and its keys.</summary>
        private void RefreshKeyAssign(StockUiOpenMenu menu)
        {
            var profile = GamepadProfile?.Invoke();
            var functions = KeyFunctionsOf(menu);
            var rows = new List<StockUiListRow>(functions.Count);
            for (int i = 0; i < functions.Count; i++)
            {
                var function = functions[i];
                string label = function.TextIndex >= 0 ? StockUiFontColors.Text(ConfigRowText, function.TextIndex, function.Label) : function.Label;
                string value = ReferenceEquals(_keyCaptureList, menu) && _keyCaptureEntry == i
                    ? KeyCapturePrompt
                    : profile != null ? KeyboardAssignments.Describe(profile, function) : "-";
                rows.Add(new StockUiListRow(rows.Count + 1, label, false, Value: value));
            }
            menu.Rows = rows;
        }

        /// <summary>Confirm on a row: wait for its key.</summary>
        private void ActivateKeyAssign(StockUiOpenMenu list, int buttonId)
        {
            int index = list.EntryIndex(buttonId);
            if (index < 0 || index >= KeyFunctionsOf(list).Count) return;
            if (GamepadProfile?.Invoke() == null || GamepadProfileEdited == null)
            {
                NoticePosted?.Invoke("Keyboard: key assignment is not available in this session.");
                return;
            }
            lock (_sync)
            {
                _keyCaptureList = list;
                _keyCaptureEntry = index;
                _keyCaptureLeft = GamepadCaptureTimeout;
                Refresh(list);
            }
            Changed?.Invoke();
        }

        /// <summary>
        /// Asks, then leaves the reset for the next input tick: the answer arrives on a pool thread, and the shared profile
        /// is only edited from the input loop's thread.
        /// </summary>
        private async Task ConfirmKeyResetAsync()
        {
            bool yes = await PromptYesNoAsync(KeyResetPrompt, defaultYes: false).ConfigureAwait(false);
            if (yes) Volatile.Write(ref _pendingKeyReset, 1);
        }

        /// <summary>
        /// Runs before the menus take the tick's input. While a window waits, the first assignable key pressed (with the
        /// modifiers held) is assigned; Escape, closing the window or the timeout give up. Returns true while it owns the
        /// input (until the assigned key is let go).
        /// </summary>
        private bool ProcessKeyboardCapture(InputState input, TimeSpan elapsed)
        {
            if (Interlocked.Exchange(ref _pendingKeyReset, 0) == 1 && GamepadProfileEdited is { } reset)
            {
                reset(KeyboardAssignments.ResetDefaults);
                GordianLog.Info(LogCategories.Input, "Keyboard: every key back on its default function.");
                Changed?.Invoke();
            }

            if (_keyCaptureRelease && _keyCaptureList == null && !input.AnyKeyHeld) _keyCaptureRelease = false;

            var list = _keyCaptureList;
            if (list == null) return _keyCaptureRelease;
            if (!ReferenceEquals(Top, list))
            {
                EndKeyCapture(list, release: false);
                return false;
            }

            foreach (var key in input.KeysPressed)
            {
                if (key == GordianKey.Escape)
                {
                    SoundCue?.Invoke(StockUiSoundCue.Close);
                    EndKeyCapture(list, release: true);
                    return true;
                }
                if (!KeyboardAssignments.IsAssignable(key)) continue;
                var function = KeyFunctionsOf(list)[_keyCaptureEntry];
                var chord = new InputChord(key, input.CurrentModifiers & ~InputModifiers.Super);
                GamepadProfileEdited?.Invoke(profile => KeyboardAssignments.Assign(profile, function, chord));
                GordianLog.Info(LogCategories.Input, $"Keyboard: {KeyboardAssignments.KeyName(chord)} assigned to {function.Label}.");
                SoundCue?.Invoke(StockUiSoundCue.Select);
                EndKeyCapture(list, release: true);
                return true;
            }

            _keyCaptureLeft -= elapsed;
            if (_keyCaptureLeft <= TimeSpan.Zero) EndKeyCapture(list, release: false);
            return true;
        }

        private void EndKeyCapture(StockUiOpenMenu list, bool release)
        {
            lock (_sync)
            {
                _keyCaptureList = null;
                _keyCaptureEntry = -1;
                _keyCaptureRelease = release;
                Refresh(list);
            }
            Changed?.Invoke();
        }
    }
}
