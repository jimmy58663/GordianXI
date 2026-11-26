// tests/Gordian.Core.Tests/Network/LsbAccountTests.cs
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Gordian.Core.Network.LandSandBoat;
using Gordian.Core.Profiles;
using Xunit;

namespace Gordian.Core.Tests.Network
{
    /// <summary>
    /// LSB account management over xi_connect (#652): create, password change and two-factor, against a loopback
    /// stand-in written from LandSandBoat's documented replies (src/login/auth_session.cpp).
    /// </summary>
    public class LsbAccountTests
    {
        private const string Secret = "JBSWY3DPEHPK3PXPJBSWY3DPEHPK3PXP";

        /// <summary>
        /// A loopback xi_connect with in-memory accounts that answers the account commands as LandSandBoat does:
        /// result codes for create and password change, <c>error_message</c> "Failed to validate credentials" for the
        /// two-factor commands, and nothing at all for a malformed name or password. Requires xiloader 3.0.
        /// </summary>
        internal sealed class FakeAccountServer : IDisposable
        {
            private sealed class Account
            {
                public string Password = string.Empty;
                public string? TotpSecret;
                public bool TotpValidated;
                public string RecoveryCode = "RECOVERYCODE1234";
            }

            private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
            private readonly X509Certificate2 _certificate;
            private readonly CancellationTokenSource _cts = new();
            private readonly Dictionary<string, Account> _accounts = new(StringComparer.Ordinal);
            public ConcurrentQueue<JsonElement> Requests { get; } = new();
            public bool CreationEnabled { get; set; } = true;
            public string ServerName { get; set; } = "Gordian Test";

            public FakeAccountServer()
            {
                using var rsa = RSA.Create(2048);
                var request = new CertificateRequest("CN=localhost", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                using var ephemeral = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
                _certificate = X509CertificateLoader.LoadPkcs12(ephemeral.Export(X509ContentType.Pfx), null);
                _listener.Start();
                _ = ServeAsync();
            }

            public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

            public void AddAccount(string name, string password, string? totpSecret = null)
            {
                lock (_accounts) _accounts[name] = new Account { Password = password, TotpSecret = totpSecret, TotpValidated = totpSecret != null };
            }

            public string? PasswordOf(string name) { lock (_accounts) return _accounts.TryGetValue(name, out var a) ? a.Password : null; }
            public bool HasTotp(string name) { lock (_accounts) return _accounts.TryGetValue(name, out var a) && a.TotpValidated; }

            private async Task ServeAsync()
            {
                while (!_cts.IsCancellationRequested)
                {
                    try
                    {
                        using var client = await _listener.AcceptTcpClientAsync(_cts.Token);
                        await AnswerAsync(client);
                    }
                    catch (Exception) when (!_cts.IsCancellationRequested) { }
                    catch (Exception) { return; }
                }
            }

            private async Task AnswerAsync(TcpClient client)
            {
                using var ssl = new SslStream(client.GetStream());
                await ssl.AuthenticateAsServerAsync(_certificate);
                var buffer = new byte[4096];
                int n = await ssl.ReadAsync(buffer, _cts.Token);
                if (n <= 0) return;
                using var doc = JsonDocument.Parse(buffer.AsMemory(0, n));
                var root = doc.RootElement.Clone();
                Requests.Enqueue(root);
                string? reply = Handle(root);
                if (reply == null)
                {
                    // LandSandBoat sends nothing for a malformed name or password; hold until the client gives up.
                    await Task.Delay(Timeout.Infinite, _cts.Token);
                    return;
                }
                await ssl.WriteAsync(Encoding.UTF8.GetBytes(reply), _cts.Token);
                await ssl.FlushAsync(_cts.Token);
            }

            private static string Result(int result, object? extra = null)
            {
                var fields = new Dictionary<string, object> { ["result"] = result };
                if (extra != null) foreach (var p in extra.GetType().GetProperties()) fields[p.Name] = p.GetValue(extra)!;
                return JsonSerializer.Serialize(fields);
            }

            private static string Error(string message) => JsonSerializer.Serialize(new { error_message = message });

            private string? Handle(JsonElement request)
            {
                var version = request.GetProperty("version");
                if (version[0].GetInt32() != 3 || version[1].GetInt32() != 0)
                    return Error($"Unsupported xiloader version {version[0]}.{version[1]}.{version[2]}.\nThis server requires version 3.0.x.");
                int command = request.GetProperty("command").GetInt32();
                string user = request.GetProperty("username").GetString()!, password = request.GetProperty("password").GetString()!;
                string newPassword = request.GetProperty("new_password").GetString()!, otp = request.GetProperty("otp").GetString()!;
                if (user.Length is 0 or > 16 || password.Length is 0 or > 32) return null;

                lock (_accounts)
                {
                    _accounts.TryGetValue(user, out var account);
                    bool valid = account != null && account.Password == password;
                    bool CodeOk(string code) => account?.TotpSecret != null && code == TotpEngine.GenerateCurrentCode(account.TotpSecret);
                    switch (command)
                    {
                        case 0x20:
                            if (!CreationEnabled) return Result(0x08);
                            if (account != null) return Result(0x04);
                            _accounts[user] = new Account { Password = password };
                            return Result(0x03);
                        case 0x30:
                            if (!valid || (account!.TotpValidated && !CodeOk(otp)) || newPassword.Length == 0) return Result(0x07);
                            account.Password = newPassword;
                            return Result(0x06, new { account_id = 0, session_hash = "" });
                        case 0x31:
                            if (!valid || account!.TotpValidated) return Error("Failed to validate credentials");
                            account.TotpSecret = Secret;
                            // Unescaped, as LandSandBoat formats it.
                            return Result(0x10, new { TOTP_uri = $"otpauth://totp/{ServerName}:{user}?secret={Secret}&issuer={ServerName}&algorithm=SHA1&digits=6&period=30" });
                        case 0x34:
                            if (!valid || !CodeOk(otp)) return Error("Failed to validate credentials");
                            account!.TotpValidated = true;
                            return Result(0x11, new { recovery_code = account.RecoveryCode });
                        case 0x32:
                            if (!valid || !(CodeOk(otp) || string.Equals(otp, account!.RecoveryCode, StringComparison.OrdinalIgnoreCase))) return Error("Failed to validate credentials");
                            account.TotpSecret = null;
                            account.TotpValidated = false;
                            return Result(0x12);
                        case 0x33:
                            if (!valid || !(CodeOk(otp) || string.Equals(otp, account!.RecoveryCode, StringComparison.OrdinalIgnoreCase))) return Error("Failed to validate credentials");
                            account.RecoveryCode = "NEWRECOVERY56789";
                            return Result(0x11, new { recovery_code = account.RecoveryCode });
                        default:
                            return Error("Unhandled");
                    }
                }
            }

            public void Dispose()
            {
                _cts.Cancel();
                _listener.Stop();
                _certificate.Dispose();
            }
        }

        private static LsbLoginClient Client() => new()
        {
            CertificateStore = ServerCertificateTrustTests.ScratchStore(),
            ConfirmServerCertificate = (_, _) => Task.FromResult(true),
        };

        private static CancellationToken Ct => TestContext.Current.CancellationToken;

        [Fact]
        public async Task CreateAccount_SendsCommand0x20AndMapsTheReplies()
        {
            using var server = new FakeAccountServer();
            var client = Client();
            await client.CreateAccountAsync("127.0.0.1", server.Port, "newbie", "secret12", Ct);
            Assert.Equal("secret12", server.PasswordOf("newbie"));
            Assert.True(server.Requests.TryPeek(out var request));
            Assert.Equal(0x20, request.GetProperty("command").GetInt32());
            Assert.Equal("", request.GetProperty("new_password").GetString());

            var taken = await Assert.ThrowsAsync<LsbAccountException>(() => client.CreateAccountAsync("127.0.0.1", server.Port, "newbie", "secret12", Ct));
            Assert.Equal(LsbAccountError.NameTaken, taken.Error);

            server.CreationEnabled = false;
            var disabled = await Assert.ThrowsAsync<LsbAccountException>(() => client.CreateAccountAsync("127.0.0.1", server.Port, "other", "secret12", Ct));
            Assert.Equal(LsbAccountError.CreationDisabled, disabled.Error);
        }

        [Theory]
        [InlineData("", "secret12")]
        [InlineData("seventeen_chars_x", "secret12")]
        [InlineData("näme", "secret12")]
        [InlineData("name", "short")]
        [InlineData("name", "thirty-three-characters-password!")]
        [InlineData("name", "tab\there")]
        public async Task CreateAccount_RefusesWhatTheServerWouldDropWithoutSendingIt(string name, string password)
        {
            using var server = new FakeAccountServer();
            var ex = await Assert.ThrowsAsync<LsbAccountException>(() => Client().CreateAccountAsync("127.0.0.1", server.Port, name, password, Ct));
            Assert.Equal(LsbAccountError.InvalidInput, ex.Error);
            Assert.Empty(server.Requests);
        }

        [Fact]
        public async Task ChangePassword_SendsTheNewPasswordAndTheCode()
        {
            using var server = new FakeAccountServer();
            server.AddAccount("knot", "oldpass1", Secret);
            var client = Client();

            var refused = await Assert.ThrowsAsync<LsbAccountException>(() =>
                client.ChangePasswordAsync("127.0.0.1", server.Port, "knot", "oldpass1", "newpass1", "000000", Ct));
            Assert.Equal(LsbAccountError.CredentialsRejected, refused.Error);
            Assert.Equal("oldpass1", server.PasswordOf("knot"));

            await client.ChangePasswordAsync("127.0.0.1", server.Port, "knot", "oldpass1", "newpass1", TotpEngine.GenerateCurrentCode(Secret), Ct);
            Assert.Equal("newpass1", server.PasswordOf("knot"));
        }

        [Fact]
        public async Task TwoFactor_SetupVerifyRegenerateAndRemove()
        {
            using var server = new FakeAccountServer { ServerName = "My Server" };
            server.AddAccount("knot", "password1");
            var client = Client();

            var setup = await client.BeginTotpSetupAsync("127.0.0.1", server.Port, "knot", "password1", Ct);
            Assert.Equal(Secret, setup.Secret);
            Assert.Equal("My Server", setup.Issuer);
            Assert.Equal("knot", setup.AccountName);
            // Rebuilt with the label and issuer escaped (LandSandBoat leaves the space in).
            Assert.Equal($"otpauth://totp/My%20Server:knot?secret={Secret}&issuer=My%20Server&algorithm=SHA1&digits=6&period=30", setup.OtpAuthUri);
            Assert.False(server.HasTotp("knot"));

            var wrong = await Assert.ThrowsAsync<LsbAccountException>(() => client.ConfirmTotpSetupAsync("127.0.0.1", server.Port, "knot", "password1", "000000", Ct));
            Assert.Equal(LsbAccountError.CredentialsRejected, wrong.Error);

            string recovery = await client.ConfirmTotpSetupAsync("127.0.0.1", server.Port, "knot", "password1", TotpEngine.GenerateCurrentCode(setup.Secret), Ct);
            Assert.Equal("RECOVERYCODE1234", recovery);
            Assert.True(server.HasTotp("knot"));

            // Already on: setting it up again is refused.
            await Assert.ThrowsAsync<LsbAccountException>(() => client.BeginTotpSetupAsync("127.0.0.1", server.Port, "knot", "password1", Ct));

            string next = await client.RegenerateRecoveryCodeAsync("127.0.0.1", server.Port, "knot", "password1", "recoverycode1234", Ct);
            Assert.Equal("NEWRECOVERY56789", next);

            await client.RemoveTotpAsync("127.0.0.1", server.Port, "knot", "password1", TotpEngine.GenerateCurrentCode(Secret), Ct);
            Assert.False(server.HasTotp("knot"));
        }

        [Fact]
        public async Task AccountCommands_NegotiateTheVersionLikeTheLogin()
        {
            using var server = new FakeAccountServer();
            await Client().CreateAccountAsync("127.0.0.1", server.Port, "versioned", "secret12", Ct);
            Assert.Equal(((byte)3, (byte)0, (byte)0), LsbLoginClient.XiloaderVersionFor("127.0.0.1", server.Port));
        }

        [Theory]
        [InlineData("otpauth://totp/Srv:me?secret=JBSWY3DPEHPK3PXP&issuer=Srv&algorithm=SHA1&digits=6&period=30", "JBSWY3DPEHPK3PXP", "Srv", "me")]
        [InlineData("otpauth://totp/A B:me?secret=jbswy3dpehpk3pxp&issuer=A B", "JBSWY3DPEHPK3PXP", "A B", "me")]
        [InlineData("otpauth://totp/me?secret=JBSWY3DPEHPK3PXP", "JBSWY3DPEHPK3PXP", "LandSandBoat", "me")]
        public void ParseOtpAuthUri_ReadsLandSandBoatsLink(string uri, string secret, string issuer, string account)
        {
            var setup = LsbLoginClient.ParseOtpAuthUri(uri, "fallback");
            Assert.NotNull(setup);
            Assert.Equal((secret, issuer, account), (setup!.Secret, setup.Issuer, setup.AccountName));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("otpauth://totp/Srv:me?issuer=Srv")]
        [InlineData("otpauth://totp/Srv:me?secret=not-base32!")]
        public void ParseOtpAuthUri_RejectsALinkWithoutAKey(string? uri)
        {
            Assert.Null(LsbLoginClient.ParseOtpAuthUri(uri, "me"));
        }
    }
}
