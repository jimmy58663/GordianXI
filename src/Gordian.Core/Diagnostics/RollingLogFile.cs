using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace Gordian.Core.Diagnostics
{
    /// <summary>
    /// One family of daily log files, <c>&lt;prefix&gt;_yyyy-MM-dd.log</c> then <c>_2</c>, <c>_3</c>… once a file
    /// reaches the size cap. The date is the date a line is written (not the launch date), so a session past midnight
    /// moves to the next day's file. Files are opened shared so the Logs tab and editors can read them while they grow.
    /// Used only from the <see cref="GordianLog"/> writer thread.
    /// </summary>
    internal sealed class RollingLogFile : IDisposable
    {
        private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        private readonly string _directory;
        private readonly string _prefix;
        private readonly long _maxBytes;
        private StreamWriter? _writer;
        private DateTime _date;
        private int _index;
        private long _bytes;

        public RollingLogFile(string directory, string prefix, long maxBytes)
        {
            _directory = directory;
            _prefix = prefix;
            _maxBytes = Math.Max(1024, maxBytes);
        }

        /// <summary>The file being written, or null before the first line.</summary>
        public string? CurrentPath { get; private set; }

        /// <summary>The file name for <paramref name="date"/> and roll <paramref name="index"/> (1 = no suffix).</summary>
        public static string FileName(string prefix, DateTime date, int index) =>
            index <= 1
                ? $"{prefix}_{date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}.log"
                : $"{prefix}_{date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}_{index.ToString(CultureInfo.InvariantCulture)}.log";

        /// <summary>Appends one line, rolling to the next day's or the next numbered file first if needed.</summary>
        public void WriteLine(DateTime timestamp, string line)
        {
            long lineBytes = Utf8NoBom.GetByteCount(line) + Environment.NewLine.Length;
            if (_writer is null || timestamp.Date != _date)
            {
                Open(timestamp.Date);
            }
            else if (_bytes > 0 && _bytes + lineBytes > _maxBytes)
            {
                OpenIndex(_date, _index + 1);
            }

            _writer!.WriteLine(line);
            _bytes += lineBytes;
        }

        /// <summary>Pushes buffered lines to disk.</summary>
        public void Flush() => _writer?.Flush();

        public void Dispose()
        {
            _writer?.Dispose();
            _writer = null;
        }

        private void Open(DateTime date)
        {
            // Continue the newest file of the day (a second launch the same day appends to it), unless it is full.
            int index = 1;
            while (File.Exists(Path.Combine(_directory, FileName(_prefix, date, index + 1)))) index++;
            string path = Path.Combine(_directory, FileName(_prefix, date, index));
            if (File.Exists(path) && new FileInfo(path).Length >= _maxBytes) index++;
            OpenIndex(date, index);
        }

        private void OpenIndex(DateTime date, int index)
        {
            _writer?.Dispose();
            Directory.CreateDirectory(_directory);
            string path = Path.Combine(_directory, FileName(_prefix, date, index));
            var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
            _writer = new StreamWriter(stream, Utf8NoBom) { AutoFlush = false };
            _date = date;
            _index = index;
            _bytes = stream.Length;
            CurrentPath = path;
        }
    }
}
