// src/Gordian.Core/Resources/Vfs/IVirtualFileSystem.cs
using System;
using System.Collections.Generic;
using Gordian.Core.Resources.Vfs.Models;

namespace Gordian.Core.Resources.Vfs
{
    /// <summary>
    /// Master interface for the GordianXI Modular Virtual File System.
    /// Provides unified asset resolution across legacy DAT overrides, modern asset packs,
    /// and the genuine base game directory.
    /// </summary>
    public interface IVirtualFileSystem : IDisposable
    {
        string BaseGameDirectory { get; }
        string ResourcesDirectory { get; }
        bool IsEnabled { get; set; }

        IReadOnlyList<DatOverlayPack> ActiveDatPacks { get; }
        IReadOnlyList<ModernAssetPack> ActiveAssetPacks { get; }

        /// <summary>
        /// Initializes the VFS with base game files and local resources root.
        /// </summary>
        void Initialize(string baseGameDirectory, string? resourcesDirectory = null);

        /// <summary>
        /// Reloads configuration and re-indexes all mounted packs.
        /// </summary>
        void Reload();

        /// <summary>
        /// Attempts to resolve a legacy DAT path through modern DAT aliases, active DAT overlays,
        /// and falling back to the genuine base game directory.
        /// </summary>
        bool TryResolveDat(string relativePath, out ResolvedAsset? resolved);

        /// <summary>
        /// Attempts to resolve a modern asset path within active modern asset packs.
        /// </summary>
        bool TryResolveAsset(string relativeAssetPath, out ResolvedAsset? resolved);

        /// <summary>
        /// Attempts to resolve an Item ID model override from active modern asset packs.
        /// </summary>
        bool TryResolveItemModel(uint itemId, out ResolvedAsset? resolved, out string? materialVariant);

        /// <summary>
        /// Attempts to resolve a Zone ID asset (terrain, collision, skybox) from active modern asset packs.
        /// </summary>
        bool TryResolveZoneAsset(ushort zoneId, string assetType, out ResolvedAsset? resolved);

        /// <summary>
        /// Fired whenever packs are reloaded or updated via hot-reload.
        /// </summary>
        event Action? OnReloaded;
    }
}
