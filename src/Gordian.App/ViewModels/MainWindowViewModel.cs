// src/Gordian.App/ViewModels/MainWindowViewModel.cs
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows.Input;
using Gordian.App.Common;
using Gordian.Core.Config;
using Gordian.Core.Network;
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
        private string _formProfileName = string.Empty;
        private string _formBootloaderPath = string.Empty;
        private string _formArguments = string.Empty;
        private string _formUsername = string.Empty;
        private string _formPassword = string.Empty;
        private string _formOtpSeed = string.Empty;
        private string _statusMessage = string.Empty;

        public ObservableCollection<ProfileItemViewModel> Profiles { get; } = new();

        /// <summary>
        /// ViewModel driving the live network packet inspector tab.
        /// </summary>
        public PacketInspectorViewModel Inspector { get; } = new();

        public bool IsEditing => !string.IsNullOrEmpty(_editingOriginalProfileName);

        public string FormTitle => IsEditing ? $"Edit Profile: {_editingOriginalProfileName}" : "New Profile Properties";

        public string FormProfileName
        {
            get => _formProfileName;
            set => SetProperty(ref _formProfileName, value);
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

        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        public ICommand SaveProfileCommand { get; }
        public ICommand ClearFormCommand { get; }
        public ICommand LaunchSelectedCommand { get; }
        public ICommand TerminateAllCommand { get; }

        public MainWindowViewModel(SessionRegistry? registry = null)
        {
            _sessionRegistry = registry ?? SessionRegistry.Default;

            SaveProfileCommand = new RelayCommand(SaveProfile);
            ClearFormCommand = new RelayCommand(ClearForm);
            LaunchSelectedCommand = new RelayCommand(LaunchSelected);
            TerminateAllCommand = new RelayCommand(TerminateAll);

            _sessionRegistry.SessionRegistered += OnSessionRegistryChanged;
            _sessionRegistry.SessionUnregistered += OnSessionRegistryChanged;

            LoadProfiles();
        }

        public void LoadProfiles()
        {
            Profiles.Clear();
            string profilesDir = GordianStorage.ProfilesDirectory;

            if (Directory.Exists(profilesDir))
            {
                var files = Directory.GetFiles(profilesDir, "*.json");
                foreach (var file in files)
                {
                    var profile = AccountProfile.LoadFromFile(file);
                    if (profile != null)
                    {
                        profile.IsSelectedForLaunch = true;
                        AddProfileViewModel(profile);
                    }
                }
            }

            if (Profiles.Count == 0)
            {
                CreateDefaultMockProfiles();
            }
        }

        private void AddProfileViewModel(AccountProfile profile)
        {
            var vm = new ProfileItemViewModel(profile, _sessionRegistry);
            vm.EditRequested += OnProfileEditRequested;
            vm.DeleteRequested += OnProfileDeleteRequested;
            Profiles.Add(vm);
        }

        private void OnProfileEditRequested(object? sender, ProfileItemViewModel item)
        {
            _editingOriginalProfileName = item.Profile.ProfileName;
            FormProfileName = item.Profile.ProfileName;
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
            item.EditRequested -= OnProfileEditRequested;
            item.DeleteRequested -= OnProfileDeleteRequested;
            Profiles.Remove(item);

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
            FormProfileName = string.Empty;
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

            // If renaming an existing profile, delete the old file
            if (!string.IsNullOrEmpty(_editingOriginalProfileName) &&
                !string.Equals(_editingOriginalProfileName, targetName, StringComparison.OrdinalIgnoreCase))
            {
                var oldProfile = new AccountProfile { ProfileName = _editingOriginalProfileName };
                oldProfile.DeleteFile(GordianStorage.ProfilesDirectory);
            }

            var profile = new AccountProfile
            {
                ProfileName = targetName,
                BootloaderPath = FormBootloaderPath.Trim(),
                Arguments = FormArguments.Trim(),
                Username = FormUsername.Trim(),
                Password = FormPassword,
                OtpSeed = FormOtpSeed.Trim(),
                IsSelectedForLaunch = true
            };

            profile.SaveToFile(GordianStorage.ProfilesDirectory);

            // Update existing or add new
            string lookupName = _editingOriginalProfileName ?? targetName;
            var existing = Profiles.FirstOrDefault(p =>
                string.Equals(p.ProfileName, lookupName, StringComparison.OrdinalIgnoreCase));

            if (existing != null)
            {
                int index = Profiles.IndexOf(existing);
                existing.EditRequested -= OnProfileEditRequested;
                existing.DeleteRequested -= OnProfileDeleteRequested;

                var updatedVm = new ProfileItemViewModel(profile, _sessionRegistry);
                updatedVm.EditRequested += OnProfileEditRequested;
                updatedVm.DeleteRequested += OnProfileDeleteRequested;
                Profiles[index] = updatedVm;
            }
            else
            {
                AddProfileViewModel(profile);
            }

            ClearForm();
            StatusMessage = $"Saved profile '{profile.ProfileName}'.";
        }

        private void LaunchSelected()
        {
            var rawProfiles = Profiles.Select(vm => vm.Profile).ToList();
            int launched = LaunchOrchestrator.LaunchSelectedProfiles(rawProfiles, _sessionRegistry);

            StatusMessage = $"Launched {launched} character profile(s).";
            RefreshAllStatuses();
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
        }

        private void CreateDefaultMockProfiles()
        {
            string profilesDir = GordianStorage.ProfilesDirectory;

            var mainChar = new AccountProfile
            {
                ProfileName = "JimmyMain (Retail Target)",
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
                Username = "jimmy_mule",
                Password = "MulePassword456",
                BootloaderPath = Path.Combine("C:", "GordianXI", "xiloader.exe"),
                Arguments = "--server 127.0.0.1 --hairpin",
                IsSelectedForLaunch = true
            };

            mainChar.SaveToFile(profilesDir);
            muleChar.SaveToFile(profilesDir);

            AddProfileViewModel(mainChar);
            AddProfileViewModel(muleChar);
        }

        public void Dispose()
        {
            _sessionRegistry.SessionRegistered -= OnSessionRegistryChanged;
            _sessionRegistry.SessionUnregistered -= OnSessionRegistryChanged;
            Inspector.Dispose();
        }
    }
}
