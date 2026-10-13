// src/Gordian.Core/Resources/Vfs/VfsHotReloadWatcher.cs
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Gordian.Core.Diagnostics;

namespace Gordian.Core.Resources.Vfs
{
    /// <summary>
    /// Thread-safe, debounced filesystem watcher for runtime hot-reloading of VFS assets and mod packs.
    /// Safely suppresses intermediate write events and verifies file readability before notifying consumers.
    /// </summary>
    public sealed class VfsHotReloadWatcher : IDisposable
    {
        private readonly object _lock = new();
        private readonly List<FileSystemWatcher> _watchers = new();
        private readonly ConcurrentDictionary<string, byte> _pendingChanges = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, int> _retries = new(StringComparer.OrdinalIgnoreCase);
        private const int MaxRetries = 40;
        private readonly Timer _debounceTimer;
        private readonly int _debounceDelayMs;
        private bool _disposed;

        /// <summary>
        /// Fired when an existing asset file is created, modified, or deleted.
        /// </summary>
        public event Action<IReadOnlyList<string>>? OnAssetsChanged;

        /// <summary>
        /// Fired when a directory addition/removal or manifest change occurs that requires pack re-indexing.
        /// </summary>
        public event Action? OnPacksStructureChanged;

        /// <summary>
        /// Fired once per settled burst of changes with every path that changed (created, written, renamed or deleted;
        /// files and folders), after <see cref="OnPacksStructureChanged"/> / <see cref="OnAssetsChanged"/>. The VFS
        /// re-indexes its packs on it (#374): a file added to or removed from a mounted pack changes the pack's index.
        /// </summary>
        public event Action<IReadOnlyList<string>>? OnChanged;

        public VfsHotReloadWatcher(int debounceDelayMs = 350)
        {
            _debounceDelayMs = debounceDelayMs;
            _debounceTimer = new Timer(OnDebounceElapsed, null, Timeout.Infinite, Timeout.Infinite);
        }

        /// <summary>
        /// Begins watching a directory and all subdirectories for changes.
        /// </summary>
        public void WatchDirectory(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            {
                return;
            }

            lock (_lock)
            {
                try
                {
                    var watcher = new FileSystemWatcher(path)
                    {
                        IncludeSubdirectories = true,
                        NotifyFilter = NotifyFilters.FileName |
                                       NotifyFilters.DirectoryName |
                                       NotifyFilters.LastWrite |
                                       NotifyFilters.Size
                    };

                    watcher.Changed += OnFileSystemEvent;
                    watcher.Created += OnFileSystemEvent;
                    watcher.Deleted += OnFileSystemEvent;
                    watcher.Renamed += OnFileSystemRenamed;

                    watcher.EnableRaisingEvents = true;
                    _watchers.Add(watcher);

                    GordianLog.Info("VFS", $"Hot-reload watching directory: {path}");
                }
                catch (Exception ex)
                {
                    GordianLog.Warn("VFS", $"Could not attach hot-reload watcher to {path}: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Watches one file in a directory (not its subdirectories): <c>vfs.json</c>, whose pack toggles and priorities
        /// take effect when it is saved (#374).
        /// </summary>
        public void WatchFile(string directory, string fileName)
        {
            if (string.IsNullOrWhiteSpace(directory) || string.IsNullOrWhiteSpace(fileName) || !Directory.Exists(directory))
            {
                return;
            }

            lock (_lock)
            {
                try
                {
                    var watcher = new FileSystemWatcher(directory, fileName)
                    {
                        IncludeSubdirectories = false,
                        NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size
                    };

                    watcher.Changed += OnFileSystemEvent;
                    watcher.Created += OnFileSystemEvent;
                    watcher.Deleted += OnFileSystemEvent;
                    watcher.Renamed += OnFileSystemRenamed;

                    watcher.EnableRaisingEvents = true;
                    _watchers.Add(watcher);

                    GordianLog.Info("VFS", $"Hot-reload watching file: {Path.Combine(directory, fileName)}");
                }
                catch (Exception ex)
                {
                    GordianLog.Warn("VFS", $"Could not attach hot-reload watcher to {Path.Combine(directory, fileName)}: {ex.Message}");
                }
            }
        }

        private void OnFileSystemEvent(object sender, FileSystemEventArgs e)
        {
            EnqueueChange(e.FullPath);
        }

        private void OnFileSystemRenamed(object sender, RenamedEventArgs e)
        {
            EnqueueChange(e.OldFullPath);
            EnqueueChange(e.FullPath);
        }

        private void EnqueueChange(string path)
        {
            if (_disposed || string.IsNullOrWhiteSpace(path)) return;

            string fileName = Path.GetFileName(path);
            if (fileName.StartsWith('.') || fileName.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _pendingChanges[path] = 0;

            // Reset debounce timer
            try
            {
                _debounceTimer.Change(_debounceDelayMs, Timeout.Infinite);
            }
            catch (ObjectDisposedException) { }
        }

        private void OnDebounceElapsed(object? state)
        {
            if (_disposed) return;

            var changedPaths = new List<string>(_pendingChanges.Keys);
            _pendingChanges.Clear();

            if (changedPaths.Count == 0) return;

            bool structuralChange = false;
            var accessibleChangedFiles = new List<string>();

            foreach (var path in changedPaths)
            {
                if (Directory.Exists(path) || Path.GetFileName(path).Equals("manifest.json", StringComparison.OrdinalIgnoreCase) ||
                    Path.GetFileName(path).Equals("vfs.json", StringComparison.OrdinalIgnoreCase))
                {
                    structuralChange = true;
                }

                if (File.Exists(path))
                {
                    if (WaitForFileReady(path))
                    {
                        _retries.TryRemove(path, out _);
                        accessibleChangedFiles.Add(path);
                    }
                    else if (_retries.AddOrUpdate(path, 1, (_, n) => n + 1) <= MaxRetries)
                    {
                        // Still being written (a large pack being copied in): look again after the next quiet period,
                        // so the last file of a copy is not missed when no further event comes for it.
                        EnqueueChange(path);
                    }
                    else
                    {
                        _retries.TryRemove(path, out _);
                        GordianLog.Warn("VFS", $"Hot-reload gave up waiting for '{path}' to be readable.");
                    }
                }
                else
                {
                    // File was deleted
                    accessibleChangedFiles.Add(path);
                }
            }

            if (structuralChange)
            {
                GordianLog.Info("VFS", "Hot-reload detected structural pack changes. Triggering re-index.");
                OnPacksStructureChanged?.Invoke();
            }

            if (accessibleChangedFiles.Count > 0)
            {
                GordianLog.Info("VFS", $"Hot-reload detected {accessibleChangedFiles.Count} asset changes.");
                OnAssetsChanged?.Invoke(accessibleChangedFiles);
            }

            if (structuralChange || accessibleChangedFiles.Count > 0)
            {
                OnChanged?.Invoke(changedPaths);
            }
        }

        private static bool WaitForFileReady(string filePath)
        {
            // Give file writes up to 500ms to release locks
            for (int i = 0; i < 5; i++)
            {
                try
                {
                    using var stream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    return true;
                }
                catch (IOException)
                {
                    Thread.Sleep(100);
                }
                catch (Exception)
                {
                    return false;
                }
            }

            return false;
        }

        public void Stop()
        {
            lock (_lock)
            {
                foreach (var watcher in _watchers)
                {
                    try
                    {
                        watcher.EnableRaisingEvents = false;
                        watcher.Dispose();
                    }
                    catch { }
                }
                _watchers.Clear();
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            Stop();
            _debounceTimer.Dispose();
        }
    }
}
