// src/Gordian.Core/World/Collision/ZoneCollisionMesh.cs
using System;
using System.Collections.Generic;
using System.Numerics;

namespace Gordian.Core.World.Collision
{
    /// <summary>
    /// Surface type of a collision triangle; drives footstep effects and swimming, not blocking.
    /// Terrain indices referenced from xi-tools (docs/zone/collision.md, https://github.com/vekien/xi-tools).
    /// </summary>
    public enum CollisionTerrain : byte
    {
        Object = 0,
        Path = 1,
        Grass = 2,
        Sand = 3,
        Snow = 4,
        Stone = 5,
        Metal = 6,
        Wood = 7,
        ShallowWater = 8,
        DeepWater = 9,
        Unknown10 = 10,
    }

    /// <summary>
    /// One world-space collision triangle (internal space: X, Y = height with -Y up, Z).
    /// </summary>
    /// <param name="Normal">Unit normal on the solid/walkable side; a floor's normal has negative Y.</param>
    /// <param name="IsWall">The triangle's wall material bit (both walls and floors block movement).</param>
    /// <param name="CameraTransparent">The camera and line of sight pass through this triangle.</param>
    public readonly record struct CollisionTriangle(
        Vector3 A,
        Vector3 B,
        Vector3 C,
        Vector3 Normal,
        bool IsWall,
        CollisionTerrain Terrain,
        bool CameraTransparent);

    /// <summary>
    /// A ground hit below a query point.
    /// </summary>
    /// <param name="Height">Internal-space Y of the surface (smaller is higher).</param>
    public readonly record struct GroundHit(float Height, Vector3 Normal, CollisionTerrain Terrain, int TriangleIndex);

    /// <summary>
    /// A zone's player-collision triangle soup indexed by a uniform XZ grid, so a query touches only the triangles in
    /// the cells around the query point instead of the whole zone.
    /// </summary>
    public sealed class ZoneCollisionMesh
    {
        /// <summary>
        /// Grid cell edge length in yalms.
        /// </summary>
        public const float CellSize = 4.0f;

        /// <summary>
        /// Triangles whose normal is flatter than this (|normal.Y|) are walls for ground queries.
        /// </summary>
        public const float MinFloorNormalY = 0.1f;

        private readonly CollisionTriangle[] _triangles;
        private readonly float _minX;
        private readonly float _minZ;
        private readonly int _columns;
        private readonly int _rows;

        // CSR layout: cell c owns _cellTriangles[_cellStart[c] .. _cellStart[c + 1]).
        private readonly int[] _cellStart;
        private readonly int[] _cellTriangles;

        public ZoneCollisionMesh(IReadOnlyList<CollisionTriangle> triangles)
        {
            ArgumentNullException.ThrowIfNull(triangles);
            _triangles = new CollisionTriangle[triangles.Count];
            for (int i = 0; i < triangles.Count; i++) _triangles[i] = triangles[i];

            if (_triangles.Length == 0)
            {
                _columns = _rows = 1;
                _cellStart = new int[2];
                _cellTriangles = Array.Empty<int>();
                return;
            }

            var min = new Vector3(float.MaxValue);
            var max = new Vector3(float.MinValue);
            for (int i = 0; i < _triangles.Length; i++)
            {
                ref readonly var t = ref _triangles[i];
                min = Vector3.Min(min, Vector3.Min(t.A, Vector3.Min(t.B, t.C)));
                max = Vector3.Max(max, Vector3.Max(t.A, Vector3.Max(t.B, t.C)));
            }
            MinBounds = min;
            MaxBounds = max;
            _minX = min.X;
            _minZ = min.Z;
            _columns = Math.Max(1, (int)MathF.Ceiling((max.X - min.X) / CellSize) + 1);
            _rows = Math.Max(1, (int)MathF.Ceiling((max.Z - min.Z) / CellSize) + 1);

            int cellCount = _columns * _rows;
            _cellStart = new int[cellCount + 1];
            for (int i = 0; i < _triangles.Length; i++)
            {
                GetCellRange(_triangles[i], out int x0, out int z0, out int x1, out int z1);
                for (int z = z0; z <= z1; z++)
                    for (int x = x0; x <= x1; x++)
                        _cellStart[(z * _columns) + x + 1]++;
            }
            for (int c = 0; c < cellCount; c++) _cellStart[c + 1] += _cellStart[c];

            _cellTriangles = new int[_cellStart[cellCount]];
            var fill = new int[cellCount];
            Array.Copy(_cellStart, fill, cellCount);
            for (int i = 0; i < _triangles.Length; i++)
            {
                GetCellRange(_triangles[i], out int x0, out int z0, out int x1, out int z1);
                for (int z = z0; z <= z1; z++)
                    for (int x = x0; x <= x1; x++)
                        _cellTriangles[fill[(z * _columns) + x]++] = i;
            }
        }

        public ReadOnlySpan<CollisionTriangle> Triangles => _triangles;
        public int TriangleCount => _triangles.Length;
        public Vector3 MinBounds { get; }
        public Vector3 MaxBounds { get; }

        /// <summary>
        /// Finds the walkable surface a character standing at <paramref name="feet"/> rests on: the highest up-facing
        /// surface under the XZ point that is at most <paramref name="stepUp"/> above the feet and at most
        /// <paramref name="maxDrop"/> below them. Surfaces further above (a bridge overhead, an upper floor) are ignored.
        /// </summary>
        public bool TryGetGround(Vector3 feet, float stepUp, float maxDrop, out GroundHit hit)
        {
            hit = default;
            if (!TryGetCell(feet.X, feet.Z, out int cell)) return false;

            float highestAllowed = feet.Y - stepUp; // internal -Y is up
            float lowestAllowed = feet.Y + maxDrop;
            float best = float.MaxValue;
            bool found = false;

            for (int k = _cellStart[cell], end = _cellStart[cell + 1]; k < end; k++)
            {
                int index = _cellTriangles[k];
                ref readonly var t = ref _triangles[index];
                if (t.Normal.Y > -MinFloorNormalY) continue; // walls and downward-facing ceilings
                if (!TryHeightAt(t, feet.X, feet.Z, out float height)) continue;
                if (height < highestAllowed || height > lowestAllowed) continue;
                if (height < best)
                {
                    best = height;
                    hit = new GroundHit(height, t.Normal, t.Terrain, index);
                    found = true;
                }
            }
            return found;
        }

        /// <summary>
        /// Treads whose corners differ in height by less than this (level surfaces: stair steps, curbs, platforms) round
        /// the player over their edges; see <see cref="TryGetSteppedGround"/>.
        /// </summary>
        public const float MaxTreadTilt = 0.01f;

        /// <summary>
        /// Like <see cref="TryGetGround"/>, but the player's feet rest on a sphere of <paramref name="footRadius"/>, so
        /// a flat tread above the floor lifts them as they come within that radius of its edge, and lowers them the same
        /// way leaving it: stairs and curbs climb and descend as a ramp instead of a staircase of jumps. Only flat treads
        /// round like this; slopes keep their exact height, so the player never floats above an incline.
        /// Calibrated against a Windower capture on Southern San d'Oria's stairs (0.25-yalm risers): a 0.9-yalm sphere
        /// matches the retail heights to within 0.03 yalms on average, where snapping to each tread misses by 0.21.
        /// </summary>
        public bool TryGetSteppedGround(Vector3 feet, float stepUp, float maxDrop, float footRadius, out GroundHit hit)
        {
            if (!TryGetGround(feet, stepUp, maxDrop, out hit)) return false;
            if (footRadius <= 0.0f) return true;

            // Internal space: smaller Y is higher. Consider treads above the floor but no higher than a step.
            float floor = hit.Height;
            float highestAllowed = feet.Y - stepUp;
            float radiusSquared = footRadius * footRadius;
            float best = floor;
            int bestIndex = -1;

            int cx0 = (int)MathF.Floor((feet.X - footRadius - _minX) / CellSize);
            int cx1 = (int)MathF.Floor((feet.X + footRadius - _minX) / CellSize);
            int cz0 = (int)MathF.Floor((feet.Z - footRadius - _minZ) / CellSize);
            int cz1 = (int)MathF.Floor((feet.Z + footRadius - _minZ) / CellSize);
            cx0 = Math.Max(cx0, 0); cz0 = Math.Max(cz0, 0);
            cx1 = Math.Min(cx1, _columns - 1); cz1 = Math.Min(cz1, _rows - 1);

            for (int cz = cz0; cz <= cz1; cz++)
            {
                for (int cx = cx0; cx <= cx1; cx++)
                {
                    int cell = (cz * _columns) + cx;
                    for (int k = _cellStart[cell], end = _cellStart[cell + 1]; k < end; k++)
                    {
                        int index = _cellTriangles[k];
                        ref readonly var t = ref _triangles[index];
                        if (t.Normal.Y > -MinFloorNormalY) continue;
                        float height = t.A.Y;
                        if (MathF.Abs(t.B.Y - height) > MaxTreadTilt || MathF.Abs(t.C.Y - height) > MaxTreadTilt) continue;
                        if (height >= floor - 1e-3f || height < highestAllowed) continue;

                        float distanceSquared = DistanceSquaredXZ(t, feet.X, feet.Z);
                        if (distanceSquared >= radiusSquared) continue;
                        float feetOnEdge = height + footRadius - MathF.Sqrt(radiusSquared - distanceSquared);
                        if (feetOnEdge < best)
                        {
                            best = feetOnEdge;
                            bestIndex = index;
                        }
                    }
                }
            }

            if (bestIndex >= 0)
            {
                ref readonly var tread = ref _triangles[bestIndex];
                hit = new GroundHit(best, tread.Normal, tread.Terrain, bestIndex);
            }
            return true;
        }

        /// <summary>
        /// Squared horizontal distance from (x, z) to the triangle's XZ projection (0 inside it).
        /// </summary>
        private static float DistanceSquaredXZ(in CollisionTriangle t, float x, float z)
        {
            var p = new Vector2(x, z);
            var a = new Vector2(t.A.X, t.A.Z);
            var b = new Vector2(t.B.X, t.B.Z);
            var c = new Vector2(t.C.X, t.C.Z);
            float d1 = Cross(b - a, p - a), d2 = Cross(c - b, p - b), d3 = Cross(a - c, p - c);
            bool hasNegative = d1 < 0 || d2 < 0 || d3 < 0;
            bool hasPositive = d1 > 0 || d2 > 0 || d3 > 0;
            if (!(hasNegative && hasPositive)) return 0.0f;
            return MathF.Min(SegmentDistanceSquared(p, a, b), MathF.Min(SegmentDistanceSquared(p, b, c), SegmentDistanceSquared(p, c, a)));
        }

        private static float Cross(Vector2 u, Vector2 v) => (u.X * v.Y) - (u.Y * v.X);

        private static float SegmentDistanceSquared(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float lengthSquared = ab.LengthSquared();
            float t = lengthSquared > 1e-12f ? Math.Clamp(Vector2.Dot(p - a, ab) / lengthSquared, 0.0f, 1.0f) : 0.0f;
            return Vector2.DistanceSquared(p, a + (ab * t));
        }

        /// <summary>
        /// Finds the walkable surface under the XZ point nearest in height to <paramref name="referenceHeight"/>, e.g. to
        /// land a teleport on the floor of the level it was aimed at.
        /// </summary>
        public bool TryGetNearestGround(float x, float z, float referenceHeight, out GroundHit hit)
        {
            hit = default;
            if (!TryGetCell(x, z, out int cell)) return false;

            float bestDistance = float.MaxValue;
            for (int k = _cellStart[cell], end = _cellStart[cell + 1]; k < end; k++)
            {
                int index = _cellTriangles[k];
                ref readonly var t = ref _triangles[index];
                if (t.Normal.Y > -MinFloorNormalY) continue;
                if (!TryHeightAt(t, x, z, out float height)) continue;
                float distance = MathF.Abs(height - referenceHeight);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    hit = new GroundHit(height, t.Normal, t.Terrain, index);
                }
            }
            return bestDistance < float.MaxValue;
        }

        /// <summary>
        /// Height of the triangle's plane at (x, z) when that point lies inside the triangle's XZ projection.
        /// </summary>
        private static bool TryHeightAt(in CollisionTriangle t, float x, float z, out float height)
        {
            height = 0.0f;
            float d1x = t.B.X - t.A.X, d1z = t.B.Z - t.A.Z;
            float d2x = t.C.X - t.A.X, d2z = t.C.Z - t.A.Z;
            float det = (d1x * d2z) - (d2x * d1z);
            if (MathF.Abs(det) < 1e-8f) return false;

            float px = x - t.A.X, pz = z - t.A.Z;
            float u = ((px * d2z) - (d2x * pz)) / det;
            float v = ((d1x * pz) - (px * d1z)) / det;
            const float Epsilon = -1e-5f;
            if (u < Epsilon || v < Epsilon || u + v > 1.0f - Epsilon) return false;

            height = t.A.Y + (u * (t.B.Y - t.A.Y)) + (v * (t.C.Y - t.A.Y));
            return true;
        }

        private bool TryGetCell(float x, float z, out int cell)
        {
            cell = 0;
            if (_triangles.Length == 0) return false;
            int cx = (int)MathF.Floor((x - _minX) / CellSize);
            int cz = (int)MathF.Floor((z - _minZ) / CellSize);
            if ((uint)cx >= (uint)_columns || (uint)cz >= (uint)_rows) return false;
            cell = (cz * _columns) + cx;
            return true;
        }

        private void GetCellRange(in CollisionTriangle t, out int x0, out int z0, out int x1, out int z1)
        {
            float minX = MathF.Min(t.A.X, MathF.Min(t.B.X, t.C.X));
            float maxX = MathF.Max(t.A.X, MathF.Max(t.B.X, t.C.X));
            float minZ = MathF.Min(t.A.Z, MathF.Min(t.B.Z, t.C.Z));
            float maxZ = MathF.Max(t.A.Z, MathF.Max(t.B.Z, t.C.Z));
            x0 = Math.Clamp((int)MathF.Floor((minX - _minX) / CellSize), 0, _columns - 1);
            x1 = Math.Clamp((int)MathF.Floor((maxX - _minX) / CellSize), 0, _columns - 1);
            z0 = Math.Clamp((int)MathF.Floor((minZ - _minZ) / CellSize), 0, _rows - 1);
            z1 = Math.Clamp((int)MathF.Floor((maxZ - _minZ) / CellSize), 0, _rows - 1);
        }
    }
}
