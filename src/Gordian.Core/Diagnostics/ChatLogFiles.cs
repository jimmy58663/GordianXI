using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Gordian.Core.Diagnostics
{
    /// <summary>
    /// The per-character chat log files, <c>chat/&lt;server&gt;/&lt;character&gt;/yyyy-MM-dd.log</c> under the logs
    /// directory (#565). One file per local day, named by the date a line is written, so play past midnight moves to the
    /// next day's file. Never rolled by size and never deleted by <see cref="LogRetention"/>. Used only from the
    /// <see cref="GordianLog"/> writer thread.
    /// </summary>
    internal sealed class ChatLogFiles : IDisposable
    {
        private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        private readonly string _logsDirectory;
        private readonly Dictionary<string, (DateTime Date, StreamWriter Writer)> _open = new(StringComparer.Ordinal);

        public ChatLogFiles(string logsDirectory) => _logsDirectory = logsDirectory;

        /// <summary>The file a line for <paramref name="server"/> / <paramref name="character"/> written at <paramref name="timestamp"/> goes to.</summary>
        public static string PathFor(string logsDirectory, string server, string character, DateTime timestamp) =>
            Path.Combine(logsDirectory, GordianLog.ChatFolder, FolderName(server), FolderName(character),
                timestamp.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".log");

        /// <summary>
        /// A server or character name made safe as one folder name on every platform: characters a file system rejects
        /// become <c>_</c>, casing is kept exactly, and an empty or dot-only name becomes <c>Unknown</c>.
        /// </summary>
        public static string FolderName(string? name)
        {
            string trimmed = (name ?? string.Empty).Trim();
            var builder = new StringBuilder(trimmed.Length);
            foreach (char c in trimmed)
            {
                bool invalid = c < 0x20 || c is '<' or '>' or ':' or '"' or '/' or '\\' or '|' or '?' or '*';
                builder.Append(invalid ? '_' : c);
            }
            string result = builder.ToString().TrimEnd('.', ' ');
            return result.Length == 0 || result.Trim('.', '_').Length == 0 ? "Unknown" : result;
        }

        public void WriteLine(string server, string character, DateTime timestamp, string line)
        {
            string key = FolderName(server) + "\u0001" + FolderName(character);
            if (!_open.TryGetValue(key, out var entry) || entry.Date != timestamp.Date)
            {
                entry.Writer?.Dispose();
                string path = PathFor(_logsDirectory, server, character, timestamp);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
                entry = (timestamp.Date, new StreamWriter(stream, Utf8NoBom) { AutoFlush = false });
                _open[key] = entry;
            }
            entry.Writer.WriteLine(line);
        }

        public void Flush()
        {
            foreach (var (_, writer) in _open.Values) writer.Flush();
        }

        public void Dispose()
        {
            foreach (var (_, writer) in _open.Values)
            {
                try { writer.Dispose(); } catch (IOException) { }
            }
            _open.Clear();
        }
    }
}
