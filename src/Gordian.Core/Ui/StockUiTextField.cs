// src/Gordian.Core/Ui/StockUiTextField.cs
using System;
using System.Text;
using Gordian.Core.Input;

namespace Gordian.Core.Ui
{
    /// <summary>
    /// A one-line text field a stock window edits in place (the macro editor's title and lines, a macro book's name; #339).
    /// While open it owns the keyboard as the chat input line does (the viewport feeds it keys and text): Enter keeps the
    /// text, Escape drops it, Up / Down keep it and ask to move to the field above / below. Printable ASCII only, cut at
    /// <see cref="MaxLength"/>. Accessed from the UI thread (keys, text) and the render thread (drawing), so state changes
    /// take a lock.
    /// </summary>
    public sealed class StockUiTextField
    {
        private readonly object _sync = new();
        private readonly StringBuilder _text = new();
        private int _caret;
        private Action<string, int>? _commit;

        /// <summary>True while the field is being edited.</summary>
        public bool IsOpen { get; private set; }

        /// <summary>Longest text the field takes.</summary>
        public int MaxLength { get; private set; }

        public string Text
        {
            get { lock (_sync) return _text.ToString(); }
        }

        public int Caret
        {
            get { lock (_sync) return _caret; }
        }

        /// <summary>Raised when the field opens or closes.</summary>
        public event Action<bool>? OpenChanged;

        /// <summary>
        /// Starts editing <paramref name="text"/> with the caret at its end. <paramref name="commit"/> gets the kept text and
        /// a step: 0 for Enter, -1 / +1 for Up / Down.
        /// </summary>
        public void Open(string text, int maxLength, Action<string, int> commit)
        {
            ArgumentNullException.ThrowIfNull(commit);
            lock (_sync)
            {
                MaxLength = Math.Max(1, maxLength);
                _text.Clear();
                foreach (char c in text ?? string.Empty)
                {
                    if (_text.Length >= MaxLength) break;
                    if (c is >= ' ' and < (char)0x7F) _text.Append(c);
                }
                _caret = _text.Length;
                _commit = commit;
                IsOpen = true;
            }
            OpenChanged?.Invoke(true);
        }

        /// <summary>Drops the edit (Escape, or the gamepad's cancel).</summary>
        public void Cancel()
        {
            lock (_sync)
            {
                if (!IsOpen) return;
                IsOpen = false;
                _commit = null;
            }
            OpenChanged?.Invoke(false);
        }

        /// <summary>Keeps the edit (Enter, or the gamepad's confirm).</summary>
        public void Commit(int step = 0)
        {
            Action<string, int>? commit;
            string text;
            lock (_sync)
            {
                if (!IsOpen) return;
                IsOpen = false;
                commit = _commit;
                _commit = null;
                text = _text.ToString();
            }
            OpenChanged?.Invoke(false);
            commit?.Invoke(text, step);
        }

        /// <summary>Types text at the caret (the platform's text input); characters past ASCII are dropped.</summary>
        public void InsertText(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            lock (_sync)
            {
                if (!IsOpen) return;
                foreach (char c in text)
                {
                    if (c < 0x20 || c > 0x7E || _text.Length >= MaxLength) continue;
                    _text.Insert(_caret++, c);
                }
            }
        }

        /// <summary>Handles an editing key while open; returns false when closed (the key goes elsewhere).</summary>
        public bool HandleKey(GordianKey key, InputModifiers modifiers)
        {
            int commitStep = int.MinValue;
            bool cancel = false;
            lock (_sync)
            {
                if (!IsOpen) return false;
                switch (key)
                {
                    case GordianKey.Enter or GordianKey.NumPadEnter: commitStep = 0; break;
                    case GordianKey.Up: commitStep = -1; break;
                    case GordianKey.Down: commitStep = 1; break;
                    case GordianKey.Escape: cancel = true; break;
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
                }
            }
            if (cancel) Cancel();
            else if (commitStep != int.MinValue) Commit(commitStep);
            return true;
        }
    }
}
