// tests/Gordian.Core.Tests/Resources/ModelSizeScaleTests.cs
using System;
using System.Buffers.Binary;
using System.IO;
using Gordian.Core.Resources;
using Gordian.Core.Resources.Models;
using Gordian.Core.Resources.Tables;
using Xunit;

namespace Gordian.Core.Tests.Resources
{
    /// <summary>
    /// A fixed model's render scale per size class: its <c>info</c> section bytes 10-13 over 100 (#82 round 5, Fenrir drawn
    /// too large against retail's <c>Fenrir_AvatarSize_Retail_2026-11-19.png</c>).
    /// </summary>
    public class ModelSizeScaleTests
    {
        private const string GameDirectory = @"G:\Program Files (x86)\PlayOnline\SquareEnix\FINAL FANTASY XI";

        private static byte[] InfoDat(params byte[] bytes10To13)
        {
            var dat = new byte[32];
            System.Text.Encoding.ASCII.GetBytes("info").CopyTo(dat, 0);
            BinaryPrimitives.WriteUInt32LittleEndian(dat.AsSpan(4), 0x45 | ((32u / 16) << 7));
            for (int i = 0; i < 16; i++) dat[16 + i] = 0xFF;
            bytes10To13.CopyTo(dat, 16 + 10);
            return dat;
        }

        [Fact]
        public void SizeScales_AreInfoBytes10To13_OverAHundred()
        {
            var scales = EntityModelLoader.ReadSizeScales(InfoDat(85, 100, 115, 140));
            Assert.Equal(new[] { 0.85f, 1.00f, 1.15f, 1.40f }, scales);
            var model = new EntityModel { SizeScales = scales };
            Assert.Equal(0.85f, model.SizeScaleFor(0));
            Assert.Equal(1.15f, model.SizeScaleFor(2));
            Assert.Equal(1.40f, model.SizeScaleFor(3));
        }

        [Fact]
        public void UnsetOrTinyBytes_ReadAsOne_AndNoInfoAsNoScale()
        {
            Assert.Equal(new[] { 1f, 1f, 0.92f, 1f }, EntityModelLoader.ReadSizeScales(InfoDat(0xFF, 0, 92, 1)));
            Assert.Null(EntityModelLoader.ReadSizeScales(new byte[8]));
            Assert.Equal(1f, new EntityModel().SizeScaleFor(1));
        }

        [Theory]
        [InlineData(17u, 0.75f)] // Fenrir
        [InlineData(21u, 0.90f)] // Garuda
        public void Avatars_CarryTheirSizeInTheirInfo(uint modelId, float expected)
        {
            if (!Directory.Exists(GameDirectory)) return;
            var rm = new ResourceManager(GameDirectory);
            rm.InitializeFileTable();
            var dat = rm.LoadDatBytesByFileId(CharacterEquipmentResolver.GetMonsterFileId(modelId))!;
            var scales = EntityModelLoader.ReadSizeScales(dat)!;
            Assert.Equal(expected, scales[0], 3);
        }
    }
}
