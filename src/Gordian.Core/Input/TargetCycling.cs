// src/Gordian.Core/Input/TargetCycling.cs
using System;
using System.Collections.Generic;
using System.Numerics;
using Gordian.Core.Graphics;
using Gordian.Core.World;

namespace Gordian.Core.Input
{
    /// <summary>How a targeting key moves the target (see <see cref="TargetCycling"/>).</summary>
    public enum TargetCycleMode
    {
        /// <summary>The closest candidate, the player excluded (Tab or gamepad Confirm with nothing targeted).</summary>
        Closest,

        /// <summary>Tab: the next candidate to the right on screen, wrapping to the left-most; the player excluded.</summary>
        TabRight,

        /// <summary>Shift+Tab: the next candidate to the left on screen, wrapping to the right-most; the player excluded.</summary>
        TabLeft,

        /// <summary>D-pad right: the next candidate to the right on screen, the player at the start and end.</summary>
        CursorRight,

        /// <summary>D-pad left: the next candidate to the left on screen, the player at the start and end.</summary>
        CursorLeft,
    }

    /// <summary>
    /// Picks the target for the targeting keys, as the retail client does (checked in retail by the maintainer,
    /// 2026-09-29). Candidates are the targetable entities within <see cref="Range"/> yalms inside the camera's
    /// horizontal field of view, ordered left to right on screen (at the same position, nearest first).
    /// <list type="bullet">
    /// <item>Tab / Shift+Tab: with nothing targeted, the closest candidate; otherwise the next one to the right / left,
    /// wrapping round to the other edge. The player is never picked.</item>
    /// <item>D-pad right / left: with nothing targeted, the player; otherwise the next one to the right / left of the
    /// current target (the player included, at their own screen position); past the edge, back to the player.</item>
    /// </list>
    /// A current target that has left the screen (or the range) stays in the order past the edge it left by, so the
    /// next press carries on from that side.
    /// </summary>
    public static class TargetCycling
    {
        /// <summary>How far away a target can be picked, in yalms (the monster draw distance).</summary>
        public const float Range = 50.0f;

        /// <summary>
        /// A candidate: its horizontal screen position (-1 left edge, +1 right edge; beyond that for a current target
        /// that has left the screen) and distance from the player.
        /// </summary>
        public readonly record struct Candidate(uint ServerId, float ScreenX, float DistanceSquared, bool IsSelf);

        /// <summary>Whether an entity can be picked by cycling at all (spawned, visible, named, not a transport).</summary>
        public static bool IsTargetable(WorldEntity entity) =>
            entity.IsSpawned && !entity.IsHidden && !entity.IsInvisible && !string.IsNullOrWhiteSpace(entity.Name)
            && entity.Type is not (EntityType.Elevator or EntityType.Ship);

        /// <summary>
        /// A world position on the ground plane in the space the camera's angles are in: the renderer draws the world
        /// mirrored, at (-x, -y, z) (see <see cref="ViewportCamera"/>), so the camera's yaw is a display-space yaw.
        /// </summary>
        public static Vector2 ToViewPlane(Vector3 world) => new(-world.X, world.Z);

        /// <summary>
        /// The camera's eye, forward and screen-right directions on the ground plane (<see cref="ToViewPlane"/>
        /// space), with the player at <paramref name="playerPosition"/>. The orbital camera sits
        /// <see cref="ViewportCamera.Distance"/> behind the player along its yaw (a wall pulling it in does not change
        /// which side of the screen anything is on); the first-person eye is at the player.
        /// </summary>
        public static (Vector2 Eye, Vector2 Forward, Vector2 Right) ViewBasis(ViewportCamera camera, Vector3 playerPosition)
        {
            float yaw = camera.Yaw * (MathF.PI / 180.0f);
            float pitch = camera.Pitch * (MathF.PI / 180.0f);
            var forward = new Vector2(-MathF.Cos(yaw), -MathF.Sin(yaw));
            // Screen right is Cross(forward, up) for the camera's right-handed look-at: (x, z) -> (-z, x).
            var right = new Vector2(-forward.Y, forward.X);
            Vector2 player = ToViewPlane(playerPosition);
            Vector2 eye = camera.Mode switch
            {
                CameraMode.ThirdPersonOrbital => player - (forward * (MathF.Cos(pitch) * camera.Distance)),
                CameraMode.FreeCam => new Vector2(camera.Position.X, camera.Position.Z),
                _ => player,
            };
            return (eye, forward, right);
        }

        /// <summary>
        /// The candidates around the player at <paramref name="playerPosition"/> that are on screen, seen from
        /// <paramref name="camera"/>, plus the current target (<paramref name="currentServerId"/>) wherever it is.
        /// </summary>
        public static List<Candidate> Gather(IEnumerable<WorldEntity> nearby, uint selfServerId, uint currentServerId,
            Vector3 playerPosition, ViewportCamera camera)
        {
            var result = new List<Candidate>();
            var (eye, forward, right) = ViewBasis(camera, playerPosition);
            float halfWidth = MathF.Tan(camera.FieldOfView * 0.5f) * camera.AspectRatio;
            foreach (var entity in nearby)
            {
                if (!IsTargetable(entity)) continue;
                bool isSelf = entity.ServerId == selfServerId;
                bool isCurrent = entity.ServerId == currentServerId && !isSelf;
                float distanceSquared = Vector3.DistanceSquared(playerPosition, entity.Position);
                if (distanceSquared > Range * Range && !isCurrent) continue;

                Vector2 toEntity = ToViewPlane(entity.Position) - eye;
                float depth = Vector2.Dot(toEntity, forward);
                float lateral = Vector2.Dot(toEntity, right);
                bool onScreen = depth > camera.NearClip && MathF.Abs(lateral) <= depth * halfWidth;
                if (onScreen && distanceSquared <= Range * Range)
                {
                    result.Add(new Candidate(entity.ServerId, lateral / (depth * halfWidth), distanceSquared, isSelf));
                }
                else if (isCurrent)
                {
                    // Past the edge it left by, further out the further round it is.
                    float side = lateral < 0 ? -1.0f : 1.0f;
                    result.Add(new Candidate(entity.ServerId, side * (2.0f + MathF.Abs(MathF.Atan2(lateral, depth))), distanceSquared, false));
                }
            }
            return result;
        }

        /// <summary>
        /// The server id to target for <paramref name="mode"/>, given the current target
        /// (<paramref name="currentServerId"/>, 0 = none), or 0 when there is nothing to pick.
        /// </summary>
        public static uint Pick(IReadOnlyList<Candidate> candidates, uint selfServerId, uint currentServerId, TargetCycleMode mode)
        {
            var others = new List<Candidate>();
            Candidate? self = null;
            foreach (var candidate in candidates)
            {
                if (candidate.ServerId == selfServerId) self = candidate;
                else others.Add(candidate);
            }
            others.Sort(CompareScreenOrder);

            switch (mode)
            {
                case TargetCycleMode.TabRight or TargetCycleMode.TabLeft:
                {
                    int current = IndexOf(others, currentServerId);
                    if (current < 0) return Closest(others);
                    int step = mode == TargetCycleMode.TabRight ? 1 : -1;
                    return others[(current + step + others.Count) % others.Count].ServerId;
                }
                case TargetCycleMode.CursorRight or TargetCycleMode.CursorLeft:
                {
                    if (selfServerId == 0) return 0;
                    // The player sits at their own screen position (the centre when not on screen, e.g. first person).
                    var ordered = new List<Candidate>(others);
                    ordered.Add(self ?? new Candidate(selfServerId, 0.0f, 0.0f, true));
                    ordered.Sort(CompareScreenOrder);
                    int current = currentServerId == 0 ? -1 : IndexOf(ordered, currentServerId);
                    if (current < 0) return selfServerId;
                    int next = current + (mode == TargetCycleMode.CursorRight ? 1 : -1);
                    return next >= 0 && next < ordered.Count ? ordered[next].ServerId : selfServerId;
                }
                default:
                    return Closest(others);
            }
        }

        private static int IndexOf(List<Candidate> ordered, uint serverId)
        {
            if (serverId == 0) return -1;
            return ordered.FindIndex(c => c.ServerId == serverId);
        }

        private static uint Closest(List<Candidate> others)
        {
            Candidate? best = null;
            foreach (var candidate in others)
            {
                if (best is not { } b || candidate.DistanceSquared < b.DistanceSquared
                    || (candidate.DistanceSquared == b.DistanceSquared && candidate.ServerId < b.ServerId))
                {
                    best = candidate;
                }
            }
            return best?.ServerId ?? 0;
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
