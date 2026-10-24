// src/Gordian.Core/Ui/StockUiMenuController.Gamepad.cs
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Gordian.Core.Diagnostics;
using Gordian.Engine.Input;

namespace Gordian.Engine.Ui
{
    /// <summary>
    /// The gamepad key-assignment list (#21): Config -> Gamepad. Each row is a <see cref="GamepadAssignments"/>
    /// function with its buttons at the right; Confirm on a row waits for the next gamepad button and assigns it
    /// (swapping with the function that had it), the keyboard's Cancel or five seconds without a button give up, and the
    /// last row puts every button back on its default. Changes go to the user's shared input profile, so every
    /// multi-boxed character gets them (#314).
    /// </summary>
    public sealed partial class StockUiMenuController
    {
        /// <summary>How long the list waits for a button after Confirm.</summary>
        public static readonly TimeSpan GamepadCaptureTimeout = TimeSpan.FromSeconds(5);

        /// <summary>Shown in a row while the list waits for its button.</summary>
        public const string GamepadCapturePrompt = "Press a button";

        /// <summary>The list's last row, after the functions.</summary>
        public const string GamepadResetLabel = "Reset to Default";

        /// <summary>
        /// The question the reset row asks (GordianXI wording). Short enough for the <c>comyn</c> window: the longer
        /// "Reset gamepad assignment to default?" ran past its right edge (round 2, B12).
        /// </summary>
        public const string GamepadResetPrompt = "Reset gamepad to default?";

        /// <summary>The user's input profile the list shows (null outside a session: the list then shows no buttons).</summary>
        public Func<InputProfile?>? GamepadProfile { get; set; }

        /// <summary>Applies an edit to the user's input profile and tells every session (wired to <see cref="InputProfileHub.Edit"/>).</summary>
        public Action<Action<InputProfile>>? GamepadProfileEdited { get; set; }

        private StockUiOpenMenu? _captureList;
        private int _captureEntry = -1;
        private TimeSpan _captureLeft;
        private bool _captureRelease;

        /// <summary>
        /// True while the list waits for a button, and after one was assigned until every gamepad button is let go: the
        /// gamepad then drives no action (<see cref="InputState.SuppressGamepadActions"/>), so the button being assigned
        /// never also acts (L3 would toggle lock-on, LB switch characters).
        /// </summary>
        public bool IsCapturingGamepad => _captureList != null || _captureRelease;

        /// <summary>The list entry waiting for its button, or -1.</summary>
        public int GamepadCaptureEntry => _captureList != null ? _captureEntry : -1;

        /// <summary>Opens the list over the config list (under the lock); null when its frame is missing.</summary>
        private StockUiOpenMenu? OpenGamepadAssignList(StockUiOpenMenu parent)
        {
            var library = _library;
            if (library == null || !library.TryGetMenu(StockUiConfigPages.GamepadAssignMenu, out var definition))
            {
                GordianLog.Warning("UI", $"Stock menu '{StockUiConfigPages.GamepadAssignMenu}' is not available.");
                return null;
            }
            var menu = new StockUiOpenMenu(definition, parent, Array.Empty<string>(), null, listKind: StockUiListKind.GamepadAssign);
            Refresh(menu);
            menu.SelectedButtonId = 1;
            var open = new StockUiOpenMenu[_open.Length + 1];
            Array.Copy(_open, open, _open.Length);
            open[^1] = menu;
            _open = open;
            return menu;
        }

        /// <summary>The rows: each function's label (retail wording where the config row table has it) and its buttons.</summary>
        private void RefreshGamepadAssign(StockUiOpenMenu menu)
        {
            var profile = GamepadProfile?.Invoke();
            var functions = GamepadAssignments.Functions;
            var rows = new List<StockUiListRow>(functions.Count + 1);
            for (int i = 0; i < functions.Count; i++)
            {
                var function = functions[i];
                string label = function.TextIndex >= 0 ? StockUiFontColors.Text(ConfigRowText, function.TextIndex, function.Label) : function.Label;
                string value = ReferenceEquals(_captureList, menu) && _captureEntry == i
                    ? GamepadCapturePrompt
                    : profile != null ? GamepadAssignments.Describe(profile, function) : "-";
                rows.Add(new StockUiListRow(rows.Count + 1, label, false, Value: value));
            }
            rows.Add(new StockUiListRow(rows.Count + 1, GamepadResetLabel, false));
            menu.Rows = rows;
        }

        /// <summary>Confirm on a row: wait for its button, or (the last row) ask and reset every button.</summary>
        private void ActivateGamepadAssign(StockUiOpenMenu list, int buttonId)
        {
            int index = list.EntryIndex(buttonId);
            if (index < 0 || index >= list.Rows.Count) return;
            if (index >= GamepadAssignments.Functions.Count)
            {
                _ = ConfirmGamepadResetAsync(list);
                return;
            }
            if (GamepadProfile?.Invoke() == null || GamepadProfileEdited == null)
            {
                NoticePosted?.Invoke("Gamepad: key assignment is not available in this session.");
                return;
            }
            lock (_sync)
            {
                _captureList = list;
                _captureEntry = index;
                _captureLeft = GamepadCaptureTimeout;
                Refresh(list);
            }
            Changed?.Invoke();
        }

        private StockUiOpenMenu? _pendingGamepadReset;

        /// <summary>
        /// Asks, then leaves the reset for the next input tick: the answer arrives on a pool thread, and the shared
        /// profile (and the desktop page that shows it) is only edited from the input loop's thread.
        /// </summary>
        private async Task ConfirmGamepadResetAsync(StockUiOpenMenu list)
        {
            bool yes = await PromptYesNoAsync(GamepadResetPrompt, defaultYes: false).ConfigureAwait(false);
            if (yes) Volatile.Write(ref _pendingGamepadReset, list);
        }

        /// <summary>
        /// Runs before the menus take the tick's input. While the list waits, the first gamepad button pressed is
        /// assigned; the keyboard's Cancel, closing the list or the timeout give up. Returns true while it owns the input.
        /// </summary>
        private bool ProcessGamepadCapture(InputState input, TimeSpan elapsed)
        {
            if (Interlocked.Exchange(ref _pendingGamepadReset, null) is { } resetList && GamepadProfileEdited is { } reset)
            {
                reset(GamepadAssignments.ResetDefaults);
                GordianLog.Info("INPUT", "Gamepad: every button back on its default function.");
                lock (_sync) Refresh(resetList);
                Changed?.Invoke();
            }

            if (_captureRelease && _captureList == null)
            {
                // After an assignment the pad stays quiet until its buttons are let go (see IsCapturingGamepad).
                if (input.GamepadButtonsHeld == GamepadButton.None) _captureRelease = false;
            }

            var list = _captureList;
            if (list == null) return false;
            if (!ReferenceEquals(Top, list))
            {
                EndGamepadCapture(list, release: false);
                return false;
            }

            var pressed = GamepadAssignments.FirstButton(input.GamepadButtonsPressed);
            if (pressed != GamepadButton.None)
            {
                var function = GamepadAssignments.Functions[_captureEntry];
                GamepadProfileEdited?.Invoke(profile => GamepadAssignments.Assign(profile, function, pressed));
                GordianLog.Info("INPUT", $"Gamepad: {GamepadAssignments.ButtonName(pressed)} assigned to {function.Label}.");
                SoundCue?.Invoke(StockUiSoundCue.Select);
                EndGamepadCapture(list, release: true);
                return true;
            }

            bool keyboardCancel = input.WasActionTriggered(InputAction.Cancel) && !input.WasActionTriggeredByGamepad(InputAction.Cancel);
            _captureLeft -= elapsed;
            if (keyboardCancel || _captureLeft <= TimeSpan.Zero)
            {
                if (keyboardCancel) SoundCue?.Invoke(StockUiSoundCue.Close);
                EndGamepadCapture(list, release: false);
            }
            return true;
        }

        private void EndGamepadCapture(StockUiOpenMenu list, bool release)
        {
            lock (_sync)
            {
                _captureList = null;
                _captureEntry = -1;
                _captureRelease = release;
                Refresh(list);
            }
            Changed?.Invoke();
        }
    }
}
