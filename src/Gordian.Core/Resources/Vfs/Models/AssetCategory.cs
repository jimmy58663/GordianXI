// src/Gordian.Core/Resources/Vfs/Models/AssetCategory.cs

namespace Gordian.Core.Resources.Vfs.Models
{
    /// <summary>
    /// Categorization of assets resolved through the Virtual File System.
    /// </summary>
    public enum AssetCategory
    {
        /// <summary>
        /// Legacy binary FFXI DAT file.
        /// </summary>
        Dat,

        /// <summary>
        /// Modern 3D model (glTF 2.0 .glb / .gltf).
        /// </summary>
        Model,

        /// <summary>
        /// GPU texture or image (DDS BC7/BC5/BC1, PNG, WebP).
        /// </summary>
        Texture,

        /// <summary>
        /// Modern audio or music stream (OGG Vorbis, FLAC, WAV).
        /// </summary>
        Audio,

        /// <summary>
        /// Structured data or configuration (JSON, YAML).
        /// </summary>
        Data,

        /// <summary>
        /// Other unclassified asset file.
        /// </summary>
        Other
    }
}
