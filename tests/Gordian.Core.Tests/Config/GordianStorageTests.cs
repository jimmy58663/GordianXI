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
            GordianStorage.RootDataDirectory = null!;
            GordianStorage.ProfilesDirectory = null!;
            if (Directory.Exists(_tempCustomDir))
            {
                Directory.Delete(_tempCustomDir, true);
            }
        }

        [Theory]
        [InlineData("Gemini", "Gemini")]
        [InlineData("myserver.example.com", "myserver.example.com")]
        [InlineData("  Spaced  ", "Spaced")]
        [InlineData("a/b\\c:d*e?f\"g<h>i|j", "a_b_c_d_e_f_g_h_i_j")]
        [InlineData("", "Unknown")]
        [InlineData(null, "Unknown")]
        [InlineData("..", "Unknown")]
        [InlineData("name.", "name")]
        [InlineData("Con", "Con_")]
        [InlineData("aux", "aux_")]
        [InlineData("NUL.txt", "NUL.txt_")]
        [InlineData("COM1", "COM1_")]
        [InlineData("lpt9", "lpt9_")]
        [InlineData("COM0", "COM0")]
        [InlineData("Conrad", "Conrad")]
        public void SafeFolderName_IsSafeAndKeepsCasing(string? name, string expected) =>
            Assert.Equal(expected, GordianStorage.SafeFolderName(name));

        [Fact]
        public void RootDataDirectory_PointsToValidLocation()
        {
            string root = GordianStorage.RootDataDirectory;

            Assert.False(string.IsNullOrWhiteSpace(root));
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
        public void Addons_Scripts_LogsDirectories_PointInsideRootDataDirectory()
        {
            string addons = GordianStorage.AddonsDirectory;
            string scripts = GordianStorage.ScriptsDirectory;
            string logs = GordianStorage.LogsDirectory;

            Assert.StartsWith(GordianStorage.RootDataDirectory, addons, StringComparison.OrdinalIgnoreCase);
            Assert.StartsWith(GordianStorage.RootDataDirectory, scripts, StringComparison.OrdinalIgnoreCase);
            Assert.StartsWith(GordianStorage.RootDataDirectory, logs, StringComparison.OrdinalIgnoreCase);

            Assert.True(Directory.Exists(addons));
            Assert.True(Directory.Exists(scripts));
            Assert.True(Directory.Exists(logs));
        }

        [Fact]
        public void ProfilesDirectory_CustomOverride_UsedAccurately()
        {
            GordianStorage.ProfilesDirectory = _tempCustomDir;

            Assert.Equal(_tempCustomDir, GordianStorage.ProfilesDirectory);
            Assert.True(Directory.Exists(_tempCustomDir));
        }

        [Fact]
        public void IsProtectedSystemDirectory_DetectsProgramFiles()
        {
            string progFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            if (!string.IsNullOrEmpty(progFiles))
            {
                string testPath = Path.Combine(progFiles, "GordianXI");
                Assert.True(GordianStorage.IsProtectedSystemDirectory(testPath));
            }

            // Normal user folders are not protected
            Assert.False(GordianStorage.IsProtectedSystemDirectory(Path.Combine("C:", "Games", "GordianXI")));
            Assert.False(GordianStorage.IsProtectedSystemDirectory(Path.Combine("D:", "FFXI", "GordianXI")));
        }
    }
}
