// tests/Gordian.Core.Tests/World/VanaClockCollection.cs
using System;
using Gordian.Core.World;
using Xunit;

namespace Gordian.Core.Tests.World
{
    /// <summary>
    /// Tests that change <see cref="VanaTime"/>'s process-wide server clock offset
    /// (<see cref="VanaTime.SynchronizeServerTime"/>, directly or through an S2C 0x00A login ack with a
    /// non-zero game time, or <see cref="VanaTime.ResetClockOffset"/>). The collection runs on its own,
    /// after the parallel tests, so no other test sees the offset change mid-test (#268). Each test
    /// class in it derives from <see cref="VanaClockTestBase"/>, which starts and ends every test at offset 0.
    /// </summary>
    [CollectionDefinition(Name, DisableParallelization = true)]
    public sealed class VanaClockCollection
    {
        public const string Name = "VanaClock";
    }

    /// <summary>Resets the server clock offset before and after each test in <see cref="VanaClockCollection"/>.</summary>
    public abstract class VanaClockTestBase : IDisposable
    {
        protected VanaClockTestBase() => VanaTime.ResetClockOffset();

        public void Dispose()
        {
            VanaTime.ResetClockOffset();
            GC.SuppressFinalize(this);
        }
    }
}
