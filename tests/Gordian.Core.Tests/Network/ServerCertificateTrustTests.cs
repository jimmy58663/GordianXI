// tests/Gordian.Core.Tests/Network/ServerCertificateTrustTests.cs
using System;
using System.IO;
using System.Net.Security;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Gordian.Core.Network.LandSandBoat;
using Xunit;

namespace Gordian.Core.Tests.Network
{
    /// <summary>
    /// Trust on first use for LandSandBoat auth server certificates (#445, docs/network/session-and-packets.md, "Auth
    /// handshake"): the decision, the fingerprint, the pin store, and that no credentials leave before the server is trusted.
    /// </summary>
    public class ServerCertificateTrustTests
    {
        private const string Fp = "AA:BB";

        /// <summary>A pin store in a fresh scratch file, so tests never touch the user's <c>known_servers.json</c>.</summary>
        internal static KnownServerCertificateStore ScratchStore() => new(ScratchPath());

        private static string ScratchPath() => Path.Combine(Path.GetTempPath(), "gordian-tests", $"known_servers_{Guid.NewGuid():N}.json");

        [Theory]
        [InlineData(SslPolicyErrors.None, null, CertificateTrustDecision.Trusted)]
        [InlineData(SslPolicyErrors.None, "CC:DD", CertificateTrustDecision.Trusted)]
        [InlineData(SslPolicyErrors.RemoteCertificateChainErrors, null, CertificateTrustDecision.Unknown)]
        [InlineData(SslPolicyErrors.RemoteCertificateChainErrors, Fp, CertificateTrustDecision.Trusted)]
        [InlineData(SslPolicyErrors.RemoteCertificateChainErrors, "aa:bb", CertificateTrustDecision.Trusted)]
        [InlineData(SslPolicyErrors.RemoteCertificateNameMismatch, "CC:DD", CertificateTrustDecision.Changed)]
        public void Evaluate_TrustsVerifiedOrPinnedCertificatesOnly(SslPolicyErrors errors, string? pinned, CertificateTrustDecision expected)
        {
            Assert.Equal(expected, ServerCertificateTrust.Evaluate(errors, Fp, pinned));
        }

        [Fact]
        public void Fingerprint_IsTheColonSeparatedSha256OfTheCertificate()
        {
            using var server = new LsbSessionKeysTests.FakeAuthServer();
            string fingerprint = ServerCertificateTrust.Fingerprint(server.Certificate);
            Assert.Equal(Convert.ToHexString(SHA256.HashData(server.Certificate.RawData)), fingerprint.Replace(":", ""));
            Assert.Equal(32, fingerprint.Split(':').Length);
        }

        [Fact]
        public void Store_PersistsOnePinPerServer()
        {
            string path = ScratchPath();
            var store = new KnownServerCertificateStore(path);
            Assert.Null(store.Get("play.example.net", 54231));
            store.Set("Play.Example.NET", 54231, Fp);
            store.Set("play.example.net", 54232, "CC:DD");

            // Another store on the same file (the next launch) reads the pins back; host names ignore case.
            var reloaded = new KnownServerCertificateStore(path);
            Assert.Equal(Fp, reloaded.Get("play.example.net", 54231));
            Assert.Equal("CC:DD", reloaded.Get("PLAY.example.net", 54232));
            Assert.Null(reloaded.Get("other.example.net", 54231));

            reloaded.Set("play.example.net", 54231, "EE:FF");
            Assert.Equal("EE:FF", new KnownServerCertificateStore(path).Get("play.example.net", 54231));
        }

        [Fact]
        public void Store_AnUnreadableFileTrustsNothing()
        {
            string path = ScratchPath();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "{ not json");
            Assert.Null(new KnownServerCertificateStore(path).Get("127.0.0.1", 54231));
        }

        /// <summary>Writes the pin file as a user editing it by hand would, with a later last-write time.</summary>
        private static void EditByHand(string path, string json)
        {
            File.WriteAllText(path, json);
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(1));
        }

        [Fact]
        public void Store_SeesHandEditsMadeWhileRunning()
        {
            string path = ScratchPath();
            var store = new KnownServerCertificateStore(path);
            store.Set("127.0.0.1", 54231, Fp);
            Assert.Equal(Fp, store.Get("127.0.0.1", 54231));

            // A changed fingerprint is seen at the next lookup, without a restart (#450).
            EditByHand(path, "{ \"127.0.0.1:54231\": \"CC:DD\" }");
            Assert.Equal("CC:DD", store.Get("127.0.0.1", 54231));

            // Deleting the file forgets every server.
            File.Delete(path);
            Assert.Null(store.Get("127.0.0.1", 54231));
        }

        [Fact]
        public void Store_AnApprovalKeepsHandEdits()
        {
            string path = ScratchPath();
            var store = new KnownServerCertificateStore(path);
            store.Set("a.example.net", 54231, Fp);
            EditByHand(path, "{ \"a.example.net:54231\": \"AA:BB\", \"b.example.net:54231\": \"CC:DD\" }");

            store.Set("c.example.net", 54231, "EE:FF");

            var reloaded = new KnownServerCertificateStore(path);
            Assert.Equal("CC:DD", reloaded.Get("b.example.net", 54231));
            Assert.Equal("EE:FF", reloaded.Get("c.example.net", 54231));
        }

        [Fact]
        public async Task Login_WithoutAPromptRefusesAnUntrustedServerBeforeSendingCredentials()
        {
            using var server = new LsbSessionKeysTests.FakeAuthServer();
            var store = ScratchStore();
            var client = new LsbLoginClient { CertificateStore = store };

            var ex = await Assert.ThrowsAsync<UntrustedServerCertificateException>(() =>
                client.AuthenticateAsync("127.0.0.1", server.Port, "user", "pass", ct: TestContext.Current.CancellationToken));
            Assert.Equal(ServerCertificateTrust.Fingerprint(server.Certificate), ex.Certificate.Sha256Fingerprint);
            Assert.False(ex.Certificate.IsChanged);
            Assert.Empty(server.Versions);
            Assert.Null(store.Get("127.0.0.1", server.Port));
        }

        [Fact]
        public async Task Login_TrustedOnFirstUseIsPinnedAndNotAskedAgain()
        {
            using var server = new LsbSessionKeysTests.FakeAuthServer();
            var store = ScratchStore();
            int asked = 0;
            ServerCertificateInfo? shown = null;
            var client = new LsbLoginClient
            {
                CertificateStore = store,
                ConfirmServerCertificate = (info, _) => { asked++; shown = info; return Task.FromResult(true); },
            };

            var (account, _) = await client.AuthenticateAsync("127.0.0.1", server.Port, "user", "pass", ct: TestContext.Current.CancellationToken);
            Assert.Equal(7u, account);
            Assert.Equal(1, asked);
            string fingerprint = ServerCertificateTrust.Fingerprint(server.Certificate);
            Assert.Equal(fingerprint, shown!.Sha256Fingerprint);
            Assert.Equal("127.0.0.1", shown.Host);
            Assert.Equal(server.Port, shown.Port);
            Assert.Equal(fingerprint, store.Get("127.0.0.1", server.Port));

            await client.AuthenticateAsync("127.0.0.1", server.Port, "user", "pass", ct: TestContext.Current.CancellationToken);
            Assert.Equal(1, asked);
        }

        [Fact]
        public async Task Login_RefusedPromptSendsNothingAndPinsNothing()
        {
            using var server = new LsbSessionKeysTests.FakeAuthServer();
            var store = ScratchStore();
            var client = new LsbLoginClient { CertificateStore = store, ConfirmServerCertificate = (_, _) => Task.FromResult(false) };

            await Assert.ThrowsAsync<UntrustedServerCertificateException>(() =>
                client.AuthenticateAsync("127.0.0.1", server.Port, "user", "pass", ct: TestContext.Current.CancellationToken));
            Assert.Empty(server.Versions);
            Assert.Null(store.Get("127.0.0.1", server.Port));
        }

        [Fact]
        public async Task Login_AChangedCertificateIsShownWithTheOldPin()
        {
            using var server = new LsbSessionKeysTests.FakeAuthServer();
            var store = ScratchStore();
            store.Set("127.0.0.1", server.Port, Fp);
            ServerCertificateInfo? shown = null;
            var client = new LsbLoginClient
            {
                CertificateStore = store,
                ConfirmServerCertificate = (info, _) => { shown = info; return Task.FromResult(false); },
            };

            var ex = await Assert.ThrowsAsync<UntrustedServerCertificateException>(() =>
                client.AuthenticateAsync("127.0.0.1", server.Port, "user", "pass", ct: TestContext.Current.CancellationToken));
            Assert.True(shown!.IsChanged);
            Assert.Equal(Fp, shown.PreviousFingerprint);
            Assert.Contains("changed", ex.Message);
            Assert.Empty(server.Versions);
            Assert.Equal(Fp, store.Get("127.0.0.1", server.Port));
        }
    }
}
