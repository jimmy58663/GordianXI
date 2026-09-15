using System;
using System.Diagnostics;
using System.IO;
using System.Runtime;
using Avalonia;

namespace Gordian.App
{

    internal class Program
    {
        // Initialization code. Don't use any Avalonia, third-party APIs or any
        // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
        // yet and stuff might break.
        [STAThread]
        public static void Main(string[] args)
        {
            // ⚡ Optimize GC for sustained low latency (suppresses blocking Gen 2 collections during rendering & packet streaming)
            GCSettings.LatencyMode = GCLatencyMode.SustainedLowLatency;

            // 📝 Initialize the Platform-Agnostic File Logger
            InitializeSegmentedLogFiles();

            Debug.WriteLine($"[GordianXI Boot] GC Profile: ServerGC={GCSettings.IsServerGC}, LatencyMode={GCSettings.LatencyMode}");

            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }

            private static void InitializeSegmentedLogFiles()
        {
            try
            {
                string logDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
                if (!Directory.Exists(logDirectory))
                {
                    Directory.CreateDirectory(logDirectory);
                }

                string dateStamp = DateTime.Now.ToString("yyyyMMdd");
                string netToken = "[NET_TRACE]";

                // 📁 File Target A: The General System / Application Log
                string systemLogPath = Path.Combine(logDirectory, $"gordian_system_{dateStamp}.log");
                TextWriterTraceListener systemListener = new TextWriterTraceListener(systemLogPath)
                {
                    Name = "SystemLogListener",
                    // ✅ RULE: Hide anything starting with [NET_TRACE] from this file
                    Filter = new PrefixTraceFilter(netToken, rejectIfMatch: true)
                };

                // 📁 File Target B: The Isolated Network Packet Log
                string networkLogPath = Path.Combine(logDirectory, $"gordian_network_{dateStamp}.log");
                TextWriterTraceListener networkListener = new TextWriterTraceListener(networkLogPath)
                {
                    Name = "NetworkLogListener",
                    // ✅ RULE: ONLY allow strings starting with [NET_TRACE] into this file
                    Filter = new PrefixTraceFilter(netToken, rejectIfMatch: false)
                };

                // Add diagnostic trace pipes to the process ledger collection
                Trace.Listeners.Add(systemListener);

                // 🛑 Network trace disk logging is disabled by default to prevent high-frequency disk I/O.
                // Enable on demand via environment variable GORDIAN_LOG_NETWORK_DISK=1 or true.
                string? netDiskLogging = Environment.GetEnvironmentVariable("GORDIAN_LOG_NETWORK_DISK");
                if (string.Equals(netDiskLogging, "1", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(netDiskLogging, "true", StringComparison.OrdinalIgnoreCase))
                {
                    Trace.Listeners.Add(networkListener);
                    Debug.WriteLine("[GordianXI Boot] High-frequency network packet disk logging ENABLED via GORDIAN_LOG_NETWORK_DISK.");
                }
                else
                {
                    networkListener.Dispose();
                }

                Debug.AutoFlush = true;

                Debug.WriteLine("[GordianXI Boot] Global tracing infrastructure online.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to initialize segmented logging: {ex.Message}");
            }
        }

        // Avalonia configuration, don't remove; also used by visual designer.
        public static AppBuilder BuildAvaloniaApp()
            => AppBuilder.Configure<App>()
                .UsePlatformDetect()
    #if DEBUG
                .WithDeveloperTools()
    #endif
                .WithInterFont()
                .LogToTrace();
    }
}