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
                        accessibleChangedFiles.Add(path);
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
