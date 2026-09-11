// src/Gordian.Core/Profiles/LaunchOrchestrator.cs
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Gordian.Core.Network;

namespace Gordian.Core.Profiles
{
    public static class LaunchOrchestrator
    {
        /// <summary>
        /// Retrieves the list of character and account names currently active in memory.
        /// Queries the central SessionRegistry rather than polling operating system processes.
        /// </summary>
        public static HashSet<string> GetActiveCharacterNames(SessionRegistry? registry = null)
        {
            var reg = registry ?? SessionRegistry.Default;
            var activeNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var session in reg.ActiveSessions)
            {
                if (session.State != SessionState.Disconnected)
                {
                    if (!string.IsNullOrWhiteSpace(session.CharacterName))
                    {
                        activeNames.Add(session.CharacterName);
                    }
                    if (!string.IsNullOrWhiteSpace(session.AccountUsername))
                    {
                        activeNames.Add(session.AccountUsername);
                    }
                }
            }

            return activeNames;
        }

        /// <summary>
        /// Processes a roster of profiles, filters out accounts/characters that are already active in memory,
        /// and launches only the remaining selected profiles.
        /// </summary>
        /// <param name="profiles">The list of account profiles to evaluate.</param>
        /// <param name="registry">Optional custom SessionRegistry for testing or dependency injection.</param>
        /// <returns>The number of profiles successfully launched.</returns>
        public static int LaunchSelectedProfiles(IEnumerable<AccountProfile> profiles, SessionRegistry? registry = null)
        {
            var reg = registry ?? SessionRegistry.Default;
            HashSet<string> onlineUsers = GetActiveCharacterNames(reg);

            var targetsToLaunch = profiles.Where(p => p.IsSelectedForLaunch);
            int launchedCount = 0;

            foreach (var profile in targetsToLaunch)
            {
                // Smart check: If the profile username or character name is already active in memory, skip it!
                if (onlineUsers.Contains(profile.Username) || onlineUsers.Contains(profile.ProfileName))
                {
                    Debug.WriteLine($"[System] Profile '{profile.ProfileName}' ({profile.Username}) is already active in memory. Skipping launch sequence.");
                    continue;
                }

                if (!File.Exists(profile.BootloaderPath))
                {
                    Debug.WriteLine($"[System] Failed to launch profile '{profile.ProfileName}': Executable path not found at '{profile.BootloaderPath}'.");
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
                launchedCount++;
            }

            return launchedCount;
        }
    }
}
