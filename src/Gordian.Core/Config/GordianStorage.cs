// src/Gordian.Core/Config/GordianStorage.cs
using System;
using System.IO;

namespace Gordian.Core.Config
{
    /// <summary>
    /// Provides standardized, cross-platform file system paths for user data,
    /// character profiles, and application settings using standard AppData / XDG directories.
    /// </summary>
    public static class GordianStorage
    {
        private const string AppFolderName = "GordianXI";
        private const string ProfilesFolderName = "profiles";

        private static string? _customProfilesDirectory;

        /// <summary>
        /// Gets the root user data directory for GordianXI (e.g., %AppData%/GordianXI on Windows,
        /// ~/.config/GordianXI on Linux/macOS).
        /// </summary>
        public static string RootDataDirectory
        {
            get
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
        }

        /// <summary>
        /// Gets the dedicated directory storing character and account profile JSON files.
        /// Can be overridden for testing or portable installations.
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
    }
}
