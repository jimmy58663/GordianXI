// tests/Gordian.Core.Tests/Diagnostics/GordianLogTests.cs
using System;
using System.IO;
using Gordian.Core.Diagnostics;
using Xunit;

namespace Gordian.Core.Tests.Diagnostics
{
    public sealed class GordianLogTests : IDisposable
    {
        private readonly string _tempLogFile;

        public GordianLogTests()
        {
            _tempLogFile = Path.Combine(Path.GetTempPath(), $"gordian_log_test_{Guid.NewGuid():N}.log");
            GordianLog.LogFilePath = _tempLogFile;
            GordianLog.EnableDebugLogging = true;
        }

        public void Dispose()
        {
            if (File.Exists(_tempLogFile))
            {
                try { File.Delete(_tempLogFile); } catch { }
            }
        }

        [Fact]
        public void DebugLogging_WhenEnabled_WritesToFile()
        {
            GordianLog.EnableDebugLogging = true;
            string testMessage = $"Debug verification token {Guid.NewGuid()}";

            GordianLog.Debug("TEST_NET", testMessage);

            Assert.True(File.Exists(_tempLogFile));
            string content = File.ReadAllText(_tempLogFile);
            Assert.Contains("[DEBUG] [TEST_NET]", content);
            Assert.Contains(testMessage, content);
        }

        [Fact]
        public void DebugLogging_WhenDisabled_SuppressesDebugMessages()
        {
            GordianLog.EnableDebugLogging = false;
            string testMessage = $"Suppressed debug message {Guid.NewGuid()}";

            GordianLog.Debug("TEST_NET", testMessage);

            if (File.Exists(_tempLogFile))
            {
                string content = File.ReadAllText(_tempLogFile);
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
            string content = File.ReadAllText(_tempLogFile);
            Assert.Contains("[INFO] [TEST_INFO]", content);
            Assert.Contains(infoMsg, content);
            Assert.Contains("[WARN] [TEST_WARN]", content);
            Assert.Contains(warnMsg, content);
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
