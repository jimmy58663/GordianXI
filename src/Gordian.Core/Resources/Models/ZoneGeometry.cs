// src/Gordian.Core/Resources/Models/ZoneGeometry.cs
using System;
using System.Collections.Generic;
using System.Numerics;

namespace Gordian.Core.Resources.Models
{
    /// <summary>
    /// Represents a single vertex in a decoded FFXI 3D mesh.
    /// </summary>
    public readonly record struct MeshVertex(
        Vector3 Position,
        Vector3 Normal,
        Vector2 TexCoord,
        uint ColorRgba
    );

    /// <summary>
    /// Represents a distinct submesh/batch within a zone or entity 3D model.
    /// </summary>
    public sealed class MeshGroup
    {
        public string Name { get; set; } = string.Empty;
        public string TextureName { get; set; } = string.Empty;
        public MeshVertex[] Vertices { get; set; } = Array.Empty<MeshVertex>();
        public int[] Indices { get; set; } = Array.Empty<int>();
        public Vector3 MinBounds { get; set; }
        public Vector3 MaxBounds { get; set; }
        public bool IsBlend { get; set; }
        public bool NoCull { get; set; }
        public bool IsFoliage { get; set; }
        public bool IsWater { get; set; }
        public Vector2 UVScroll { get; set; } = Vector2.Zero;

        /// <summary>
        /// The placement's sub-environment link (e.g. <c>ev01</c> for a cave interior); empty for the main environment.
        /// </summary>
        public string EnvironmentId { get; set; } = string.Empty;

        /// <summary>
        /// Zero-based indices into <see cref="ZoneGeometry.PointLightIds"/> of the (at most four) point lights that shine
        /// on this placement; empty when none do.
        /// </summary>
        public int[] PointLightSlots { get; set; } = Array.Empty<int>();

        public int TriangleCount => Indices.Length / 3;

        public override string ToString() => $"SubMesh [{Name}] Tex: '{TextureName}' (Verts: {Vertices.Length}, Tris: {TriangleCount})";
    }

    /// <summary>
    /// Represents an individual world placement entry decoded from Section 0x1C (ZoneDef).
    /// </summary>
    /// <param name="EnvironmentId">Sub-environment link at record +0x4C (e.g. <c>ev01</c>); empty for the main environment.</param>
    /// <param name="PointLightSlots">Zero-based light-table indices from the four 1-based references at record +0x54.</param>
    /// <param name="BlockId">Record +0x34 FourCC: empty for a static object; one starting <c>_</c>/<c>@</c> marks a part of
    /// an animated multi-part object (a door or an elevator) moved by the client.</param>
    public readonly record struct ZonePlacement(
        string MeshId,
        Vector3 Position,
        Vector3 Rotation,
        Vector3 Scale,
        float DrawDistance,
        string EnvironmentId = "",
        int[]? PointLightSlots = null,
        string BlockId = ""
    )
    {
        /// <summary>
        /// True for a part of an elevator or other moving platform (BlockID starting <c>@</c>).
        /// </summary>
        public bool IsMovingPlatformPart => BlockId.Length > 0 && BlockId[0] == '@';
    }

    /// <summary>
    /// Represents a complete zone terrain model composed of multiple submeshes.
    /// </summary>
    public sealed class ZoneGeometry
    {
        public int ZoneId { get; set; }
        public List<MeshGroup> MeshGroups { get; } = new();
        public List<ZonePlacement> Placements { get; } = new();

        /// <summary>
        /// The ZoneDef light table: the point-light generator FourCC in each slot (empty for an unused slot).
        /// </summary>
        public List<string> PointLightIds { get; } = new();

        public List<Graphics.WeatherSkyLayer> WeatherSkyLayers { get; } = new();

        /// <summary>
        /// World-space zone effects driven by Section 0x05 generators (sea surfaces and similar persistent effect meshes).
        /// </summary>
        public List<Graphics.WeatherSkyLayer> EffectLayers { get; } = new();
        public Graphics.ZoneEnvironmentData? EnvironmentData { get; set; }

        /// <summary>
        /// Mesh groups of moving platforms (elevators), keyed by their BlockID FourCC, in world space at their authored
        /// pose. They are drawn offset by the platform's live height instead of with the static scenery.
        /// </summary>
        public Dictionary<string, List<MeshGroup>> MovingPlatformGroups { get; } = new(StringComparer.Ordinal);

        /// <summary>
        /// The zone's player-collision mesh from the ZoneDef collision block; null when the zone has none.
        /// </summary>
        public World.Collision.ZoneCollisionMesh? Collision { get; set; }

        /// <summary>
        /// Short weather routines (lightning strikes) grouped by directory, played one random routine at a time.
        /// </summary>
        public List<Gordian.Core.Graphics.WeatherRoutineGroup> WeatherRoutineGroups { get; } = new();

        public int TotalVertices
        {
            get
            {
                int sum = 0;
                for (int i = 0; i < MeshGroups.Count; i++) sum += MeshGroups[i].Vertices.Length;
                return sum;
            }
        }

        public int TotalTriangles
        {
            get
            {
                int sum = 0;
                for (int i = 0; i < MeshGroups.Count; i++) sum += MeshGroups[i].TriangleCount;
                return sum;
            }
        }

        public override string ToString() => $"ZoneGeometry [ID: {ZoneId}] ({MeshGroups.Count} meshes, {TotalTriangles} tris)";
    }
}
