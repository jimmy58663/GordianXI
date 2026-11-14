using System;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Gordian.Core.Diagnostics
{
    /// <summary>
    /// GordianXI's logger. Call sites pass a category (<see cref="LogCategories"/>) and a message; each category has a
    /// minimum level (<see cref="LoggingSettings"/>), checked before an interpolated message is formatted, so a disabled
    /// line costs one lookup and no allocation. Records go onto a bounded queue and one background thread writes them,
    /// so no game or packet thread waits on disk. Folders, under the logs directory given to <see cref="Initialize"/>:
    /// <list type="bullet">
    /// <item><c>system/gordian_yyyy-MM-dd.log</c>: everything except status messages and packet detail.</item>
    /// <item><c>network/network_yyyy-MM-dd.log</c>: network-category detail (<see cref="LogCategories.Network"/>), only when the network file is on.</item>
    /// <item><c>status/status_yyyy-MM-dd_HH-mm-ss.log</c>: one file per launch with the messages the user was shown (<see cref="Status"/>).</item>
    /// </list>
    /// System and network files roll at a size cap; old ones are deleted at startup (<see cref="LogRetention"/>).
    /// Until <see cref="Initialize"/> runs (tests, headless tools) nothing is logged. See <c>docs/app/logging.md</c>.
    /// </summary>
    public static class GordianLog
    {
        /// <summary>Sub-folder of the logs directory for the system log.</summary>
        public const string SystemFolder = "system";

        /// <summary>Sub-folder of the logs directory for the opt-in packet log.</summary>
        public const string NetworkFolder = "network";

        /// <summary>Sub-folder of the logs directory for the per-launch status logs.</summary>
        public const string StatusFolder = "status";

        /// <summary>Sub-folder of the logs directory for the per-character chat logs (#565); never swept by retention.</summary>
        public const string ChatFolder = "chat";

        private const int QueueCapacity = 65_536;

        private static readonly object StateLock = new();
        private static readonly AsyncLocal<LogScope?> FlowScope = new();

        [ThreadStatic] private static LogScope? t_scope;

        private static volatile LogFilter? s_filter;
        private static Channel<LogRecord>? s_channel;
        private static Task? s_writerTask;
        private static LogWriter? s_writer;
        private static long s_dropped;

        /// <summary>Whether <see cref="Initialize"/> has run and records are being written.</summary>
        public static bool IsInitialized => s_filter is not null;

        /// <summary>Whether packet detail is being written to <c>logs/network/</c>.</summary>
        public static bool NetworkFileEnabled => s_filter?.NetworkFile == true;

        /// <summary>The logs directory given to <see cref="Initialize"/>, or null before it.</summary>
        public static string? LogsDirectory { get; private set; }

        /// <summary>This launch's status file, or null before <see cref="Initialize"/>.</summary>
        public static string? StatusFilePath { get; private set; }

        /// <summary>
        /// Starts logging into <paramref name="options"/>' logs directory: builds the filter, opens this launch's status
        /// file, starts the writer thread and sweeps expired logs on it. Calling it again restarts logging with the new
        /// options.
        /// </summary>
        public static void Initialize(LogOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);
            ArgumentException.ThrowIfNullOrWhiteSpace(options.LogsDirectory);

            lock (StateLock)
            {
                ShutdownLocked();

                LogFilter filter = LogFilter.Build(options.Settings ?? new LoggingSettings(), options.DebugByDefault, options.NetworkFileOverride);
                DateTime launch = options.LaunchTime ?? DateTime.Now;
                var writer = new LogWriter(options.LogsDirectory, filter.MaxFileBytes, launch, options.MirrorToConsole);
                var channel = Channel.CreateBounded<LogRecord>(new BoundedChannelOptions(QueueCapacity)
                {
                    SingleReader = true,
                    SingleWriter = false,
                    FullMode = BoundedChannelFullMode.Wait,
                });

                LogsDirectory = options.LogsDirectory;
                StatusFilePath = writer.StatusPath;
                s_writer = writer;
                s_channel = channel;
                Interlocked.Exchange(ref s_dropped, 0);
                s_writerTask = Task.Factory.StartNew(
                    () => WriterLoop(channel.Reader, writer, options.LogsDirectory, filter.RetentionDays, launch),
                    CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
                s_filter = filter;
            }
        }

        /// <summary>Applies new settings to the running logger (levels, network file); the change is immediate.</summary>
        public static void ApplySettings(LoggingSettings settings, bool debugByDefault, bool networkFileOverride = false)
        {
            lock (StateLock)
            {
                if (s_filter is null) return;
                s_filter = LogFilter.Build(settings, debugByDefault, networkFileOverride);
            }
        }

        /// <summary>Waits until every record logged so far is on disk, or <paramref name="timeout"/> passes.</summary>
        public static bool Flush(TimeSpan? timeout = null)
        {
            Channel<LogRecord>? channel = s_channel;
            if (channel is null) return true;
            var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            // A barrier must not be dropped like a record, so wait for room rather than TryWrite.
            if (!channel.Writer.WriteAsync(LogRecord.Barrier(done)).AsTask().Wait(timeout ?? TimeSpan.FromSeconds(5))) return false;
            return done.Task.Wait(timeout ?? TimeSpan.FromSeconds(5));
        }

        /// <summary>Flushes and stops the writer; until the next <see cref="Initialize"/> nothing is logged.</summary>
        public static void Shutdown()
        {
            lock (StateLock) ShutdownLocked();
        }

        private static void ShutdownLocked()
        {
            if (s_channel is null) return;
            s_filter = null;
            s_channel.Writer.TryComplete();
            try { s_writerTask?.Wait(TimeSpan.FromSeconds(5)); } catch (AggregateException) { }
            s_writer?.Dispose();
            s_channel = null;
            s_writerTask = null;
            s_writer = null;
            LogsDirectory = null;
            StatusFilePath = null;
        }

        /// <summary>Whether a record of <paramref name="level"/> in <paramref name="category"/> would be written.</summary>
        public static bool IsEnabled(LogLevel level, string category) =>
            s_filter is { } filter && filter.IsEnabled(level, category);

        /// <summary>
        /// Tags lines logged on this thread with <paramref name="scope"/> until the returned handle is disposed. For
        /// synchronous work only: the tag does not follow an <c>await</c>. Use <see cref="SetFlowScope"/> for async loops.
        /// </summary>
        public static ScopeHandle BeginScope(LogScope? scope)
        {
            LogScope? previous = t_scope;
            t_scope = scope;
            return new ScopeHandle(previous);
        }

        /// <summary>
        /// Tags lines logged by the calling async flow (and the work it starts) with <paramref name="scope"/>. Set it once
        /// at the top of a long-running loop; it is undone when that async method returns.
        /// </summary>
        public static void SetFlowScope(LogScope? scope) => FlowScope.Value = scope;

        /// <summary>The scope tag that lines logged here now would carry.</summary>
        public static string? CurrentScopeName => (t_scope ?? FlowScope.Value)?.Name;

        /// <summary>Logs very high-frequency detail.</summary>
        public static void Trace(string category, string message) => Write(LogLevel.Trace, category, message);

        /// <summary>Logs very high-frequency detail; the message is formatted only if the category is at Trace.</summary>
        public static void Trace(string category, [InterpolatedStringHandlerArgument("category")] ref TraceLogHandler message)
        {
            if (message.Enabled) Enqueue(LogLevel.Trace, category, message.ToStringAndClear());
        }

        /// <summary>Logs developer detail.</summary>
        public static void Debug(string category, string message) => Write(LogLevel.Debug, category, message);

        /// <summary>Logs developer detail; the message is formatted only if the category is at Debug.</summary>
        public static void Debug(string category, [InterpolatedStringHandlerArgument("category")] ref DebugLogHandler message)
        {
            if (message.Enabled) Enqueue(LogLevel.Debug, category, message.ToStringAndClear());
        }

        /// <summary>Logs a normal milestone.</summary>
        public static void Info(string category, string message) => Write(LogLevel.Info, category, message);

        /// <summary>Logs a normal milestone; the message is formatted only if the category is at Info.</summary>
        public static void Info(string category, [InterpolatedStringHandlerArgument("category")] ref InfoLogHandler message)
        {
            if (message.Enabled) Enqueue(LogLevel.Info, category, message.ToStringAndClear());
        }

        /// <summary>Logs something unexpected that was recovered from.</summary>
        public static void Warn(string category, string message) => Write(LogLevel.Warn, category, message);

        /// <summary>Logs something unexpected; the message is formatted only if the category is at Warn.</summary>
        public static void Warn(string category, [InterpolatedStringHandlerArgument("category")] ref WarnLogHandler message)
        {
            if (message.Enabled) Enqueue(LogLevel.Warn, category, message.ToStringAndClear());
        }

        /// <summary>Logs a failure, with the full exception (type, message and stack trace) when there is one.</summary>
        public static void Error(string category, string message, Exception? ex = null)
        {
            if (!IsEnabled(LogLevel.Error, category)) return;
            Enqueue(LogLevel.Error, category, ex is null ? message : message + Environment.NewLine + ex);
        }

        /// <summary>
        /// Records a message the user was shown (the Profiles and Launch status box) in this launch's status file. A
        /// failure the user must act on also deserves a <see cref="Warn"/> or <see cref="Error"/> with the technical
        /// detail, since the system log does not repeat status messages.
        /// </summary>
        public static void Status(string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return;
            Write(LogLevel.Status, LogCategories.Status, message);
        }

        private static void Write(LogLevel level, string category, string message)
        {
            if (!IsEnabled(level, category)) return;
            Enqueue(level, category, message);
        }

        private static void Enqueue(LogLevel level, string category, string message)
        {
            Channel<LogRecord>? channel = s_channel;
            if (channel is null) return;
            var record = new LogRecord(DateTime.Now, level, category, CurrentScopeName, message);
            if (!channel.Writer.TryWrite(record)) Interlocked.Increment(ref s_dropped);
        }

        private static async Task WriterLoop(ChannelReader<LogRecord> reader, LogWriter writer, string logsDirectory, int retentionDays, DateTime now)
        {
            try
            {
                int swept = LogRetention.Sweep(logsDirectory, retentionDays, now);
                if (swept > 0)
                {
                    writer.Write(new LogRecord(DateTime.Now, LogLevel.Info, LogCategories.Logging, null,
                        $"Deleted {swept} log file(s) older than {retentionDays} days."));
                }
            }
            catch (Exception ex)
            {
                writer.Write(new LogRecord(DateTime.Now, LogLevel.Warn, LogCategories.Logging, null, "Log retention sweep failed: " + ex.Message));
            }

            while (await reader.WaitToReadAsync().ConfigureAwait(false))
            {
                while (reader.TryRead(out LogRecord record))
                {
                    if (record.FlushSignal is { } signal)
                    {
                        writer.Flush();
                        signal.TrySetResult();
                        continue;
                    }

                    try
                    {
                        writer.Write(record);
                    }
                    catch (Exception)
                    {
                        // A full disk or a removed folder must not take the client down; the line is lost.
                    }
                }

                long dropped = Interlocked.Exchange(ref s_dropped, 0);
                if (dropped > 0)
                {
                    try
                    {
                        writer.Write(new LogRecord(DateTime.Now, LogLevel.Warn, LogCategories.Logging, null,
                            $"{dropped.ToString(CultureInfo.InvariantCulture)} log record(s) dropped: the log queue was full."));
                    }
                    catch (Exception) { }
                }

                try { writer.Flush(); } catch (Exception) { }
            }

            try { writer.Flush(); } catch (Exception) { }
        }

        /// <summary>Restores the previous thread scope when disposed; returned by <see cref="BeginScope"/>.</summary>
        public readonly struct ScopeHandle : IDisposable
        {
            private readonly LogScope? _previous;

            internal ScopeHandle(LogScope? previous) => _previous = previous;

            /// <summary>Restores the scope that was active before <see cref="BeginScope"/>.</summary>
            public void Dispose() => t_scope = _previous;
        }
    }

    /// <summary>Startup options for <see cref="GordianLog.Initialize"/>.</summary>
    public sealed class LogOptions
    {
        /// <summary>The logs root (normally <c>GordianStorage.LogsDirectory</c>); the system, network and status folders go under it.</summary>
        public required string LogsDirectory { get; init; }

        /// <summary>The user's settings (<see cref="LoggingSettings.Load"/>); null uses the defaults.</summary>
        public LoggingSettings? Settings { get; init; }

        /// <summary>Whether the default level is Debug (Debug builds, <c>GORDIAN_DEBUG=1</c>) rather than Info.</summary>
        public bool DebugByDefault { get; init; }

        /// <summary>Forces the network file on (<c>GORDIAN_LOG_NETWORK_DISK=1</c>) whatever the settings say.</summary>
        public bool NetworkFileOverride { get; init; }

        /// <summary>Also writes every line to standard output (<c>GORDIAN_LOG_CONSOLE=1</c>, or a console tool).</summary>
        public bool MirrorToConsole { get; init; }

        /// <summary>The launch time naming this launch's status file; null uses now.</summary>
        public DateTime? LaunchTime { get; init; }
    }
}
