// tests/Gordian.Core.Tests/Network/FfxiCodecTests.cs
using System;
using System.Text;
using Gordian.Core.Network.Compression;
using Xunit;

namespace Gordian.Core.Tests.Network
{
    public class FfxiCodecTests
    {
        [Fact]
        public void EmbeddedCompressionTableProvider_LoadsValidTables()
        {
            var provider = EmbeddedCompressionTableProvider.Instance;
            uint[] enc = provider.GetCompressTable();
            uint[] dec = provider.GetDecompressTable();

            Assert.NotNull(enc);
            Assert.Equal(512, enc.Length); // 2048 bytes
            Assert.NotNull(dec);
            Assert.Equal(2556, dec.Length); // 10224 bytes
        }

        [Fact]
        public void CompressAndDecompress_AsciiString_RoundTripsAccurately()
        {
            var codec = FfxiCodec.Default;
            byte[] original = Encoding.ASCII.GetBytes("Hello Final Fantasy XI World! Testing custom zlib codec in GordianXI.");

            byte[] compressed = new byte[original.Length * 2 + 16];
            int compressedBytes = codec.Compress(original, compressed);

            Assert.True(compressedBytes > 1);
            Assert.Equal(0x01, compressed[0]); // Must start with 0x01 marker

            byte[] decompressed = new byte[original.Length + 64];
            int decompressedBytes = codec.Decompress(compressed.AsSpan(0, compressedBytes), decompressed);

            Assert.Equal(original.Length, decompressedBytes);
            Assert.Equal(original, decompressed.AsSpan(0, decompressedBytes).ToArray());
        }

        [Fact]
        public void CompressAndDecompress_BinaryPayload_RoundTripsAccurately()
        {
            var codec = FfxiCodec.Default;
            byte[] original = new byte[256];
            for (int i = 0; i < original.Length; i++)
            {
                original[i] = (byte)i;
            }

            byte[] compressed = new byte[original.Length * 4];
            int compressedBytes = codec.Compress(original, compressed);

            Assert.Equal(0x01, compressed[0]);

            byte[] decompressed = new byte[original.Length * 2];
            int decompressedBytes = codec.Decompress(compressed.AsSpan(0, compressedBytes), decompressed);

            Assert.Equal(original.Length, decompressedBytes);
            Assert.Equal(original, decompressed.AsSpan(0, decompressedBytes).ToArray());
        }

        [Fact]
        public void Decompress_InvalidMarker_ThrowsException()
        {
            var codec = FfxiCodec.Default;
            byte[] invalidCompressed = new byte[] { 0x02, 0x10, 0x20 };
            byte[] dest = new byte[64];

            Assert.Throws<InvalidOperationException>(() =>
            {
                codec.Decompress(invalidCompressed, dest);
            });
        }
    }
}
