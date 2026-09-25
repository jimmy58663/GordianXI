// src/Gordian.Core/World/Collision/EntityBumpCollision.cs
using System;
using System.Numerics;

namespace Gordian.Core.World.Collision
{
    /// <summary>
    /// The legacy client's soft collision between the local player and other characters: walking into a player, NPC
    /// or monster stops the player, but pressing on for a moment passes through it, and after passing through the
    /// player moves freely for a short grace period so crowds (auction houses, zone lines) never trap it. Something the
    /// player already overlaps never blocks, so it can always walk out. Hidden, invisible and server-flagged
    /// non-blocking entities are skipped, as the client skips them in its actor contact check.
    /// </summary>
    public sealed class EntityBumpCollision
    {
        /// <summary>Local player's body radius for entity contact (yalms).</summary>
        public const float PlayerRadius = 0.35f;

        /// <summary>How long the player must keep pushing into a character before passing through it.</summary>
        public const float HoldSeconds = 0.35f;

        /// <summary>Free movement after passing through, so the next characters in a crowd do not block again.</summary>
        public const float GraceSeconds = 1.5f;

        /// <summary>Characters further than this above or below the player are on another floor and never block.</summary>
        public const float MaxHeightDifference = 3.0f;

        /// <summary>A push that pauses for longer than this starts over.</summary>
        private const float PressGapSeconds = 0.25f;

        private uint _pressingId;
        private float _pressSeconds;
        private float _sincePress = float.MaxValue;
        private float _graceSeconds;

        /// <summary>Remaining free-movement time after passing through a character.</summary>
        public float GraceRemaining => MathF.Max(_graceSeconds, 0.0f);

        /// <summary>
        /// Body radius of a character by its size class (0 small, 1 medium, 2 large).
        /// </summary>
        public static float RadiusOf(byte graphSize) => graphSize switch
        {
            0 => 0.35f,
            1 => 0.7f,
            _ => 1.4f,
        };

        /// <summary>
        /// Whether the local player may move from <paramref name="from"/> to <paramref name="to"/> this tick, advancing
        /// the press and grace timers by <paramref name="deltaSeconds"/>. False means the player stops where it is.
        /// </summary>
        public bool TryMove(WorldState world, WorldEntity self, Vector3 from, Vector3 to, float deltaSeconds)
        {
            ArgumentNullException.ThrowIfNull(world);
            _graceSeconds -= deltaSeconds;
            _sincePress += deltaSeconds;
            if (_graceSeconds > 0.0f) return true;

            WorldEntity? blocker = FindBlocker(world, self, from, to);
            if (blocker == null) return true;

            if (blocker.ServerId != _pressingId || _sincePress > PressGapSeconds)
            {
                _pressingId = blocker.ServerId;
                _pressSeconds = 0.0f;
            }
            _pressSeconds += deltaSeconds;
            _sincePress = 0.0f;

            if (_pressSeconds < HoldSeconds) return false;

            _pressSeconds = 0.0f;
            _pressingId = 0;
            _graceSeconds = GraceSeconds;
            return true;
        }

        /// <summary>
        /// The nearest character the move would walk into from outside it, or null.
        /// </summary>
        private static WorldEntity? FindBlocker(WorldState world, WorldEntity self, Vector3 from, Vector3 to)
        {
            WorldEntity? nearest = null;
            float nearestDistance = float.MaxValue;
            foreach (var other in world.GetEntitiesInRadius(to, 4.0f))
            {
                if (!Blocks(other, self)) continue;
                var position = other.Position;
                if (MathF.Abs(position.Y - from.Y) > MaxHeightDifference) continue;

                float contact = PlayerRadius + RadiusOf(other.GraphSize);
                float before = DistanceXZ(from, position);
                float after = DistanceXZ(to, position);
                if (before < contact || after >= contact) continue; // already inside, or not reaching it
                if (after < nearestDistance)
                {
                    nearestDistance = after;
                    nearest = other;
                }
            }
            return nearest;
        }

        private static bool Blocks(WorldEntity other, WorldEntity self)
        {
            if (ReferenceEquals(other, self) || other.ServerId == self.ServerId) return false;
            if (!other.IsSpawned || other.IsHidden || other.IsInvisible || other.IsNonBlocking) return false;
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
