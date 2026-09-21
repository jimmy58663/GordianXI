// tests/Gordian.Core.Tests/Network/PacketParserTests.cs
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Threading.Tasks;
using Gordian.Core.Config;
using Gordian.Core.Network;
using Gordian.Core.Network.Compression;
using Gordian.Core.Network.Crypto;
using Xunit;

namespace Gordian.Core.Tests.Network
{
    public class PacketParserTests
    {
        private static void PackSubPacketHeader(Span<byte> buffer, ushort packetId, int size, ushort sequence)
        {
            // Set first 9 bits to packetId
            ushort idBits = (ushort)(packetId & 0x1FF);
            buffer[0] = (byte)(idBits & 0xFF);
            buffer[1] = (byte)((idBits >> 8) & 1);

            // Set size in byte 1 (upper 7 bits)
            int rounded = ((size + 3) & ~3) / 2;
            buffer[1] |= (byte)(rounded & 0xFE);

            BinaryPrimitives.WriteUInt16LittleEndian(buffer.Slice(2, 2), sequence);
        }

        [Fact]
        public void ProcessIncomingChunk_ParsesSubPacketsAndTriggersPong()
        {
            var profile = new SessionProfile();
            var sentChunks = new List<byte[]>();

            Task SendChunkCallback(ReadOnlyMemory<byte> mem, bool isHighPriority)
            {
                sentChunks.Add(mem.ToArray());
                return Task.CompletedTask;
            }

            using var suite = new LegacyBlowfishCryptoSuite();
            byte[] key = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
            suite.InitializeKey(key);

            var parser = new PacketParser(profile, SendChunkCallback, suite, FfxiCodec.Default);

            // Construct sub-packet 0x015 (Keepalive ping)
            ushort seq = 0x1234;
            byte[] subPacket = new byte[4];
            PackSubPacketHeader(subPacket, 0x015, 4, seq);

            // Compress sub-packet
            byte[] compressed = new byte[64];
            int compressedBytes = FfxiCodec.Default.Compress(subPacket, compressed);

            // Build full datagram: 28-byte header + compressed payload + 16-byte MD5
            const int headerSize = 28;
            byte[] datagram = new byte[headerSize + compressedBytes + 32];
            compressed.AsSpan(0, compressedBytes).CopyTo(datagram.AsSpan(headerSize));

            int totalDatagramSize = suite.EncryptAndSign(datagram, headerSize, compressedBytes);

            // Process datagram through parser
            parser.ProcessIncomingChunk(datagram.AsSpan(0, totalDatagramSize));

            // Verify keepalive pong was triggered
            Assert.Single(sentChunks);
            byte[] pong = sentChunks[0];
            Assert.Equal(HandshakePackets.PosSubPacketSize, pong.Length);
            ushort pongType = (ushort)(BinaryPrimitives.ReadUInt16LittleEndian(pong.AsSpan(0, 2)) & 0x1FF);
            ushort pongSeq = BinaryPrimitives.ReadUInt16LittleEndian(pong.AsSpan(2, 2));

            Assert.Equal(0x015, pongType);
            Assert.Equal(seq, pongSeq);
        }

        [Fact]
        public void ProcessIncomingChunk_FeatureRestrictions_UpdatesProfile()
        {
            var profile = new SessionProfile();
            var parser = new PacketParser(profile, (_, _) => Task.CompletedTask);

            // Construct sub-packet 0x0EE (Server feature restrictions, 4-byte header + 8-byte ulong payload = 12)
            var expected = FeatureRestrictions.Combat | FeatureRestrictions.Movement | FeatureRestrictions.RawPacketInjection;
            byte[] subPacket = new byte[12];
            PackSubPacketHeader(subPacket, 0x0EE, 12, 0x0001);
            BinaryPrimitives.WriteUInt64LittleEndian(subPacket.AsSpan(4, 8), (ulong)expected);

            byte[] compressed = new byte[64];
            int compressedBytes = FfxiCodec.Default.Compress(subPacket, compressed);

            const int headerSize = 28;
            byte[] datagram = new byte[headerSize + compressedBytes + 32];
            compressed.AsSpan(0, compressedBytes).CopyTo(datagram.AsSpan(headerSize));

            int totalDatagramSize = parser.CryptoSuite.EncryptAndSign(datagram, headerSize, compressedBytes);

            parser.ProcessIncomingChunk(datagram.AsSpan(0, totalDatagramSize));

            Assert.Equal(expected, profile.FeatureRestrictions);
        }

        [Fact]
        public void ProcessIncomingChunk_WithPerformanceTracker_RecordsInboundPacketsAndLatency()
        {
            var profile = new SessionProfile();
            var tracker = new Gordian.Core.Diagnostics.SessionPerformanceTracker();
            var parser = new PacketParser(profile, (_, _) => Task.CompletedTask)
            {
                Performance = tracker
            };

            // Construct sub-packet 0x015
            byte[] subPacket = new byte[4];
            PackSubPacketHeader(subPacket, 0x015, 4, 0x0042);

            byte[] compressed = new byte[64];
            int compressedBytes = FfxiCodec.Default.Compress(subPacket, compressed);

            const int headerSize = 28;
            byte[] datagram = new byte[headerSize + compressedBytes + 32];
            compressed.AsSpan(0, compressedBytes).CopyTo(datagram.AsSpan(headerSize));

            int totalDatagramSize = parser.CryptoSuite.EncryptAndSign(datagram, headerSize, compressedBytes);

            bool result = parser.ProcessIncomingChunk(datagram.AsSpan(0, totalDatagramSize));

            Assert.True(result);
            var snapshot = tracker.GetSnapshot();
            Assert.Equal(1, snapshot.PacketsReceivedTotal);
            Assert.True(snapshot.LastDispatchLatencyMicroseconds >= 0);
        }
    }
}
