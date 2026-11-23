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
    /// <param name="CullingTable">The ZoneDef culling table its collision transform names at +0xA8 (the table a camera
    /// over this floor draws with, #70); -1 for none.</param>
    /// <param name="MapFloor">The zone map (the <c>m_ZZZ_FF</c> number) its collision transform names at +0xA4 (the
    /// floor the Map window shows for a character standing here, #481); 0 for none.</param>
    /// <param name="Interior">The triangle is a building interior's (<see cref="ZoneCollisionMesh.Merge"/>): under a roof, where
    /// rain does not wet the floor.</param>
    public readonly record struct CollisionTriangle(
        Vector3 A,
        Vector3 B,
        Vector3 C,
        Vector3 Normal,
        bool IsWall,
        CollisionTerrain Terrain,
        bool CameraTransparent,
        int CullingTable = -1,
        int MapFloor = 0,
        bool Interior = false);

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
        /// Triangles whose normal is flatter than this (|normal.Y|, faces steeper than 84 degrees) are walls for ground
        /// queries. Faces of 60-84 degrees stay ground, so the player walks down steep banks and ledges (Bastok Mines at
        /// (-159.69, -2.69, -28.52)) and over the 60-degree bevels of Southern San d'Oria's ramp strip from the ramp, as
        /// retail does (2026-11-22 test round 1, #632). Vertical faces lower than a step are handled by
        /// <see cref="CrossesLowWall"/>.
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

        private static bool InsideAnyFootprint(in CollisionTriangle triangle, IReadOnlyList<ZoneCollisionMesh> interiors)
        {
            var centroid = (triangle.A + triangle.B + triangle.C) / 3f;
            foreach (var interior in interiors)
            {
                if (interior._triangles.Length == 0) continue;
                var min = interior.MinBounds;
                var max = interior.MaxBounds;
                // A floor over the interior's top stays: the street over a basement room (#468).
                if (triangle.Normal.Y < -MinFloorNormalY && centroid.Y < min.Y) continue;
                if (centroid.X > min.X + InteriorFootprintInset && centroid.X < max.X - InteriorFootprintInset &&
                    centroid.Z > min.Z + InteriorFootprintInset && centroid.Z < max.Z - InteriorFootprintInset &&
                    centroid.Y > min.Y - 1f && centroid.Y < max.Y + 1f &&
                    // Only where the interior has its own floor: an interior's bounds can reach over the street beside it.
                    interior.TryGetNearestGround(centroid.X, centroid.Z, centroid.Y, out _)) return true;
            }
            return false;
        }

        /// <summary>How far inside an interior's bounds (yalms, horizontally) the zone's own triangles give way to the interior's.</summary>
        public const float InteriorFootprintInset = 0.5f;

        /// <summary>
        /// A zone's collision with building interiors added (#68): the zone's triangles, except those inside an interior's
        /// footprint, then each interior's, and the zone's platforms, doors, zone lines and sub-area volumes. Inside the
        /// footprint (the interior's bounds, <see cref="InteriorFootprintInset"/> in from its sides, between its floor and
        /// ceiling with a yalm to spare) the zone's own collision is the closed building's shell: Lower Jeuno's chocobo
        /// stables have zone walls running through the room the interior opens (round 3: blocked at (-87.19, -0.10,
        /// -186.15) where the stables interior has floor), so collision switches with the drawing, as the issue's format
        /// notes say it must. Walls on the footprint's edge (the shop fronts, the doorway) stay, and so does anything over
        /// ground the interior has no floor for. So do the zone's floors above the interior's top: Bastok Metalworks' interior
        /// 310 (<c>m6li</c>) is a room 0.56 to 8 yalms below the street, and the yalm to spare above it took the street's floor
        /// over it, so the player fell through to Elayne's floor (#468).
        /// </summary>
        public static ZoneCollisionMesh Merge(ZoneCollisionMesh zone, IReadOnlyList<ZoneCollisionMesh> interiors)
        {
            ArgumentNullException.ThrowIfNull(zone);
            ArgumentNullException.ThrowIfNull(interiors);
            var triangles = new List<CollisionTriangle>(zone._triangles.Length + 8192);
            foreach (var triangle in zone._triangles)
            {
                if (!InsideAnyFootprint(triangle, interiors)) triangles.Add(triangle);
            }
            // An interior's culling tables are its own DAT's, not the zone's: its floors pick none. Its triangles are marked
            // as indoors (no rain foot effects there).
            foreach (var interior in interiors)
            {
                foreach (var triangle in interior._triangles) triangles.Add(triangle with { CullingTable = -1, Interior = true });
            }
            return new ZoneCollisionMesh(triangles)
            {
                MovingPlatforms = zone.MovingPlatforms,
                Doors = zone.Doors,
                ZoneLineVolumes = zone.ZoneLineVolumes,
                SubAreaVolumes = zone.SubAreaVolumes,
            };
        }

        /// <summary>
        /// The zone's moving platforms (elevators); set once when the zone loads.
        /// </summary>
        public IReadOnlyList<MovingPlatform> MovingPlatforms { get; internal set; } = Array.Empty<MovingPlatform>();

        /// <summary>
        /// The zone's doorways, solid while their door is closed (see <see cref="ZoneDoors"/>).
        /// </summary>
        public IReadOnlyList<DoorBlocker> Doors { get; internal set; } = Array.Empty<DoorBlocker>();

        /// <summary>
        /// The zone's zone-line volumes (Section 0x36 <c>z</c> records from every table), see <see cref="ZoneLines"/>.
        /// </summary>
        public IReadOnlyList<Resources.Graphics.ZoneInteraction> ZoneLineVolumes { get; internal set; } = Array.Empty<Resources.Graphics.ZoneInteraction>();

        /// <summary>
        /// The zone's building-interior volumes (Section 0x36 <c>'m'</c> records, <see cref="ZoneSubAreas"/>, #68): standing in
        /// one shows its interior.
        /// </summary>
        public IReadOnlyList<Resources.Graphics.ZoneInteraction> SubAreaVolumes { get; internal set; } = Array.Empty<Resources.Graphics.ZoneInteraction>();

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

        /// <summary>
        /// Two triangles sharing an edge are one plane when their normals' dot product is above this (about 1.1 degrees apart).
        /// </summary>
        public const float CoplanarNormalDot = 0.9998f;

        /// <summary>
        /// Floors steeper than this (degrees) get a plane area (<see cref="GetPlaneArea"/>); flatter ones report 0.
        /// </summary>
        public const float PlaneAreaMinSlopeDegrees = 15.0f;

        // Vertices closer than 1/20 yalm are the same vertex when edges are matched.
        private const float PlaneVertexQuantum = 20.0f;

        private float[]? _planeAreas;

        /// <summary>
        /// The area (square yalms) of the flat plane a sloped floor triangle is part of: the triangles joined to it through
        /// shared edges with the same normal (<see cref="CoplanarNormalDot"/>), each distinct triangle counted once. 0 when
        /// no other triangle lies in its plane (an exact duplicate, which the soup has on many faces, does not count), and
        /// for floors flatter than <see cref="PlaneAreaMinSlopeDegrees"/>, walls and ceilings. Built stairs and ramps are
        /// such planes; natural rock is irregular and its steep faces stand alone or in small pieces (#610 round 5). Computed
        /// for the whole mesh on first use.
        /// </summary>
        public float GetPlaneArea(int triangle)
        {
            if ((uint)triangle >= (uint)_triangles.Length) return 0.0f;
            var areas = _planeAreas;
            if (areas == null)
            {
                var computed = ComputePlaneAreas(_triangles);
                areas = System.Threading.Interlocked.CompareExchange(ref _planeAreas, computed, null) ?? computed;
            }
            return areas[triangle];
        }

        private static float[] ComputePlaneAreas(CollisionTriangle[] triangles)
        {
            var areas = new float[triangles.Length];
            float maxNormalY = -MathF.Cos(PlaneAreaMinSlopeDegrees * (MathF.PI / 180.0f));
            var parent = new int[triangles.Length];
            var faceOf = new Dictionary<(long, long, long), int>();
            var edges = new Dictionary<(long, long), List<int>>();
            for (int i = 0; i < triangles.Length; i++)
            {
                parent[i] = i;
                ref readonly var t = ref triangles[i];
                if (t.Normal.Y > -MinFloorNormalY || t.Normal.Y < maxNormalY) continue; // a wall, ceiling or gentle floor
                long a = VertexKey(t.A), b = VertexKey(t.B), c = VertexKey(t.C);
                var face = Sort(a, b, c);
                if (faceOf.TryGetValue(face, out int first)) { parent[i] = first; continue; } // a duplicate joins its original
                faceOf[face] = i;
                AddEdge(edges, a, b, i);
                AddEdge(edges, b, c, i);
                AddEdge(edges, c, a, i);
            }

            foreach (var sharing in edges.Values)
            {
                for (int x = 0; x < sharing.Count; x++)
                {
                    for (int y = x + 1; y < sharing.Count; y++)
                    {
                        if (Vector3.Dot(triangles[sharing[x]].Normal, triangles[sharing[y]].Normal) <= CoplanarNormalDot) continue;
                        int rx = Find(parent, sharing[x]), ry = Find(parent, sharing[y]);
                        if (rx != ry) parent[Math.Max(rx, ry)] = Math.Min(rx, ry);
                    }
                }
            }

            // Sum each plane's distinct faces; a plane of one face (with or without duplicates) has no area.
            var total = new Dictionary<int, (float Area, int Faces)>();
            foreach (int i in faceOf.Values)
            {
                ref readonly var t = ref triangles[i];
                int root = Find(parent, i);
                var sum = total.GetValueOrDefault(root);
                total[root] = (sum.Area + (0.5f * Vector3.Cross(t.B - t.A, t.C - t.A).Length()), sum.Faces + 1);
            }
            for (int i = 0; i < triangles.Length; i++)
            {
                if (total.TryGetValue(Find(parent, i), out var plane) && plane.Faces > 1) areas[i] = plane.Area;
            }
            return areas;

            static long VertexKey(Vector3 v) =>
                ((long)(MathF.Round(v.X * PlaneVertexQuantum) + (1 << 20)) << 42) |
                ((long)(MathF.Round(v.Y * PlaneVertexQuantum) + (1 << 20)) << 21) |
                (long)(MathF.Round(v.Z * PlaneVertexQuantum) + (1 << 20));

            static (long, long, long) Sort(long a, long b, long c)
            {
                if (a > b) (a, b) = (b, a);
                if (b > c) (b, c) = (c, b);
                if (a > b) (a, b) = (b, a);
                return (a, b, c);
            }

            static void AddEdge(Dictionary<(long, long), List<int>> edges, long a, long b, int triangle)
            {
                var key = a < b ? (a, b) : (b, a);
                if (!edges.TryGetValue(key, out var list)) edges[key] = list = new List<int>(2);
                list.Add(triangle);
            }

            static int Find(int[] parent, int i)
            {
                while (parent[i] != i) { parent[i] = parent[parent[i]]; i = parent[i]; }
                return i;
            }
        }

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
        /// How far above the feet (yalms) a wall face must reach to stop the feet (<see cref="CrossesLowWall"/>): a riser the
        /// foot rounding has lifted the feet onto, or a seam a hair proud of the floor, is passed.
        /// </summary>
        public const float LowWallTolerance = 0.01f;

        /// <summary>
        /// How far past a riser (yalms, along the move) <see cref="CrossesLowWall"/> looks for the surface on its top.
        /// </summary>
        public const float RiserTopProbe = 0.02f;

        /// <summary>
        /// Whether the feet, moving from <paramref name="start"/> to <paramref name="end"/> (internal space, feet heights),
        /// would step up a riser (a face too steep to stand on, |normal.Y| below <see cref="MinFloorNormalY"/>) lower than
        /// the body's wall spheres (<see cref="ResolveWalls"/> starts at <paramref name="stepUp"/>) onto a face of 60 degrees
        /// or steeper (|normal.Y| below <see cref="MaxWallNormalY"/>), which retail does not do (#632). Retail evidence
        /// (2026-11-22): Southern San d'Oria's ramp strip stops the player walking west from the gutter where a 0.02-0.1
        /// yalm riser at x = 8 carries the strip's 60.0-degree bevel (y 4.0-16 and from 28), and lets it climb where the
        /// bevel is 59.9 degrees with no riser (y 3.86); the player still steps up the 0.1-yalm curbs from the paving onto
        /// the 8.6-degree gutters, walks over the 60.0-degree bevel from the ramp itself (no riser), and climbs stairs.
        /// A riser is also passed where the foot rounding (<see cref="TryGetSteppedGround"/> with
        /// <paramref name="footRadius"/>) has already lifted the feet to its top. Moving down or away from a face is never
        /// tested. The test is geometric (the move's path against the face), so it does not depend on how far one tick
        /// moves. On a hit, <paramref name="wallNormal"/> is the nearest such riser's normal and <paramref name="distance"/>
        /// the horizontal distance along the move to it.
        /// </summary>
        public bool CrossesLowWall(Vector3 start, Vector3 end, float stepUp, float footRadius, float maxDrop, out Vector3 wallNormal, out float distance)
        {
            wallNormal = default;
            distance = float.MaxValue;
            if (_triangles.Length == 0) return false;
            Vector2 move = new(end.X - start.X, end.Z - start.Z);
            float length = move.Length();
            if (length < 1e-5f) return false;
            var direction = new Vector3(move.X / length, 0.0f, move.Y / length);

            // Walk the move's ground path in short samples, so a riser is met level with the feet at its foot however far
            // one tick moves (a riser of 0.02 yalm sits below a sloped gutter's feet a tick away).
            int samples = Math.Max(1, (int)MathF.Ceiling(length / LowWallSampleSpacing));
            float spacing = length / samples;
            float feet = start.Y;
            for (int i = 0; i < samples; i++)
            {
                float along = i * spacing;
                var at = new Vector3(start.X + (direction.X * along), feet, start.Z + (direction.Z * along));
                if (i > 0 && TryGetSteppedGround(at, stepUp, maxDrop, footRadius, out var path)) feet = path.Height;
                if (TryFindRiser(at with { Y = feet }, direction, spacing + 1e-3f, stepUp, footRadius, maxDrop, out wallNormal, out float hit))
                {
                    distance = along + hit;
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Horizontal distance along <paramref name="direction"/> (unit, Y = 0) from <paramref name="origin"/> to where the
        /// triangle's XZ outline is first crossed, or -1 when it is not.
        /// </summary>
        private static float EntryDistanceXZ(in CollisionTriangle t, Vector3 origin, Vector3 direction)
        {
            float best = float.MaxValue;
            Edge(t.A, t.B); Edge(t.B, t.C); Edge(t.C, t.A);
            return best < float.MaxValue ? best : -1.0f;

            void Edge(Vector3 p, Vector3 q)
            {
                float ex = q.X - p.X, ez = q.Z - p.Z;
                float denom = (direction.X * ez) - (direction.Z * ex);
                if (MathF.Abs(denom) < 1e-9f) return;
                float ox = p.X - origin.X, oz = p.Z - origin.Z;
                float along = ((ox * ez) - (oz * ex)) / denom;
                float u = ((ox * direction.Z) - (oz * direction.X)) / denom;
                if (along >= 0.0f && u >= 0.0f && u <= 1.0f && along < best) best = along;
            }
        }

        /// <summary>Path sample spacing (yalms) of <see cref="CrossesLowWall"/>.</summary>
        public const float LowWallSampleSpacing = 0.05f;

        /// <summary>
        /// The nearest riser within <paramref name="reach"/> ahead of <paramref name="feet"/> that the feet may not step up
        /// (<see cref="CrossesLowWall"/>).
        /// </summary>
        private bool TryFindRiser(Vector3 feet, Vector3 direction, float reach, float stepUp, float footRadius, float maxDrop, out Vector3 wallNormal, out float distance)
        {
            wallNormal = default;
            distance = float.MaxValue;
            float endX = feet.X + (direction.X * reach), endZ = feet.Z + (direction.Z * reach);
            int cx0 = Math.Max((int)MathF.Floor((MathF.Min(feet.X, endX) - _minX) / CellSize), 0);
            int cx1 = Math.Min((int)MathF.Floor((MathF.Max(feet.X, endX) - _minX) / CellSize), _columns - 1);
            int cz0 = Math.Max((int)MathF.Floor((MathF.Min(feet.Z, endZ) - _minZ) / CellSize), 0);
            int cz1 = Math.Min((int)MathF.Floor((MathF.Max(feet.Z, endZ) - _minZ) / CellSize), _rows - 1);
            for (int cz = cz0; cz <= cz1; cz++)
            {
                for (int cx = cx0; cx <= cx1; cx++)
                {
                    int cell = (cz * _columns) + cx;
                    for (int k = _cellStart[cell], stop = _cellStart[cell + 1]; k < stop; k++)
                    {
                        ref readonly var t = ref _triangles[_cellTriangles[k]];
                        if (MathF.Abs(t.Normal.Y) >= MinFloorNormalY) continue; // standable: the ground and climb rules own it
                        if (Vector3.Dot(direction, t.Normal) >= 0.0f) continue; // moving out of the face, not into it
                        // Where the move first meets the face seen from above (a riser is a sliver there), at any height.
                        float hit = EntryDistanceXZ(t, feet, direction);
                        if (hit < 0.0f || hit > reach || hit >= distance) continue;

                        // The feet at its foot: passed when the foot rounding has already lifted them over its top.
                        float back = MathF.Max(hit - 1e-3f, 0.0f);
                        var near = new Vector3(feet.X + (direction.X * back), feet.Y, feet.Z + (direction.Z * back));
                        float atFoot = TryGetSteppedGround(near, stepUp, maxDrop, footRadius, out var ground) ? ground.Height : feet.Y;
                        float lifted = RayTriangleDistance(near with { Y = atFoot - LowWallTolerance }, direction, t.A, t.B, t.C);
                        if (lifted < 0.0f || lifted > RiserTopProbe) continue;

                        // Passed when its top is a floor (a curb, a stair tread); blocked when it is a 60-degree face.
                        float past = hit + RiserTopProbe;
                        var top = new Vector3(feet.X + (direction.X * past), atFoot, feet.Z + (direction.Z * past));
                        if (!TryGetGround(top, stepUp, 0.0f, out var onTop)) continue;
                        if (onTop.Height > atFoot - LowWallTolerance || MathF.Abs(onTop.Normal.Y) >= MaxWallNormalY) continue;
                        wallNormal = t.Normal;
                        distance = hit;
                    }
                }
            }
            return distance < float.MaxValue;
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
                        if (MathF.Abs(t.Normal.Y) >= MinFloorNormalY) continue; // standable: the ground and climb rules own it
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
        /// The culling table a camera at <paramref name="eye"/> (internal space) draws with (#70): the one named by the
        /// highest floor at or below the eye, at most <paramref name="maxDrop"/> under it; -1 when there is no floor there
        /// or it names none (then every placement draws). Rule referenced from xi-tools (docs/zone/format.md "Visibility").
        /// </summary>
        public int CullingTableUnder(Vector3 eye, float maxDrop = 1000.0f) =>
            TryGetGround(eye, 0.0f, maxDrop, out GroundHit hit) ? _triangles[hit.TriangleIndex].CullingTable : -1;

        /// <summary>
        /// The zone map of the floor under <paramref name="feet"/> (internal space, #481): the map number the highest floor
        /// at or below the feet (a step up allowed) names, or 0 when there is no floor there or it names none.
        /// </summary>
        public int MapFloorUnder(Vector3 feet, float maxDrop = 1000.0f) =>
            TryGetGround(feet, MapFloorStepUp, maxDrop, out GroundHit hit) ? _triangles[hit.TriangleIndex].MapFloor : 0;

        /// <summary>
        /// The zone map of the highest floor at (<paramref name="x"/>, <paramref name="z"/>) that names one, for a point
        /// without a height (a home point of the retail table, #481); 0 when no floor there names a map.
        /// </summary>
        public int MapFloorAt(float x, float z)
        {
            if (!TryGetCell(x, z, out int cell)) return 0;
            int map = 0;
            float best = float.MaxValue;
            for (int k = _cellStart[cell], end = _cellStart[cell + 1]; k < end; k++)
            {
                ref readonly var t = ref _triangles[_cellTriangles[k]];
                if (t.MapFloor == 0 || t.Normal.Y > -MinFloorNormalY || !TryHeightAt(t, x, z, out float height)) continue;
                if (height < best)
                {
                    best = height;
                    map = t.MapFloor;
                }
            }
            return map;
        }

        /// <summary>How far above the feet <see cref="MapFloorUnder"/> still takes a floor (a stair tread mid-step).</summary>
        private const float MapFloorStepUp = 1.0f;

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
