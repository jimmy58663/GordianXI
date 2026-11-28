// src/Gordian.Core/Config/GordianStorage.cs
using System;
using System.IO;
using System.Text;
using Gordian.Core.Diagnostics;

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
        /// A server, world or character name made safe as one folder name on every platform, for the chat logs (#565) and
        /// <see cref="CharacterConfigStorage"/> (#639): characters a file system rejects become <c>_</c>, casing is kept
        /// exactly, an empty or dot-only name becomes <c>Unknown</c>, and a Windows device name (<c>Con</c>, <c>Aux</c>,
        /// <c>Nul</c>, <c>Prn</c>, <c>COM1</c>-<c>COM9</c>, <c>LPT1</c>-<c>LPT9</c>, also with an extension) gets a
        /// trailing <c>_</c>. That last rule applies on every platform, so a data folder copied between systems keeps
        /// its names; FFXI allows three-letter names such as Con.
        /// </summary>
        public static string SafeFolderName(string? name)
        {
            string trimmed = (name ?? string.Empty).Trim();
            var builder = new StringBuilder(trimmed.Length);
            foreach (char c in trimmed)
            {
                bool invalid = c < 0x20 || c is '<' or '>' or ':' or '"' or '/' or '\\' or '|' or '?' or '*';
                builder.Append(invalid ? '_' : c);
            }
            string result = builder.ToString().TrimEnd('.', ' ');
            if (result.Length == 0 || result.Trim('.', '_').Length == 0) return "Unknown";
            return IsWindowsDeviceName(result) ? result + "_" : result;
        }

        /// <summary>
        /// Whether Windows reserves <paramref name="name"/> for a device: the part before the first dot is <c>CON</c>,
        /// <c>PRN</c>, <c>AUX</c>, <c>NUL</c>, <c>COM1</c>-<c>COM9</c> or <c>LPT1</c>-<c>LPT9</c>, in any case.
        /// </summary>
        private static bool IsWindowsDeviceName(string name)
        {
            int dot = name.IndexOf('.');
            string stem = (dot < 0 ? name : name[..dot]).TrimEnd(' ').ToUpperInvariant();
            if (stem is "CON" or "PRN" or "AUX" or "NUL") return true;
            return stem.Length == 4 && (stem.StartsWith("COM", StringComparison.Ordinal) || stem.StartsWith("LPT", StringComparison.Ordinal))
                && stem[3] is >= '1' and <= '9';
        }

        /// <summary>Removes <paramref name="folder"/> if it is empty: an older build's config or chat log folder once its last file has moved out (#639).</summary>
        internal static void DeleteIfEmpty(string folder)
        {
            try
            {
                if (Directory.Exists(folder) && Directory.GetFileSystemEntries(folder).Length == 0) Directory.Delete(folder);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                GordianLog.Debug(LogCategories.Storage, $"Could not remove the empty folder '{folder}': {ex.Message}.");
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
