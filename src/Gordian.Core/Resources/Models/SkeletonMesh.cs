// src/Gordian.Core/Resources/Models/SkeletonMesh.cs
using System;
using System.Collections.Generic;
using System.Numerics;

namespace Gordian.Core.Resources.Models
{
    /// <summary>
    /// Skinned vertex containing up to two joint attachments and pre-weighted coordinates.
    /// Format referenced from xi-model-viewer (https://github.com/vekien/xi-model-viewer) and xim.
    /// </summary>
    public struct SkinnedVertex
    {
        public Vector3 Position0;
        public Vector3 Position1;
        public Vector3 Normal0;
        public Vector3 Normal1;
        public float Weight0;
        public float Weight1;
        public int Joint0;
        public int Joint1; // -1 if single joint
    }

    public enum MeshTopology : byte
    {
        TriangleList = 0,
        TriangleStrip = 1
    }

    /// <summary>
    /// Corner vertex indexing into the skinned vertex array with UV coordinates and vertex color.
    /// </summary>
    public readonly record struct MeshCorner(ushort VertexIndex, Vector2 TexCoord, uint ColorRgba);

    /// <summary>
    /// Submesh primitive piece with topology, indices/corners, and diffuse texture association.
    /// </summary>
    public sealed class SkeletonMeshPiece
    {
        public MeshTopology Topology { get; init; }
        public MeshCorner[] Corners { get; init; } = Array.Empty<MeshCorner>();
        public string TextureName { get; init; } = string.Empty;
        public bool Mirrored { get; init; }

        /// <summary>
        /// What the piece is, for gear occlusion (byte 13 of the 0x8010 render-properties block in force when the
        /// piece was read; 0 when none preceded it): 1-3 hair, 4 face, 5 wrist, 6 pants, 7 shins. See
        /// <see cref="GearOcclusion"/>.
        /// </summary>
        public byte DisplayType { get; init; }
    }

    /// <summary>
    /// Decoded skinned mesh structure containing vertex pool and primitive pieces.
    /// </summary>
    public sealed class SkeletonMeshGroup
    {
        public SkinnedVertex[] Vertices { get; set; } = Array.Empty<SkinnedVertex>();
        public SkinnedVertex[]? FlippedVertices { get; set; }
        public List<SkeletonMeshPiece> Pieces { get; } = new();
        public bool Symmetric { get; set; }
        /// <summary>
        /// What this mesh hides on other pieces of the worn set (0x2A header byte 3). See <see cref="GearOcclusion"/>.
        /// </summary>
        public byte OccludeType { get; set; }
        public string SourcePath { get; set; } = string.Empty;

        /// <summary>The mesh section's name (e.g. <c>hh_b</c>, <c>wep4</c>).</summary>
        public string SectionName { get; set; } = string.Empty;
    }
}
