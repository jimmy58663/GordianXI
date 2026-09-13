// src/Gordian.Core/Diagnostics/GordianLog.cs
using System;
using System.Diagnostics;
using System.IO;
using Gordian.Core.Config;

namespace Gordian.Core.Diagnostics
{
    /// <summary>
    /// Unified, thread-safe diagnostic logging system for GordianXI.
    /// Manages file logging to GordianStorage.LogsDirectory, console mirroring when attached,
    /// and routing to TraceListeners, all gated by a configurable debug flag.
    /// </summary>
    public static class GordianLog
    {
        private static readonly object _fileLock = new();
        private static string? _logFilePath;

        /// <summary>
        /// Controls whether verbose diagnostic and packet-level debug messages are emitted.
        /// Defaults to true in DEBUG builds or when the GORDIAN_DEBUG environment variable is '1' or 'true'.
        /// </summary>
        public static bool EnableDebugLogging { get; set; } =
#if DEBUG
            true;
#else
            Environment.GetEnvironmentVariable("GORDIAN_DEBUG") is "1" or "true";
#endif

        /// <summary>
        /// Absolute path to the persistent network debug log file.
        /// Defaults to Path.Combine(GordianStorage.LogsDirectory, "network_debug.log").
        /// </summary>
        public static string LogFilePath
        {
            get
            {
                if (string.IsNullOrWhiteSpace(_logFilePath))
                {
                    _logFilePath = Path.Combine(GordianStorage.LogsDirectory, "network_debug.log");
                }
                return _logFilePath;
            }
            set => _logFilePath = value;
        }

        /// <summary>
        /// Emits a debug-level message if EnableDebugLogging is true.
        /// </summary>
        public static void Debug(string category, string message)
        {
            if (!EnableDebugLogging) return;
            Write("DEBUG", category, message);
        }

        /// <summary>
        /// Emits an informational message.
        /// </summary>
        public static void Info(string category, string message)
        {
            Write("INFO", category, message);
        }

        /// <summary>
        /// Emits a warning message.
        /// </summary>
        public static void Warning(string category, string message)
        {
            Write("WARN", category, message);
        }

        /// <summary>
        /// Emits an error message with optional exception details.
        /// </summary>
        public static void Error(string category, string message, Exception? ex = null)
        {
            string full = ex != null ? $"{message} (Exception: {ex.GetType().Name}: {ex.Message})" : message;
            Write("ERROR", category, full);
        }

        private static void Write(string level, string category, string message)
        {
            string timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
            string formatted = $"[{timestamp}] [{level}] [{category}] {message}";

            // 1. Route to Trace infrastructure (captured by PrefixTraceFilter / TextWriterTraceListeners)
            Trace.WriteLine($"[NET_TRACE] {formatted}");

            // 2. Output to standard console
            try
            {
                Console.WriteLine(formatted);
            }
            catch
            {
                // Silently continue if standard output handle is invalid
            }

            // 3. Persist to network_debug.log file
            try
            {
                lock (_fileLock)
                {
                    string target = LogFilePath;
                    string? dir = Path.GetDirectoryName(target);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    {
                        Directory.CreateDirectory(dir);
                    }
                    File.AppendAllText(target, formatted + Environment.NewLine);
                }
            }
            catch
            {
                // Avoid crashing on log file write contention
            }
        }
    }
}
