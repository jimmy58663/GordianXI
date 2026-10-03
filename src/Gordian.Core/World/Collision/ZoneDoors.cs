// src/Gordian.Core/World/Collision/ZoneDoors.cs
using System;
using System.Collections.Generic;
using System.Numerics;
using Gordian.Core.Resources.Graphics;

namespace Gordian.Core.World.Collision
{
    /// <summary>
    /// A zone door's doorway: the thin box of its Section 0x36 record (<see cref="ZoneInteraction"/>), solid while the
    /// door is closed. The collision soup has no door leaves (a ray through every Metalworks doorway passes), so this box
    /// is what stops the player.
    /// </summary>
    public sealed record DoorBlocker(string Id, Vector3 Center, float RotationY, Vector3 Size)
    {
        /// <summary>The box's width axis (its local X) in the XZ plane.</summary>
        public Vector2 WidthAxis => new(MathF.Cos(RotationY), -MathF.Sin(RotationY));

        /// <summary>The box's depth axis (its local Z, through the doorway) in the XZ plane.</summary>
        public Vector2 DepthAxis => new(MathF.Sin(RotationY), MathF.Cos(RotationY));
    }

    /// <summary>
    /// Zone doors for collision. A door blocks while its entity's status is anything but open (8), switching at once
    /// when the status changes rather than following the leaves: in retail a player walks through a door as soon as it
    /// starts to open, and a closing door blocks before its leaves are shut (maintainer, in-game, 2026-10-03). The
    /// status is the event's (<see cref="WorldEntity.EventStatus"/>, opcodes 0x4C / 0x4D) while one runs, else the
    /// server's (LandSandBoat opens a door with animation 8 and closes it with 9). A door whose entity has not been
    /// sent does not block, so a door the server never spawns cannot wall off a passage.
    /// Door volumes referenced from xi-tools (https://github.com/vekien/xi-tools, docs/zone/doors.md: the record's
    /// runtime flag is the collision toggle, 1 closed, 0 open).
    /// </summary>
    public static class ZoneDoors
    {
        /// <summary>Entity status: the door is open (LandSandBoat <c>Animation::OpenDoor</c>, event opcode 0x4C).</summary>
        public const byte StatusOpen = 8;

        /// <summary>Entity status: the door is closed (LandSandBoat <c>Animation::CloseDoor</c>, event opcode 0x4D).</summary>
        public const byte StatusClosed = 9;

        /// <summary>The door blockers of a zone's Section 0x36 records.</summary>
        public static List<DoorBlocker> CreateBlockers(IEnumerable<ZoneInteraction> interactions)
        {
            var blockers = new List<DoorBlocker>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var record in interactions)
            {
                if (!record.IsDoor || record.Size.X <= 0.0f || record.Size.Y <= 0.0f || !seen.Add(record.Id)) continue;
                blockers.Add(new DoorBlocker(record.Id, record.Center, record.RotationY, record.Size));
            }
            return blockers;
        }

        /// <summary>The status that drives a door entity: the event's while it set one, else the server's.</summary>
        public static byte StatusOf(WorldEntity door) => door.EventStatus != 0 ? door.EventStatus : door.AnimationState;

        /// <summary>The spawned door entities of the world by door id (their 0x00E door FourCC).</summary>
        public static Dictionary<string, WorldEntity> DoorEntities(WorldState world)
        {
            var doors = new Dictionary<string, WorldEntity>(StringComparer.Ordinal);
            foreach (var entity in world.Entities)
            {
                if (entity.IsSpawned && entity.Type == EntityType.Door && entity.TransportId.Length > 0) doors[entity.TransportId] = entity;
            }
            return doors;
        }

        /// <summary>The zone's doors that block now, for one tick.</summary>
        public static DoorBlocker[] EvaluateClosed(ZoneCollisionMesh? collision, WorldState world)
        {
            if (collision == null || collision.Doors.Count == 0) return Array.Empty<DoorBlocker>();
            var entities = DoorEntities(world);
            if (entities.Count == 0) return Array.Empty<DoorBlocker>();

            var closed = new List<DoorBlocker>();
            foreach (var door in collision.Doors)
            {
                if (entities.TryGetValue(door.Id, out var entity) && StatusOf(entity) != StatusOpen) closed.Add(door);
            }
            return closed.ToArray();
        }

        /// <summary>
        /// Moves a body from <paramref name="start"/> toward <paramref name="end"/> (internal space, +Y down) and returns
        /// where it ends up outside every closed doorway, sliding along the door. The body is a vertical capsule of
        /// <paramref name="radius"/> from <paramref name="stepUp"/> above the feet to <paramref name="bodyHeight"/>. A
        /// body that already stands inside a doorway (a door closed on it) is let out freely.
        /// </summary>
        public static Vector3 Resolve(ReadOnlySpan<DoorBlocker> closed, Vector3 start, Vector3 end, float radius, float stepUp, float bodyHeight)
        {
            if (closed.IsEmpty) return end;
            var target = new Vector2(end.X, end.Z);
            var from = new Vector2(start.X, start.Z);
            float bodyTop = start.Y - bodyHeight, bodyBottom = start.Y - stepUp;

            foreach (var door in closed)
            {
                float halfHeight = door.Size.Y * 0.5f;
                if (bodyBottom < door.Center.Y - halfHeight || bodyTop > door.Center.Y + halfHeight) continue;

                var center = new Vector2(door.Center.X, door.Center.Z);
                Vector2 widthAxis = door.WidthAxis, depthAxis = door.DepthAxis;
                float halfWidth = (door.Size.X * 0.5f) + radius;
                float halfDepth = (door.Size.Z * 0.5f) + radius;

                var s = new Vector2(Vector2.Dot(from - center, widthAxis), Vector2.Dot(from - center, depthAxis));
                var t = new Vector2(Vector2.Dot(target - center, widthAxis), Vector2.Dot(target - center, depthAxis));
                bool startInside = MathF.Abs(s.X) < halfWidth && MathF.Abs(s.Y) < halfDepth;
                if (startInside || !SegmentHitsBox(s, t, halfWidth, halfDepth)) continue;

                // Stop at the face the body comes from and keep the motion along it.
                if (MathF.Abs(s.Y) >= halfDepth) t.Y = MathF.CopySign(halfDepth, s.Y);
                else t.X = MathF.CopySign(halfWidth, s.X);
                target = center + (widthAxis * t.X) + (depthAxis * t.Y);
            }

            return new Vector3(target.X, end.Y, target.Y);
        }

        /// <summary>Whether the segment from <paramref name="a"/> to <paramref name="b"/> enters the centred box.</summary>
        private static bool SegmentHitsBox(Vector2 a, Vector2 b, float halfX, float halfY)
        {
            float enter = 0.0f, exit = 1.0f;
            return Clip(a.X, b.X - a.X, halfX, ref enter, ref exit) && Clip(a.Y, b.Y - a.Y, halfY, ref enter, ref exit) && enter < exit;
        }

        private static bool Clip(float origin, float delta, float half, ref float enter, ref float exit)
        {
            if (MathF.Abs(delta) < 1e-7f) return MathF.Abs(origin) < half;
            float t0 = (-half - origin) / delta, t1 = (half - origin) / delta;
            if (t0 > t1) (t0, t1) = (t1, t0);
            enter = MathF.Max(enter, t0);
            exit = MathF.Min(exit, t1);
            return enter < exit;
        }
    }
}
