// src/Gordian.Core/World/Collision/EntityBumpCollision.cs
using System;
using System.Collections.Generic;
using System.Numerics;
using Gordian.Core.Resources.Tables;

namespace Gordian.Core.World.Collision
{
    /// <summary>
    /// The legacy client's soft collision between the local player and other characters: walking into a player, NPC
    /// or monster stops the player, but pressing on for a moment passes through it, and once passed through, that
    /// character never blocks again until it leaves (despawns or goes out of range), so a crowd (auction house, zone line)
    /// never traps the player twice. Something the player already overlaps never blocks, so it can always walk out. Hidden,
    /// invisible and server-flagged non-blocking entities are skipped, as the client skips them in its actor contact check.
    /// <para>
    /// Retail (Collision_Bump.mp4, Castoff Point, #16): the stop distance grows with the character's size (a Hume-size NPC
    /// about 0.6-0.8 yalm centre to centre, a Galka-size NPC about 1-1.5, a Galka PC about 1) and the hold before passing
    /// is about 0.7 s for a Hume-size NPC and longer for a large one (up to about 2 s, not separable from the player's
    /// input). The pass-through lasting per character until it leaves render distance is the maintainer's retail
    /// observation. The radii and holds are frame estimates.
    /// </para>
    /// </summary>
    public sealed class EntityBumpCollision
    {
        /// <summary>Body radius (yalms) of a Hume, Elvaan or Mithra, and of a character whose race is not known.</summary>
        public const float PlayerRadius = 0.35f;

        /// <summary>Body radius of a Galka: about 1 yalm between centres from a Galka PC to the Elvaan player (#16).</summary>
        public const float GalkaRadius = 0.65f;

        /// <summary>Body radius of a Tarutaru (guessed from its build; not in the capture).</summary>
        public const float TarutaruRadius = 0.25f;

        /// <summary>
        /// The client's position update interval: C2S 0x015 goes out every 250 ms (<c>SessionNetworkManager</c>'s network tick).
        /// </summary>
        public const float PositionUpdateSeconds = 0.25f;

        /// <summary>
        /// How long the player must keep pushing into a Hume-size character before passing through it: two position updates
        /// (provisional). Retail's hold of about 0.7-0.8 s against a Hume-size NPC (#16) reads as two of its ~400 ms updates;
        /// at GordianXI's 250 ms that is 0.5 s, which also felt right in the in-game test (round 1, 2026-10-21: 0.7 s felt
        /// slightly long).
        /// </summary>
        public const float HoldSeconds = 2 * PositionUpdateSeconds;

        /// <summary>The hold grows with the contact distance, up to this for the largest characters (retail up to about 2 s).</summary>
        public const float MaxHoldSeconds = 2.0f;

        /// <summary>Characters further than this above or below the player are on another floor and never block.</summary>
        public const float MaxHeightDifference = 3.0f;

        /// <summary>A push that pauses for longer than this starts over.</summary>
        private const float PressGapSeconds = 0.25f;

        /// <summary>Contact distance of two Hume-size bodies, for which the hold is <see cref="HoldSeconds"/>.</summary>
        private const float HumeContact = PlayerRadius * 2.0f;

        /// <summary>
        /// How far (yalms, across the ground) a character passed through may be from the player before it blocks again: the
        /// server's entity render distance (LandSandBoat <c>zone_entities.cpp</c> ENTITY_RENDER_DISTANCE).
        /// </summary>
        public const float RenderDistance = 50.0f;

        /// <summary>The characters passed through, by server id, with where each was last seen.</summary>
        private readonly Dictionary<uint, Vector3> _passedThrough = new();
        private ushort _zoneId;
        private uint _pressingId;
        private float _pressSeconds;
        private float _sincePress = float.MaxValue;

        /// <summary>The characters passed through that are still in range; they no longer block.</summary>
        public int PassedThroughCount => _passedThrough.Count;

        /// <summary>
        /// Body radius of a character in yalms: by race for a character drawn from a race model (players, and NPCs that look
        /// like them), scaled by a player's small / medium / large build; by the size class for a fixed model (monsters,
        /// most NPCs).
        /// </summary>
        public static float RadiusOf(WorldEntity entity)
        {
            ArgumentNullException.ThrowIfNull(entity);
            if (entity.Appearance.ModelId > 0) return RadiusOf(entity.GraphSize);
            float radius = (CharacterRace)((entity.Appearance.FaceModel >> 8) & 0xFF) switch
            {
                CharacterRace.Galka => GalkaRadius,
                CharacterRace.TaruMale or CharacterRace.TaruFemale => TarutaruRadius,
                _ => PlayerRadius,
            };
            return entity.Type == EntityType.Player ? radius * PlayerSizeScale.For(entity.GraphSize) : radius;
        }

        /// <summary>Body radius of a fixed-model character by its size class (0 small, 1 medium, 2 large).</summary>
        public static float RadiusOf(byte graphSize) => graphSize switch
        {
            0 => 0.35f,
            1 => 0.7f,
            _ => 1.4f,
        };

        /// <summary>How long the player pushes into a character at this contact distance before passing through.</summary>
        public static float HoldFor(float contact) => Math.Clamp(HoldSeconds * contact / HumeContact, HoldSeconds, MaxHoldSeconds);

        /// <summary>
        /// Whether the local player may move from <paramref name="from"/> to <paramref name="to"/> this tick, advancing
        /// the press timer by <paramref name="deltaSeconds"/>. False means the player stops where it is.
        /// </summary>
        public bool TryMove(WorldState world, WorldEntity self, Vector3 from, Vector3 to, float deltaSeconds)
        {
            ArgumentNullException.ThrowIfNull(world);
            ArgumentNullException.ThrowIfNull(self);
            _sincePress += deltaSeconds;
            ForgetDeparted(world, self);

            WorldEntity? blocker = FindBlocker(world, self, from, to, out float contact);
            if (blocker == null) return true;

            if (blocker.ServerId != _pressingId || _sincePress > PressGapSeconds)
            {
                _pressingId = blocker.ServerId;
                _pressSeconds = 0.0f;
            }
            _pressSeconds += deltaSeconds;
            _sincePress = 0.0f;

            if (_pressSeconds < HoldFor(contact)) return false;

            _pressSeconds = 0.0f;
            _pressingId = 0;
            _passedThrough[blocker.ServerId] = blocker.Position;
            return true;
        }

        /// <summary>
        /// Forgets the characters passed through that have left render distance (<see cref="RenderDistance"/>, measured from
        /// where each was last seen) or whose zone the player left. They are kept by server id, not by entity, because
        /// LandSandBoat despawns and respawns standing NPCs every few seconds even right beside the player (a Port Jeuno log,
        /// 2026-10-21): a new entity for the same NPC is the same character, which kept blocking again (round 1).
        /// </summary>
        private void ForgetDeparted(WorldState world, WorldEntity self)
        {
            if (world.CurrentZoneId != _zoneId)
            {
                _zoneId = world.CurrentZoneId;
                _passedThrough.Clear();
                return;
            }
            if (_passedThrough.Count == 0) return;

            List<uint>? gone = null;
            foreach (var (id, lastSeen) in _passedThrough)
            {
                Vector3 seen = world.TryGetByServerId(id, out var entity) && entity.IsSpawned ? entity.Position : lastSeen;
                if (DistanceXZ(self.Position, seen) > RenderDistance) (gone ??= new List<uint>()).Add(id);
                else if (seen != lastSeen) _passedThrough[id] = seen;
            }
            if (gone != null) foreach (uint id in gone) _passedThrough.Remove(id);
        }

        /// <summary>
        /// The nearest character the move would walk into from outside it, or null, with its contact distance.
        /// </summary>
        private WorldEntity? FindBlocker(WorldState world, WorldEntity self, Vector3 from, Vector3 to, out float nearestContact)
        {
            WorldEntity? nearest = null;
            float nearestDistance = float.MaxValue;
            nearestContact = 0.0f;
            float ownRadius = RadiusOf(self);
            foreach (var other in world.GetEntitiesInRadius(to, 4.0f))
            {
                if (!Blocks(other, self) || _passedThrough.ContainsKey(other.ServerId)) continue;
                var position = other.Position;
                if (MathF.Abs(position.Y - from.Y) > MaxHeightDifference) continue;

                float contact = ownRadius + RadiusOf(other);
                float before = DistanceXZ(from, position);
                float after = DistanceXZ(to, position);
                if (before < contact || after >= contact) continue; // already inside, or not reaching it
                if (after < nearestDistance)
                {
                    nearestDistance = after;
                    nearest = other;
                    nearestContact = contact;
                }
            }
            return nearest;
        }

        private static bool Blocks(WorldEntity other, WorldEntity self)
        {
            if (ReferenceEquals(other, self) || other.ServerId == self.ServerId) return false;
            if (!other.IsSpawned || other.IsHidden || other.IsInvisible || other.IsSleeping || other.IsAutoTargetOnly || other.IsNonBlocking) return false;
            if (other.Type is not (EntityType.Player or EntityType.Npc or EntityType.Monster or EntityType.Pet or EntityType.Trust)) return false;
            return !(other.Type == EntityType.Monster && other.Hpp == 0); // defeated monsters
        }

        private static float DistanceXZ(Vector3 a, Vector3 b)
        {
            float dx = a.X - b.X, dz = a.Z - b.Z;
            return MathF.Sqrt((dx * dx) + (dz * dz));
        }
    }
}
