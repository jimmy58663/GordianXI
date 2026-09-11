// tests/Gordian.Core.Tests/Network/HandshakePacketsTests.cs
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Gordian.Core.Config;
using Gordian.Core.Network;
using Gordian.Core.Network.Compression;
using Gordian.Core.Network.Crypto;
using Xunit;

namespace Gordian.Core.Tests.Network
{
    public class HandshakePacketsTests
    {
        [Fact]
        public void BuildLoginSubPacket_MatchesLandSandBoatLayoutAndChecksum()
        {
            const uint charId = 12345;
            const string charName = "TestPlayer";
            const string accName = "test_account";
            byte[] ticket = new byte[16] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16 };

            byte[] subPacket = HandshakePackets.BuildLoginSubPacket(charId, charName, accName, ticket, clientVersion: 2026);

            // 1. Total sub-packet size
            Assert.Equal(HandshakePackets.LoginSubPacketSize, subPacket.Length);
            Assert.Equal(92, subPacket.Length);

            // 2. Header: ID 0x00A, Size 23 (23 * 4 = 92 bytes)
            ushort headerWord = BinaryPrimitives.ReadUInt16LittleEndian(subPacket.AsSpan(0, 2));
            ushort packetId = (ushort)(headerWord & 0x1FF);
            int sizeDwords = (headerWord >> 9) & 0x7F;
            Assert.Equal(0x00A, packetId);
            Assert.Equal(23, sizeDwords);

            // 3. UniqueNo / Character ID
            uint uniqueNo = BinaryPrimitives.ReadUInt32LittleEndian(subPacket.AsSpan(12, 4));
            Assert.Equal(charId, uniqueNo);

            // 4. Character Name & Account Name null-terminated ASCII
            string decodedCharName = Encoding.ASCII.GetString(subPacket.AsSpan(34, 15)).TrimEnd('\0');
            string decodedAccName = Encoding.ASCII.GetString(subPacket.AsSpan(49, 15)).TrimEnd('\0');
            Assert.Equal(charName, decodedCharName);
            Assert.Equal(accName, decodedAccName);

            // 5. Version
            uint ver = BinaryPrimitives.ReadUInt32LittleEndian(subPacket.AsSpan(80, 4));
            Assert.Equal(2026u, ver);

            // 6. Platform: PC = 1
            Assert.Equal(0x01, subPacket[84]);

            // 7. Checksum validation matching LandSandBoat map_networking.cpp:
            // checksum = sum of bytes from offset 8 (unknown01) to 91
            byte expectedChecksum = 0;
            for (int i = 8; i < subPacket.Length; i++)
            {
                expectedChecksum += subPacket[i];
            }
            Assert.Equal(expectedChecksum, subPacket[4]);
        }

        [Fact]
        public void BuildLoginDatagram_ConstructsAccurate136ByteEnvelopeWithMd5()
        {
            byte[] datagram = HandshakePackets.BuildLoginDatagram(
                characterId: 9999,
                characterName: "GordianChar",
                accountName: "gordian_acc",
                clientPacketSeq: 5
            );

            Assert.Equal(HandshakePackets.LoginDatagramTotalSize, datagram.Length);
            Assert.Equal(136, datagram.Length);

            // FFXI Header (28 bytes)
            ushort serverPacketId = BinaryPrimitives.ReadUInt16LittleEndian(datagram.AsSpan(0, 2));
            ushort clientPacketId = BinaryPrimitives.ReadUInt16LittleEndian(datagram.AsSpan(2, 2));
            Assert.Equal(0, serverPacketId);
            Assert.Equal(5, clientPacketId);

            // Sub-packet offset 28..120
            ReadOnlySpan<byte> subPacketSpan = datagram.AsSpan(28, 92);
            ushort packetId = (ushort)(BinaryPrimitives.ReadUInt16LittleEndian(subPacketSpan.Slice(0, 2)) & 0x1FF);
            Assert.Equal(0x00A, packetId);

            // MD5 Checksum offset 120..136
            ReadOnlySpan<byte> datagramHash = datagram.AsSpan(120, 16);
            Span<byte> computedHash = stackalloc byte[16];
            MD5.HashData(subPacketSpan, computedHash);
            Assert.True(computedHash.SequenceEqual(datagramHash));
        }

        [Fact]
        public void BuildGameOkSubPacket_HasCorrectHeaderAndSize()
        {
            byte[] packet = HandshakePackets.BuildGameOkSubPacket(sequenceId: 42);
            Assert.Equal(HandshakePackets.GameOkSubPacketSize, packet.Length);
            Assert.Equal(12, packet.Length);

            ushort headerWord = BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(0, 2));
            ushort packetId = (ushort)(headerWord & 0x1FF);
            int sizeDwords = (headerWord >> 9) & 0x7F;
            ushort seq = BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(2, 2));

            Assert.Equal(0x00C, packetId);
            Assert.Equal(3, sizeDwords);
            Assert.Equal(42, seq);
        }

        [Fact]
        public void BuildNetEndSubPacket_HasCorrectHeaderAndSize()
        {
            byte[] packet = HandshakePackets.BuildNetEndSubPacket(sequenceId: 100);
            Assert.Equal(HandshakePackets.NetEndSubPacketSize, packet.Length);
            Assert.Equal(8, packet.Length);

            ushort headerWord = BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(0, 2));
            ushort packetId = (ushort)(headerWord & 0x1FF);
            int sizeDwords = (headerWord >> 9) & 0x7F;
            ushort seq = BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(2, 2));

            Assert.Equal(0x00D, packetId);
            Assert.Equal(2, sizeDwords);
            Assert.Equal(100, seq);
        }

        [Fact]
        public void BuildPosPingPongSubPacket_HasCorrectHeaderAndSize()
        {
            byte[] packet = HandshakePackets.BuildPosPingPongSubPacket(sequenceId: 7, x: 10.5f, y: -2.0f, z: 100.25f, dir: 128);
            Assert.Equal(HandshakePackets.PosSubPacketSize, packet.Length);
            Assert.Equal(32, packet.Length);

            ushort headerWord = BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(0, 2));
            ushort packetId = (ushort)(headerWord & 0x1FF);
            int sizeDwords = (headerWord >> 9) & 0x7F;
            ushort seq = BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(2, 2));

            Assert.Equal(0x015, packetId);
            Assert.Equal(8, sizeDwords);
            Assert.Equal(7, seq);

            float x = BinaryPrimitives.ReadSingleLittleEndian(packet.AsSpan(4, 4));
            float z = BinaryPrimitives.ReadSingleLittleEndian(packet.AsSpan(8, 4));
            float y = BinaryPrimitives.ReadSingleLittleEndian(packet.AsSpan(12, 4));
            Assert.Equal(10.5f, x);
            Assert.Equal(100.25f, z);
            Assert.Equal(-2.0f, y);
            Assert.Equal(128, (byte)packet[20]);
        }

        [Fact]
        public void PacketLogEntry_ResolvesNamesAndFormatsHexAsciiAccurately()
        {
            Assert.Equal("GP_SERV_LOGIN", PacketLogEntry.ResolvePacketName(0x00A, PacketDirection.Inbound));
            Assert.Equal("GP_CLI_LOGIN", PacketLogEntry.ResolvePacketName(0x00A, PacketDirection.Outbound));
            Assert.Equal("GP_CLI_GAMEOK", PacketLogEntry.ResolvePacketName(0x00C, PacketDirection.Outbound));
            Assert.Equal("GP_SERV_ENTERZONE", PacketLogEntry.ResolvePacketName(0x008, PacketDirection.Inbound));
            Assert.Equal("GP_CLI_NETEND", PacketLogEntry.ResolvePacketName(0x00D, PacketDirection.Outbound));
            Assert.Equal("GP_SERV_PING", PacketLogEntry.ResolvePacketName(0x015, PacketDirection.Inbound));

            byte[] sample = Encoding.ASCII.GetBytes("Hello FFXI!");
            string dump = PacketLogEntry.FormatHexAscii(sample);

            Assert.Contains("Hello FFXI!", dump);
            Assert.Contains("48 65 6C 6C 6F", dump); // "Hello" in hex
        }

        [Fact]
        public void PacketParser_HandshakeFlow_TriggersGameOkThenNetEnd()
        {
            var profile = new SessionProfile();
            var sentPackets = new List<byte[]>();
            var inspectedPackets = new List<PacketLogEntry>();
            bool handshakeCompletedFired = false;

            using var suite = new LegacyBlowfishCryptoSuite();
            suite.InitializeKey(Encoding.ASCII.GetBytes("TestSessionKey16"));

            var parser = new PacketParser(
                profile,
                (mem, isHighPriority) =>
                {
                    sentPackets.Add(mem.ToArray());
                    return Task.CompletedTask;
                },
                suite
            );

            parser.PacketInspected += (s, entry) => inspectedPackets.Add(entry);
            parser.HandshakeCompleted += () => handshakeCompletedFired = true;

            // 1. Simulate server sending GP_SERV_LOGIN (0x00A)
            byte[] srvLoginSubPacket = new byte[8];
            // Header: 0x00A, size = 2 (2 * 4 = 8 bytes)
            BinaryPrimitives.WriteUInt16LittleEndian(srvLoginSubPacket.AsSpan(0, 2), (ushort)(0x00A | (2 << 9)));
            BinaryPrimitives.WriteUInt16LittleEndian(srvLoginSubPacket.AsSpan(2, 2), 1);

            byte[] compressed = new byte[64];
            int compBytes = FfxiCodec.Default.Compress(srvLoginSubPacket, compressed);
            byte[] datagram = new byte[28 + compBytes + 32];
            compressed.AsSpan(0, compBytes).CopyTo(datagram.AsSpan(28));
            int totalLen = suite.EncryptAndSign(datagram, 28, compBytes);

            parser.ProcessIncomingChunk(datagram.AsSpan(0, totalLen));

            // Verify client replied with GP_CLI_GAMEOK (0x00C)
            Assert.Single(sentPackets);
            byte[] firstReply = sentPackets[0];
            ushort replyId = (ushort)(BinaryPrimitives.ReadUInt16LittleEndian(firstReply.AsSpan(0, 2)) & 0x1FF);
            Assert.Equal(0x00C, replyId);
            Assert.False(handshakeCompletedFired);

            // 2. Simulate server sending GP_SERV_ENTERZONE (0x008)
            sentPackets.Clear();
            byte[] srvEnterZoneSubPacket = new byte[8];
            BinaryPrimitives.WriteUInt16LittleEndian(srvEnterZoneSubPacket.AsSpan(0, 2), (ushort)(0x008 | (2 << 9)));
            BinaryPrimitives.WriteUInt16LittleEndian(srvEnterZoneSubPacket.AsSpan(2, 2), 2);

            compBytes = FfxiCodec.Default.Compress(srvEnterZoneSubPacket, compressed);
            datagram = new byte[28 + compBytes + 32];
            compressed.AsSpan(0, compBytes).CopyTo(datagram.AsSpan(28));
            totalLen = suite.EncryptAndSign(datagram, 28, compBytes);

            parser.ProcessIncomingChunk(datagram.AsSpan(0, totalLen));

            // Verify client replied with GP_CLI_NETEND (0x00D) and HandshakeCompleted fired!
            Assert.Single(sentPackets);
            byte[] secondReply = sentPackets[0];
            ushort secondReplyId = (ushort)(BinaryPrimitives.ReadUInt16LittleEndian(secondReply.AsSpan(0, 2)) & 0x1FF);
            Assert.Equal(0x00D, secondReplyId);
            Assert.True(handshakeCompletedFired);

            // Verify PacketInspected captured both inbound and outbound sub-packets
            Assert.True(inspectedPackets.Count >= 4);
        }

        [Fact]
        public void LegacyBlowfish_20ByteRawKey_DerivesAndDecryptsCorrectly()
        {
            using var suite = new LegacyBlowfishCryptoSuite();
            // 20-byte key as passed from LSB accounts_sessions blob / xiloader handoff
            byte[] raw20ByteKey = new byte[20]
            {
                0x11, 0x22, 0x33, 0x44, 0x55, 0x66, 0x77, 0x88,
                0x99, 0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0xFF, 0x00,
                0x12, 0x34, 0x56, 0x78
            };

            suite.InitializeKey(raw20ByteKey);
            Assert.True(suite.IsKeyInitialized);

            byte[] payload = Encoding.ASCII.GetBytes("20ByteKeyPreHashValidationVectorTest");
            byte[] datagram = new byte[28 + payload.Length + 32];
            payload.CopyTo(datagram, 28);

            int signedLen = suite.EncryptAndSign(datagram, 28, payload.Length);
            bool success = suite.TryDecryptAndVerify(datagram.AsSpan(0, signedLen), 28, out int decryptedLen);

            Assert.True(success);
            Assert.Equal(payload.Length, decryptedLen);
            Assert.Equal(payload, datagram.AsSpan(28, decryptedLen).ToArray());
        }
    }
}
