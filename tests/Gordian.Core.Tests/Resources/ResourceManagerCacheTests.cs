// tests/Gordian.Core.Tests/Resources/ResourceManagerCacheTests.cs
using System.IO;
using Gordian.Core.Resources;
using Xunit;

namespace Gordian.Core.Tests.Resources
{
    public class ResourceManagerCacheTests
    {
        [Fact]
        public void ClearCache_AdvancesCacheGeneration()
        {
            var rm = new ResourceManager(Path.GetTempPath());
            int before = rm.CacheGeneration;

            rm.ClearCache();
            int afterOne = rm.CacheGeneration;
            rm.ClearCache();

            Assert.NotEqual(before, afterOne);
            Assert.NotEqual(afterOne, rm.CacheGeneration);
        }
    }
}
