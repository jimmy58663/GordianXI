// tests/Gordian.Core.Tests/Audio/SoundPointerRetailTests.cs
using System.IO;
using System.Linq;
using Gordian.Core.Audio;
using Gordian.Core.Resources;
using Gordian.Core.Resources.Containers;
using Xunit;
using Xunit.Abstractions;

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
            Assert.Equal(100001, table.FootstepSound(1, '1', 0, running: false));
            Assert.Equal(100011, table.FootstepSound(1, '1', 0, running: true));
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
    }
}
