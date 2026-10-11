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
            // Flagged so the particle shader can halve it back to the half-scale alpha it expects (#208).
            Assert.True(texture.AlphaDoubled);
        }

        [Fact]
        public void TextureDecoder_DecodeTexture_Dxt3Texture_ReadsBlockDataAtVerifiedOffset()
        {
            // Header layout verified against real FFXI zone/gear DAT texture sections by rendering
            // decoded candidates and visually comparing: pixel data starts at 0x45 when the fourCC
            // tag sits at offset 0x39 (0x50, which matches payload_len - width*height*bpp exactly,
            // looked byte-clean but decodes to visual noise -- the true data starts earlier).
            const int width = 4, height = 4;
            const int headerLen = 0x45;
            byte[] block = new byte[16]; // one 4x4 DXT3 block
            // Alpha: all 16 pixels fully opaque (0xF nibble each)
            for (int i = 0; i < 8; i++) block[i] = 0xFF;
            // Color: c0 = pure red (0xF800), c1 = pure blue (0x001F), lookup = all c0
            BinaryPrimitives.WriteUInt16LittleEndian(block.AsSpan(8, 2), 0xF800);
            BinaryPrimitives.WriteUInt16LittleEndian(block.AsSpan(10, 2), 0x001F);
            BinaryPrimitives.WriteUInt32LittleEndian(block.AsSpan(12, 4), 0x00000000);

            byte[] data = new byte[headerLen + block.Length];
            data[0] = 0xA1; // DXT texture type
            BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0x15, 4), width);
            BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0x19, 4), height);
            "3TXD"u8.CopyTo(data.AsSpan(0x39, 4));
            block.CopyTo(data.AsSpan(headerLen, block.Length));

            var texture = TextureDecoder.DecodeTexture(data);

            Assert.NotNull(texture);
            Assert.Equal(width, texture.Width);
            Assert.Equal(height, texture.Height);

            // Pixel (0,0) must be pure opaque red, decoded from the block at the verified offset
            Assert.Equal(255, texture.RgbaPixels[0]); // R
            Assert.Equal(0, texture.RgbaPixels[1]);   // G
            Assert.Equal(0, texture.RgbaPixels[2]);   // B
            Assert.Equal(255, texture.RgbaPixels[3]); // A
            Assert.False(texture.AlphaDoubled); // DXT alpha is kept as stored
        }

        /// <summary>
        /// The header-only stub of ROM/30/66 (#310): type 0xA1, 64x64, 8 bpp, image size 0x2000, a 64-byte payload with
        /// no FourCC and no pixels. It used to read the fallback FourCC one byte past the end and throw.
        /// </summary>
        internal static byte[] HeaderOnlyDxtStub()
        {
            byte[] data = new byte[0x40];
            data[0] = 0xA1;
            "tim     hf_sti1_"u8.CopyTo(data.AsSpan(1, 16));
            BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0x11, 4), 0x28);
            BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0x15, 4), 64);
            BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0x19, 4), 64);
            BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(0x1D, 2), 1);
            BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(0x1F, 2), 8);
            BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0x25, 4), 0x2000);
            return data;
        }

        [Fact]
        public void DecodeTexture_HeaderOnlyDxtStub_IsSkippedWithoutThrowing()
        {
            Assert.Null(TextureDecoder.DecodeTexture(HeaderOnlyDxtStub()));
        }

        [Theory]
        [InlineData(0x39, "1TXD", 0x45 + 7)]  // FourCC at 0x39, one byte short of the first DXT1 block
        [InlineData(0x39, "3TXD", 0x45 + 15)] // one byte short of the first DXT3 block
        [InlineData(0x3D, "5TXD", 0x49 + 15)] // fallback FourCC, one byte short of the first DXT5 block
        [InlineData(0x3D, "1TXD", 0x40)]      // fallback FourCC itself runs past the payload
        public void DecodeTexture_DxtPayloadEndingBeforeItsFirstBlock_IsSkipped(int fourCcOffset, string fourCc, int length)
        {
            byte[] data = new byte[length];
            data[0] = 0xA1;
            BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0x15, 4), 4);
            BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0x19, 4), 4);
            int copy = Math.Min(4, length - fourCcOffset);
            System.Text.Encoding.ASCII.GetBytes(fourCc).AsSpan(0, copy).CopyTo(data.AsSpan(fourCcOffset, copy));

            Assert.Null(TextureDecoder.DecodeTexture(data));
        }

        [Fact]
        public void DecodeTexture_Dxt1AtFallbackOffset_DecodesItsFirstBlock()
        {
            byte[] data = new byte[0x49 + 8];
            data[0] = 0xA1;
            BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0x15, 4), 4);
            BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0x19, 4), 4);
            "1TXD"u8.CopyTo(data.AsSpan(0x3D, 4));
            BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(0x49, 2), 0x001F); // blue

            var texture = TextureDecoder.DecodeTexture(data);

            Assert.NotNull(texture);
            Assert.Equal(255, texture!.RgbaPixels[2]);
        }

        [Theory]
        [InlineData(0x30)]
        [InlineData(0x38)]
        public void DecodeTexture_PalettedPayloadShorterThanItsHeader_IsSkipped(int length)
        {
            byte[] data = new byte[length];
            data[0] = 0x91;
            BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0x15, 4), 2);
            BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0x19, 4), 2);

            Assert.Null(TextureDecoder.DecodeTexture(data));
        }
    }
}
