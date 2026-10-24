// src/Gordian.Core/Macros/MacroStore.cs
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Gordian.Core.Config;
using Gordian.Core.Diagnostics;

namespace Gordian.Engine.Macros
{
    /// <summary>
    /// GordianXI's own copy of a character's macros, kept client-side per character as retail keeps its <c>mcr*.dat</c> in
    /// the character's <c>USER</c> folder: <c>macros/&lt;character&gt;.json</c> under <see cref="GordianStorage.RootDataDirectory"/>,
    /// saved on every change. Only books, sets and macros that hold something are written. The retail files are never
    /// written; <c>/importretail &lt;folder&gt; macros</c> copies them in (<see cref="RetailMacroFiles"/>).
    /// </summary>
    public static class MacroStore
    {
        private const string FolderName = "macros";
        private const string FileExtension = ".json";

        private static readonly ConcurrentDictionary<string, MacroLibrary> Libraries = new(StringComparer.OrdinalIgnoreCase);

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };

        public static string Directory => Path.Combine(GordianStorage.RootDataDirectory, FolderName);

        public static string GetPath(string? characterName)
        {
            string name = string.IsNullOrWhiteSpace(characterName) ? "default" : characterName.Trim();
            foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            return Path.Combine(Directory, name + FileExtension);
        }

        /// <summary>The character's library (loaded once, saved on change).</summary>
        public static MacroLibrary GetForCharacter(string? characterName)
        {
            string key = string.IsNullOrWhiteSpace(characterName) ? string.Empty : characterName.Trim();
            return Libraries.GetOrAdd(key, name =>
            {
                string path = GetPath(name);
                var library = LoadOrEmpty(path);
                library.Changed += () =>
                {
                    try
                    {
                        Save(library, path);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        GordianLog.Warning("MACRO", $"Could not save the macros '{path}': {ex.Message}");
                    }
                };
                return library;
            });
        }

        public static MacroLibrary LoadOrEmpty(string path)
        {
            var library = new MacroLibrary();
            if (!File.Exists(path)) return library;
            try
            {
                var document = JsonSerializer.Deserialize<Document>(File.ReadAllText(path), JsonOptions);
                if (document != null) Apply(document, library);
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                GordianLog.Warning("MACRO", $"Could not read the macros '{path}': {ex.Message}");
            }
            return library;
        }

        public static void Save(MacroLibrary library, string path)
        {
            ArgumentNullException.ThrowIfNull(library);
            string? dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) System.IO.Directory.CreateDirectory(dir);
            string json = JsonSerializer.Serialize(ToDocument(library), JsonOptions);
            string temp = path + ".tmp";
            File.WriteAllText(temp, json);
            File.Move(temp, path, overwrite: true);
        }

        /// <summary>Serializes a library to the store's JSON (for tests and the save).</summary>
        public static string ToJson(MacroLibrary library) => JsonSerializer.Serialize(ToDocument(library), JsonOptions);

        /// <summary>Loads a library from the store's JSON.</summary>
        public static MacroLibrary FromJson(string json)
        {
            var library = new MacroLibrary();
            var document = JsonSerializer.Deserialize<Document>(json, JsonOptions);
            if (document != null) Apply(document, library);
            return library;
        }

        private static Document ToDocument(MacroLibrary library)
        {
            var document = new Document { Book = library.CurrentBook + 1, Set = library.CurrentSet + 1 };
            for (int b = 0; b < MacroLibrary.BookCount; b++)
            {
                var (title, sets) = library.GetBook(b);
                BookDocument? book = title.Length > 0 ? new BookDocument { Number = b + 1, Title = title } : null;
                for (int s = 0; s < sets.Length; s++)
                {
                    SetDocument? set = null;
                    for (int m = 0; m < sets[s].Length; m++)
                    {
                        var macro = sets[s][m];
                        if (macro.IsEmpty) continue;
                        set ??= new SetDocument { Number = s + 1 };
                        var lines = new List<string>(macro.Lines);
                        while (lines.Count > 0 && lines[^1].Length == 0) lines.RemoveAt(lines.Count - 1);
                        set.Macros.Add(new MacroDocument
                        {
                            Key = MacroLibrary.KeyLabel(m < MacroLibrary.KeysPerPalette ? MacroPalette.Ctrl : MacroPalette.Alt, m % MacroLibrary.KeysPerPalette),
                            Title = macro.Title.Length > 0 ? macro.Title : null,
                            Lines = lines,
                        });
                    }
                    if (set == null) continue;
                    book ??= new BookDocument { Number = b + 1 };
                    book.Sets.Add(set);
                }
                if (book != null) document.Books.Add(book);
            }
            return document;
        }

        private static void Apply(Document document, MacroLibrary library)
        {
            var sets = new Macro[MacroLibrary.BookCount * MacroLibrary.SetsPerBook][];
            var titles = new string?[MacroLibrary.BookCount];
            foreach (var book in document.Books)
            {
                int b = book.Number - 1;
                if (b is < 0 or >= MacroLibrary.BookCount) continue;
                titles[b] = book.Title;
                foreach (var set in book.Sets)
                {
                    int s = set.Number - 1;
                    if (s is < 0 or >= MacroLibrary.SetsPerBook) continue;
                    var macros = new Macro[MacroLibrary.MacrosPerSet];
                    foreach (var macro in set.Macros)
                    {
                        if (!TryParseKey(macro.Key, out int index)) continue;
                        macros[index] = new Macro(macro.Title, macro.Lines);
                    }
                    sets[b * MacroLibrary.SetsPerBook + s] = macros;
                }
            }
            library.ReplaceAll(sets, titles, document.Book - 1, document.Set - 1);
        }

        /// <summary>"Ctrl1".."Ctrl0" and "Alt1".."Alt0" to the index in the set (0-19).</summary>
        private static bool TryParseKey(string? key, out int index)
        {
            index = -1;
            if (string.IsNullOrEmpty(key) || key.Length < 2) return false;
            char digit = key[^1];
            if (digit is < '0' or > '9') return false;
            int k = digit == '0' ? 9 : digit - '1';
            string palette = key[..^1];
            if (palette.Equals("Ctrl", StringComparison.OrdinalIgnoreCase)) index = k;
            else if (palette.Equals("Alt", StringComparison.OrdinalIgnoreCase)) index = MacroLibrary.KeysPerPalette + k;
            return index >= 0;
        }

        private sealed class Document
        {
            /// <summary>The current book, 1-40.</summary>
            public int Book { get; set; } = 1;

            /// <summary>The current set, 1-10.</summary>
            public int Set { get; set; } = 1;

            public List<BookDocument> Books { get; set; } = new();
        }

        private sealed class BookDocument
        {
            public int Number { get; set; }
            public string? Title { get; set; }
            public List<SetDocument> Sets { get; set; } = new();
        }

        private sealed class SetDocument
        {
            public int Number { get; set; }
            public List<MacroDocument> Macros { get; set; } = new();
        }

        private sealed class MacroDocument
        {
            public string Key { get; set; } = string.Empty;
            public string? Title { get; set; }
            public List<string> Lines { get; set; } = new();
        }
    }
}
