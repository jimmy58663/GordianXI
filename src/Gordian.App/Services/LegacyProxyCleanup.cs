// src/Gordian.App/Services/LegacyProxyCleanup.cs
using System;
using System.IO;
using Gordian.Core.Diagnostics;

namespace Gordian.App.Services
{
    /// <summary>
    /// Repairs an install the removed proxy launch path left behind. That path renamed the genuine
    /// <c>FFXiMain.dll</c> to <c>FFXiMain.dll.orig</c> while a proxy DLL stood in its place, and a crash between the two
    /// steps could leave the install that way. GordianXI no longer touches the game folder (the retail and LandSandBoat
    /// logins are direct), so this one startup check puts the original back if a backup is still there.
    /// </summary>
    public static class LegacyProxyCleanup
    {
        /// <summary>The game client DLL in the FFXI install folder.</summary>
        public const string ClientDllName = "FFXiMain.dll";

        /// <summary>The name the removed proxy launch path gave the genuine client DLL while the proxy stood in for it.</summary>
        public const string BackupDllName = "FFXiMain.dll.orig";

        /// <summary>
        /// Moves <c>FFXiMain.dll.orig</c> back over <c>FFXiMain.dll</c> in <paramref name="gameDirectory"/> when the backup
        /// exists. Returns true when it restored one; false when there was nothing to do or the move failed (logged).
        /// </summary>
        public static bool RestoreOrphanedBackup(string? gameDirectory)
        {
            if (string.IsNullOrWhiteSpace(gameDirectory) || !Directory.Exists(gameDirectory)) return false;

            string backup = Path.Combine(gameDirectory, BackupDllName);
            if (!File.Exists(backup)) return false;

            try
            {
                File.Move(backup, Path.Combine(gameDirectory, ClientDllName), overwrite: true);
                GordianLog.Info("SESSION", $"Restored {ClientDllName} from the {BackupDllName} the old proxy launch path left in '{gameDirectory}'.");
                return true;
            }
            catch (Exception ex)
            {
                GordianLog.Warning("SESSION", $"Could not restore {ClientDllName} from {BackupDllName} in '{gameDirectory}': {ex.Message}");
                return false;
            }
        }
    }
}
