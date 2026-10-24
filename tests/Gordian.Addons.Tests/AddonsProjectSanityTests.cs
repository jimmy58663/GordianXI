// tests/Gordian.Addons.Tests/AddonsProjectSanityTests.cs
using System;
using System.IO;
using Xunit;

namespace Gordian.Addons.Tests
{
    public sealed class AddonsProjectSanityTests
    {
        [Fact]
        public void AddonsProject_ReferencesAndInitializesCleanly()
        {
            // Verify that the Gordian.Addons assembly loads and is accessible
            var addonsAssembly = typeof(AddonsProjectSanityTests).Assembly;
            Assert.NotNull(addonsAssembly);

            // Verify Core dependency boundary: Addons has access to Gordian.Core types
            var sessionConfigType = typeof(Gordian.Core.Config.SessionProfile);
            Assert.NotNull(sessionConfigType);
        }

        [Fact]
        public void AddonsProject_DoesNotReferenceTheEngine()
        {
            // #433: Addons builds against Gordian.Core only. A project reference to Gordian.Engine (or to the App, which
            // brings it) would copy the engine assembly next to these tests, even before any code used it.
            Assert.False(File.Exists(Path.Combine(AppContext.BaseDirectory, "Gordian.Engine.dll")));
        }
    }
}
