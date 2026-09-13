// tests/Gordian.Core.Tests/Network/LsbLoginClientTests.cs
using System;
using System.Buffers.Binary;
using System.Text;
using Gordian.Core.Network.LandSandBoat;
using Xunit;

namespace Gordian.Core.Tests.Network
{
    public class LsbLoginClientTests
    {
        [Fact]
        public void LsbSessionTicket_InitializesWithDefaultValues()
        {
            var ticket = new LsbSessionTicket
            {
                AccountId = 1000,
                CharacterId = 2000,
                CharacterName = "TestChar",
                ZoneIp = "127.0.0.1",
                ZonePort = 54230,
                SessionHash = new byte[16],
                BlowfishKey = new byte[20]
            };

            Assert.Equal(1000u, ticket.AccountId);
            Assert.Equal(2000u, ticket.CharacterId);
            Assert.Equal("TestChar", ticket.CharacterName);
            Assert.Equal("127.0.0.1", ticket.ZoneIp);
            Assert.Equal(54230, ticket.ZonePort);
            Assert.Equal(16, ticket.SessionHash.Length);
            Assert.Equal(20, ticket.BlowfishKey.Length);
        }

        [Fact]
        public void LsbCharacterInfo_PropertiesStoreAccurately()
        {
            var info = new LsbCharacterInfo
            {
                CharacterId = 12345,
                ContentId = 67890,
                CharIdMain = 12345,
                WorldId = 0,
                CharIdExtra = 0
            };

            Assert.Equal(12345u, info.CharacterId);
            Assert.Equal(67890u, info.ContentId);
            Assert.Equal(12345, info.CharIdMain);
            Assert.Equal(0, info.WorldId);
            Assert.Equal(0, info.CharIdExtra);
        }

        [Fact]
        public void A1Packet_Encoding_MatchesLandSandBoatSpecification()
        {
            uint accountId = 1000;
            byte[] sessionHash = new byte[16] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16 };

            byte[] a1Packet = new byte[28];
            a1Packet[0] = 0xA1;
            BinaryPrimitives.WriteUInt32LittleEndian(a1Packet.AsSpan(1, 4), accountId);
            sessionHash.CopyTo(a1Packet.AsSpan(12, 16));

            Assert.Equal(0xA1, a1Packet[0]);
            Assert.Equal(accountId, BinaryPrimitives.ReadUInt32LittleEndian(a1Packet.AsSpan(1, 4)));
            Assert.Equal(sessionHash, a1Packet.AsSpan(12, 16).ToArray());
        }

        [Fact]
        public void A2Packet_Encoding_MatchesLandSandBoatSpecification()
        {
            byte[] blowfishKey = new byte[20];
            Array.Fill(blowfishKey, (byte)0x42);
            uint selectedCharId = 54321;

            byte[] a2Packet = new byte[28];
            a2Packet[0] = 0xA2;
            blowfishKey.CopyTo(a2Packet.AsSpan(1, 20));
            BinaryPrimitives.WriteUInt32LittleEndian(a2Packet.AsSpan(21, 4), selectedCharId);

            Assert.Equal(0xA2, a2Packet[0]);
            Assert.Equal(blowfishKey, a2Packet.AsSpan(1, 20).ToArray());
            Assert.Equal(selectedCharId, BinaryPrimitives.ReadUInt32LittleEndian(a2Packet.AsSpan(21, 4)));
        }
    }
}
