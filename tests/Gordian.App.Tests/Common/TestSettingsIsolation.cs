// tests/Gordian.App.Tests/Common/TestSettingsIsolation.cs
using System.IO;
using System.Runtime.CompilerServices;
using Gordian.Core.Config;

namespace Gordian.App.Tests.Common
{
    /// <summary>
    /// Points the shell's per-user settings folder (input profile, viewport, sound, window placements) at a scratch folder
    /// before any test runs, so a view model built with its default paths (MainWindowViewModel and the view models it owns)
    /// never reads or writes the player's real settings (#383).
    /// </summary>
    internal static class TestSettingsIsolation
    {
        [ModuleInitializer]
        internal static void Isolate()
        {
            string folder = Path.Combine(Path.GetTempPath(), "GordianXI.AppTests." + Path.GetRandomFileName());
            Directory.CreateDirectory(folder);
            GordianStorage.UserSettingsDirectory = folder;
        }
    }
}
