// src/Gordian.Engine/Macros/MacroLibrary.cs
using System;
using System.Collections.Generic;
using System.Globalization;

namespace Gordian.Engine.Macros
{
    /// <summary>Which macro palette a key belongs to: the Ctrl row or the Alt row of a macro set.</summary>
    public enum MacroPalette : byte
    {
        None = 0,
        Ctrl = 1,
        Alt = 2,
    }

    /// <summary>
    /// One macro: a title (up to <see cref="MacroLibrary.MaxTitleLength"/> characters, shown on the palette button) and up
    /// to <see cref="MacroLibrary.LinesPerMacro"/> lines, each a chat line or command as typed in the input line
    /// (retail: <c>mcr*.dat</c>, docs/ui/macros.md). Immutable; the library swaps whole macros.
    /// </summary>
    public sealed record Macro
    {
        private static readonly string[] NoLines = Array.Empty<string>();

        public Macro(string? title, IReadOnlyList<string>? lines)
        {
            Title = MacroLibrary.Clip(title, MacroLibrary.MaxTitleLength);
            var kept = new string[MacroLibrary.LinesPerMacro];
            for (int i = 0; i < kept.Length; i++)
            {
                kept[i] = lines != null && i < lines.Count ? MacroLibrary.Clip(lines[i], MacroLibrary.MaxLineLength) : string.Empty;
            }
            Lines = kept;
        }

        /// <summary>An empty macro (no title, six empty lines).</summary>
        public static Macro Empty { get; } = new(string.Empty, NoLines);

        public string Title { get; }

        /// <summary>Always <see cref="MacroLibrary.LinesPerMacro"/> lines; unused lines are empty.</summary>
        public IReadOnlyList<string> Lines { get; }

        /// <summary>True when the macro has neither a title nor any line.</summary>
        public bool IsEmpty
        {
            get
            {
                if (Title.Length > 0) return false;
                foreach (string line in Lines) if (line.Length > 0) return false;
                return true;
            }
        }

        /// <summary>A copy with one line replaced.</summary>
        public Macro WithLine(int index, string text)
        {
            var lines = new string[MacroLibrary.LinesPerMacro];
            for (int i = 0; i < lines.Length; i++) lines[i] = i == index ? text : Lines[i];
            return new Macro(Title, lines);
        }

        /// <summary>A copy with a new title.</summary>
        public Macro WithTitle(string title) => new(title, Lines);

        public bool Equals(Macro? other)
        {
            if (other is null) return false;
            if (ReferenceEquals(this, other)) return true;
            if (!string.Equals(Title, other.Title, StringComparison.Ordinal)) return false;
            for (int i = 0; i < MacroLibrary.LinesPerMacro; i++)
            {
                if (!string.Equals(Lines[i], other.Lines[i], StringComparison.Ordinal)) return false;
            }
            return true;
        }

        public override int GetHashCode()
        {
            var hash = new HashCode();
            hash.Add(Title);
            foreach (string line in Lines) hash.Add(line);
            return hash.ToHashCode();
        }
    }

    /// <summary>Where a macro lives: book (0-39), set (0-9), palette and key (0-9 for keys 1..0).</summary>
    public readonly record struct MacroSlot(int Book, int Set, MacroPalette Palette, int Key)
    {
        /// <summary>The macro's index within its set: Ctrl 1..0 are 0-9, Alt 1..0 are 10-19 (the retail file's order).</summary>
        public int IndexInSet => (Palette == MacroPalette.Alt ? MacroLibrary.KeysPerPalette : 0) + Key;

        /// <summary>The key's label as the palette shows it: "Ctrl1".."Ctrl0", "Alt1".."Alt0".</summary>
        public string KeyLabel => MacroLibrary.KeyLabel(Palette, Key);
    }

    /// <summary>
    /// A character's macros, as retail lays them out (BG-Wiki "Macro"; the retail <c>mcr*.dat</c> files, docs/ui/macros.md):
    /// 40 macro books, each with a title and 10 macro sets; each set holds 20 macros, the Ctrl palette's 10 (Ctrl+1 ..
    /// Ctrl+0) and the Alt palette's 10. One book and set is current: its macros run from the keys and show on the palette.
    /// Thread-safe (edited on the UI thread, read by the renderer and the input tick).
    /// </summary>
    public sealed class MacroLibrary
    {
        public const int BookCount = 40;
        public const int SetsPerBook = 10;
        public const int KeysPerPalette = 10;
        public const int MacrosPerSet = 2 * KeysPerPalette;
        public const int LinesPerMacro = 6;

        /// <summary>Longest macro line: 60 characters (the retail file keeps 61 bytes per line, the last always NUL).</summary>
        public const int MaxLineLength = 60;

        /// <summary>Longest macro title: 8 characters (the retail file keeps 10 bytes; titles of 8 are common, none longer).</summary>
        public const int MaxTitleLength = 8;

        /// <summary>Longest book title: 15 characters (16 bytes per title in <c>mcr.ttl</c>, NUL-terminated).</summary>
        public const int MaxBookTitleLength = 15;

        private readonly object _sync = new();
        private readonly Macro[][] _sets = new Macro[BookCount * SetsPerBook][];
        private readonly string[] _bookTitles = new string[BookCount];
        private int _book;
        private int _set;

        public MacroLibrary()
        {
            for (int i = 0; i < _sets.Length; i++) _sets[i] = NewSet();
            for (int i = 0; i < BookCount; i++) _bookTitles[i] = string.Empty;
        }

        /// <summary>Raised after any change (a macro, a title, the current book or set).</summary>
        public event Action? Changed;

        /// <summary>The current book (0-39).</summary>
        public int CurrentBook
        {
            get { lock (_sync) return _book; }
        }

        /// <summary>The current set within the current book (0-9).</summary>
        public int CurrentSet
        {
            get { lock (_sync) return _set; }
        }

        /// <summary>Makes a book and set current (clamped to range).</summary>
        public void SelectSet(int book, int set)
        {
            book = Math.Clamp(book, 0, BookCount - 1);
            set = Math.Clamp(set, 0, SetsPerBook - 1);
            lock (_sync)
            {
                if (_book == book && _set == set) return;
                _book = book;
                _set = set;
            }
            Changed?.Invoke();
        }

        /// <summary>Makes a book current, keeping the set number (retail <c>/macro book n</c>).</summary>
        public void SelectBook(int book) => SelectSet(book, CurrentSet);

        /// <summary>Moves to the next (+1) or previous (-1) set of the current book, wrapping (retail Ctrl/Alt + Up/Down).</summary>
        public void CycleSet(int step)
        {
            int book, set;
            lock (_sync)
            {
                book = _book;
                set = ((_set + step) % SetsPerBook + SetsPerBook) % SetsPerBook;
            }
            SelectSet(book, set);
        }

        /// <summary>A book's title as stored (empty when never named).</summary>
        public string GetStoredBookTitle(int book)
        {
            lock (_sync) return _bookTitles[Math.Clamp(book, 0, BookCount - 1)];
        }

        /// <summary>A book's title, or its default name ("Book01".."Book40", as the retail title files hold for unnamed books).</summary>
        public string GetBookTitle(int book)
        {
            string stored = GetStoredBookTitle(book);
            return stored.Length > 0 ? stored : DefaultBookTitle(book);
        }

        /// <summary>The name retail gives an unnamed book: "Book" and its two-digit number.</summary>
        public static string DefaultBookTitle(int book) => "Book" + (Math.Clamp(book, 0, BookCount - 1) + 1).ToString("00", CultureInfo.InvariantCulture);

        public void SetBookTitle(int book, string? title)
        {
            string clipped = Clip(title, MaxBookTitleLength);
            if (clipped == DefaultBookTitle(book)) clipped = string.Empty;
            lock (_sync)
            {
                ref string slot = ref _bookTitles[Math.Clamp(book, 0, BookCount - 1)];
                if (slot == clipped) return;
                slot = clipped;
            }
            Changed?.Invoke();
        }

        public Macro Get(MacroSlot slot)
        {
            lock (_sync) return SetOf(slot.Book, slot.Set)[slot.IndexInSet];
        }

        /// <summary>The macro on a key of the current set.</summary>
        public Macro GetCurrent(MacroPalette palette, int key)
        {
            lock (_sync) return SetOf(_book, _set)[Index(palette, key)];
        }

        /// <summary>The slot of a key in the current set.</summary>
        public MacroSlot CurrentSlot(MacroPalette palette, int key)
        {
            lock (_sync) return new MacroSlot(_book, _set, palette, Math.Clamp(key, 0, KeysPerPalette - 1));
        }

        public void Set(MacroSlot slot, Macro macro)
        {
            ArgumentNullException.ThrowIfNull(macro);
            lock (_sync)
            {
                var set = SetOf(slot.Book, slot.Set);
                if (set[slot.IndexInSet].Equals(macro)) return;
                set[slot.IndexInSet] = macro;
            }
            Changed?.Invoke();
        }

        /// <summary>A copy of a set's 20 macros (Ctrl 1..0 then Alt 1..0).</summary>
        public Macro[] GetSet(int book, int set)
        {
            lock (_sync) return (Macro[])SetOf(book, set).Clone();
        }

        /// <summary>Replaces a set's 20 macros (missing entries become empty).</summary>
        public void ReplaceSet(int book, int set, IReadOnlyList<Macro>? macros)
        {
            lock (_sync)
            {
                var target = SetOf(book, set);
                for (int i = 0; i < MacrosPerSet; i++) target[i] = macros != null && i < macros.Count && macros[i] != null ? macros[i] : Macro.Empty;
            }
            Changed?.Invoke();
        }

        /// <summary>A copy of a whole book: its title (stored) and its ten sets.</summary>
        public (string Title, Macro[][] Sets) GetBook(int book)
        {
            lock (_sync)
            {
                var sets = new Macro[SetsPerBook][];
                for (int s = 0; s < SetsPerBook; s++) sets[s] = (Macro[])SetOf(book, s).Clone();
                return (_bookTitles[Math.Clamp(book, 0, BookCount - 1)], sets);
            }
        }

        /// <summary>Replaces a whole book (title and sets); null sets become empty.</summary>
        public void ReplaceBook(int book, string? title, IReadOnlyList<IReadOnlyList<Macro>>? sets)
        {
            lock (_sync)
            {
                _bookTitles[Math.Clamp(book, 0, BookCount - 1)] = Clip(title, MaxBookTitleLength);
                for (int s = 0; s < SetsPerBook; s++)
                {
                    var target = SetOf(book, s);
                    var source = sets != null && s < sets.Count ? sets[s] : null;
                    for (int i = 0; i < MacrosPerSet; i++) target[i] = source != null && i < source.Count && source[i] != null ? source[i] : Macro.Empty;
                }
            }
            Changed?.Invoke();
        }

        /// <summary>
        /// Replaces the whole library in one change: <paramref name="sets"/> indexed book x 10 + set (null entries and
        /// missing ones become empty), the 40 book titles (null = unnamed), and the current set.
        /// </summary>
        public void ReplaceAll(IReadOnlyList<Macro[]?> sets, IReadOnlyList<string?>? bookTitles, int book, int set)
        {
            ArgumentNullException.ThrowIfNull(sets);
            lock (_sync)
            {
                for (int i = 0; i < _sets.Length; i++)
                {
                    var source = i < sets.Count ? sets[i] : null;
                    var target = NewSet();
                    if (source != null)
                    {
                        for (int m = 0; m < MacrosPerSet && m < source.Length; m++) target[m] = source[m] ?? Macro.Empty;
                    }
                    _sets[i] = target;
                }
                for (int b = 0; b < BookCount; b++)
                {
                    string title = Clip(bookTitles != null && b < bookTitles.Count ? bookTitles[b] : null, MaxBookTitleLength);
                    _bookTitles[b] = title == DefaultBookTitle(b) ? string.Empty : title;
                }
                _book = Math.Clamp(book, 0, BookCount - 1);
                _set = Math.Clamp(set, 0, SetsPerBook - 1);
            }
            Changed?.Invoke();
        }

        /// <summary>Clears every book, title and the current set (before an import of a whole library).</summary>
        public void Clear()
        {
            lock (_sync)
            {
                for (int i = 0; i < _sets.Length; i++) _sets[i] = NewSet();
                for (int i = 0; i < BookCount; i++) _bookTitles[i] = string.Empty;
                _book = 0;
                _set = 0;
            }
            Changed?.Invoke();
        }

        /// <summary>Whether a set holds any macro.</summary>
        public bool IsSetEmpty(int book, int set)
        {
            lock (_sync)
            {
                foreach (var macro in SetOf(book, set)) if (!macro.IsEmpty) return false;
                return true;
            }
        }

        /// <summary>The key label a palette button shows: "Ctrl1".."Ctrl9", "Ctrl0" for the tenth key; likewise "Alt".</summary>
        public static string KeyLabel(MacroPalette palette, int key) =>
            (palette == MacroPalette.Alt ? "Alt" : "Ctrl") + ((key + 1) % 10).ToString(CultureInfo.InvariantCulture);

        /// <summary>Text cut to a length, with characters a macro cannot hold dropped (control characters).</summary>
        public static string Clip(string? text, int max)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            Span<char> buffer = stackalloc char[Math.Min(text.Length, max)];
            int n = 0;
            foreach (char c in text)
            {
                if (n >= buffer.Length) break;
                if (c < 0x20 || c == 0x7F) continue;
                buffer[n++] = c;
            }
            return new string(buffer[..n]);
        }

        private Macro[] SetOf(int book, int set) =>
            _sets[Math.Clamp(book, 0, BookCount - 1) * SetsPerBook + Math.Clamp(set, 0, SetsPerBook - 1)];

        private static int Index(MacroPalette palette, int key) =>
            (palette == MacroPalette.Alt ? KeysPerPalette : 0) + Math.Clamp(key, 0, KeysPerPalette - 1);

        private static Macro[] NewSet()
        {
            var set = new Macro[MacrosPerSet];
            Array.Fill(set, Macro.Empty);
            return set;
        }
    }
}
