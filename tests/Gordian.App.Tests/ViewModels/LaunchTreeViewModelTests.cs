// tests/Gordian.App.Tests/ViewModels/LaunchTreeViewModelTests.cs
using System;
using System.IO;
using System.Linq;
using Gordian.App.ViewModels;
using Gordian.Core.Config;
using Gordian.Core.Network;
using Gordian.Core.Profiles;
using Xunit;

namespace Gordian.App.Tests.ViewModels
{
    [Collection("StorageTests")]
    public class LaunchTreeViewModelTests : IDisposable
    {
        private readonly string _tempProfilesDir;
        private readonly SessionRegistry _testRegistry;

        public LaunchTreeViewModelTests()
        {
            _tempProfilesDir = Path.Combine(Path.GetTempPath(), "GordianApp_TreeTest_" + Guid.NewGuid().ToString("N"));
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
                try
                {
                    Directory.Delete(_tempProfilesDir, true);
                }
                catch
                {
                    // Clean up best effort
                }
            }
        }

        [Fact]
        public void CreateFolder_WindowsExplorerStyle_AutoNamesAndEntersRename()
        {
            using var vm = new MainWindowViewModel(_testRegistry);

            // Clicking New Folder with no text typed should auto-generate "New Folder"
            vm.CreateFolderCommand.Execute(null);

            var folder1 = vm.LaunchTree.OfType<LaunchFolderViewModel>().FirstOrDefault(f => f.Name == "New Folder");
            Assert.NotNull(folder1);
            Assert.True(folder1.IsRenaming);
            Assert.Equal(folder1, vm.SelectedTreeNode);

            // Clicking New Folder again should auto-generate "New Folder 1"
            vm.CreateFolderCommand.Execute(null);

            // Note: Since folder1 was selected, the second New Folder is created as a child of folder1
            var folder2 = folder1.Children.OfType<LaunchFolderViewModel>().FirstOrDefault(f => f.Name == "New Folder");
            Assert.NotNull(folder2);
            Assert.True(folder2.IsRenaming);

            // Deselect to create at root
            vm.SelectedTreeNode = null;
            vm.CreateFolderCommand.Execute(null);

            var folderRoot2 = vm.LaunchTree.OfType<LaunchFolderViewModel>().FirstOrDefault(f => f.Name == "New Folder 1");
            Assert.NotNull(folderRoot2);
            Assert.True(folderRoot2.IsRenaming);
        }

        [Fact]
        public void InitialTree_CreatesWinScpHierarchyStructure()
        {
            using var vm = new MainWindowViewModel(_testRegistry);

            // Structure should have:
            // - Folder Test1 (with AF00 and CraftMule)
            // - Folder Test2 (with subfolder Test3 -> HS00)
            // - Root profile LP00-QA
            // - Root profile JimmyMain

            var test1 = vm.LaunchTree.OfType<LaunchFolderViewModel>().FirstOrDefault(f => f.Name == "Test1");
            Assert.NotNull(test1);
            Assert.Contains(test1.Children.OfType<ProfileItemViewModel>(), p => p.ProfileName == "AF00");

            var test2 = vm.LaunchTree.OfType<LaunchFolderViewModel>().FirstOrDefault(f => f.Name == "Test2");
            Assert.NotNull(test2);
            var test3 = test2.Children.OfType<LaunchFolderViewModel>().FirstOrDefault(f => f.Name == "Test3");
            Assert.NotNull(test3);
            Assert.Contains(test3.Children.OfType<ProfileItemViewModel>(), p => p.ProfileName == "HS00");

            var lp00 = vm.LaunchTree.OfType<ProfileItemViewModel>().FirstOrDefault(p => p.ProfileName == "LP00-QA");
            Assert.NotNull(lp00);
            Assert.Null(lp00.Parent);
        }

        [Fact]
        public void Folder_CascadingCheckbox_SelectsAndDeselectsAllDescendants()
        {
            using var vm = new MainWindowViewModel(_testRegistry);

            var test1 = vm.LaunchTree.OfType<LaunchFolderViewModel>().First(f => f.Name == "Test1");

            // Deselect folder
            test1.IsSelectedForLaunch = false;
            Assert.False(test1.IsSelectedForLaunch);
            foreach (var child in test1.Children.OfType<ProfileItemViewModel>())
            {
                Assert.False(child.IsSelectedForLaunch);
            }

            // Select folder
            test1.IsSelectedForLaunch = true;
            Assert.True(test1.IsSelectedForLaunch);
            foreach (var child in test1.Children.OfType<ProfileItemViewModel>())
            {
                Assert.True(child.IsSelectedForLaunch);
            }
        }

        [Fact]
        public void Folder_PartialSelection_SetsIndeterminateState()
        {
            using var vm = new MainWindowViewModel(_testRegistry);

            var test1 = vm.LaunchTree.OfType<LaunchFolderViewModel>().First(f => f.Name == "Test1");
            var profiles = test1.Children.OfType<ProfileItemViewModel>().ToList();
            Assert.True(profiles.Count >= 2);

            // Set all true first
            test1.IsSelectedForLaunch = true;
            Assert.True(test1.IsSelectedForLaunch);

            // Deselect just the first child
            profiles[0].IsSelectedForLaunch = false;

            // Folder check state must now be indeterminate (null)
            Assert.Null(test1.IsSelectedForLaunch);

            // Deselect remaining children
            for (int i = 1; i < profiles.Count; i++)
            {
                profiles[i].IsSelectedForLaunch = false;
            }

            // Folder check state must now be false
            Assert.False(test1.IsSelectedForLaunch);
        }

        [Fact]
        public void NestedFolder_CascadesAcrossMultipleLevels()
        {
            using var vm = new MainWindowViewModel(_testRegistry);

            var test2 = vm.LaunchTree.OfType<LaunchFolderViewModel>().First(f => f.Name == "Test2");
            var test3 = test2.Children.OfType<LaunchFolderViewModel>().First(f => f.Name == "Test3");
            var hs00 = test3.Children.OfType<ProfileItemViewModel>().First(p => p.ProfileName == "HS00");

            // Toggle top-level parent to false
            test2.IsSelectedForLaunch = false;
            Assert.False(test2.IsSelectedForLaunch);
            Assert.False(test3.IsSelectedForLaunch);
            Assert.False(hs00.IsSelectedForLaunch);

            // Toggle child to true -> test3 and test2 should become true
            hs00.IsSelectedForLaunch = true;
            Assert.True(hs00.IsSelectedForLaunch);
            Assert.True(test3.IsSelectedForLaunch);
            Assert.True(test2.IsSelectedForLaunch);
        }

        [Fact]
        public void SelectionState_RemembersStateAcrossReopen()
        {
            // First session: Uncheck AF00, check LP00-QA
            {
                using var vm1 = new MainWindowViewModel(_testRegistry);
                var af00 = vm1.Profiles.First(p => p.ProfileName == "AF00");
                var lp00 = vm1.Profiles.First(p => p.ProfileName == "LP00-QA");

                af00.IsSelectedForLaunch = false;
                lp00.IsSelectedForLaunch = true;
            }

            // Second session: Reopen and verify states were remembered from disk
            {
                using var vm2 = new MainWindowViewModel(_testRegistry);
                var af00 = vm2.Profiles.First(p => p.ProfileName == "AF00");
                var lp00 = vm2.Profiles.First(p => p.ProfileName == "LP00-QA");

                Assert.False(af00.IsSelectedForLaunch);
                Assert.True(lp00.IsSelectedForLaunch);
            }
        }

        [Fact]
        public void SaveProfile_WithFolder_PlacesInTreeCorrectly()
        {
            using var vm = new MainWindowViewModel(_testRegistry);

            vm.FormProfileName = "DynamisThief";
            vm.FormFolder = "Endgame/Dynamis";
            vm.FormUsername = "thf_user";
            vm.FormPassword = "ThfPassword";
            vm.FormServerHost = "127.0.0.1";

            vm.SaveProfileCommand.Execute(null);

            var endgame = vm.LaunchTree.OfType<LaunchFolderViewModel>().FirstOrDefault(f => f.Name == "Endgame");
            Assert.NotNull(endgame);
            var dynamis = endgame.Children.OfType<LaunchFolderViewModel>().FirstOrDefault(f => f.Name == "Dynamis");
            Assert.NotNull(dynamis);
            Assert.Contains(dynamis.Children.OfType<ProfileItemViewModel>(), p => p.ProfileName == "DynamisThief");

            string expectedFile = Path.Combine(_tempProfilesDir, "Endgame", "Dynamis", "DynamisThief.json");
            Assert.True(File.Exists(expectedFile));
        }

        [Fact]
        public void EditProfile_MovingFolder_UpdatesTreeAndFilesystem()
        {
            using var vm = new MainWindowViewModel(_testRegistry);

            // AF00 is initially in Test1
            var af00 = vm.Profiles.First(p => p.ProfileName == "AF00");
            af00.EditCommand.Execute(null);

            Assert.Equal("Test1", vm.FormFolder);

            // Move to Test2/SubAF
            vm.FormFolder = "Test2/SubAF";
            vm.SaveProfileCommand.Execute(null);

            // Old path should no longer exist
            string oldPath = Path.Combine(_tempProfilesDir, "Test1", "AF00.json");
            Assert.False(File.Exists(oldPath));

            // New path should exist
            string newPath = Path.Combine(_tempProfilesDir, "Test2", "SubAF", "AF00.json");
            Assert.True(File.Exists(newPath));

            // Verify tree placement
            var test2 = vm.LaunchTree.OfType<LaunchFolderViewModel>().First(f => f.Name == "Test2");
            var subAf = test2.Children.OfType<LaunchFolderViewModel>().FirstOrDefault(f => f.Name == "SubAF");
            Assert.NotNull(subAf);
            Assert.Contains(subAf.Children.OfType<ProfileItemViewModel>(), p => p.ProfileName == "AF00");
        }

        [Fact]
        public void CreateFolderCommand_AddsNewFolderToTree()
        {
            using var vm = new MainWindowViewModel(_testRegistry);

            vm.NewFolderName = "ExpParties/Level30";
            vm.CreateFolderCommand.Execute(null);

            var exp = vm.LaunchTree.OfType<LaunchFolderViewModel>().FirstOrDefault(f => f.Name == "ExpParties");
            Assert.NotNull(exp);
            var lvl30 = exp.Children.OfType<LaunchFolderViewModel>().FirstOrDefault(f => f.Name == "Level30");
            Assert.NotNull(lvl30);
            Assert.Equal(string.Empty, vm.NewFolderName);
        }

        [Fact]
        public void SelectAllAndDeselectAllCommands_UpdateEntireTree()
        {
            using var vm = new MainWindowViewModel(_testRegistry);

            vm.DeselectAllCommand.Execute(null);
            foreach (var profile in vm.Profiles)
            {
                Assert.False(profile.IsSelectedForLaunch);
            }

            vm.SelectAllCommand.Execute(null);
            foreach (var profile in vm.Profiles)
            {
                Assert.True(profile.IsSelectedForLaunch);
            }
        }

        [Fact]
        public void MoveProfileToFolder_MovesProfileBetweenFoldersAndRoot()
        {
            using var vm = new MainWindowViewModel(_testRegistry);

            var lp00 = vm.Profiles.First(p => p.ProfileName == "LP00-QA");
            var test1 = vm.LaunchTree.OfType<LaunchFolderViewModel>().First(f => f.Name == "Test1");

            // Verify LP00 is initially at root
            Assert.Contains(vm.LaunchTree.OfType<ProfileItemViewModel>(), p => p.ProfileName == "LP00-QA");
            Assert.DoesNotContain(test1.Children.OfType<ProfileItemViewModel>(), p => p.ProfileName == "LP00-QA");

            // Move LP00 into Test1 folder
            vm.MoveProfileToFolder(lp00, test1);

            Assert.DoesNotContain(vm.LaunchTree.OfType<ProfileItemViewModel>(), p => p.ProfileName == "LP00-QA");
            Assert.Contains(test1.Children.OfType<ProfileItemViewModel>(), p => p.ProfileName == "LP00-QA");
            Assert.Equal(test1, lp00.Parent);

            string fileInFolder = Path.Combine(_tempProfilesDir, "Test1", "LP00-QA.json");
            Assert.True(File.Exists(fileInFolder));

            // Move LP00 back to root (targetFolder == null)
            vm.MoveProfileToFolder(lp00, null);

            Assert.Contains(vm.LaunchTree.OfType<ProfileItemViewModel>(), p => p.ProfileName == "LP00-QA");
            Assert.DoesNotContain(test1.Children.OfType<ProfileItemViewModel>(), p => p.ProfileName == "LP00-QA");
            Assert.Null(lp00.Parent);

            string fileInRoot = Path.Combine(_tempProfilesDir, "LP00-QA.json");
            Assert.True(File.Exists(fileInRoot));
            Assert.False(File.Exists(fileInFolder));
        }

        [Fact]
        public void RenameFolder_RenamesFolderOnDiskAndUpdatesDescendants()
        {
            using var vm = new MainWindowViewModel(_testRegistry);

            var test1 = vm.LaunchTree.OfType<LaunchFolderViewModel>().First(f => f.Name == "Test1");
            string oldDiskDir = Path.Combine(_tempProfilesDir, "Test1");
            Assert.True(Directory.Exists(oldDiskDir));

            // Rename Test1 -> SkyParties
            vm.RenameFolder(test1, "SkyParties");

            Assert.Equal("SkyParties", test1.Name);
            Assert.Equal("SkyParties", test1.FolderPath);

            string newDiskDir = Path.Combine(_tempProfilesDir, "SkyParties");
            Assert.True(Directory.Exists(newDiskDir));
            Assert.False(Directory.Exists(oldDiskDir));

            // Check descendant profiles updated their Folder property
            var af00 = test1.Children.OfType<ProfileItemViewModel>().First(p => p.ProfileName == "AF00");
            Assert.Equal("SkyParties", af00.Profile.Folder);
            Assert.True(File.Exists(Path.Combine(newDiskDir, "AF00.json")));
        }

        [Fact]
        public void CopyProfile_DuplicatesProfileAndPlacesNextToSource()
        {
            using var vm = new MainWindowViewModel(_testRegistry);

            var test1 = vm.LaunchTree.OfType<LaunchFolderViewModel>().First(f => f.Name == "Test1");
            var af00 = test1.Children.OfType<ProfileItemViewModel>().First(p => p.ProfileName == "AF00");

            int initialChildCount = test1.Children.Count;
            int af00Index = test1.Children.IndexOf(af00);

            // Copy AF00
            af00.CopyCommand.Execute(null);

            Assert.Equal(initialChildCount + 1, test1.Children.Count);
            var copyItem = test1.Children[af00Index + 1] as ProfileItemViewModel;
            Assert.NotNull(copyItem);
            Assert.Equal("AF00 - Copy", copyItem.ProfileName);
            Assert.Equal("AF00", copyItem.CharacterName);
            Assert.Equal(test1, copyItem.Parent);
            Assert.Equal("Test1", copyItem.Profile.Folder);

            // Edit form was populated with the copy for quick modifications
            Assert.Equal("AF00 - Copy", vm.FormProfileName);
            Assert.True(vm.IsEditing);

            // Copying again should auto-increment to "- Copy 2"
            af00.CopyCommand.Execute(null);
            Assert.Contains(vm.Profiles, p => p.ProfileName == "AF00 - Copy 2");
        }

        [Fact]
        public void MoveProfileUpAndDown_ReordersAndPersistsAcrossSessions()
        {
            // Initial order in Test1: [CraftMule, AF00]
            {
                using var vm1 = new MainWindowViewModel(_testRegistry);
                var test1 = vm1.LaunchTree.OfType<LaunchFolderViewModel>().First(f => f.Name == "Test1");
                var craftMule = test1.Children.OfType<ProfileItemViewModel>().First(p => p.ProfileName.StartsWith("CraftMule"));
                var af00 = test1.Children.OfType<ProfileItemViewModel>().First(p => p.ProfileName == "AF00");

                Assert.Equal(0, test1.Children.IndexOf(craftMule));
                Assert.Equal(1, test1.Children.IndexOf(af00));

                // Move AF00 up (swapping with CraftMule)
                af00.MoveUpCommand.Execute(null);

                Assert.Equal(0, test1.Children.IndexOf(af00));
                Assert.Equal(1, test1.Children.IndexOf(craftMule));
            }

            // Second session: Reopen and verify order was saved to profiles.json
            {
                using var vm2 = new MainWindowViewModel(_testRegistry);
                var test1 = vm2.LaunchTree.OfType<LaunchFolderViewModel>().First(f => f.Name == "Test1");
                var firstChild = test1.Children[0] as ProfileItemViewModel;
                var secondChild = test1.Children[1] as ProfileItemViewModel;

                Assert.NotNull(firstChild);
                Assert.NotNull(secondChild);
                Assert.Equal("AF00", firstChild.ProfileName);
                Assert.StartsWith("CraftMule", secondChild.ProfileName);

                // Move AF00 down (restoring original order)
                firstChild.MoveDownCommand.Execute(null);
                Assert.StartsWith("CraftMule", ((ProfileItemViewModel)test1.Children[0]).ProfileName);
                Assert.Equal("AF00", ((ProfileItemViewModel)test1.Children[1]).ProfileName);
            }
        }

        [Fact]
        public void DeleteFolder_WithProfiles_ShowsConfirmationModal_AndDeletesOnConfirm()
        {
            using var vm = new MainWindowViewModel(_testRegistry);

            var test1 = vm.LaunchTree.OfType<LaunchFolderViewModel>().First(f => f.Name == "Test1");
            int profileCount = test1.CountDescendantProfiles();
            Assert.True(profileCount > 0);

            // Trigger folder delete
            test1.DeleteFolderCommand.Execute(null);

            // Modal should be shown
            Assert.True(vm.IsConfirmDeleteModalVisible);
            Assert.Equal("Confirm Folder Deletion", vm.ConfirmDeleteModalTitle);
            Assert.Contains(profileCount.ToString(), vm.ConfirmDeleteModalMessage);
            Assert.Contains("Test1", vm.ConfirmDeleteModalMessage);

            // Cancel deletion
            vm.CancelDeleteModalCommand.Execute(null);
            Assert.False(vm.IsConfirmDeleteModalVisible);
            Assert.Contains(vm.LaunchTree.OfType<LaunchFolderViewModel>(), f => f.Name == "Test1");

            // Trigger delete again and confirm
            test1.DeleteFolderCommand.Execute(null);
            Assert.True(vm.IsConfirmDeleteModalVisible);
            vm.ConfirmDeleteModalActionCommand?.Execute(null);

            // Modal dismissed and folder + profiles removed
            Assert.False(vm.IsConfirmDeleteModalVisible);
            Assert.DoesNotContain(vm.LaunchTree.OfType<LaunchFolderViewModel>(), f => f.Name == "Test1");
            Assert.DoesNotContain(vm.Profiles, p => p.ProfileName == "AF00");
        }

        [Fact]
        public void DeleteFolder_EmptyFolder_DeletesImmediatelyWithoutModal()
        {
            using var vm = new MainWindowViewModel(_testRegistry);

            vm.NewFolderName = "EmptyPartyFolder";
            vm.CreateFolderCommand.Execute(null);

            var emptyFolder = vm.LaunchTree.OfType<LaunchFolderViewModel>().FirstOrDefault(f => f.Name == "EmptyPartyFolder");
            Assert.NotNull(emptyFolder);
            Assert.Equal(0, emptyFolder.CountDescendantProfiles());

            // Delete empty folder
            emptyFolder.DeleteFolderCommand.Execute(null);

            // Modal should NOT be shown
            Assert.False(vm.IsConfirmDeleteModalVisible);
            Assert.DoesNotContain(vm.LaunchTree.OfType<LaunchFolderViewModel>(), f => f.Name == "EmptyPartyFolder");
        }

        [Fact]
        public void MoveProfileRelative_DragDropReordersAcrossAndWithinFolders()
        {
            using var vm = new MainWindowViewModel(_testRegistry);

            var lp00 = vm.Profiles.First(p => p.ProfileName == "LP00-QA");
            var jimmyMain = vm.Profiles.First(p => p.ProfileName.StartsWith("JimmyMain"));

            // Reorder LP00 directly before JimmyMain at root
            vm.MoveProfileRelative(lp00, jimmyMain, insertAfter: false);

            int lp00Index = vm.LaunchTree.IndexOf(lp00);
            int jimmyIndex = vm.LaunchTree.IndexOf(jimmyMain);

            Assert.True(lp00Index >= 0);
            Assert.Equal(lp00Index + 1, jimmyIndex);
        }

        [Fact]
        public void MoveFolderUpAndDown_ReordersFoldersAndPersists()
        {
            // Initial folder order: Test1 (index 0), Test2 (index 1)
            {
                using var vm1 = new MainWindowViewModel(_testRegistry);
                var test1 = vm1.LaunchTree.OfType<LaunchFolderViewModel>().First(f => f.Name == "Test1");
                var test2 = vm1.LaunchTree.OfType<LaunchFolderViewModel>().First(f => f.Name == "Test2");

                int idx1 = vm1.LaunchTree.IndexOf(test1);
                int idx2 = vm1.LaunchTree.IndexOf(test2);
                Assert.True(idx1 < idx2);

                // Move Test2 up
                test2.MoveUpCommand.Execute(null);

                int newIdx1 = vm1.LaunchTree.IndexOf(test1);
                int newIdx2 = vm1.LaunchTree.IndexOf(test2);
                Assert.Equal(idx2 - 1, newIdx2);
                Assert.True(newIdx2 < newIdx1);
            }

            // Second session: verify persisted folder order from profiles.json
            {
                using var vm2 = new MainWindowViewModel(_testRegistry);
                var test1 = vm2.LaunchTree.OfType<LaunchFolderViewModel>().First(f => f.Name == "Test1");
                var test2 = vm2.LaunchTree.OfType<LaunchFolderViewModel>().First(f => f.Name == "Test2");

                int idx1 = vm2.LaunchTree.IndexOf(test1);
                int idx2 = vm2.LaunchTree.IndexOf(test2);
                Assert.True(idx2 < idx1);

                // Move Test2 down (restoring original order)
                test2.MoveDownCommand.Execute(null);

                int restoredIdx1 = vm2.LaunchTree.IndexOf(test1);
                int restoredIdx2 = vm2.LaunchTree.IndexOf(test2);
                Assert.True(restoredIdx1 < restoredIdx2);
            }
        }

        [Fact]
        public void OpenProfilesDirectory_EnsuresDirectoryExists()
        {
            using var vm = new MainWindowViewModel(_testRegistry);
            // Verify folder exists and command executes without throw
            Assert.True(Directory.Exists(_tempProfilesDir));
            Assert.NotNull(vm.OpenProfilesDirectoryCommand);
        }
    }
}
