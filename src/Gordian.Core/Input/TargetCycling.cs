// src/Gordian.Core/Input/TargetCycling.cs
using System;
using System.Collections.Generic;
using System.Numerics;
using Gordian.Core.Graphics;
using Gordian.Core.World;

namespace Gordian.Core.Input
{
    /// <summary>
    /// Picks the target for the target-cycling keys (Tab / Shift+Tab, the triggers and d-pad) and for Confirm with
    /// nothing targeted. Candidates are the targetable entities within <see cref="Range"/> yalms that are in front of
    /// the camera and inside its horizontal field of view, the player included.
    /// <list type="bullet">
    /// <item>Nothing targeted: the nearest candidate (by distance from the player) on the pressed side of the screen
    /// centre, or on either side for Confirm; the other side when that side is empty. The player is only picked
    /// when nobody else is on screen.</item>
    /// <item>A target on screen: the next candidate over on screen in the pressed direction (ties by distance),
    /// wrapping from one edge of the screen to the other, so every candidate is reachable.</item>
    /// </list>
    /// </summary>
    public static class TargetCycling
    {
        /// <summary>How far away a target can be picked, in yalms.</summary>
        public const float Range = 50.0f;

        /// <summary>Height above an entity's feet its screen position is taken at (about its chest), in yalms.</summary>
        public const float AimHeight = 1.0f;

        /// <summary>A candidate: its horizontal screen position (-1 left edge, +1 right edge) and distance from the player.</summary>
        public readonly record struct Candidate(uint ServerId, float ScreenX, float DistanceSquared, bool IsSelf);

        /// <summary>Whether an entity can be picked by cycling at all (spawned, visible, named, not a transport).</summary>
        public static bool IsTargetable(WorldEntity entity) =>
            entity.IsSpawned && !entity.IsHidden && !entity.IsInvisible && !string.IsNullOrWhiteSpace(entity.Name)
            && entity.Type is not (EntityType.Elevator or EntityType.Ship);

        /// <summary>
        /// The on-screen candidates around the player at <paramref name="playerPosition"/>, as seen from
        /// <paramref name="camera"/>.
        /// </summary>
        public static List<Candidate> Gather(IEnumerable<WorldEntity> nearby, uint selfServerId, Vector3 playerPosition, ViewportCamera camera)
        {
            var result = new List<Candidate>();
            Vector3 forward = camera.Forward;
            Vector3 right = camera.Right;
            float halfWidth = MathF.Tan(camera.FieldOfView * 0.5f) * camera.AspectRatio;
            foreach (var entity in nearby)
            {
                if (!IsTargetable(entity)) continue;
                float distanceSquared = Vector3.DistanceSquared(playerPosition, entity.Position);
                if (distanceSquared > Range * Range) continue;
                Vector3 toEntity = entity.Position + new Vector3(0, AimHeight, 0) - camera.Position;
                float depth = Vector3.Dot(toEntity, forward);
                if (depth <= camera.NearClip) continue;
                float screenX = Vector3.Dot(toEntity, right) / (depth * halfWidth);
                if (MathF.Abs(screenX) > 1.0f) continue;
                result.Add(new Candidate(entity.ServerId, screenX, distanceSquared, entity.ServerId == selfServerId));
            }
            return result;
        }

        /// <summary>
        /// The candidate to target next: <paramref name="direction"/> is +1 (right), -1 (left) or 0 (Confirm, nothing
        /// targeted, either side). <paramref name="currentServerId"/> is the current target (0 = none; a target not
        /// among the candidates counts as none). Returns the server id, or 0 when there is nothing to pick.
        /// </summary>
        public static uint Pick(IReadOnlyList<Candidate> candidates, uint currentServerId, int direction)
        {
            if (candidates.Count == 0) return 0;
            int current = -1;
            if (currentServerId != 0 && direction != 0)
            {
                for (int i = 0; i < candidates.Count; i++)
                {
                    if (candidates[i].ServerId == currentServerId) current = i;
                }
            }
            return current < 0 ? PickFirst(candidates, direction) : PickNext(candidates, current, direction);
        }

        private static uint PickFirst(IReadOnlyList<Candidate> candidates, int direction)
        {
            uint best = NearestOnSide(candidates, direction);
            if (best == 0 && direction != 0) best = NearestOnSide(candidates, 0);
            if (best != 0) return best;
            foreach (var candidate in candidates)
            {
                if (candidate.IsSelf) return candidate.ServerId;
            }
            return 0;
        }

        private static uint NearestOnSide(IReadOnlyList<Candidate> candidates, int direction)
        {
            Candidate? best = null;
            foreach (var candidate in candidates)
            {
                if (candidate.IsSelf) continue;
                if (direction > 0 && candidate.ScreenX < 0) continue;
                if (direction < 0 && candidate.ScreenX > 0) continue;
                if (best is not { } b || candidate.DistanceSquared < b.DistanceSquared
                    || (candidate.DistanceSquared == b.DistanceSquared && candidate.ServerId < b.ServerId))
                {
                    best = candidate;
                }
            }
            return best?.ServerId ?? 0;
        }

        private static uint PickNext(IReadOnlyList<Candidate> candidates, int current, int direction)
        {
            var ordered = new List<Candidate>(candidates);
            ordered.Sort(CompareScreenOrder);
            int index = ordered.IndexOf(candidates[current]);
            int next = (index + direction + ordered.Count) % ordered.Count;
            return ordered[next].ServerId;
        }

        /// <summary>Left to right on screen; at the same position, nearest first.</summary>
        private static int CompareScreenOrder(Candidate a, Candidate b)
        {
            int byX = a.ScreenX.CompareTo(b.ScreenX);
            if (byX != 0) return byX;
            int byDistance = a.DistanceSquared.CompareTo(b.DistanceSquared);
            return byDistance != 0 ? byDistance : a.ServerId.CompareTo(b.ServerId);
        }
    }
}
