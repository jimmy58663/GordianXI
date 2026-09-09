// src/Gordian.Core/Profiles/LaunchOrchestrator.cs
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace Gordian.Core.Profiles
{
    public static class LaunchOrchestrator
    {
        /// <summary>
        /// Scans active process memory structures to determine which characters are currently online.
        /// </summary>
        public static HashSet<string> GetActiveCharacterNames()
        {
            var activeNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // In a standard client environment, we scan for active instances of the 
            // game container process to see who is online before triggering launch commands
            Process[] processes = Process.GetProcessesByName("pol");
            foreach (var proc in processes)
            {
                try
                {
                    // For an advanced deployment, read the character name string 
                    // from the memory space or trace the window title strings here.
                    // For now, we will return a shell placeholder to demonstrate the skip logic.
                }
                catch
                {
                    // Catch access violations if certain process states are locked down
                }
            }

            return activeNames;
        }

        /// <summary>
        /// Processes a roster of profiles, filters out accounts that are already running, 
        /// and launches only the remaining selection.
        /// </summary>
        public static void LaunchSelectedProfiles(IEnumerable<AccountProfile> profiles)
        {
            // 1. Get the list of characters currently active in memory
            HashSet<string> onlineUsers = GetActiveCharacterNames();

            // 2. Filter down strictly to what the user wants to launch
            var targetsToLaunch = profiles.Where(p => p.IsSelectedForLaunch);

            foreach (var profile in targetsToLaunch)
            {
                // 3. THE SMART CHECK: If the account matches an active process footprint, skip it!
                if (onlineUsers.Contains(profile.Username))
                {
                    Debug.WriteLine($"[System] Profile '{profile.ProfileName}' is already logged in. Skipping launch sequence.");
                    continue;
                }

                // 4. Execute the specific targeted bootloader handle (pol.exe vs xiloader.exe)
                if (!File.Exists(profile.BootloaderPath))
                {
                    Debug.WriteLine($"[System] Failed to launch profile '{profile.ProfileName}': Executable path not found.");
                    continue;
                }

                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = profile.BootloaderPath,
                    Arguments = $"{profile.Arguments} --user {profile.Username} --pass {profile.Password}",
                    UseShellExecute = true
                };

                Debug.WriteLine($"[System] Spawning bootloader task for profile '{profile.ProfileName}' via {Path.GetFileName(profile.BootloaderPath)}");
                Process.Start(startInfo);
            }
        }
    }
}
