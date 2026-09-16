// tests/Gordian.Core.Tests/Resources/TextureDecoderTests.cs
using System;
using System.Buffers.Binary;
using Gordian.Core.Resources.Graphics;
using Xunit;

namespace Gordian.Core.Tests.Resources
{
    public class TextureDecoderTests
    {
        [Fact]
        public void TextureDecoder_DecompressDxt1_UnpacksSolidColorBlock()
        {
            // 4x4 DXT1 block:
            // c0 = pure red (RGB565 = 0xF800), c1 = pure blue (RGB565 = 0x001F)
            // lookup = 0x00000000 (all 16 pixels select c0 -> Red)
            byte[] dxt = new byte[8];
            BinaryPrimitives.WriteUInt16LittleEndian(dxt.AsSpan(0, 2), 0xF800);
            BinaryPrimitives.WriteUInt16LittleEndian(dxt.AsSpan(2, 2), 0x001F);
            BinaryPrimitives.WriteUInt32LittleEndian(dxt.AsSpan(4, 4), 0x00000000);

            byte[] rgba = new byte[4 * 4 * 4];
            TextureDecoder.DecompressDxt1(dxt, rgba, 4, 4);

            // Pixel (0,0) must be pure red (R=255, G=0, B=0, A=255)
            Assert.Equal(255, rgba[0]); // R
            Assert.Equal(0, rgba[1]);   // G
            Assert.Equal(0, rgba[2]);   // B
            Assert.Equal(255, rgba[3]); // A

            // Pixel (3,3) must also be pure red
            int lastPixelOffset = (3 * 4 + 3) * 4;
            Assert.Equal(255, rgba[lastPixelOffset]);
            Assert.Equal(0, rgba[lastPixelOffset + 1]);
            Assert.Equal(0, rgba[lastPixelOffset + 2]);
            Assert.Equal(255, rgba[lastPixelOffset + 3]);
        }

        [Fact]
        public void TextureDecoder_DecodeTexture_PalettedTexture_DecodesRgba()
        {
            // Create a small 2x2 paletted texture (type 0x91)
            int headerLen = 0x40;
            int paletteLen = 256 * 4;
            int pixelLen = 4;
            byte[] data = new byte[headerLen + paletteLen + pixelLen + 32];

            data[0] = 0x91; // type
            // Width = 2, Height = 2
            int p = 1 + 16 + 4;
            BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(p, 4), 2); p += 4;
            BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(p, 4), 2); p += 4;
            p += 2; // skip 1
            BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(p, 2), 8); p += 2; // 8 bit count
            p += 20; // zeros
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(p, 4), 0x20); p += 4; // 32-bit palette

            // Write palette color index 1: Green (R=0, G=255, B=0, A=128)
            int palOffset = p;
            uint greenRgba = (128u << 24) | (0u << 16) | (255u << 8) | 0u;
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(palOffset + (1 * 4), 4), greenRgba);

            // Pixels at end: 4 pixels pointing to palette index 1
            int pixStart = palOffset + paletteLen;
            data[pixStart] = 1;
            data[pixStart + 1] = 1;
            data[pixStart + 2] = 1;
            data[pixStart + 3] = 1;

            var texture = TextureDecoder.DecodeTexture(data);

            Assert.NotNull(texture);
            Assert.Equal(2, texture.Width);
            Assert.Equal(2, texture.Height);
            Assert.Equal(16, texture.RgbaPixels.Length);

            // Pixel 0 should be green (R=0, G=255, B=0, A=255 because client 128 is scaled * 2)
            Assert.Equal(0, texture.RgbaPixels[0]);
            Assert.Equal(255, texture.RgbaPixels[1]);
            Assert.Equal(0, texture.RgbaPixels[2]);
            Assert.Equal(255, texture.RgbaPixels[3]);
        }
    }
}
