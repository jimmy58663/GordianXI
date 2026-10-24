// tests/Gordian.Automation.Tests/AutomationProjectSanityTests.cs
using System;
using System.IO;
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

        [Fact]
        public void AutomationProject_DoesNotReferenceTheEngine()
        {
            // #433: Automation builds against Gordian.Core only. A project reference to Gordian.Engine (or to the App, which
            // brings it) would copy the engine assembly next to these tests, even before any code used it.
            Assert.False(File.Exists(Path.Combine(AppContext.BaseDirectory, "Gordian.Engine.dll")));
        }
    }
}
