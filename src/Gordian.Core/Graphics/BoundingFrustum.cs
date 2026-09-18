// src/Gordian.Core/Graphics/BoundingFrustum.cs
using System;
using System.Numerics;

namespace Gordian.Core.Graphics
{
    /// <summary>
    /// Represents a 3D view frustum defined by 6 clipping planes (Left, Right, Bottom, Top, Near, Far).
    /// Used for fast O(1) frustum culling of zone terrain meshes and world entities.
    /// Clean-room implementation referencing standard Fast Extraction of Viewing Frustum Planes (Gribb & Hartmann).
    /// </summary>
    public sealed class BoundingFrustum
    {
        // 6 Planes: Left=0, Right=1, Bottom=2, Top=3, Near=4, Far=5
        private readonly Plane[] _planes = new Plane[6];

        public ReadOnlySpan<Plane> Planes => _planes;

        public Plane Left => _planes[0];
        public Plane Right => _planes[1];
        public Plane Bottom => _planes[2];
        public Plane Top => _planes[3];
        public Plane Near => _planes[4];
        public Plane Far => _planes[5];

        public BoundingFrustum()
        {
            Update(Matrix4x4.Identity);
        }

        public BoundingFrustum(Matrix4x4 viewProjection)
        {
            Update(viewProjection);
        }

        /// <summary>
        /// Updates the frustum planes from a combined View-Projection matrix.
        /// Compatible with Veldrid's standard depth range [0, 1].
        /// </summary>
        public void Update(Matrix4x4 m)
        {
            // Left Plane: col3 + col0
            _planes[0] = NormalizePlane(new Plane(
                m.M14 + m.M11,
                m.M24 + m.M21,
                m.M34 + m.M31,
                m.M44 + m.M41));

            // Right Plane: col3 - col0
            _planes[1] = NormalizePlane(new Plane(
                m.M14 - m.M11,
                m.M24 - m.M21,
                m.M34 - m.M31,
                m.M44 - m.M41));

            // Bottom Plane: col3 + col1
            _planes[2] = NormalizePlane(new Plane(
                m.M14 + m.M12,
                m.M24 + m.M22,
                m.M34 + m.M32,
                m.M44 + m.M42));

            // Top Plane: col3 - col1
            _planes[3] = NormalizePlane(new Plane(
                m.M14 - m.M12,
                m.M24 - m.M22,
                m.M34 - m.M32,
                m.M44 - m.M42));

            // Near Plane (Z >= 0 in DirectX/Veldrid): col2
            _planes[4] = NormalizePlane(new Plane(
                m.M13,
                m.M23,
                m.M33,
                m.M43));

            // Far Plane (Z <= W in DirectX/Veldrid): col3 - col2
            _planes[5] = NormalizePlane(new Plane(
                m.M14 - m.M13,
                m.M24 - m.M23,
                m.M34 - m.M33,
                m.M44 - m.M43));
        }

        /// <summary>
        /// Tests whether an Axis-Aligned Bounding Box (AABB) intersects or lies inside the frustum.
        /// </summary>
        /// <param name="min">Minimum corner of the bounding box.</param>
        /// <param name="max">Maximum corner of the bounding box.</param>
        /// <returns>True if any part of the box is inside or intersecting the frustum; otherwise false.</returns>
        public bool IntersectsBox(Vector3 min, Vector3 max)
        {
            if (float.IsNaN(min.X) || float.IsNaN(min.Y) || float.IsNaN(min.Z) ||
                float.IsNaN(max.X) || float.IsNaN(max.Y) || float.IsNaN(max.Z) ||
                float.IsInfinity(min.X) || float.IsInfinity(min.Y) || float.IsInfinity(min.Z) ||
                float.IsInfinity(max.X) || float.IsInfinity(max.Y) || float.IsInfinity(max.Z))
            {
                return false;
            }

            for (int i = 0; i < 6; i++)
            {
                var plane = _planes[i];
                var normal = plane.Normal;

                // Positive vertex in direction of normal
                float px = normal.X >= 0 ? max.X : min.X;
                float py = normal.Y >= 0 ? max.Y : min.Y;
                float pz = normal.Z >= 0 ? max.Z : min.Z;

                if (Vector3.Dot(normal, new Vector3(px, py, pz)) + plane.D < 0)
                {
                    // Box is completely behind this plane
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Tests whether a single 3D point lies inside the frustum.
        /// </summary>
        public bool ContainsPoint(Vector3 point)
        {
            for (int i = 0; i < 6; i++)
            {
                var plane = _planes[i];
                if (Vector3.Dot(plane.Normal, point) + plane.D < 0)
                {
                    return false;
                }
            }

            return true;
        }

        private static Plane NormalizePlane(Plane plane)
        {
            float length = plane.Normal.Length();
            if (length <= 0.00001f)
            {
                return plane;
            }

            float invLen = 1.0f / length;
            return new Plane(plane.Normal * invLen, plane.D * invLen);
        }
    }
}
