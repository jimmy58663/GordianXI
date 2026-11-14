// tests/Gordian.App.Tests/MainWindowViewModelTests.cs
using System;
using System.IO;
using System.Linq;
using Gordian.App.ViewModels;
using Gordian.Core.Config;
using Gordian.Core.Network;
using Gordian.Core.Profiles;
using Gordian.Engine.Sessions;
using Xunit;

namespace Gordian.App.Tests
{
    [Collection("StorageTests")]
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
        public void StatusBox_KeepsTheLatestMessages()
        {
            using var vm = new MainWindowViewModel(_testRegistry);
            var time = new DateTime(2026, 11, 14, 9, 55, 35);

            for (int i = 0; i < MainWindowViewModel.RecentStatusCapacity + 5; i++) vm.AddRecentStatus($"message {i}", time);

            Assert.Equal(MainWindowViewModel.RecentStatusCapacity, vm.RecentStatusLines.Count);
            Assert.Equal($"[09:55:35] message {MainWindowViewModel.RecentStatusCapacity + 4}", vm.RecentStatusLines[^1]);
        }

        [Fact]
        public void ViewFullLog_OpensTheLogsTabOnTheStatusFile()
        {
            using var vm = new MainWindowViewModel(_testRegistry);

            vm.ShowLogsTabCommand.Execute(null);

            Assert.Equal(MainWindowViewModel.LogsTabIndex, vm.SelectedTabIndex);
            Assert.True(vm.Logs.IsStatusSelected);
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
            vm.FormServerHost = "127.0.0.1";

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

        [Fact]
        public void ProfileItemViewModel_OnlyTheProfileOfTheLoggedInCharacterIsOnline()
        {
            var knot = new ProfileItemViewModel(new AccountProfile { ProfileName = "Knot", Username = "acct1000", CharacterName = "Knot" }, _testRegistry);
            var blm = new ProfileItemViewModel(new AccountProfile { ProfileName = "BLM", Username = "acct1000", CharacterSlot = 2 }, _testRegistry);

            var net = new SessionNetworkManager("127.0.0.1", 54231) { CurrentState = SessionState.ActiveInWorld };
            _testRegistry.RegisterSession(new CharacterSession("BLM", 21900, "acct1000", net) { ProfileName = "BLM" });

            knot.RefreshOnlineStatus();
            blm.RefreshOnlineStatus();

            Assert.False(knot.IsOnline);
            Assert.True(blm.IsOnline);
            Assert.Equal("Char: slot 2", blm.CharacterSubtitle);

            // Terminating one profile leaves the account's other characters alone.
            var net2 = new SessionNetworkManager("127.0.0.1", 54231) { CurrentState = SessionState.ActiveInWorld };
            _testRegistry.RegisterSession(new CharacterSession("Knot", 21828, "acct1000", net2) { ProfileName = "Knot" });
            knot.RefreshOnlineStatus();
            Assert.True(knot.IsOnline);
            blm.Terminate();
            knot.RefreshOnlineStatus();
            Assert.False(blm.IsOnline);
            Assert.True(knot.IsOnline);
        }

        [Fact]
        public void SaveProfile_KeepsTheProfilePositionInTheTree()
        {
            using var vm = new MainWindowViewModel(_testRegistry);
            var order = vm.LaunchTree.OfType<ProfileItemViewModel>().Select(p => p.ProfileName).ToList();
            Assert.True(order.Count >= 2);

            var first = vm.LaunchTree.OfType<ProfileItemViewModel>().First();
            first.EditCommand.Execute(null);
            vm.FormUsername = "changed_account";
            vm.SaveProfileCommand.Execute(null);

            Assert.Equal(order, vm.LaunchTree.OfType<ProfileItemViewModel>().Select(p => p.ProfileName).ToList());
            Assert.Equal(first.ProfileName, vm.Profiles[0].ProfileName);

            // Same inside a folder: edit the first of two profiles there.
            foreach (var name in new[] { "FolderA", "FolderB" })
            {
                vm.FormProfileName = name;
                vm.FormFolder = "Grp";
                vm.FormServerHost = "127.0.0.1";
                vm.FormUsername = name;
                vm.SaveProfileCommand.Execute(null);
            }
            var folder = vm.LaunchTree.OfType<LaunchFolderViewModel>().First(f => f.FolderPath == "Grp");
            var a = folder.Children.OfType<ProfileItemViewModel>().First(p => p.ProfileName == "FolderA");
            a.EditCommand.Execute(null);
            vm.FormUsername = "changed_account";
            vm.SaveProfileCommand.Execute(null);
            Assert.Equal(new[] { "FolderA", "FolderB" }, folder.Children.OfType<ProfileItemViewModel>().Select(p => p.ProfileName).ToArray());
        }

        [Fact]
        public void GetLaunchSkipReason_BlocksASecondCharacterOfALiveAccount_ButOnlyThatAccount()
        {
            using var vm = new MainWindowViewModel(_testRegistry);
            var knot = new AccountProfile { ProfileName = "Knot", Username = "acct1000", CharacterName = "Knot" };
            var blm = new AccountProfile { ProfileName = "BLM", Username = "acct1000", CharacterSlot = 2 };
            var other = new AccountProfile { ProfileName = "Other", Username = "acct2000" };

            Assert.Null(vm.GetLaunchSkipReason(blm));

            var net = new SessionNetworkManager("127.0.0.1", 54231) { CurrentState = SessionState.ActiveInWorld };
            _testRegistry.RegisterSession(new CharacterSession("Knot", 21828, "acct1000", net) { ProfileName = "Knot" });

            Assert.Contains("already active", vm.GetLaunchSkipReason(knot));
            Assert.Equal("Account 'acct1000' already has a character in game (Knot). Skipped.", vm.GetLaunchSkipReason(blm));
            Assert.Null(vm.GetLaunchSkipReason(other));
        }

        [Fact]
        public void EditProfileCommand_PopulatesFormAndUpdatesOnSave()
        {
            using var vm = new MainWindowViewModel(_testRegistry);
            var item = vm.Profiles.First();

            item.EditCommand.Execute(null);

            Assert.True(vm.IsEditing);
            Assert.True(vm.IsEditorOpen);
            Assert.False(vm.IsListVisible);
            Assert.Equal(item.ProfileName, vm.FormProfileName);
            Assert.True(vm.FormIsRetail);

            vm.FormPolId = "NEWPOLID";
            vm.SaveProfileCommand.Execute(null);

            Assert.False(vm.IsEditing);
            Assert.False(vm.IsEditorOpen);
            Assert.True(vm.IsListVisible);
            Assert.Equal(string.Empty, vm.FormProfileName);
            Assert.Contains(vm.Profiles, p => p.Profile.PolId == "NEWPOLID");
        }

        [Fact]
        public void NewProfile_OpensABlankEditorInTheSelectedFolder_AndCancelReturnsToTheList()
        {
            using var vm = new MainWindowViewModel(_testRegistry);
            Assert.True(vm.IsListVisible);

            vm.SelectedTreeNode = vm.LaunchTree.OfType<LaunchFolderViewModel>().First(f => f.FolderPath == "Test1");
            vm.NewProfileCommand.Execute(null);

            Assert.True(vm.IsEditorOpen);
            Assert.False(vm.IsEditing);
            Assert.Equal("New Profile", vm.FormTitle);
            Assert.Equal("Test1", vm.FormFolder);
            Assert.True(vm.FormIsLsb);
            Assert.True(vm.FormTwoFactorNone);

            vm.ClearFormCommand.Execute(null);
            Assert.True(vm.IsListVisible);
        }

        [Fact]
        public void SaveProfile_RefusesALandSandBoatProfileWithoutAHost_AndABadAuthenticatorKey()
        {
            using var vm = new MainWindowViewModel(_testRegistry);
            vm.NewProfileCommand.Execute(null);
            vm.FormProfileName = "NoHost";
            vm.SaveProfileCommand.Execute(null);
            Assert.Equal("A LandSandBoat profile needs a Server Host.", vm.StatusMessage);
            Assert.True(vm.IsEditorOpen);

            vm.FormServerHost = "127.0.0.1";
            vm.FormTwoFactorKey = true;
            vm.FormOtpSeed = "not a key!";
            vm.SaveProfileCommand.Execute(null);
            Assert.StartsWith("The authenticator key", vm.StatusMessage);
            Assert.DoesNotContain(vm.Profiles, p => p.ProfileName == "NoHost");
        }

        [Fact]
        public void SaveProfile_KeepsOnlyTheChosenServerTypesFields()
        {
            using var vm = new MainWindowViewModel(_testRegistry);
            vm.NewProfileCommand.Execute(null);
            vm.FormProfileName = "Switched";
            vm.FormServerHost = "10.0.0.5";
            vm.FormDataPort = 60000;
            vm.FormIsRetail = true;
            vm.FormPolId = "ABCD1234";
            vm.FormPolPassword = "polpass";
            vm.FormUsername = "seid";
            vm.FormTwoFactorAtLaunch = true;
            vm.SaveProfileCommand.Execute(null);

            var saved = vm.Profiles.Single(p => p.ProfileName == "Switched").Profile;
            Assert.Equal(ProfileServerType.Retail, saved.ServerType);
            Assert.Equal(string.Empty, saved.ServerHost);
            Assert.Equal(0, saved.DataPort);
            Assert.Equal("ABCD1234", saved.PolId);
            Assert.Equal("polpass", saved.PolPassword);
            Assert.Equal(TwoFactorMode.EnterAtLaunch, saved.TwoFactor);
            Assert.Equal("Retail", vm.Profiles.Single(p => p.ProfileName == "Switched").ServerBadgeText);
        }

        [Fact]
        public void ProfileCard_AsksForTheCodeOnlyForEnterAtLaunch_AndKeepsDigitsOnly()
        {
            var atLaunch = new ProfileItemViewModel(new AccountProfile { ProfileName = "A", TwoFactor = TwoFactorMode.EnterAtLaunch }, _testRegistry);
            var keyed = new ProfileItemViewModel(new AccountProfile { ProfileName = "B", OtpSeed = "HXDMVJECJJWSRB3D" }, _testRegistry);
            var none = new ProfileItemViewModel(new AccountProfile { ProfileName = "C" }, _testRegistry);

            Assert.True(atLaunch.AsksForCode);
            Assert.False(atLaunch.ShowsComputedCode);
            Assert.False(keyed.AsksForCode);
            Assert.True(keyed.ShowsComputedCode);
            Assert.False(none.AsksForCode);
            Assert.False(none.ShowsComputedCode);

            atLaunch.EnteredCode = "12 34-567x8";
            Assert.Equal("123456", atLaunch.EnteredCode);
            Assert.Equal("123456", atLaunch.Profile.ResolveOneTimeCode());
        }

        [Fact]
        public void DeleteProfileCommand_RemovesFromCollectionAndDeletesDiskFile()
        {
            using var vm = new MainWindowViewModel(_testRegistry);
            var target = vm.Profiles.First();
            string profileName = target.ProfileName;
            int initialCount = vm.Profiles.Count;

            target.DeleteCommand.Execute(null);

            Assert.Equal(initialCount - 1, vm.Profiles.Count);
            Assert.DoesNotContain(vm.Profiles, p => p.ProfileName == profileName);

            string expectedFile = Path.Combine(_tempProfilesDir, $"{profileName}.json");
            Assert.False(File.Exists(expectedFile));
        }

        [Fact]
        public void SaveProfile_WithCharacterName_PersistsAndPopulatesSubtitle()
        {
            using var vm = new MainWindowViewModel(_testRegistry);

            vm.FormProfileName = "Local (Cybin)";
            vm.FormCharacterName = "Cybin";
            vm.FormUsername = "Cybin";
            vm.FormPassword = "SecretPassword123";
            vm.FormServerHost = "127.0.0.1";

            vm.SaveProfileCommand.Execute(null);

            var created = vm.Profiles.FirstOrDefault(p => p.ProfileName == "Local (Cybin)");
            Assert.NotNull(created);
            Assert.Equal("Local (Cybin)", created.ProfileName);
            Assert.Equal("Cybin", created.CharacterName);
            Assert.True(created.HasCharacterSubtitle);
            Assert.Equal("Char: Cybin", created.CharacterSubtitle);
        }
    }
}
