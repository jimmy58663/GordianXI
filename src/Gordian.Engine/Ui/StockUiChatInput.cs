// src/Gordian.Engine/Ui/StockUiChatInput.cs
using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using Gordian.Core.Network;
using Gordian.Core.Resources;
using Gordian.Engine.Input;

namespace Gordian.Engine.Ui
{
    /// <summary>
    /// The default chat modes: what a line typed without a slash command is sent as. Values are the <c>fep</c>
    /// element group's grey label images (the input line's mode tab); <see cref="Window1"/> and
    /// <see cref="Window2"/> are only labels (the log windows' titles are laid out from them).
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
        Window1 = 18,
        Window2 = 19,
        AssistJ = 29,
        AssistE = 30,
    }

    /// <summary>
    /// The stock chat input line (<c>inline</c>): opened from gameplay, it takes the keyboard until the line is sent
    /// (Enter) or dropped (Escape, or B on a gamepad), so typing never moves the character. Holds the text and caret,
    /// the sent-line history (Up/Down) and the default chat mode (set by <c>/chatmode</c>). Accessed from the UI
    /// thread (keys, text) and the render thread (drawing), so state changes take a lock.
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


        private readonly object _sync = new();
        private readonly StringBuilder _text = new();
        private readonly List<ChatText> _history = new();
        private readonly List<ChatPhraseSpan> _phrases = new();
        private AutoTranslateCatalog _catalog = AutoTranslateCatalog.Empty;
        public AutoTranslateCatalog Catalog { get { lock (_sync) return _catalog; } set { lock (_sync) { _catalog = value; _completion = null; } } }
        public ChatText RichText { get { lock (_sync) return new(_text.ToString(), _phrases); } }
        public event Action<ChatText, ChatInputMode>? RichSubmitted;
        private CompletionView? _completion;
        private int _completionStart;
        public CompletionView Completion { get { lock (_sync) return _completion ?? new(false, [], 0); } }
        public sealed record CompletionRow(string Label, uint Key);
        public sealed record CompletionView(bool IsOpen, IReadOnlyList<CompletionRow> Rows, int Selected, string? Notice = null);
        private int _caret;
        private int _historyIndex;
        private string _swallowText = string.Empty;
        private Action<int>? _numberEntered;
        private int _numberMaxDigits;

        /// <summary>True while the input line is open (it owns the keyboard).</summary>
        public bool IsOpen { get; private set; }

        /// <summary>
        /// True while the line takes an event's number (<see cref="OpenNumber"/>) instead of a chat line.
        /// </summary>
        public bool IsNumberEntry
        {
            get { lock (_sync) return _numberEntered != null; }
        }


        public ChatInputMode Mode { get; private set; } = ChatInputMode.Say;


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

        /// <summary>
        /// Scroll requests while the line is open: the window (1 or 2), the amount (positive = back through history)
        /// and whether it counts pages (true) or lines.
        /// </summary>
        public event Action<int, int, bool>? ScrollRequested;

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
                _text.Clear();
                _phrases.Clear();
                _completion = null;
                _caret = 0;
                _historyIndex = _history.Count;
                _swallowText = swallowText ?? string.Empty;
            }
            OpenChanged?.Invoke(true);
        }

        /// <summary>
        /// Opens the line for an event's number (opcode 0x71 subs 0x10 / 0x12, XiEvents OpCodes/0x0071): a treasure casket's
        /// "Enter a combination" is typed into the chat input line in retail (the maintainer's retail check, #582 round 3),
        /// not picked in a window. Only digits are typed, at most <paramref name="maxDigits"/>; Enter answers with the number
        /// (0 for an empty line, as the client's <c>atoi</c> reads it), and Escape or anything else that closes the line
        /// answers -1. The line is not sent as chat and is kept out of the history. A chat line being typed is dropped.
        /// </summary>
        public void OpenNumber(int maxDigits, Action<int> completed)
        {
            ArgumentNullException.ThrowIfNull(completed);
            Close();
            lock (_sync)
            {
                _numberEntered = completed;
                _numberMaxDigits = Math.Clamp(maxDigits, 1, 9);
            }
            Open();
        }

        public void Close()
        {
            Action<int>? numberEntered;
            lock (_sync)
            {
                numberEntered = _numberEntered;
                _numberEntered = null;
                if (!IsOpen) return;
                IsOpen = false;
                _text.Clear();
                _phrases.Clear();
                _completion = null;
                _caret = 0;
                _swallowText = string.Empty;
            }
            OpenChanged?.Invoke(false);
            numberEntered?.Invoke(-1);
        }


        /// <summary>Drops the line (Escape, or the gamepad's cancel button).</summary>
        public void Cancel() => Close();

        public void SetMode(ChatInputMode mode)
        {
            lock (_sync) Mode = mode;
        }

        /// <summary>
        /// Types text at the caret (from the platform's text input, so shifted and layout-specific characters come
        /// through). Characters the log font cannot draw (control characters, anything past ASCII) are dropped.
        /// </summary>
        public void InsertText(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            lock (_sync)
            {
                if (!IsOpen) return;
                if (_swallowText.Length > 0)
                {
                    bool swallow = text == _swallowText;
                    _swallowText = string.Empty;
                    if (swallow) return;
                }
                InsertUnlocked(text);
            }
        }

        /// <summary>
        /// Types the character a key press produces (its layout-aware key symbol). Used because a handled key press
        /// may or may not be followed by the platform's text input for it; if that text does arrive next it is
        /// dropped, so the character is typed once either way.
        /// </summary>
        public void InsertKeySymbol(string symbol)
        {
            if (string.IsNullOrEmpty(symbol)) return;
            lock (_sync)
            {
                if (!IsOpen) return;
                InsertUnlocked(symbol);
                _swallowText = symbol;
            }
        }

        /// <summary>True for a key symbol that types into the line (one printable ASCII character).</summary>
        public static bool IsTypedSymbol(string? symbol) => symbol is { Length: 1 } && symbol[0] >= 0x20 && symbol[0] <= 0x7E;

        private void InsertUnlocked(string text)
        {
            foreach (char c in text)
            {
                if (c < 0x20 || c > 0x7E || _text.Length >= MaxLength) continue;
                if (_numberEntered != null && (c < '0' || c > '9' || _text.Length >= _numberMaxDigits)) continue;
                EditSpans(_caret, 0, 1);
                _text.Insert(_caret++, c);
            }
        }

        /// <summary>
        /// Handles a key while the line is open (returns false when closed, so the key goes to gameplay).
        /// Every key is consumed while open: the character does not move while you type.
        /// </summary>
        public bool HandleKey(GordianKey key, InputModifiers modifiers)
        {
            string? submitted = null;
            ChatText? richSubmitted = null;
            Action<int>? numberEntered = null;
            int number = 0;
            ChatInputMode submittedMode;
            bool close = false, cancel = false;
            int scrollWindow = 0, scrollAmount = 0;
            bool scrollPages = false;
            lock (_sync)
            {
                if (!IsOpen) return false;
                _swallowText = string.Empty;
                submittedMode = Mode;
                bool ctrl = (modifiers & InputModifiers.Control) != 0;

                if (_numberEntered == null && !ctrl)
                {
                    if (key == GordianKey.Tab)
                    {
                        if (_completion == null) OpenCompletion();
                        else MoveCompletion((modifiers & InputModifiers.Shift) != 0 ? -1 : 1);
                        return true;
                    }
                    if (_completion != null)
                    {
                        if (key == GordianKey.Escape) { _completion = null; return true; }
                        if (key is GordianKey.Up or GordianKey.Down) { MoveCompletion(key == GordianKey.Up ? -1 : 1); return true; }
                        if (key is GordianKey.Enter or GordianKey.NumPadEnter) { SelectCompletion(); return true; }
                        _completion = null;
                    }
                }
                switch (key)
                {
                    case GordianKey.Enter or GordianKey.NumPadEnter when _numberEntered != null:
                        numberEntered = _numberEntered;
                        _numberEntered = null;
                        number = int.TryParse(_text.ToString(), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int typed) ? typed : 0;
                        close = true;
                        break;
                    case GordianKey.Enter or GordianKey.NumPadEnter:
                        string line = _text.ToString().Trim();
                        if (line.Length > 0)
                        {
                            submitted = line;
                            int trimStart = _text.ToString().Length - _text.ToString().TrimStart().Length;
                            richSubmitted = new ChatText(_text.ToString(), _phrases).Slice(trimStart, line.Length);
                            if (_history.Count == 0 || _history[^1].Text != line || !_history[^1].Phrases.SequenceEqual(richSubmitted.Phrases))
                            {
                                _history.Add(richSubmitted);
                                if (_history.Count > HistoryCapacity) _history.RemoveAt(0);
                            }
                        }
                        close = true;
                        break;
                    case GordianKey.Escape:
                        cancel = true;
                        break;
                    case GordianKey.Backspace:
                        if (_caret > 0) { EditSpans(_caret - 1, 1, 0); _text.Remove(--_caret, 1); }
                        break;
                    case GordianKey.Delete:
                        if (_caret < _text.Length) { EditSpans(_caret, 1, 0); _text.Remove(_caret, 1); }
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
                    case GordianKey.Up when _numberEntered == null:
                        RecallHistory(-1);
                        break;
                    case GordianKey.Down when _numberEntered == null:
                        RecallHistory(1);
                        break;
                    case GordianKey.PageUp:
                        (scrollWindow, scrollAmount, scrollPages) = (ctrl ? 2 : 1, 1, true);
                        break;
                    case GordianKey.PageDown:
                        (scrollWindow, scrollAmount, scrollPages) = (ctrl ? 2 : 1, -1, true);
                        break;
                }
            }

            if (scrollAmount != 0) ScrollRequested?.Invoke(scrollWindow, scrollAmount, scrollPages);
            if (cancel) Cancel();
            if (close)
            {
                Close();
                if (richSubmitted != null) RichSubmitted?.Invoke(richSubmitted, submittedMode);
                if (submitted != null) Submitted?.Invoke(submitted, submittedMode);
                numberEntered?.Invoke(number);
            }
            return true;
        }


        private void RecallHistory(int delta)
        {
            if (_history.Count == 0) return;
            _historyIndex = Math.Clamp(_historyIndex + delta, 0, _history.Count);
            _text.Clear();
            _phrases.Clear();
            _completion = null;
            if (_historyIndex < _history.Count) { _text.Append(_history[_historyIndex].Text); _phrases.AddRange(_history[_historyIndex].Phrases); }
            _caret = _text.Length;
        }


        // Editing any part of a phrase makes it plain text (official PlayOnline dictionary guide).
        private void EditSpans(int start, int removed, int added)
        {
            _completion = null;
            for (int i = _phrases.Count - 1; i >= 0; i--)
            {
                var p = _phrases[i];
                bool overlaps = removed > 0 ? start < p.Start + p.Length && start + removed > p.Start
                    : start > p.Start && start < p.Start + p.Length;
                if (overlaps) _phrases.RemoveAt(i);
                else if (p.Start >= start + removed) _phrases[i] = p with { Start = p.Start + added - removed };
            }
        }
        private void OpenCompletion()
        {
            string text = _text.ToString();
            _completionStart = _caret;
            var usable = _catalog.Phrases.Where(p => p.Label.All(c => c >= 32 && c <= 126)).ToArray();
            CompletionRow[] rows = [];
            // Longest word-boundary suffix with matches, including multi-word prefixes.
            if (_caret > 0 && !char.IsWhiteSpace(text[_caret - 1]))
            {
                for (int start = 0; start < _caret; start++)
                {
                    if (start > 0 && !char.IsWhiteSpace(text[start - 1]) && text[start - 1] != '"') continue;
                    if (_phrases.Any(p => start < p.Start + p.Length && _caret > p.Start)) continue;
                    string prefix = text[start.._caret];
                    rows = usable.Where(p => p.Label.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                        .Select(p => new CompletionRow(p.Label, p.Key)).ToArray();
                    if (rows.Length > 0) { _completionStart = start; break; }
                }
            }
            else rows = usable.Select(p => p.Category).Distinct().Select(c => new CompletionRow(c, 0)).ToArray();
            _completion = new(true, Array.AsReadOnly(rows), 0, _catalog.Phrases.Count == 0 ? "Dictionary unavailable (English ROM/168/25)"
                : _catalog.UnresolvedCount > 0 ? "Some dictionary references unavailable" : null);
            // Only a unique prefix result is immediate; category browsing still needs confirmation.
            if (rows.Length == 1 && rows[0].Key != 0) SelectCompletion();
        }
        private void MoveCompletion(int delta)
        {
            if (_completion is not { Rows.Count: > 0 } view) return;
            _completion = view with { Selected = (view.Selected + delta + view.Rows.Count) % view.Rows.Count };
        }
        private void SelectCompletion()
        {
            if (_completion is not { Rows.Count: > 0 } view) return;
            var row = view.Rows[view.Selected];
            if (row.Key == 0)
            {
                var rows = _catalog.Phrases.Where(p => p.Category == row.Label && p.Label.All(c => c >= 32 && c <= 126))
                    .Select(p => new CompletionRow(p.Label, p.Key)).ToArray();
                _completion = new(true, Array.AsReadOnly(rows), 0); return;
            }
            int removed = _caret - _completionStart;
            if (_text.Length - removed + row.Label.Length > MaxLength) return;
            int at = _completionStart;
            EditSpans(at, removed, row.Label.Length);
            _text.Remove(at, removed).Insert(at, row.Label);
            _phrases.Add(new(at, row.Label.Length, row.Key));
            _phrases.Sort((a, b) => a.Start.CompareTo(b.Start));
            _caret = at + row.Label.Length;
        }

        /// <summary>An entry's label text, as its <c>fep</c> sprite reads.</summary>
        public static string Label(ChatInputMode mode) => mode switch
        {
            ChatInputMode.Say => "Say",
            ChatInputMode.Shout => "Shout",
            ChatInputMode.Tell => "Tell",
            ChatInputMode.Party => "Party",
            ChatInputMode.Linkshell => "Linkshell",
            ChatInputMode.Linkshell2 => "Linkshell 2",
            ChatInputMode.Unity => "Unity",
            ChatInputMode.Window1 => "Window 1",
            ChatInputMode.Window2 => "Window 2",
            ChatInputMode.AssistJ => "Assist J",
            ChatInputMode.AssistE => "Assist E",
            _ => mode.ToString(),
        };
    }
}
