// src/Gordian.App/Services/GameDirectoryDetector.cs
using System;
using System.IO;
using Microsoft.Win32;

namespace Gordian.App.Services
{
    /// <summary>
    /// Detects the Final Fantasy XI game installation directory from the Windows Registry,
    /// supporting US, European, and Japanese client variants.
    /// Registry key mapping referenced from XILoader research (https://github.com/LandSandBoat/xiloader).
    /// </summary>
    public static class GameDirectoryDetector
    {
        private static readonly string[] RegistrySubKeys = new[]
        {
            @"SOFTWARE\PlayOnlineUS\InstallFolder",
            @"SOFTWARE\PlayOnlineEU\InstallFolder",
            @"SOFTWARE\PlayOnline\InstallFolder"
        };

        private const string FfxiValueName = "0001";
        private const string TargetDllName = "FFXiMain.dll";

        /// <summary>
        /// Attempts to locate the Final Fantasy XI installation directory.
        /// Returns the absolute path to the folder containing FFXiMain.dll, or null if not found.
        /// </summary>
        public static string? DetectGameDirectory()
        {
            if (!OperatingSystem.IsWindows())
            {
                return null;
            }

            try
            {
                // Always query the 32-bit registry view (WOW6432Node on 64-bit systems)
                using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32);

                foreach (var subKey in RegistrySubKeys)
                {
                    using var key = hklm.OpenSubKey(subKey);
                    if (key == null) continue;

                    var rawPath = key.GetValue(FfxiValueName) as string;
                    if (string.IsNullOrWhiteSpace(rawPath)) continue;

                    string cleanPath = rawPath.Trim();
                    if (Directory.Exists(cleanPath))
                    {
                        // FFXiMain.dll, or the backup the removed proxy launch path could leave (LegacyProxyCleanup restores it)
                        string dllPath = Path.Combine(cleanPath, TargetDllName);
                        string origDllPath = Path.Combine(cleanPath, TargetDllName + ".orig");

                        if (File.Exists(dllPath) || File.Exists(origDllPath))
                        {
                            return cleanPath;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[GameDirectoryDetector] Registry query failed: {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// The PlayOnline Viewer's install folder: the <c>"1000"</c> value under the same InstallFolder keys, or null when
        /// it is missing or not Windows. The retail login reads the Viewer's versions from it.
        /// </summary>
        public static string? DetectViewerDirectory()
        {
            if (!OperatingSystem.IsWindows()) return null;
            try
            {
                using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32);
                foreach (var subKey in RegistrySubKeys)
                {
                    using var key = hklm.OpenSubKey(subKey);
                    if (key?.GetValue(ViewerValueName) is string rawPath && Directory.Exists(rawPath.Trim()))
                        return rawPath.Trim();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[GameDirectoryDetector] Viewer registry query failed: {ex.Message}");
            }
            return null;
        }

        private const string ViewerValueName = "1000";

        /// <summary>
        /// Returns the full path to FFXiMain.dll within the detected game directory, or null if not found.
        /// </summary>
        public static string? DetectFFXiMainPath()
        {
            var gameDir = DetectGameDirectory();
            if (gameDir != null)
            {
                return Path.Combine(gameDir, TargetDllName);
            }
            return null;
        }
    }
}
