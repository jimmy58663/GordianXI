// tests/Gordian.Core.Tests/Network/LsbSessionKeysTests.cs
using System;
using System.Buffers.Binary;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Gordian.Core.Network.Crypto;
using Gordian.Core.Network.LandSandBoat;
using Xunit;

namespace Gordian.Core.Tests.Network
{
    /// <summary>
    /// LandSandBoat's map session key schemes (docs/network/session-and-packets.md, "Map session key"): the lobby key
    /// counter, the key derivations, the negotiation that picks the server's scheme and the xiloader version fallback.
    /// </summary>
    public class LsbSessionKeysTests
    {
        private static readonly byte[] Hash = Enumerable.Range(1, 16).Select(i => (byte)i).ToArray();
        private const uint Account = 1000;
        private const uint ServerKey = 0xAD5DE04F; // LandSandBoat's 0x05 key (view_session.cpp 0x26)

        private static byte[] View(byte command, int size = 0x20)
        {
            var packet = new byte[size];
            BinaryPrimitives.WriteUInt32LittleEndian(packet, (uint)size);
            BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(4), LobbyPackets.Terminator);
            packet[8] = command;
            return packet;
        }

        private static byte[] KeyReply(uint key)
        {
            var packet = View(0x05, 0x28);
            BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(0x1C), key);
            return packet;
        }

        // --- Key derivation ---

        [Fact]
        public void CounterKey_IsTheMd5OfTheSessionHashAndTheCounterPlusThree()
        {
            var key = LsbSessionKeys.DeriveCounterKey(Hash, 0xAD5DE052, LsbLobbySession.CreateBaseKey());
            Assert.Equal(MD5.HashData(Hash), key.AsSpan(0, 16).ToArray());
            Assert.Equal(0xAD5DE055u, BinaryPrimitives.ReadUInt32LittleEndian(key.AsSpan(16)));
        }

        [Fact]
        public void CounterKey_WithoutACounter_KeepsTheLoadersBytes()
        {
            var key = LsbSessionKeys.DeriveCounterKey(Hash, 0, LsbLobbySession.CreateBaseKey());
            Assert.Equal(new byte[] { 0x58, 0xE0, 0x5D, 0xAD }, key.AsSpan(16).ToArray());
        }

        [Fact]
        public void CounterKey_CarriesIntoTheUpperBytes()
        {
            var key = LsbSessionKeys.DeriveCounterKey(Hash, 0x000000FE, LsbLobbySession.CreateBaseKey());
            Assert.Equal(0x101u, BinaryPrimitives.ReadUInt32LittleEndian(key.AsSpan(16)));
        }

        [Fact]
        public void LegacyKey_RaisesByteSixteenWithoutCarry()
        {
            var baseKey = LsbLobbySession.CreateBaseKey();
            baseKey[16] = 0xFE;
            var key = LsbSessionKeys.DeriveLegacyKey(baseKey, 6);
            Assert.Equal(0x04, key[16]);
            Assert.Equal(0xE0, key[17]);
            Assert.Equal(new byte[16], key.AsSpan(0, 16).ToArray());

            var hashed = LsbSessionKeys.DeriveLegacyKey(LsbLobbySession.CreateBaseKey(), 4, Hash);
            Assert.Equal(MD5.HashData(Hash), hashed.AsSpan(0, 16).ToArray());
            Assert.Equal(0x58 + 4, hashed[16]);
        }

        [Fact]
        public void Fingerprint_IsFnv1aOfTheFirstSixteenBytes()
        {
            Assert.Equal(0x811C9DC5u, LsbSessionKeys.Fingerprint(ReadOnlySpan<byte>.Empty));
            Assert.Equal(0xE40C292Cu, LsbSessionKeys.Fingerprint("a"u8));
            // Bytes past 16 (the counter word) do not count.
            var key = LsbSessionKeys.DeriveCounterKey(Hash, 1, LsbLobbySession.CreateBaseKey());
            var other = LsbSessionKeys.DeriveCounterKey(Hash, 2, LsbLobbySession.CreateBaseKey());
            Assert.Equal(LsbSessionKeys.Fingerprint(key), LsbSessionKeys.Fingerprint(other));
        }

        [Fact]
        public void Candidates_NewestSchemeFirst_WithoutDuplicates()
        {
            var candidates = LsbSessionKeys.BuildCandidates(Hash, 0xAD5DE052, LsbLobbySession.CreateBaseKey(), 0);
            Assert.Equal(new[] { LsbKeyStyle.Counter, LsbKeyStyle.HashedLegacy, LsbKeyStyle.Legacy }, candidates.Select(c => c.Style));
            Assert.Equal(LsbLobbySession.CreateBaseKey(), candidates[2].Key);

            // Without a counter the counter key and the hashed legacy key are the same bytes.
            var noCounter = LsbSessionKeys.BuildCandidates(Hash, 0, LsbLobbySession.CreateBaseKey(), 0);
            Assert.Equal(new[] { LsbKeyStyle.Counter, LsbKeyStyle.Legacy }, noCounter.Select(c => c.Style));
        }

        // --- Lobby key counter ---

        /// <summary>
        /// GordianXI's direct select: 0x26 -> 0x05, data 0xA1 -> view 0x20, 0x07, data 0xA2. The maintainer's connect
        /// server logged "key counter AD5DE055" for exactly this flow (2026-10-12 18:26:22, LandSandBoat e27adcc46d).
        /// </summary>
        [Fact]
        public void Counter_PlainSelect_MatchesTheServerLog()
        {
            var counter = new LobbyKeyCounter();
            counter.OnRequest(0x26);
            counter.OnReply(KeyReply(ServerKey));
            counter.OnReply(View(0x20));
            counter.OnRequest(0x07);
            Assert.Equal(0xAD5DE055u, counter.Value + LsbSessionKeys.CounterStepsAfterSelect);
        }

        /// <summary>
        /// The retail flow (0x1F refresh and 0x24 world list before selecting) lands on 0xAD5DE058, the bytes xiloader
        /// always sent: the old fixed key was the retail counter of a plain login.
        /// </summary>
        [Fact]
        public void Counter_RetailFlow_LandsOnTheXiloaderKey()
        {
            var counter = new LobbyKeyCounter();
            counter.OnRequest(0x26);
            counter.OnReply(KeyReply(ServerKey));
            counter.OnRequest(0x1F);
            counter.OnReply(View(0x20));
            counter.OnRequest(0x24);
            counter.OnReply(View(0x23));
            counter.OnRequest(0x07);
            Assert.Equal(0xAD5DE058u, counter.Value + 3);
        }

        [Fact]
        public void Counter_ErrorReplies_StepOnlyAfterNameCheckWorldListRenameAnd2B()
        {
            var counter = new LobbyKeyCounter();
            counter.OnReply(KeyReply(100));
            Assert.Equal(101u, counter.Value);

            counter.OnRequest(0x22);           // +1
            counter.OnReply(View(0x04, 0x24)); // +1: a refused name
            Assert.Equal(103u, counter.Value);

            counter.OnRequest(0x07);           // +1
            counter.OnReply(View(0x04, 0x24)); // already logged in: no step
            Assert.Equal(104u, counter.Value);

            counter.OnRequest(0x14);           // +1
            counter.OnReply(View(0x04, 0x24)); // deletion disabled: no step
            Assert.Equal(105u, counter.Value);

            counter.OnRequest(0x28);           // +1
            counter.OnReply(View(0x04, 0x24)); // +1
            Assert.Equal(107u, counter.Value);
        }

        [Fact]
        public void Counter_KeyReplyStartsOver_AndDeferredRequestsCountWhenConfirmed()
        {
            var counter = new LobbyKeyCounter();
            counter.OnRequest(0x22);
            counter.OnReply(KeyReply(500));
            Assert.Equal(501u, counter.Value);

            counter.OnRequest(0x1F, deferStep: true);
            Assert.Equal(501u, counter.Value);
            counter.ConfirmDeferredRequest();
            Assert.Equal(502u, counter.Value);
        }

        // --- Through the lobby session against a loopback LandSandBoat stand-in ---

        private static FakeLsbLobbyServer CounterServer()
        {
            var server = new FakeLsbLobbyServer(Account, Hash) { CounterKeys = true };
            server.Characters.Add(new FakeLsbLobbyServer.Character { Id = 21828, Name = "Knot", Race = 2, Face = 5, Job = 4, Nation = 1 });
            server.Characters.Add(new FakeLsbLobbyServer.Character { Id = 21900, Name = "Blm", Race = 7, Face = 14, Job = 6 });
            return server;
        }

        private static Task<LsbLobbySession> Connect(FakeLsbLobbyServer server) =>
            LsbLobbySession.ConnectAsync("127.0.0.1", server.DataPort, server.ViewPort, Account, Hash, timeout: TimeSpan.FromSeconds(5));

        private static void AssertCounterKeyMatches(FakeLsbLobbyServer server, LsbSessionTicket ticket)
        {
            Assert.Equal(LsbKeyStyle.Counter, ticket.KeyCandidates[0].Style);
            Assert.Equal(server.StoredKey, ticket.KeyCandidates[0].Key);
        }

        [Fact]
        public async Task Lobby_PlainSelect_CounterKeyMatchesTheServer()
        {
            using var server = CounterServer();
            await using var lobby = await Connect(server);
            var ticket = await lobby.SelectCharacterAsync(lobby.Characters[0], TestContext.Current.CancellationToken);
            AssertCounterKeyMatches(server, ticket);
            Assert.Equal(0xAD5DE055u, ticket.KeyCandidates[0].CounterWord);
        }

        [Fact]
        public async Task Lobby_CreateThenSelect_CounterKeyMatchesTheServer()
        {
            using var server = CounterServer();
            await using var lobby = await Connect(server);
            var free = lobby.Characters.First(c => c.IsEmpty);
            await lobby.GetWorldsAsync(TestContext.Current.CancellationToken);
            await lobby.CreateCharacterAsync(free, new LobbyCharacterCreation("Newbie", 5, 3, 1, 4, 1, 2), TestContext.Current.CancellationToken);
            var ticket = await lobby.SelectCharacterAsync(lobby.Characters.Single(c => c.Name == "Newbie"), TestContext.Current.CancellationToken);
            AssertCounterKeyMatches(server, ticket);
        }

        [Fact]
        public async Task Lobby_RefusedNameThenSelect_CounterKeyMatchesTheServer()
        {
            using var server = CounterServer();
            await using var lobby = await Connect(server);
            var free = lobby.Characters.First(c => c.IsEmpty);
            await Assert.ThrowsAsync<LobbyRequestException>(() => lobby.CheckNameAsync(free, "knot", "Gordian", TestContext.Current.CancellationToken));
            var ticket = await lobby.SelectCharacterAsync(lobby.Characters[0], TestContext.Current.CancellationToken);
            AssertCounterKeyMatches(server, ticket);
        }

        [Fact]
        public async Task Lobby_DeleteThenSelect_CounterKeyMatchesTheServer()
        {
            using var server = CounterServer();
            await using var lobby = await Connect(server);
            await lobby.DeleteCharacterAsync(lobby.Characters[0], TestContext.Current.CancellationToken);
            var ticket = await lobby.SelectCharacterAsync(lobby.Characters[0], TestContext.Current.CancellationToken);
            AssertCounterKeyMatches(server, ticket);
        }

        /// <summary>A 0x1F the server lost never stepped its counter; the resent one did.</summary>
        [Fact]
        public async Task Lobby_DeleteWithALostRefresh_CounterKeyMatchesTheServer()
        {
            using var server = CounterServer();
            server.LoseRequestAfterDelete = true;
            await using var lobby = await Connect(server);
            await lobby.DeleteCharacterAsync(lobby.Characters[0], TestContext.Current.CancellationToken);
            Assert.Equal(1, server.DroppedRequests);
            var ticket = await lobby.SelectCharacterAsync(lobby.Characters[0], TestContext.Current.CancellationToken);
            AssertCounterKeyMatches(server, ticket);
        }

        /// <summary>A slow 0x1F and its resend both reach the server: both prompts count.</summary>
        [Fact]
        public async Task Lobby_DeleteWithALateRefresh_CounterKeyMatchesTheServer()
        {
            using var server = CounterServer();
            server.DelayRequestAfterDeleteMs = 2000;
            await using var lobby = await Connect(server);
            await lobby.DeleteCharacterAsync(lobby.Characters[0], TestContext.Current.CancellationToken);
            var ticket = await lobby.SelectCharacterAsync(lobby.Characters[0], TestContext.Current.CancellationToken);
            AssertCounterKeyMatches(server, ticket);
        }

        [Fact]
        public async Task Lobby_RenameThenSelect_CounterKeyMatchesTheServer()
        {
            using var server = CounterServer();
            server.Characters[0].Rename = true;
            await using var lobby = await Connect(server);
            var ticket = await lobby.RenameAndSelectAsync(lobby.Characters[0], "Renamed", TestContext.Current.CancellationToken);
            AssertCounterKeyMatches(server, ticket);
        }

        [Fact]
        public async Task Lobby_AlreadyLoggedInThenSelect_CounterKeyMatchesTheServer()
        {
            using var server = CounterServer();
            server.AlreadyLoggedInOnce = true;
            await using var lobby = await Connect(server);
            await Assert.ThrowsAsync<LobbyRequestException>(() => lobby.SelectCharacterAsync(lobby.Characters[0], TestContext.Current.CancellationToken));
            var ticket = await lobby.SelectCharacterAsync(lobby.Characters[0], TestContext.Current.CancellationToken);
            AssertCounterKeyMatches(server, ticket);
            // The old scheme's +1 for the refusal stays on the legacy candidate.
            Assert.Equal(0x58 + 1, ticket.KeyCandidates.Single(c => c.Style == LsbKeyStyle.Legacy).Key[16]);
        }

        [Fact]
        public async Task Lobby_OldServer_LegacyCandidateMatches()
        {
            using var server = CounterServer();
            server.CounterKeys = false;
            await using var lobby = await Connect(server);
            await lobby.DeleteCharacterAsync(lobby.Characters[0], TestContext.Current.CancellationToken);
            var ticket = await lobby.SelectCharacterAsync(lobby.Characters[0], TestContext.Current.CancellationToken);
            Assert.Equal(server.StoredKey, ticket.KeyCandidates.Single(c => c.Style == LsbKeyStyle.Legacy).Key);
            Assert.Equal(server.StoredKey, ticket.BlowfishKey);
        }

        // --- Negotiation ---

        private const int Header = 28;

        private static byte[] Encrypt(byte[] key, string text, bool advanceZone = false)
        {
            using var suite = new LegacyBlowfishCryptoSuite();
            suite.InitializeKey(key);
            if (advanceZone) suite.AdvanceZoneKey();
            byte[] payload = Encoding.ASCII.GetBytes(text);
            byte[] datagram = new byte[Header + payload.Length + 16];
            for (int i = 0; i < Header; i++) datagram[i] = (byte)(0xA0 + i);
            payload.CopyTo(datagram, Header);
            int length = suite.EncryptAndSign(datagram, Header, payload.Length);
            return datagram.AsSpan(0, length).ToArray();
        }

        private static SessionKeyCandidate[] ThreeKeys() =>
            LsbSessionKeys.BuildCandidates(Hash, 0xAD5DE052, LsbLobbySession.CreateBaseKey(), 4).Select(c => c.ToCryptoCandidate()).ToArray();

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        public void Negotiation_LocksInTheCandidateTheServersFirstDatagramVerifiesWith(int serverKeyIndex)
        {
            var keys = ThreeKeys();
            using var suite = new LegacyBlowfishCryptoSuite();
            suite.InitializeKeyCandidates(keys);
            Assert.True(suite.IsNegotiatingKey);

            var datagram = Encrypt(keys[serverKeyIndex].Key, "S2C 0x00A login and friends, padded to a few blocks.");
            Assert.True(suite.TryDecryptAndVerify(datagram, Header, out int length));
            Assert.Equal("S2C 0x00A login and friends, padded to a few blocks.", Encoding.ASCII.GetString(datagram, Header, length));
            Assert.False(suite.IsNegotiatingKey);
            Assert.Equal(keys[serverKeyIndex].Label, suite.NegotiatedKeyLabel);

            // From then on the session encrypts with that key: the server (holding that key) can read our packets.
            var outbound = new byte[Header + 64];
            Encoding.ASCII.GetBytes("C2S 0x015 position").CopyTo(outbound, Header);
            int sent = suite.EncryptAndSign(outbound, Header, 24);
            using var serverSide = new LegacyBlowfishCryptoSuite();
            serverSide.InitializeKey(keys[serverKeyIndex].Key);
            Assert.True(serverSide.TryDecryptAndVerify(outbound.AsSpan(0, sent), Header, out _));
        }

        [Fact]
        public void Negotiation_AnUndecryptableDatagram_LeavesItUntouchedAndKeepsNegotiating()
        {
            var keys = ThreeKeys();
            using var suite = new LegacyBlowfishCryptoSuite();
            suite.InitializeKeyCandidates(keys);

            var stranger = Encrypt(Encoding.ASCII.GetBytes("not one of them, 20b"), "garbage from another key entirely");
            var copy = (byte[])stranger.Clone();
            Assert.False(suite.TryDecryptAndVerify(stranger, Header, out _));
            Assert.Equal(copy, stranger);
            Assert.True(suite.IsNegotiatingKey);

            Assert.True(suite.TryDecryptAndVerify(Encrypt(keys[1].Key, "the real first datagram"), Header, out _));
            Assert.Equal(keys[1].Label, suite.NegotiatedKeyLabel);
        }

        [Fact]
        public void Negotiation_ZoneAdvancesBeforeTheLockAreKept()
        {
            var keys = ThreeKeys();
            using var suite = new LegacyBlowfishCryptoSuite();
            suite.InitializeKeyCandidates(keys);
            suite.AdvanceZoneKey();

            Assert.True(suite.TryDecryptAndVerify(Encrypt(keys[2].Key, "first datagram of the next zone", advanceZone: true), Header, out _));
            Assert.Equal(keys[2].Label, suite.NegotiatedKeyLabel);
            // The next zone advance continues from the locked key.
            suite.AdvanceZoneKey();
            using var twice = new LegacyBlowfishCryptoSuite();
            twice.InitializeKey(keys[2].Key);
            twice.AdvanceZoneKey();
            twice.AdvanceZoneKey();
            Assert.Equal(twice.CurrentRawKey.ToArray(), suite.CurrentRawKey.ToArray());
        }

        [Fact]
        public void Negotiation_ASingleCandidate_IsAnOrdinaryKey()
        {
            var keys = ThreeKeys();
            using var suite = new LegacyBlowfishCryptoSuite();
            suite.InitializeKeyCandidates(new[] { keys[0] });
            Assert.False(suite.IsNegotiatingKey);
            Assert.Equal(keys[0].Label, suite.NegotiatedKeyLabel);
            Assert.True(suite.TryDecryptAndVerify(Encrypt(keys[0].Key, "hello"), Header, out _));
        }

        // --- xiloader version fallback ---

        [Theory]
        [InlineData("Unsupported xiloader version 2.2.0.\nThis server requires version 2.1.x.", 2, 1)]
        [InlineData("Unsupported xiloader version 2.1.0.\nThis server requires version 2.2.x.", 2, 2)]
        [InlineData("Your xiloader is too old.\nPlease update to version '2.1.x'.\nYour client reported '2.2.0'.", 2, 1)]
        [InlineData("Your xiloader is too old.\nPlease update to version '2.0.x'.\nYour client reported '1.1.1'.", 2, 0)]
        public void VersionFallback_ParsesTheRequiredVersion(string message, int major, int minor)
        {
            Assert.True(LsbLoginClient.TryParseRequiredXiloaderVersion(message, out var required));
            Assert.Equal(((byte)major, (byte)minor), required);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("Failed to validate credentials")]
        [InlineData("Unsupported xiloader version 2.2.0.")]
        public void VersionFallback_OtherErrorsAreNotVersionErrors(string? message)
        {
            Assert.False(LsbLoginClient.TryParseRequiredXiloaderVersion(message, out _));
        }

        /// <summary>
        /// A loopback xi_connect stand-in that requires xiloader 2.1.x and answers others with LandSandBoat's
        /// "Unsupported xiloader version" error (auth_session.cpp). Records the versions it was sent.
        /// </summary>
        internal sealed class FakeAuthServer : IDisposable
        {
            private readonly System.Net.Sockets.TcpListener _listener = new(System.Net.IPAddress.Loopback, 0);
            private readonly System.Security.Cryptography.X509Certificates.X509Certificate2 _certificate;
            private readonly System.Threading.CancellationTokenSource _cts = new();
            public System.Collections.Concurrent.ConcurrentQueue<string> Versions { get; } = new();

            public FakeAuthServer()
            {
                using var rsa = RSA.Create(2048);
                var request = new System.Security.Cryptography.X509Certificates.CertificateRequest("CN=localhost", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                using var ephemeral = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
                _certificate = System.Security.Cryptography.X509Certificates.X509CertificateLoader.LoadPkcs12(
                    ephemeral.Export(System.Security.Cryptography.X509Certificates.X509ContentType.Pfx), null);
                _listener.Start();
                _ = ServeAsync();
            }

            public int Port => ((System.Net.IPEndPoint)_listener.LocalEndpoint).Port;

            /// <summary>The self-signed certificate it presents, new for each server.</summary>
            public System.Security.Cryptography.X509Certificates.X509Certificate2 Certificate => _certificate;

            private async Task ServeAsync()
            {
                while (!_cts.IsCancellationRequested)
                {
                    try
                    {
                        using var client = await _listener.AcceptTcpClientAsync(_cts.Token);
                        await AnswerAsync(client);
                    }
                    catch (Exception) when (!_cts.IsCancellationRequested)
                    {
                        // The client refused the certificate (with TLS 1.3 that can surface on the first read); it may
                        // connect again once the user trusts it.
                    }
                    catch (Exception)
                    {
                        return;
                    }
                }
            }

            private async Task AnswerAsync(System.Net.Sockets.TcpClient client)
            {
                using var ssl = new System.Net.Security.SslStream(client.GetStream());
                await ssl.AuthenticateAsServerAsync(_certificate);
                var buffer = new byte[4096];
                int n = await ssl.ReadAsync(buffer, _cts.Token);
                if (n <= 0) return;
                using var request = System.Text.Json.JsonDocument.Parse(buffer.AsMemory(0, n));
                var v = request.RootElement.GetProperty("version").EnumerateArray().Select(e => e.GetInt32()).ToArray();
                Versions.Enqueue($"{v[0]}.{v[1]}.{v[2]}");
                string reply = v[0] == 2 && v[1] == 1
                    ? "{\"result\":1,\"account_id\":7,\"session_hash\":[1,2,3,4,5,6,7,8,9,10,11,12,13,14,15,16]}"
                    : System.Text.Json.JsonSerializer.Serialize(new { error_message = $"Unsupported xiloader version {v[0]}.{v[1]}.{v[2]}.\nThis server requires version 2.1.x." });
                await ssl.WriteAsync(Encoding.UTF8.GetBytes(reply), _cts.Token);
                await ssl.FlushAsync(_cts.Token);
            }

            public void Dispose()
            {
                _cts.Cancel();
                _listener.Stop();
                _certificate.Dispose();
            }
        }

        [Fact]
        public async Task VersionFallback_RetriesWithTheRequiredVersionAndRemembersIt()
        {
            using var server = new FakeAuthServer();
            // The stand-in is self-signed: the user trusts it on first use (#445).
            var client = new LsbLoginClient
            {
                CertificateStore = ServerCertificateTrustTests.ScratchStore(),
                ConfirmServerCertificate = (_, _) => Task.FromResult(true),
            };
            var (account, hash) = await client.AuthenticateAsync("127.0.0.1", server.Port, "user", "pass", ct: TestContext.Current.CancellationToken);
            Assert.Equal(7u, account);
            Assert.Equal(Hash, hash);
            Assert.Equal(new[] { "2.2.0", "2.1.0" }, server.Versions.ToArray());
            Assert.Equal(((byte)2, (byte)1, (byte)0), LsbLoginClient.XiloaderVersionFor("127.0.0.1", server.Port));

            // The next login to the same server reports the version it accepted straight away.
            await client.AuthenticateAsync("127.0.0.1", server.Port, "user", "pass", ct: TestContext.Current.CancellationToken);
            Assert.Equal(new[] { "2.2.0", "2.1.0", "2.1.0" }, server.Versions.ToArray());
        }

        [Fact]
        public void VersionFallback_DefaultIsTheCurrentLandSandBoatVersion()
        {
            Assert.Equal(((byte)2, (byte)2, (byte)0), LsbLoginClient.DefaultXiloaderVersion);
            Assert.Equal(LsbLoginClient.DefaultXiloaderVersion, LsbLoginClient.XiloaderVersionFor("never.contacted.invalid", 1));
        }
    }
}
