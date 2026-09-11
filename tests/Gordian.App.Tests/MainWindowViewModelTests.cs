// tests/Gordian.App.Tests/MainWindowViewModelTests.cs
using System;
using System.IO;
using Gordian.App.ViewModels;
using Gordian.Core.Config;
using Gordian.Core.Network;
using Gordian.Core.Profiles;
using Xunit;

namespace Gordian.App.Tests
{
    public class MainWindowViewModelTests : IDisposable
    {
        private readonly string _tempProfilesDir;
        private readonly SessionRegistry _testRegistry;

        public MainWindowViewModelTests()
        {
            _tempProfilesDir = Path.Combine(Path.GetTempPath(), "GordianApp_Test_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempProfilesDir);
            GordianStorage.ProfilesDirectory = _tempProfilesDir;
            _testRegistry = new SessionRegistry();
        }

        public void Dispose()
        {
            _testRegistry.Dispose();
            GordianStorage.ProfilesDirectory = null!;
            if (Directory.Exists(_tempProfilesDir))
            {
                Directory.Delete(_tempProfilesDir, true);
            }
        }

        [Fact]
        public void InitialLoad_CreatesDefaultProfilesWhenEmpty()
        {
            using var vm = new MainWindowViewModel(_testRegistry);

            Assert.True(vm.Profiles.Count >= 2);
            Assert.Contains(vm.Profiles, p => p.ProfileName.Contains("JimmyMain"));
            Assert.Contains(vm.Profiles, p => p.ProfileName.Contains("CraftMule"));
        }

        [Fact]
        public void SaveProfileCommand_AddsNewProfileAndWritesToDisk()
        {
            using var vm = new MainWindowViewModel(_testRegistry);
            int initialCount = vm.Profiles.Count;

            vm.FormProfileName = "BlackMage";
            vm.FormUsername = "blm_account";
            vm.FormPassword = "NukePassword123";
            vm.FormBootloaderPath = "xiloader.exe";
            vm.FormArguments = "--server 127.0.0.1";

            vm.SaveProfileCommand.Execute(null);

            Assert.Equal(initialCount + 1, vm.Profiles.Count);
            Assert.Contains(vm.Profiles, p => p.ProfileName == "BlackMage");
            Assert.Contains("Saved profile 'BlackMage'", vm.StatusMessage);

            // Verify file exists in AppData profiles directory
            string expectedFile = Path.Combine(_tempProfilesDir, "BlackMage.json");
            Assert.True(File.Exists(expectedFile));
        }

        [Fact]
        public void ProfileItemViewModel_ReflectsLiveOnlineStatusFromRegistry()
        {
            using var vm = new MainWindowViewModel(_testRegistry);

            var item = new ProfileItemViewModel(new AccountProfile
            {
                ProfileName = "LiveChar",
                Username = "live_user"
            }, _testRegistry);

            Assert.False(item.IsOnline);
            Assert.Equal("Offline", item.StatusText);

            // Register active session into registry
            var net = new SessionNetworkManager("127.0.0.1", 54231)
            {
                CurrentState = SessionState.ActiveInWorld
            };
            var session = new CharacterSession("LiveChar", 500, "live_user", net);
            _testRegistry.RegisterSession(session);

            item.RefreshOnlineStatus();

            Assert.True(item.IsOnline);
            Assert.Equal("Online", item.StatusText);
        }
    }
}
