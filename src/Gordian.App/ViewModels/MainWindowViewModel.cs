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

            // Startup self-healing: restore any orphaned FFXiMain.dll.orig from ungraceful shutdowns
            ProxyStager.SelfHealStartup();

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
                    foreach (var profile in directLsbProfiles)
                    {
                        if (_sessionRegistry.IsAccountActive(profile.Username) || _sessionRegistry.IsCharacterActive(profile.ProfileName))
                        {
                            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                            {
                                StatusMessage = $"Profile '{profile.ProfileName}' ({profile.Username}) is already active in memory. Skipped.";
                            });
                            continue;
                        }

                        try
                        {
                            string serverHost = ExtractServerHost(profile.Arguments);
                            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                            {
                                StatusMessage = $"[{profile.ProfileName}] Authenticating with LandSandBoat at {serverHost}...";
                            });

                            string otp = !string.IsNullOrWhiteSpace(profile.OtpSeed) ? profile.CurrentTwoFactorCode : string.Empty;
                            var ticket = await client.LoginAndSelectAsync(
                                host: serverHost,
                                username: profile.Username,
                                password: profile.Password,
                                otp: otp,
                                targetCharacterName: profile.ProfileName
                            ).ConfigureAwait(false);

                            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                            {
                                StatusMessage = $"[{profile.ProfileName}] Character selected (ID: {ticket.CharacterId}). Establishing game session to {ticket.ZoneIp}:{ticket.ZonePort}...";
                            });

                            // Create and register the character session in SessionRegistry
                            var netManager = new SessionNetworkManager(ticket.ZoneIp, ticket.ZonePort)
                            {
                                CharacterId = ticket.CharacterId,
                                CharacterName = !string.IsNullOrWhiteSpace(ticket.CharacterName) ? ticket.CharacterName : profile.ProfileName,
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

                            _sessionRegistry.RegisterSession(session);

                            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                            {
                                RefreshAllStatuses();
                                StatusMessage = $"[{session.CharacterName}] Connected! Session active in world.";
                            });

                            // Connect UDP socket and transmit 0x00A login handshake
                            await netManager.ConnectAsync().ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
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
