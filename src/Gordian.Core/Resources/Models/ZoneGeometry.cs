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

        /// <summary>
        /// The sub-area (building interior) whose closed exterior this placement is (<see cref="ZonePlacement.FileIdLink"/>):
        /// hidden while that interior is shown (#68); 0 for everything else.
        /// </summary>
        public int SubAreaLink { get; set; }

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
    /// <param name="FileIdLink">Record +0x50: the sub-area (building interior) this placement stands in for, hidden while
    /// that interior is shown; 0 for none (xi-tools docs/zone/subareas.md, #68).</param>
    public readonly record struct ZonePlacement(
        string MeshId,
        Vector3 Position,
        Vector3 Rotation,
        Vector3 Scale,
        float DrawDistance,
        string EnvironmentId = "",
        int[]? PointLightSlots = null,
        string BlockId = "",
        int FileIdLink = 0
    )
    {
        /// <summary>
        /// True for a part of an elevator or other moving platform (BlockID starting <c>@</c>).
        /// </summary>
        public bool IsMovingPlatformPart => BlockId.Length > 0 && BlockId[0] == '@';

        /// <summary>
        /// True for a leaf of a zone door (BlockID starting <c>_</c>, the door's Section 0x36 id).
        /// </summary>
        public bool IsDoorPart => BlockId.Length > 0 && BlockId[0] == '_';

        /// <summary>
        /// True for a placement drawn by the Section 0x05 generator its BlockID names (any FourCC not starting with
        /// <c>_</c> (doors) or <c>@</c> (elevators)); see xi-tools docs/zone/format.md, "Generator-bound objects".
        /// </summary>
        public bool IsGeneratorBound => BlockId.Length > 0 && BlockId[0] != '@' && BlockId[0] != '_';
    }

    /// <summary>
    /// One leaf of a zone door: a placement with the door's BlockID and its submeshes (world space, authored pose).
    /// <see cref="Part"/> is the leaf's index in the door's routines: the placements of a door in ZoneDef order. Most
    /// two-leaf doors use the same mesh twice (1,098 of 1,146 in the retail zones), so the order is the only link
    /// (*inference*, to confirm in-game by which way the leaves swing).
    /// </summary>
    public sealed record DoorLeaf(int Part, ZonePlacement Placement, List<MeshGroup> Submeshes);

    /// <summary>
    /// A zone door's routines: <c>open</c> / <c>clos</c> animate it, <c>into</c> / <c>intc</c> are its poses at load
    /// (open / closed). Any of them may be missing.
    /// </summary>
    public sealed class ZoneDoorRoutines
    {
        public Graphics.DoorRoutine? Open { get; set; }
        public Graphics.DoorRoutine? Close { get; set; }
        public Graphics.DoorRoutine? InitOpen { get; set; }
        public Graphics.DoorRoutine? InitClose { get; set; }

        /// <summary>Stores a routine by its section name; false for names that are not door routines.</summary>
        public bool Set(string name, Graphics.DoorRoutine routine)
        {
            switch (name)
            {
                case "open": Open = routine; return true;
                case "clos": Close = routine; return true;
                case "into": InitOpen = routine; return true;
                case "intc": InitClose = routine; return true;
                default: return false;
            }
        }
    }

    /// <summary>
    /// A ship zone's vessel: a second ZoneDef under the zone's <c>mode/ship</c> directory with its own meshes and collision,
    /// in the vessel's frame (the frame the server places the passengers in: Phanauet Channel spawns them at (0, -2, -1)
    /// on the barge, LandSandBoat scripts/zones/Phanauet_Channel/Zone.lua). The zone's main ZoneDef is the scenery the
    /// vessel travels through, along the <c>mode/c000</c>-<c>c003</c> Routes (Section 0x06): each key's eye is the
    /// vessel's place in scenery coordinates, its look-at the next place. Found in the retail DATs (2026-10-20, #403):
    /// the ship zones 1, 3, 46, 47, 58-60, 220, 221 and 223-228 are the only files of the install with two ZoneDefs.
    /// **Beyond xi-tools / xi-model-viewer**, which read one ZoneDef per zone.
    /// </summary>
    public sealed class ZoneVessel
    {
        /// <summary>The vessel's submeshes in its own frame, display space (-X, -Y, Z) like the zone's.</summary>
        public List<MeshGroup> MeshGroups { get; } = new();

        /// <summary>The vessel ZoneDef's collision block (the deck); null when it has none.</summary>
        public World.Collision.ZoneCollisionMesh? Collision { get; set; }

        /// <summary>The scenery Routes the vessel follows (<c>mode/cNNN</c>), in file order; empty at a dock zone.</summary>
        public List<Events.CameraRoute> Routes { get; } = new();

        /// <summary>A voyage zone: the vessel stays put and the scenery moves past along <see cref="Routes"/>.</summary>
        public bool IsVoyage => Routes.Count > 0;
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
        /// The leaves of the zone's doors (placements whose BlockID starts with <c>_</c>), keyed by door id, in world space
        /// at their authored pose. They are drawn with their door's live pose instead of with the static scenery.
        /// </summary>
        public Dictionary<string, List<DoorLeaf>> DoorLeaves { get; } = new(StringComparer.Ordinal);

        /// <summary>The routines of the zone's doors (<c>&lt;zone code&gt;/door/&lt;door id&gt;/</c>), keyed by door id.</summary>
        public Dictionary<string, ZoneDoorRoutines> DoorRoutines { get; } = new(StringComparer.Ordinal);

        /// <summary>
        /// The directory of each door that carries effect routines (<c>d_at/door/_20t</c>), keyed by door id: the door's
        /// <c>open</c> / <c>clos</c> / <c>into</c> / <c>intc</c> there play through <see cref="MapRoutines"/> on the door
        /// entity when its leaves move (#227).
        /// </summary>
        public Dictionary<string, string> DoorDirectories { get; } = new(StringComparer.Ordinal);

        /// <summary>
        /// The zone's player-collision mesh from the ZoneDef collision block; null when the zone has none. In a voyage zone
        /// (<see cref="Vessel"/> with scenery routes) it is the vessel's collision: the player walks the deck.
        /// </summary>
        public World.Collision.ZoneCollisionMesh? Collision { get; set; }

        /// <summary>
        /// The vessel of a ship zone or of a zone whose DAT carries one (the second ZoneDef, under a <c>ship</c>
        /// directory: Phanauet Channel's barge <c>s_ju/mode/ship/ship</c>, the Manaclipper's <c>s_pa/mode/ship/ship</c>);
        /// null for every other zone.
        /// </summary>
        public ZoneVessel? Vessel { get; set; }

        /// <summary>
        /// Short weather routines (lightning strikes) grouped by directory, played one random routine at a time.
        /// </summary>
        public List<Gordian.Core.Graphics.WeatherRoutineGroup> WeatherRoutineGroups { get; } = new();

        /// <summary>
        /// The zone's routines outside the weather directories with the emitters they start, which the client plays when a
        /// trigger names them (S2C 0x039 map schedulers, <see cref="Gordian.Core.Graphics.ZoneRoutinePlayer"/>). Door
        /// directories' routines are in it by path only (<see cref="DoorDirectories"/>): a map scheduler's name never
        /// starts a door's <c>open</c>.
        /// </summary>
        public Gordian.Core.Graphics.ZoneRoutineLibrary MapRoutines { get; } = new();

        /// <summary>
        /// For an actor's effect DAT (<c>actorEffects</c> parse), its Section 0x07 routines by name, each with the effect
        /// layers it starts; the client plays them on the actor (e.g. the Home Point's <c>bind</c>).
        /// </summary>
        public List<Gordian.Core.Graphics.WeatherRoutineVariant> ActorRoutines { get; } = new();

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
