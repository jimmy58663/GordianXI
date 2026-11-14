using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace Gordian.Core.Diagnostics
{
    /// <summary>One queued log line, or a flush barrier (<see cref="FlushSignal"/> set, everything else empty).</summary>
    internal readonly record struct LogRecord(DateTime Timestamp, LogLevel Level, string Category, string? Scope, string Message)
    {
        /// <summary>Completed by the writer once every record queued before it is on disk.</summary>
        public TaskCompletionSource? FlushSignal { get; init; }

        /// <summary>For a chat line: the server it goes under (<see cref="Category"/> holds the character, <see cref="Message"/> the formatted line).</summary>
        public string? ChatServer { get; init; }

        public static LogRecord Barrier(TaskCompletionSource signal) =>
            new(default, LogLevel.Off, string.Empty, null, string.Empty) { FlushSignal = signal };
    }

    /// <summary>
    /// Formats records and routes them to the system, network and status files. Runs only on the
    /// <see cref="GordianLog"/> writer thread.
    /// </summary>
    internal sealed class LogWriter : IDisposable
    {
        private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        private readonly RollingLogFile _system;
        private readonly RollingLogFile _network;
        private readonly StreamWriter _status;
        private readonly ChatLogFiles _chat;
        private readonly bool _console;
        private readonly StringBuilder _line = new(256);

        public LogWriter(string logsDirectory, long maxFileBytes, DateTime launch, bool mirrorToConsole)
        {
            _system = new RollingLogFile(Path.Combine(logsDirectory, GordianLog.SystemFolder), "gordian", maxFileBytes);
            _network = new RollingLogFile(Path.Combine(logsDirectory, GordianLog.NetworkFolder), "network", maxFileBytes);
            _chat = new ChatLogFiles(logsDirectory);
            _console = mirrorToConsole;

            string statusDirectory = Path.Combine(logsDirectory, GordianLog.StatusFolder);
            Directory.CreateDirectory(statusDirectory);
            StatusPath = UniqueStatusPath(statusDirectory, launch);
            var stream = new FileStream(StatusPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
            _status = new StreamWriter(stream, Utf8NoBom) { AutoFlush = false };
        }

        /// <summary>This launch's status file.</summary>
        public string StatusPath { get; }

        /// <summary>The file name of the status log for a launch at <paramref name="launch"/>.</summary>
        public static string StatusFileName(DateTime launch) =>
            $"status_{launch.ToString("yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture)}.log";

        public void Write(in LogRecord record)
        {
            if (record.ChatServer is { } server)
            {
                _chat.WriteLine(server, record.Category, record.Timestamp, record.Message);
                return;
            }

            if (record.Level == LogLevel.Status)
            {
                string statusLine = FormatStatus(record);
                _status.WriteLine(statusLine);
                if (_console) Mirror(statusLine);
                return;
            }

            string line = Format(record);
            bool network = LogCategories.IsNetwork(record.Category);
            // Packet detail goes only to the network file; network milestones (Info and up) go to the system log, and
            // also to the network file when it is on, so it reads on its own.
            if (!network || record.Level >= LogLevel.Info) _system.WriteLine(record.Timestamp, line);
            if (network && (record.Level < LogLevel.Info || GordianLog.NetworkFileEnabled)) _network.WriteLine(record.Timestamp, line);
            if (_console) Mirror(line);
        }

        public void Flush()
        {
            _system.Flush();
            _network.Flush();
            _status.Flush();
            _chat.Flush();
        }

        public void Dispose()
        {
            try { Flush(); } catch (IOException) { }
            _system.Dispose();
            _network.Dispose();
            _status.Dispose();
            _chat.Dispose();
        }

        private string Format(in LogRecord record)
        {
            _line.Clear();
            _line.Append('[').Append(record.Timestamp.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture)).Append("] [")
                 .Append(LevelName(record.Level)).Append("] [").Append(record.Category).Append(']');
            if (record.Scope is not null) _line.Append(" [").Append(record.Scope).Append(']');
            _line.Append(' ').Append(record.Message);
            return _line.ToString();
        }

        private string FormatStatus(in LogRecord record)
        {
            _line.Clear();
            _line.Append('[').Append(record.Timestamp.ToString("HH:mm:ss", CultureInfo.InvariantCulture)).Append(']');
            if (record.Scope is not null) _line.Append(" [").Append(record.Scope).Append(']');
            _line.Append(' ').Append(record.Message);
            return _line.ToString();
        }

        private static string LevelName(LogLevel level) => level switch
        {
            LogLevel.Trace => "TRACE",
            LogLevel.Debug => "DEBUG",
            LogLevel.Info => "INFO",
            LogLevel.Warn => "WARN",
            LogLevel.Error => "ERROR",
            LogLevel.Status => "STATUS",
            _ => level.ToString().ToUpperInvariant(),
        };

        private static void Mirror(string line)
        {
            try { Console.WriteLine(line); } catch (IOException) { }
        }

        private static string UniqueStatusPath(string directory, DateTime launch)
        {
            string path = Path.Combine(directory, StatusFileName(launch));
            for (int i = 2; File.Exists(path); i++)
            {
                path = Path.Combine(directory, Path.GetFileNameWithoutExtension(StatusFileName(launch)) + "_" + i.ToString(CultureInfo.InvariantCulture) + ".log");
            }
            return path;
        }
    }
}
