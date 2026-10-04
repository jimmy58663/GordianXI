// tests/Gordian.Core.Tests/Network/LsbLobbySessionTests.cs
using System;
using System.Buffers.Binary;
using System.Linq;
using System.Threading.Tasks;
using Gordian.Core.Network.LandSandBoat;
using Xunit;

namespace Gordian.Core.Tests.Network
{
    public class LsbLobbySessionTests
    {
        private static readonly byte[] Hash = Enumerable.Range(1, 16).Select(i => (byte)i).ToArray();
        private const uint Account = 1000;

        private static FakeLsbLobbyServer Server()
        {
            var server = new FakeLsbLobbyServer(Account, Hash);
            server.Characters.Add(new FakeLsbLobbyServer.Character { Id = 21828, Name = "Knot", Race = 2, Face = 5, Job = 4, Nation = 1, Zone = 236, Head = 0x0010, Body = 0x0020 });
            server.Characters.Add(new FakeLsbLobbyServer.Character { Id = 21900, Name = "Blm", Race = 7, Face = 14, Job = 6, Size = 2, Zone = 0x101 });
            return server;
        }

        private static Task<LsbLobbySession> Connect(FakeLsbLobbyServer server) =>
            LsbLobbySession.ConnectAsync("127.0.0.1", server.DataPort, server.ViewPort, Account, Hash, timeout: TimeSpan.FromSeconds(5));

        [Fact]
        public async Task Connect_ReadsCharactersWithLookAndFreeSlots()
        {
            using var server = Server();
            await using var lobby = await Connect(server);

            Assert.Equal(4, lobby.Characters.Count);
            var knot = lobby.Characters[0];
            Assert.Equal((1, 21828u, "Knot", "Gordian"), (knot.Slot, knot.ContentId, knot.Name, knot.WorldName));
            Assert.Equal((2, 4, 5, 1, 1), (knot.Race, knot.MainJob, knot.Face, knot.Nation, knot.MainJobLevel));
            Assert.Equal(236, knot.ZoneId);
            Assert.Equal(0x0205, knot.FaceModel);
            // LandSandBoat's raw look values get the world packets' slot bits.
            Assert.Equal(0x1010, knot.Equipment[0]);
            Assert.Equal(0x2020, knot.Equipment[1]);
            Assert.Equal(0x3000, knot.Equipment[2]);
            Assert.Equal(0x101, lobby.Characters[1].ZoneId);
            Assert.True(lobby.Characters[2].IsEmpty);
            Assert.True(lobby.Characters[3].IsEmpty);
            Assert.Equal(0x0FFFu, lobby.ServerExpansions);
            Assert.Equal(new byte[] { 0x26 }, server.ViewCommands.ToArray());
            Assert.Equal(new byte[] { 0xFE, 0xA1 }, server.DataCommands.ToArray());
        }

        [Fact]
        public async Task Connect_VersionLock_ThrowsServerError()
        {
            using var server = Server();
            server.VersionLocked = true;
            var ex = await Assert.ThrowsAsync<LobbyRequestException>(() => Connect(server));
            Assert.Equal(LobbyErrorCode.GameDataUpdated, ex.ErrorCode);
        }

        [Fact]
        public async Task Select_ReturnsTicketWithBaseKey()
        {
            using var server = Server();
            await using var lobby = await Connect(server);
            var ticket = await lobby.SelectCharacterAsync(lobby.Characters[1]);

            Assert.Equal((21900u, "Blm", "127.0.0.1", 54230), (ticket.CharacterId, ticket.CharacterName, ticket.ZoneIp, ticket.ZonePort));
            Assert.Equal(LsbLobbySession.CreateBaseKey(), ticket.BlowfishKey);
            Assert.Equal(server.StoredKey, ticket.BlowfishKey);
            Assert.False(lobby.IsConnected); // the server ends the lobby after 0x0B
        }

        [Fact]
        public async Task Refresh_GoesThroughTheDataChannelPrompt()
        {
            using var server = Server();
            await using var lobby = await Connect(server);
            server.Characters.RemoveAt(1);
            var characters = await lobby.RefreshCharactersAsync();

            Assert.Equal("Knot", characters[0].Name);
            Assert.True(characters[1].IsEmpty);
            Assert.Equal(new byte[] { 0x26, 0x1F }, server.ViewCommands.ToArray());
            Assert.Equal(new byte[] { 0xFE, 0xA1, 0xA1 }, server.DataCommands.ToArray());
        }

        [Fact]
        public async Task Create_ChecksNameCreatesRefreshesAndRaisesTheKeyBySix()
        {
            using var server = Server();
            await using var lobby = await Connect(server);
            var freeSlot = lobby.Characters.First(c => c.IsEmpty);
            var creation = new LobbyCharacterCreation("Newbie", Race: 5, Face: 3, Hair: 1, MainJob: 4, Size: 1, Nation: 2);
            await lobby.CreateCharacterAsync(freeSlot, creation);

            var created = lobby.Characters.Single(c => c.Name == "Newbie");
            Assert.Equal((5, 7, 4, 1, 2), (created.Race, created.Face, created.MainJob, created.Size, created.Nation));
            Assert.Equal(6, lobby.KeyIncrement);
            Assert.Equal(new byte[] { 0x26, 0x22, 0x21, 0x1F }, server.ViewCommands.ToArray());

            var ticket = await lobby.SelectCharacterAsync(created);
            Assert.Equal(0x58 + 6, ticket.BlowfishKey[16]);
            Assert.Equal(server.StoredKey, ticket.BlowfishKey);
        }

        [Fact]
        public async Task Create_TakenName_IsRefusedAndTheSessionStaysUsable()
        {
            using var server = Server();
            await using var lobby = await Connect(server);
            var freeSlot = lobby.Characters.First(c => c.IsEmpty);
            var ex = await Assert.ThrowsAsync<LobbyRequestException>(() =>
                lobby.CreateCharacterAsync(freeSlot, new LobbyCharacterCreation("knot", 1, 0, 0, 1, 0, 0)));
            Assert.Equal(LobbyErrorCode.CharacterNameUnavailable, ex.ErrorCode);
            Assert.True(lobby.IsConnected);
            Assert.Equal(0, lobby.KeyIncrement);

            await lobby.CheckNameAsync(freeSlot, "Fresh", "Gordian");
            Assert.Equal(4, (await lobby.RefreshCharactersAsync()).Count);
        }

        [Fact]
        public async Task Create_RefusedParameters_DropTheConnection()
        {
            using var server = Server();
            await using var lobby = await Connect(server);
            var freeSlot = lobby.Characters.First(c => c.IsEmpty);
            var ex = await Assert.ThrowsAsync<LobbyRequestException>(() =>
                lobby.CreateCharacterAsync(freeSlot, new LobbyCharacterCreation("Valid", Race: 9, 0, 0, 1, 0, 0)));
            Assert.False(ex.IsServerError);
            Assert.False(lobby.IsConnected);
        }

        [Fact]
        public async Task Delete_RefreshesAndRaisesTheKeyByFour()
        {
            using var server = Server();
            await using var lobby = await Connect(server);
            await lobby.DeleteCharacterAsync(lobby.Characters[0]);

            Assert.Equal("Blm", lobby.Characters[0].Name);
            Assert.Equal(4, lobby.KeyIncrement);
            var ticket = await lobby.SelectCharacterAsync(lobby.Characters[0]);
            Assert.Equal(0x58 + 4, ticket.BlowfishKey[16]);
            Assert.Equal(server.StoredKey, ticket.BlowfishKey);
        }

        [Fact]
        public async Task Delete_DisabledOnTheServer_IsRefused()
        {
            using var server = Server();
            server.DeletionEnabled = false;
            await using var lobby = await Connect(server);
            var ex = await Assert.ThrowsAsync<LobbyRequestException>(() => lobby.DeleteCharacterAsync(lobby.Characters[0]));
            Assert.Equal(LobbyErrorCode.CouldNotConnectToLobbyServer, ex.ErrorCode);
            Assert.Equal(0, lobby.KeyIncrement);
            Assert.Equal(2, lobby.Characters.Count(c => !c.IsEmpty));
        }

        [Fact]
        public async Task Select_AlreadyLoggedIn_RaisesTheKeyByOneForTheRetry()
        {
            using var server = Server();
            server.AlreadyLoggedInOnce = true;
            await using var lobby = await Connect(server);
            var ex = await Assert.ThrowsAsync<LobbyRequestException>(() => lobby.SelectCharacterAsync(lobby.Characters[0]));
            Assert.Equal(LobbyErrorCode.CharacterAlreadyLoggedIn, ex.ErrorCode);
            Assert.Contains("CHARACTER_ALREADY_LOGGED_IN", ex.Message);

            var ticket = await lobby.SelectCharacterAsync(lobby.Characters[0]);
            Assert.Equal(0x58 + 1, ticket.BlowfishKey[16]);
            Assert.Equal(server.StoredKey, ticket.BlowfishKey);
        }

        [Fact]
        public async Task Rename_SelectsUnderTheNewName()
        {
            using var server = Server();
            server.Characters[0].Rename = true;
            await using var lobby = await Connect(server);
            Assert.True(lobby.Characters[0].RenameRequired);
            var ticket = await lobby.RenameAndSelectAsync(lobby.Characters[0], "Renamed");

            Assert.Equal("Renamed", ticket.CharacterName);
            Assert.Equal(0x58 + 4, ticket.BlowfishKey[16]);
            Assert.Equal(server.StoredKey, ticket.BlowfishKey);
        }

        [Fact]
        public async Task Worlds_ListsTheServer()
        {
            using var server = Server();
            await using var lobby = await Connect(server);
            var worlds = await lobby.GetWorldsAsync();
            Assert.Equal(new LobbyWorld(0x20, "Gordian"), Assert.Single(worlds));
        }

        [Fact]
        public async Task LoginClient_SelectCharacterAsync_PicksBySlotThroughTheLobby()
        {
            using var server = Server();
            var client = new LsbLoginClient();
            int inspected = 0;
            client.PacketInspected += (_, _) => inspected++;
            var ticket = await client.SelectCharacterAsync("127.0.0.1", server.DataPort, server.ViewPort, Account, Hash, targetCharacterSlot: 2);
            Assert.Equal("Blm", ticket.CharacterName);
            Assert.True(inspected >= 8);
            // A slot that is free is refused rather than logging in as another character.
            using var server2 = Server();
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                client.SelectCharacterAsync("127.0.0.1", server2.DataPort, server2.ViewPort, Account, Hash, targetCharacterSlot: 3));
        }

        [Fact]
        public void Packets_UseTheRetailSizesAndLandSandBoatOffsets()
        {
            var buffer = new byte[0x98];
            LobbyPackets.WriteSelectCharacter(buffer, Hash, 21828, 0x0155, "Knot");
            Assert.Equal(0x58u, BinaryPrimitives.ReadUInt32LittleEndian(buffer));
            Assert.Equal(LobbyPackets.Terminator, BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(4)));
            Assert.Equal(0x07, buffer[8]);
            Assert.Equal(Hash, buffer.AsSpan(12, 16).ToArray());
            Assert.Equal(21828u, BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(28)));
            Assert.Equal("Knot", LobbyPackets.ReadAscii(buffer.AsSpan(36, 16)));
            Assert.Equal(3u, BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(0x44)));

            LobbyPackets.WriteLobbyLogin(buffer, Hash, "30260904_1");
            Assert.Equal(0x98u, BinaryPrimitives.ReadUInt32LittleEndian(buffer));
            Assert.Equal("30260904_1", LobbyPackets.ReadAscii(buffer.AsSpan(0x74, 16)));

            var create = new byte[LobbyPackets.CreateCharacterSize];
            LobbyPackets.WriteCreateCharacter(create, Hash, 0, new LobbyCharacterCreation("Newbie", Race: 8, Face: 7, Hair: 1, MainJob: 2, Size: 2, Nation: 1));
            // LandSandBoat reads race 48, job 50, nation 54, size 57 and the combined face (GrapIDTbl[0]'s low byte) at 60.
            Assert.Equal((8, 2, 1, 2, 15), (create[48], create[50], create[54], create[57], create[60]));
            Assert.Equal(0x080F, BinaryPrimitives.ReadUInt16LittleEndian(create.AsSpan(60)));

            var pre = new byte[LobbyPackets.CreateCharacterPreSize];
            LobbyPackets.WriteCreateCharacterPre(pre, Hash, 0, "Newbie", "Gordian");
            Assert.Equal(("Newbie", "Gordian"), (LobbyPackets.ReadAscii(pre.AsSpan(32, 16)), LobbyPackets.ReadAscii(pre.AsSpan(64, 16))));

            var rename = new byte[LobbyPackets.RenameCharacterSize];
            LobbyPackets.WriteRenameCharacter(rename, Hash, 5, 5, "Other");
            Assert.Equal("Other", LobbyPackets.ReadAscii(rename.AsSpan(36, 16)));
        }

        [Fact]
        public void ParseCharacterList_KeepsRetailModelIds()
        {
            // A retail-style entry (XiPackets ResponseChrInfo2 example): GrapIDTbl[0] = (race << 8) | face, full model ids.
            var packet = new byte[32 + 140];
            BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(28), 1);
            int info = 32 + 44;
            BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(info), 1);
            BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(info + 12), 0x010A);
            BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(info + 14), 0x11D6);
            "Atomos"u8.CopyTo(packet.AsSpan(32 + 12));
            var character = Assert.Single(LobbyPackets.ParseCharacterList(packet));
            Assert.Equal((10, 0x11D6, 0x010A), (character.Face, character.Equipment[0], character.FaceModel));
        }
    }
}
