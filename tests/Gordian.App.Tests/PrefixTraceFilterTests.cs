// tests/Gordian.App.Tests/PrefixTraceFilterTests.cs
using System.Diagnostics;
using Gordian.App;
using Xunit;

namespace Gordian.App.Tests
{
    public sealed class PrefixTraceFilterTests
    {
        [Fact]
        public void NetworkFilter_AllowsOnlyMatchingPrefix()
        {
            // Arrange: Filter configured to ONLY allow "[NET_TRACE]" messages
            var networkFilter = new PrefixTraceFilter("[NET_TRACE]", rejectIfMatch: false);

            // Act & Assert
            Assert.True(networkFilter.ShouldTrace(null, "Source", TraceEventType.Information, 0, "[NET_TRACE] Packet 0x015 received", null, null, null));
            Assert.False(networkFilter.ShouldTrace(null, "Source", TraceEventType.Information, 0, "[SYSTEM] Application starting", null, null, null));
        }

        [Fact]
        public void SystemFilter_RejectsMatchingPrefix()
        {
            // Arrange: Filter configured to HIDE "[NET_TRACE]" messages (rejectIfMatch: true)
            var systemFilter = new PrefixTraceFilter("[NET_TRACE]", rejectIfMatch: true);

            // Act & Assert
            Assert.False(systemFilter.ShouldTrace(null, "Source", TraceEventType.Information, 0, "[NET_TRACE] Packet 0x015 received", null, null, null));
            Assert.True(systemFilter.ShouldTrace(null, "Source", TraceEventType.Information, 0, "[SYSTEM] Application starting", null, null, null));
        }

        [Fact]
        public void Filter_EmptyOrNullMessage_ReturnsTrue()
        {
            var filter = new PrefixTraceFilter("[NET_TRACE]", rejectIfMatch: false);

            Assert.True(filter.ShouldTrace(null, "Source", TraceEventType.Information, 0, null, null, null, null));
            Assert.True(filter.ShouldTrace(null, "Source", TraceEventType.Information, 0, string.Empty, null, null, null));
        }
    }
}
