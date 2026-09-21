// src/Gordian.App/ViewModels/MainWindowViewModel.cs
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Gordian.App.Common;
using Gordian.App.Services;
using Gordian.Core.Config;
using Gordian.Core.Diagnostics;
using Gordian.Core.Network;
using Gordian.Core.Network.LandSandBoat;
using Gordian.Core.Profiles;

namespace Gordian.App.ViewModels
{
    /// <summary>
    /// ViewModel for the primary launcher window, managing profile collection,
    /// form inputs, and launch commands without code-behind coupling.
    /// </summary>
    public sealed class MainWindowViewModel : ViewModelBase, IDisposable
    {
        private readonly SessionRegistry _sessionRegistry;
        private string? _editingOriginalProfileName;
        private string? _editingOriginalFolder;
        private string _formProfileName = string.Empty;
        private string _formCharacterName = string.Empty;
        private string _formFolder = string.Empty;
        private string _formServerHost = string.Empty;
        private int _formServerPort = 54231;
        private string _formBootloaderPath = string.Empty;
        private string _formArguments = string.Empty;
        private string _formUsername = string.Empty;
        private string _formPassword = string.Empty;
        private string _formOtpSeed = string.Empty;
        private string _newFolderName = string.Empty;
        private string _statusMessage = string.Empty;

        public ObservableCollection<ProfileItemViewModel> Profiles { get; } = new();
        public ObservableCollection<LaunchTreeNodeViewModel> LaunchTree { get; } = new();

        /// <summary>
        /// ViewModel driving the live network packet inspector tab.
        /// </summary>
        public PacketInspectorViewModel Inspector { get; } = new();

        /// <summary>
        /// ViewModel driving the character vitals, world state, and telemetry inspector tab.
        /// </summary>
        public StateInspectorViewModel StateInspector { get; } = new();

        /// <summary>
        /// ViewModel driving the live chat and communication window / tab.
        /// </summary>
        public ChatViewModel Chat { get; } = new();

        /// <summary>
        /// ViewModel driving the inventory, container items, equipment, and currencies tab.
        /// </summary>
        public InventoryViewModel Inventory { get; } = new();

        /// <summary>
        /// ViewModel driving the interactive character command console (CLI) tab.
        /// </summary>
        public CommandConsoleViewModel Console { get; } = new();

        /// <summary>
        /// ViewModel driving the cross-platform Controls & Input configuration and telemetry tab.
        /// </summary>
        public ControlsInputViewModel Controls { get; } = new();

        /// <summary>
        /// ViewModel driving the 3D viewport surface and graphics backend telemetry tab.
        /// </summary>
        public ViewportViewModel Viewport { get; } = new();

        private bool _showStateInspector = true;

        /// <summary>
        /// Gets or sets whether the diagnostic State Inspector tab is visible in the UI.
        /// Can be toggled or disabled for non-debug runtime builds.
        /// </summary>
        public bool ShowStateInspector
        {
            get => _showStateInspector;
            set => SetProperty(ref _showStateInspector, value);
        }

        public bool IsEditing => !string.IsNullOrEmpty(_editingOriginalProfileName);

        public string FormTitle => IsEditing ? $"Edit Profile: {_editingOriginalProfileName}" : "New Profile Properties";

        public string FormProfileName
        {
            get => _formProfileName;
            set => SetProperty(ref _formProfileName, value);
        }

        public string FormCharacterName
        {
            get => _formCharacterName;
            set => SetProperty(ref _formCharacterName, value);
        }

        public string FormFolder
        {
            get => _formFolder;
            set => SetProperty(ref _formFolder, value);
        }

        public string FormServerHost
        {
            get => _formServerHost;
            set => SetProperty(ref _formServerHost, value);
        }

        public int FormServerPort
        {
            get => _formServerPort;
            set => SetProperty(ref _formServerPort, value);
        }

        public string FormBootloaderPath
        {
            get => _formBootloaderPath;
            set => SetProperty(ref _formBootloaderPath, value);
        }

        public string FormArguments
        {
            get => _formArguments;
            set => SetProperty(ref _formArguments, value);
        }

        public string FormUsername
        {
            get => _formUsername;
            set => SetProperty(ref _formUsername, value);
        }

        public string FormPassword
        {
            get => _formPassword;
            set => SetProperty(ref _formPassword, value);
        }

        public string FormOtpSeed
        {
            get => _formOtpSeed;
            set => SetProperty(ref _formOtpSeed, value);
        }

        private LaunchTreeNodeViewModel? _selectedTreeNode;

        public LaunchTreeNodeViewModel? SelectedTreeNode
        {
            get => _selectedTreeNode;
            set => SetProperty(ref _selectedTreeNode, value);
        }

        public string NewFolderName
        {
            get => _newFolderName;
            set => SetProperty(ref _newFolderName, value);
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        private bool _isConfirmDeleteModalVisible;
        private string _confirmDeleteModalTitle = string.Empty;
        private string _confirmDeleteModalMessage = string.Empty;
        private ICommand? _confirmDeleteModalActionCommand;

        public bool IsConfirmDeleteModalVisible
        {
            get => _isConfirmDeleteModalVisible;
            set => SetProperty(ref _isConfirmDeleteModalVisible, value);
        }

        public string ConfirmDeleteModalTitle
        {
            get => _confirmDeleteModalTitle;
            set => SetProperty(ref _confirmDeleteModalTitle, value);
        }

        public string ConfirmDeleteModalMessage
        {
            get => _confirmDeleteModalMessage;
            set => SetProperty(ref _confirmDeleteModalMessage, value);
        }

        public ICommand? ConfirmDeleteModalActionCommand
        {
            get => _confirmDeleteModalActionCommand;
            set => SetProperty(ref _confirmDeleteModalActionCommand, value);
        }

        public ICommand CancelDeleteModalCommand { get; }
        public ICommand OpenProfilesDirectoryCommand { get; }

        public ICommand SaveProfileCommand { get; }
        public ICommand ClearFormCommand { get; }
        public ICommand LaunchSelectedCommand { get; }
        public ICommand TerminateAllCommand { get; }
        public ICommand SelectAllCommand { get; }
        public ICommand DeselectAllCommand { get; }
        public ICommand ExpandAllCommand { get; }
        public ICommand CollapseAllCommand { get; }
        public ICommand CreateFolderCommand { get; }
        public ICommand OpenViewportWindowCommand => Viewport.LaunchViewportCommand;

        public MainWindowViewModel(SessionRegistry? registry = null)
        {
            _sessionRegistry = registry ?? SessionRegistry.Default;

            CancelDeleteModalCommand = new RelayCommand(() => IsConfirmDeleteModalVisible = false);
            OpenProfilesDirectoryCommand = new RelayCommand(OpenProfilesDirectory);

            SaveProfileCommand = new RelayCommand(SaveProfile);
            ClearFormCommand = new RelayCommand(ClearForm);
            LaunchSelectedCommand = new RelayCommand(LaunchSelected);
            TerminateAllCommand = new RelayCommand(TerminateAll);
            SelectAllCommand = new RelayCommand(SelectAll);
            DeselectAllCommand = new RelayCommand(DeselectAll);
            ExpandAllCommand = new RelayCommand(ExpandAll);
            CollapseAllCommand = new RelayCommand(CollapseAll);
            CreateFolderCommand = new RelayCommand(CreateFolder);

            _sessionRegistry.SessionRegistered += OnSessionRegistryChanged;
            _sessionRegistry.SessionUnregistered += OnSessionRegistryChanged;

            Controls.SetSession(Console.SelectedSession);
            _sessionRegistry.SetPrimaryRenderingSession(Console.SelectedSession);
            Console.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(CommandConsoleViewModel.SelectedSession))
                {
                    Controls.SetSession(Console.SelectedSession);
                    _sessionRegistry.SetPrimaryRenderingSession(Console.SelectedSession);
                }
            };

            // Wire ViewportWindowManager to use our ViewportViewModel
            ViewportWindowManager.Default.SetPrimaryViewModel(Viewport);

            // Startup self-healing: restore any orphaned FFXiMain.dll.orig from ungraceful shutdowns
            ProxyStager.SelfHealStartup();

            LoadProfiles();
        }

        public void LoadProfiles()
        {
            Profiles.Clear();
            LaunchTree.Clear();
            string profilesDir = GordianStorage.ProfilesDirectory;

            var doc = ProfileStorageService.Load(profilesDir);

            // Recreate saved folders preserving their expansion states
            foreach (var folderEntry in doc.Folders)
            {
                var folderVm = GetOrCreateFolder(folderEntry.Path);
                folderVm.IsExpanded = folderEntry.IsExpanded;
            }

            // Add saved profiles in exact sequence
            foreach (var profile in doc.Profiles)
            {
                AddProfileToTree(profile);
            }

            if (Profiles.Count == 0)
            {
                CreateDefaultMockProfiles();
            }
            else
            {
                foreach (var node in LaunchTree)
                {
                    if (node is LaunchFolderViewModel folder)
                    {
                        folder.UpdateCheckStateFromChildren();
                    }
                }
            }
        }

        public void SelectAll()
        {
            foreach (var node in LaunchTree)
            {
                node.IsSelectedForLaunch = true;
            }
            SaveProfilesToStore();
        }

        public void DeselectAll()
        {
            foreach (var node in LaunchTree)
            {
                node.IsSelectedForLaunch = false;
            }
            SaveProfilesToStore();
        }

        public void ExpandAll()
        {
            SetExpandedRecursive(LaunchTree, true);
            SaveProfilesToStore();
        }

        public void CollapseAll()
        {
            SetExpandedRecursive(LaunchTree, false);
            SaveProfilesToStore();
        }

        private static void SetExpandedRecursive(IEnumerable<LaunchTreeNodeViewModel> nodes, bool isExpanded)
        {
            foreach (var node in nodes)
            {
                node.IsExpanded = isExpanded;
                if (node.Children != null)
                {
                    SetExpandedRecursive(node.Children, isExpanded);
                }
            }
        }

        public void CreateFolder()
        {
            LaunchFolderViewModel? targetParent = null;
            if (SelectedTreeNode is LaunchFolderViewModel folder)
            {
                targetParent = folder;
            }
            else if (SelectedTreeNode is ProfileItemViewModel profile)
            {
                targetParent = profile.Parent;
            }

            // If a custom name was typed in NewFolderName, create or resolve it; otherwise generate Windows Explorer-style default name
            if (!string.IsNullOrWhiteSpace(NewFolderName))
            {
                string inputPath = NewFolderName.Trim().Replace('\\', '/');
                NewFolderName = string.Empty;

                string fullPath = targetParent != null
                    ? $"{targetParent.FolderPath}/{inputPath}"
                    : inputPath;

                var createdFolder = GetOrCreateFolder(fullPath);
                createdFolder.IsExpanded = true;
                SelectedTreeNode = createdFolder;
                SaveProfilesToStore();
                StatusMessage = $"Created folder '{createdFolder.FolderPath}'.";
                return;
            }

            var siblings = targetParent != null ? targetParent.Children : LaunchTree;
            var existingNames = new HashSet<string>(
                siblings.OfType<LaunchFolderViewModel>().Select(f => f.Name),
                StringComparer.OrdinalIgnoreCase);

            string candidateName = "New Folder";
            if (existingNames.Contains(candidateName))
            {
                int counter = 1;
                while (existingNames.Contains($"New Folder {counter}"))
                {
                    counter++;
                }
                candidateName = $"New Folder {counter}";
            }

            string accumulatedPath = targetParent != null
                ? $"{targetParent.FolderPath}/{candidateName}"
                : candidateName;

            var newFolder = new LaunchFolderViewModel(candidateName, accumulatedPath, targetParent);
            newFolder.DeleteRequested += OnFolderDeleteRequested;
            newFolder.RenameCommitted += OnFolderRenameCommitted;
            newFolder.MoveUpRequested += OnFolderMoveUpRequested;
            newFolder.MoveDownRequested += OnFolderMoveDownRequested;

            if (targetParent != null)
            {
                targetParent.AddChild(newFolder);
                targetParent.IsExpanded = true;
            }
            else
            {
                LaunchTree.Add(newFolder);
            }

            SelectedTreeNode = newFolder;
            newFolder.BeginRename();
            SaveProfilesToStore();
            StatusMessage = $"Created folder '{candidateName}'. Press Enter to commit rename.";
        }

        public void MoveProfileToFolder(ProfileItemViewModel profile, LaunchFolderViewModel? targetFolder)
        {
            if (profile == null) return;

            // Check if already in target location
            if (profile.Parent == targetFolder) return;
            if (targetFolder == null && profile.Parent == null) return;

            string oldFilePath = Path.Combine(GordianStorage.ProfilesDirectory, profile.Profile.GetRelativeFilePath());

            // Detach from current parent
            if (profile.Parent != null)
            {
                profile.Parent.RemoveChild(profile);
            }
            else
            {
                LaunchTree.Remove(profile);
            }

            // Update folder path and save profile to disk
            string newFolderPath = targetFolder != null ? targetFolder.FolderPath : string.Empty;
            profile.Profile.Folder = newFolderPath;
            profile.Profile.SaveToFile(GordianStorage.ProfilesDirectory);

            // Clean up old file location if moved
            string newFilePath = Path.Combine(GordianStorage.ProfilesDirectory, profile.Profile.GetRelativeFilePath());
            if (!string.Equals(oldFilePath, newFilePath, StringComparison.OrdinalIgnoreCase) && File.Exists(oldFilePath))
            {
                try
                {
                    File.Delete(oldFilePath);
                }
                catch
                {
                    // Best-effort cleanup
                }
            }

            // Attach to new parent
            if (targetFolder != null)
            {
                targetFolder.AddChild(profile);
                targetFolder.IsExpanded = true;
            }
            else
            {
                LaunchTree.Add(profile);
            }

            SaveProfilesToStore();
            StatusMessage = $"Moved '{profile.ProfileName}' to {(targetFolder != null ? targetFolder.Name : "Root")}.";
        }

        public void MoveProfileRelative(ProfileItemViewModel dragged, ProfileItemViewModel target, bool insertAfter)
        {
            if (dragged == null || target == null || dragged == target) return;

            string oldFilePath = Path.Combine(GordianStorage.ProfilesDirectory, dragged.Profile.GetRelativeFilePath());

            // Remove from current position
            if (dragged.Parent != null)
            {
                dragged.Parent.RemoveChild(dragged);
            }
            else
            {
                LaunchTree.Remove(dragged);
            }

            // Determine target parent
            var targetParent = target.Parent;
            var targetList = targetParent != null ? targetParent.Children : LaunchTree;

            string newFolder = targetParent != null ? targetParent.FolderPath : string.Empty;
            dragged.Profile.Folder = newFolder;
            dragged.Profile.SaveToFile(GordianStorage.ProfilesDirectory);
            dragged.Parent = targetParent;

            // Clean up old file location if moved folders
            string newFilePath = Path.Combine(GordianStorage.ProfilesDirectory, dragged.Profile.GetRelativeFilePath());
            if (!string.Equals(oldFilePath, newFilePath, StringComparison.OrdinalIgnoreCase) && File.Exists(oldFilePath))
            {
                try
                {
                    File.Delete(oldFilePath);
                }
                catch { }
            }

            int targetIndex = targetList.IndexOf(target);
            if (targetIndex < 0)
            {
                targetList.Add(dragged);
            }
            else
            {
                int insertIndex = insertAfter ? targetIndex + 1 : targetIndex;
                if (insertIndex >= targetList.Count)
                {
                    targetList.Add(dragged);
                }
                else
                {
                    targetList.Insert(insertIndex, dragged);
                }
            }

            if (targetParent != null)
            {
                targetParent.IsExpanded = true;
                targetParent.UpdateCheckStateFromChildren();
            }

            SyncProfilesCollectionWithTree();
            SaveProfilesToStore();
            StatusMessage = $"Reordered '{dragged.ProfileName}' next to '{target.ProfileName}'.";
        }

        public void MoveProfileUp(ProfileItemViewModel profile)
        {
            if (profile == null) return;
            var list = profile.Parent != null ? profile.Parent.Children : LaunchTree;
            int index = list.IndexOf(profile);
            if (index > 0)
            {
                list.Move(index, index - 1);
                SyncProfilesCollectionWithTree();
                SaveProfilesToStore();
                StatusMessage = $"Moved '{profile.ProfileName}' up.";
            }
        }

        public void MoveProfileDown(ProfileItemViewModel profile)
        {
            if (profile == null) return;
            var list = profile.Parent != null ? profile.Parent.Children : LaunchTree;
            int index = list.IndexOf(profile);
            if (index >= 0 && index < list.Count - 1)
            {
                list.Move(index, index + 1);
                SyncProfilesCollectionWithTree();
                SaveProfilesToStore();
                StatusMessage = $"Moved '{profile.ProfileName}' down.";
            }
        }

        public void MoveFolderUp(LaunchFolderViewModel folder)
        {
            if (folder == null) return;
            var list = folder.Parent != null ? folder.Parent.Children : LaunchTree;
            int index = list.IndexOf(folder);
            if (index > 0)
            {
                list.Move(index, index - 1);
                SyncProfilesCollectionWithTree();
                SaveProfilesToStore();
                StatusMessage = $"Moved folder '{folder.Name}' up.";
            }
        }

        public void MoveFolderDown(LaunchFolderViewModel folder)
        {
            if (folder == null) return;
            var list = folder.Parent != null ? folder.Parent.Children : LaunchTree;
            int index = list.IndexOf(folder);
            if (index >= 0 && index < list.Count - 1)
            {
                list.Move(index, index + 1);
                SyncProfilesCollectionWithTree();
                SaveProfilesToStore();
                StatusMessage = $"Moved folder '{folder.Name}' down.";
            }
        }

        public void CopyProfile(ProfileItemViewModel sourceVm)
        {
            if (sourceVm == null) return;

            string baseName = sourceVm.ProfileName;
            string candidateName = $"{baseName} - Copy";
            int counter = 2;
            while (Profiles.Any(p => string.Equals(p.ProfileName, candidateName, StringComparison.OrdinalIgnoreCase)))
            {
                candidateName = $"{baseName} - Copy {counter++}";
            }

            var clonedProfile = sourceVm.Profile.Clone(candidateName);
            clonedProfile.SaveToFile(GordianStorage.ProfilesDirectory);

            var newVm = new ProfileItemViewModel(clonedProfile, _sessionRegistry);
            WireProfileEvents(newVm);

            // Insert immediately after sourceVm in the tree
            if (sourceVm.Parent != null)
            {
                int index = sourceVm.Parent.Children.IndexOf(sourceVm);
                if (index >= 0 && index + 1 <= sourceVm.Parent.Children.Count)
                {
                    newVm.Parent = sourceVm.Parent;
                    sourceVm.Parent.Children.Insert(index + 1, newVm);
                    sourceVm.Parent.UpdateCheckStateFromChildren();
                }
                else
                {
                    sourceVm.Parent.AddChild(newVm);
                }
            }
            else
            {
                int index = LaunchTree.IndexOf(sourceVm);
                if (index >= 0 && index + 1 <= LaunchTree.Count)
                {
                    LaunchTree.Insert(index + 1, newVm);
                }
                else
                {
                    LaunchTree.Add(newVm);
                }
            }

            // Insert into Profiles collection
            int profIndex = Profiles.IndexOf(sourceVm);
            if (profIndex >= 0 && profIndex + 1 <= Profiles.Count)
            {
                Profiles.Insert(profIndex + 1, newVm);
            }
            else
            {
                Profiles.Add(newVm);
            }

            SaveProfilesToStore();

            // Populate form with copied profile so user can immediately edit credentials
            OnProfileEditRequested(this, newVm);

            StatusMessage = $"Copied profile to '{candidateName}'. Modify credentials and click Save.";
        }

        private void OnFolderRenameCommitted(object? sender, string newName)
        {
            if (sender is not LaunchFolderViewModel folder) return;
            RenameFolder(folder, newName);
        }

        public void RenameFolder(LaunchFolderViewModel folder, string newName)
        {
            string cleanName = string.Concat(newName.Split(Path.GetInvalidFileNameChars())).Trim();
            if (string.IsNullOrWhiteSpace(cleanName))
            {
                folder.CancelRename();
                return;
            }

            string oldPath = folder.FolderPath;
            string parentPath = folder.Parent != null ? folder.Parent.FolderPath : string.Empty;
            string newPath = string.IsNullOrEmpty(parentPath) ? cleanName : $"{parentPath}/{cleanName}";

            if (string.Equals(oldPath, newPath, StringComparison.OrdinalIgnoreCase))
            {
                folder.Name = cleanName;
                return;
            }

            // Move directory on disk
            string oldDiskDir = Path.Combine(GordianStorage.ProfilesDirectory, oldPath.Replace('/', Path.DirectorySeparatorChar));
            string newDiskDir = Path.Combine(GordianStorage.ProfilesDirectory, newPath.Replace('/', Path.DirectorySeparatorChar));

            if (Directory.Exists(oldDiskDir))
            {
                try
                {
                    if (!Directory.Exists(newDiskDir))
                    {
                        Directory.Move(oldDiskDir, newDiskDir);
                    }
                }
                catch
                {
                    // Best-effort directory move
                }
            }

            folder.Name = cleanName;
            folder.FolderPath = newPath;

            // Recursively update child folders and profiles
            UpdateDescendantPaths(folder);
            SaveProfilesToStore();

            StatusMessage = $"Renamed folder to '{cleanName}'.";
        }

        private void UpdateDescendantPaths(LaunchFolderViewModel folder)
        {
            foreach (var child in folder.Children)
            {
                if (child is LaunchFolderViewModel sub)
                {
                    sub.FolderPath = $"{folder.FolderPath}/{sub.Name}";
                    UpdateDescendantPaths(sub);
                }
                else if (child is ProfileItemViewModel profile)
                {
                    profile.Profile.Folder = folder.FolderPath;
                    profile.Profile.SaveToFile(GordianStorage.ProfilesDirectory);
                }
            }
        }

        private ProfileItemViewModel AddProfileToTree(AccountProfile profile)
        {
            var vm = new ProfileItemViewModel(profile, _sessionRegistry);
            WireProfileEvents(vm);
            Profiles.Add(vm);

            if (string.IsNullOrWhiteSpace(profile.Folder))
            {
                LaunchTree.Add(vm);
            }
            else
            {
                var folder = GetOrCreateFolder(profile.Folder);
                folder.AddChild(vm);
            }

            return vm;
        }

        private LaunchFolderViewModel GetOrCreateFolder(string folderPath)
        {
            string[] parts = folderPath.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
            LaunchFolderViewModel? currentFolder = null;
            string accumulatedPath = string.Empty;

            foreach (var part in parts)
            {
                accumulatedPath = string.IsNullOrEmpty(accumulatedPath) ? part : $"{accumulatedPath}/{part}";
                ObservableCollection<LaunchTreeNodeViewModel> searchList =
                    currentFolder != null ? currentFolder.Children : LaunchTree;

                var existing = searchList.OfType<LaunchFolderViewModel>()
                    .FirstOrDefault(f => string.Equals(f.Name, part, StringComparison.OrdinalIgnoreCase));

                if (existing == null)
                {
                    existing = new LaunchFolderViewModel(part, accumulatedPath, currentFolder);
                    existing.DeleteRequested += OnFolderDeleteRequested;
                    existing.RenameCommitted += OnFolderRenameCommitted;
                    existing.MoveUpRequested += OnFolderMoveUpRequested;
                    existing.MoveDownRequested += OnFolderMoveDownRequested;
                    if (currentFolder != null)
                    {
                        currentFolder.AddChild(existing);
                    }
                    else
                    {
                        LaunchTree.Add(existing);
                    }
                }

                currentFolder = existing;
            }

            return currentFolder!;
        }

        private void RemoveNodeFromTree(LaunchTreeNodeViewModel node)
        {
            if (node.Parent != null)
            {
                node.Parent.RemoveChild(node);
            }
            else
            {
                LaunchTree.Remove(node);
            }
        }

        private void OnFolderDeleteRequested(object? sender, LaunchFolderViewModel folder)
        {
            int profileCount = folder.CountDescendantProfiles();
            if (profileCount == 0)
            {
                ExecuteDeleteFolderAndContents(folder);
                return;
            }

            ConfirmDeleteModalTitle = "Confirm Folder Deletion";
            ConfirmDeleteModalMessage = $"Folder \"{folder.Name}\" contains {profileCount} profile(s). All profiles within this folder will be deleted also.\n\nDo you wish to delete \"{folder.Name}\" and its {profileCount} profile(s)?";
            ConfirmDeleteModalActionCommand = new RelayCommand(() =>
            {
                ExecuteDeleteFolderAndContents(folder);
                IsConfirmDeleteModalVisible = false;
            });
            IsConfirmDeleteModalVisible = true;
        }

        public void ExecuteDeleteFolderAndContents(LaunchFolderViewModel folder)
        {
            var profilesToDelete = folder.GetDescendantProfiles();
            int count = profilesToDelete.Count;

            foreach (var profileItem in profilesToDelete)
            {
                profileItem.Terminate();
                profileItem.Profile.DeleteFile(GordianStorage.ProfilesDirectory);
                UnwireProfileEvents(profileItem);
                Profiles.Remove(profileItem);
            }

            folder.DeleteRequested -= OnFolderDeleteRequested;
            folder.RenameCommitted -= OnFolderRenameCommitted;
            folder.MoveUpRequested -= OnFolderMoveUpRequested;
            folder.MoveDownRequested -= OnFolderMoveDownRequested;
            RemoveNodeFromTree(folder);

            string folderDiskPath = Path.Combine(GordianStorage.ProfilesDirectory, folder.FolderPath.Replace('/', Path.DirectorySeparatorChar));
            if (Directory.Exists(folderDiskPath))
            {
                try
                {
                    Directory.Delete(folderDiskPath, true);
                }
                catch
                {
                    // Best-effort directory cleanup
                }
            }

            SaveProfilesToStore();

            StatusMessage = $"Deleted folder '{folder.Name}'{(count > 0 ? $" and {count} profile(s)" : "")}.";
        }

        private void OnFolderMoveUpRequested(object? sender, LaunchFolderViewModel folder)
        {
            MoveFolderUp(folder);
        }

        private void OnFolderMoveDownRequested(object? sender, LaunchFolderViewModel folder)
        {
            MoveFolderDown(folder);
        }

        public void OpenProfilesDirectory()
        {
            string dir = GordianStorage.ProfilesDirectory;
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            try
            {
                if (OperatingSystem.IsWindows())
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        Arguments = $"\"{dir}\"",
                        UseShellExecute = true
                    });
                }
                else if (OperatingSystem.IsMacOS())
                {
                    System.Diagnostics.Process.Start("open", $"\"{dir}\"");
                }
                else
                {
                    System.Diagnostics.Process.Start("xdg-open", $"\"{dir}\"");
                }
                StatusMessage = $"Opened profiles directory: {dir}";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Failed to open profiles directory: {ex.Message}";
            }
        }

        private void WireProfileEvents(ProfileItemViewModel vm)
        {
            vm.EditRequested += OnProfileEditRequested;
            vm.DeleteRequested += OnProfileDeleteRequested;
            vm.CopyRequested += OnProfileCopyRequested;
            vm.MoveUpRequested += OnProfileMoveUpRequested;
            vm.MoveDownRequested += OnProfileMoveDownRequested;
            vm.SelectionChanged += OnProfileSelectionChanged;
        }

        private void UnwireProfileEvents(ProfileItemViewModel vm)
        {
            vm.EditRequested -= OnProfileEditRequested;
            vm.DeleteRequested -= OnProfileDeleteRequested;
            vm.CopyRequested -= OnProfileCopyRequested;
            vm.MoveUpRequested -= OnProfileMoveUpRequested;
            vm.MoveDownRequested -= OnProfileMoveDownRequested;
            vm.SelectionChanged -= OnProfileSelectionChanged;
        }

        private void OnProfileSelectionChanged(object? sender, ProfileItemViewModel item)
        {
            SaveProfilesToStore();
        }

        private void OnProfileCopyRequested(object? sender, ProfileItemViewModel item)
        {
            CopyProfile(item);
        }

        private void OnProfileMoveUpRequested(object? sender, ProfileItemViewModel item)
        {
            MoveProfileUp(item);
        }

        private void OnProfileMoveDownRequested(object? sender, ProfileItemViewModel item)
        {
            MoveProfileDown(item);
        }

        private void SyncProfilesCollectionWithTree()
        {
            var treeProfiles = new List<ProfileItemViewModel>();
            CollectProfileViewModelsInTreeOrder(LaunchTree, treeProfiles);

            for (int i = 0; i < treeProfiles.Count; i++)
            {
                int curIndex = Profiles.IndexOf(treeProfiles[i]);
                if (curIndex >= 0 && curIndex != i)
                {
                    Profiles.Move(curIndex, i);
                }
            }
        }

        private static void CollectProfileViewModelsInTreeOrder(IEnumerable<LaunchTreeNodeViewModel> nodes, List<ProfileItemViewModel> list)
        {
            foreach (var node in nodes)
            {
                if (node is ProfileItemViewModel profile)
                {
                    list.Add(profile);
                }
                else if (node is LaunchFolderViewModel folder)
                {
                    CollectProfileViewModelsInTreeOrder(folder.Children, list);
                }
            }
        }

        public void SaveProfilesToStore()
        {
            var doc = new ProfileStoreDocument();
            CollectFolderEntries(LaunchTree, doc.Folders);
            CollectProfilesInTreeOrder(LaunchTree, doc.Profiles);
            ProfileStorageService.Save(GordianStorage.ProfilesDirectory, doc);
        }

        private static void CollectFolderEntries(IEnumerable<LaunchTreeNodeViewModel> nodes, List<ProfileFolderEntry> folders)
        {
            foreach (var node in nodes)
            {
                if (node is LaunchFolderViewModel folder)
                {
                    folders.Add(new ProfileFolderEntry(folder.FolderPath, folder.IsExpanded));
                    CollectFolderEntries(folder.Children, folders);
                }
            }
        }

        private static void CollectProfilesInTreeOrder(IEnumerable<LaunchTreeNodeViewModel> nodes, List<AccountProfile> profiles)
        {
            foreach (var node in nodes)
            {
                if (node is ProfileItemViewModel profileItem)
                {
                    profiles.Add(profileItem.Profile);
                }
                else if (node is LaunchFolderViewModel folder)
                {
                    CollectProfilesInTreeOrder(folder.Children, profiles);
                }
            }
        }

        private void OnProfileEditRequested(object? sender, ProfileItemViewModel item)
        {
            _editingOriginalProfileName = item.Profile.ProfileName;
            _editingOriginalFolder = item.Profile.Folder;
            FormProfileName = item.Profile.ProfileName;
            FormCharacterName = item.Profile.CharacterName;
            FormFolder = item.Profile.Folder;
            FormServerHost = item.Profile.ServerHost;
            FormServerPort = item.Profile.ServerPort > 0 ? item.Profile.ServerPort : 54231;
            FormBootloaderPath = item.Profile.BootloaderPath;
            FormArguments = item.Profile.Arguments;
            FormUsername = item.Profile.Username;
            FormPassword = item.Profile.Password;
            FormOtpSeed = item.Profile.OtpSeed;

            OnPropertyChanged(nameof(IsEditing));
            OnPropertyChanged(nameof(FormTitle));
            StatusMessage = $"Editing profile '{item.Profile.ProfileName}'. Modify fields and click Save.";
        }

        private void OnProfileDeleteRequested(object? sender, ProfileItemViewModel item)
        {
            // Terminate any live session before deleting
            item.Terminate();

            // Delete file from storage
            item.Profile.DeleteFile(GordianStorage.ProfilesDirectory);

            // Detach events and remove from collection
            UnwireProfileEvents(item);
            Profiles.Remove(item);
            RemoveNodeFromTree(item);

            SaveProfilesToStore();

            // If we were editing this profile, clear the form
            if (string.Equals(_editingOriginalProfileName, item.Profile.ProfileName, StringComparison.OrdinalIgnoreCase))
            {
                ClearForm();
            }

            StatusMessage = $"Deleted profile '{item.Profile.ProfileName}'.";
        }

        public void ClearForm()
        {
            _editingOriginalProfileName = null;
            _editingOriginalFolder = null;
            FormProfileName = string.Empty;
            FormCharacterName = string.Empty;
            FormFolder = string.Empty;
            FormServerHost = string.Empty;
            FormServerPort = 54231;
            FormBootloaderPath = string.Empty;
            FormArguments = string.Empty;
            FormUsername = string.Empty;
            FormPassword = string.Empty;
            FormOtpSeed = string.Empty;

            OnPropertyChanged(nameof(IsEditing));
            OnPropertyChanged(nameof(FormTitle));
            StatusMessage = string.Empty;
        }

        private void SaveProfile()
        {
            if (string.IsNullOrWhiteSpace(FormProfileName))
            {
                StatusMessage = "Profile name cannot be empty.";
                return;
            }

            string targetName = FormProfileName.Trim();
            string targetFolder = (FormFolder ?? string.Empty).Trim().Replace('\\', '/');

            // If renaming an existing profile or moving folder, clean up the old file
            if (!string.IsNullOrEmpty(_editingOriginalProfileName))
            {
                if (!string.Equals(_editingOriginalProfileName, targetName, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(_editingOriginalFolder ?? string.Empty, targetFolder, StringComparison.OrdinalIgnoreCase))
                {
                    var oldProfile = new AccountProfile
                    {
                        ProfileName = _editingOriginalProfileName,
                        Folder = _editingOriginalFolder ?? string.Empty
                    };
                    oldProfile.DeleteFile(GordianStorage.ProfilesDirectory);
                }
            }

            string lookupName = _editingOriginalProfileName ?? targetName;
            var existing = Profiles.FirstOrDefault(p =>
                string.Equals(p.ProfileName, lookupName, StringComparison.OrdinalIgnoreCase));

            bool isSelected = existing?.IsSelectedForLaunch == true;

            var profile = new AccountProfile
            {
                ProfileName = targetName,
                CharacterName = FormCharacterName.Trim(),
                Folder = targetFolder,
                ServerHost = FormServerHost.Trim(),
                ServerPort = FormServerPort > 0 ? FormServerPort : 54231,
                BootloaderPath = FormBootloaderPath.Trim(),
                Arguments = FormArguments.Trim(),
                Username = FormUsername.Trim(),
                Password = FormPassword,
                OtpSeed = FormOtpSeed.Trim(),
                IsSelectedForLaunch = existing != null ? isSelected : true
            };

            profile.SaveToFile(GordianStorage.ProfilesDirectory);

            if (existing != null)
            {
                int index = Profiles.IndexOf(existing);
                UnwireProfileEvents(existing);
                RemoveNodeFromTree(existing);

                var updatedVm = new ProfileItemViewModel(profile, _sessionRegistry);
                WireProfileEvents(updatedVm);
                Profiles[index] = updatedVm;

                if (string.IsNullOrWhiteSpace(profile.Folder))
                {
                    LaunchTree.Add(updatedVm);
                }
                else
                {
                    var folderVm = GetOrCreateFolder(profile.Folder);
                    folderVm.AddChild(updatedVm);
                }
            }
            else
            {
                AddProfileToTree(profile);
            }

            foreach (var node in LaunchTree)
            {
                if (node is LaunchFolderViewModel f)
                {
                    f.UpdateCheckStateFromChildren();
                }
            }

            SaveProfilesToStore();

            ClearForm();
            StatusMessage = $"Saved profile '{profile.ProfileName}'.";
        }

        private void LaunchSelected()
        {
            var rawProfiles = Profiles.Select(vm => vm.Profile).ToList();
            var targetsToLaunch = rawProfiles.Where(p => p.IsSelectedForLaunch).ToList();
            if (targetsToLaunch.Count == 0)
            {
                StatusMessage = "No profiles selected for launch.";
                return;
            }

            // Separate private server profiles (direct native LsbLoginClient) from retail profiles
            var directLsbProfiles = new List<AccountProfile>();
            var retailProfiles = new List<AccountProfile>();

            foreach (var p in targetsToLaunch)
            {
                if (IsLsbProfile(p))
                {
                    directLsbProfiles.Add(p);
                }
                else
                {
                    retailProfiles.Add(p);
                }
            }

            // 1. Launch LandSandBoat private server profiles natively via LsbLoginClient
            if (directLsbProfiles.Count > 0)
            {
                _ = Task.Run(async () =>
                {
                    var client = new LsbLoginClient();
                    client.PacketInspected += (s, entry) =>
                    {
                        Inspector.OnPacketInspected(s, entry);
                    };
                    client.StatusChanged += (s, msg) =>
                    {
                        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                        {
                            StatusMessage = msg;
                        });
                    };
                    foreach (var profile in directLsbProfiles)
                    {
                        if (_sessionRegistry.IsAccountActive(profile.Username) ||
                            _sessionRegistry.IsCharacterActive(profile.ProfileName) ||
                            (!string.IsNullOrWhiteSpace(profile.CharacterName) && _sessionRegistry.IsCharacterActive(profile.CharacterName)))
                        {
                            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                            {
                                StatusMessage = $"Profile '{profile.ProfileName}' ({profile.Username}) is already active in memory. Skipped.";
                            });
                            continue;
                        }

                        try
                        {
                            string serverHost = !string.IsNullOrWhiteSpace(profile.ServerHost)
                                ? profile.ServerHost
                                : ExtractServerHost(profile.Arguments);
                            int connectPort = profile.ServerPort > 0
                                ? profile.ServerPort
                                : ExtractPort(profile.Arguments, "--authport", LsbLoginClient.DefaultConnectPort);
                            int dataPort = ExtractPort(profile.Arguments, "--dataport", LsbLoginClient.DefaultDataPort);
                            int viewPort = ExtractPort(profile.Arguments, "--viewport", LsbLoginClient.DefaultViewPort);

                            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                            {
                                StatusMessage = $"[{profile.ProfileName}] Authenticating with LandSandBoat at {serverHost}:{connectPort}...";
                            });

                            string otp = !string.IsNullOrWhiteSpace(profile.OtpSeed) ? profile.CurrentTwoFactorCode : string.Empty;
                            string? targetCharName = !string.IsNullOrWhiteSpace(profile.CharacterName)
                                ? profile.CharacterName
                                : null;

                            var ticket = await client.LoginAndSelectAsync(
                                host: serverHost,
                                username: profile.Username,
                                password: profile.Password,
                                otp: otp,
                                connectPort: connectPort,
                                dataPort: dataPort,
                                viewPort: viewPort,
                                targetCharacterName: targetCharName
                            ).ConfigureAwait(false);

                            string resolvedCharName = !string.IsNullOrWhiteSpace(ticket.CharacterName)
                                ? ticket.CharacterName
                                : (!string.IsNullOrWhiteSpace(profile.CharacterName) ? profile.CharacterName : profile.ProfileName);

                            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                            {
                                StatusMessage = $"[{profile.ProfileName}] Character '{resolvedCharName}' selected (ID: {ticket.CharacterId}). Establishing game session to {ticket.ZoneIp}:{ticket.ZonePort}...";
                            });

                            // Create and register the character session in SessionRegistry
                            var netManager = new SessionNetworkManager(ticket.ZoneIp, ticket.ZonePort)
                            {
                                CharacterId = ticket.CharacterId,
                                CharacterName = resolvedCharName,
                                AccountName = profile.Username,
                                Ticket = ticket.SessionHash
                            };

                            // Initialize session Blowfish crypto key from LandSandBoat handshake
                            netManager.Parser.InitializeSessionCrypto(ticket.BlowfishKey);

                            var session = new CharacterSession(
                                netManager.CharacterName,
                                ticket.CharacterId,
                                profile.Username,
                                netManager
                            );

                            netManager.StateChanged += (s, state) =>
                            {
                                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                                {
                                    RefreshAllStatuses();
                                    StatusMessage = state switch
                                    {
                                        SessionState.ConnectingToGameServer => $"[{session.CharacterName}] Connecting UDP socket to {ticket.ZoneIp}:{ticket.ZonePort}...",
                                        SessionState.ExchangingCryptoKeys => $"[{session.CharacterName}] Handshaking (0x00A) with map server at {ticket.ZoneIp}:{ticket.ZonePort}...",
                                        SessionState.LoadingWorldData => $"[{session.CharacterName}] Loading zone world data...",
                                        SessionState.ActiveInWorld => $"[{session.CharacterName}] Connected! In-game session active in world.",
                                        SessionState.Disconnected => $"[{session.CharacterName}] Session disconnected.",
                                        _ => StatusMessage
                                    };
                                });
                            };

                            netManager.ZoneTransitionStarted += (targetIp, targetPort) =>
                            {
                                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                                {
                                    StatusMessage = $"[{session.CharacterName}] Crossing zoneline... Transitioning to map server {targetIp}:{targetPort}...";
                                });
                            };

                            _sessionRegistry.RegisterSession(session);

                            // Connect UDP socket and transmit 0x00A login handshake
                            await netManager.ConnectAsync().ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            GordianLog.Error("SESSION", $"Connection error for profile '{profile.ProfileName}'", ex);
                            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                            {
                                StatusMessage = $"[{profile.ProfileName}] Connection error: {ex.Message}";
                                RefreshAllStatuses();
                            });
                        }
                    }
                });
            }

            // 2. Retail bootloader / Proxy staging flow (preserved for retail POL)
            if (retailProfiles.Count > 0)
            {
                string? gameDir = GameDirectoryDetector.DetectGameDirectory();
                if (string.IsNullOrWhiteSpace(gameDir))
                {
                    foreach (var p in retailProfiles)
                    {
                        if (!string.IsNullOrWhiteSpace(p.BootloaderPath))
                        {
                            string? parent = Path.GetDirectoryName(p.BootloaderPath);
                            if (parent != null)
                            {
                                string candidate = Path.GetFullPath(Path.Combine(parent, "..", "SquareEnix", "FINAL FANTASY XI"));
                                if (Directory.Exists(candidate) && File.Exists(Path.Combine(candidate, "FFXiMain.dll")))
                                {
                                    gameDir = candidate;
                                    break;
                                }
                            }
                        }
                    }
                }

                bool staged = false;
                if (!string.IsNullOrWhiteSpace(gameDir))
                {
                    staged = ProxyStager.StageProxy(gameDir);
                    if (staged)
                    {
                        StatusMessage = $"Proxy staged in '{Path.GetFileName(gameDir)}'. Spawning bootloader...";
                    }
                }

                int launched = LaunchOrchestrator.LaunchSelectedProfiles(retailProfiles, _sessionRegistry);
                StatusMessage = $"Launched {launched} character profile(s). Awaiting handoff...";
                RefreshAllStatuses();

                if (staged && !string.IsNullOrWhiteSpace(gameDir))
                {
                    string stagedDir = gameDir;
                    _ = Task.Run(async () =>
                    {
                        int waitMs = 0;
                        const int maxWaitMs = 30000;
                        const int stepMs = 500;
                        int initialSessionCount = _sessionRegistry.ActiveSessions.Count;

                        while (waitMs < maxWaitMs)
                        {
                            await Task.Delay(stepMs).ConfigureAwait(false);
                            waitMs += stepMs;

                            if (_sessionRegistry.ActiveSessions.Count > initialSessionCount)
                            {
                                await Task.Delay(500).ConfigureAwait(false);
                                break;
                            }
                        }

                        bool restored = ProxyStager.RestoreOriginal(stagedDir);
                        if (restored)
                        {
                            StatusMessage = "Original FFXiMain.dll restored. Client running.";
                        }
                    });
                }
            }
        }

        private static bool IsLsbProfile(AccountProfile profile)
        {
            if (profile == null) return false;
            if (!string.IsNullOrWhiteSpace(profile.ServerHost)) return true;
            string args = profile.Arguments ?? string.Empty;
            string bootloader = profile.BootloaderPath ?? string.Empty;

            // Detection criteria for LandSandBoat / private servers:
            // 1. Arguments contain --server or 127.0.0.1 or localhost
            // 2. Bootloader is xiloader.exe
            if (bootloader.Contains("xiloader", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            if (args.Contains("127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
                args.Contains("localhost", StringComparison.OrdinalIgnoreCase) ||
                args.Contains("--server", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            return false;
        }

        private static string ExtractServerHost(string? arguments)
        {
            if (string.IsNullOrWhiteSpace(arguments)) return "127.0.0.1";

            string[] tokens = arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            for (int i = 0; i < tokens.Length; i++)
            {
                if (string.Equals(tokens[i], "--server", StringComparison.OrdinalIgnoreCase) && i + 1 < tokens.Length)
                {
                    return tokens[i + 1];
                }
                if (string.Equals(tokens[i], "-s", StringComparison.OrdinalIgnoreCase) && i + 1 < tokens.Length)
                {
                    return tokens[i + 1];
                }
            }

            return "127.0.0.1";
        }

        private static int ExtractPort(string? arguments, string paramName, int defaultPort)
        {
            if (string.IsNullOrWhiteSpace(arguments)) return defaultPort;

            string[] tokens = arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            for (int i = 0; i < tokens.Length; i++)
            {
                if (string.Equals(tokens[i], paramName, StringComparison.OrdinalIgnoreCase) && i + 1 < tokens.Length)
                {
                    if (int.TryParse(tokens[i + 1], out int port) && port > 0 && port <= 65535)
                    {
                        return port;
                    }
                }
            }

            return defaultPort;
        }

        private void TerminateAll()
        {
            int count = _sessionRegistry.ActiveSessions.Count;
            _sessionRegistry.Clear();
            StatusMessage = $"Terminated {count} active session(s).";
            RefreshAllStatuses();
        }

        public void RefreshAllStatuses()
        {
            foreach (var item in Profiles)
            {
                item.RefreshOnlineStatus();
            }
        }

        private void OnSessionRegistryChanged(object? sender, CharacterSession session)
        {
            RefreshAllStatuses();
            Controls.SetSession(Console.SelectedSession);
            if (Console.SelectedSession != null)
            {
                _sessionRegistry.SetPrimaryRenderingSession(Console.SelectedSession);
            }
        }

        private void CreateDefaultMockProfiles()
        {
            string profilesDir = GordianStorage.ProfilesDirectory;

            var mainChar = new AccountProfile
            {
                ProfileName = "JimmyMain (Retail Target)",
                CharacterName = "Jimmy",
                Folder = string.Empty,
                Username = "jimmy_war",
                Password = "SuperSecurePassword123",
                OtpSeed = "HXDMVJECJJWSRB3D",
                BootloaderPath = Path.Combine("C:", "Program Files (x86)", "PlayOnline", "SquareEnix", "PlayOnlineViewer", "pol.exe"),
                Arguments = "--server official.retail --windowed",
                IsSelectedForLaunch = true
            };

            var muleChar = new AccountProfile
            {
                ProfileName = "CraftMule (Private Target)",
                CharacterName = "Crafty",
                Folder = "Test1",
                Username = "jimmy_mule",
                Password = "MulePassword456",
                ServerHost = "127.0.0.1",
                ServerPort = 54231,
                IsSelectedForLaunch = true
            };

            var af00Char = new AccountProfile
            {
                ProfileName = "AF00",
                CharacterName = "AF00",
                Folder = "Test1",
                Username = "af00_user",
                Password = "AFPassword123",
                ServerHost = "127.0.0.1",
                ServerPort = 54231,
                IsSelectedForLaunch = true
            };

            var hs00Char = new AccountProfile
            {
                ProfileName = "HS00",
                CharacterName = "HS00",
                Folder = "Test2/Test3",
                Username = "hs00_user",
                Password = "HSPassword456",
                ServerHost = "127.0.0.1",
                ServerPort = 54231,
                IsSelectedForLaunch = true
            };

            var lp00Char = new AccountProfile
            {
                ProfileName = "LP00-QA",
                CharacterName = "LP00-QA",
                Folder = string.Empty,
                Username = "lp00_user",
                Password = "LPPassword789",
                ServerHost = "127.0.0.1",
                ServerPort = 54231,
                IsSelectedForLaunch = false
            };

            mainChar.SaveToFile(profilesDir);
            muleChar.SaveToFile(profilesDir);
            af00Char.SaveToFile(profilesDir);
            hs00Char.SaveToFile(profilesDir);
            lp00Char.SaveToFile(profilesDir);

            AddProfileToTree(mainChar);
            AddProfileToTree(muleChar);
            AddProfileToTree(af00Char);
            AddProfileToTree(hs00Char);
            AddProfileToTree(lp00Char);

            foreach (var node in LaunchTree)
            {
                if (node is LaunchFolderViewModel f)
                {
                    f.UpdateCheckStateFromChildren();
                }
            }

            SaveProfilesToStore();
        }

        public void Dispose()
        {
            _sessionRegistry.SessionRegistered -= OnSessionRegistryChanged;
            _sessionRegistry.SessionUnregistered -= OnSessionRegistryChanged;
            Inspector.Dispose();
            StateInspector.Dispose();
            Chat.Dispose();
            Inventory.Dispose();
            Console.Dispose();
        }
    }
}
