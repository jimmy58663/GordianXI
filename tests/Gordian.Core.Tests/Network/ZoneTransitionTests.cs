// tests/Gordian.Core.Tests/Network/ZoneTransitionTests.cs
using System;
using System.Buffers.Binary;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Gordian.Core.Config;
using Gordian.Core.Network;
using Gordian.Core.Network.Crypto;
using Gordian.Core.Network.Packets;
using Xunit;

namespace Gordian.Core.Tests.Network
{
    public class ZoneTransitionTests
    {
        #region Crypto Tests

        [Fact]
        public void LegacyBlowfishCryptoSuite_AdvanceZoneKey_IncrementsByte4AndReinitializes()
        {
            using var suite = new LegacyBlowfishCryptoSuite();
            byte[] initialKey = new byte[20]
            {
                0x01, 0x02, 0x03, 0x04, 0x10, 0x06, 0x07, 0x08,
                0x09, 0x0A, 0x0B, 0x0C, 0x0D, 0x0E, 0x0F, 0x10,
                0x11, 0x12, 0x13, 0x14
            };

            suite.InitializeKey(initialKey);
            Assert.True(suite.IsKeyInitialized);
            Assert.Equal(0x10, suite.CurrentRawKey[4]);

            // Advance zone key
            bool advanced = suite.AdvanceZoneKey();
            Assert.True(advanced);
            Assert.Equal(0x12, suite.CurrentRawKey[4]); // 0x10 + 2 = 0x12

            // Encrypt and decrypt a test datagram with the new advanced key
            byte[] datagram = new byte[28 + 16 + 16]; // 28 header + 16 payload + 16 md5
            Encoding.ASCII.GetBytes("TestPayload1234!").CopyTo(datagram, 28);
            int encLen = suite.EncryptAndSign(datagram, 28, 16);
            Assert.Equal(28 + 16 + 16, encLen);

            bool success = suite.TryDecryptAndVerify(datagram, 28, out int decLen);
            Assert.True(success);
            Assert.Equal(16, decLen);
            Assert.Equal("TestPayload1234!", Encoding.ASCII.GetString(datagram, 28, 16));
        }

        [Fact]
        public void LegacyBlowfishCryptoSuite_FallbackDecryption_SucceedsWithPreviousKey()
        {
            using var suiteOld = new LegacyBlowfishCryptoSuite();
            using var suiteNew = new LegacyBlowfishCryptoSuite();

            byte[] keyOld = new byte[20]
            {
                0xAA, 0xBB, 0xCC, 0xDD, 0x04, 0x11, 0x22, 0x33,
                0x44, 0x55, 0x66, 0x77, 0x88, 0x99, 0x00, 0x11,
                0x22, 0x33, 0x44, 0x55
            };

            suiteOld.InitializeKey(keyOld);
            suiteNew.InitializeKey(keyOld);

            // suiteNew transitions zones, advancing its key
            suiteNew.AdvanceZoneKey();
            Assert.Equal(0x06, suiteNew.CurrentRawKey[4]); // 0x04 + 2 = 0x06

            // Datagram is encrypted by old zone map server using the old key
            byte[] datagram = new byte[28 + 24 + 16];
            Encoding.ASCII.GetBytes("OldZonePacketPayload!123").CopyTo(datagram, 28);
            suiteOld.EncryptAndSign(datagram, 28, 24);

            // suiteNew receives the in-flight datagram from the old zone; fallback should decipher it
            bool decrypted = suiteNew.TryDecryptAndVerify(datagram, 28, out int decLen);
            Assert.True(decrypted);
            Assert.Equal(24, decLen);
            Assert.Equal("OldZonePacketPayload!123", Encoding.ASCII.GetString(datagram, 28, 24));
        }

        #endregion

        #region Inbound Decoder Tests

        [Fact]
        public void S2C_0x05B_WPos_DecodesPositionAndModeCorrectly()
        {
            byte[] payload = new byte[24];
            BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(0, 4), 123.45f); // X
            BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(4, 4), -50.25f); // Y
            BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(8, 4), 987.65f); // Z
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(12, 4), 0x12345678); // UniqueNo
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(16, 2), 0x00A1); // ActorIndex
            payload[18] = (byte)PosMode.Reset;
            payload[19] = 192; // Dir

            var wpos = new S2C_0x05B_WPos(payload);
            Assert.True(wpos.IsValid);
            Assert.Equal(123.45f, wpos.X, 2);
            Assert.Equal(987.65f, wpos.Y, 2);
            Assert.Equal(-50.25f, wpos.Z, 2);
            Assert.Equal(0x12345678u, wpos.UniqueNo);
            Assert.Equal(0x00A1, wpos.ActorIndex);
            Assert.Equal(PosMode.Reset, wpos.Mode);
            Assert.Equal(192, wpos.Direction);
        }

        [Fact]
        public void S2C_0x065_WPos2_DecodesPositionAndModeCorrectly()
        {
            byte[] payload = new byte[24];
            BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(0, 4), 10.0f);
            BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(4, 4), 0.0f);
            BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(8, 4), -20.0f);
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(12, 4), 0x99887766);
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(16, 2), 42);
            payload[18] = (byte)PosMode.Normal;
            payload[19] = 64;

            var wpos = new S2C_0x065_WPos2(payload);
            Assert.True(wpos.IsValid);
            Assert.Equal(10.0f, wpos.X, 2);
            Assert.Equal(-20.0f, wpos.Y, 2);
            Assert.Equal(0.0f, wpos.Z, 2);
            Assert.Equal(0x99887766u, wpos.UniqueNo);
            Assert.Equal(42, wpos.ActorIndex);
            Assert.Equal(PosMode.Normal, wpos.Mode);
            Assert.Equal(64, wpos.Direction);
        }

        #endregion

        #region Outbound Builder Tests

        [Fact]
        public void BuildMapRect_WithFourCc_ConstructsCorrectPacket()
        {
            byte[] packet = LifecycleOutboundPackets.BuildMapRect(
                "zmrq",
                100.5f,
                -2.0f,
                300.25f,
                actorIndex: 1,
                myRoomExitBit: MogHouseExitBit.SandOria,
                myRoomExitMode: MogHouseExitMode.Option1,
                sequenceId: 10
            );

            Assert.Equal(24, packet.Length);
            ushort headerWord = BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(0, 2));
            Assert.Equal(0x05E, headerWord & 0x1FF);
            Assert.Equal(6, headerWord >> 9);
            Assert.Equal(10, BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(2, 2)));

            // RectID as string
            string rectTag = Encoding.ASCII.GetString(packet, 4, 4);
            Assert.Equal("zmrq", rectTag);

            Assert.Equal(100.5f, BinaryPrimitives.ReadSingleLittleEndian(packet.AsSpan(8, 4)));
            Assert.Equal(-2.0f, BinaryPrimitives.ReadSingleLittleEndian(packet.AsSpan(12, 4)));
            Assert.Equal(300.25f, BinaryPrimitives.ReadSingleLittleEndian(packet.AsSpan(16, 4)));
            Assert.Equal(1, BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(20, 2)));
            Assert.Equal((byte)MogHouseExitBit.SandOria, packet[22]);
            Assert.Equal((byte)MogHouseExitMode.Option1, packet[23]);
        }

        [Fact]
        public void BuildZoneTransition_0x011_ConstructsCorrectPacket()
        {
            byte[] packet = LifecycleOutboundPackets.BuildZoneTransition(unknown00: 2, unknown01: 0, sequenceId: 5);

            Assert.Equal(8, packet.Length);
            ushort headerWord = BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(0, 2));
            Assert.Equal(0x011, headerWord & 0x1FF);
            Assert.Equal(2, headerWord >> 9); // 2 words = 8 bytes
            Assert.Equal(5, BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(2, 2)));
            Assert.Equal(2, packet[4]);
            Assert.Equal(0, packet[5]);
            Assert.Equal(0, packet[6]);
            Assert.Equal(0, packet[7]);
        }

        [Fact]
        public void BuildEventEndXzy_0x05C_ConstructsCorrectPacket()
        {
            byte[] packet = LifecycleOutboundPackets.BuildEventEndXzy(
                x: 15.5f,
                y: 2.0f,
                z: -45.0f,
                uniqueNo: 1001,
                endPara: 2,
                actIndex: 12,
                mode: 1,
                dir: -30,
                eventNum: 55,
                eventPara: 3,
                sequenceId: 7
            );

            Assert.Equal(32, packet.Length);
            ushort headerWord = BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(0, 2));
            Assert.Equal(0x05C, headerWord & 0x1FF);
            Assert.Equal(8, headerWord >> 9); // 8 words = 32 bytes
            Assert.Equal(7, BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(2, 2)));
            Assert.Equal(15.5f, BinaryPrimitives.ReadSingleLittleEndian(packet.AsSpan(4, 4)));
            Assert.Equal(2.0f, BinaryPrimitives.ReadSingleLittleEndian(packet.AsSpan(8, 4)));
            Assert.Equal(-45.0f, BinaryPrimitives.ReadSingleLittleEndian(packet.AsSpan(12, 4)));
            Assert.Equal(1001u, BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(16, 4)));
            Assert.Equal(2u, BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(20, 4)));
            Assert.Equal(55, BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(24, 2)));
            Assert.Equal(3, BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(26, 2)));
            Assert.Equal(12, BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(28, 2)));
            Assert.Equal(1, packet[30]);
            Assert.Equal(unchecked((byte)-30), packet[31]);
        }

        [Fact]
        public void BuildReqLogout_0x0E7_ConstructsCorrectPacket()
        {
            byte[] packet = LifecycleOutboundPackets.BuildReqLogout(
                ReqLogoutMode.ShutdownOn,
                ReqLogoutKind.Shutdown,
                sequenceId: 12
            );

            Assert.Equal(8, packet.Length);
            ushort headerWord = BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(0, 2));
            Assert.Equal(0x0E7, headerWord & 0x1FF);
            Assert.Equal(2, headerWord >> 9); // 2 words = 8 bytes
            Assert.Equal(12, BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(2, 2)));
            Assert.Equal((ushort)ReqLogoutMode.ShutdownOn, BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(4, 2)));
            Assert.Equal((ushort)ReqLogoutKind.Shutdown, BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(6, 2)));
        }

        #endregion

        #region Lifecycle & Networking Tests

        [Fact]
        public async Task LifecycleModule_HandleEnterZone_SendsNetEndAndZoneTransition0x011()
        {
            var profile = new SessionProfile();
            int sentPackets = 0;
            ushort lastSentId = 0;

            var module = new LifecyclePacketModule(
                profile,
                sendChunkCallback: (data, highPri) =>
                {
                    sentPackets++;
                    ushort rawWord = BinaryPrimitives.ReadUInt16LittleEndian(data.Span.Slice(0, 2));
                    lastSentId = (ushort)(rawWord & 0x1FF);
                    return Task.CompletedTask;
                }
            );

            var dispatcher = new PacketDispatcher();
            module.Register(dispatcher);

            bool completedFired = false;
            module.HandshakeCompleted += () => completedFired = true;

            // Dispatch S2C 0x008 EnterZone
            byte[] enterZonePayload = new byte[48];
            var header = new PacketHeader(0x008, 52, 1);
            dispatcher.Dispatch(header, enterZonePayload);

            Assert.True(completedFired);
            Assert.Equal(3, sentPackets); // First 0x00D (NetEnd), then 0x011 (ZoneTransition), then 0x061 (CliStatus)
            Assert.Equal(0x061, lastSentId);
        }

        [Fact]
        public void LifecycleModule_WPosPackets_UpdatePlayerPosition()
        {
            var profile = new SessionProfile();
            var module = new LifecyclePacketModule(profile, (d, h) => Task.CompletedTask);
            var dispatcher = new PacketDispatcher();
            module.Register(dispatcher);

            float lastX = 0, lastY = 0, lastZ = 0;
            byte lastDir = 0;
            ushort lastAct = 0;
            module.PlayerPositionUpdated += (x, y, z, dir, act) =>
            {
                lastX = x;
                lastY = y;
                lastZ = z;
                lastDir = dir;
                lastAct = act;
            };

            // Test 0x05B WPos
            byte[] payload05B = new byte[24];
            BinaryPrimitives.WriteSingleLittleEndian(payload05B.AsSpan(0, 4), 50.0f);
            BinaryPrimitives.WriteSingleLittleEndian(payload05B.AsSpan(4, 4), 1.5f);
            BinaryPrimitives.WriteSingleLittleEndian(payload05B.AsSpan(8, 4), -100.0f);
            BinaryPrimitives.WriteUInt16LittleEndian(payload05B.AsSpan(16, 2), 10);
            payload05B[19] = 128;

            dispatcher.Dispatch(new PacketHeader(0x05B, 28, 1), payload05B);
            Assert.Equal(50.0f, lastX);
            Assert.Equal(-100.0f, lastY);
            Assert.Equal(1.5f, lastZ);
            Assert.Equal(10, lastAct);
            Assert.Equal(128, lastDir);

            // Test 0x065 WPos2
            byte[] payload065 = new byte[24];
            BinaryPrimitives.WriteSingleLittleEndian(payload065.AsSpan(0, 4), -25.0f);
            BinaryPrimitives.WriteSingleLittleEndian(payload065.AsSpan(4, 4), 0.0f);
            BinaryPrimitives.WriteSingleLittleEndian(payload065.AsSpan(8, 4), 200.0f);
            BinaryPrimitives.WriteUInt16LittleEndian(payload065.AsSpan(16, 2), 20);
            payload065[19] = 64;

            dispatcher.Dispatch(new PacketHeader(0x065, 28, 2), payload065);
            Assert.Equal(-25.0f, lastX);
            Assert.Equal(200.0f, lastY);
            Assert.Equal(0.0f, lastZ);
            Assert.Equal(20, lastAct);
            Assert.Equal(64, lastDir);
        }

        [Fact]
        public async Task SessionNetworkManager_PerformZoneTransitionAsync_ReTargetsEndpointAndAdvancesKey()
        {
            var crypto = new LegacyBlowfishCryptoSuite();
            byte[] rawKey = new byte[20]
            {
                1, 2, 3, 4, 0x20, 6, 7, 8,
                9, 10, 11, 12, 13, 14, 15, 16,
                17, 18, 19, 20
            };
            crypto.InitializeKey(rawKey);

            using var netManager = new SessionNetworkManager("127.0.0.1", 54230, crypto)
            {
                CharacterId = 12345,
                CharacterName = "TestHero",
                AccountName = "Tester"
            };

            bool transitionStarted = false;
            IPAddress? transitionedIp = null;
            int transitionedPort = 0;

            netManager.ZoneTransitionStarted += (ip, port) =>
            {
                transitionStarted = true;
                transitionedIp = ip;
                transitionedPort = port;
            };

            // Execute dynamic zone transition to new map server
            var targetIp = IPAddress.Parse("127.0.0.2");
            int targetPort = 54235;

            await netManager.PerformZoneTransitionAsync(targetIp, targetPort);

            Assert.True(transitionStarted);
            Assert.Equal(targetIp, transitionedIp);
            Assert.Equal(targetPort, transitionedPort);
            Assert.Equal("127.0.0.2", netManager.ServerAddress);
            Assert.Equal(54235, netManager.ServerPort);
            Assert.Equal(SessionState.LoadingWorldData, netManager.CurrentState);

            // Blowfish key byte 4 should have advanced by 2: 0x20 + 2 = 0x22
            Assert.Equal(0x22, crypto.CurrentRawKey[4]);
        }

        #endregion
    }
}
