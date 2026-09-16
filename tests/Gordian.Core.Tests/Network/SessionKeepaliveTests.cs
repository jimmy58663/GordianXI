// tests/Gordian.Core.Tests/Network/SessionKeepaliveTests.cs
using System;
using System.Buffers.Binary;
using System.Threading.Tasks;
using Gordian.Core.Config;
using Gordian.Core.Network;
using Gordian.Core.Network.Compression;
using Gordian.Core.Network.Crypto;
using Xunit;

namespace Gordian.Core.Tests.Network
{
    public sealed class SessionKeepaliveTests
    {
        [Fact]
        public void PacketParser_ExtractsPlayerPositionFromLogin0x00A()
        {
            var profile = new SessionProfile();
            byte[]? sentChunk = null;
            var parser = new PacketParser(
                profile,
                (chunk, highPriority) =>
                {
                    sentChunk = chunk.ToArray();
                    return Task.CompletedTask;
                }
            );

            float capturedX = 0f;
            float capturedY = 0f;
            float capturedZ = 0f;
            byte capturedDir = 0;
            ushort capturedActIndex = 0;
            bool eventFired = false;

            parser.PlayerPositionUpdated += (x, y, z, dir, actIndex) =>
            {
                capturedX = x;
                capturedY = y;
                capturedZ = z;
                capturedDir = dir;
                capturedActIndex = actIndex;
                eventFired = true;
            };

            // Build simulated 0x00A uncompressed subpacket
            // Header: ID = 0x00A, Size = 13 (52 bytes)
            byte[] subPacket = new byte[52];
            ushort headerWord = (ushort)(0x00A | (13 << 9));
            BinaryPrimitives.WriteUInt16LittleEndian(subPacket.AsSpan(0, 2), headerWord);
            BinaryPrimitives.WriteUInt16LittleEndian(subPacket.AsSpan(2, 2), 0); // Seq 0

            // PosHead in payload (starts at subPacket[4]):
            // UniqueNo at 0 (subPacket[4]) = 12345
            BinaryPrimitives.WriteUInt32LittleEndian(subPacket.AsSpan(4, 4), 12345);
            // ActIndex at 4 (subPacket[8]) = 42
            BinaryPrimitives.WriteUInt16LittleEndian(subPacket.AsSpan(8, 2), 42);
            // dir at 7 (subPacket[11]) = 192
            subPacket[11] = 192;
            // x at 8 (subPacket[12]) = 100.5f
            BinaryPrimitives.WriteSingleLittleEndian(subPacket.AsSpan(12, 4), 100.5f);
            // z at 12 (subPacket[16]) = -25.25f
            BinaryPrimitives.WriteSingleLittleEndian(subPacket.AsSpan(16, 4), -25.25f);
            // y at 16 (subPacket[20]) = 300.75f
            BinaryPrimitives.WriteSingleLittleEndian(subPacket.AsSpan(20, 4), 300.75f);

            // Compress payload
            byte[] compressed = new byte[256];
            int compressedBytes = FfxiCodec.Default.Compress(subPacket, compressed);

            // Build raw datagram envelope (unencrypted, 28-byte header + compressed + 16-byte MD5)
            byte[] datagram = new byte[28 + compressedBytes + 16];
            BinaryPrimitives.WriteUInt16LittleEndian(datagram.AsSpan(0, 2), 1); // ServerSeq = 1
            BinaryPrimitives.WriteUInt16LittleEndian(datagram.AsSpan(2, 2), 1); // ClientSeqAck = 1
            compressed.AsSpan(0, compressedBytes).CopyTo(datagram.AsSpan(28));

            bool success = parser.ProcessIncomingChunk(datagram);

            Assert.True(success);
            Assert.True(eventFired);
            Assert.Equal(100.5f, capturedX);
            Assert.Equal(-25.25f, capturedY);
            Assert.Equal(300.75f, capturedZ);
            Assert.Equal(192, capturedDir);
            Assert.Equal(42, capturedActIndex);

            // Should also have queued GP_CLI_GAMEOK (0x00C)
            Assert.NotNull(sentChunk);
            ushort sentId = (ushort)(BinaryPrimitives.ReadUInt16LittleEndian(sentChunk.AsSpan(0, 2)) & 0x1FF);
            Assert.Equal(0x00C, sentId);
        }

        [Fact]
        public void SessionNetworkManager_InitialPositionCachesCorrectly()
        {
            using var mgr = new SessionNetworkManager("127.0.0.1", 54230);
            Assert.Equal(0f, mgr.PositionX);
            Assert.Equal(0f, mgr.PositionY);
            Assert.Equal(0f, mgr.PositionZ);
            Assert.Equal(0, mgr.Direction);
            Assert.Equal(0, mgr.TargetIndex);

            // Simulate 0x00A incoming packet to manager's parser
            byte[] subPacket = new byte[52];
            ushort headerWord = (ushort)(0x00A | (13 << 9));
            BinaryPrimitives.WriteUInt16LittleEndian(subPacket.AsSpan(0, 2), headerWord);
            BinaryPrimitives.WriteUInt16LittleEndian(subPacket.AsSpan(8, 2), 101); // ActIndex
            subPacket[11] = 64; // Dir
            BinaryPrimitives.WriteSingleLittleEndian(subPacket.AsSpan(12, 4), 12.34f); // X
            BinaryPrimitives.WriteSingleLittleEndian(subPacket.AsSpan(16, 4), 56.78f); // Z
            BinaryPrimitives.WriteSingleLittleEndian(subPacket.AsSpan(20, 4), 90.12f); // Y

            byte[] compressed = new byte[256];
            int compressedBytes = FfxiCodec.Default.Compress(subPacket, compressed);

            byte[] datagram = new byte[28 + compressedBytes + 16];
            BinaryPrimitives.WriteUInt16LittleEndian(datagram.AsSpan(0, 2), 1);
            BinaryPrimitives.WriteUInt16LittleEndian(datagram.AsSpan(2, 2), 1);
            compressed.AsSpan(0, compressedBytes).CopyTo(datagram.AsSpan(28));

            mgr.Parser.ProcessIncomingChunk(datagram);

            Assert.Equal(12.34f, mgr.PositionX);
            Assert.Equal(56.78f, mgr.PositionY);
            Assert.Equal(90.12f, mgr.PositionZ);
            Assert.Equal(64, mgr.Direction);
            Assert.Equal(101, mgr.TargetIndex);
        }

        [Fact]
        public async Task SessionNetworkManager_OutboundFlush_FiresPacketInspectedWithClientSeq()
        {
            var inspected = new System.Collections.Generic.List<PacketLogEntry>();
            using var mgr = new SessionNetworkManager("127.0.0.1", 59999);
            mgr.PacketInspected += (s, e) =>
            {
                if (e.Direction == PacketDirection.Outbound)
                {
                    lock (inspected)
                    {
                        inspected.Add(e);
                    }
                }
            };

            await mgr.ConnectAsync();
            try
            {
                mgr.CurrentState = SessionState.ActiveInWorld;

                byte[] gameOkChunk = HandshakePackets.BuildGameOkSubPacket(sequenceId: 0);
                await mgr.QueueChunkAsync(gameOkChunk, isHighPriority: true);

                lock (inspected)
                {
                    Assert.NotEmpty(inspected);
                    var entry = inspected.Find(p => p.PacketId == 0x00C);
                    Assert.NotNull(entry);
                    Assert.Equal(PacketDirection.Outbound, entry.Direction);
                    Assert.Equal("GP_CLI_GAMEOK", entry.PacketName);
                    Assert.True(entry.SequenceId >= 1);
                }
            }
            finally
            {
                mgr.Disconnect();
            }
        }
    }
}
