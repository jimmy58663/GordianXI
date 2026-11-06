// src/Gordian.Core/World/VoyageSchedule.cs
using System;
using System.Collections.Generic;
using System.Numerics;
using Gordian.Core.Resources.Events;
using Gordian.Core.Resources.Models;

namespace Gordian.Core.World
{
    /// <summary>
    /// One leg of a voyage zone's day: the scenery Route it follows (by section name), whether it runs the Route backwards,
    /// and when the Route starts and ends, in Earth seconds into the cycle (the cycle starts at Vana'diel midnight).
    /// </summary>
    public readonly record struct VoyageLeg(string RouteName, bool Reversed, double DepartSeconds, double ArriveSeconds);

    /// <summary>
    /// A vessel's run as a server sends it in S2C 0x00A: the Route number (0-3 for <c>c000</c>-<c>c003</c>), its direction, when
    /// it started (Earth seconds since the Vana'diel epoch) and how long it lasts in seconds.
    /// </summary>
    public readonly record struct VoyageRun(int Path, bool Reversed, double StartSeconds, double LengthSeconds);

    /// <summary>Where a voyage zone's vessel is along its scenery: the Route, its progress (0-1) and direction.</summary>
    public readonly record struct VoyagePosition(CameraRoute Route, float Progress, bool Reversed);

    /// <summary>
    /// Where a voyage zone's vessel is on its scenery, and the transform that gives (#403, #434). In a voyage zone
    /// (<see cref="ZoneVessel.IsVoyage"/>) the vessel stays at the origin of the zone (the frame the server places the
    /// passengers in) and the scenery moves past it: the vessel is at the Route's eye, heading along its look-at.
    /// <para>
    /// A run the server sends (<see cref="VoyageRun"/>, S2C 0x00A) wins: retail sends the Route, direction, start and length on
    /// every ride, and progress is the time since the start over the length (XiPackets world/server/0x000A,
    /// <c>FUNC_ShipInit</c>, https://github.com/atom0s/XiPackets; the Route and direction bits **Beyond XiPackets**, see
    /// <see cref="Gordian.Core.Network.Packets.ZoneLoginInfo"/>). LandSandBoat sends none, so the timetables below stand in.
    /// A zone with neither holds the vessel halfway along its first Route.
    /// </para>
    /// <para>
    /// The timetables are retail's runs, from nine rides' S2C 0x00A on 2026-11-05
    /// (<c>Retail Captures/Ship_Rides_Retail_0x00A_Packets.txt</c>, and the barge's morning Central -> South in
    /// <c>docs/rendering/viewport-and-terrain.md</c>), as seconds into the 3456 s cycle (one Vana'diel day): a leg's start is
    /// <c>ShipStart</c> and its end <c>ShipStart</c> + <c>ShipEnd</c>. The ends land within 2 s of LandSandBoat's disembark
    /// seconds (https://github.com/LandSandBoat/server, data/zones/carpenters_landing/zone.yaml and bibiki_bay/zone.yaml), and
    /// its passengers zone in 7-13 s after the starts. The Manaclipper's two evening crossings had no capture and are inferred:
    /// its morning ones half a day later, as its two tours are.
    /// </para>
    /// </summary>
    public static class VoyageSchedule
    {
        /// <summary>
        /// The direction the vessels' bows point in their own frame (internal space): +Z. The vessels' own generators say so:
        /// the barge's stern paddle wheel (<c>su00</c>) and wake foam (<c>1osa</c>, <c>2osa</c>, <c>bac1</c>) sit at z -17 to
        /// -20 and its bow splashes (<c>upsw</c>) at +19.6 to +20.5; the Selbina ship's propeller shaft (<c>shaf</c>) is at -21
        /// and its bow wave (<c>awa2</c>) at +26; the Manaclipper's <c>yke1</c> at +26 (round 3: a -Z bow was tried and dropped).
        /// </summary>
        public static readonly Vector3 VesselForward = Vector3.UnitZ;

        private readonly record struct Timetable(double CycleSeconds, VoyageLeg[] Legs);

        private static readonly Dictionary<int, Timetable> Timetables = new()
        {
            // Phanauet Channel: South -> Central (Emfea), Central -> South (Newtpool, day), South -> North, North -> Central,
            // Central -> South (Newtpool, night).
            [1] = new Timetable(3456, new[]
            {
                new VoyageLeg("c001", false, 154, 662),
                new VoyageLeg("c000", true, 777, 1285),
                new VoyageLeg("c002", false, 1497, 2305),
                new VoyageLeg("c003", false, 2536, 2774),
                new VoyageLeg("c000", true, 2889, 3397),
            }),
            // Manaclipper, twice a day: the tour (Dhalmel Rock in the morning, Maliyakaleya Reef in the afternoon), Bibiki Bay ->
            // Purgonorgo Isle, Purgonorgo Isle -> Bibiki Bay (the evening one arrives 33 s into the next day).
            [3] = new Timetable(3456, new[]
            {
                new VoyageLeg("c000", false, 156, 705),
                new VoyageLeg("c002", false, 834, 1233),
                new VoyageLeg("c003", false, 1382, 1761),
                new VoyageLeg("c001", false, 1884, 2433),
                new VoyageLeg("c002", false, 2562, 2961),
                new VoyageLeg("c003", false, 3110, 3489),
            }),
        };

        /// <summary>The legs of a zone's timetable; empty for a zone without one.</summary>
        public static IReadOnlyList<VoyageLeg> LegsOf(int zoneId) => Timetables.TryGetValue(zoneId, out var table) ? table.Legs : Array.Empty<VoyageLeg>();

        /// <summary>
        /// Where the vessel is at <paramref name="earthSecondsSinceEpoch"/> (the session's server clock): along the server's
        /// <paramref name="run"/> if it sent one; otherwise inside a timetable leg, its progress, and between legs, the end of
        /// the leg that arrived last. False when the zone is not a voyage zone.
        /// </summary>
        public static bool TryGetPosition(ZoneGeometry zone, double earthSecondsSinceEpoch, out VoyagePosition position, VoyageRun? run = null)
        {
            position = default;
            var routes = zone.Vessel?.Routes;
            if (routes == null || routes.Count == 0) return false;

            if (run is { LengthSeconds: > 0 } sent)
            {
                float along = (float)Math.Clamp((earthSecondsSinceEpoch - sent.StartSeconds) / sent.LengthSeconds, 0, 1);
                position = new VoyagePosition(FindRoute(routes, $"c{sent.Path:D3}") ?? routes[0], along, sent.Reversed);
                return true;
            }

            if (!Timetables.TryGetValue(zone.ZoneId, out var table))
            {
                position = new VoyagePosition(routes[0], 0.5f, false);
                return true;
            }

            double t = earthSecondsSinceEpoch % table.CycleSeconds;
            if (t < 0) t += table.CycleSeconds;
            VoyageLeg? current = null, lastArrived = null;
            double lastArrivedAgo = double.MaxValue;
            float progress = 1f;
            foreach (var leg in table.Legs)
            {
                // A leg may run past the end of the cycle (the Manaclipper's last one).
                for (int wrap = 0; wrap < 2; wrap++)
                {
                    double shift = wrap == 0 ? 0 : -table.CycleSeconds;
                    double depart = leg.DepartSeconds + shift, arrive = leg.ArriveSeconds + shift;
                    if (t >= depart && t < arrive)
                    {
                        current = leg;
                        progress = (float)((t - depart) / (arrive - depart));
                    }
                    double ago = t - arrive;
                    if (ago < 0) ago += table.CycleSeconds;
                    if (ago < lastArrivedAgo) { lastArrivedAgo = ago; lastArrived = leg; }
                }
            }
            var chosen = current ?? lastArrived;
            if (chosen == null) return false;
            var route = FindRoute(routes, chosen.Value.RouteName) ?? routes[0];
            position = new VoyagePosition(route, current != null ? progress : 1f, chosen.Value.Reversed);
            return true;
        }

        /// <summary>
        /// The display-space transform that moves the scenery so the vessel (at the origin, bow along <see cref="VesselForward"/>)
        /// sits at its place on the Route; identity for a zone that is not a voyage zone.
        /// </summary>
        public static Matrix4x4 GetSceneryTransform(ZoneGeometry? zone, double earthSecondsSinceEpoch, VoyageRun? run = null)
        {
            if (zone == null || !TryGetPosition(zone, earthSecondsSinceEpoch, out var position, run)) return Matrix4x4.Identity;
            return SceneryTransform(position);
        }

        /// <summary>The display-space scenery transform of a vessel position (see <see cref="GetSceneryTransform"/>).</summary>
        public static Matrix4x4 SceneryTransform(VoyagePosition position)
        {
            float along = position.Reversed ? 1f - position.Progress : position.Progress;
            var pose = position.Route.Evaluate(along);
            // The heading: towards the look-at, or away from it when the Route runs backwards. Routes are flat (y constant).
            var heading = position.Reversed ? pose.Eye - pose.LookAt : pose.LookAt - pose.Eye;
            heading.Y = 0;
            if (heading.LengthSquared() < 1e-8f)
            {
                // The last key looks at nothing new: carry on along the final stretch.
                var keys = position.Route.Keys;
                heading = keys.Count >= 2 ? keys[^1].Eye - keys[^2].Eye : VesselForward;
                if (position.Reversed) heading = -heading;
                heading.Y = 0;
            }
            float turn = MathF.Atan2(VesselForward.X, VesselForward.Z) - MathF.Atan2(heading.X, heading.Z);
            // Internal space: move the vessel's place to the origin, then turn its heading onto the bow.
            var internalTransform = Matrix4x4.CreateTranslation(-pose.Eye) * Matrix4x4.CreateRotationY(turn);
            return DisplayFlip * internalTransform * DisplayFlip;
        }

        /// <summary>Internal space (+Y down) to display space (-X, -Y, Z); its own inverse.</summary>
        private static readonly Matrix4x4 DisplayFlip = Matrix4x4.CreateScale(-1f, -1f, 1f);

        private static CameraRoute? FindRoute(List<CameraRoute> routes, string name)
        {
            foreach (var route in routes)
            {
                if (string.Equals(route.Name, name, StringComparison.OrdinalIgnoreCase)) return route;
            }
            return null;
        }
    }
}
