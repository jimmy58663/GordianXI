// src/Gordian.Core/Resources/Graphics/WeightedMeshDecoder.cs
using System;
using System.Buffers.Binary;
using System.Numerics;
using System.Text;
using Gordian.Core.Resources.Models;

namespace Gordian.Core.Resources.Graphics
{
    /// <summary>
    /// A decoded Section 0x25 weighted mesh: one triangle list whose positions and normals come in several morph targets
    /// (chunks) that each particle blends with its own weights (updaters 0x1E-0x22, 0x6E). Positions and normals are in
    /// raw DAT space; <see cref="Blend"/> writes display-space (-x, -y, z) vertices like the other particle meshes.
    /// </summary>
    public sealed class WeightedMesh
    {
        /// <summary>The most morph targets a particle can weigh (updaters 0x1E-0x22).</summary>
        public const int MaxWeights = 5;

        public string Name { get; init; } = string.Empty;
        public string TextureName { get; init; } = string.Empty;

        /// <summary>Header flag 0x80: the texture's alpha is tested (not applied yet).</summary>
        public bool AlphaDiscard { get; init; }

        /// <summary>Per morph target, the raw DAT-space positions.</summary>
        public Vector3[][] Positions { get; init; } = Array.Empty<Vector3[]>();

        /// <summary>Per morph target, the raw DAT-space unit normals.</summary>
        public Vector3[][] Normals { get; init; } = Array.Empty<Vector3[]>();

        /// <summary>Per triangle-list vertex, the position and normal it reads.</summary>
        public ushort[] PositionIndices { get; init; } = Array.Empty<ushort>();

        /// <inheritdoc cref="PositionIndices"/>
        public ushort[] NormalIndices { get; init; } = Array.Empty<ushort>();

        /// <summary>Per triangle-list vertex texture coordinate.</summary>
        public Vector2[] TexCoords { get; init; } = Array.Empty<Vector2>();

        /// <summary>Per triangle-list vertex colour, RGBA with R in the low byte.</summary>
        public uint[] Colors { get; init; } = Array.Empty<uint>();

        public int TargetCount => Positions.Length;
        public int VertexCount => PositionIndices.Length;

        /// <summary>
        /// Blends the morph targets with <paramref name="weights"/> (a plain weighted sum: 51328's <c>uw</c> runs its
        /// two weights from 1.5 / -0.5 to 0 / 1) into display-space vertices, one per triangle-list vertex.
        /// </summary>
        public void Blend(ReadOnlySpan<float> weights, Span<MeshVertex> output)
        {
            int targets = Math.Min(weights.Length, TargetCount);
            for (int v = 0; v < VertexCount; v++)
            {
                int pi = PositionIndices[v];
                int ni = NormalIndices[v];
                Vector3 position = Vector3.Zero;
                Vector3 normal = Vector3.Zero;
                for (int t = 0; t < targets; t++)
                {
                    float w = weights[t];
                    if (w == 0f) continue;
                    position += Positions[t][pi] * w;
                    normal += Normals[t][ni] * w;
                }
                normal = normal.LengthSquared() > 1e-12f ? Vector3.Normalize(normal) : Normals[0][ni];
                output[v] = new MeshVertex(new Vector3(-position.X, -position.Y, position.Z),
                    new Vector3(-normal.X, -normal.Y, normal.Z), TexCoords[v], Colors[v]);
            }
        }

        /// <summary>The largest distance of any morph target's position from the mesh origin.</summary>
        public float BoundingRadius()
        {
            float r2 = 0f;
            foreach (var target in Positions)
            {
                foreach (var p in target) r2 = MathF.Max(r2, p.LengthSquared());
            }
            return MathF.Sqrt(r2);
        }
    }

    /// <summary>
    /// Clean-room binary decoder for FFXI DAT Section 0x25 (WeightedMesh): a morphable particle mesh drawn by Section 0x05
    /// generators with linked data type 0x1D (Port Jeuno 324's eye-shaped blink mask <c>mb</c> in file 51402, the
    /// <c>uw</c> ripple in 51328, the zone weather birds <c>tobi</c>).
    /// Header (after the 16-byte section header): u16 1, u8 flags (low nibble = morph target count, 0x80 = alpha
    /// discard), u8 offset extension (times 0x10000, added to the index and UV offsets), u16 position count, u16 normal
    /// count, u16 index offset, u16 triangle count, u16 colour offset, u16 UV offset, a 16-byte texture name. The morph
    /// targets follow at +0x20, each the positions (3 x f32) then the normals packed 10:10:10 (signed X, Y, Z from bit 0);
    /// then per triangle-list vertex a BGRA colour, a UV pair (2 x f32), and the index streams: every vertex's u16 position
    /// index, then every vertex's u16 normal index. Offsets are from the start of the payload.
    /// Header fields referenced from xi-model-viewer (https://github.com/vekien/xi-model-viewer, ui/js/dat/inspect.js
    /// parseInspectWeightedMesh); the morph-target, normal and index layout was read from the retail files (#204, a walk of
    /// all 680 retail Section 0x25 sections, 2026-10-02). <b>Beyond xi-model-viewer:</b> it reads only the header.
    /// </summary>
    public static class WeightedMeshDecoder
    {
        private const int HeaderSize = 0x20;
        private const int TextureNameLength = 16;

        /// <summary>
        /// Decodes a Section 0x25 payload (excluding the 16-byte section header), or null if it is malformed.
        /// </summary>
        /// <param name="payload">Section payload.</param>
        /// <param name="datId">Section DatId.</param>
        /// <param name="zoneResource">Doubles the half-range vertex colours (clamped), as <see cref="ParticleMeshDecoder"/>.</param>
        public static WeightedMesh? Decode(ReadOnlySpan<byte> payload, string datId, bool zoneResource = true)
        {
            if (payload.Length < HeaderSize) return null;
            if (BinaryPrimitives.ReadUInt16LittleEndian(payload) != 1) return null;

            byte flags = payload[2];
            int targets = flags & 0x0F;
            if (targets is <= 0 or > WeightedMesh.MaxWeights) return null;
            int extension = payload[3] * 0x10000;
            int positionCount = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(4));
            int normalCount = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(6));
            int indexOffset = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(8)) + extension;
            int vertexCount = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(10)) * 3;
            int colorOffset = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(12));
            int uvOffset = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(14)) + extension;
            if (positionCount == 0 || normalCount == 0 || vertexCount == 0) return null;

            int targetStride = positionCount * 12 + normalCount * 4;
            if (HeaderSize + targets * targetStride > payload.Length ||
                colorOffset + vertexCount * 4 > payload.Length ||
                uvOffset + vertexCount * 8 > payload.Length ||
                indexOffset + vertexCount * 4 > payload.Length) return null;

            var positions = new Vector3[targets][];
            var normals = new Vector3[targets][];
            for (int t = 0; t < targets; t++)
            {
                var block = payload.Slice(HeaderSize + t * targetStride, targetStride);
                var pos = new Vector3[positionCount];
                for (int i = 0; i < positionCount; i++)
                {
                    pos[i] = new Vector3(
                        BinaryPrimitives.ReadSingleLittleEndian(block.Slice(i * 12)),
                        BinaryPrimitives.ReadSingleLittleEndian(block.Slice(i * 12 + 4)),
                        BinaryPrimitives.ReadSingleLittleEndian(block.Slice(i * 12 + 8)));
                }
                var nrm = new Vector3[normalCount];
                for (int i = 0; i < normalCount; i++)
                {
                    nrm[i] = UnpackNormal(BinaryPrimitives.ReadUInt32LittleEndian(block.Slice(positionCount * 12 + i * 4)));
                }
                positions[t] = pos;
                normals[t] = nrm;
            }

            int colorScale = zoneResource ? 2 : 1;
            var colors = new uint[vertexCount];
            var uvs = new Vector2[vertexCount];
            var positionIndices = new ushort[vertexCount];
            var normalIndices = new ushort[vertexCount];
            for (int v = 0; v < vertexCount; v++)
            {
                // BGRA, repacked as RGBA (R in the low byte).
                var c = payload.Slice(colorOffset + v * 4, 4);
                uint r = (uint)Math.Min(0xFF, c[2] * colorScale);
                uint g = (uint)Math.Min(0xFF, c[1] * colorScale);
                uint b = (uint)Math.Min(0xFF, c[0] * colorScale);
                uint a = (uint)Math.Min(0xFF, c[3] * colorScale);
                colors[v] = r | (g << 8) | (b << 16) | (a << 24);
                uvs[v] = new Vector2(
                    BinaryPrimitives.ReadSingleLittleEndian(payload.Slice(uvOffset + v * 8)),
                    BinaryPrimitives.ReadSingleLittleEndian(payload.Slice(uvOffset + v * 8 + 4)));
                positionIndices[v] = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(indexOffset + v * 2));
                normalIndices[v] = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(indexOffset + (vertexCount + v) * 2));
                if (positionIndices[v] >= positionCount || normalIndices[v] >= normalCount) return null;
            }

            return new WeightedMesh
            {
                Name = datId,
                TextureName = ReadName(payload.Slice(16, TextureNameLength)),
                AlphaDiscard = (flags & 0x80) != 0,
                Positions = positions,
                Normals = normals,
                PositionIndices = positionIndices,
                NormalIndices = normalIndices,
                TexCoords = uvs,
                Colors = colors
            };
        }

        /// <summary>
        /// Unpacks a 10:10:10 normal: signed 10-bit X, Y and Z from bit 0 (0x20000000, the flat masks' normal, is -Z).
        /// </summary>
        public static Vector3 UnpackNormal(uint packed)
        {
            static float Component(uint bits) => Math.Max(-1f, ((int)(bits << 22) >> 22) / 511f);
            return new Vector3(Component(packed), Component(packed >> 10), Component(packed >> 20));
        }

        private static string ReadName(ReadOnlySpan<byte> span)
        {
            int end = span.IndexOf((byte)0);
            if (end < 0) end = span.Length;
            return Encoding.ASCII.GetString(span.Slice(0, end)).TrimEnd();
        }
    }
}
