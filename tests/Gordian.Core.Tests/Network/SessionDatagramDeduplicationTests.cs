// tests/Gordian.Core.Tests/Network/SessionDatagramDeduplicationTests.cs
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Gordian.Core.Network;
using Gordian.Core.Network.Compression;
using Gordian.Core.Network.Crypto;
using Gordian.Core.Network.Packets;
using Xunit;

namespace Gordian.Core.Tests.Network
{
    public sealed class SessionDatagramDeduplicationTests
    {
        private static byte[] BuildTestDatagram(ushort serverSeq, ushort clientSeqAck, ReadOnlySpan<byte> uncompressedSubPacket)
        {
            byte[] compressed = new byte[512];
            int compBytes = FfxiCodec.Default.Compress(uncompressedSubPacket, compressed);

            byte[] datagram = new byte[28 + compBytes + 16];
            BinaryPrimitives.WriteUInt16LittleEndian(datagram.AsSpan(0, 2), serverSeq);
            BinaryPrimitives.WriteUInt16LittleEndian(datagram.AsSpan(2, 2), clientSeqAck);
            compressed.AsSpan(0, compBytes).CopyTo(datagram.AsSpan(28));
            return datagram;
        }

        private static byte[] BuildChatSubPacket(ChatMessageType kind, string sender, string message, ushort sequenceId = 1)
        {
            byte[] payload = new byte[19 + Encoding.ASCII.GetByteCount(message) + 1];
            payload[0] = (byte)kind;
            payload[1] = 0; // Attr
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(2, 2), 100); // ZoneId

            byte[] senderBytes = Encoding.ASCII.GetBytes(sender);
            senderBytes.AsSpan(0, Math.Min(senderBytes.Length, 15)).CopyTo(payload.AsSpan(4));

            byte[] msgBytes = Encoding.ASCII.GetBytes(message);
            msgBytes.CopyTo(payload.AsSpan(19));
            payload[^1] = 0; // Null terminator

            int totalSize = 4 + payload.Length;
            totalSize = (totalSize + 3) & ~3; // 4-byte align

            byte[] subPacket = new byte[totalSize];
            ushort words = (ushort)(totalSize / 4);
            ushort headerWord = (ushort)(0x017 | (words << 9));
            BinaryPrimitives.WriteUInt16LittleEndian(subPacket.AsSpan(0, 2), headerWord);
            BinaryPrimitives.WriteUInt16LittleEndian(subPacket.AsSpan(2, 2), sequenceId);
            payload.CopyTo(subPacket.AsSpan(4));

            return subPacket;
        }

        [Fact]
        public void ProcessInboundDatagram_SequentialDatagrams_AllParsedSuccessfully()
        {
            using var mgr = new SessionNetworkManager("127.0.0.1", 54230);
            byte[] chat = BuildChatSubPacket(ChatMessageType.Say, "Cybin", "Hello 1");

            byte[] d1 = BuildTestDatagram(serverSeq: 1, clientSeqAck: 1, chat);
            byte[] d2 = BuildTestDatagram(serverSeq: 2, clientSeqAck: 1, chat);
            byte[] d3 = BuildTestDatagram(serverSeq: 3, clientSeqAck: 1, chat);

            Assert.True(mgr.ProcessInboundDatagram(d1));
            Assert.True(mgr.ProcessInboundDatagram(d2));
            Assert.True(mgr.ProcessInboundDatagram(d3));

            Assert.Equal(0, mgr.Performance.DuplicateDatagramsDropped);
            Assert.Equal(3, mgr.ServerPacketIdSequence);
        }

        [Fact]
        public void ProcessInboundDatagram_ExactRetransmission_DropsDuplicateAndIncrementsMetric()
        {
            using var mgr = new SessionNetworkManager("127.0.0.1", 54230);
            byte[] chat = BuildChatSubPacket(ChatMessageType.Tell, "Cybin", "Hello");
            byte[] d1 = BuildTestDatagram(serverSeq: 10, clientSeqAck: 5, chat);

            // First arrival
            bool firstParsed = mgr.ProcessInboundDatagram(d1);
            Assert.True(firstParsed);
            Assert.Equal(0, mgr.Performance.DuplicateDatagramsDropped);

            // Exact retransmission from server
            bool secondParsed = mgr.ProcessInboundDatagram(d1);
            Assert.False(secondParsed);
            Assert.Equal(1, mgr.Performance.DuplicateDatagramsDropped);
            Assert.Equal(10, mgr.ServerPacketIdSequence); // ACK state maintained!
        }

        [Fact]
        public void ProcessInboundDatagram_FourRetransmissions_DispatchesChatEventExactlyOnce()
        {
            using var mgr = new SessionNetworkManager("127.0.0.1", 54230);
            int chatEventsFired = 0;
            mgr.ChatModule.ChatMessageReceived += msg =>
            {
                if (msg.Message == "Hello" && msg.Sender == "Cybin")
                {
                    chatEventsFired++;
                }
            };

            byte[] chat = BuildChatSubPacket(ChatMessageType.Tell, "Cybin", "Hello");
            byte[] tellDatagram = BuildTestDatagram(serverSeq: 42, clientSeqAck: 100, chat);

            // Simulate the exact 4x retransmission bug:
            // Datagram arrived 4 times within the same second
            bool r1 = mgr.ProcessInboundDatagram(tellDatagram);
            bool r2 = mgr.ProcessInboundDatagram(tellDatagram);
            bool r3 = mgr.ProcessInboundDatagram(tellDatagram);
            bool r4 = mgr.ProcessInboundDatagram(tellDatagram);

            Assert.True(r1);
            Assert.False(r2);
            Assert.False(r3);
            Assert.False(r4);

            // CRITICAL: Chat event MUST only fire once, eliminating the 4x duplicate replies!
            Assert.Equal(1, chatEventsFired);
            Assert.Equal(3, mgr.Performance.DuplicateDatagramsDropped);
            Assert.Equal(42, mgr.ServerPacketIdSequence);
        }

        [Fact]
        public void ProcessInboundDatagram_OutOfOrderArrival_ProcessesWithinWindow()
        {
            using var mgr = new SessionNetworkManager("127.0.0.1", 54230);
            byte[] chat = BuildChatSubPacket(ChatMessageType.Say, "Cybin", "Test");

            byte[] d10 = BuildTestDatagram(serverSeq: 10, clientSeqAck: 1, chat);
            byte[] d11 = BuildTestDatagram(serverSeq: 11, clientSeqAck: 1, chat);
            byte[] d12 = BuildTestDatagram(serverSeq: 12, clientSeqAck: 1, chat);

            Assert.True(mgr.ProcessInboundDatagram(d10));
            // Gap: 12 arrives before 11
            Assert.True(mgr.ProcessInboundDatagram(d12));
            Assert.Equal(1, mgr.Performance.GetSnapshot().SequenceDiscrepancies);

            // 11 arrives out of order within 64-packet window
            Assert.True(mgr.ProcessInboundDatagram(d11));

            // If 11 arrives AGAIN, it is dropped as duplicate
            Assert.False(mgr.ProcessInboundDatagram(d11));
            Assert.Equal(1, mgr.Performance.DuplicateDatagramsDropped);
        }

        [Fact]
        public void ProcessInboundDatagram_SequenceRollover_HandlesMaxToZero()
        {
            using var mgr = new SessionNetworkManager("127.0.0.1", 54230);
            byte[] chat = BuildChatSubPacket(ChatMessageType.Say, "Cybin", "Rollover");

            byte[] dMax = BuildTestDatagram(serverSeq: 65535, clientSeqAck: 1, chat);
            byte[] dZero = BuildTestDatagram(serverSeq: 0, clientSeqAck: 1, chat);

            Assert.True(mgr.ProcessInboundDatagram(dMax));
            Assert.True(mgr.ProcessInboundDatagram(dZero));

            // Retransmitted 0 is dropped
            Assert.False(mgr.ProcessInboundDatagram(dZero));
            // Retransmitted 65535 is dropped
            Assert.False(mgr.ProcessInboundDatagram(dMax));

            Assert.Equal(2, mgr.Performance.DuplicateDatagramsDropped);
        }

        [Fact]
        public void ProcessInboundDatagram_DisabledDeduplication_AllowsDuplicates()
        {
            using var mgr = new SessionNetworkManager("127.0.0.1", 54230)
            {
                EnableSequenceDeduplication = false
            };

            byte[] chat = BuildChatSubPacket(ChatMessageType.Say, "Cybin", "Test");
            byte[] d1 = BuildTestDatagram(serverSeq: 5, clientSeqAck: 1, chat);

            Assert.True(mgr.ProcessInboundDatagram(d1));
            Assert.True(mgr.ProcessInboundDatagram(d1)); // Allowed when disabled
            Assert.Equal(0, mgr.Performance.DuplicateDatagramsDropped);
        }

        /// <summary>
        /// Builds a datagram encrypted and signed the way a map server does with the given session key.
        /// </summary>
        private static byte[] BuildEncryptedDatagram(IPacketCryptoSuite serverSuite, ushort serverSeq, ReadOnlySpan<byte> subPacket)
        {
            byte[] compressed = new byte[512];
            int compBytes = FfxiCodec.Default.Compress(subPacket, compressed);
            byte[] buffer = new byte[28 + compBytes + 16 + 16];
            BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(0, 2), serverSeq);
            compressed.AsSpan(0, compBytes).CopyTo(buffer.AsSpan(28));
            int length = serverSuite.EncryptAndSign(buffer, 28, compBytes);
            return buffer.AsSpan(0, length).ToArray();
        }

        [Fact]
        public async Task ZoneTransition_StaleOldZoneDatagramsDoNotPoisonTheNewServerSequence()
        {
            byte[] sessionKey = new byte[20] { 0x10, 0x20, 0x30, 0x40, 0x00, 0x11, 0x22, 0x33, 0x44, 0x55, 0x66, 0x77, 0x88, 0x99, 0xAA, 0xBB, 0x58, 0xE0, 0x5D, 0xAD };
            using var oldServer = new LegacyBlowfishCryptoSuite();
            using var newServer = new LegacyBlowfishCryptoSuite();
            oldServer.InitializeKey(sessionKey);
            newServer.InitializeKey(sessionKey);
            newServer.AdvanceZoneKey();

            using var mgr = new SessionNetworkManager("127.0.0.1", 54230);
            mgr.Parser.CryptoSuite.InitializeKey(sessionKey);
            byte[] chat = BuildChatSubPacket(ChatMessageType.Say, "Cybin", "Zone");

            // Old zone traffic up to server Seq 176, then the zone change.
            Assert.True(mgr.ProcessInboundDatagram(BuildEncryptedDatagram(oldServer, 176, chat)));
            await mgr.PerformZoneTransitionAsync(System.Net.IPAddress.Loopback, 54230);
            Assert.True(mgr.ZoneTransitionPending);

            // A straggler from the old map server (retransmitted, old key, old sequence) must be ignored entirely.
            Assert.False(mgr.ProcessInboundDatagram(BuildEncryptedDatagram(oldServer, 177, chat)));
            Assert.True(mgr.ZoneTransitionPending);

            // The new map server restarts its sequence at 1: accepted, and it becomes the baseline.
            Assert.True(mgr.ProcessInboundDatagram(BuildEncryptedDatagram(newServer, 1, chat)));
            Assert.False(mgr.ZoneTransitionPending);
            Assert.Equal(1, mgr.ServerPacketIdSequence);
            Assert.True(mgr.ProcessInboundDatagram(BuildEncryptedDatagram(newServer, 2, chat)));

            // Normal duplicate suppression resumes on the new server.
            Assert.False(mgr.ProcessInboundDatagram(BuildEncryptedDatagram(newServer, 2, chat)));
        }

        [Fact]
        public void LegacyBlowfishCryptoSuite_ReportsWhenOnlyThePreviousZoneKeyVerifies()
        {
            byte[] sessionKey = new byte[20] { 0x10, 0x20, 0x30, 0x40, 0x00, 0x11, 0x22, 0x33, 0x44, 0x55, 0x66, 0x77, 0x88, 0x99, 0xAA, 0xBB, 0x58, 0xE0, 0x5D, 0xAD };
            using var oldServer = new LegacyBlowfishCryptoSuite();
            using var client = new LegacyBlowfishCryptoSuite();
            oldServer.InitializeKey(sessionKey);
            client.InitializeKey(sessionKey);
            client.AdvanceZoneKey();

            byte[] chat = BuildChatSubPacket(ChatMessageType.Say, "Cybin", "Stale");
            byte[] stale = BuildEncryptedDatagram(oldServer, 5, chat);

            Assert.True(client.TryDecryptAndVerify(stale, 28, out _));
            Assert.True(client.LastDecryptUsedPreviousKey);
        }

        [Fact]
        public async Task PerformZoneTransitionAsync_ResetsSequenceHistory()
        {
            using var mgr = new SessionNetworkManager("127.0.0.1", 54230);
            byte[] chat = BuildChatSubPacket(ChatMessageType.Say, "Cybin", "Zone");
            byte[] d1 = BuildTestDatagram(serverSeq: 1, clientSeqAck: 1, chat);

            Assert.True(mgr.ProcessInboundDatagram(d1));
            Assert.False(mgr.ProcessInboundDatagram(d1)); // duplicate

            // Perform zone transition
            await mgr.PerformZoneTransitionAsync(System.Net.IPAddress.Loopback, 54231);

            // Sequence 1 should now be accepted again for the new zone
            Assert.True(mgr.ProcessInboundDatagram(d1));
        }
    }
}
