// tests/Gordian.Core.Tests/Audio/SoundPointerRetailTests.cs
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Gordian.Core.Audio;
using Gordian.Core.Resources;
using Gordian.Core.Resources.Containers;
using Xunit;

namespace Gordian.Core.Tests.Audio
{
    /// <summary>The 0x3D SoundEffectPointer sections of retail DATs (#38).</summary>
    public class SoundPointerRetailTests
    {
        private const string GameDirectory = @"G:\Program Files (x86)\PlayOnline\SquareEnix\FINAL FANTASY XI";
        private readonly ITestOutputHelper _output;

        public SoundPointerRetailTests(ITestOutputHelper output) => _output = output;

        [Fact]
        public void Decode_ReadsTheIdAfterTheMagic()
        {
            byte[] payload = "SeSep  \0"u8.ToArray().Concat(System.BitConverter.GetBytes(16023)).ToArray();
            Assert.True(SoundEffectPointer.TryDecode(payload, out int id));
            Assert.Equal(16023, id);
            Assert.False(SoundEffectPointer.TryDecode(new byte[12], out _));
        }

        [Theory]
        [InlineData(230)] // Southern San d'Oria
        [InlineData(4)]   // Bibiki Bay
        public void Retail_ZonePointersResolveToFiles(int zoneId)
        {
            if (!Directory.Exists(GameDirectory))
            {
                return;
            }

            var rm = new ResourceManager(GameDirectory);
            rm.InitializeFileTable();
            byte[]? dat = rm.LoadDatBytesByFileId(ZoneDataLoader.GetZoneModelFileId(zoneId));
            Assert.NotNull(dat);
            DatDirectoryNode root = DatDirectoryTree.Build(dat);
            var pointers = root.CollectByTypeRecursive(DatSectionType.SoundEffectPointer);
            var locator = new FfxiSoundLocator(GameDirectory);
            int resolved = 0;
            foreach (var group in pointers.GroupBy(p => p.Parent.DatId))
            {
                _output.WriteLine($"dir '{group.Key}' (parent '{group.First().Parent.Parent?.DatId}'): {group.Count()} pointers, e.g. " +
                    string.Join(", ", group.Take(6).Select(p => SoundEffectPointer.TryDecode(p.Payload.Span, out int id) ? $"{p.DatId}->{id}" : $"{p.DatId}->?")));
            }

            foreach (var p in pointers)
            {
                Assert.True(SoundEffectPointer.TryDecode(p.Payload.Span, out int id), p.DatId);
                if (locator.FindEffect(id) is not null)
                {
                    resolved++;
                }
            }

            _output.WriteLine($"zone {zoneId}: {pointers.Count} pointers, {resolved} resolve to files");
            Assert.NotEmpty(pointers);
            Assert.True(resolved >= pointers.Count * 9 / 10);

            ZoneSoundTable table = ZoneSoundTable.Read(root);
            Assert.Equal(100001, table.FootstepSound(1, '1', 0, wet: false));
            Assert.Equal(100011, table.FootstepSound(1, '1', 0, wet: true));
            if (zoneId == 4)
            {
                Assert.Equal(1013, table.AmbientSound("suny", 7 * 60));
                Assert.Equal(1015, table.AmbientSound("suny", 20 * 60));
                Assert.Equal(1015, table.AmbientSound("suny", 3 * 60));
                Assert.Equal(1013, table.AmbientSound("rain", 12 * 60, false, "clod"));
            }
            else
            {
                Assert.Equal(1081, table.AmbientSound("fine", 12 * 60));
                Assert.Equal(new[] { 9021, 9022 }, table.Doors["_6e1"]);
            }

            foreach (int id in new[] { 1013, 1015, 1081 })
            {
                string? path = locator.FindEffect(id);
                if (path is not null && FfxiSoundHeader.TryParse(File.ReadAllBytes(path), out FfxiSoundHeader h))
                {
                    _output.WriteLine($"ambient {id}: {h.Format} {h.Channels} ch {h.SampleRate} Hz {h.DurationSeconds:F1} s looped {h.IsLooped}");
                }
            }
        }
        /// <summary>The rain sets apply in rain and squall only (#342).</summary>
        [Theory]
        [InlineData("rain", true)]
        [InlineData("SQUL", true)]
        [InlineData("fine", false)]
        [InlineData("thdr", false)]
        [InlineData("snow", false)]
        [InlineData(null, false)]
        public void WetWeather_IsRainAndSquall(string? weather, bool wet) => Assert.Equal(wet, ZoneSoundTable.IsWetWeather(weather));

        /// <summary>
        /// South Gustaberg's dirt (#342): its floors are mostly Path and Stone, and a dry Path step plays the dry set's
        /// 100001 whether walking or running; the rain set's 100011 (wet) only in the rain. Before, running took fser and
        /// played the wet step on dry dirt.
        /// </summary>
        [Fact]
        public void Retail_SouthGustabergDirt_PlaysTheDryPathStep()
        {
            if (!Directory.Exists(GameDirectory)) return;
            var rm = new ResourceManager(GameDirectory);
            rm.InitializeFileTable();
            byte[]? dat = rm.LoadDatBytesByFileId(ZoneDataLoader.GetZoneModelFileId(107));
            if (dat == null) return;
            ZoneSoundTable table = ZoneSoundTable.Read(DatDirectoryTree.Build(dat));
            Assert.Equal(100001, table.FootstepSound((int)Gordian.Core.World.Collision.CollisionTerrain.Path, '1', 0, wet: false));
            Assert.Equal(100011, table.FootstepSound((int)Gordian.Core.World.Collision.CollisionTerrain.Path, '1', 0, wet: true));
            // Water steps are the same in both sets.
            Assert.Equal(table.FootstepSound(8, '1', 0, wet: false), table.FootstepSound(8, '1', 0, wet: true));

            var collision = rm.TryLoadZoneCollision(107)!;
            var area = new Dictionary<Gordian.Core.World.Collision.CollisionTerrain, float>();
            foreach (var t in collision.Triangles)
            {
                if (t.IsWall || t.Normal.Y > -0.5f) continue;
                area[t.Terrain] = area.GetValueOrDefault(t.Terrain) + System.Numerics.Vector3.Cross(t.B - t.A, t.C - t.A).Length() * 0.5f;
            }
            _output.WriteLine("South Gustaberg floor area by terrain: " + string.Join(", ", area.OrderByDescending(a => a.Value).Select(a => $"{a.Key} {a.Value:F0}")));
            Assert.False(area.ContainsKey(Gordian.Core.World.Collision.CollisionTerrain.ShallowWater));
            Assert.Contains(area.OrderByDescending(a => a.Value).First().Key, new[] { Gordian.Core.World.Collision.CollisionTerrain.Path, Gordian.Core.World.Collision.CollisionTerrain.Stone });
        }

        /// <summary>Doors' own open / close sounds come from their routines' op 0x0B (round 5 E2).</summary>
        [Theory]
        [InlineData(72, "_20d", 9229, 9230, 30f)]
        [InlineData(237, "_6l0", 9021, 9022, 10f)]
        public void DoorRoutineSounds_ComeFromTheDoorsOpenAndClos(int zoneId, string doorId, int open, int close, float far)
        {
            if (!Directory.Exists(GameDirectory)) return;
            var rm = new ResourceManager(GameDirectory);
            rm.InitializeFileTable();
            byte[]? dat = rm.LoadDatBytesByFileId(ZoneDataLoader.GetZoneModelFileId(zoneId));
            if (dat == null) return;
            var sounds = ZoneSoundTable.Read(DatDirectoryTree.Build(dat)).DoorRoutineSounds[doorId];
            Assert.Equal(open, sounds.OpenSound);
            Assert.Equal(close, sounds.CloseSound);
            Assert.Equal(far, sounds.Far, 1);
            Assert.Equal(3f, sounds.Near, 1);
        }
    }
}
