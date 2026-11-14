// tests/Gordian.Core.Tests/Diagnostics/GordianLogTests.cs
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Gordian.Core.Diagnostics;
using Xunit;

namespace Gordian.Core.Tests.Diagnostics
{
    [CollectionDefinition("LogTests", DisableParallelization = true)]
    public class LogTestsCollection { }

    /// <summary>
    /// The logger end to end (#564): level gating before formatting, category routing to the system, network and status
    /// files, the character scope tag and full exceptions.
    /// </summary>
    [Collection("LogTests")]
    public sealed class GordianLogTests : IDisposable
    {
        private static readonly DateTime Launch = new(2026, 11, 14, 8, 39, 12);
        private readonly string _logs = Path.Combine(Path.GetTempPath(), $"gordian_logs_{Guid.NewGuid():N}");

        public void Dispose()
        {
            GordianLog.Shutdown();
            try { Directory.Delete(_logs, recursive: true); } catch (IOException) { }
        }

        private void Start(LoggingSettings? settings = null, bool debug = true, bool network = false) =>
            GordianLog.Initialize(new LogOptions
            {
                LogsDirectory = _logs,
                Settings = settings,
                DebugByDefault = debug,
                NetworkFileOverride = network,
                LaunchTime = Launch,
            });

        private string SystemLog() => ReadAll(GordianLog.SystemFolder);
        private string NetworkLog() => ReadAll(GordianLog.NetworkFolder);
        private string StatusLog() => ReadAll(GordianLog.StatusFolder);

        private string ReadAll(string folder)
        {
            Assert.True(GordianLog.Flush(TimeSpan.FromSeconds(5)));
            string path = Path.Combine(_logs, folder);
            if (!Directory.Exists(path)) return string.Empty;
            return string.Concat(Directory.GetFiles(path, "*.log").OrderBy(f => f).Select(ReadShared));
        }

        private static string ReadShared(string path)
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }

        [Fact]
        public void BeforeInitialize_NothingIsEnabledAndCallsAreHarmless()
        {
            Assert.False(GordianLog.IsInitialized);
            Assert.False(GordianLog.IsEnabled(LogLevel.Error, "TEST"));
            GordianLog.Info("TEST", "dropped");
            GordianLog.Status("dropped");
            Assert.True(GordianLog.Flush());
        }

        [Fact]
        public void Info_WritesFormattedLineToTheDailySystemLog()
        {
            Start();
            string token = Guid.NewGuid().ToString("N");

            GordianLog.Info("TEST_INFO", $"hello {token}");

            string content = SystemLog();
            Assert.Matches(@"^\[\d\d:\d\d:\d\d\.\d{3}\] \[INFO\] \[TEST_INFO\] hello " + token, content);
            string expectedName = RollingLogFile.FileName("gordian", DateTime.Now, 1);
            Assert.True(File.Exists(Path.Combine(_logs, GordianLog.SystemFolder, expectedName)));
        }

        [Fact]
        public void Debug_BelowTheDefaultLevel_IsNotFormatted()
        {
            Start(debug: false);
            var probe = new FormatProbe();

            GordianLog.Debug("TEST_DEBUG", $"value {probe}");
            GordianLog.Info("TEST_DEBUG", $"value {probe}");

            Assert.Equal(1, probe.Calls);
            Assert.DoesNotContain("[DEBUG]", SystemLog());
        }

        [Fact]
        public void CategoryLevel_FromSettings_OverridesTheDefault()
        {
            var settings = new LoggingSettings();
            settings.Categories["test_quiet"] = LogLevel.Warn;
            Start(settings);

            GordianLog.Info("TEST_QUIET", "info-line");
            GordianLog.Warn("TEST_QUIET", "warn-line");
            GordianLog.Info("TEST_OTHER", "other-line");

            string content = SystemLog();
            Assert.DoesNotContain("info-line", content);
            Assert.Contains("[WARN] [TEST_QUIET] warn-line", content);
            Assert.Contains("other-line", content);
        }

        [Fact]
        public void BuiltInDefaults_QuietLocomotionInfo()
        {
            Start();
            Assert.False(GordianLog.IsEnabled(LogLevel.Info, LogCategories.Locomotion));
            Assert.True(GordianLog.IsEnabled(LogLevel.Warn, LogCategories.Locomotion));
            Assert.False(GordianLog.IsEnabled(LogLevel.Debug, LogCategories.Graphics));
        }

        [Fact]
        public void NetworkDetail_WithTheNetworkFileOff_IsDropped()
        {
            Start(debug: true);

            Assert.False(GordianLog.IsEnabled(LogLevel.Debug, LogCategories.Net));
            GordianLog.Debug(LogCategories.Net, "packet-detail");
            GordianLog.Info(LogCategories.Net, "zone-milestone");

            Assert.DoesNotContain("packet-detail", SystemLog());
            Assert.Contains("[INFO] [NET] zone-milestone", SystemLog());
            Assert.False(Directory.Exists(Path.Combine(_logs, GordianLog.NetworkFolder)) && NetworkLog().Length > 0);
        }

        [Fact]
        public void NetworkDetail_WithTheNetworkFileOn_GoesOnlyToTheNetworkFile()
        {
            Start(debug: false, network: true);

            GordianLog.Debug(LogCategories.Parser, "packet-detail");
            GordianLog.Info(LogCategories.Net, "zone-milestone");
            GordianLog.Info("TEST_UI", "ui-line");

            string system = SystemLog();
            string network = NetworkLog();
            Assert.DoesNotContain("packet-detail", system);
            Assert.Contains("[DEBUG] [PARSER] packet-detail", network);
            Assert.Contains("zone-milestone", system);
            Assert.Contains("zone-milestone", network);
            Assert.DoesNotContain("ui-line", network);
        }

        [Fact]
        public void Status_GoesOnlyToThisLaunchsStatusFile()
        {
            Start();

            GordianLog.Status("Saved profile 'Main'.");
            GordianLog.Status("Saved profile 'Main'.");
            GordianLog.Status("   ");

            Assert.Equal(Path.Combine(_logs, GordianLog.StatusFolder, "status_2026-11-14_08-39-12.log"), GordianLog.StatusFilePath);
            string status = StatusLog();
            Assert.Equal(2, status.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
            Assert.Matches(@"^\[\d\d:\d\d:\d\d\] Saved profile 'Main'\.", status);
            Assert.DoesNotContain("Saved profile", SystemLog());
        }

        [Fact]
        public void Status_CarriesNoCharacterTag()
        {
            Start();

            using (GordianLog.BeginScope(new LogScope("Knot")))
            {
                GordianLog.Status("[Gordian] Session disconnected.");
            }

            string status = StatusLog();
            Assert.Matches(@"^\[\d\d:\d\d:\d\d\] \[Gordian\] Session disconnected\.", status);
            Assert.DoesNotContain("Knot", status);
        }

        [Fact]
        public void WireTrace_GoesToTheNetworkFileOnly()
        {
            Start(debug: true, network: true);

            GordianLog.Debug(LogCategories.Wire, "Map datagram in Seq=3");

            Assert.Contains("[DEBUG] [WIRE] Map datagram in Seq=3", NetworkLog());
            Assert.DoesNotContain("Map datagram", SystemLog());
        }

        [Fact]
        public void SecondLaunchInTheSameSecond_GetsItsOwnStatusFile()
        {
            Start();
            string first = GordianLog.StatusFilePath!;
            Start();

            Assert.NotEqual(first, GordianLog.StatusFilePath);
            Assert.EndsWith("status_2026-11-14_08-39-12_2.log", GordianLog.StatusFilePath);
        }

        [Fact]
        public void Scope_TagsLinesWithTheCharacterName()
        {
            Start();
            var scope = new LogScope();

            using (GordianLog.BeginScope(scope))
            {
                scope.Name = "Gemini";
                GordianLog.Info("TEST_SCOPE", "inside");
            }
            GordianLog.Info("TEST_SCOPE", "outside");

            string content = SystemLog();
            Assert.Contains("[INFO] [TEST_SCOPE] [Gemini] inside", content);
            Assert.Contains("[INFO] [TEST_SCOPE] outside", content);
        }

        [Fact]
        public async Task FlowScope_FollowsAnAsyncLoopAcrossAwaits()
        {
            Start();
            var scope = new LogScope("Claude");

            await Task.Run(async () =>
            {
                GordianLog.SetFlowScope(scope);
                await Task.Yield();
                GordianLog.Info("TEST_FLOW", "after-await");
            }, TestContext.Current.CancellationToken);
            GordianLog.Info("TEST_FLOW", "caller");

            string content = SystemLog();
            Assert.Contains("[TEST_FLOW] [Claude] after-await", content);
            Assert.Contains("[TEST_FLOW] caller", content);
        }

        [Fact]
        public void Error_KeepsTheFullException()
        {
            Start();
            Exception ex;
            try { throw new InvalidOperationException("Simulated socket fault"); }
            catch (InvalidOperationException caught) { ex = caught; }

            GordianLog.Error("TEST_ERR", "Login failed", ex);

            string content = SystemLog();
            Assert.Contains("[ERROR] [TEST_ERR] Login failed", content);
            Assert.Contains("System.InvalidOperationException: Simulated socket fault", content);
            Assert.Contains(nameof(Error_KeepsTheFullException), content); // the stack trace
        }

        [Fact]
        public void ApplySettings_ChangesLevelsImmediately()
        {
            Start(debug: false);
            Assert.False(GordianLog.IsEnabled(LogLevel.Debug, "TEST_LIVE"));

            var settings = new LoggingSettings();
            settings.Categories["TEST_LIVE"] = LogLevel.Debug;
            GordianLog.ApplySettings(settings, debugByDefault: false);

            Assert.True(GordianLog.IsEnabled(LogLevel.Debug, "TEST_LIVE"));
        }

        private sealed class FormatProbe
        {
            public int Calls { get; private set; }

            public override string ToString()
            {
                Calls++;
                return "probe";
            }
        }
    }
}
