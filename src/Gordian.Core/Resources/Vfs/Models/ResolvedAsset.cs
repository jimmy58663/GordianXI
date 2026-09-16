// src/Gordian.Core/Resources/Vfs/Models/ResolvedAsset.cs
using System;
using System.IO;

namespace Gordian.Core.Resources.Vfs.Models
{
    /// <summary>
    /// Represents an asset resolved through the Virtual File System.
    /// Provides access to physical location, origin pack metadata, and lazy streaming.
    /// </summary>
    public sealed class ResolvedAsset
    {
        public string VirtualPath { get; }
        public string PhysicalPath { get; }
        public string SourcePack { get; }
        public AssetCategory Category { get; }
        public bool IsOverride { get; }

        public ResolvedAsset(
            string virtualPath,
            string physicalPath,
            string sourcePack,
            AssetCategory category,
            bool isOverride)
        {
            VirtualPath = virtualPath ?? string.Empty;
            PhysicalPath = physicalPath ?? string.Empty;
            SourcePack = sourcePack ?? string.Empty;
            Category = category;
            IsOverride = isOverride;
        }

        /// <summary>
        /// Gets the size in bytes of the physical file.
        /// </summary>
        public long GetFileSize()
        {
            if (string.IsNullOrEmpty(PhysicalPath) || !File.Exists(PhysicalPath))
            {
                return 0;
            }

            return new FileInfo(PhysicalPath).Length;
        }

        /// <summary>
        /// Opens a read-only stream to the physical asset on disk.
        /// </summary>
        public Stream OpenRead()
        {
            if (!File.Exists(PhysicalPath))
            {
                throw new FileNotFoundException($"Resolved asset physical file not found: {PhysicalPath}", PhysicalPath);
            }

            return new FileStream(PhysicalPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        }

        /// <summary>
        /// Reads all bytes of the physical asset.
        /// </summary>
        public byte[] ReadAllBytes()
        {
            if (!File.Exists(PhysicalPath))
            {
                throw new FileNotFoundException($"Resolved asset physical file not found: {PhysicalPath}", PhysicalPath);
            }

            return File.ReadAllBytes(PhysicalPath);
        }

        /// <summary>
        /// Reads all text of the physical asset (for JSON/YAML/text assets).
        /// </summary>
        public string ReadAllText()
        {
            if (!File.Exists(PhysicalPath))
            {
                throw new FileNotFoundException($"Resolved asset physical file not found: {PhysicalPath}", PhysicalPath);
            }

            return File.ReadAllText(PhysicalPath);
        }

        public override string ToString() =>
            $"[{Category}] {VirtualPath} -> {PhysicalPath} (Pack: {SourcePack})";
    }
}
