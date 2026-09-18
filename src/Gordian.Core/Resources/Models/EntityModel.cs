// src/Gordian.Core/Resources/Models/EntityModel.cs
using System;
using System.Collections.Generic;
using System.Numerics;
using Gordian.Core.Resources.Graphics;

namespace Gordian.Core.Resources.Models
{
    /// <summary>
    /// Complete assembled 3D entity model containing skeletal hierarchy,
    /// baked bind-pose submeshes, and texture palettes.
    /// Clean-room implementation referencing FFXI entity model specifications.
    /// </summary>
    public sealed class EntityModel
    {
        public string Name { get; set; } = string.Empty;
        public Skeleton? Skeleton { get; set; }
        public List<MeshGroup> MeshGroups { get; } = new();
        public Dictionary<string, DecodedTexture> Textures { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Vector3 MinBounds { get; private set; } = new(float.MaxValue);
        public Vector3 MaxBounds { get; private set; } = new(float.MinValue);

        public int TotalVertices
        {
            get
            {
                int count = 0;
                for (int i = 0; i < MeshGroups.Count; i++) count += MeshGroups[i].Vertices.Length;
                return count;
            }
        }

        public int TotalTriangles
        {
            get
            {
                int count = 0;
                for (int i = 0; i < MeshGroups.Count; i++) count += MeshGroups[i].TriangleCount;
                return count;
            }
        }

        /// <summary>
        /// Recalculates the composite bounding box across all constituent mesh groups.
        /// </summary>
        public void UpdateBounds()
        {
            if (MeshGroups.Count == 0)
            {
                MinBounds = -Vector3.One;
                MaxBounds = Vector3.One;
                return;
            }

            Vector3 min = new(float.MaxValue);
            Vector3 max = new(float.MinValue);

            for (int i = 0; i < MeshGroups.Count; i++)
            {
                var g = MeshGroups[i];
                if (g.Vertices.Length == 0) continue;

                min = Vector3.Min(min, g.MinBounds);
                max = Vector3.Max(max, g.MaxBounds);
            }

            MinBounds = min;
            MaxBounds = max;
        }

        public override string ToString() => $"EntityModel [{Name}] ({MeshGroups.Count} meshes, {Textures.Count} textures, {TotalTriangles} tris)";
    }
}
