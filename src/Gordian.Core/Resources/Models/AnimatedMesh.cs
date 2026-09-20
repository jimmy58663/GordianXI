// src/Gordian.Core/Resources/Models/AnimatedMesh.cs
using System;
using System.Numerics;

namespace Gordian.Core.Resources.Models
{
    /// <summary>
    /// GPU-ready skinned vertex carrying raw joint/weight/position/normal data for up to two
    /// bone influences, unrolled per triangle corner. Skinning itself happens in the vertex
    /// shader against a per-instance joint palette, not baked on the CPU.
    /// </summary>
    public readonly record struct AnimatedMeshVertex(
        Vector3 Position0,
        Vector3 Position1,
        Vector3 Normal0,
        Vector3 Normal1,
        float Weight0,
        float Weight1,
        int Joint0,
        int Joint1,
        Vector2 TexCoord,
        uint ColorRgba
    );

    /// <summary>
    /// Represents a distinct GPU-skinned submesh/batch within an animated entity model.
    /// Mirrors <see cref="MeshGroup"/>'s shape but carries unbaked skin data instead of
    /// final world-space positions.
    /// </summary>
    public sealed class AnimatedMeshGroup
    {
        public string Name { get; set; } = string.Empty;
        public string TextureName { get; set; } = string.Empty;
        public AnimatedMeshVertex[] Vertices { get; set; } = Array.Empty<AnimatedMeshVertex>();
        public int[] Indices { get; set; } = Array.Empty<int>();
        public Vector3 MinBounds { get; set; }
        public Vector3 MaxBounds { get; set; }

        public int TriangleCount => Indices.Length / 3;

        public override string ToString() => $"AnimatedSubMesh [{Name}] Tex: '{TextureName}' (Verts: {Vertices.Length}, Tris: {TriangleCount})";
    }
}
