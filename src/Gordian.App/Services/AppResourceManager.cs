// src/Gordian.App/Services/AppResourceManager.cs
using System;
using Gordian.Core.Diagnostics;
using Gordian.Core.Resources;

namespace Gordian.App.Services
{
    /// <summary>
    /// Thread-safe application-wide provider for the retail Final Fantasy XI <see cref="ResourceManager"/>.
    /// Resolves the game installation folder via <see cref="GameDirectoryDetector"/> and initializes the master file table.
    /// </summary>
    public static class AppResourceManager
    {
        private static readonly Lazy<ResourceManager?> _instance = new(() =>
        {
            try
            {
                string? gameDir = GameDirectoryDetector.DetectGameDirectory();
                if (string.IsNullOrEmpty(gameDir))
                {
                    GordianLog.Warning("RES", "GameDirectoryDetector could not locate FFXI installation directory.");
                    return null;
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

                return rm;
            }
            catch (Exception ex)
            {
                GordianLog.Error("RES", $"Failed to initialize AppResourceManager: {ex.Message}");
                return null;
            }
        });

        /// <summary>
        /// Gets the singleton ResourceManager instance for the detected FFXI installation, or null if not found.
        /// </summary>
        public static ResourceManager? Instance => _instance.Value;
    }
}
