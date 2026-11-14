// tests/Gordian.Core.Tests/Diagnostics/LogFilesTests.cs
using System;
using System.IO;
using Gordian.Core.Diagnostics;
using Xunit;

namespace Gordian.Core.Tests.Diagnostics
{
    /// <summary>Rolling files, retention and the settings file (#564).</summary>
    public sealed class LogFilesTests : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), $"gordian_logfiles_{Guid.NewGuid():N}");

        public LogFilesTests() => Directory.CreateDirectory(_root);

        public void Dispose()
        {
            try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
        }

        [Fact]
        public void RollingFile_RollsToTheNextNumberAtTheSizeCap()
        {
            var day = new DateTime(2026, 11, 14, 10, 0, 0);
            using (var file = new RollingLogFile(_root, "gordian", maxBytes: 1024))
            {
                string line = new('x', 300);
                for (int i = 0; i < 8; i++) file.WriteLine(day, line);
            }

            Assert.True(File.Exists(Path.Combine(_root, "gordian_2026-11-14.log")));
            Assert.True(File.Exists(Path.Combine(_root, "gordian_2026-11-14_2.log")));
            Assert.True(new FileInfo(Path.Combine(_root, "gordian_2026-11-14.log")).Length <= 1024);
        }

        [Fact]
        public void RollingFile_NamesFilesByTheDateALineIsWritten()
        {
            using (var file = new RollingLogFile(_root, "gordian", maxBytes: 1 << 20))
            {
                file.WriteLine(new DateTime(2026, 11, 14, 23, 59, 59), "before midnight");
                file.WriteLine(new DateTime(2026, 11, 15, 0, 0, 1), "after midnight");
            }

            Assert.Contains("before midnight", File.ReadAllText(Path.Combine(_root, "gordian_2026-11-14.log")));
            Assert.Contains("after midnight", File.ReadAllText(Path.Combine(_root, "gordian_2026-11-15.log")));
        }

        [Fact]
        public void RollingFile_ASecondLaunchContinuesTheNewestFileOfTheDay()
        {
            var day = new DateTime(2026, 11, 14, 10, 0, 0);
            File.WriteAllText(Path.Combine(_root, "gordian_2026-11-14.log"), "old\n");
            File.WriteAllText(Path.Combine(_root, "gordian_2026-11-14_2.log"), "older run\n");

            using (var file = new RollingLogFile(_root, "gordian", maxBytes: 1 << 20))
            {
                file.WriteLine(day, "new run");
            }

            Assert.Contains("new run", File.ReadAllText(Path.Combine(_root, "gordian_2026-11-14_2.log")));
            Assert.DoesNotContain("new run", File.ReadAllText(Path.Combine(_root, "gordian_2026-11-14.log")));
        }

        [Fact]
        public void Retention_DeletesOldDiagnosticsButNeverChat()
        {
            var now = new DateTime(2026, 11, 14, 12, 0, 0);
            string oldSystem = Touch(Path.Combine(GordianLog.SystemFolder, "gordian_2026-11-01.log"), now.AddDays(-13));
            string newSystem = Touch(Path.Combine(GordianLog.SystemFolder, "gordian_2026-11-12.log"), now.AddDays(-2));
            string oldNetwork = Touch(Path.Combine(GordianLog.NetworkFolder, "network_2026-11-01.log"), now.AddDays(-13));
            string oldStatus = Touch(Path.Combine(GordianLog.StatusFolder, "status_2026-11-01_10-00-00.log"), now.AddDays(-13));
            string oldChat = Touch(Path.Combine(GordianLog.ChatFolder, "Server", "Gemini", "2025-01-01.log"), now.AddDays(-600));
            string legacy = Touch("gordian_system_20261101.log", now.AddDays(-13));
            string unrelated = Touch("notes.txt", now.AddDays(-600));

            int deleted = LogRetention.Sweep(_root, retentionDays: 7, now);

            Assert.Equal(3, deleted);
            Assert.False(File.Exists(oldSystem));
            Assert.False(File.Exists(oldNetwork));
            Assert.False(File.Exists(oldStatus));
            Assert.True(File.Exists(legacy)); // pre-#564 logs are kept as evidence; the user deletes them by hand
            Assert.True(File.Exists(newSystem));
            Assert.True(File.Exists(oldChat));
            Assert.True(File.Exists(unrelated));
        }

        [Fact]
        public void Settings_RoundTripWithCaseInsensitiveCategories()
        {
            string path = Path.Combine(_root, "nested", LoggingSettings.FileName);
            var settings = new LoggingSettings { DefaultLevel = LogLevel.Info, NetworkFile = true, RetentionDays = 3 };
            settings.Categories["LOCOMOTION"] = LogLevel.Debug;

            settings.Save(path);
            LoggingSettings loaded = LoggingSettings.Load(path);

            Assert.Equal(LogLevel.Info, loaded.DefaultLevel);
            Assert.True(loaded.NetworkFile);
            Assert.Equal(3, loaded.RetentionDays);
            Assert.Equal(LogLevel.Debug, loaded.Categories["locomotion"]);
            Assert.Contains("\"Debug\"", File.ReadAllText(path));
        }

        [Fact]
        public void Settings_MissingOrBrokenFileGivesDefaults()
        {
            Assert.Null(LoggingSettings.Load(Path.Combine(_root, "missing.json")).DefaultLevel);
            string broken = Path.Combine(_root, "broken.json");
            File.WriteAllText(broken, "{ not json");
            Assert.Empty(LoggingSettings.Load(broken).Categories);
        }

        [Fact]
        public void Filter_NetworkFileLowersNetworkCategoriesToDebugUnlessOverridden()
        {
            var settings = new LoggingSettings { NetworkFile = true };
            settings.Categories[LogCategories.Crypto] = LogLevel.Warn;

            LogFilter filter = LogFilter.Build(settings, debugDefault: false, networkFileEnv: false);

            Assert.True(filter.IsEnabled(LogLevel.Debug, LogCategories.Net));
            Assert.False(filter.IsEnabled(LogLevel.Info, LogCategories.Crypto));
            Assert.False(filter.IsEnabled(LogLevel.Debug, "TEST_UI"));
            Assert.True(filter.IsEnabled(LogLevel.Status, "ANY"));
        }

        private string Touch(string relative, DateTime lastWrite)
        {
            string path = Path.Combine(_root, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "line\n");
            File.SetLastWriteTime(path, lastWrite);
            return path;
        }
    }
}
