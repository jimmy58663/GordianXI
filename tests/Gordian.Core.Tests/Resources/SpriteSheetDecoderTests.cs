// tests/Gordian.Core.Tests/Resources/SpriteSheetDecoderTests.cs
using System;
using System.Buffers.Binary;
using System.Text;
using Gordian.Core.Resources.Graphics;
using Xunit;

namespace Gordian.Core.Tests.Resources
{
    public class SpriteSheetDecoderTests
    {
        private static byte[] BuildSheet(ushort flag, byte normalization, bool lensFlare, int cardCount, string textureName)
        {
            int cardSize = 4 + (lensFlare ? 16 : 0) + 6 * 24;
            var payload = new byte[0x18 + cardCount * cardSize];
            BinaryPrimitives.WriteUInt16LittleEndian(payload, flag);
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(2), (ushort)cardCount);
            payload[4] = (byte)(lensFlare ? 1 : 0);
            payload[7] = normalization;
            Encoding.ASCII.GetBytes(textureName.PadRight(16)).CopyTo(payload.AsSpan(8));

            for (int c = 0; c < cardCount; c++)
            {
                int o = 0x18 + c * cardSize;
                BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(o), 1);
                payload[o + 2] = 1; // one quad
                if (lensFlare) BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(o + 4), 0.25f * c);
                int vo = o + 4 + (lensFlare ? 16 : 0);
                for (int v = 0; v < 6; v++)
                {
                    var vs = payload.AsSpan(vo + v * 24, 24);
                    BinaryPrimitives.WriteSingleLittleEndian(vs, v % 2 == 0 ? -2f : 2f);
                    BinaryPrimitives.WriteSingleLittleEndian(vs.Slice(4), v < 3 ? -2f : 2f);
                    BinaryPrimitives.WriteUInt32LittleEndian(vs.Slice(12), 0x80808080);
                    BinaryPrimitives.WriteSingleLittleEndian(vs.Slice(16), 64f * c + 63f);
                    BinaryPrimitives.WriteSingleLittleEndian(vs.Slice(20), 128f);
                }
            }
            return payload;
        }

        [Fact]
        public void Decode_TexelSpaceMoonPhaseSheet_NormalizesUvsAndReadsCards()
        {
            var sheet = SpriteSheetDecoder.Decode(BuildSheet(flag: 1, normalization: 0, lensFlare: false, cardCount: 12, "moon    moonshap"), "moon");

            Assert.NotNull(sheet);
            Assert.Equal("moon    moonshap", sheet.TextureName);
            Assert.False(sheet.IsLensFlare);
            Assert.Equal(12, sheet.Cards.Count);
            Assert.All(sheet.Cards, card => Assert.Equal(6, card.Length));

            var v0 = sheet.Cards[2][0];
            Assert.Equal(-2f, v0.Position.X);
            Assert.Equal((64f * 2 + 63f) / 256f, v0.TexCoord.X, 5);
            Assert.Equal(0.5f, v0.TexCoord.Y, 5);
            Assert.Equal(0x80808080u, v0.ColorRgba);
        }

        [Fact]
        public void Decode_NormalizedLensFlareSheet_SkipsFlareParamsAndKeepsUvs()
        {
            var sheet = SpriteSheetDecoder.Decode(BuildSheet(flag: 0, normalization: 0, lensFlare: true, cardCount: 2, "moon    kasa"), "molf");

            Assert.NotNull(sheet);
            Assert.True(sheet.IsLensFlare);
            Assert.Equal(2, sheet.Cards.Count);
            Assert.Equal(new[] { 0.0f, 0.25f }, sheet.FlareOffsets);
            Assert.Equal(63f, sheet.Cards[0][0].TexCoord.X);
            Assert.Equal(-2f, sheet.Cards[1][0].Position.X);
        }

        [Fact]
        public void Decode_TruncatedPayload_ReturnsNull()
        {
            var full = BuildSheet(1, 0, false, 3, "moon    moonshap");
            Assert.Null(SpriteSheetDecoder.Decode(full.AsSpan(0, full.Length - 10), "moon"));
            Assert.Null(SpriteSheetDecoder.Decode(new byte[8], "moon"));
        }
    }
}
