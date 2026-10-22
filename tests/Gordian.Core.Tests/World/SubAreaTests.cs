using System.Buffers.Binary;
using System.Numerics;
using System.Text;
using Gordian.Core.Resources;
using Gordian.Core.Resources.Graphics;
using Gordian.Core.World;
using Gordian.Core.World.Collision;

namespace Gordian.Core.Tests.World
{
    /// <summary>Building interiors (sub-areas, #68).</summary>
    public class SubAreaTests
    {
        private const string GameDirectory = @"G:\Program Files (x86)\PlayOnline\SquareEnix\FINAL FANTASY XI";
        private readonly ITestOutputHelper _output;

        public SubAreaTests(ITestOutputHelper output) => _output = output;

        private static ZoneCollisionMesh Floor(float minX, float minZ, float maxX, float maxZ, float y = 0f) => new(new[]
        {
            new CollisionTriangle(new Vector3(minX, y, minZ), new Vector3(maxX, y, minZ), new Vector3(minX, y, maxZ), -Vector3.UnitY, false, default, false),
            new CollisionTriangle(new Vector3(maxX, y, minZ), new Vector3(maxX, y, maxZ), new Vector3(minX, y, maxZ), -Vector3.UnitY, false, default, false),
        });

        [Fact]
        public void InteriorFileId_FollowsTheTwoRanges()
        {
            Assert.Equal(554, ZoneSubAreas.InteriorFileId(454));     // Lower Jeuno m6t1, ROM/2/86
            Assert.Equal(566, ZoneSubAreas.InteriorFileId(466));
            Assert.Equal(0x14768, ZoneSubAreas.InteriorFileId(0x271)); // Escha - Ru'Aun
        }

        [Fact]
        public void RidRecord_CarriesTheInteriorId()
        {
            // RID header, the table at +0x14: count 1, pad, one 0x40-byte entry.
            byte[] payload = new byte[0x14 + 16 + 0x40];
            Encoding.ASCII.GetBytes("RID").CopyTo(payload, 0);
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(0x10), 0x14);
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(0x14), 1);
            var entry = payload.AsSpan(0x24);
            BinaryPrimitives.WriteSingleLittleEndian(entry.Slice(0x18), 8f);
            BinaryPrimitives.WriteSingleLittleEndian(entry.Slice(0x1C), 5f);
            BinaryPrimitives.WriteSingleLittleEndian(entry.Slice(0x20), 10f);
            Encoding.ASCII.GetBytes("m6t9").CopyTo(entry.Slice(0x24));
            BinaryPrimitives.WriteInt32LittleEndian(entry.Slice(0x2C), 462);
            var record = Assert.Single(ZoneInteractionDecoder.Decode(payload));
            Assert.True(record.IsSubArea);
            Assert.Equal(462, record.Param);
            Assert.Single(ZoneSubAreas.Select(new[] { record }));
            Assert.Equal(462, ZoneSubAreas.FindActive(new[] { record }, new Vector3(1, 0, 1)));
            Assert.Equal(0, ZoneSubAreas.FindActive(new[] { record }, new Vector3(20, 0, 1)));
        }

        [Fact]
        public void Tracker_WalksNearInteriors_AndHoldsTheOneUnderfoot()
        {
            // A street floor and a doorway volume at x = 0; the interior's floor runs from x = 2 to 20, below the street.
            var street = Floor(-50, -50, 50, 50);
            street.SubAreaVolumes = new[] { new ZoneInteraction("m6t9", Vector3.Zero, 0f, new Vector3(4, 5, 4), Param: 462) };
            var interior = Floor(2, -5, 20, 5, 0.5f);
            var world = new WorldState { Collision = street };
            var previous = SubAreaTracker.InteriorCollisionLoader;
            SubAreaTracker.InteriorCollisionLoader = id => id == 462 ? interior : null;
            try
            {
                // Far away: nothing near.
                world.SubAreas.Update(new Vector3(-40, 0, 0));
                Assert.Empty(world.SubAreas.NearSubAreas);
                Assert.Same(street, world.Collision);

                // At the doorway: near and in; the merged collision follows once loaded.
                world.SubAreas.Update(new Vector3(0, 0, 0));
                Assert.Equal(new[] { 462 }, world.SubAreas.NearSubAreas);
                Assert.Equal(462, world.SubAreas.ActiveSubArea);
                for (int i = 0; i < 200 && ReferenceEquals(world.Collision, street); i++)
                {
                    world.SubAreas.Update(new Vector3(0, 0, 0));
                    Thread.Sleep(10);
                }
                Assert.NotSame(street, world.Collision);
                Assert.Equal(street.TriangleCount + interior.TriangleCount, world.Collision!.TriangleCount);
                Assert.Same(street, world.ZoneCollision);

                // Deep inside, past the volume: still in, over the interior's floor.
                world.SubAreas.Update(new Vector3(15, 0.5f, 0));
                Assert.Equal(462, world.SubAreas.ActiveSubArea);

                // Out in the street again: out, then the interior drops away and so does its collision.
                world.SubAreas.Update(new Vector3(-30, 0, 0));
                Assert.Equal(0, world.SubAreas.ActiveSubArea);
                Assert.Empty(world.SubAreas.NearSubAreas);
                for (int i = 0; i < 200 && !ReferenceEquals(world.Collision, street); i++)
                {
                    world.SubAreas.Update(new Vector3(-30, 0, 0));
                    Thread.Sleep(10);
                }
                Assert.Same(street, world.Collision);

                // A new zone mesh forgets the interiors.
                world.SubAreas.Update(new Vector3(0, 0, 0));
                world.Collision = Floor(-10, -10, 10, 10);
                Assert.Equal(0, world.SubAreas.ActiveSubArea);
                Assert.Empty(world.SubAreas.NearSubAreas);
            }
            finally
            {
                SubAreaTracker.InteriorCollisionLoader = previous;
            }
        }

        [Fact]
        public void UnencryptedInteriors_KeepTheirNames_AndStayInTheirRoom()
        {
            if (!Directory.Exists(GameDirectory)) return;
            var rm = new ResourceManager(GameDirectory);
            rm.InitializeFileTable();
            // Port Jeuno's shop (469, file 569) and Lower Jeuno's last three (464-466) have ZoneDefs of mode 0x1A: not
            // encrypted, names included. "Unmasking" their names threw parts out as kilometre-wide brown walls (round 1).
            foreach (int id in new[] { 469, 464, 465, 466 })
            {
                Assert.True(rm.TryLoadSubAreaInterior(id, out var interior, out _));
                Assert.All(interior!.Placements, p => Assert.True(p.Scale.Length() < 100f && p.Position.Length() < 2000f, $"{id}: {p.MeshId} {p.Position} {p.Scale}"));
                Assert.All(interior.MeshGroups, g => Assert.True((g.MaxBounds - g.MinBounds).Length() < 500f, $"{id}: {g.Name} {g.MinBounds}..{g.MaxBounds}"));
            }
        }

        [Fact]
        public void LowerJeuno_HasThirteenInteriors_EachWithItsPlaceholderAndDat()
        {
            if (!Directory.Exists(GameDirectory)) return;
            var rm = new ResourceManager(GameDirectory);
            rm.InitializeFileTable();
            Assert.True(rm.TryLoadZone(245, out var zone, out _));
            var volumes = zone!.Collision!.SubAreaVolumes;
            Assert.Equal(17, volumes.Count);
            var ids = volumes.Select(v => v.Param).Distinct().OrderBy(id => id).ToArray();
            Assert.Equal(Enumerable.Range(454, 13).ToArray(), ids);
            foreach (int id in ids)
            {
                Assert.Contains(zone.MeshGroups, g => g.SubAreaLink == id);
                Assert.True(rm.TryLoadSubAreaInterior(id, out var interior, out var textures), $"interior {id}");
                Assert.NotNull(interior!.Collision);
                Assert.True(textures.Count > 0);
                Assert.Equal(interior.Collision!.TriangleCount, rm.TryLoadSubAreaCollision(id)!.TriangleCount);
                _output.WriteLine($"{id}: {interior.MeshGroups.Count} submeshes, {interior.Collision.TriangleCount} triangles");
            }

            // The headless loader reads the same volumes.
            var headless = new ResourceManager(GameDirectory);
            headless.InitializeFileTable();
            Assert.Equal(17, headless.TryLoadZoneCollision(245)!.SubAreaVolumes.Count);
        }
    }
}
