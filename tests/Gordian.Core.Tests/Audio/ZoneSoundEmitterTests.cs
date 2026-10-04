// tests/Gordian.Core.Tests/Audio/ZoneSoundEmitterTests.cs
using System;
using System.IO;
using System.Linq;
using System.Numerics;
using Gordian.Core.Audio;
using Gordian.Core.Resources;
using Gordian.Core.Resources.Containers;
using Xunit;
using Xunit.Abstractions;

namespace Gordian.Core.Tests.Audio
{
    /// <summary>The sound generators of retail zones (#39).</summary>
    public class ZoneSoundEmitterTests
    {
        private const string GameDirectory = @"G:\Program Files (x86)\PlayOnline\SquareEnix\FINAL FANTASY XI";
        private readonly ITestOutputHelper _output;

        public ZoneSoundEmitterTests(ITestOutputHelper output) => _output = output;

        [Fact]
        public void SourceFor_PathSoundsComeFromTheNearestPointOnThePath()
        {
            var emitter = new ZoneSoundEmitter("hama", 2011, Vector3.Zero, 10, 60,
                new[] { new Vector3(0, 0, 0), new Vector3(100, 0, 0) }, null, true, 1);
            Assert.Equal(new Vector3(40, 0, 0), emitter.SourceFor(new Vector3(40, 0, 20)));
            Assert.Equal(new Vector3(100, 0, 0), emitter.SourceFor(new Vector3(150, 0, 0)));
            Assert.Equal(1f, emitter.VolumeAt(0.5f));
        }

        [Fact]
        public void ReadPath_ReadsCountAndPoints()
        {
            var payload = new byte[0x40 + 2 * 0x20];
            "RAB"u8.CopyTo(payload);
            BitConverter.GetBytes(2).CopyTo(payload, 0x30);
            BitConverter.GetBytes(560f).CopyTo(payload, 0x40);
            BitConverter.GetBytes(710f).CopyTo(payload, 0x48);
            BitConverter.GetBytes(-3f).CopyTo(payload, 0x64);
            Vector3[] points = ZoneSoundEmitterDecoder.ReadPath(payload);
            Assert.Equal(new[] { new Vector3(560, 0, 710), new Vector3(0, -3, 0) }, points);
        }

        [Theory]
        [InlineData(4)]   // Bibiki Bay: shoreline paths
        [InlineData(230)] // Southern San d'Oria: fountains with a daytime volume curve
        public void Retail_ZoneSoundGenerators(int zoneId)
        {
            if (!Directory.Exists(GameDirectory))
            {
                return;
            }

            var rm = new ResourceManager(GameDirectory);
            rm.InitializeFileTable();
            byte[] dat = rm.LoadDatBytesByFileId(ZoneDataLoader.GetZoneModelFileId(zoneId))!;
            var emitters = ZoneSoundEmitterDecoder.Read(DatDirectoryTree.Build(dat));
            foreach (ZoneSoundEmitter e in emitters)
            {
                _output.WriteLine($"{e.Name}: sound {e.SoundId} at {e.Position} near {e.Near} far {e.Far} path {e.Path.Count} points, time volume {(e.TimeVolume is null ? "none" : $"{e.VolumeAt(0.1f):F1} / {e.VolumeAt(0.5f):F1}")}, auto {e.AutoRun}");
            }

            if (zoneId == 4)
            {
                Assert.Equal(3, emitters.Count);
                ZoneSoundEmitter mina = emitters.Single(e => e.Name == "mina");
                Assert.Equal(2011, mina.SoundId);
                Assert.Equal(60f, mina.Far);
                Assert.Equal(10f, mina.Near);
                Assert.Equal(2, mina.Path.Count);
                Assert.Equal(new Vector3(560, mina.Position.Y, 710), mina.Path[0]); // a flat path takes the generator's height
            }
            else
            {
                Assert.Equal(5, emitters.Count);
                ZoneSoundEmitter se01 = emitters.Single(e => e.Name == "se01");
                Assert.Equal(2003, se01.SoundId);
                Assert.Equal(30f, se01.Far);
                Assert.NotNull(se01.TimeVolume);
                Assert.Equal(0f, se01.VolumeAt(0.1f));
                Assert.Equal(1f, se01.VolumeAt(0.5f));
            }
        }

        [Fact]
        public void Retail_EveryZoneDecodes()
        {
            if (!Directory.Exists(GameDirectory))
            {
                return;
            }

            var rm = new ResourceManager(GameDirectory);
            rm.InitializeFileTable();
            int zones = 0, emitters = 0, paths = 0, curves = 0, auto = 0;
            for (int zone = 1; zone < 300; zone++)
            {
                byte[]? dat = rm.LoadDatBytesByFileId(ZoneDataLoader.GetZoneModelFileId(zone));
                if (dat is null || dat.Length < 64)
                {
                    continue;
                }

                zones++;
                foreach (ZoneSoundEmitter e in ZoneSoundEmitterDecoder.Read(DatDirectoryTree.Build(dat)))
                {
                    emitters++;
                    paths += e.Path.Count > 0 ? 1 : 0;
                    curves += e.TimeVolume is null ? 0 : 1;
                    auto += e.AutoRun ? 1 : 0;
                    Assert.True(e.Far > 0 && e.Near >= 0 && e.Near <= e.Far, $"{zone}/{e.Name}");
                }
            }

            _output.WriteLine($"{zones} zones: {emitters} sound generators ({auto} auto-run), {paths} on paths, {curves} with a time-of-day volume");
            Assert.True(emitters > 1000);
        }
    }
}
