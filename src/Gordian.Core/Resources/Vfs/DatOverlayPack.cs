// src/Gordian.Core/Resources/Vfs/DatOverlayPack.cs
using System;
using System.Collections.Generic;
using System.IO;
using Gordian.Core.Diagnostics;

namespace Gordian.Core.Resources.Vfs
{
    /// <summary>
    /// Represents an indexed legacy FFXI DAT overlay pack (XIPivot / Pivot compatible).
    /// Scans a directory structure (e.g. ROM, ROM2...ROM9, root DATs) and indexes all files
    /// using uppercase invariant normalized keys to guarantee case-insensitive cross-platform resolution.
    /// </summary>
    public sealed class DatOverlayPack
    {
        private readonly Dictionary<string, string> _fileIndex = new(StringComparer.Ordinal);

        public string Id { get; }
        public string RootPath { get; }
        public int Priority { get; set; }
        public int FileCount => _fileIndex.Count;
        public IReadOnlyDictionary<string, string> FileIndex => _fileIndex;

        public DatOverlayPack(string id, string rootPath, int priority = 50)
        {
            Id = id ?? string.Empty;
            RootPath = rootPath ?? string.Empty;
            Priority = priority;
        }

        /// <summary>
        /// Mounts and indexes all files within the pack directory.
        /// </summary>
        public bool Mount()
        {
            _fileIndex.Clear();

            if (string.IsNullOrWhiteSpace(RootPath) || !Directory.Exists(RootPath))
            {
                GordianLog.Warn("VFS", $"Cannot mount DAT pack '{Id}': directory does not exist ({RootPath}).");
                return false;
            }

            try
            {
                var files = Directory.EnumerateFiles(RootPath, "*", SearchOption.AllDirectories);

                foreach (var physicalPath in files)
                {
                    string fileName = Path.GetFileName(physicalPath);

                    // Skip metadata/hidden files
                    if (fileName.StartsWith('.') || fileName.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    // Compute relative path from pack root
                    string relPath = Path.GetRelativePath(RootPath, physicalPath);
                    string key = VfsPathNormalizer.Normalize(relPath);

                    if (!string.IsNullOrEmpty(key))
                    {
                        _fileIndex[key] = physicalPath;
                    }
                }

                GordianLog.Info("VFS", $"Mounted DAT overlay pack '{Id}' with {_fileIndex.Count} files (Priority: {Priority}).");
                return true;
            }
            catch (Exception ex)
            {
                GordianLog.Error("VFS", $"Error mounting DAT overlay pack '{Id}': {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Checks if this pack contains an override for the normalized DAT path.
        /// </summary>
        public bool TryResolve(string normalizedKey, out string physicalPath)
        {
            return _fileIndex.TryGetValue(normalizedKey, out physicalPath!);
        }

        public void Clear()
        {
            _fileIndex.Clear();
        }

        public override string ToString() => $"[DAT Pack] {Id} ({FileCount} files, Priority: {Priority})";
    }
}
