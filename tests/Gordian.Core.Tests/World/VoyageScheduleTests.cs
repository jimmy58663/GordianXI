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

            // South -> North leaves 1472 s into the day and lands at 2306 s (LandSandBoat barge_south_north); the scenery
            // runs RouteLeadSeconds ahead of that.
            double day = 3456 * 1000.0;
            double lead = VoyageSchedule.RouteLeadSeconds;
            Assert.True(VoyageSchedule.TryGetPosition(zone, day + 1472 + 417 - lead, out var mid));
            Assert.Equal("c002", mid.Route.Name);
            Assert.Equal(0.5f, mid.Progress, 3);
            Assert.False(mid.Reversed);
            // Retail at 10:51 (image-14) was 16.5 % along it.
            Assert.True(VoyageSchedule.TryGetPosition(zone, day + 10.85 * 144, out var shot));
            Assert.InRange(shot.Progress, 0.145f, 0.185f);

            // The Emfea leg runs c001 forwards (it passes under the broken rope bridge), both Newtpool legs c000 backwards.
            Assert.True(VoyageSchedule.TryGetPosition(zone, day + 400, out var emfea));
            Assert.Equal("c001", emfea.Route.Name);
            Assert.False(emfea.Reversed);
            Assert.True(VoyageSchedule.TryGetPosition(zone, day + 800, out var south));
            Assert.Equal("c000", south.Route.Name);
            Assert.True(south.Reversed);
            // Retail at 21:58 (image-24) was in the Newtpool, 60-66 % along the night leg.
            Assert.True(VoyageSchedule.TryGetPosition(zone, day + (21 + 58 / 60.0) * 144, out var night));
            Assert.Equal("c000", night.Route.Name);
            Assert.True(night.Reversed);
            Assert.InRange(night.Progress, 0.6f, 0.67f);

            // Between legs the vessel waits at the end of the leg that landed last (North -> Central lands at 2774 s).
            Assert.True(VoyageSchedule.TryGetPosition(zone, day + 2800, out var docked));
            Assert.Equal("c003", docked.Route.Name);
            Assert.Equal(1f, docked.Progress);

            // The Manaclipper's last leg runs past the end of its day.
            var clipper = new ZoneGeometry { ZoneId = 3, Vessel = new ZoneVessel() };
            foreach (string name in new[] { "c000", "c001", "c002", "c003" })
            {
                clipper.Vessel.Routes.Add(StraightRoute(name, Vector3.Zero, new Vector3(100, 0, 0)));
            }
            Assert.True(VoyageSchedule.TryGetPosition(clipper, 3456 * 7.0 + 10, out var wrapped));
            Assert.Equal("c003", wrapped.Route.Name);
            Assert.InRange(wrapped.Progress, 0.9f, 1f);

            // Its morning tour (Dhalmel Rock) and afternoon tour (Maliyakaleya Reef) take different loops (round 3).
            Assert.True(VoyageSchedule.TryGetPosition(clipper, 3456 * 7.0 + 400, out var morning));
            Assert.Equal("c001", morning.Route.Name);
            // Vana'diel 14:19 and 15:08 (Maliyakaleya Reef shots image-19 and image-21) are 2061.6 s and 2179.2 s into the
            // day; retail showed 47 % and 62-65 % of c000.
            Assert.True(VoyageSchedule.TryGetPosition(clipper, 3456 * 7.0 + 2061.6, out var afternoon));
            Assert.Equal("c000", afternoon.Route.Name);
            Assert.False(afternoon.Reversed);
            Assert.InRange(afternoon.Progress, 0.44f, 0.5f);
            Assert.True(VoyageSchedule.TryGetPosition(clipper, 3456 * 7.0 + 2179.2, out var arch));
            Assert.InRange(arch.Progress, 0.61f, 0.67f);
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
        public void VoyageZones_KeepTheirVesselsEffectsAndDoorsOnBoard()
        {
            if (!Directory.Exists(GameDirectory)) return;
            var rm = new ResourceManager(GameDirectory);
            rm.InitializeFileTable();
            // The Manaclipper's wake (s_pa/effe/nami) is authored at the vessel's origin; the rocks' surf is not.
            Assert.True(rm.TryLoadZone(3, out var clipper, out _));
            Assert.Contains(clipper!.EffectLayers, l => l.Name == "ykf1" && l.OnVessel);
            Assert.Contains(clipper.EffectLayers, l => l.Name == "skb1" && !l.OnVessel);
            // Phanauet Channel's river surface is the barge's own layer (cors, texture rinkawa, about 310 x 360 yalms around
            // it): without it the channel draws as a dark bed (#424).
            Assert.True(rm.TryLoadZone(1, out var channel, out _));
            var river = Assert.Single(channel!.EffectLayers, l => l.DatId == "cors");
            Assert.True(river.OnVessel);
            Assert.Contains("rinkawa", river.TextureName);
            Assert.True(river.Scale.X * 23f > 250f && river.Scale.Z * 23f > 250f, river.Scale.ToString());
            // The Selbina ship's cabin doors swing with their door entities.
            Assert.True(rm.TryLoadZone(220, out var ship, out _));
            foreach (string id in new[] { "_640", "_641", "_642", "_643" })
            {
                Assert.True(ship!.Vessel!.DoorLeaves.ContainsKey(id), id);
                Assert.True(ship.DoorLeaves.ContainsKey(id), id);
            }
            // Its surface overlays carry a small positive z offset that keeps them under the sea (#430); the wakes pull forward.
            Assert.Equal(5f, Assert.Single(ship!.EffectLayers, l => l.DatId == "lowc").DepthOffset);
            Assert.Equal(10f, Assert.Single(ship.EffectLayers, l => l.DatId == "1low").DepthOffset);
            Assert.All(ship.EffectLayers.Where(l => l.GeneratorId is "awa1" or "awa2" or "awa3" or "vstr"), l => Assert.True(l.DepthOffset < 0, l.GeneratorId));
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
