using System;
using System.Runtime;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Logging;
using Gordian.App.Services;
using Gordian.Core.Config;
using Gordian.Core.Diagnostics;

namespace Gordian.App
{

    internal class Program
    {
        /// <summary>Whether logging started with Debug as its default level (Debug build or <c>GORDIAN_DEBUG=1</c>).</summary>
        internal static bool LogDebugByDefault { get; private set; }

        /// <summary>Whether <c>GORDIAN_LOG_NETWORK_DISK=1</c> forced the network file on.</summary>
        internal static bool LogNetworkFileOverride { get; private set; }

        // Initialization code. Don't use any Avalonia, third-party APIs or any
        // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
        // yet and stuff might break.
        [STAThread]
        public static void Main(string[] args)
        {
            // ⚡ Optimize GC for sustained low latency (suppresses blocking Gen 2 collections during rendering & packet streaming)
            GCSettings.LatencyMode = GCLatencyMode.SustainedLowLatency;

            InitializeLogging();
            GordianLog.Info(LogCategories.App, $"GordianXI starting. GC: ServerGC={GCSettings.IsServerGC}, LatencyMode={GCSettings.LatencyMode}. Logs: {GordianLog.LogsDirectory}");

            DevLaunchOptions.Current = DevLaunchOptions.Parse(args);
            if (DevLaunchOptions.Current.ProfilesDirectory is { } profilesDirectory)
            {
                GordianStorage.ProfilesDirectory = profilesDirectory;
            }

            try
            {
                BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
            }
            finally
            {
                GordianLog.Info(LogCategories.App, "GordianXI exiting.");
                GordianLog.Shutdown();
            }
        }

        /// <summary>
        /// Starts <see cref="GordianLog"/> in the user data logs folder (system, network and status files; see
        /// docs/app/logging.md) with the user's logging.json, and makes sure a crash reaches the log before the process ends.
        /// </summary>
        private static void InitializeLogging()
        {
            try
            {
#if DEBUG
                const bool debugBuild = true;
#else
                const bool debugBuild = false;
#endif
                LogDebugByDefault = debugBuild || IsSet("GORDIAN_DEBUG");
                LogNetworkFileOverride = IsSet("GORDIAN_LOG_NETWORK_DISK");
                GordianLog.Initialize(new LogOptions
                {
                    LogsDirectory = GordianStorage.LogsDirectory,
                    Settings = LoggingSettings.Load(LoggingSettings.DefaultPath),
                    DebugByDefault = LogDebugByDefault,
                    NetworkFileOverride = LogNetworkFileOverride,
                    MirrorToConsole = IsSet("GORDIAN_LOG_CONSOLE"),
                });

                AppDomain.CurrentDomain.UnhandledException += (_, e) =>
                {
                    GordianLog.Error(LogCategories.App, "Unhandled exception; the client is closing.", e.ExceptionObject as Exception);
                    GordianLog.Flush(TimeSpan.FromSeconds(2));
                };
                TaskScheduler.UnobservedTaskException += (_, e) =>
                    GordianLog.Error(LogCategories.App, "Unobserved task exception.", e.Exception);
                // Backstop for exits that skip Main's finally (a native library ending the process): flush what is queued.
                AppDomain.CurrentDomain.ProcessExit += (_, _) => GordianLog.Shutdown();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Failed to start logging: {ex}");
            }
        }

        private static bool IsSet(string variable) =>
            Environment.GetEnvironmentVariable(variable) is { } value
            && (value == "1" || value.Equals("true", StringComparison.OrdinalIgnoreCase));

        // Avalonia configuration, don't remove; also used by visual designer.
        public static AppBuilder BuildAvaloniaApp()
        {
            Logger.Sink = new AvaloniaLogSink(LogEventLevel.Warning);
            return AppBuilder.Configure<App>()
                .UsePlatformDetect()
    #if DEBUG
                .WithDeveloperTools()
    #endif
                .WithInterFont();
        }
    }
}
