// src/Gordian.App/Graphics/NamePlateOcclusion.cs
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using Gordian.Core.World.Collision;

namespace Gordian.App.Graphics
{
    /// <summary>
    /// Decides which name plates are hidden behind the zone: a segment from the camera to the entity's overhead point
    /// cast against the zone collision (triangles the camera sees through ignored, as for the camera's own collision).
    /// Retail does not draw names through walls (in-game comparison, 2026-09-29). Each entity is re-tested at most every
    /// <see cref="RetestSeconds"/>, so a crowded town costs a handful of casts a frame.
    /// </summary>
    public sealed class NamePlateOcclusion
    {
        /// <summary>How long an entity's last answer is kept.</summary>
        public const double RetestSeconds = 0.1;

        /// <summary>Yalms short of the overhead point the segment stops, so geometry touching the head does not count.</summary>
        public const float EndClearance = 0.25f;

        private readonly Dictionary<uint, (bool Occluded, long Stamp)> _cache = new();
        private ZoneCollisionMesh? _collision;

        /// <summary>
        /// Whether zone geometry lies between the camera and the point (both display space). Without collision nothing
        /// is occluded.
        /// </summary>
        public bool IsOccluded(ZoneCollisionMesh? collision, uint serverId, Vector3 cameraPosition, Vector3 point)
        {
            if (!ReferenceEquals(collision, _collision))
            {
                _cache.Clear();
                _collision = collision;
            }
            if (collision == null) return false;

            long now = Stopwatch.GetTimestamp();
            if (_cache.TryGetValue(serverId, out var cached) && now - cached.Stamp < RetestSeconds * Stopwatch.Frequency)
            {
                return cached.Occluded;
            }

            bool occluded = false;
            var offset = point - cameraPosition;
            float length = offset.Length();
            if (length > EndClearance)
            {
                var end = cameraPosition + offset * ((length - EndClearance) / length);
                // Collision is in internal space; display space is (-x, -y, z).
                static Vector3 ToInternal(Vector3 v) => new(-v.X, -v.Y, v.Z);
                occluded = collision.TryRaycast(ToInternal(cameraPosition), ToInternal(end), skipCameraTransparent: true, out _);
            }
            _cache[serverId] = (occluded, now);
            return occluded;
        }
    }
}
