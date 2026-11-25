// tests/Gordian.Core.Tests/Network/LsbLobbyTlsTests.cs
using System;
using System.Linq;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Gordian.Core.Network.LandSandBoat;
using Gordian.Core.Network.Lobby;
using Xunit;

namespace Gordian.Core.Tests.Network
{
    /// <summary>
    /// LandSandBoat's TLS lobby (xiloader 3.0, LandSandBoat 778b74294a, #656): the data and view channels over TLS 1.3
    /// with the auth server's key, against the loopback lobby stand-in.
    /// </summary>
    public class LsbLobbyTlsTests
    {
        private static readonly byte[] Hash = Enumerable.Range(1, 16).Select(i => (byte)i).ToArray();
        private const uint Account = 1000;

        private static X509Certificate2 SelfSigned(RSA? key = null)
        {
            using var owned = key == null ? RSA.Create(2048) : null;
            var rsa = key ?? owned!;
            var request = new CertificateRequest("CN=localhost", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            using var ephemeral = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
            return X509CertificateLoader.LoadPkcs12(ephemeral.Export(X509ContentType.Pfx), null);
        }

        private static FakeLsbLobbyServer Server(X509Certificate2? certificate)
        {
            var server = new FakeLsbLobbyServer(Account, Hash, certificate);
            server.Characters.Add(new FakeLsbLobbyServer.Character { Id = 21828, Name = "Knot", Race = 2, Face = 5, Job = 4, Nation = 1, Zone = 236 });
            server.Characters.Add(new FakeLsbLobbyServer.Character { Id = 21900, Name = "Blm", Race = 7, Face = 14, Job = 6, Size = 2, Zone = 0x101 });
            return server;
        }

        [Theory]
        [InlineData(3, 0, LsbLobbyTransport.Auto, true)]
        [InlineData(4, 1, LsbLobbyTransport.Auto, true)]
        [InlineData(2, 2, LsbLobbyTransport.Auto, false)]
        [InlineData(2, 1, LsbLobbyTransport.Auto, false)]
        [InlineData(2, 2, LsbLobbyTransport.Tls, true)]
        [InlineData(3, 0, LsbLobbyTransport.Plain, false)]
        public void UsesTls_FollowsTheAcceptedVersionUnlessOverridden(int major, int minor, LsbLobbyTransport transport, bool expected)
        {
            Assert.Equal(expected, LsbLobbyTls.UsesTls(((byte)major, (byte)minor, (byte)0), transport));
        }

        [Fact]
        public void KeyFingerprint_IsXiloadersSha256OfThePublicKey()
        {
            using var rsa = RSA.Create(2048);
            using var first = SelfSigned(rsa);
            using var renewed = SelfSigned(rsa);
            using var other = SelfSigned();

            string fingerprint = LsbLobbyTls.KeyFingerprint(first);
            string expected = "SHA256:" + Convert.ToBase64String(SHA256.HashData(first.PublicKey.ExportSubjectPublicKeyInfo())).TrimEnd('=');
            Assert.Equal(expected, fingerprint);
            Assert.Equal(50, fingerprint.Length); // "SHA256:" + 43 base64 characters, padding dropped
            // It names the key: a renewed certificate for the same key keeps it, another key does not.
            Assert.Equal(fingerprint, LsbLobbyTls.KeyFingerprint(renewed));
            Assert.NotEqual(fingerprint, LsbLobbyTls.KeyFingerprint(other));
        }

        [Fact]
        public async Task TlsLobby_SelectsACharacterOverBothChannels()
        {
            using var certificate = SelfSigned();
            using var server = Server(certificate);
            var client = new LsbLoginClient();
            var ticket = await client.SelectCharacterAsync("127.0.0.1", server.DataPort, server.ViewPort, Account, Hash,
                targetCharacterSlot: 2, ct: TestContext.Current.CancellationToken, lobbyServerKey: LsbLobbyTls.KeyFingerprint(certificate));
            Assert.Equal("Blm", ticket.CharacterName);
            Assert.Equal(0, server.FailedHandshakes);
            Assert.Contains((byte)0xA2, server.DataCommands);
        }

        [Fact]
        public async Task TlsLobby_RefusesAServerWithAnotherKey()
        {
            using var certificate = SelfSigned();
            using var impostor = SelfSigned();
            using var server = Server(impostor);
            var client = new LsbLoginClient();
            var ex = await Assert.ThrowsAsync<LobbyServerKeyMismatchException>(() =>
                client.OpenLobbyAsync("127.0.0.1", server.DataPort, server.ViewPort, Account, Hash,
                    ct: TestContext.Current.CancellationToken, lobbyServerKey: LsbLobbyTls.KeyFingerprint(certificate)));
            Assert.Contains(LsbLobbyTls.KeyFingerprint(impostor), ex.Message);
            // Nothing reached the lobby: the view channel is checked before 0x26 is sent.
            Assert.Empty(server.ViewCommands);
        }

        [Fact]
        public async Task PlainClient_FailsAgainstATlsLobby()
        {
            // What GordianXI did before #656 against a current server: LandSandBoat logs "wrong version number".
            using var certificate = SelfSigned();
            using var server = Server(certificate);
            await Assert.ThrowsAnyAsync<Exception>(() =>
                LobbySession.ConnectAsync("127.0.0.1", server.ViewPort,
                    new XiloaderLobbyHandoff("127.0.0.1", server.DataPort, Account, Hash, timeout: TimeSpan.FromSeconds(2)),
                    timeout: TimeSpan.FromSeconds(2), ct: TestContext.Current.CancellationToken));
            Assert.True(server.FailedHandshakes > 0);
            Assert.Empty(server.ViewCommands);
        }

        [Fact]
        public async Task TlsClient_AgainstAPlainLobby_SuggestsThePlainSetting()
        {
            using var certificate = SelfSigned();
            using var server = Server(null);
            using var tcp = new TcpClient();
            await tcp.ConnectAsync("127.0.0.1", server.ViewPort, TestContext.Current.CancellationToken);
            var ex = await Assert.ThrowsAnyAsync<Exception>(() => LsbLobbyTls.AuthenticateAsync(tcp.GetStream(), "127.0.0.1", server.ViewPort, "view",
                LsbLobbyTls.KeyFingerprint(certificate), TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken));
            Assert.Contains("Plain", ex.Message);
        }
    }
}
