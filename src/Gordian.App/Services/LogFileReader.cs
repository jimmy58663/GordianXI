using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Gordian.App.Services
{
    /// <summary>
    /// Reads log files for the Logs tab while GordianLog may still be writing them (opened shared). A file is read from
    /// its tail when it is larger than the cap, so an old 200 MB pre-#564 log opens quickly, and a growing file is followed
    /// by reading only what was appended since the last read (#566).
    /// </summary>
    public sealed class LogFileReader
    {
        /// <summary>The most a file view loads: the last 32 MB.</summary>
        public const long DefaultMaxBytes = 32L * 1024 * 1024;

        private readonly string _path;
        private readonly long _maxBytes;
        private long _offset;
        private string _partial = string.Empty;

        public LogFileReader(string path, long maxBytes = DefaultMaxBytes)
        {
            _path = path ?? throw new ArgumentNullException(nameof(path));
            _maxBytes = Math.Max(1024, maxBytes);
        }

        /// <summary>The file this reader follows.</summary>
        public string Path => _path;

        /// <summary>Bytes skipped at the start because the file was larger than the cap (0 when it was read whole).</summary>
        public long SkippedBytes { get; private set; }

        /// <summary>The file's length at the last read.</summary>
        public long Length => _offset;

        /// <summary>
        /// Reads the file from the start, or its last <c>maxBytes</c> starting at a line boundary. A last line without its
        /// newline yet is held back until it is finished.
        /// </summary>
        public List<string> ReadInitial()
        {
            _offset = 0;
            _partial = string.Empty;
            SkippedBytes = 0;
            if (!File.Exists(_path)) return new List<string>();

            using var stream = Open();
            long length = stream.Length;
            if (length > _maxBytes)
            {
                SkippedBytes = length - _maxBytes;
                stream.Seek(SkippedBytes, SeekOrigin.Begin);
            }
            _offset = stream.Position;
            var lines = ReadFrom(stream);
            // Starting mid-file lands mid-line: drop that first fragment.
            if (SkippedBytes > 0 && lines.Count > 0) lines.RemoveAt(0);
            return lines;
        }

        /// <summary>
        /// Reads the lines appended since the last read. A file that shrank (deleted and started again) is read from the
        /// start again; <paramref name="restarted"/> says so, and the caller should replace what it shows.
        /// </summary>
        public List<string> ReadAppended(out bool restarted)
        {
            restarted = false;
            if (!File.Exists(_path)) return new List<string>();
            using var stream = Open();
            if (stream.Length < _offset)
            {
                restarted = true;
                _offset = 0;
                _partial = string.Empty;
            }
            if (stream.Length == _offset) return new List<string>();
            stream.Seek(_offset, SeekOrigin.Begin);
            return ReadFrom(stream);
        }

        private FileStream Open() =>
            new(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

        private List<string> ReadFrom(FileStream stream)
        {
            var lines = new List<string>();
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 64 * 1024, leaveOpen: true);
            string text = _partial + reader.ReadToEnd();
            _offset = stream.Position;

            int start = 0;
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] != '\n') continue;
                int end = i > start && text[i - 1] == '\r' ? i - 1 : i;
                lines.Add(text.Substring(start, end - start));
                start = i + 1;
            }
            _partial = start < text.Length ? text.Substring(start) : string.Empty;
            return lines;
        }
    }
}
