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

        [Fact]
        public void FePacket_Encoding_MatchesLandSandBoatSpecification()
        {
            byte[] sessionHash = new byte[16] { 0xAA, 0xBB, 0xCC, 0xDD, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12 };

            byte[] fePacket = new byte[28];
            fePacket[0] = 0xFE;
            sessionHash.CopyTo(fePacket.AsSpan(12, 16));

            Assert.Equal(0xFE, fePacket[0]);
            Assert.Equal(sessionHash, fePacket.AsSpan(12, 16).ToArray());
        }

        [Fact]
        public void View07Packet_Encoding_MatchesLandSandBoatSpecification()
        {
            uint selectedCharId = 12345;
            string charName = "Cybin";
            byte[] sessionHash = new byte[16] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16 };

            byte[] view07Packet = new byte[64];
            BinaryPrimitives.WriteUInt32LittleEndian(view07Packet.AsSpan(0, 4), 64);
            view07Packet[4] = 0x49; // I
            view07Packet[5] = 0x58; // X
            view07Packet[6] = 0x46; // F
            view07Packet[7] = 0x46; // F
            view07Packet[8] = 0x07;
            sessionHash.CopyTo(view07Packet.AsSpan(12, 16));
            BinaryPrimitives.WriteUInt32LittleEndian(view07Packet.AsSpan(28, 4), selectedCharId);

            byte[] nameBytes = Encoding.ASCII.GetBytes(charName);
            nameBytes.CopyTo(view07Packet.AsSpan(36, nameBytes.Length));

            Assert.Equal(64u, BinaryPrimitives.ReadUInt32LittleEndian(view07Packet.AsSpan(0, 4)));
            Assert.Equal(0x49, view07Packet[4]);
            Assert.Equal(0x07, view07Packet[8]);
            Assert.Equal(sessionHash, view07Packet.AsSpan(12, 16).ToArray());
            Assert.Equal(selectedCharId, BinaryPrimitives.ReadUInt32LittleEndian(view07Packet.AsSpan(28, 4)));
            Assert.Equal("Cybin", Encoding.ASCII.GetString(view07Packet, 36, 5));
        }

        [Fact]
        public void View26Packet_Encoding_MatchesLandSandBoatSpecification()
        {
            byte[] sessionHash = new byte[16] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16 };

            byte[] view26Packet = new byte[128];
            BinaryPrimitives.WriteUInt32LittleEndian(view26Packet.AsSpan(0, 4), 128);
            view26Packet[4] = 0x49; // I
            view26Packet[5] = 0x58; // X
            view26Packet[6] = 0x46; // F
            view26Packet[7] = 0x46; // F
            view26Packet[8] = 0x26;
            sessionHash.CopyTo(view26Packet.AsSpan(12, 16));

            byte[] verBytes = Encoding.ASCII.GetBytes("30260904_1");
            verBytes.CopyTo(view26Packet.AsSpan(0x74, Math.Min(verBytes.Length, 10)));

            Assert.Equal(128u, BinaryPrimitives.ReadUInt32LittleEndian(view26Packet.AsSpan(0, 4)));
            Assert.Equal(0x26, view26Packet[8]);
            Assert.Equal(sessionHash, view26Packet.AsSpan(12, 16).ToArray());
            Assert.Equal("30260904_1", Encoding.ASCII.GetString(view26Packet, 0x74, 10));
        }

        [Fact]
        public void LsbLoginClient_StatusChangedEvent_CanBeSubscribedAndFired()
        {
            var client = new LsbLoginClient();
            string? capturedStatus = null;
            client.StatusChanged += (s, msg) => capturedStatus = msg;

            Assert.Null(capturedStatus);
            Assert.NotNull(client);
        }

        [Fact]
        public async System.Threading.Tasks.Task LsbLoginClient_AuthenticateAsync_ThrowsOperationCanceledException_WhenCancelled()
        {
            var client = new LsbLoginClient();
            using var cts = new System.Threading.CancellationTokenSource();
            cts.Cancel(); // Pre-cancel

            await Assert.ThrowsAnyAsync<System.OperationCanceledException>(async () =>
            {
                await client.AuthenticateAsync("127.0.0.1", 54231, "user", "pass", ct: cts.Token);
            });
        }

        /// <summary>An xi_view 0x20 list: header, count at 28, 140-byte slots (ffxi_id at +0, name at +12).</summary>
        private static byte[] CharacterList(params (uint Id, string Name)[] slots)
        {
            var packet = new byte[32 + 16 * 140];
            BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(0, 4), (uint)packet.Length);
            packet[8] = 0x20;
            BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(28, 4), (uint)slots.Length);
            for (int i = 0; i < slots.Length; i++)
            {
                int at = 32 + i * 140;
                BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(at, 4), slots[i].Id);
                Encoding.ASCII.GetBytes(slots[i].Name).CopyTo(packet.AsSpan(at + 12, 16));
            }
            return packet;
        }

        [Fact]
        public void ParseCharacterSlots_ReadsEverySlot_SkippingFreeOnes()
        {
            var slots = LsbLoginClient.ParseCharacterSlots(CharacterList((21828, "Knot"), (21900, "BLM"), (0, " ")));
            Assert.Equal(new[] { (21828u, "Knot"), (21900u, "BLM") }, slots.ToArray());
        }

        /// <summary>
        /// The maintainer's second character on account 1000 (2026-10-01): the profile names "BLM", but the id came from the
        /// list's first slot (Knot), and LandSandBoat's xi_view dropped the "mismatched character name" selection.
        /// </summary>
        [Fact]
        public void ChooseCharacter_TakesTheIdOfTheNamedCharacter()
        {
            var slots = LsbLoginClient.ParseCharacterSlots(CharacterList((21828, "Knot"), (21900, "BLM")));
            Assert.Equal((21900u, "BLM"), LsbLoginClient.ChooseCharacter(slots, "BLM", 0));
            Assert.Equal((21900u, "BLM"), LsbLoginClient.ChooseCharacter(slots, "blm", 21828)); // the name wins over a stale id
            Assert.Equal((21900u, "BLM"), LsbLoginClient.ChooseCharacter(slots, null, 21900));
            Assert.Equal((21828u, "Knot"), LsbLoginClient.ChooseCharacter(slots, null, 0));
            Assert.Equal((21828u, "Knot"), LsbLoginClient.ChooseCharacter(slots, "Nobody", 0));
            Assert.Equal((5u, "Solo"), LsbLoginClient.ChooseCharacter(System.Array.Empty<(uint, string)>(), "Solo", 5));
        }
    }
}
