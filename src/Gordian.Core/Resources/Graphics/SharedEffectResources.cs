// src/Gordian.Core/Resources/Graphics/SharedEffectResources.cs
using System;
using System.Collections.Generic;
using Gordian.Core.Resources.Containers;

namespace Gordian.Core.Resources.Graphics
{
    /// <summary>
    /// Effect resources from the shared ROM/0/0.DAT (`syst/effe`) tree that zone generators link into when a
    /// DatId is not found in the zone itself (e.g. the weather-sky pole star draws sprite sheet `hit6`).
    /// Link resolution order (local directory, zone root, then the shared effects DAT) referenced from
    /// xi-model-viewer (https://github.com/vekien/xi-model-viewer, ui/js/particle/system.js).
    /// </summary>
    public sealed class SharedEffectResources
    {
        public Dictionary<string, SpriteSheetMesh> SpriteSheets { get; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Decoded textures keyed by their full 16-character name.
        /// </summary>
        public Dictionary<string, DecodedTexture> Textures { get; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Decodes the sprite sheets and textures of a shared effects DAT. The first section with a given DatId wins.
        /// </summary>
        public static SharedEffectResources Parse(ReadOnlySpan<byte> datBytes)
        {
            var shared = new SharedEffectResources();
            foreach (var header in DatSectionWalker.ReadHeaders(datBytes))
            {
                if (header.DataOffset + header.DataSizeBytes > datBytes.Length) continue;
                var payload = datBytes.Slice(header.DataOffset, header.DataSizeBytes);

                if (header.TypeCode == DatSectionType.SpriteSheetMesh && !shared.SpriteSheets.ContainsKey(header.DatId))
                {
                    var sheet = SpriteSheetDecoder.Decode(payload, header.DatId);
                    if (sheet != null) shared.SpriteSheets[header.DatId] = sheet;
                }
                else if (header.TypeCode == DatSectionType.Texture)
                {
                    var texture = TextureDecoder.DecodeTexture(payload);
                    if (texture != null) shared.Textures.TryAdd(texture.Name, texture);
                }
            }
            return shared;
        }
    }
}
