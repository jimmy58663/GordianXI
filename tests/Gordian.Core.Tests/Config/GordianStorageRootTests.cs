// tests/Gordian.Core.Tests/Config/GordianStorageRootTests.cs
using System;
using System.IO;
using Gordian.Core.Config;
using Xunit;

namespace Gordian.Core.Tests.Config
{
    /// <summary>
    /// Where user data (and so the logs folder the Logs tab opens) goes: next to the exe, or the user's application data
    /// folder when the exe is in a protected folder (#566).
    /// </summary>
    public sealed class GordianStorageRootTests
    {
        private static readonly string AppData = Path.Combine(Path.GetTempPath(), "appdata");

        [Fact]
        public void PortableFolder_KeepsDataNextToTheExe()
        {
            string portable = Path.Combine(Path.GetTempPath(), "Games", "GordianXI");

            Assert.Equal(portable, GordianStorage.ResolveRootDataDirectory(portable, AppData, null));
        }

        [Fact]
        public void ProtectedFolder_MovesDataToApplicationData()
        {
            string installed = OperatingSystem.IsWindows()
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "GordianXI")
                : "/opt/gordianxi";

            string root = GordianStorage.ResolveRootDataDirectory(installed, AppData, null);

            Assert.Equal(Path.Combine(AppData, "GordianXI"), root);
            Assert.Equal(Path.Combine(AppData, "GordianXI", "logs"), Path.Combine(root, "logs"));
        }

        [Fact]
        public void ProtectedFolder_WithoutApplicationData_UsesTheUserProfile()
        {
            string installed = OperatingSystem.IsWindows()
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "GordianXI")
                : "/usr/lib/gordianxi";
            string home = Path.Combine(Path.GetTempPath(), "home");

            Assert.Equal(Path.Combine(home, "GordianXI"), GordianStorage.ResolveRootDataDirectory(installed, "", home));
        }
    }
}
