// src/Gordian.App/Services/AppResourceManager.cs
using System;
using System.IO;
using System.Threading;
using Gordian.Core.Diagnostics;
using Gordian.Core.Resources;

namespace Gordian.App.Services
{
    /// <summary>
    /// Thread-safe application-wide provider for the Final Fantasy XI <see cref="ResourceManager"/>.
    /// <para>
    /// One install is loaded at a time. A launch picks it (<see cref="SelectForLaunch"/>): the profile's own game folder
    /// when it names one (a private server's separate install, such as Horizon's), otherwise the default install
    /// (<see cref="GameDirectoryDetector.DetectGameDirectory"/>: the app-wide folder, else the registry's). Opening the
    /// viewport with nothing running asks for the default install (<see cref="SelectDefaultForViewport"/>).
    /// </para>
    /// <para>
    /// The install can change without a restart (#665), but only while nothing uses it: no viewport window open (each
    /// reads the DATs, the lobby too), no character logged in (collision and events read them with every window closed)
    /// and no launch in flight (<see cref="LaunchHold"/>). The zone, model and UI caches and the static DAT loaders are
    /// shared by every session, so two installs are never mixed in one process. A switch happens only when an install
    /// is about to be used; changing a folder in the settings changes nothing until then.
    /// </para>
    /// </summary>
    public static class AppResourceManager
    {
        private static readonly object Gate = new();
        private static ResourceManager? _instance;
        private static bool _created;
        private static int _launchHolds;

        /// <summary>
        /// What uses the loaded install besides launches in flight: a short description ("a game window is open", "2
        /// characters are logged in") or null when nothing does. Set once at startup (<c>App</c>); null treats the install
        /// as unused.
        /// </summary>
        internal static Func<string?>? UsageProbe { get; set; }

        /// <summary>
        /// Raised after the install changed (not on the first load), inside the gate, with the new manager (null when the
        /// new folder could not be loaded). Subscribers rebuild what they hold from the old one (the sound library).
        /// </summary>
        internal static event Action<ResourceManager?>? InstallSwitched;

        /// <summary>
        /// Builds the manager for an install. Tests replace it to count loads; the default reads the folder's file tables.
        /// </summary>
        internal static Func<string, ResourceManager> Factory { get; set; } = DefaultFactory;

        /// <summary>
        /// Gets the ResourceManager for the loaded FFXI install, loading the default install if none is loaded yet, or
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

        /// <summary>The ResourceManager when one has been created, without loading an install (housekeeping such as #576's purge).</summary>
        public static ResourceManager? Loaded
        {
            get
            {
                lock (Gate) return _instance;
            }
        }

        /// <summary>The number of launches holding the install (<see cref="LaunchHold"/>).</summary>
        internal static int LaunchHolds => Volatile.Read(ref _launchHolds);

        /// <summary>
        /// Makes sure the install a profile plays with is the loaded one, loading it if nothing is loaded yet, or switching
        /// to it when nothing uses the loaded one. Returns null when the launch can go ahead, or why it cannot: the profile
        /// names a different install while something uses the loaded one. With no install found anywhere the launch still
        /// goes ahead, as before (the lobby and HUD fall back to no art). On success <paramref name="hold"/> keeps the
        /// install from switching until the launch has a window or session of its own (or failed); dispose it then.
        /// </summary>
        /// <param name="profileGameDirectory">The profile's game folder, or empty for the default install.</param>
        /// <param name="hold">Held by the launch on success, null on failure.</param>
        public static string? SelectForLaunch(string? profileGameDirectory, out LaunchHold? hold)
        {
            string? wanted = string.IsNullOrWhiteSpace(profileGameDirectory) ? null : Path.GetFullPath(profileGameDirectory.Trim());
            lock (Gate)
            {
                hold = null;
                string? problem = Select(wanted, forLaunch: true);
                if (problem != null) return problem;
                hold = new LaunchHold();
                return null;
            }
        }

        /// <summary>
        /// The viewport is about to open with no launch behind it (no lobby, no character): if nothing uses the loaded
        /// install and it is not the default one, switches to the default, so the empty viewport shows the default
        /// install. Otherwise leaves everything as it is.
        /// </summary>
        public static void SelectDefaultForViewport()
        {
            lock (Gate)
            {
                if (!_created) return; // The viewport loads the default install on first use (Instance).
                Select(null, forLaunch: false);
            }
        }

        /// <summary>
        /// What the app's <see cref="UsageProbe"/> reports: logged-in characters first, then an open viewport window or a
        /// graphics device still alive (the device holds the GPU texture, model and zone caches, and outlives its last
        /// window by a moment), else null.
        /// </summary>
        internal static string? DescribeUsers(int sessions, bool windowsOpen, bool deviceAlive)
        {
            if (sessions > 0) return sessions == 1 ? "a character is logged in" : $"{sessions} characters are logged in";
            if (windowsOpen || deviceAlive) return "a game window is open";
            return null;
        }

        /// <summary>Whether <paramref name="directory"/> looks like an FFXI install (it has the VTABLE.DAT / FTABLE.DAT pair).</summary>
        public static bool LooksLikeGameDirectory(string? directory) =>
            !string.IsNullOrWhiteSpace(directory) &&
            File.Exists(Path.Combine(directory.Trim(), "VTABLE.DAT")) &&
            File.Exists(Path.Combine(directory.Trim(), "FTABLE.DAT"));

        /// <summary>Full path, trailing separators trimmed, case-insensitive on Windows only.</summary>
        internal static bool SamePath(string a, string b) =>
            string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)), Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)),
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

        /// <summary>Tests only: disposes the loaded install and forgets it, every hold, the probe and the factory.</summary>
        internal static void ResetForTests()
        {
            lock (Gate)
            {
                _instance?.Dispose();
                _instance = null;
                _created = false;
                _launchHolds = 0;
                UsageProbe = null;
                InstallSwitched = null;
                Factory = DefaultFactory;
            }
        }

        /// <summary>Under the gate: loads, keeps or switches to <paramref name="wanted"/> (null: the default install).</summary>
        private static string? Select(string? wanted, bool forLaunch)
        {
            if (!_created)
            {
                Create(wanted);
                return null;
            }

            string? active = _instance?.GameDirectory;
            string? target = wanted ?? GameDirectoryDetector.DetectGameDirectory();
            if (target == null) return null;
            if (active != null && SamePath(active, target)) return null;

            string? user = DescribeUsage();
            if (user != null)
            {
                if (!forLaunch) return null;
                string from = active ?? "(no install)";
                return $"plays with the FFXI install at '{target}', but GordianXI has '{from}' loaded and {user}. " +
                       $"Close the game windows and log out of the characters on '{from}' to switch.";
            }

            Switch(active, target);
            return null;
        }

        private static string? DescribeUsage()
        {
            int holds = Volatile.Read(ref _launchHolds);
            if (holds > 0) return holds == 1 ? "a launch is in progress" : $"{holds} launches are in progress";
            try
            {
                return UsageProbe?.Invoke();
            }
            catch (Exception ex)
            {
                // Unknown counts as in use: a switch under a running window would mix installs.
                GordianLog.Warn(LogCategories.Res, $"Could not tell whether the FFXI install is in use: {ex.Message}");
                return "it may still be in use";
            }
        }

        /// <summary>
        /// Under the gate, with nothing using the install: drops every cache built from the old install, loads the new
        /// one and disposes the old manager. The GPU caches need nothing here: they live on the shared graphics device,
        /// which is disposed with the last viewport window, and nothing switches while a window is open or the device is
        /// still alive (<see cref="UsageProbe"/>).
        /// </summary>
        private static void Switch(string? from, string to)
        {
            var old = _instance;
            _instance = null;
            ResourceManager.ResetSharedCaches();
            Create(to);
            GordianLog.Info(LogCategories.Res, $"RES: install switched '{from ?? "(none)"}' -> '{to}'.");
            try
            {
                InstallSwitched?.Invoke(_instance);
            }
            catch (Exception ex)
            {
                GordianLog.Error(LogCategories.Res, $"A holder of the old FFXI install failed to follow the switch: {ex.Message}", ex);
            }
            old?.Dispose();
        }

        private static void Create(string? requestedDirectory)
        {
            _created = true;
            try
            {
                string? gameDir = requestedDirectory ?? GameDirectoryDetector.DetectGameDirectory();
                if (string.IsNullOrEmpty(gameDir))
                {
                    GordianLog.Warn(LogCategories.Res, "GameDirectoryDetector could not locate FFXI installation directory.");
                    return;
                }

                var rm = Factory(gameDir);
                // Item names come from the same install as everything else.
                ItemNameResolver.Initialize(gameDir);
                _instance = rm;
            }
            catch (Exception ex)
            {
                GordianLog.Error(LogCategories.Res, $"Failed to initialize AppResourceManager: {ex.Message}");
            }
        }

        private static ResourceManager DefaultFactory(string gameDir)
        {
            var rm = new ResourceManager(gameDir);
            if (rm.InitializeFileTable())
            {
                GordianLog.Info(LogCategories.Res, $"AppResourceManager successfully initialized for '{gameDir}' with {rm.FileTable.Count} files.");
            }
            else
            {
                GordianLog.Warn(LogCategories.Res, $"AppResourceManager initialized for '{gameDir}', but InitializeFileTable() returned false.");
            }
            return rm;
        }

        /// <summary>
        /// Keeps the loaded install from switching while a launch logs in, before its lobby window or session exists.
        /// Disposing it twice is harmless.
        /// </summary>
        public sealed class LaunchHold : IDisposable
        {
            private int _released;

            internal LaunchHold() => Interlocked.Increment(ref _launchHolds);

            public void Dispose()
            {
                if (Interlocked.Exchange(ref _released, 1) == 0) Interlocked.Decrement(ref _launchHolds);
            }
        }
    }
}
