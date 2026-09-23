// tests/Gordian.Core.Tests/Network/WeatherPacketTests.cs
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Threading.Tasks;
using Gordian.Core.Config;
using Gordian.Core.Network;
using Gordian.Core.Network.Compression;
using Gordian.Core.Network.Crypto;
using Gordian.Core.Network.Packets;
using Gordian.Core.World;
using Xunit;

namespace Gordian.Core.Tests.Network
{
    public class WeatherPacketTests
    {
        [Fact]
        public void S2C_0x057_Weather_DecodesWirePayload()
        {
            byte[] payload = new byte[8];
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(0, 4), 1600000000); // StartTime
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(4, 2), 2);          // WeatherNumber (clod)
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(6, 2), 60);         // WeatherOffsetTime

            var weatherPacket = new S2C_0x057_Weather(payload);

            Assert.True(weatherPacket.IsValid);
            Assert.Equal(1600000000u, weatherPacket.StartTime);
            Assert.Equal(2, weatherPacket.WeatherNumber);
            Assert.Equal(60, weatherPacket.WeatherOffsetTime);
        }

        [Fact]
        public void S2C_0x00A_LoginAck_DecodesWeatherNumber()
        {
            byte[] payload = new byte[128];
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(0, 4), 0x1234);     // PlayerId
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(4, 2), 42);         // TargetIndex
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(44, 2), 4);        // ZoneId (Bibiki Bay) at offset 44
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(100, 2), 2);       // WeatherNumber (clod) at offset 100

            var loginAck = new S2C_0x00A_LoginAck(payload);

            Assert.True(loginAck.IsValid);
            Assert.Equal(0x1234u, loginAck.UniqueNo);
            Assert.Equal(42, loginAck.ActorIndex);
            Assert.Equal(4, loginAck.ZoneId);
            Assert.Equal(2, loginAck.WeatherNumber);
        }

        [Fact]
        public void LifecyclePacketModule_DispatchesWeatherPacket_AndFiresEvent()
        {
            var profile = new SessionProfile();
            var module = new LifecyclePacketModule(profile, (m, h) => Task.CompletedTask);
            ushort receivedWeather = 0;
            module.WeatherReceived += w => receivedWeather = w;

            var dispatcher = new PacketDispatcher();
            module.Register(dispatcher);

            byte[] subPacket = new byte[12]; // 4-byte header + 8-byte payload
            PacketHeader.Write(subPacket.AsSpan(), 0x057, 3, 0);

            BinaryPrimitives.WriteUInt32LittleEndian(subPacket.AsSpan(4, 4), 1600000000);
            BinaryPrimitives.WriteUInt16LittleEndian(subPacket.AsSpan(8, 2), 2); // WeatherNumber = 2 (clod)
            BinaryPrimitives.WriteUInt16LittleEndian(subPacket.AsSpan(10, 2), 30);

            var header = new PacketHeader(0x057, 12, 0);
            dispatcher.Dispatch(header, subPacket.AsSpan(4));

            Assert.Equal(2, receivedWeather);
        }

        [Fact]
        public void PacketParser_WeatherPacketUpdatesWorldState()
        {
            var profile = new SessionProfile();
            var world = new WorldState();

            using var suite = new LegacyBlowfishCryptoSuite();
            byte[] key = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
            suite.InitializeKey(key);

            var parser = new PacketParser(profile, (m, h) => Task.CompletedTask, suite, FfxiCodec.Default, dispatcher: null, world: world);

            ushort firedWeather = 0;
            parser.WeatherReceived += w => firedWeather = w;

            // Construct sub-packet 0x057
            byte[] subPacket = new byte[12];
            PacketHeader.Write(subPacket.AsSpan(), 0x057, 3, 0);

            BinaryPrimitives.WriteUInt32LittleEndian(subPacket.AsSpan(4, 4), 1600000000);
            BinaryPrimitives.WriteUInt16LittleEndian(subPacket.AsSpan(8, 2), 2); // clod
            BinaryPrimitives.WriteUInt16LittleEndian(subPacket.AsSpan(10, 2), 0);

            // Compress sub-packet
            byte[] compressed = new byte[64];
            int compLen = FfxiCodec.Default.Compress(subPacket, compressed);

            // Build full datagram: 28-byte header + compressed payload + 32-byte Blowfish signature
            const int headerSize = 28;
            byte[] datagram = new byte[headerSize + compLen + 32];
            compressed.AsSpan(0, compLen).CopyTo(datagram.AsSpan(headerSize));

            int totalDatagramSize = suite.EncryptAndSign(datagram, headerSize, compLen);

            parser.ProcessIncomingChunk(datagram.AsSpan(0, totalDatagramSize));

            Assert.Equal(2, world.WeatherNumber);
            Assert.Equal("clod", world.WeatherId);
            Assert.Equal(2, firedWeather);
        }
    }
}
