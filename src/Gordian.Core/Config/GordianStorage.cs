// src/Gordian.Core/Config/GordianStorage.cs
using System;
using System.IO;

namespace Gordian.Core.Config
{
    /// <summary>
    /// Provides standardized, cross-platform file system paths for user data,
    /// character profiles, addons, scripts, and logs.
    /// Automatically detects if running in a protected system directory (such as Program Files)
    /// and redirects to AppData, or runs entirely self-contained in local application space.
    /// </summary>
    public static class GordianStorage
    {
        private const string AppFolderName = "GordianXI";
        private const string ProfilesFolderName = "profiles";
        private const string AddonsFolderName = "addons";
        private const string ScriptsFolderName = "scripts";
        private const string LogsFolderName = "logs";
        private const string ScreenshotsFolderName = "screenshots";

        private static string? _customRootDirectory;
        private static string? _customProfilesDirectory;

        /// <summary>
        /// Determines whether a given directory path is located inside an OS-protected system folder
        /// (e.g. Windows Program Files or Unix /usr/ /opt/) requiring Administrator UAC write elevation.
        /// </summary>
        public static bool IsProtectedSystemDirectory(string directory)
        {
            if (string.IsNullOrWhiteSpace(directory)) return false;

            // 1. Windows: "C:\Program Files", "C:\Program Files (x86)", and "C:\Windows"
            string progFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            if (!string.IsNullOrEmpty(progFiles) && directory.StartsWith(progFiles, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            string progFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            if (!string.IsNullOrEmpty(progFilesX86) && directory.StartsWith(progFilesX86, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            string winDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            if (!string.IsNullOrEmpty(winDir) && directory.StartsWith(winDir, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // 2. Linux / macOS: System root directories
            if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
            {
                if (directory.StartsWith("/usr", StringComparison.Ordinal) ||
                    directory.StartsWith("/opt", StringComparison.Ordinal) ||
                    directory.StartsWith("/var", StringComparison.Ordinal) ||
                    directory.StartsWith("/etc", StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Gets the root user data directory for GordianXI.
        /// If the application is located in a standard user directory (e.g., C:\Games\GordianXI, D:\GordianXI, Desktop),
        /// it operates completely self-contained. If located in Program Files, it redirects to %APPDATA%/GordianXI.
        /// </summary>
        public static string RootDataDirectory
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(_customRootDirectory))
                {
                    if (!Directory.Exists(_customRootDirectory))
                    {
                        Directory.CreateDirectory(_customRootDirectory);
                    }
                    return _customRootDirectory;
                }

                string path = ResolveRootDataDirectory(
                    AppDomain.CurrentDomain.BaseDirectory,
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
                if (!Directory.Exists(path))
                {
                    Directory.CreateDirectory(path);
                }
                return path;
            }
            set
            {
                _customRootDirectory = value;
            }
        }

        /// <summary>
        /// The data root for an executable in <paramref name="appDirectory"/>: that folder itself (portable), unless it is a
        /// protected system folder (<see cref="IsProtectedSystemDirectory"/>), in which case
        /// <c>&lt;applicationData&gt;/GordianXI</c> (<paramref name="userProfile"/> when the platform has no application data
        /// folder). The logs folder, and so the Logs tab's Open logs folder button, follow it (#566).
        /// </summary>
        public static string ResolveRootDataDirectory(string appDirectory, string? applicationData, string? userProfile)
        {
            if (!IsProtectedSystemDirectory(appDirectory)) return appDirectory;
            string baseFolder = !string.IsNullOrWhiteSpace(applicationData) ? applicationData : userProfile ?? string.Empty;
            return Path.Combine(baseFolder, AppFolderName);
        }

        private static string? _customUserSettingsDirectory;

        /// <summary>
        /// The per-user folder the desktop shell keeps its own settings in (input profile, viewport, sound and window
        /// placements): <c>%LOCALAPPDATA%/GordianXI</c> (the platform's equivalent elsewhere), or <see cref="RootDataDirectory"/>
        /// when the platform has none. A test assembly sets it to a scratch folder so no test reads or writes the
        /// player's real settings (#383).
        /// </summary>
        public static string UserSettingsDirectory
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(_customUserSettingsDirectory)) return _customUserSettingsDirectory;
                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                return !string.IsNullOrWhiteSpace(localAppData) ? Path.Combine(localAppData, AppFolderName) : RootDataDirectory;
            }
            set => _customUserSettingsDirectory = value;
        }

        /// <summary>
        /// Gets the dedicated directory storing character and account profile JSON files.
        /// Can be overridden for testing or specific configurations.
        /// </summary>
        public static string ProfilesDirectory
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(_customProfilesDirectory))
                {
                    if (!Directory.Exists(_customProfilesDirectory))
                    {
                        Directory.CreateDirectory(_customProfilesDirectory);
                    }
                    return _customProfilesDirectory;
                }

                string path = Path.Combine(RootDataDirectory, ProfilesFolderName);
                if (!Directory.Exists(path))
                {
                    Directory.CreateDirectory(path);
                }
                return path;
            }
            set
            {
                _customProfilesDirectory = value;
            }
        }

        /// <summary>
        /// Gets the dedicated directory for Lua addon plugins and their per-addon scoped storage.
        /// </summary>
        public static string AddonsDirectory
        {
            get
            {
                string path = Path.Combine(RootDataDirectory, AddonsFolderName);
                if (!Directory.Exists(path))
                {
                    Directory.CreateDirectory(path);
                }
                return path;
            }
        }

        /// <summary>
        /// Gets the dedicated directory for macro and automation scripts.
        /// </summary>
        public static string ScriptsDirectory
        {
            get
            {
                string path = Path.Combine(RootDataDirectory, ScriptsFolderName);
                if (!Directory.Exists(path))
                {
                    Directory.CreateDirectory(path);
                }
                return path;
            }
        }

        /// <summary>
        /// Gets the dedicated directory for saved screenshots.
        /// </summary>
        public static string ScreenshotsDirectory
        {
            get
            {
                string path = Path.Combine(RootDataDirectory, ScreenshotsFolderName);
                if (!Directory.Exists(path))
                {
                    Directory.CreateDirectory(path);
                }
                return path;
            }
        }

        /// <summary>
        /// Gets the dedicated directory for packet inspection and runtime log files.
        /// </summary>
        public static string LogsDirectory
        {
            get
            {
                string path = Path.Combine(RootDataDirectory, LogsFolderName);
                if (!Directory.Exists(path))
                {
                    Directory.CreateDirectory(path);
                }
                return path;
            }
        }
    }
}
