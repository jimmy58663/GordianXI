// tests/Gordian.Core.Tests/Network/Retail/RetailProtocolTests.cs
using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Gordian.Core.Network.LandSandBoat;
using Gordian.Core.Network.Retail;
using Xunit;

namespace Gordian.Core.Tests.Network.Retail
{
    /// <summary>Retail login frames and stage clients, the clients run against loopback fakes of the retail servers.</summary>
    public class RetailProtocolTests
    {
        private static readonly CancellationToken Ct = new CancellationTokenSource(TimeSpan.FromSeconds(20)).Token;

        // ----- POLP -----

        [Fact]
        public void Polp_VersionRequest_HasTheSpecLayout()
        {
            byte[] frame = new byte[PolpPackets.VersionRequestSize];
            PolpPackets.WriteVersionRequest(frame, PolpPackets.FfxiProduct, "000130261006_0");
            Assert.Equal(88u, BinaryPrimitives.ReadUInt32LittleEndian(frame));
            Assert.Equal("POLP", Encoding.ASCII.GetString(frame, 8, 4));
            Assert.Equal(7u, BinaryPrimitives.ReadUInt32LittleEndian(frame.AsSpan(12)));
            Assert.Equal("W20\0", Encoding.ASCII.GetString(frame, 0x10, 4));
            Assert.Equal("000130261006_0", PolpPackets.ReadAscii(frame.AsSpan(0x14, 68)));
            Assert.Equal(PolHashes.Crc32(frame.AsSpan(16)), BinaryPrimitives.ReadUInt32LittleEndian(frame.AsSpan(4)));
        }

        private static byte[] VersionResponse(uint status, string version)
        {
            byte[] frame = new byte[PolpPackets.VersionResponseSize];
            BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(0x14), status);
            PolpPackets.WriteAscii(frame.AsSpan(0x18, 11), "registered");
            PolpPackets.WriteAscii(frame.AsSpan(0x23, 16), "124.150.156.107");
            BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(0x58), 11);
            PolpPackets.WriteAscii(frame.AsSpan(0x5C, 11), version);
            PolpPackets.WriteHeader(frame, PolpPackets.TypeVersionResponse);
            return frame;
        }

        [Fact]
        public void Polp_VersionResponse_Parses()
        {
            byte[] frame = VersionResponse(1, "30261006_0");
            Assert.True(PolpPackets.TryParseVersionResponse(frame, out var r));
            Assert.True(r.IsUpToDate);
            Assert.True(r.ChecksumValid);
            Assert.Equal("registered", r.StatusText);
            Assert.Equal("124.150.156.107", r.ServerIp);
            Assert.Equal("30261006_0", r.CurrentVersion);

            frame[0x30] ^= 1;
            Assert.True(PolpPackets.TryParseVersionResponse(frame, out r));
            Assert.False(r.ChecksumValid);
            Assert.False(PolpPackets.TryParseVersionResponse(frame.AsSpan(0, 50), out _));
        }

        [Fact]
        public async Task Polp_Checker_SendsType7AndReadsType8()
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var server = Task.Run(async () =>
            {
                using var c = await listener.AcceptTcpClientAsync(Ct);
                var s = c.GetStream();
                byte[] req = new byte[88];
                await s.ReadExactlyAsync(req, Ct);
                Assert.Equal("100020110829_E", PolpPackets.ReadAscii(req.AsSpan(0x14, 68)));
                await s.WriteAsync(VersionResponse(1, "20110829_E"), Ct);
            });
            var result = await PolpVersionChecker.CheckAsync("127.0.0.1", port, PolpPackets.ViewerProduct, "100020110829_E", Ct);
            await server;
            Assert.True(result.IsUpToDate);
            Assert.Equal("20110829_E", result.CurrentVersion);
        }

        [Fact]
        public void PatchVer_ReadsTheNullTerminatedVersion()
        {
            string dir = Path.Combine(Path.GetTempPath(), "GordianXI_PatchVer_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                Assert.Null(PatchVersionFile.Read(dir));
                byte[] file = new byte[0x120];
                Encoding.ASCII.GetBytes("000130261006_0").CopyTo(file, 0);
                file[0x40] = 0x7F; // junk after the terminator
                File.WriteAllBytes(Path.Combine(dir, PatchVersionFile.FileName), file);
                Assert.Equal("000130261006_0", PatchVersionFile.Read(dir));
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        // ----- Core IRC -----

        [Fact]
        public void Core_ParsesNumericsAndTokens()
        {
            Assert.True(PolCoreProtocol.TryParseNumeric(":202.67.54.132 001 ABC", out var src, out int n, out var rest));
            Assert.Equal(("202.67.54.132", 1, "ABC"), (src, n, rest));
            Assert.False(PolCoreProtocol.TryParseNumeric("NOTICE x", out _, out _, out _));

            byte[] data = new byte[24];
            BinaryPrimitives.WriteUInt32LittleEndian(data, 1731110400);
            BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(4), 51242);
            string token = PolBase32.Encode(data, 41) + "pYme";
            Assert.Equal(45, token.Length);
            Assert.Equal(token, PolCoreProtocol.ReadGreetingToken($":202.67.54.52 300 * {token}", out var ip));
            Assert.Equal("202.67.54.52", ip);
            Assert.Equal((1731110400u, (ushort)51242, "pYme"), PolCoreProtocol.ParseRedirectToken(token));
        }

        [Fact]
        public void Core_UserLineCarriesThePublicValueTheServerCanRead()
        {
            var dh = new PolDiffieHellman();
            string line = PolCoreProtocol.BuildUserLine(dh.PublicValue);
            Assert.Equal(61, line.Length);
            Assert.StartsWith("USER x 8 * :", line);
            Assert.EndsWith("\r\n", line);
            string token = line["USER x 8 * :".Length..^2];
            Assert.Equal(PolCoreProtocol.UserTokenChars, token.Length);
            Assert.All(token, c => Assert.Contains(c, PolCoreBase64.Alphabet));
            var (mode, pub) = PolCoreProtocol.ParseUserToken(token);
            Assert.Equal(0x02, mode);
            Assert.Equal(dh.PublicValue, pub);
        }

        // Spec v1.5.0 test vector 9: the USER token of retail capture 3 and the 35 bytes it decodes to.
        private const string CaptureUserToken = "rYsUyi_rIJqPf66Pi9yBcUS4Ag18XONjfOrkmWZ@lIAKuIP";
        private const string CaptureUserBytes = "95C6216A8DE51384AADBDF6AA1169B1A106980FE43FCEF2CD8E94D5875EBEC480BF84A";

        [Fact]
        public void Core_CapturedUserTokenDecodesToTheSpecBytes()
        {
            Assert.Equal(61, $"USER x 8 * :{CaptureUserToken}\r\n".Length);
            byte[] data = new byte[PolCoreProtocol.UserTokenBytes];
            Assert.True(PolCoreBase64.TryDecode(CaptureUserToken, data));
            Assert.Equal(Convert.FromHexString(CaptureUserBytes), data);

            // Encoding the bytes back gives the same first 46 characters. The capture's last character carries 2 spare
            // bits of 10, which the encoder writes as zeros (an open question), so it ends in 'i' rather than 'P'.
            string encoded = PolCoreBase64.Encode(data);
            Assert.Equal(PolCoreProtocol.UserTokenChars, encoded.Length);
            Assert.Equal(CaptureUserToken[..46], encoded[..46]);
            Assert.Equal('i', encoded[46]);
        }

        [Fact]
        public void Core_Base64HandlesPartialGroups()
        {
            byte[] data = { 0xFF, 0x00, 0x81, 0x7E };
            string text = PolCoreBase64.Encode(data);
            Assert.Equal(6, text.Length);
            byte[] back = new byte[4];
            Assert.True(PolCoreBase64.TryDecode(text, back));
            Assert.Equal(data, back);
            Assert.False(PolCoreBase64.TryDecode(text.AsSpan(0, 5), back));
            Assert.False(PolCoreBase64.TryDecode("TTTTT+", back));
        }

        [Fact]
        public void Core_NickLine_EndsWithFourTabs_AndIs86BytesWithRealMetadata()
        {
            Assert.Equal("NICK 232JR2NC1:5f4dcc3b5aa765d61d8327deb882cf99:META\t\t\t\t\r\n", PolCoreProtocol.BuildNickLine("TEST0001", "password", "META"));
            Assert.Equal(86, PolCoreProtocol.BuildNickLine("TEST0001", "password", PolClientMeta.Create()).Length);
        }

        [Fact]
        public void Core_ClientMetaMatchesTheSpecPrefixes()
        {
            byte[] meta = new byte[PolClientMeta.Length];
            PolClientMeta.Write(meta, new byte[18]);
            Assert.Equal(new byte[] { 0, 0, 0, 2, 1, 1 }, meta[..6]);
            string text = PolCoreBase64.Encode(meta);
            Assert.Equal(32, text.Length);
            Assert.StartsWith("TTTTTAIST", text); // the spec's observed prefix (v1.3.0 and v1.4.0 section 4.2.2)

            // 02 01 00 is 000000 100000 000100 000000 = TAIT (the spec's worked bits for it are off by one group).
            Assert.Equal("TTTTTAIT", PolCoreBase64.Encode(new byte[] { 0, 0, 0, 2, 1, 0 }));

            string created = PolClientMeta.Create();
            Assert.Equal(32, created.Length);
            Assert.StartsWith("TTTTTAIS", created);
            Assert.All(created, c => Assert.Contains(c, PolCoreBase64.Alphabet));
        }

        [Fact]
        public void Core_Base64AlphabetHas64DistinctCharacters() =>
            Assert.Equal(64, PolCoreBase64.Alphabet.Distinct().Count());

        private static readonly byte[] Routing = Convert.FromHexString("0ECC10D59F4C");
        private static readonly byte[] Account = Convert.FromHexString("A1A2A3A4A5A6A7A8A9AAABAC");
        private static readonly byte[] Secret = Convert.FromHexString("5152535455565758");

        private static string WelcomePayload(uint accountId)
        {
            byte[] p = new byte[30];
            Routing.CopyTo(p, 0);
            Account.CopyTo(p, 6);
            Secret.CopyTo(p, 18);
            BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(26), accountId);
            return PolBase32.Encode(p);
        }

        [Fact]
        public void Core_WelcomeDecodesTheTicket()
        {
            Assert.True(PolCoreProtocol.TryParseWelcome($":202.67.54.132 001 {WelcomePayload(137240103)}", out var t));
            Assert.Equal(Routing, t!.RoutingToken);
            Assert.Equal(Account, t.AccountToken);
            Assert.Equal(Secret, t.SessionSecret);
            Assert.Equal(137240103u, t.FfxiAccountId);
            Assert.False(PolCoreProtocol.TryParseWelcome(":x 422 TEST :tag", out _));
        }

        /// <summary>A loopback gatekeeper and session host, speaking the spec's stage 2 and 3.</summary>
        private sealed class FakeCore : IAsyncDisposable
        {
            private readonly TcpListener _gate = new(IPAddress.Loopback, 0);
            private readonly TcpListener _host = new(IPAddress.Loopback, 0);
            public int GatePort => ((IPEndPoint)_gate.LocalEndpoint).Port;
            public string? ReceivedNick;
            public PolStreamCipher? Cipher;
            public TcpClient? Connection;
            public Task Run { get; }

            public FakeCore(int refuseWith = 0)
            {
                _gate.Start();
                _host.Start();
                Run = Task.Run(() => Serve(refuseWith));
            }

            private static async Task Line(NetworkStream s, string text) => await s.WriteAsync(Encoding.ASCII.GetBytes(text + "\r\n"), Ct);

            private async Task Serve(int refuseWith)
            {
                using (var g = await _gate.AcceptTcpClientAsync(Ct))
                {
                    byte[] data = new byte[24];
                    BinaryPrimitives.WriteUInt32LittleEndian(data, 1731110400);
                    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(4), (ushort)((IPEndPoint)_host.LocalEndpoint).Port);
                    await Line(g.GetStream(), $":127.0.0.1 300 * {PolBase32.Encode(data, 41)}pYme");
                }

                Connection = await _host.AcceptTcpClientAsync(Ct);
                var s = Connection.GetStream();
                var reader = new LineReader(s);
                var dh = new PolDiffieHellman();
                byte[] greet = new byte[35];
                greet[0] = 2;
                dh.PublicValue.CopyTo(greet, 1);
                await Line(s, $":127.0.0.1 300 * {PolBase32.Encode(greet, 57)}pYme");

                string user = (await reader.ReadLineAsync(Ct))!;
                Assert.Equal(PolCoreProtocol.UserTokenChars, user.Length - "USER x 8 * :".Length);
                var (_, clientPublic) = PolCoreProtocol.ParseUserToken(user["USER x 8 * :".Length..]);
                Cipher = new PolStreamCipher(PolDiffieHellman.ExtractKey(dh.ComputeSharedSecret(clientPublic)));
                await Line(s, $":127.0.0.1 300 * {new string('A', 45)}");

                byte[] nick = (await reader.ReadRawLineAsync(Ct))!;
                Cipher.TransformFrame(nick);
                ReceivedNick = Encoding.ASCII.GetString(nick);

                async Task Encrypted(string text)
                {
                    byte[] b = Encoding.ASCII.GetBytes(text + "\r\n");
                    Cipher!.TransformFrame(b);
                    await s.WriteAsync(b, Ct);
                }
                if (refuseWith != 0)
                {
                    await Encrypted($":127.0.0.1 {refuseWith} * :bad password");
                    return;
                }
                await Encrypted(":127.0.0.1 300 * TOKEN");
                await Encrypted($":127.0.0.1 001 {WelcomePayload(4242)}");
                await Encrypted(":127.0.0.1 422 TEST0001 :pYme");
                await Encrypted(":TEST0001 MODE TEST0001 :+iLiv");
            }

            public async ValueTask DisposeAsync()
            {
                Connection?.Dispose();
                _gate.Stop();
                _host.Stop();
                await Task.CompletedTask;
            }
        }

        [Fact]
        public async Task CoreSession_LogsInThroughTheGatekeeperAndKeyExchange()
        {
            await using var fake = new FakeCore();
            await using var session = await PolCoreSession.LoginAsync("TEST0001", "password", Ct, "META", "127.0.0.1", fake.GatePort);
            await fake.Run;

            Assert.Equal("NICK 232JR2NC1:5f4dcc3b5aa765d61d8327deb882cf99:META\t\t\t\t", fake.ReceivedNick);
            Assert.Equal(4242u, session.Ticket.FfxiAccountId);
            Assert.Equal(Secret, session.Ticket.SessionSecret);
            Assert.InRange(session.Clock.Now, 1731110400, 1731110460);

            // Both ends keyed the same cipher.
            byte[] a = new byte[16], b = new byte[16];
            session.Cipher.TransformFrame(a);
            fake.Cipher!.TransformFrame(b);
            Assert.Equal(a, b);
        }

        [Fact]
        public async Task CoreSession_ARefusalNumericFailsWithItsCode()
        {
            await using var fake = new FakeCore(refuseWith: 464);
            var ex = await Assert.ThrowsAsync<RetailLoginException>(() => PolCoreSession.LoginAsync("TEST0001", "wrong", Ct, "META", "127.0.0.1", fake.GatePort));
            Assert.Equal(464, ex.Code);
            Assert.Equal("core session", ex.Stage);
        }

        // ----- :51220 -----

        private static PolCoreTicket Ticket => new(Routing, Account, Secret, 4242);

        [Fact]
        public void Presence_FramesHaveTheSpecLayout()
        {
            byte[] hello = new byte[40];
            PolPresencePackets.WriteHello(hello, Routing, ReadOnlySpan<byte>.Empty);
            Assert.Equal(new byte[] { 0, 0, 0, 0, 1, 0 }, hello[..6]);
            Assert.Equal(Routing, hello[6..12]);
            Assert.All(hello[12..], b => Assert.Equal(0, b));

            byte[] header = new byte[40];
            PolPresencePackets.WriteRequestHeader(header, 0x04, 0x07, 64, Ticket, 0x6AF0F350);
            Assert.Equal(new byte[] { 2, 4, 7, 0, 64, 0, 0, 0 }, header[..8]);
            Assert.Equal(Account, header[12..24]);
            Assert.Equal(PolHashes.ChallengeDigest(Secret, Account, 0x6AF0F350), header[24..40]);

            byte[] auth = new byte[64];
            PolPresencePackets.WriteAuthPayload(auth, SquareEnixAuthMode.PasswordAndOneTimeCode, "seid", "SecretPass123", "300246", 1731110400);
            Assert.Equal(2, auth[0]);
            Assert.Equal("seid", PolpPackets.ReadAscii(auth.AsSpan(1, 17)));
            Assert.Equal("300246", Encoding.ASCII.GetString(auth, 0x12, 6));
            Assert.All(auth[0x18..0x20], b => Assert.Equal(0, b));
            Assert.Equal("2e6a0284491d4529a6d115832e6eb389af4abc56", Convert.ToHexStringLower(auth.AsSpan(0x20, 20)));
            Assert.True(PolHashes.VerifyAdditive(auth));

            PolPresencePackets.WriteAuthPayload(auth, SquareEnixAuthMode.Password, "seid", "x", "300246", 0);
            Assert.All(auth[0x12..0x20], b => Assert.Equal(0, b));

            byte[] key = new byte[40];
            PolPresencePackets.WriteKeyRequestPayload(key, PolSessionKeyService.Pml, 0x01020304);
            Assert.Equal(1, key[0x13]);
            Assert.Equal(new byte[] { 0, 1, 0, 5, 0x03, 0xE8 }, key[0x14..0x1A]);
            Assert.Equal(0x01020304u, BinaryPrimitives.ReadUInt32LittleEndian(key.AsSpan(0x20)));
            Assert.True(PolHashes.VerifyAdditive(key));

            // The lobby key: ServiceType 0, flags 1 (spec v1.3.0 section 4.3.5).
            PolPresencePackets.WriteKeyRequestPayload(key, PolSessionKeyService.Lobby, 7);
            Assert.Equal(0, key[0x13]);
            Assert.Equal(new byte[] { 0, 1, 0, 5, 0x00, 0x01 }, key[0x14..0x1A]);
            Assert.True(PolHashes.VerifyAdditive(key));
        }

        [Fact]
        public void Presence_RedirectAddressReadsTheSpecExample()
        {
            byte[] header = new byte[24];
            header[0] = 0x83;
            new byte[] { 0xA5, 0x36, 0x43, 0xCA }.CopyTo(header, 8);
            Assert.Equal(IPAddress.Parse("202.67.54.165"), PolPresencePackets.ReadResponseHeader(header).RedirectAddress);
        }

        [Fact]
        public async Task Presence_AuthenticatesThenGetsAKeyFromTheAssignedServer()
        {
            await using var core = new FakeCore();
            await using var session = await PolCoreSession.LoginAsync("TEST0001", "password", Ct, "META", "127.0.0.1", core.GatePort);
            await core.Run;
            var serverCipher = core.Cipher!;

            using var pp = new TcpListener(IPAddress.Loopback, 0);
            pp.Start();
            int port = ((IPEndPoint)pp.LocalEndpoint).Port;
            byte[] sessionKey = Convert.FromHexString("00112233445566778899AABBCCDDEEFF");
            byte[]? redirectCipherText = null;

            async Task<byte[]> Serve(Func<byte[], byte[], (byte[] Header, byte[] Payload)> answer, int payloadLength, Action<byte[]>? onHello = null)
            {
                using var c = await pp.AcceptTcpClientAsync(Ct);
                var s = c.GetStream();
                byte[] hello = new byte[40];
                await s.ReadExactlyAsync(hello, Ct);
                onHello?.Invoke(hello);
                byte[] greet = new byte[24];
                greet[0] = 0x81;
                BinaryPrimitives.WriteUInt32LittleEndian(greet.AsSpan(0x14), 0x6AF0F350);
                await s.WriteAsync(greet, Ct);

                byte[] request = new byte[40 + payloadLength];
                await s.ReadExactlyAsync(request, Ct);
                serverCipher.TransformFrame(request);
                Assert.Equal(PolHashes.ChallengeDigest(Secret, Account, 0x6AF0F350), request[24..40]);

                var (header, payload) = answer(request[..40], request[40..]);
                byte[] reply = header.Concat(payload).ToArray();
                serverCipher.TransformFrame(reply);
                redirectCipherText ??= reply[..24];
                await s.WriteAsync(reply, Ct);
                return request;
            }

            var presence = new PolPresenceClient(session);

            var authServer = Serve((h, p) =>
            {
                Assert.Equal(0x07, h[2]);
                Assert.Equal(1, p[0]); // no one-time code: password mode
                byte[] header = new byte[24];
                header[0] = 0x83;
                BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(4), 8);
                new byte[] { 1, 0, 0, 127 }.CopyTo(header, 8); // 127.0.0.1
                Account.CopyTo(header, 12);
                return (header, Array.Empty<byte>());
            }, 64);
            await presence.AuthenticateAsync("seid", "SecretPass123", "", Ct, "127.0.0.1", port);
            await authServer;
            Assert.Equal(IPAddress.Loopback, presence.AssignedServer);

            var keyServer = Serve((h, p) =>
            {
                Assert.Equal(0x05, h[2]);
                Assert.Equal(1, BinaryPrimitives.ReadUInt16BigEndian(p.AsSpan(0x18)));
                byte[] header = new byte[24];
                header[0] = 0x83;
                BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(4), 32);
                byte[] payload = new byte[32];
                sessionKey.CopyTo(payload, 0);
                PolHashes.SealAdditive(payload);
                return (header, payload);
            }, 40, hello => Assert.Equal(redirectCipherText![12..24], hello[12..24]));
            byte[] key = await presence.RequestSessionKeyAsync(PolSessionKeyService.Lobby, Ct, port);
            await keyServer;
            Assert.Equal(sessionKey, key);
        }

        // ----- Lobby -----

        [Fact]
        public void Lobby_SealedPacketsVerify_AndTheLoginHasTheSpecLayout()
        {
            byte[] login = new byte[LobbyPackets.LobbyLoginSize];
            RetailLobbyPackets.WriteLobbyLogin(login, 0x082E1E27, "30261006_0");
            Assert.True(RetailLobbyPackets.Verify(login));
            Assert.Equal(0x26u, BinaryPrimitives.ReadUInt32LittleEndian(login.AsSpan(8)));
            Assert.Equal(5u, BinaryPrimitives.ReadUInt32LittleEndian(login.AsSpan(0x2C)));
            Assert.Equal(new byte[] { 0x27, 0x1E, 0x2E, 0x08 }, login[0x34..0x38]);
            Assert.Equal("30261006_0", PolpPackets.ReadAscii(login.AsSpan(0x74, 15)));
            Assert.All(login[0x38..0x74], b => Assert.Equal(0, b));
            Assert.Equal(0xFF, login[0x83]);
            Assert.Equal(0xFFFu, BinaryPrimitives.ReadUInt32LittleEndian(login.AsSpan(0x84)));

            login[0x90] ^= 1;
            Assert.False(RetailLobbyPackets.Verify(login));
        }

        [Fact]
        public void Lobby_ModifierStepsLikeTheSpec()
        {
            var m = new RetailLobbyModifier();
            Assert.Throws<InvalidOperationException>(() => m.Take());
            m.Seed(0x590DF1FA);
            Assert.Equal(0x590DF1FBu, m.Take()); // 0x1F
            Assert.Equal(0x590DF1FDu, m.Take()); // 0x24
            Assert.Equal(0x590DF1FFu, m.Take()); // 0x07
        }

        [Fact]
        public void Lobby_SelectCharacterCarriesTheSelectionDigest()
        {
            byte[] login = new byte[LobbyPackets.LobbyLoginSize];
            RetailLobbyPackets.WriteLobbyLogin(login, 4242, "30261006_0");
            byte[] reserved = RetailLobbyPackets.ReservedToken(login).ToArray();
            byte[] hash = PolHashes.LobbySessionHash(Convert.FromHexString("0123456789ABCDEFFEDCBA9876543210"), 0x590DF1FF);

            byte[] select = new byte[RetailLobbyPackets.SelectCharacterSize];
            RetailLobbyPackets.WriteSelectCharacter(select, 0x1111, 0x2222, "Knot", hash, reserved);
            Assert.True(RetailLobbyPackets.Verify(select));
            Assert.Equal("Knot", PolpPackets.ReadAscii(select.AsSpan(0x24, 16)));
            Assert.Equal(hash, select[0x34..0x44]);

            byte[] expected = MD5.HashData(reserved.Concat(hash).Concat(BitConverter.GetBytes(0x1111u)).Concat(BitConverter.GetBytes(0x2222u)).Concat(new byte[4]).ToArray());
            Assert.Equal(expected, select[0x48..0x58]);
        }

        [Fact]
        public void Lobby_MapHandoffParsesTheMapAndSearchServers()
        {
            byte[] p = new byte[RetailLobbyPackets.MapHandoffSize];
            Assert.Equal(0x48, p.Length);
            RetailLobbyPackets.WriteHeader(p, LobbyCommand.ResponseNextLogin);
            new byte[] { 124, 150, 154, 10 }.CopyTo(p, 0x38);
            BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(0x3C), 54231);
            new byte[] { 124, 150, 154, 20 }.CopyTo(p, 0x40);
            BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(0x44), 54002);
            Assert.True(RetailLobbyPackets.TryParseMapHandoff(p, out var h));
            Assert.Equal(IPAddress.Parse("124.150.154.10"), h.MapServer);
            Assert.Equal(54231, h.MapPort);
            Assert.Equal(IPAddress.Parse("124.150.154.20"), h.SearchServer);
            Assert.Equal(54002, h.SearchPort);
            Assert.False(RetailLobbyPackets.TryParseMapHandoff(p.AsSpan(0, 0x44), out _));
        }

        [Fact]
        public void Lobby_MapLoginTicketIsMd5OfTheNameAndSessionHash()
        {
            byte[] hash = Convert.FromHexString("906fc2f1cc7f88ae1c06c215be8eb208");
            byte[] expected = MD5.HashData(Encoding.ASCII.GetBytes("Knot").Concat(hash).ToArray());
            Assert.Equal(expected, RetailLobbyPackets.MapLoginTicket("Knot", hash));
        }
    }
}
