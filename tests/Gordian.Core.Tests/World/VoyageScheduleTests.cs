using System.Numerics;
using Gordian.Core.Network.Packets;
using Gordian.Core.Resources;
using Gordian.Core.Resources.Events;
using Gordian.Core.Resources.Models;
using Gordian.Core.World;

namespace Gordian.Core.Tests.World
{
    /// <summary>Ship zones' vessels, their voyages and the ships that call at dock zones (#403).</summary>
    public class VoyageScheduleTests
    {
        private const string GameDirectory = @"G:\Program Files (x86)\PlayOnline\SquareEnix\FINAL FANTASY XI";
        private readonly ITestOutputHelper _output;

        public VoyageScheduleTests(ITestOutputHelper output) => _output = output;

        private static CameraRoute StraightRoute(string name, Vector3 from, Vector3 to) => new()
        {
            Name = name,
            Keys = new[]
            {
                new CameraKey(from, 350, Vector3.Lerp(from, to, 0.5f), 0, 0f),
                new CameraKey(Vector3.Lerp(from, to, 0.5f), 350, to, 0, 0.5f),
                new CameraKey(to, 350, to + (to - from), 0, 1f),
            }
        };

        private static Vector3 Internal(Vector3 display) => new(-display.X, -display.Y, display.Z);

        [Fact]
        public void SceneryTransform_PutsTheVesselsPlaceAtTheOrigin_BowAlongItsHeading()
        {
            // A route heading east (+X) from (100, 0, 50): halfway it is at (150, 0, 50).
            var route = StraightRoute("c000", new Vector3(100, 0, 50), new Vector3(200, 0, 50));
            var transform = VoyageSchedule.SceneryTransform(new VoyagePosition(route, 0.5f, false));

            // Scenery vertices are in display space; the vessel's place lands on the origin.
            var place = Vector3.Transform(Internal(new Vector3(150, 0, 50)), transform);
            Assert.True(place.Length() < 1e-3f, place.ToString());

            // A point 10 yalms further along the route lies 10 yalms off the bow (+Z, the same in both spaces).
            var ahead = Vector3.Transform(Internal(new Vector3(160, 0, 50)), transform);
            Assert.Equal(0f, ahead.X, 3);
            Assert.Equal(10f, ahead.Z, 3);

            // Run backwards, the same place has the route's start ahead of the bow.
            var back = VoyageSchedule.SceneryTransform(new VoyagePosition(route, 0.5f, true));
            var start = Vector3.Transform(Internal(new Vector3(140, 0, 50)), back);
            Assert.Equal(10f, start.Z, 3);
        }

        [Fact]
        public void PhanauetChannel_LegsFollowTheTimetable()
        {
            var zone = new ZoneGeometry { ZoneId = 1, Vessel = new ZoneVessel() };
            foreach (string name in new[] { "c000", "c001", "c002", "c003" })
            {
                zone.Vessel.Routes.Add(StraightRoute(name, Vector3.Zero, new Vector3(100, 0, 0)));
            }

            // South -> North leaves 1472 s into the day and lands at 2306 s (LandSandBoat barge_south_north).
            double day = 3456 * 1000.0;
            Assert.True(VoyageSchedule.TryGetPosition(zone, day + 1472 + 417, out var mid));
            Assert.Equal("c002", mid.Route.Name);
            Assert.Equal(0.5f, mid.Progress, 3);
            Assert.False(mid.Reversed);

            // The two central-to-south legs run c001 backwards (round 1: c000 took another branch of the channel than retail).
            Assert.True(VoyageSchedule.TryGetPosition(zone, day + 800, out var south));
            Assert.Equal("c001", south.Route.Name);
            Assert.True(south.Reversed);

            // Between legs the vessel waits at the end of the leg that landed last (North -> Central lands at 2774 s).
            Assert.True(VoyageSchedule.TryGetPosition(zone, day + 2800, out var docked));
            Assert.Equal("c003", docked.Route.Name);
            Assert.Equal(1f, docked.Progress);

            // The Manaclipper's last leg runs past the end of its 1728 s cycle.
            var clipper = new ZoneGeometry { ZoneId = 3, Vessel = new ZoneVessel() };
            foreach (string name in new[] { "c000", "c001", "c002", "c003" })
            {
                clipper.Vessel.Routes.Add(StraightRoute(name, Vector3.Zero, new Vector3(100, 0, 0)));
            }
            Assert.True(VoyageSchedule.TryGetPosition(clipper, 1728 * 7.0 + 10, out var wrapped));
            Assert.Equal("c003", wrapped.Route.Name);
            Assert.InRange(wrapped.Progress, 0.9f, 1f);
        }

        [Fact]
        public void ZonesWithoutAVoyage_KeepTheirScenery()
        {
            Assert.True(VoyageSchedule.GetSceneryTransform(new ZoneGeometry { ZoneId = 230 }, 1000).IsIdentity);
            var dock = new ZoneGeometry { ZoneId = 104, Vessel = new ZoneVessel() };
            Assert.False(dock.Vessel.IsVoyage);
            Assert.True(VoyageSchedule.GetSceneryTransform(dock, 1000).IsIdentity);
        }

        [Fact]
        public void S2C_0x00E_CharNpc_ShipCarriesItsShipNumber()
        {
            byte[] payload = new byte[0x40];
            payload[0x2C] = (byte)EntitySubKind.Ship;
            payload[0x30] = 16;
            var npc = new S2C_0x00E_CharNpc(payload);
            Assert.True(npc.TryGetShipNumber(out uint ship));
            Assert.Equal(16u, ship);
            Assert.True(ShipModels.TryGetVesselZone(ship, out int zone));
            Assert.Equal(1, zone);
            Assert.True(ShipModels.TryGetVesselZone(17, out zone));
            Assert.Equal(3, zone);

            payload[0x2C] = (byte)EntitySubKind.Door;
            Assert.False(new S2C_0x00E_CharNpc(payload).TryGetShipNumber(out _));
        }

        [Theory]
        [InlineData(1, 4)]   // Phanauet Channel: the barge
        [InlineData(3, 4)]   // Manaclipper
        [InlineData(220, 1)] // Ship bound for Selbina
        public void ShipZones_LoadTheirVessel_AndWalkItsDeck(int zoneId, int routes)
        {
            if (!Directory.Exists(GameDirectory)) return;
            var rm = new ResourceManager(GameDirectory);
            rm.InitializeFileTable();
            Assert.True(rm.TryLoadZone(zoneId, out var zone, out _));
            var vessel = zone!.Vessel;
            Assert.NotNull(vessel);
            Assert.True(vessel!.MeshGroups.Count > 0);
            Assert.Equal(routes, vessel.Routes.Count);
            Assert.True(vessel.IsVoyage);
            Assert.NotNull(vessel.Collision);
            Assert.Same(vessel.Collision, zone.Collision);

            // The deck is under the passengers' spawn (Phanauet Channel: (0, -2, -1), LandSandBoat Zone.lua).
            if (zoneId == 1)
            {
                Assert.True(vessel.Collision!.TryGetGround(new Vector3(0.15f, -2f, -1f), 2f, 2f, out var deck), "no deck under the spawn");
                _output.WriteLine($"barge deck at {deck.Height:F3}");
                Assert.InRange(deck.Height, -2.5f, -1.5f);
            }

            // The headless loader takes the same ZoneDef.
            var headless = new ResourceManager(GameDirectory);
            headless.InitializeFileTable();
            Assert.Equal(vessel.Collision!.TriangleCount, headless.TryLoadZoneCollision(zoneId)!.TriangleCount);
        }

        [Fact]
        public void DockZones_HaveNoVesselOfTheirOwn()
        {
            if (!Directory.Exists(GameDirectory)) return;
            var rm = new ResourceManager(GameDirectory);
            rm.InitializeFileTable();
            foreach (int zoneId in new[] { 2, 4 })
            {
                Assert.True(rm.TryLoadZone(zoneId, out var zone, out _));
                Assert.Null(zone!.Vessel);
            }
        }
    }
}
