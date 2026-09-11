// tests/Gordian.Core.Tests/Config/GordianStorageTests.cs
using System;
using System.IO;
using Gordian.Core.Config;
using Xunit;

namespace Gordian.Core.Tests.Config
{
    public class GordianStorageTests : IDisposable
    {
        private readonly string _tempCustomDir;

        public GordianStorageTests()
        {
            _tempCustomDir = Path.Combine(Path.GetTempPath(), "GordianXI_Test_" + Guid.NewGuid().ToString("N"));
        }

        public void Dispose()
        {
            GordianStorage.ProfilesDirectory = null!;
            if (Directory.Exists(_tempCustomDir))
            {
                Directory.Delete(_tempCustomDir, true);
            }
        }

        [Fact]
        public void RootDataDirectory_PointsToGordianXIInAppData()
        {
            string root = GordianStorage.RootDataDirectory;

            Assert.False(string.IsNullOrWhiteSpace(root));
            Assert.True(root.Contains("GordianXI", StringComparison.OrdinalIgnoreCase));
            Assert.True(Directory.Exists(root));
        }

        [Fact]
        public void ProfilesDirectory_Default_PointsInsideRootDataDirectory()
        {
            string profiles = GordianStorage.ProfilesDirectory;

            Assert.False(string.IsNullOrWhiteSpace(profiles));
            Assert.StartsWith(GordianStorage.RootDataDirectory, profiles, StringComparison.OrdinalIgnoreCase);
            Assert.True(Directory.Exists(profiles));
        }

        [Fact]
        public void ProfilesDirectory_CustomOverride_UsedAccurately()
        {
            GordianStorage.ProfilesDirectory = _tempCustomDir;

            Assert.Equal(_tempCustomDir, GordianStorage.ProfilesDirectory);
            Assert.True(Directory.Exists(_tempCustomDir));
        }
    }
}
