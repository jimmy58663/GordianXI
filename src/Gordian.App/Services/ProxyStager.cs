// src/Gordian.App/Services/ProxyStager.cs
using System;
using System.IO;
using System.Threading;

namespace Gordian.App.Services
{
    /// <summary>
    /// Manages ephemeral staging and restoration of the GordianXI proxy FFXiMain.dll.
    /// Ensures zero permanent modification to the user's game installation.
    /// </summary>
    public static class ProxyStager
    {
        public const string TargetDllName = "FFXiMain.dll";
        public const string BackupDllName = "FFXiMain.dll.orig";

        private static readonly object _lock = new object();
        public static bool IsStaged { get; private set; }

        /// <summary>
        /// Locates the compiled Gordian proxy FFXiMain.dll binary from build or runtime folders.
        /// </summary>
        public static string? FindCompiledProxyPath()
        {
            // 1. Check runtime executable directory
            string appDir = AppContext.BaseDirectory;
            string localProxy = Path.Combine(appDir, TargetDllName);
            if (File.Exists(localProxy)) return localProxy;

            // 2. Check src/Gordian.Proxy directory relative to current directory
            string projectSourceProxy = Path.Combine(Directory.GetCurrentDirectory(), "src", "Gordian.Proxy", TargetDllName);
            if (File.Exists(projectSourceProxy)) return projectSourceProxy;

            // 3. Check relative navigation from bin/Debug/net10.0/
            try
            {
                string devRelative = Path.GetFullPath(Path.Combine(appDir, "..", "..", "..", "..", "src", "Gordian.Proxy", TargetDllName));
                if (File.Exists(devRelative)) return devRelative;
            }
            catch { }

            return null;
        }

        /// <summary>
        /// Stages the proxy by renaming genuine FFXiMain.dll to FFXiMain.dll.orig
        /// and copying our proxy DLL in its place.
        /// </summary>
        /// <param name="gameDir">The directory containing FFXiMain.dll.</param>
        /// <param name="proxySourcePath">Optional path to the compiled proxy DLL.</param>
        public static bool StageProxy(string gameDir, string? proxySourcePath = null)
        {
            lock (_lock)
            {
                if (string.IsNullOrWhiteSpace(gameDir) || !Directory.Exists(gameDir))
                {
                    System.Diagnostics.Debug.WriteLine($"[ProxyStager] Target directory does not exist: '{gameDir}'");
                    return false;
                }

                string sourceProxy = proxySourcePath ?? FindCompiledProxyPath() ?? string.Empty;
                if (!File.Exists(sourceProxy))
                {
                    System.Diagnostics.Debug.WriteLine($"[ProxyStager] Proxy binary '{TargetDllName}' could not be located.");
                    return false;
                }

                string targetDll = Path.Combine(gameDir, TargetDllName);
                string backupDll = Path.Combine(gameDir, BackupDllName);

                try
                {
                    // If the backup doesn't already exist and the target exists, backup the original
                    if (!File.Exists(backupDll) && File.Exists(targetDll))
                    {
                        File.Move(targetDll, backupDll);
                    }

                    // Copy the proxy DLL into the target slot
                    File.Copy(sourceProxy, targetDll, overwrite: true);
                    IsStaged = true;
                    System.Diagnostics.Debug.WriteLine($"[ProxyStager] Successfully staged proxy in '{gameDir}'.");
                    return true;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[ProxyStager] Failed to stage proxy: {ex.Message}");
                    // Attempt rollback if backup exists and target was not replaced
                    RestoreOriginal(gameDir);
                    return false;
                }
            }
        }

        /// <summary>
        /// Restores the original FFXiMain.dll by deleting the proxy and renaming FFXiMain.dll.orig back.
        /// Retries with short delays if the proxy is temporarily held by a closing process.
        /// </summary>
        /// <param name="gameDir">The directory containing FFXiMain.dll.</param>
        /// <param name="maxRetrySeconds">Maximum seconds to retry if file is temporarily locked.</param>
        public static bool RestoreOriginal(string gameDir, int maxRetrySeconds = 3)
        {
            lock (_lock)
            {
                if (string.IsNullOrWhiteSpace(gameDir) || !Directory.Exists(gameDir))
                {
                    IsStaged = false;
                    return false;
                }

                string targetDll = Path.Combine(gameDir, TargetDllName);
                string backupDll = Path.Combine(gameDir, BackupDllName);

                if (!File.Exists(backupDll))
                {
                    // Nothing to restore
                    IsStaged = false;
                    return true;
                }

                int attempts = Math.Max(1, maxRetrySeconds * 10);
                for (int i = 0; i < attempts; i++)
                {
                    try
                    {
                        if (File.Exists(targetDll))
                        {
                            File.Delete(targetDll);
                        }

                        File.Move(backupDll, targetDll);
                        IsStaged = false;
                        System.Diagnostics.Debug.WriteLine($"[ProxyStager] Successfully restored original '{TargetDllName}' in '{gameDir}'.");
                        return true;
                    }
                    catch (IOException)
                    {
                        // File is still locked by exiting process, wait 100ms and retry
                        Thread.Sleep(100);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[ProxyStager] Exception during restoration: {ex.Message}");
                        break;
                    }
                }

                System.Diagnostics.Debug.WriteLine($"[ProxyStager] Warning: Could not restore '{BackupDllName}' within timeout.");
                return false;
            }
        }

        /// <summary>
        /// Self-healing startup check.
        /// If a previous session crashed or terminated unexpectedly leaving FFXiMain.dll.orig in place,
        /// this immediately restores the original file.
        /// </summary>
        public static bool SelfHealStartup(string? gameDir = null)
        {
            string? dir = gameDir ?? GameDirectoryDetector.DetectGameDirectory();
            if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
            {
                return false;
            }

            string backupDll = Path.Combine(dir, BackupDllName);
            if (File.Exists(backupDll))
            {
                System.Diagnostics.Debug.WriteLine($"[ProxyStager] Orphaned backup '{BackupDllName}' detected in '{dir}'. Performing self-healing restoration.");
                return RestoreOriginal(dir);
            }

            return false;
        }
    }
}
