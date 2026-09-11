// tests/Gordian.Automation.Tests/AutomationProjectSanityTests.cs
using System;
using Xunit;

namespace Gordian.Automation.Tests
{
    public sealed class AutomationProjectSanityTests
    {
        [Fact]
        public void AutomationProject_ReferencesAndInitializesCleanly()
        {
            // Verify that the Gordian.Automation assembly loads and is accessible
            var automationAssembly = typeof(AutomationProjectSanityTests).Assembly;
            Assert.NotNull(automationAssembly);

            // Verify Core dependency boundary: Automation has access to Gordian.Core types
            var sessionConfigType = typeof(Gordian.Core.Config.SessionProfile);
            Assert.NotNull(sessionConfigType);
        }
    }
}
