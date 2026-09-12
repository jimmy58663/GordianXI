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

                string appDir = AppDomain.CurrentDomain.BaseDirectory;

                // If running from Program Files or other protected system root, redirect to AppData
                if (IsProtectedSystemDirectory(appDir))
                {
                    string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                    if (string.IsNullOrWhiteSpace(appData))
                    {
                        appData = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                    }

                    string path = Path.Combine(appData, AppFolderName);
                    if (!Directory.Exists(path))
                    {
                        Directory.CreateDirectory(path);
                    }
                    return path;
                }

                // Standard user-space location: keep everything local!
                return appDir;
            }
            set
            {
                _customRootDirectory = value;
            }
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
        /// Gets the dedicated directory for Lua and QuickJS addon plugins.
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
