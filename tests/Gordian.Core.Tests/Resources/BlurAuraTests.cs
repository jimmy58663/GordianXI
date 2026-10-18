// tests/Gordian.Core.Tests/Resources/BlurAuraTests.cs
using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using System.Numerics;
using Gordian.Core.Resources;
using Gordian.Core.Resources.Models;
using Xunit;

namespace Gordian.Core.Tests.Resources
{
    public class BlurAuraTests
    {
        private static byte[] Section(params (byte R, byte G, byte B, byte A, float Radius)[] rings)
        {
            var body = new byte[16 + 8 * rings.Length];
            body[0] = 11;
            body[1] = 7;
            BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(2), 400);
            body[4] = body[5] = body[6] = 192;
            body[8] = body[9] = body[10] = 128;
            BinaryPrimitives.WriteSingleLittleEndian(body.AsSpan(12), 50f);
            for (int i = 0; i < rings.Length; i++)
            {
                int o = 16 + 8 * i;
                body[o] = rings[i].R;
                body[o + 1] = rings[i].G;
                body[o + 2] = rings[i].B;
                body[o + 3] = rings[i].A;
                BinaryPrimitives.WriteSingleLittleEndian(body.AsSpan(o + 4), rings[i].Radius);
            }
            return body;
        }

        [Fact]
        public void Decode_ReadsHeaderAndRings_UpToTheFirstRingAtRadiusZero()
        {
            var aura = BlurAura.Decode(Section((255, 255, 128, 12, 60f), (128, 128, 128, 10, 130f), (128, 128, 128, 32, 0f), (0, 0, 128, 63, 0f)));

            Assert.NotNull(aura);
            Assert.Equal(11, aura!.Layers);
            Assert.Equal(7, aura.Frequency);
            Assert.Equal(400, aura.Falloff);
            Assert.Equal(192 / 255f, aura.BaseColor.X, 4);
            Assert.Equal(50f, aura.InnerRadius);
            Assert.Equal(2, aura.Rings.Count);
            Assert.Equal(new Vector3(1f, 1f, 128 / 255f), aura.Rings[0].Color);
            Assert.Equal(12, aura.Rings[0].Alpha);
            Assert.Equal(130f, aura.Rings[1].Radius);
        }

        [Fact]
        public void Decode_RejectsATruncatedSection()
        {
            Assert.Null(BlurAura.Decode(new byte[12]));
        }

        [Fact]
        public void Avatars_CarryTheirRetailAuras()
        {
            string gameDir = @"G:\Program Files (x86)\PlayOnline\SquareEnix\FINAL FANTASY XI";
            if (!Directory.Exists(gameDir)) return;
            var rm = new ResourceManager(gameDir);
            rm.InitializeFileTable();

            // Garuda (21): wide neutral rings; Fenrir (17): seven cyan rings at radius 110 (retail capture SMN_Avatars.mp4).
            var garuda = EntityModelLoader.LoadMonsterModel(21, rm.LoadDatBytesByFileId);
            var fenrir = EntityModelLoader.LoadMonsterModel(17, rm.LoadDatBytesByFileId);
            if (garuda == null || fenrir == null) return;

            Assert.NotNull(garuda.BlurAura);
            Assert.Equal(7, garuda.BlurAura!.Rings.Count);
            Assert.Equal(60f, garuda.BlurAura.Rings.Min(r => r.Radius));
            Assert.Equal(130f, garuda.BlurAura.Rings.Max(r => r.Radius));

            Assert.NotNull(fenrir.BlurAura);
            Assert.All(fenrir.BlurAura!.Rings, r =>
            {
                Assert.Equal(new Vector3(128 / 255f, 1f, 1f), r.Color);
                Assert.Equal(110f, r.Radius);
            });

            // An ordinary monster has none.
            var rabbit = EntityModelLoader.LoadMonsterModel(269, rm.LoadDatBytesByFileId);
            if (rabbit != null) Assert.Null(rabbit.BlurAura);
        }
    }
}
