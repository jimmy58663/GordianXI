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
        private string _formProfileName = string.Empty;
        private string _formBootloaderPath = string.Empty;
        private string _formArguments = string.Empty;
        private string _formUsername = string.Empty;
        private string _formPassword = string.Empty;
        private string _formOtpSeed = string.Empty;
        private string _statusMessage = string.Empty;

        public ObservableCollection<ProfileItemViewModel> Profiles { get; } = new();

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
        public ICommand LaunchSelectedCommand { get; }

        public MainWindowViewModel(SessionRegistry? registry = null)
        {
            _sessionRegistry = registry ?? SessionRegistry.Default;

            SaveProfileCommand = new RelayCommand(SaveProfile);
            LaunchSelectedCommand = new RelayCommand(LaunchSelected);

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
                        Profiles.Add(new ProfileItemViewModel(profile, _sessionRegistry));
                    }
                }
            }

            if (Profiles.Count == 0)
            {
                CreateDefaultMockProfiles();
            }
        }

        private void SaveProfile()
        {
            if (string.IsNullOrWhiteSpace(FormProfileName))
            {
                StatusMessage = "Profile name cannot be empty.";
                return;
            }

            var profile = new AccountProfile
            {
                ProfileName = FormProfileName.Trim(),
                BootloaderPath = FormBootloaderPath.Trim(),
                Arguments = FormArguments.Trim(),
                Username = FormUsername.Trim(),
                Password = FormPassword,
                OtpSeed = FormOtpSeed.Trim(),
                IsSelectedForLaunch = true
            };

            profile.SaveToFile(GordianStorage.ProfilesDirectory);

            // Update existing or add new
            var existing = Profiles.FirstOrDefault(p =>
                string.Equals(p.ProfileName, profile.ProfileName, StringComparison.OrdinalIgnoreCase));

            if (existing != null)
            {
                int index = Profiles.IndexOf(existing);
                Profiles[index] = new ProfileItemViewModel(profile, _sessionRegistry);
            }
            else
            {
                Profiles.Add(new ProfileItemViewModel(profile, _sessionRegistry));
            }

            StatusMessage = $"Saved profile '{profile.ProfileName}'.";

            // Clear form
            FormProfileName = string.Empty;
            FormBootloaderPath = string.Empty;
            FormArguments = string.Empty;
            FormUsername = string.Empty;
            FormPassword = string.Empty;
            FormOtpSeed = string.Empty;
        }

        private void LaunchSelected()
        {
            var rawProfiles = Profiles.Select(vm => vm.Profile).ToList();
            int launched = LaunchOrchestrator.LaunchSelectedProfiles(rawProfiles, _sessionRegistry);

            StatusMessage = $"Launched {launched} character profile(s).";
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

            Profiles.Add(new ProfileItemViewModel(mainChar, _sessionRegistry));
            Profiles.Add(new ProfileItemViewModel(muleChar, _sessionRegistry));
        }

        public void Dispose()
        {
            _sessionRegistry.SessionRegistered -= OnSessionRegistryChanged;
            _sessionRegistry.SessionUnregistered -= OnSessionRegistryChanged;
        }
    }
}
