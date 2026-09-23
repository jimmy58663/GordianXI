// src/Gordian.Core/Resources/Graphics/SpriteSheetDecoder.cs
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Numerics;
using System.Text;
using Gordian.Core.Resources.Models;

namespace Gordian.Core.Resources.Graphics
{
    /// <summary>
    /// Decoded FFXI Section 0x21 SpriteSheetMesh: one texture and N billboard cards, of which a
    /// sprite-sheet particle draws exactly one per frame (e.g. the twelve moon phases).
    /// Card vertices are in raw DAT space (cards face -Z) with normalized texture coordinates.
    /// </summary>
    public sealed class SpriteSheetMesh
    {
        public string DatId { get; init; } = string.Empty;

        /// <summary>
        /// The referenced Section 0x20 texture's own 16-character name (e.g. "moon    moonshap").
        /// </summary>
        public string TextureName { get; init; } = string.Empty;

        public bool IsLensFlare { get; init; }

        /// <summary>
        /// One triangle-list vertex array per card (6 vertices per quad).
        /// </summary>
        public IReadOnlyList<MeshVertex[]> Cards { get; init; } = Array.Empty<MeshVertex[]>();
    }

    /// <summary>
    /// Clean-room binary decoder for FFXI DAT Section 0x21 (SpriteSheetMesh) chunks.
    /// Layout: u16 flag, u16 cardCount, u8 lensFlare, u8, u8, u8 normalization, 16-byte texture name,
    /// then per card: u16 (always 1), u8 quadCount, u8, [16 bytes lens-flare params], and
    /// 6 * quadCount vertices of { f32 x, y, z; RGBA diffuse; f32 u, v }.
    /// Format referenced from xi-tools (docs/fx/particle_mesh.md) and xi-model-viewer
    /// (https://github.com/vekien/xi-model-viewer, ui/js/dat/sections.js parseSpriteSheet).
    /// </summary>
    public static class SpriteSheetDecoder
    {
        private const int HeaderSize = 0x18;
        private const int VertexStride = 24;
        private const int LensFlareParamsSize = 16;

        public static SpriteSheetMesh? Decode(ReadOnlySpan<byte> payload, string datId)
        {
            if (payload.Length < HeaderSize) return null;

            ushort flag = BinaryPrimitives.ReadUInt16LittleEndian(payload);
            int cardCount = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(2));
            bool lensFlare = payload[4] == 1;
            byte normalization = payload[7];
            string textureName = ReadName(payload.Slice(8, 16));

            // Texel-space UVs on a 256-wide atlas are authored when flag == 1 and normalization == 0.
            float uvScale = (flag == 1 && normalization == 0) ? 1.0f / 256.0f : 1.0f;

            var cards = new List<MeshVertex[]>(cardCount);
            int offset = HeaderSize;
            for (int c = 0; c < cardCount; c++)
            {
                if (offset + 4 > payload.Length) return null;
                if (BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(offset)) != 1) return null;
                int quadCount = payload[offset + 2];
                offset += 4;
                if (lensFlare) offset += LensFlareParamsSize;

                int vertexCount = 6 * quadCount;
                if (offset + vertexCount * VertexStride > payload.Length) return null;

                var vertices = new MeshVertex[vertexCount];
                for (int v = 0; v < vertexCount; v++)
                {
                    var vs = payload.Slice(offset + v * VertexStride, VertexStride);
                    var position = new Vector3(
                        BinaryPrimitives.ReadSingleLittleEndian(vs),
                        BinaryPrimitives.ReadSingleLittleEndian(vs.Slice(4)),
                        BinaryPrimitives.ReadSingleLittleEndian(vs.Slice(8)));
                    uint rgba = BinaryPrimitives.ReadUInt32LittleEndian(vs.Slice(12));
                    var uv = new Vector2(
                        BinaryPrimitives.ReadSingleLittleEndian(vs.Slice(16)) * uvScale,
                        BinaryPrimitives.ReadSingleLittleEndian(vs.Slice(20)) * uvScale);
                    vertices[v] = new MeshVertex(position, -Vector3.UnitZ, uv, rgba);
                }

                cards.Add(vertices);
                offset += vertexCount * VertexStride;
            }

            return new SpriteSheetMesh
            {
                DatId = datId ?? string.Empty,
                TextureName = textureName,
                IsLensFlare = lensFlare,
                Cards = cards
            };
        }

        private static string ReadName(ReadOnlySpan<byte> span)
        {
            int end = span.IndexOf((byte)0);
            if (end < 0) end = span.Length;
            return Encoding.ASCII.GetString(span.Slice(0, end)).TrimEnd();
        }
    }
}
