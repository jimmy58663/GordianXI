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

        /// <summary>
        /// The zone's moving platforms (elevators); set once when the zone loads.
        /// </summary>
        public IReadOnlyList<MovingPlatform> MovingPlatforms { get; internal set; } = Array.Empty<MovingPlatform>();

        /// <summary>
        /// The level walkable floors (|normal.Y| above 0.7) whose centroid lies in the XZ rectangle: height, area, XZ centroid.
        /// </summary>
        public IEnumerable<(float Height, float Area, Vector2 Centroid)> FloorsNear(Vector2 min, Vector2 max)
        {
            if (_triangles.Length == 0) yield break;
            var seen = new HashSet<int>();
            int cx0 = Math.Max((int)MathF.Floor((min.X - _minX) / CellSize), 0);
            int cx1 = Math.Min((int)MathF.Floor((max.X - _minX) / CellSize), _columns - 1);
            int cz0 = Math.Max((int)MathF.Floor((min.Y - _minZ) / CellSize), 0);
            int cz1 = Math.Min((int)MathF.Floor((max.Y - _minZ) / CellSize), _rows - 1);
            for (int cz = cz0; cz <= cz1; cz++)
            {
                for (int cx = cx0; cx <= cx1; cx++)
                {
                    int cell = (cz * _columns) + cx;
                    for (int k = _cellStart[cell], end = _cellStart[cell + 1]; k < end; k++)
                    {
                        int index = _cellTriangles[k];
                        var t = _triangles[index];
                        if (t.Normal.Y > -0.7f || !seen.Add(index)) continue;
                        var centroid = (t.A + t.B + t.C) / 3.0f;
                        if (centroid.X < min.X || centroid.X > max.X || centroid.Z < min.Y || centroid.Z > max.Y) continue;
                        yield return (centroid.Y, 0.5f * Vector3.Cross(t.B - t.A, t.C - t.A).Length(), new Vector2(centroid.X, centroid.Z));
                    }
                }
            }
        }
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
        /// Casts the segment <paramref name="start"/> -> <paramref name="end"/> (internal space) against the soup and
        /// returns the fraction along it of the first triangle hit (either face). With
        /// <paramref name="skipCameraTransparent"/>, triangles the camera passes through (the collision block's camera
        /// transparency bit) are ignored. Only the grid cells under the segment's XZ bounds are searched.
        /// </summary>
        public bool TryRaycast(Vector3 start, Vector3 end, bool skipCameraTransparent, out float fraction)
        {
            fraction = 1.0f;
            if (_triangles.Length == 0) return false;
            var dir = end - start;
            if (dir.LengthSquared() < 1e-10f) return false;

            int x0 = Math.Clamp((int)MathF.Floor((MathF.Min(start.X, end.X) - _minX) / CellSize), 0, _columns - 1);
            int x1 = Math.Clamp((int)MathF.Floor((MathF.Max(start.X, end.X) - _minX) / CellSize), 0, _columns - 1);
            int z0 = Math.Clamp((int)MathF.Floor((MathF.Min(start.Z, end.Z) - _minZ) / CellSize), 0, _rows - 1);
            int z1 = Math.Clamp((int)MathF.Floor((MathF.Max(start.Z, end.Z) - _minZ) / CellSize), 0, _rows - 1);

            bool found = false;
            for (int z = z0; z <= z1; z++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    int cell = (z * _columns) + x;
                    for (int k = _cellStart[cell], stop = _cellStart[cell + 1]; k < stop; k++)
                    {
                        ref readonly var t = ref _triangles[_cellTriangles[k]];
                        if (skipCameraTransparent && t.CameraTransparent) continue;
                        if (IntersectSegment(t, start, dir, out float f) && f < fraction)
                        {
                            fraction = f;
                            found = true;
                        }
                    }
                }
            }
            return found;
        }

        // Moller-Trumbore, double-sided, t in [0, 1].
        private static bool IntersectSegment(in CollisionTriangle t, Vector3 origin, Vector3 dir, out float fraction)
        {
            fraction = 0.0f;
            var e1 = t.B - t.A;
            var e2 = t.C - t.A;
            var p = Vector3.Cross(dir, e2);
            float det = Vector3.Dot(e1, p);
            if (MathF.Abs(det) < 1e-9f) return false;
            float inv = 1.0f / det;
            var s = origin - t.A;
            float u = Vector3.Dot(s, p) * inv;
            if (u < 0.0f || u > 1.0f) return false;
            var q = Vector3.Cross(s, e1);
            float v = Vector3.Dot(dir, q) * inv;
            if (v < 0.0f || u + v > 1.0f) return false;
            fraction = Vector3.Dot(e2, q) * inv;
            return fraction >= 0.0f && fraction <= 1.0f;
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
        /// Triangles whose normal is flatter than this (|normal.Y| below it: walls, cliffs, steep banks) block horizontal
        /// movement; see <see cref="ResolveWalls"/>.
        /// </summary>
        public const float MaxWallNormalY = 0.5f;

        /// <summary>
        /// Moves a body from <paramref name="start"/> toward <paramref name="end"/> (same height, internal space) and
        /// returns where it ends up after being pushed out of walls, sliding along them. The body is a column of spheres
        /// of <paramref name="radius"/> from <paramref name="stepUp"/> above the feet to <paramref name="bodyHeight"/>,
        /// so anything lower than a step (stair risers, curbs) is left to ground following. Walls block only from their
        /// front face, as the client's collision is one-sided: a body behind a face walks out through it.
        /// </summary>
        public Vector3 ResolveWalls(Vector3 start, Vector3 end, float radius, float stepUp, float bodyHeight)
        {
            if (_triangles.Length == 0 || radius <= 0.0f) return end;

            Vector2 delta = new(end.X - start.X, end.Z - start.Z);
            float length = delta.Length();
            int steps = Math.Max(1, (int)MathF.Ceiling(length / (radius * 0.5f)));
            Vector2 stepDelta = delta / steps;

            // Sphere centers above the feet (internal -Y is up): the lowest clears a step, the highest reaches the head.
            float lowest = stepUp + radius;
            float highest = MathF.Max(lowest, bodyHeight - radius);
            int sphereCount = Math.Max(1, (int)MathF.Ceiling((highest - lowest) / radius) + 1);

            var position = new Vector2(start.X, start.Z);
            for (int step = 0; step < steps; step++)
            {
                var before = position;
                position += stepDelta;
                for (int pass = 0; pass < 4; pass++)
                {
                    bool pushed = false;
                    for (int sphere = 0; sphere < sphereCount; sphere++)
                    {
                        float lift = sphereCount == 1 ? lowest : lowest + ((highest - lowest) * sphere / (sphereCount - 1));
                        var center = new Vector3(position.X, start.Y - lift, position.Y);
                        if (PushOutOfWalls(ref center, radius))
                        {
                            position = new Vector2(center.X, center.Z);
                            pushed = true;
                        }
                    }
                    if (!pushed) break;
                }

                // Pushes from several faces at once (a body wedged against a prop) can add up past a thin wall: never
                // let a sub-step carry the body through the front of a wall; stop where it was instead.
                if (CrossesWallFront(before, position, start.Y - lowest, start.Y - highest))
                {
                    position = before;
                    break;
                }
            }

            return new Vector3(position.X, end.Y, position.Y);
        }

        /// <summary>
        /// Whether moving horizontally from <paramref name="from"/> to <paramref name="to"/> at either body height passes
        /// through the front face of a wall.
        /// </summary>
        private bool CrossesWallFront(Vector2 from, Vector2 to, float lowY, float highY)
        {
            Vector2 move = to - from;
            float length = move.Length();
            if (length < 1e-5f) return false;
            var direction = new Vector3(move.X / length, 0.0f, move.Y / length);

            int cx0 = Math.Max((int)MathF.Floor((MathF.Min(from.X, to.X) - _minX) / CellSize), 0);
            int cx1 = Math.Min((int)MathF.Floor((MathF.Max(from.X, to.X) - _minX) / CellSize), _columns - 1);
            int cz0 = Math.Max((int)MathF.Floor((MathF.Min(from.Y, to.Y) - _minZ) / CellSize), 0);
            int cz1 = Math.Min((int)MathF.Floor((MathF.Max(from.Y, to.Y) - _minZ) / CellSize), _rows - 1);
            for (int cz = cz0; cz <= cz1; cz++)
            {
                for (int cx = cx0; cx <= cx1; cx++)
                {
                    int cell = (cz * _columns) + cx;
                    for (int k = _cellStart[cell], end = _cellStart[cell + 1]; k < end; k++)
                    {
                        ref readonly var t = ref _triangles[_cellTriangles[k]];
                        if (MathF.Abs(t.Normal.Y) >= MaxWallNormalY) continue;
                        if (Vector3.Dot(direction, t.Normal) >= 0.0f) continue; // moving out of the face, not into it
                        float low = RayTriangleDistance(new Vector3(from.X, lowY, from.Y), direction, t.A, t.B, t.C);
                        if (low >= 0.0f && low <= length) return true;
                        float high = RayTriangleDistance(new Vector3(from.X, highY, from.Y), direction, t.A, t.B, t.C);
                        if (high >= 0.0f && high <= length) return true;
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// Distance along a ray to a triangle (Möller-Trumbore, double-sided), or -1 on a miss.
        /// </summary>
        private static float RayTriangleDistance(Vector3 origin, Vector3 direction, Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 edge1 = b - a, edge2 = c - a;
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

        /// <summary>
        /// Pushes a sphere horizontally out of every wall triangle it penetrates from the front; true when it moved.
        /// </summary>
        private bool PushOutOfWalls(ref Vector3 center, float radius)
        {
            int cx0 = Math.Max((int)MathF.Floor((center.X - radius - _minX) / CellSize), 0);
            int cx1 = Math.Min((int)MathF.Floor((center.X + radius - _minX) / CellSize), _columns - 1);
            int cz0 = Math.Max((int)MathF.Floor((center.Z - radius - _minZ) / CellSize), 0);
            int cz1 = Math.Min((int)MathF.Floor((center.Z + radius - _minZ) / CellSize), _rows - 1);

            bool moved = false;
            float radiusSquared = radius * radius;
            for (int cz = cz0; cz <= cz1; cz++)
            {
                for (int cx = cx0; cx <= cx1; cx++)
                {
                    int cell = (cz * _columns) + cx;
                    for (int k = _cellStart[cell], end = _cellStart[cell + 1]; k < end; k++)
                    {
                        ref readonly var t = ref _triangles[_cellTriangles[k]];
                        if (MathF.Abs(t.Normal.Y) >= MaxWallNormalY) continue;
                        if (Vector3.Dot(center - t.A, t.Normal) <= 0.0f) continue; // behind the face

                        Vector3 closest = ClosestPointOnTriangle(center, t.A, t.B, t.C);
                        Vector3 away = center - closest;
                        if (away.LengthSquared() >= radiusSquared) continue;

                        // Separate horizontally only: the body keeps its height, ground following owns that.
                        var horizontal = new Vector2(away.X, away.Z);
                        float horizontalDistance = horizontal.Length();
                        float needed = MathF.Sqrt(MathF.Max(radiusSquared - (away.Y * away.Y), 0.0f));
                        Vector2 direction = horizontalDistance > 1e-4f
                            ? horizontal / horizontalDistance
                            : Vector2.Normalize(new Vector2(t.Normal.X, t.Normal.Z));
                        float push = needed - horizontalDistance + 1e-3f;
                        if (push <= 0.0f || float.IsNaN(direction.X)) continue;
                        center += new Vector3(direction.X * push, 0.0f, direction.Y * push);
                        moved = true;
                    }
                }
            }
            return moved;
        }

        /// <summary>
        /// The point of triangle ABC closest to P (Ericson, Real-Time Collision Detection, 5.1.5).
        /// </summary>
        private static Vector3 ClosestPointOnTriangle(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 ab = b - a, ac = c - a, ap = p - a;
            float d1 = Vector3.Dot(ab, ap), d2 = Vector3.Dot(ac, ap);
            if (d1 <= 0.0f && d2 <= 0.0f) return a;

            Vector3 bp = p - b;
            float d3 = Vector3.Dot(ab, bp), d4 = Vector3.Dot(ac, bp);
            if (d3 >= 0.0f && d4 <= d3) return b;

            float vc = (d1 * d4) - (d3 * d2);
            if (vc <= 0.0f && d1 >= 0.0f && d3 <= 0.0f) return a + (ab * (d1 / (d1 - d3)));

            Vector3 cp = p - c;
            float d5 = Vector3.Dot(ab, cp), d6 = Vector3.Dot(ac, cp);
            if (d6 >= 0.0f && d5 <= d6) return c;

            float vb = (d5 * d2) - (d1 * d6);
            if (vb <= 0.0f && d2 >= 0.0f && d6 <= 0.0f) return a + (ac * (d2 / (d2 - d6)));

            float va = (d3 * d6) - (d5 * d4);
            if (va <= 0.0f && (d4 - d3) >= 0.0f && (d5 - d6) >= 0.0f)
                return b + ((c - b) * ((d4 - d3) / ((d4 - d3) + (d5 - d6))));

            float denominator = 1.0f / (va + vb + vc);
            return a + (ab * (vb * denominator)) + (ac * (vc * denominator));
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
