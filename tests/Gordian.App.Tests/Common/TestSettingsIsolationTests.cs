// tests/Gordian.App.Tests/Common/TestSettingsIsolationTests.cs
using System;
using System.IO;
using Gordian.App.Audio;
using Gordian.App.Graphics;
using Gordian.App.Services;
using Gordian.App.ViewModels;
using Gordian.Core.Config;

namespace Gordian.App.Tests.Common
{
    public class TestSettingsIsolationTests
    {
        [Fact]
        public void DefaultSettingsPaths_StayOutOfTheRealUserFolder()
        {
            string real = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GordianXI");
            foreach (string path in new[]
            {
                ControlsInputViewModel.GetDefaultProfilePath(), SoundControlSettings.GetDefaultSettingsPath(),
                ViewportSettings.GetDefaultSettingsPath(), WindowPlacementManager.GetDefaultFilePath(),
            })
            {
                Assert.StartsWith(GordianStorage.UserSettingsDirectory, path);
                Assert.DoesNotContain(real + Path.DirectorySeparatorChar, path);
            }
        }
    }
}
