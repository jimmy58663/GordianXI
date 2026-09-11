// src/Gordian.App/MainWindow.axaml.cs
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Gordian.Core.Profiles;
using Gordian.Core.Network;

namespace Gordian.App
{
    public partial class MainWindow : Window
    {
        // Use an ObservableCollection to bind data directly to our UI checklist container
        public ObservableCollection<AccountProfile> Profiles { get; } = new ObservableCollection<AccountProfile>();

        private readonly string _profilesDirectory;

        private readonly HandoffPipeServer _ipcServer;

        public MainWindow()
        {
            InitializeComponent();

            // Set up a dedicated local storage directory for our profile JSON files
            _profilesDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "profiles");
            if (!Directory.Exists(_profilesDirectory))
            {
                Directory.CreateDirectory(_profilesDirectory);
            }

            // Initialize and activate our Named Pipe IPC server
            _ipcServer = new HandoffPipeServer();
            _ipcServer.SessionReceived += OnSessionTokenIntercepted;
            _ipcServer.Start();

            DataContext = this;
            LoadAllProfilesFromDisk();
        }

        /// <summary>
        /// Scans our local storage folder and hydrates our checklist loop arrays.
        /// </summary>
        private void LoadAllProfilesFromDisk()
        {
            Profiles.Clear();
            var jsonFiles = Directory.GetFiles(_profilesDirectory, "*.json");

            foreach (var file in jsonFiles)
            {
                var profile = AccountProfile.LoadFromFile(file);
                if (profile != null)
                {
                    // Default to checked so it's ready for immediate launching
                    profile.IsSelectedForLaunch = true;
                    Profiles.Add(profile);
                }
            }

            // Mock an initial configuration if the directory is completely empty
            if (Profiles.Count == 0)
            {
                CreateDefaultMockProfiles();
            }
        }

        private void CreateDefaultMockProfiles()
        {
            var mainChar = new AccountProfile
            {
                ProfileName = "JimmyMain (Retail Target)",
                Username = "jimmy_war",
                Password = "SuperSecurePassword123",
                OtpSeed = "HXDMVJECJJWSRB3D", // Example 2FA Seed
                BootloaderPath = @"C:\Program Files (x86)\PlayOnline\SquareEnix\PlayOnlineViewer\pol.exe",
                Arguments = "--server official.retail --windowed"
            };

            var muleChar = new AccountProfile
            {
                ProfileName = "CraftMule (Private Target)",
                Username = "jimmy_mule",
                Password = "MulePassword456",
                BootloaderPath = @"C:\GordianXI\xiloader.exe", // Custom targeted launcher path
                Arguments = "--server 127.0.0.1 --hairpin"
            };

            mainChar.SaveToFile(_profilesDirectory);
            muleChar.SaveToFile(_profilesDirectory);

            Profiles.Add(mainChar);
            Profiles.Add(muleChar);
        }

        /// <summary>
        /// Fires when the user clicks the master "Launch Selected Characters" action bar.
        /// </summary>
        private void OnLaunchSelectedButtonClick(object sender, RoutedEventArgs e)
        {
            // Trigger our smart skip manager logic to filter running processes
            LaunchOrchestrator.LaunchSelectedProfiles(Profiles);

            // Refresh our UI indicators to accurately match the newly modified environment states
            LoadAllProfilesFromDisk();
        }

        /// <summary>
        /// Fires automatically when the 32-bit proxy pipes real login keys back into this master process window.
        /// </summary>
        private void OnSessionTokenIntercepted(object? sender, SessionHandoffArgs e)
        {
            // Marshall onto the Avalonia UI thread safely
            Avalonia.Threading.Dispatcher.UIThread.Post(async () =>
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[NET_TRACE] Secure handoff caught for character: {e.TargetCharacterName} | Target Server: {e.ServerIp}:{e.ServerPort}"
                );

                // Register session in the central registry and begin asynchronous connection
                var session = SessionRegistry.Default.CreateAndRegisterSession(e);
                try
                {
                    await session.NetworkManager.ConnectAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[NET_TRACE] Failed to connect character session for '{e.TargetCharacterName}': {ex.Message}");
                }
            });
        }

        // Ensure you clean up resource handles when the desktop dashboard terminates
        protected override void OnUnloaded(RoutedEventArgs e)
        {
            _ipcServer.SessionReceived -= OnSessionTokenIntercepted;
            _ipcServer.Dispose();
            SessionRegistry.Default.Clear();
            base.OnUnloaded(e);
        }
    }
}
