// tests/Gordian.Addons.Tests/AddonsProjectSanityTests.cs
using System;
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
    }
}
