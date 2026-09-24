// src/Gordian.Core/Graphics/ZoneRaycaster.cs
using System;
using System.Numerics;
using Gordian.Core.Resources.Models;

namespace Gordian.Core.Graphics
{
    /// <summary>
    /// Ray queries against a zone's opaque world geometry (display space), e.g. whether terrain hides the sun.
    /// </summary>
    public static class ZoneRaycaster
    {
        /// <summary>
        /// Returns true if a ray from <paramref name="origin"/> along the normalized <paramref name="direction"/> hits
        /// any solid zone triangle within <paramref name="maxDistance"/>. Water and blended decal surfaces never occlude.
        /// </summary>
        public static bool IsOccluded(ZoneGeometry zone, Vector3 origin, Vector3 direction, float maxDistance)
        {
            var inverseDirection = new Vector3(1.0f / direction.X, 1.0f / direction.Y, 1.0f / direction.Z);
            var groups = zone.MeshGroups;
            for (int g = 0; g < groups.Count; g++)
            {
                var group = groups[g];
                if (group.IsWater || group.IsBlend) continue;
                if (!RayHitsBox(origin, inverseDirection, group.MinBounds, group.MaxBounds, maxDistance)) continue;

                var vertices = group.Vertices;
                var indices = group.Indices;
                for (int i = 0; i + 2 < indices.Length; i += 3)
                {
                    if (RayHitsTriangle(origin, direction, vertices[indices[i]].Position, vertices[indices[i + 1]].Position,
                                        vertices[indices[i + 2]].Position, maxDistance))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// Finds the solid mesh group whose surface lies nearest straight below <paramref name="origin"/> (display space,
        /// +Y up) within <paramref name="maxDistance"/>, or null when there is none (e.g. over open water).
        /// </summary>
        public static MeshGroup? FindFloor(ZoneGeometry zone, Vector3 origin, float maxDistance)
        {
            var down = -Vector3.UnitY;
            MeshGroup? floor = null;
            float nearest = maxDistance;
            var groups = zone.MeshGroups;
            for (int g = 0; g < groups.Count; g++)
            {
                var group = groups[g];
                if (group.IsWater || group.IsBlend) continue;
                if (origin.X < group.MinBounds.X || origin.X > group.MaxBounds.X ||
                    origin.Z < group.MinBounds.Z || origin.Z > group.MaxBounds.Z ||
                    group.MinBounds.Y > origin.Y || group.MaxBounds.Y < origin.Y - nearest) continue;

                var vertices = group.Vertices;
                var indices = group.Indices;
                for (int i = 0; i + 2 < indices.Length; i += 3)
                {
                    float t = RayTriangleDistance(origin, down, vertices[indices[i]].Position, vertices[indices[i + 1]].Position,
                                                  vertices[indices[i + 2]].Position);
                    if (t > 1e-3f && t < nearest)
                    {
                        nearest = t;
                        floor = group;
                    }
                }
            }
            return floor;
        }

        private static bool RayHitsBox(Vector3 origin, Vector3 inverseDirection, Vector3 min, Vector3 max, float maxDistance)
        {
            Vector3 t0 = (min - origin) * inverseDirection;
            Vector3 t1 = (max - origin) * inverseDirection;
            Vector3 near = Vector3.Min(t0, t1);
            Vector3 far = Vector3.Max(t0, t1);
            float enter = MathF.Max(MathF.Max(near.X, near.Y), near.Z);
            float exit = MathF.Min(MathF.Min(far.X, far.Y), far.Z);
            return exit >= MathF.Max(enter, 0.0f) && enter <= maxDistance;
        }

        // Möller–Trumbore, double-sided (zone geometry is drawn without culling).
        private static bool RayHitsTriangle(Vector3 origin, Vector3 direction, Vector3 a, Vector3 b, Vector3 c, float maxDistance)
        {
            float t = RayTriangleDistance(origin, direction, a, b, c);
            return t > 1e-3f && t <= maxDistance;
        }

        /// <summary>
        /// Distance along the ray to the triangle, or -1 on a miss.
        /// </summary>
        private static float RayTriangleDistance(Vector3 origin, Vector3 direction, Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 edge1 = b - a;
            Vector3 edge2 = c - a;
            Vector3 p = Vector3.Cross(direction, edge2);
            float determinant = Vector3.Dot(edge1, p);
            if (MathF.Abs(determinant) < 1e-8f) return -1.0f;

            float inverse = 1.0f / determinant;
            Vector3 s = origin - a;
            float u = Vector3.Dot(s, p) * inverse;
            if (u < 0.0f || u > 1.0f) return -1.0f;

            Vector3 q = Vector3.Cross(s, edge1);
            float v = Vector3.Dot(direction, q) * inverse;
            if (v < 0.0f || u + v > 1.0f) return -1.0f;

            return Vector3.Dot(edge2, q) * inverse;
        }
    }
}
