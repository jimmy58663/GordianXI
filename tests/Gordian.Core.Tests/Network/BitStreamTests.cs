// tests/Gordian.Core.Tests/Network/BitStreamTests.cs
using System;
using Gordian.Core.Network.Packets;
using Xunit;

namespace Gordian.Core.Tests.Network
{
    public class BitStreamTests
    {
        [Fact]
        public void BitStreamWriterAndReader_RoundtripBasicTypes()
        {
            byte[] buffer = new byte[32];
            var writer = new BitStreamWriter(buffer);

            writer.WriteUInt32(0x12345678, 32);
            writer.WriteByte(0x2A, 6);
            writer.WriteBool(true);
            writer.WriteUInt16(0x0ABC, 12);
            writer.WriteUInt32(0x1F, 5);

            var reader = new BitStreamReader(buffer);

            Assert.Equal(0x12345678u, reader.ReadUInt32(32));
            Assert.Equal((byte)0x2A, reader.ReadByte(6));
            Assert.True(reader.ReadBool());
            Assert.Equal((ushort)0x0ABC, reader.ReadUInt16(12));
            Assert.Equal(0x1Fu, reader.ReadUInt32(5));
        }

        [Fact]
        public void BitStream_UnalignedCrossingAndOddBitWidths()
        {
            byte[] buffer = new byte[32];
            var writer = new BitStreamWriter(buffer);

            // Write fields with various odd bit widths
            writer.WriteBits(3, 3);          // ActionResolution (3 bits)
            writer.WriteBits(1, 2);          // Kind (2 bits)
            writer.WriteBits(1023, 12);      // Animation (12 bits)
            writer.WriteBits(17, 5);         // Info (5 bits)
            writer.WriteBits(23, 5);         // Scale (5 bits)
            writer.WriteBits(12345, 17);     // Param (17 bits)
            writer.WriteBits(456, 10);       // Message (10 bits)
            writer.WriteBits(0x7FFFFFFFu, 31);// Modifier (31 bits)

            var reader = new BitStreamReader(buffer);

            Assert.Equal(3UL, reader.ReadBits(3));
            Assert.Equal(1UL, reader.ReadBits(2));
            Assert.Equal(1023UL, reader.ReadBits(12));
            Assert.Equal(17UL, reader.ReadBits(5));
            Assert.Equal(23UL, reader.ReadBits(5));
            Assert.Equal(12345UL, reader.ReadBits(17));
            Assert.Equal(456UL, reader.ReadBits(10));
            Assert.Equal(0x7FFFFFFFu, reader.ReadUInt32(31));
        }

        [Fact]
        public void BitStream_Full64BitReadAndWrite()
        {
            byte[] buffer = new byte[16];
            var writer = new BitStreamWriter(buffer);

            ulong expected = 0xFEDCBA9876543210UL;
            writer.WriteBits(expected, 64);

            var reader = new BitStreamReader(buffer);
            ulong actual = reader.ReadBits(64);

            Assert.Equal(expected, actual);
        }

        [Fact]
        public void BitStreamReader_PeekBits_DoesNotAdvanceOffset()
        {
            byte[] buffer = new byte[8];
            var writer = new BitStreamWriter(buffer);
            writer.WriteUInt32(0xCAFEBABE, 32);

            var reader = new BitStreamReader(buffer);
            ulong peek1 = reader.PeekBits(32);
            ulong peek2 = reader.PeekBits(32);
            ulong actual = reader.ReadBits(32);

            Assert.Equal(0xCAFEBABEUL, peek1);
            Assert.Equal(0xCAFEBABEUL, peek2);
            Assert.Equal(0xCAFEBABEUL, actual);
        }

        [Fact]
        public void BitStreamReader_RemainingBits_TracksCorrectly()
        {
            byte[] buffer = new byte[4]; // 32 bits
            var reader = new BitStreamReader(buffer);

            Assert.Equal(32, reader.RemainingBits);
            reader.ReadBits(10);
            Assert.Equal(22, reader.RemainingBits);
            reader.ReadBits(22);
            Assert.Equal(0, reader.RemainingBits);
            reader.ReadBits(5);
            Assert.Equal(0, reader.RemainingBits);
        }
    }
}
