using System;
using System.Diagnostics;
using System.IO;
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
                // 📝 Initialize the Platform-Agnostic File Logger
                InitializeSegmentedLogFiles();

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

                // Add both diagnostic trace pipes to the process ledger collection
                Trace.Listeners.Add(systemListener);
                Trace.Listeners.Add(networkListener);

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