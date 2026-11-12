// src/Gordian.App/Services/AppResourceManager.cs
using System;
using System.IO;
using Gordian.Core.Diagnostics;
using Gordian.Core.Resources;

namespace Gordian.App.Services
{
    /// <summary>
    /// Thread-safe application-wide provider for the Final Fantasy XI <see cref="ResourceManager"/>.
    /// <para>
    /// One install is loaded per GordianXI process. The first launch picks it (<see cref="SelectForLaunch"/>): the
    /// profile's own game folder when it names one (a private server's separate install, such as Horizon's), otherwise
    /// the install <see cref="GameDirectoryDetector"/> finds in the registry. Anything that needs the DATs before a
    /// launch (opening the viewport) loads the registry install. A later profile that names a different install is
    /// refused until GordianXI restarts: the zone, model and UI caches and the static DAT loaders are shared by every
    /// session, so two installs cannot be mixed in one process yet.
    /// </para>
    /// </summary>
    public static class AppResourceManager
    {
        private static readonly object Gate = new();
        private static ResourceManager? _instance;
        private static bool _created;

        /// <summary>
        /// Gets the ResourceManager for the loaded FFXI install, loading the registry install if none is loaded yet, or
        /// null when no install can be found.
        /// </summary>
        public static ResourceManager? Instance
        {
            get
            {
                lock (Gate)
                {
                    if (!_created) Create(null);
                    return _instance;
                }
            }
        }

        /// <summary>
        /// Makes sure the install a profile plays with is the loaded one, loading it if nothing is loaded yet. Returns null
        /// when the launch can go ahead, or why it cannot: the profile names a different install than the one loaded.
        /// With no install found anywhere the launch still goes ahead, as before (the lobby and HUD fall back to no art).
        /// </summary>
        /// <param name="profileGameDirectory">The profile's game folder, or empty for the registry install.</param>
        public static string? SelectForLaunch(string? profileGameDirectory)
        {
            string? wanted = string.IsNullOrWhiteSpace(profileGameDirectory) ? null : Path.GetFullPath(profileGameDirectory.Trim());
            lock (Gate)
            {
                if (!_created)
                {
                    Create(wanted);
                    return null;
                }

                string? active = _instance?.GameDirectory;
                string? target = wanted ?? GameDirectoryDetector.DetectGameDirectory();
                if (active == null || target == null || SamePath(active, target)) return null;
                return $"plays with the FFXI install at '{target}', but GordianXI already loaded '{active}'. Restart GordianXI to switch installs.";
            }
        }

        /// <summary>Whether <paramref name="directory"/> looks like an FFXI install (it has the VTABLE.DAT / FTABLE.DAT pair).</summary>
        public static bool LooksLikeGameDirectory(string? directory) =>
            !string.IsNullOrWhiteSpace(directory) &&
            File.Exists(Path.Combine(directory.Trim(), "VTABLE.DAT")) &&
            File.Exists(Path.Combine(directory.Trim(), "FTABLE.DAT"));

        private static bool SamePath(string a, string b) =>
            string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)), Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)),
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

        private static void Create(string? requestedDirectory)
        {
            _created = true;
            try
            {
                string? gameDir = requestedDirectory ?? GameDirectoryDetector.DetectGameDirectory();
                if (string.IsNullOrEmpty(gameDir))
                {
                    GordianLog.Warning("RES", "GameDirectoryDetector could not locate FFXI installation directory.");
                    return;
                }

                var rm = new ResourceManager(gameDir);
                if (rm.InitializeFileTable())
                {
                    GordianLog.Info("RES", $"AppResourceManager successfully initialized for '{gameDir}' with {rm.FileTable.Count} files.");
                }
                else
                {
                    GordianLog.Warning("RES", $"AppResourceManager initialized for '{gameDir}', but InitializeFileTable() returned false.");
                }

                // Item names come from the same install as everything else.
                if (requestedDirectory != null) ItemNameResolver.Initialize(gameDir);
                _instance = rm;
            }
            catch (Exception ex)
            {
                GordianLog.Error("RES", $"Failed to initialize AppResourceManager: {ex.Message}");
            }
        }
    }
}
