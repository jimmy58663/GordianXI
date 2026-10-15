// src/Gordian.Core/Macros/RetailMacroFiles.cs
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;

namespace Gordian.Core.Macros
{
    /// <summary>What an import of retail macro files brought in.</summary>
    public readonly record struct RetailMacroImport(int Sets, int Macros, int BookTitles, bool CurrentSet, int DroppedPhrases);

    /// <summary>
    /// Reads (and writes, for tests and a later export) the retail client's macro files in a character's <c>USER</c> folder.
    /// Our own reading of the maintainer's files (16 characters, 2026-10-12); no public reference documents them. Layout
    /// and evidence: docs/ui/macros.md. Every file starts with the same 24-byte header: a u32 version (1), a u32 the client
    /// does not appear to use (it holds varying values; written as 0), and the MD5 of the rest of the file.
    /// <list type="bullet">
    /// <item><c>mcr.dat</c>, <c>mcr1.dat</c> .. <c>mcr399.dat</c> (7624 bytes): one macro set each, file number = book x 10 +
    /// set (0-based; <c>mcr.dat</c> is 0). A missing file is an empty set. After the header, 20 macros of 380 bytes, Ctrl 1..0
    /// then Alt 1..0: 4 zero bytes, six lines of 61 bytes (60 characters and a NUL), the title in 10 bytes (8 characters,
    /// NUL-terminated; bytes after the NUL are leftovers).</item>
    /// <item><c>mcr.ttl</c> / <c>mcr_2.ttl</c> (344 bytes): the titles of books 1-20 / 21-40, 16 bytes each, NUL-terminated.</item>
    /// <item><c>mcr.sys</c> (28 bytes): one u32, the current set as book x 10 + set (PROVISIONAL: inferred from the files'
    /// values, which all fall in range, e.g. 222 = book 23 set 3; not yet checked against the palette retail shows).</item>
    /// </list>
    /// Text is Shift-JIS; auto-translate phrases (0xFD, five bytes, 0xFD) are dropped on import, since GordianXI's lines are
    /// plain text.
    /// </summary>
    public static class RetailMacroFiles
    {
        public const int HeaderSize = 24;
        public const int MacroSize = 380;
        public const int LineSize = 61;
        public const int TitleSize = 10;
        public const int MacroFileSize = HeaderSize + MacroLibrary.MacrosPerSet * MacroSize;
        public const int BookTitleSize = 16;
        public const int BooksPerTitleFile = 20;
        public const int TitleFileSize = HeaderSize + BooksPerTitleFile * BookTitleSize;
        public const int SystemFileSize = HeaderSize + 4;

        private const int LinesOffset = 4;
        private const int TitleOffset = LinesOffset + MacroLibrary.LinesPerMacro * LineSize;

        /// <summary>The file holding a set: <c>mcr.dat</c> for book 0 set 0, else <c>mcr{book*10+set}.dat</c>.</summary>
        public static string SetFileName(int book, int set)
        {
            int number = book * MacroLibrary.SetsPerBook + set;
            return number == 0 ? "mcr.dat" : "mcr" + number.ToString(CultureInfo.InvariantCulture) + ".dat";
        }

        /// <summary>The title file for a book: <c>mcr.ttl</c> (books 0-19) or <c>mcr_2.ttl</c> (20-39).</summary>
        public static string TitleFileName(int book) => book < BooksPerTitleFile ? "mcr.ttl" : "mcr_2.ttl";

        public const string SystemFileName = "mcr.sys";

        /// <summary>Whether a file's header checksum matches its contents.</summary>
        public static bool HasValidChecksum(ReadOnlySpan<byte> file)
        {
            if (file.Length < HeaderSize) return false;
            Span<byte> hash = stackalloc byte[16];
            MD5.HashData(file[HeaderSize..], hash);
            return hash.SequenceEqual(file.Slice(8, 16));
        }

        /// <summary>Decodes one set file; null when it is not one (wrong size or version).</summary>
        public static Macro[]? DecodeSet(ReadOnlySpan<byte> file) => DecodeSet(file, out _);

        /// <summary>Decodes one set file, counting the auto-translate phrases dropped.</summary>
        public static Macro[]? DecodeSet(ReadOnlySpan<byte> file, out int droppedPhrases)
        {
            droppedPhrases = 0;
            if (file.Length != MacroFileSize || BinaryPrimitives.ReadUInt32LittleEndian(file) != 1) return null;
            var macros = new Macro[MacroLibrary.MacrosPerSet];
            var lines = new string[MacroLibrary.LinesPerMacro];
            for (int i = 0; i < macros.Length; i++)
            {
                var record = file.Slice(HeaderSize + i * MacroSize, MacroSize);
                for (int l = 0; l < lines.Length; l++) lines[l] = DecodeText(record.Slice(LinesOffset + l * LineSize, LineSize), ref droppedPhrases);
                string title = DecodeText(record.Slice(TitleOffset, TitleSize), ref droppedPhrases);
                macros[i] = new Macro(title, lines);
            }
            return macros;
        }

        /// <summary>Encodes a set file (20 macros, Ctrl then Alt) with a valid header.</summary>
        public static byte[] EncodeSet(IReadOnlyList<Macro> macros)
        {
            ArgumentNullException.ThrowIfNull(macros);
            var file = new byte[MacroFileSize];
            for (int i = 0; i < MacroLibrary.MacrosPerSet && i < macros.Count; i++)
            {
                var macro = macros[i] ?? Macro.Empty;
                var record = file.AsSpan(HeaderSize + i * MacroSize, MacroSize);
                for (int l = 0; l < MacroLibrary.LinesPerMacro; l++) EncodeText(macro.Lines[l], record.Slice(LinesOffset + l * LineSize, LineSize));
                EncodeText(macro.Title, record.Slice(TitleOffset, TitleSize));
            }
            WriteHeader(file);
            return file;
        }

        /// <summary>Decodes a title file's 20 book titles; null when it is not one.</summary>
        public static string[]? DecodeTitles(ReadOnlySpan<byte> file)
        {
            if (file.Length != TitleFileSize || BinaryPrimitives.ReadUInt32LittleEndian(file) != 1) return null;
            var titles = new string[BooksPerTitleFile];
            int dropped = 0;
            for (int i = 0; i < titles.Length; i++) titles[i] = DecodeText(file.Slice(HeaderSize + i * BookTitleSize, BookTitleSize), ref dropped);
            return titles;
        }

        public static byte[] EncodeTitles(IReadOnlyList<string> titles)
        {
            ArgumentNullException.ThrowIfNull(titles);
            var file = new byte[TitleFileSize];
            for (int i = 0; i < BooksPerTitleFile && i < titles.Count; i++) EncodeText(titles[i] ?? string.Empty, file.AsSpan(HeaderSize + i * BookTitleSize, BookTitleSize));
            WriteHeader(file);
            return file;
        }

        /// <summary>Decodes <c>mcr.sys</c>: the current (book, set), or null.</summary>
        public static (int Book, int Set)? DecodeSystem(ReadOnlySpan<byte> file)
        {
            if (file.Length != SystemFileSize || BinaryPrimitives.ReadUInt32LittleEndian(file) != 1) return null;
            uint value = BinaryPrimitives.ReadUInt32LittleEndian(file[HeaderSize..]);
            if (value >= MacroLibrary.BookCount * MacroLibrary.SetsPerBook) return null;
            return ((int)value / MacroLibrary.SetsPerBook, (int)value % MacroLibrary.SetsPerBook);
        }

        public static byte[] EncodeSystem(int book, int set)
        {
            var file = new byte[SystemFileSize];
            BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(HeaderSize), (uint)(book * MacroLibrary.SetsPerBook + set));
            WriteHeader(file);
            return file;
        }

        /// <summary>
        /// Imports a retail <c>USER/&lt;id&gt;</c> folder into a library: every set file found, the book titles and the
        /// current set. Sets without a file are cleared (retail shows them empty). The folder is only read.
        /// </summary>
        public static RetailMacroImport Import(string folder, MacroLibrary library)
        {
            ArgumentNullException.ThrowIfNull(folder);
            ArgumentNullException.ThrowIfNull(library);
            int sets = 0, macros = 0, titles = 0, dropped = 0;
            var allSets = new Macro[MacroLibrary.BookCount * MacroLibrary.SetsPerBook][];
            for (int book = 0; book < MacroLibrary.BookCount; book++)
            {
                for (int set = 0; set < MacroLibrary.SetsPerBook; set++)
                {
                    byte[]? bytes = TryRead(Path.Combine(folder, SetFileName(book, set)));
                    int d = 0;
                    var decoded = bytes != null ? DecodeSet(bytes, out d) : null;
                    if (decoded == null) continue;
                    allSets[book * MacroLibrary.SetsPerBook + set] = decoded;
                    sets++;
                    dropped += d;
                    foreach (var macro in decoded) if (!macro.IsEmpty) macros++;
                }
            }
            var bookTitles = new string[MacroLibrary.BookCount];
            foreach (int first in new[] { 0, BooksPerTitleFile })
            {
                byte[]? bytes = TryRead(Path.Combine(folder, TitleFileName(first)));
                if (bytes == null || DecodeTitles(bytes) is not { } decoded) continue;
                for (int i = 0; i < decoded.Length; i++)
                {
                    bookTitles[first + i] = decoded[i];
                    titles++;
                }
            }
            byte[]? system = TryRead(Path.Combine(folder, SystemFileName));
            var current = system != null ? DecodeSystem(system) : null;
            library.ReplaceAll(allSets, bookTitles, current?.Book ?? 0, current?.Set ?? 0);
            return new RetailMacroImport(sets, macros, titles, current != null, dropped);
        }

        /// <summary>Whether a retail folder holds any macro file.</summary>
        public static bool HasMacroFiles(string folder)
        {
            try
            {
                return File.Exists(Path.Combine(folder, SystemFileName)) || File.Exists(Path.Combine(folder, "mcr.ttl"))
                    || Directory.EnumerateFiles(folder, "mcr*.dat").GetEnumerator().MoveNext();
            }
            catch (IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }

        private static byte[]? TryRead(string path)
        {
            try
            {
                return File.Exists(path) ? File.ReadAllBytes(path) : null;
            }
            catch (IOException)
            {
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }
        }

        private static void WriteHeader(byte[] file)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(file, 1);
            MD5.HashData(file.AsSpan(HeaderSize), file.AsSpan(8, 16));
        }

        /// <summary>NUL-terminated text: printable ASCII kept, auto-translate phrases (0xFD .. 0xFD) and other bytes dropped.</summary>
        private static string DecodeText(ReadOnlySpan<byte> field, ref int droppedPhrases)
        {
            Span<char> chars = stackalloc char[field.Length];
            int n = 0;
            for (int i = 0; i < field.Length; i++)
            {
                byte b = field[i];
                if (b == 0) break;
                if (b == 0xFD)
                {
                    // An auto-translate phrase: 0xFD, four bytes (type, language, group, id), 0xFD.
                    droppedPhrases++;
                    i += 5;
                    continue;
                }
                if (b is >= 0x20 and < 0x7F) chars[n++] = (char)b;
                else if (b >= 0x81 && i + 1 < field.Length && b is <= 0x9F or >= 0xE0) i++; // a Shift-JIS double-byte character
            }
            return new string(chars[..n]);
        }

        private static void EncodeText(string text, Span<byte> field)
        {
            field.Clear();
            int max = field.Length - 1;
            int n = 0;
            foreach (char c in text)
            {
                if (n >= max) break;
                if (c is >= ' ' and < (char)0x7F) field[n++] = (byte)c;
            }
        }
    }
}
