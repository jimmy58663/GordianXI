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
    /// and when the ride leaves and arrives, in Earth seconds into the cycle (the cycle starts at Vana'diel midnight).
    /// </summary>
    public readonly record struct VoyageLeg(string RouteName, bool Reversed, double DepartSeconds, double ArriveSeconds);

    /// <summary>Where a voyage zone's vessel is along its scenery: the Route, its progress (0-1) and direction.</summary>
    public readonly record struct VoyagePosition(CameraRoute Route, float Progress, bool Reversed);

    /// <summary>
    /// The voyage zones' timetables and the scenery transform they give (#403). In a voyage zone (<see cref="ZoneVessel.IsVoyage"/>)
    /// the vessel stays at the origin of the zone (the frame the server places the passengers in) and the scenery moves past
    /// it: the vessel is at the Route's eye, heading along its look-at.
    /// <para>
    /// The timetables are derived from LandSandBoat's transport data, which follows retail captures
    /// (https://github.com/LandSandBoat/server, data/zones/carpenters_landing/zone.yaml and bibiki_bay/zone.yaml): a leg leaves
    /// when its run's <c>departing</c> phase starts (offset plus the arriving, docked and closing seconds) and arrives at its
    /// <c>disembark</c> second, in a 3456 s cycle (one Vana'diel day). The leg ends land on the
    /// channel's own timetable in scripts/globals/barge.lua (04:35, 08:55, 16:00, 19:15, 23:35). S2C 0x00A's ShipStart /
    /// ShipEnd would say this for the client, but LandSandBoat leaves them 0, and a retail client on LandSandBoat still moves
    /// the channel past the barge (<c>Retail Captures/Phanauet_Channel_Birds.mp4</c>, zone-in at 8:26, mid-river, a landing
    /// coming up at the end), so the client keeps the time itself.
    /// </para>
    /// <para>
    /// Which Route each leg plays (**Beyond LandSandBoat / xi-tools**, which do not name them). Retail picks it from S2C
    /// 0x00A: <c>ShipStart</c> / <c>ShipEnd</c> start the client's ship clock (<c>ship_t</c> = time since the start over the
    /// run's length), zone flag 0x20 runs the path backwards and the high byte of the fourth zone flags names the path
    /// (XiPackets world/server/0x000A, <c>FUNC_ShipInit</c>; https://github.com/atom0s/XiPackets). LandSandBoat leaves all of
    /// it 0; its older fork AirSkyBoat (https://github.com/AirSkyBoat/AirSkyBoat, src/map/transport.cpp) still sent it, with
    /// a path byte of 0 / 8 / 16 / 24 per departure hour. Phanauet Channel (#434, 2026-10-24): the Emfea leg (South ->
    /// Central) plays <c>c001</c> forwards, both Newtpool legs (Central -> South) <c>c000</c> backwards, South -> North
    /// <c>c002</c>, North -> Central <c>c003</c>. Three checks agree: AirSkyBoat's path bytes (00h 8, 05h and 20h 0, 10h 16,
    /// 17h 24); the Emfea narration's "awfully dangerous" bridge (LandSandBoat barge.lua <c>routeDialogue</c>, 96 minutes
    /// before 04:35, 54-57 % into the leg) is the broken rope bridge <c>jag_k_turi</c> that <c>c001</c> passes under at 61 %;
    /// and the retail night shot (round 4 <c>image-24</c>, 21:58) shows <c>c000</c>'s open pool, run backwards, at 60-66 %.
    /// The Manaclipper's <c>c002</c> runs Bibiki Bay to Purgonorgo, <c>c003</c> back, <c>c000</c> / <c>c001</c> are loops from
    /// Sunset Docks back to it; the morning tour (Dhalmel Rock) plays <c>c001</c>, the afternoon tour (Maliyakaleya Reef)
    /// <c>c000</c>: the round 3 shots (<c>image-19</c>-<c>22</c>, 14:19-15:38) show <c>c000</c>'s reef, arch and islands, and
    /// <c>c001</c> never comes within 1,500 yalms of them. **Differs from AirSkyBoat**, whose path bytes give the morning
    /// tour 0 and the afternoon tour 8.
    /// </para>
    /// <para>
    /// When: the vessel plays the Route's key times linearly over the leg's length, but <see cref="RouteLeadSeconds"/>
    /// earlier than the leg in LandSandBoat's timetable, so it reaches the far end that long before the passengers
    /// disembark. Fitted to eight retail shots with their clocks (Phanauet South -> North <c>image-14</c>-<c>18</c>: 16.5 %,
    /// 28.5 %, 56.5 %, 67 % at 10:51, 11:22, 13:02, 13:45; the Maliyakaleya tour <c>image-19</c>-<c>21</c>: 47 %, 53 %,
    /// 62-65 % at 14:19, 14:40, 15:08; the Newtpool night shot): each leads the timetable by 36-73 s, 52 s on average, and
    /// every shot then lands within 4 % of where retail showed it. Playing the Route at constant speed instead
    /// fits the barge's day leg but misses the tour by 8-11 %. Zones without a timetable hold the vessel halfway along their
    /// first Route.
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

        /// <summary>
        /// How many Earth seconds the scenery runs ahead of LandSandBoat's timetable: each leg plays its Route over
        /// [depart - lead, arrive - lead] (fitted to retail captures, see the class notes). Known wrong on normally boarded
        /// trips, where retail's vessels are well behind ours; accepted for now, see #457.
        /// </summary>
        public const double RouteLeadSeconds = 52;

        private readonly record struct Timetable(double CycleSeconds, VoyageLeg[] Legs);

        private static readonly Dictionary<int, Timetable> Timetables = new()
        {
            // Phanauet Channel: South -> Central (Emfea), Central -> South (Newtpool, day), South -> North, North -> Central,
            // Central -> South (Newtpool, night).
            [1] = new Timetable(3456, new[]
            {
                new VoyageLeg("c001", false, 129, 661),
                new VoyageLeg("c000", true, 752, 1285),
                new VoyageLeg("c002", false, 1472, 2306),
                new VoyageLeg("c003", false, 2514, 2774),
                new VoyageLeg("c000", true, 2864, 3397),
            }),
            // Manaclipper, twice a day: the tour (Dhalmel Rock in the morning, Maliyakaleya Reef in the afternoon), Bibiki Bay ->
            // Purgonorgo Isle, Purgonorgo Isle -> Bibiki Bay (the evening one arrives 31 s into the next day).
            [3] = new Timetable(3456, new[]
            {
                new VoyageLeg("c001", false, 120, 703),
                new VoyageLeg("c002", false, 798, 1231),
                new VoyageLeg("c003", false, 1338, 1759),
                new VoyageLeg("c000", false, 1848, 2431),
                new VoyageLeg("c002", false, 2526, 2959),
                new VoyageLeg("c003", false, 3066, 3487),
            }),
        };

        /// <summary>The legs of a zone's timetable; empty for a zone without one.</summary>
        public static IReadOnlyList<VoyageLeg> LegsOf(int zoneId) => Timetables.TryGetValue(zoneId, out var table) ? table.Legs : Array.Empty<VoyageLeg>();

        /// <summary>
        /// Where the vessel is at <paramref name="earthSecondsSinceEpoch"/> (the session's server clock): inside a leg, its
        /// progress; between legs, the end of the leg that arrived last. False when the zone is not a voyage zone.
        /// </summary>
        public static bool TryGetPosition(ZoneGeometry zone, double earthSecondsSinceEpoch, out VoyagePosition position)
        {
            position = default;
            var routes = zone.Vessel?.Routes;
            if (routes == null || routes.Count == 0) return false;

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
                    double depart = leg.DepartSeconds - RouteLeadSeconds + shift, arrive = leg.ArriveSeconds - RouteLeadSeconds + shift;
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
        public static Matrix4x4 GetSceneryTransform(ZoneGeometry? zone, double earthSecondsSinceEpoch)
        {
            if (zone == null || !TryGetPosition(zone, earthSecondsSinceEpoch, out var position)) return Matrix4x4.Identity;
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
