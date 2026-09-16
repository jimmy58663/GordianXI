// tests/Gordian.Core.Tests/Diagnostics/GordianLogTests.cs
using System;
using System.IO;
using Gordian.Core.Diagnostics;
using Xunit;

namespace Gordian.Core.Tests.Diagnostics
{
    [CollectionDefinition("LogTests", DisableParallelization = true)]
    public class LogTestsCollection { }

    [Collection("LogTests")]
    public sealed class GordianLogTests : IDisposable
    {
        private static readonly object _testLock = new();
        private readonly string _tempLogFile;

        public GordianLogTests()
        {
            System.Threading.Monitor.Enter(_testLock);
            _tempLogFile = Path.Combine(Path.GetTempPath(), $"gordian_log_test_{Guid.NewGuid():N}.log");
            GordianLog.LogFilePath = _tempLogFile;
            GordianLog.EnableDebugLogging = true;
            GordianLog.EnableFileLogging = true;
        }

        public void Dispose()
        {
            try
            {
                GordianLog.LogFilePath = null!;
                GordianLog.EnableDebugLogging = true;
                GordianLog.EnableFileLogging = false;
                if (File.Exists(_tempLogFile))
                {
                    try { File.Delete(_tempLogFile); } catch { }
                }
            }
            finally
            {
                System.Threading.Monitor.Exit(_testLock);
            }
        }

        [Fact]
        public void DebugLogging_WhenEnabled_WritesToFile()
        {
            GordianLog.EnableDebugLogging = true;
            string testMessage = $"Debug verification token {Guid.NewGuid()}";

            GordianLog.Debug("TEST_NET", testMessage);

            Assert.True(File.Exists(_tempLogFile));
            string content = ReadLogFileSafely(_tempLogFile);
            Assert.Contains("[DEBUG] [TEST_NET]", content);
            Assert.Contains(testMessage, content);
        }

        [Fact]
        public void DebugLogging_WhenDisabled_SuppressesDebugMessages()
        {
            GordianLog.EnableDebugLogging = false;
            string testMessage = $"Suppressed token {Guid.NewGuid()}";

            GordianLog.Debug("TEST_NET", testMessage);

            if (File.Exists(_tempLogFile))
            {
                string content = ReadLogFileSafely(_tempLogFile);
                Assert.DoesNotContain(testMessage, content);
            }
        }

        [Fact]
        public void InfoAndWarning_AlwaysLogRegardlessOfDebugFlag()
        {
            GordianLog.EnableDebugLogging = false;
            string infoMsg = $"Info token {Guid.NewGuid()}";
            string warnMsg = $"Warn token {Guid.NewGuid()}";

            GordianLog.Info("TEST_INFO", infoMsg);
            GordianLog.Warning("TEST_WARN", warnMsg);

            Assert.True(File.Exists(_tempLogFile));
            string content = ReadLogFileSafely(_tempLogFile);
            Assert.Contains("[INFO] [TEST_INFO]", content);
            Assert.Contains(infoMsg, content);
            Assert.Contains("[WARN] [TEST_WARN]", content);
            Assert.Contains(warnMsg, content);
        }

        private static string ReadLogFileSafely(string path)
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }

        [Fact]
        public void Error_FormatsExceptionDetails()
        {
            var ex = new InvalidOperationException("Simulated socket fault");
            string errorMsg = $"Error event {Guid.NewGuid()}";

            GordianLog.Error("TEST_ERR", errorMsg, ex);

            Assert.True(File.Exists(_tempLogFile));
            string content = File.ReadAllText(_tempLogFile);
            Assert.Contains("[ERROR] [TEST_ERR]", content);
            Assert.Contains(errorMsg, content);
            Assert.Contains("InvalidOperationException", content);
            Assert.Contains("Simulated socket fault", content);
        }
    }
}
