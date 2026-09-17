// tests/Gordian.Core.Tests/Network/PacketDispatcherTests.cs
using System;
using System.Buffers.Binary;
using System.Net;
using System.Threading.Tasks;
using Gordian.Core.Config;
using Gordian.Core.Network.Packets;
using Xunit;

namespace Gordian.Core.Tests.Network
{
    public class PacketDispatcherTests
    {
        [Fact]
        public void PacketHeader_TryParse_ValidSubPacket_ExtractsFields()
        {
            byte[] buffer = new byte[8];
            // ID = 0x00B (11), Size = 6 words (24 bytes)
            ushort rawWord = (ushort)(0x00B | (6 << 9));
            BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(0, 2), rawWord);
            BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(2, 2), 0x0042);

            bool success = PacketHeader.TryParse(buffer, out PacketHeader header);

            Assert.True(success);
            Assert.Equal(0x00B, header.PacketId);
            Assert.Equal(24, header.TotalSize);
            Assert.Equal(0x0042, header.SequenceId);
        }

        [Fact]
        public void PacketHeader_TryParse_ShortBuffer_ReturnsFalse()
        {
            byte[] shortBuffer = new byte[3];
            bool success = PacketHeader.TryParse(shortBuffer, out _);
            Assert.False(success);
        }

        [Fact]
        public void Dispatcher_RegisterAndDispatch_InvokesHandler()
        {
            var dispatcher = new PacketDispatcher();
            bool invoked = false;
            PacketHeader receivedHeader = default;

            dispatcher.Register(0x01A, (header, payload) =>
            {
                invoked = true;
                receivedHeader = header;
            });

            Assert.True(dispatcher.HasHandler(0x01A));

            var testHeader = new PacketHeader(0x01A, 12, 1);
            byte[] dummyPayload = new byte[] { 1, 2, 3, 4 };

            bool dispatched = dispatcher.Dispatch(testHeader, dummyPayload);

            Assert.True(dispatched);
            Assert.True(invoked);
            Assert.Equal(0x01A, receivedHeader.PacketId);

            // Unregister and ensure not invoked
            dispatcher.Unregister(0x01A);
            Assert.False(dispatcher.HasHandler(0x01A));

            invoked = false;
            dispatched = dispatcher.Dispatch(testHeader, dummyPayload);
            Assert.False(dispatched);
            Assert.False(invoked);
        }

        [Fact]
        public void Dispatcher_UnhandledPacket_TriggersUnhandledEvent()
        {
            var dispatcher = new PacketDispatcher();
            bool unhandledInvoked = false;
            ushort unhandledId = 0;

            dispatcher.UnhandledPacket += (header, payload) =>
            {
                unhandledInvoked = true;
                unhandledId = header.PacketId;
            };

            var header = new PacketHeader(0x199, 8, 1);
            bool dispatched = dispatcher.Dispatch(header, ReadOnlySpan<byte>.Empty);

            Assert.False(dispatched);
            Assert.True(unhandledInvoked);
            Assert.Equal(0x199, unhandledId);
        }

        [Fact]
        public void S2C_0x00A_LoginAck_DecodesCoordinatesAndHeading()
        {
            byte[] payload = new byte[20];
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(0, 4), 1001); // UniqueNo
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(4, 2), 42);   // ActIndex
            payload[7] = 128; // Dir
            BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(8, 4), 10.5f);  // X (East/West)
            BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(12, 4), 5.0f);   // Wire offset 12 is Elevation (Z)
            BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(16, 4), -20.2f); // Wire offset 16 is North/South (Y)

            var ack = new S2C_0x00A_LoginAck(payload);

            Assert.True(ack.IsValid);
            Assert.Equal(1001u, ack.UniqueNo);
            Assert.Equal((ushort)42, ack.ActorIndex);
            Assert.Equal((byte)128, ack.Direction);
            Assert.Equal(10.5f, ack.X);
            Assert.Equal(-20.2f, ack.Y);
            Assert.Equal(5.0f, ack.Z);
        }

        [Fact]
        public void S2C_0x008_EnterZone_DecodesVisitedZoneBitmask()
        {
            byte[] payload = new byte[48];
            // Mark Zone 0 and Zone 10 as visited
            // Zone 0: Byte 0, bit 0 (0x01)
            // Zone 10: Byte 1, bit 2 (0x04)
            payload[0] = 0x01;
            payload[1] = 0x04;

            var enterZone = new S2C_0x008_EnterZone(payload);

            Assert.True(enterZone.IsValid);
            Assert.True(enterZone.HasVisitedZone(0));
            Assert.False(enterZone.HasVisitedZone(1));
            Assert.True(enterZone.HasVisitedZone(10));
            Assert.False(enterZone.HasVisitedZone(11));
        }

        [Fact]
        public void S2C_0x00B_Logout_DecodesZoneTransitionAndTargetEndpoint()
        {
            byte[] payload = new byte[24];
            payload[0] = (byte)LogoutState.ZoneChange; // State = 2

            // Target IP: 127.0.0.1 (0x7F, 0x00, 0x00, 0x01)
            payload[4] = 127;
            payload[5] = 0;
            payload[6] = 0;
            payload[7] = 1;

            // Target Port: 54230 (0xD3D6)
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(8, 4), 54230);

            // Error code: 0
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(20, 4), 0);

            var logout = new S2C_0x00B_Logout(payload);

            Assert.True(logout.IsValid);
            Assert.Equal(LogoutState.ZoneChange, logout.State);
            Assert.Equal((ushort)54230, logout.TargetPort);
            Assert.Equal(IPAddress.Loopback, logout.GetTargetIpAddress());
            Assert.Equal(0u, logout.ErrorCode);
        }

        [Fact]
        public void LifecycleModule_ZoneTransitionReceived_FiresOn0x00B()
        {
            var profile = new SessionProfile();
            var dispatcher = new PacketDispatcher();
            var lifecycle = new LifecyclePacketModule(profile, (_, _) => Task.CompletedTask);
            lifecycle.Register(dispatcher);

            LogoutState receivedState = LogoutState.None;
            IPAddress? receivedIp = null;
            ushort receivedPort = 0;

            lifecycle.ZoneTransitionReceived += (state, ip, port, err) =>
            {
                receivedState = state;
                receivedIp = ip;
                receivedPort = port;
            };

            // Build 0x00B packet payload for MyRoom (Mog House)
            byte[] payload = new byte[24];
            payload[0] = (byte)LogoutState.MyRoom;
            payload[4] = 192;
            payload[5] = 168;
            payload[6] = 1;
            payload[7] = 50;
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(8, 4), 54231);

            var header = new PacketHeader(0x00B, 28, 1);
            bool handled = dispatcher.Dispatch(header, payload);

            Assert.True(handled);
            Assert.Equal(LogoutState.MyRoom, receivedState);
            Assert.Equal(IPAddress.Parse("192.168.1.50"), receivedIp);
            Assert.Equal((ushort)54231, receivedPort);
        }

        [Fact]
        public void LifecycleOutboundPackets_BuildMapRect_ProducesAccurateBinary()
        {
            byte[] packet = LifecycleOutboundPackets.BuildMapRect(
                rectId: 100,
                x: 5.5f,
                y: 1.0f,
                z: -12.3f,
                actorIndex: 7,
                myRoomExitBit: 1,
                myRoomExitMode: 2,
                sequenceId: 15
            );

            Assert.Equal(24, packet.Length);
            ushort rawHeader = BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(0, 2));
            ushort packetId = (ushort)(rawHeader & 0x1FF);
            int size = (packet[1] & 0xFE) * 2;
            ushort seq = BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(2, 2));

            Assert.Equal(0x05E, packetId);
            Assert.Equal(24, size);
            Assert.Equal((ushort)15, seq);

            uint rectId = BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(4, 4));
            float x = BinaryPrimitives.ReadSingleLittleEndian(packet.AsSpan(8, 4));
            float y = BinaryPrimitives.ReadSingleLittleEndian(packet.AsSpan(12, 4));
            float z = BinaryPrimitives.ReadSingleLittleEndian(packet.AsSpan(16, 4));
            ushort actIndex = BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(20, 2));

            Assert.Equal(100u, rectId);
            Assert.Equal(5.5f, x);
            Assert.Equal(1.0f, y);
            Assert.Equal(-12.3f, z);
            Assert.Equal((ushort)7, actIndex);
            Assert.Equal(1, packet[22]);
            Assert.Equal(2, packet[23]);
        }
    }
}
