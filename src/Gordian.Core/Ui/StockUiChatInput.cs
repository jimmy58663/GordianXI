// src/Gordian.Core/Ui/StockUiChatInput.cs
using System;
using System.Collections.Generic;
using System.Text;
using Gordian.Core.Input;

namespace Gordian.Core.Ui
{
    /// <summary>
    /// The chat modes of the stock chat-mode list (the <c>fep</c> menu): what a line typed without a slash command
    /// is sent as. Values are the <c>fep</c> element group's label images (unselected; the orange selected variants
    /// are listed in <see cref="StockUiChatInput.SelectedLabelImage"/>).
    /// </summary>
    public enum ChatInputMode : byte
    {
        Say = 10,
        Shout = 11,
        Tell = 12,
        Party = 13,
        Linkshell = 14,
        Linkshell2 = 16,
        Unity = 17,
        AssistJ = 29,
        AssistE = 30,
    }

    /// <summary>
    /// The stock chat input line (<c>inline</c>): opened from gameplay, it takes the keyboard until the line is sent
    /// (Enter) or dropped (Escape), so typing never moves the character. Holds the text and caret, the sent-line
    /// history (Up/Down), the chat mode and the chat-mode list (Tab on an empty line, the <c>fep</c> menu).
    /// Accessed from the UI thread (keys, text) and the render thread (drawing), so state changes take a lock.
    /// </summary>
    public sealed class StockUiChatInput
    {
        /// <summary>
        /// Longest line accepted, in characters: C2S 0x0B5 carries at most 128 bytes of text, and retail's input
        /// line stops at about this length.
        /// </summary>
        public const int MaxLength = 120;

        /// <summary>Sent lines kept for Up/Down recall.</summary>
        public const int HistoryCapacity = 100;

        /// <summary>The chat-mode list's entries, top to bottom.</summary>
        public static readonly IReadOnlyList<ChatInputMode> Modes = new[]
        {
            ChatInputMode.Say, ChatInputMode.Shout, ChatInputMode.Tell, ChatInputMode.Party, ChatInputMode.Linkshell,
            ChatInputMode.Linkshell2, ChatInputMode.Unity, ChatInputMode.AssistJ, ChatInputMode.AssistE,
        };

        /// <summary>Rows the <c>fep</c> list shows at once (its eight 16-px buttons); longer lists scroll.</summary>
        public const int ModeListRows = 8;

        private readonly object _sync = new();
        private readonly StringBuilder _text = new();
        private readonly List<string> _history = new();
        private int _caret;
        private int _historyIndex;
        private string _swallowText = string.Empty;

        /// <summary>True while the input line is open (it owns the keyboard).</summary>
        public bool IsOpen { get; private set; }

        /// <summary>True while the chat-mode list is open over the input line.</summary>
        public bool IsModeListOpen { get; private set; }

        public ChatInputMode Mode { get; private set; } = ChatInputMode.Say;

        /// <summary>Row of the chat-mode list under the cursor, and the first row shown.</summary>
        public int ModeListIndex { get; private set; }
        public int ModeListFirstRow { get; private set; }

        /// <summary>Last tell partner (who you told or who told you): Tell mode sends to them.</summary>
        public string TellTarget { get; set; } = string.Empty;

        /// <summary>Caret position (characters before it).</summary>
        public int Caret
        {
            get { lock (_sync) return _caret; }
        }

        public string Text
        {
            get { lock (_sync) return _text.ToString(); }
        }

        /// <summary>Raised with the line when it is sent (Enter on a non-empty line); the line closes first.</summary>
        public event Action<string, ChatInputMode>? Submitted;

        /// <summary>Raised when the line opens or closes.</summary>
        public event Action<bool>? OpenChanged;

        /// <summary>Scroll requests while the line is open: window (1 or 2), then pages (+1 back, -1 forward).</summary>
        public event Action<int, int>? ScrollRequested;

        /// <summary>
        /// Opens the line. <paramref name="swallowText"/> is the text the opening key is about to type (Space), which
        /// is dropped when it arrives next rather than starting the line with it.
        /// </summary>
        public void Open(string swallowText = "")
        {
            lock (_sync)
            {
                if (IsOpen) return;
                IsOpen = true;
                IsModeListOpen = false;
                _text.Clear();
                _caret = 0;
                _historyIndex = _history.Count;
                _swallowText = swallowText ?? string.Empty;
            }
            OpenChanged?.Invoke(true);
        }

        public void Close()
        {
            lock (_sync)
            {
                if (!IsOpen) return;
                IsOpen = false;
                IsModeListOpen = false;
                _text.Clear();
                _caret = 0;
                _swallowText = string.Empty;
            }
            OpenChanged?.Invoke(false);
        }

        public void SetMode(ChatInputMode mode)
        {
            lock (_sync) Mode = mode;
        }

        /// <summary>
        /// Types text at the caret (from the platform's text input, so shifted and layout-specific characters come
        /// through). Characters the stock font cannot draw (control characters, anything past ASCII) are dropped.
        /// </summary>
        public void InsertText(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            lock (_sync)
            {
                if (!IsOpen || IsModeListOpen) return;
                if (_swallowText.Length > 0)
                {
                    bool swallow = text == _swallowText;
                    _swallowText = string.Empty;
                    if (swallow) return;
                }
                foreach (char c in text)
                {
                    if (c < 0x20 || c > 0x7E || _text.Length >= MaxLength) continue;
                    _text.Insert(_caret++, c);
                }
            }
        }

        /// <summary>
        /// Handles a key while the line is open (returns false when closed, so the key goes to gameplay).
        /// Every key is consumed while open: the character does not move while you type.
        /// </summary>
        public bool HandleKey(GordianKey key, InputModifiers modifiers)
        {
            string? submitted = null;
            ChatInputMode submittedMode;
            bool close = false;
            int scrollWindow = 0, scrollPages = 0;
            lock (_sync)
            {
                if (!IsOpen) return false;
                _swallowText = string.Empty;
                submittedMode = Mode;

                if (IsModeListOpen)
                {
                    switch (key)
                    {
                        case GordianKey.Up or GordianKey.NumPad8:
                            MoveModeCursor(-1);
                            break;
                        case GordianKey.Down or GordianKey.NumPad2:
                            MoveModeCursor(1);
                            break;
                        case GordianKey.Enter or GordianKey.NumPadEnter:
                            Mode = Modes[ModeListIndex];
                            IsModeListOpen = false;
                            break;
                        case GordianKey.Escape or GordianKey.Tab:
                            IsModeListOpen = false;
                            break;
                    }
                    return true;
                }

                bool ctrl = (modifiers & InputModifiers.Control) != 0;
                switch (key)
                {
                    case GordianKey.Enter or GordianKey.NumPadEnter:
                        string line = _text.ToString().Trim();
                        if (line.Length > 0)
                        {
                            submitted = line;
                            if (_history.Count == 0 || _history[^1] != line)
                            {
                                _history.Add(line);
                                if (_history.Count > HistoryCapacity) _history.RemoveAt(0);
                            }
                        }
                        close = true;
                        break;
                    case GordianKey.Escape:
                        close = true;
                        break;
                    case GordianKey.Backspace:
                        if (_caret > 0) _text.Remove(--_caret, 1);
                        break;
                    case GordianKey.Delete:
                        if (_caret < _text.Length) _text.Remove(_caret, 1);
                        break;
                    case GordianKey.Left:
                        if (_caret > 0) _caret--;
                        break;
                    case GordianKey.Right:
                        if (_caret < _text.Length) _caret++;
                        break;
                    case GordianKey.Home:
                        _caret = 0;
                        break;
                    case GordianKey.End:
                        _caret = _text.Length;
                        break;
                    case GordianKey.Up:
                        RecallHistory(-1);
                        break;
                    case GordianKey.Down:
                        RecallHistory(1);
                        break;
                    case GordianKey.PageUp:
                        scrollWindow = ctrl ? 2 : 1;
                        scrollPages = 1;
                        break;
                    case GordianKey.PageDown:
                        scrollWindow = ctrl ? 2 : 1;
                        scrollPages = -1;
                        break;
                    case GordianKey.Tab:
                        if (_text.Length == 0) OpenModeList();
                        break;
                }
            }

            if (scrollPages != 0) ScrollRequested?.Invoke(scrollWindow, scrollPages);
            if (close)
            {
                Close();
                if (submitted != null) Submitted?.Invoke(submitted, submittedMode);
            }
            return true;
        }

        /// <summary>Opens the chat-mode list with the cursor on the current mode.</summary>
        public void OpenModeList()
        {
            lock (_sync)
            {
                if (!IsOpen) return;
                IsModeListOpen = true;
                int index = 0;
                for (int i = 0; i < Modes.Count; i++) if (Modes[i] == Mode) index = i;
                ModeListIndex = index;
                ModeListFirstRow = Math.Clamp(index - ModeListRows + 1, 0, Math.Max(0, Modes.Count - ModeListRows));
            }
        }

        private void MoveModeCursor(int delta)
        {
            ModeListIndex = (ModeListIndex + delta + Modes.Count) % Modes.Count;
            if (ModeListIndex < ModeListFirstRow) ModeListFirstRow = ModeListIndex;
            else if (ModeListIndex >= ModeListFirstRow + ModeListRows) ModeListFirstRow = ModeListIndex - ModeListRows + 1;
        }

        private void RecallHistory(int delta)
        {
            if (_history.Count == 0) return;
            _historyIndex = Math.Clamp(_historyIndex + delta, 0, _history.Count);
            _text.Clear();
            if (_historyIndex < _history.Count) _text.Append(_history[_historyIndex]);
            _caret = _text.Length;
        }

        /// <summary>The <c>fep</c> image of a mode's highlighted (orange) label.</summary>
        public static int SelectedLabelImage(ChatInputMode mode) => mode switch
        {
            ChatInputMode.Say => 20,
            ChatInputMode.Shout => 21,
            ChatInputMode.Tell => 22,
            ChatInputMode.Party => 23,
            ChatInputMode.Linkshell => 24,
            ChatInputMode.Linkshell2 => 25,
            ChatInputMode.Unity => 26,
            ChatInputMode.AssistJ => 31,
            ChatInputMode.AssistE => 32,
            _ => (int)mode,
        };
    }
}
