// tests/Gordian.Core.Tests/CoreProjectSanityTests.cs
using System;
using System.IO;
using Xunit;

namespace Gordian.Core.Tests
{
    public sealed class CoreProjectSanityTests
    {
        [Fact]
        public void CoreTests_DoNotReferenceTheEngine()
        {
            // #433: these tests cover Gordian.Core on its own; anything that needs the engine belongs in Gordian.Engine.Tests.
            // A project reference to Gordian.Engine would copy the engine assembly next to these tests.
            Assert.False(File.Exists(Path.Combine(AppContext.BaseDirectory, "Gordian.Engine.dll")));
        }
    }
}
