// src/Gordian.Core/World/Collision/ZoneSubAreas.cs
using System;
using System.Collections.Generic;
using System.Numerics;
using Gordian.Core.Resources.Graphics;

namespace Gordian.Core.World.Collision
{
    /// <summary>
    /// Building interiors (sub-areas, #68). Walking into most town shops and houses does not change zone: the client draws the
    /// building's interior, a separate DAT, hides the closed exterior that stands in for it, and tells the server nothing.
    /// Format referenced from xi-tools (https://github.com/vekien/xi-tools, docs/zone/subareas.md, after xim
    /// <c>SubAreaManager</c>), checked against the retail DATs:
    /// <list type="bullet">
    /// <item>Each interior has one or more <c>'m'</c> volumes in the zone's Section 0x36 tables (<see cref="ZoneInteraction.IsSubArea"/>,
    /// the interior id in +0x2C). In Lower Jeuno they are room-sized (6-10 x 5 x 6-10 yalms) and cover the shop floor, so
    /// the interior is the one whose volume holds the player.</item>
    /// <item>The closed exterior is any ZoneDef placement whose file-id link (+0x50) is the interior id
    /// (<see cref="Resources.Models.ZonePlacement.FileIdLink"/>).</item>
    /// <item>The interior is the zone DAT <see cref="InteriorFileId"/>, already in the parent zone's space, with its own collision.</item>
    /// </list>
    /// A retail capture of a Lower Jeuno shop (<c>Retail Captures/Shop_Interior.mp4</c>, 2026-10-17) shows no fade or pop at
    /// the doorway: the lit interior shows through the open door from outside and the street from inside. So the interior is
    /// drawn (and walked) while the player is near its volume (<see cref="NearMargin"/>), and the exterior hidden only while
    /// the player is inside. **Differs from xi-tools**, whose rule draws the interior only while it is the active one.
    /// </summary>
    public static class ZoneSubAreas
    {
        /// <summary>How far outside an interior's volume (yalms) the interior is drawn and walkable already.</summary>
        public const float NearMargin = 6.0f;

        /// <summary>
        /// The interior DAT of a sub-area: its id + 0x64, or + (0x14768 - 0x271) from 0x271 on (Escha - Ru'Aun only), resolved
        /// through the file table (xi-tools docs/zone/subareas.md, after xim <c>ZoneIdToResourceId.getSubAreaResourcePath</c>).
        /// Lower Jeuno's 454-466 are files 554-566 (ROM/2/86-98).
        /// </summary>
        public static int InteriorFileId(int subAreaId) => subAreaId >= 0x271 ? subAreaId + (0x14768 - 0x271) : subAreaId + 0x64;

        /// <summary>The sub-area volumes of a zone's interaction tables (records with a box and an interior id).</summary>
        public static IReadOnlyList<ZoneInteraction> Select(IEnumerable<ZoneInteraction> interactions)
        {
            List<ZoneInteraction>? volumes = null;
            foreach (var record in interactions)
            {
                if (!record.IsSubArea || record.Size.X <= 0.0f || record.Size.Y <= 0.0f || record.Size.Z <= 0.0f) continue;
                (volumes ??= new List<ZoneInteraction>()).Add(record);
            }
            return volumes ?? (IReadOnlyList<ZoneInteraction>)Array.Empty<ZoneInteraction>();
        }

        /// <summary>The interior whose volume holds <paramref name="position"/> (zone coordinates), or 0 outside every one.</summary>
        public static int FindActive(IReadOnlyList<ZoneInteraction> volumes, Vector3 position)
        {
            for (int i = 0; i < volumes.Count; i++)
            {
                if (volumes[i].Contains(position)) return volumes[i].Param;
            }
            return 0;
        }

        /// <summary>
        /// Adds to <paramref name="near"/> (sorted, distinct) every interior whose volume is within <paramref name="margin"/> of
        /// <paramref name="position"/>; returns whether the set changed from what it held.
        /// </summary>
        public static bool FindNear(IReadOnlyList<ZoneInteraction> volumes, Vector3 position, float margin, List<int> near)
        {
            int before = near.Count;
            Span<int> previous = before <= 16 ? stackalloc int[before] : new int[before];
            for (int i = 0; i < before; i++) previous[i] = near[i];
            near.Clear();
            for (int i = 0; i < volumes.Count; i++)
            {
                int id = volumes[i].Param;
                if (!near.Contains(id) && volumes[i].Contains(position, margin)) near.Add(id);
            }
            near.Sort();
            if (near.Count != before) return true;
            for (int i = 0; i < before; i++)
            {
                if (near[i] != previous[i]) return true;
            }
            return false;
        }
    }
}
