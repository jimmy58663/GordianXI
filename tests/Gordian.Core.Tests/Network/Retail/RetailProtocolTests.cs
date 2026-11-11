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
using Gordian.Core.Network.Lobby;
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
                Assert.Equal("100020261022_0", PolpPackets.ReadAscii(req.AsSpan(0x14, 68)));
                await s.WriteAsync(VersionResponse(1, "20261022_0"), Ct);
            });
            var result = await PolpVersionChecker.CheckAsync("127.0.0.1", port, PolpPackets.ViewerProduct,
                PatchConfigFile.PolpVersion(PolpPackets.ViewerProductId, "20261022_0"), Ct);
            await server;
            Assert.True(result.IsUpToDate);
            Assert.Equal("20261022_0", result.CurrentVersion);
        }

        // The first live try: the FFXI check server closed without a full reply. That is a named version-check failure
        // (which the login then only warns about), not a bare end-of-stream error.
        [Theory]
        [InlineData(0)]
        [InlineData(10)]
        public async Task Polp_Checker_AShortReplyIsAVersionCheckFailure(int bytesSent)
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var server = Task.Run(async () =>
            {
                using var c = await listener.AcceptTcpClientAsync(Ct);
                var s = c.GetStream();
                await s.ReadExactlyAsync(new byte[88], Ct);
                await s.WriteAsync(VersionResponse(1, "30261102_0").AsMemory(0, bytesSent), Ct);
            });
            var ex = await Assert.ThrowsAsync<RetailLoginException>(() => PolpVersionChecker.CheckAsync("127.0.0.1", port, PolpPackets.FfxiProduct,
                PatchConfigFile.PolpVersion(PolpPackets.FfxiProductId, "30261102_0"), Ct));
            await server;
            Assert.Equal("version check", ex.Stage);
        }

        [Fact]
        public void PatchCfg_TheInstalledVersionIsTheHighestTag()
        {
            const string cfg = "file patch.txt {\n30020917_0 833977 62439499 -1977655372 30020917_0/Direct/patch.txt.slc 372355\n" +
                "30261102_0 2905680 1 2 30261102_0/Direct/FFXiMain.dll.slc 3\n30261006_0 1 2 3 x 4\n}\nfile FFXiMain.dll {\n" +
                "  30260904_1 1 2 3 x 4\n}\n";
            Assert.Equal("30261102_0", PatchConfigFile.HighestTag(new StringReader(cfg)));
            Assert.Null(PatchConfigFile.HighestTag(new StringReader("file x {\n}\n")));
            Assert.Equal("000130261102_0", PatchConfigFile.PolpVersion(PolpPackets.FfxiProductId, "30261102_0"));

            string dir = Path.Combine(Path.GetTempPath(), "GordianXI_PatchCfg_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                Assert.Null(PatchConfigFile.ReadInstalledVersion(dir));
                File.WriteAllText(Path.Combine(dir, PatchConfigFile.FileName), cfg);
                Assert.Equal("30261102_0", PatchConfigFile.ReadInstalledVersion(dir));
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        // ----- Core IRC -----

        private static string Token(uint time, IPAddress redirect, ushort port, ushort clientPort = 50000)
        {
            byte[] data = new byte[25];
            BinaryPrimitives.WriteUInt32BigEndian(data, time);
            new byte[] { 192, 0, 2, 10 }.CopyTo(data, 4);
            redirect.GetAddressBytes().CopyTo(data, 8);
            BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(0x0C), port);
            BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(0x14), clientPort);
            data[0x16] = data[0x17] = 1;
            return PolBase32.Encode(data) + "vfrA";
        }

        [Fact]
        public void Core_ParsesNumericsAndTokens()
        {
            Assert.True(PolCoreProtocol.TryParseNumeric(":202.67.54.132 001 ABC", out var src, out int n, out var rest));
            Assert.Equal(("202.67.54.132", 1, "ABC"), (src, n, rest));
            Assert.False(PolCoreProtocol.TryParseNumeric("NOTICE x", out _, out _, out _));

            string token = Token(1731110400, IPAddress.Parse("202.67.54.125"), 51242);
            Assert.Equal(44, token.Length);
            Assert.Equal(token, PolCoreProtocol.ReadGreetingToken($":202.67.54.52 300 * {token}", out var ip));
            Assert.Equal("202.67.54.52", ip);
            var parsed = PolCoreToken.Parse(token);
            Assert.Equal(1731110400u, parsed.ServerTimestamp);
            Assert.Equal(IPAddress.Parse("202.67.54.125"), parsed.RedirectAddress);
            Assert.Equal(51242, parsed.RedirectPort);
            Assert.Equal(50000, parsed.ClientPort);
        }

        // Test vector 9: capture #3's USER line.
        [Fact]
        public void Core_UserLineIsTheModulusAndFourZeroBytes_CutTo47Characters()
        {
            byte[] modulus = Convert.FromHexString("95C6216A8DE51384AADBDF6AA1169B1A106980FE43FCEF2CD8E94D5875EBEC48");
            string line = PolCoreProtocol.BuildUserLine(modulus);
            Assert.Equal(61, line.Length);
            Assert.StartsWith("USER x 8 * :", line);
            string token = line["USER x 8 * :".Length..^2];
            // Capture #3 had 0B F8 4A 80 in the 4 trailing bytes; with zeros the characters from 42 on differ.
            Assert.Equal("rYsUyi_rIJqPf66Pi9yBcUS4Ag18XONjfOrkmWZ@lI", token[..42]);
            Assert.Equal(modulus, PolCoreProtocol.ParseUserToken(token));
            Assert.Equal(modulus, PolCoreProtocol.ParseUserToken("rYsUyi_rIJqPf66Pi9yBcUS4Ag18XONjfOrkmWZ@lIAKuIP"));

            // With capture #3's trailing bytes the line is the captured one, character for character.
            string captured = PolCoreProtocol.BuildUserLine(modulus, Convert.FromHexString("0BF84A80"));
            Assert.Equal("USER x 8 * :rYsUyi_rIJqPf66Pi9yBcUS4Ag18XONjfOrkmWZ@lIAKuIP\r\n", captured);
        }

        [Fact]
        public void Core_CiphertextTokenIgnoresCharactersPastTheFirst43()
        {
            byte[] c = PolCoreProtocol.ParseCiphertextToken("OlyyM2LoViSrkkvTTuyCUsDQPvhUHOKSbQImR@QqeX3n]E|");
            Assert.Equal("3BB69AB59BCC7E806534DC8003E695858B9EAB2EA124E2C199E1168EB79253F2", Convert.ToHexString(c));
            Assert.Equal(c, PolCoreProtocol.ParseCiphertextToken(PolCoreProtocol.BuildCiphertextToken(c)));
            Assert.Throws<FormatException>(() => PolCoreProtocol.ParseCiphertextToken("Olyy"));
        }

        [Fact]
        public void Core_NickLineIsTheEncodedIdTheResponseTheBodyAndItsSuffix_AndNoTabs()
        {
            string greeting = "WCMH3HGNNN4NI55OBMGAE21NNNNNNNNNY0QE3N1NvfrA";
            string line = PolCoreProtocol.BuildNickLine("ABCD1234", "password", greeting);
            Assert.Equal(86, line.Length);
            string head = $"NICK {PolIdEncoding.Encode("ABCD1234")}:{PolCoreProtocol.PassResponse(greeting, "password")}:{PolClientMeta.Body("ABCD1234")}";
            Assert.Equal(head + PolClientMeta.Suffix(Encoding.ASCII.GetBytes(head)) + "\r\n", line);
            Assert.DoesNotContain('\t', line);

            // The password field answers this connection's greeting, so another greeting changes it.
            Assert.NotEqual(line, PolCoreProtocol.BuildNickLine("ABCD1234", "password", "WCM2B3GNNN4NI55OBU3AECZNNNNNNNNNXCVN3N1N}HNM"));
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
        public void Core_Base64AlphabetHas64DistinctCharacters() =>
            Assert.Equal(64, PolCoreBase64.Alphabet.Distinct().Count());

        [Fact]
        public void Core_PingsAreAnsweredWithTheirArguments()
        {
            Assert.Equal("PONG :abc\r\n", PolCoreProtocol.AnswerPing("PING :abc"));
            Assert.Equal("PONG 12345\r\n", PolCoreProtocol.AnswerPing(":202.67.54.132 PING 12345"));
            Assert.Null(PolCoreProtocol.AnswerPing(":202.67.54.132 NOTICE x"));
        }

        private static readonly byte[] Routing = Convert.FromHexString("50C30A0200C0");

        /// <summary>A loopback gatekeeper and session host, speaking the spec's stages 2 and 3.</summary>
        private sealed class FakeCore : IAsyncDisposable
        {
            private readonly TcpListener _gate = new(IPAddress.Loopback, 0);
            private readonly TcpListener _host = new(IPAddress.Loopback, 0);
            public int GatePort => ((IPEndPoint)_gate.LocalEndpoint).Port;
            public string? ReceivedNick, ReceivedPong, GreetingToken;
            public byte[]? SessionKey, Modulus;
            public PolStreamCipher? Cipher;
            public TcpClient? Connection;
            public Task Run { get; }

            public FakeCore(int refuseWith = 0, bool ping = false, bool silentAfterNick = false)
            {
                _gate.Start();
                _host.Start();
                Run = Task.Run(() => Serve(refuseWith, ping, silentAfterNick));
            }

            private static async Task Line(NetworkStream s, string text) => await s.WriteAsync(Encoding.ASCII.GetBytes(text + "\r\n"), Ct);

            private async Task Serve(int refuseWith, bool ping, bool silentAfterNick)
            {
                ushort hostPort = (ushort)((IPEndPoint)_host.LocalEndpoint).Port;
                using (var g = await _gate.AcceptTcpClientAsync(Ct))
                    await Line(g.GetStream(), $":127.0.0.1 300 * {Token(1731110400, IPAddress.Loopback, hostPort)}");

                Connection = await _host.AcceptTcpClientAsync(Ct);
                var s = Connection.GetStream();
                var reader = new LineReader(s);
                GreetingToken = Token(1731110401, IPAddress.Any, 0);
                await Line(s, $":127.0.0.1 300 * {GreetingToken}");

                string user = (await reader.ReadLineAsync(Ct))!;
                Assert.Equal(PolCoreProtocol.KeyTokenChars, user.Length - "USER x 8 * :".Length);
                Modulus = PolCoreProtocol.ParseUserToken(user["USER x 8 * :".Length..]);
                SessionKey = RandomNumberGenerator.GetBytes(8);
                byte[] c = PolRsaKeyExchange.Encrypt(Modulus, PolRsaKeyExchange.WrapSessionKey(SessionKey));
                await Line(s, $":127.0.0.1 300 * {PolCoreProtocol.BuildCiphertextToken(c)}");
                Cipher = new PolStreamCipher(SessionKey, Modulus.AsSpan(0, 8));

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
                if (silentAfterNick)
                {
                    // A server that rejects NICK without a numeric: it goes quiet.
                    await Task.Delay(Timeout.Infinite, Ct).ContinueWith(_ => { });
                    return;
                }
                await Encrypted($":127.0.0.1 001 {PolCoreBase64.Encode(new byte[] { 0, 0, 0, 1, 0, 0 })}abcd");
                await Encrypted(":127.0.0.1 422 US77G53VV :abcd");
                await Encrypted(":US77G53VV MODE US77G53VV :+iLiv");

                if (ping)
                {
                    await Encrypted("PING :keepalive");
                    byte[] pong = (await reader.ReadRawLineAsync(Ct))!;
                    Cipher.TransformFrame(pong);
                    ReceivedPong = Encoding.ASCII.GetString(pong);
                }
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
            await using var session = await PolCoreSession.LoginAsync("ABCD1234", "password", Ct, "127.0.0.1", fake.GatePort);
            await fake.Run;

            Assert.Equal(PolCoreProtocol.BuildNickLine("ABCD1234", "password", fake.GreetingToken!)[..^2], fake.ReceivedNick);
            Assert.Equal(Routing, session.RoutingToken);
            Assert.Equal(fake.SessionKey, session.SessionKey);
            Assert.Equal(1731110401u, session.GreetingTimestamp);
            Assert.InRange(session.Clock.Now, 1731110400, 1731110460);

            // Both ends keyed the same cipher, IV from the modulus.
            byte[] a = new byte[16], b = new byte[16];
            session.Cipher.TransformFrame(a);
            fake.Cipher!.TransformFrame(b);
            Assert.Equal(a, b);
        }

        [Fact]
        public async Task CoreSession_AnswersTheServersPings()
        {
            await using var fake = new FakeCore(ping: true);
            await using var session = await PolCoreSession.LoginAsync("ABCD1234", "password", Ct, "127.0.0.1", fake.GatePort);
            await fake.Run;
            Assert.Equal("PONG :keepalive", fake.ReceivedPong);
        }

        [Fact]
        public async Task CoreSession_ASilentServerAfterNickNamesTheStepAndTheRepliesSeen()
        {
            await using var fake = new FakeCore(silentAfterNick: true);
            var saved = PolCoreSession.Timeout;
            PolCoreSession.Timeout = TimeSpan.FromSeconds(2);
            try
            {
                var ex = await Assert.ThrowsAsync<RetailLoginException>(() => PolCoreSession.LoginAsync("ABCD1234", "password", Ct, "127.0.0.1", fake.GatePort));
                Assert.Equal("PlayOnline login", ex.Stage);
                Assert.Contains("welcome after NICK", ex.Message);
                Assert.Contains("Lines received after NICK: 300.", ex.Message);
                Assert.DoesNotContain("TOKEN", ex.Message); // line types only, never contents
            }
            finally
            {
                PolCoreSession.Timeout = saved;
            }
        }

        [Theory]
        [InlineData(":127.0.0.1 001 abc", "001")]
        [InlineData(":US77G53VV MODE US77G53VV :+iLiv", "MODE")]
        [InlineData("PING :keepalive", "PING")]
        [InlineData("ÿgarbage", "unreadable (9 bytes)")]
        public void CoreSession_DescribeLineGivesTheTypeOnly(string line, string expected) =>
            Assert.Equal(expected, PolCoreProtocol.DescribeLine(line));

        [Fact]
        public async Task CoreSession_ARefusalNumericFailsWithItsCode()
        {
            await using var fake = new FakeCore(refuseWith: 464);
            var ex = await Assert.ThrowsAsync<RetailLoginException>(() => PolCoreSession.LoginAsync("ABCD1234", "wrong", Ct, "127.0.0.1", fake.GatePort));
            Assert.Equal(464, ex.Code);
            Assert.Equal("core session", ex.Stage);
        }

        // ----- :51220 -----


        [Fact]
        public void Presence_FramesHaveTheSpecLayout()
        {
            byte[] hello = new byte[40];
            PolPresencePackets.WriteHello(hello, Routing);
            Assert.Equal(new byte[] { 0, 0, 0, 0, 1, 0 }, hello[..6]);
            Assert.Equal(Routing, hello[6..12]);
            Assert.All(hello[12..], b => Assert.Equal(0, b));

            byte[] header = new byte[40];
            PolPresencePackets.WriteRequestHeader(header, 0x04, 0x07, 64, "ABCD1234", "password", 0x6AF0F350);
            Assert.Equal(new byte[] { 2, 4, 7, 0, 64, 0, 0, 0 }, header[..8]);
            Assert.All(header[12..24], b => Assert.Equal(0, b));
            Assert.Equal(PolHashes.ChallengeDigest("ABCD1234", "password", 0x6AF0F350), header[24..40]);

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
            PolPresencePackets.WriteKeyRequestPayload(key, PolSessionKeyService.Viewer, 0x01020304);
            Assert.Equal(1, key[0x13]);
            Assert.Equal(new byte[] { 0, 1, 0, 5, 0x03, 0xE8 }, key[0x14..0x1A]);
            Assert.Equal(0x01020304u, BinaryPrimitives.ReadUInt32LittleEndian(key.AsSpan(0x20)));
            Assert.True(PolHashes.VerifyAdditive(key));

            // The lobby key: ServiceType 0, flags 1, ClientNonce 0 (spec v2.1.19 section 4.3.5).
            PolPresencePackets.WriteKeyRequestPayload(key, PolSessionKeyService.Lobby);
            Assert.Equal(0, key[0x13]);
            Assert.Equal(0u, BinaryPrimitives.ReadUInt32LittleEndian(key.AsSpan(0x20)));
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
            await using var session = await PolCoreSession.LoginAsync("ABCD1234", "password", Ct, "127.0.0.1", core.GatePort);
            await core.Run;
            var serverCipher = core.Cipher!;

            using var pp = new TcpListener(IPAddress.Loopback, 0);
            pp.Start();
            int port = ((IPEndPoint)pp.LocalEndpoint).Port;
            byte[] sessionKey = Convert.FromHexString("00112233445566778899AABBCCDDEEFF");

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
                Assert.Equal(PolHashes.ChallengeDigest("ABCD1234", "password", 0x6AF0F350), request[24..40]);

                var (header, payload) = answer(request[..40], request[40..]);
                byte[] reply = header.Concat(payload).ToArray();
                serverCipher.TransformFrame(reply);
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
            }, 40, hello => Assert.All(hello[12..], b => Assert.Equal(0, b)));
            byte[] key = await presence.RequestSessionKeyAsync(PolSessionKeyService.Lobby, Ct, port);
            await keyServer;
            Assert.Equal(sessionKey, key);
        }

        // ----- Lobby -----

        private static readonly byte[] AuthToken = Enumerable.Range(1, 52).Select(i => (byte)i).ToArray();

        [Fact]
        public void Lobby_SealedPacketsVerify_AndTheLoginHasTheSpecLayout()
        {
            byte[] login = new byte[LobbyPackets.LobbyLoginSize];
            LobbyClientPackets.WriteLobbyLogin(login, AuthToken, "30261102_0");
            Assert.True(LobbyClientPackets.Verify(login));
            Assert.Equal(0x26u, BinaryPrimitives.ReadUInt32LittleEndian(login.AsSpan(8)));
            Assert.All(login[0x1C..0x2C], b => Assert.Equal(0, b));
            Assert.Equal(new byte[] { 5, 0, 0, 0, 0, 0, 0, 0 }, login[0x2C..0x34]);
            Assert.Equal(AuthToken, login[0x34..0x68]);
            Assert.All(login[0x68..0x74], b => Assert.Equal(0, b));
            Assert.Equal(login[0x34..0x74], LobbyClientPackets.AuthCodeField(login).ToArray());
            Assert.Equal("30261102_0", PolpPackets.ReadAscii(login.AsSpan(0x74, 15)));
            Assert.Equal(0xFF, login[0x83]);
            Assert.Equal(0xFFFu, BinaryPrimitives.ReadUInt32LittleEndian(login.AsSpan(0x84)));

            login[0x90] ^= 1;
            Assert.False(LobbyClientPackets.Verify(login));
        }

        [Fact]
        public void Lobby_ModifierStepsLikeTheSpec()
        {
            var m = new LobbyKeyModifier();
            Assert.Throws<InvalidOperationException>(() => m.Take());
            m.Seed(0x590DF1FA);
            Assert.Equal(0x590DF1FBu, m.Take()); // 0x1F
            Assert.Equal(0x590DF1FDu, m.Take()); // 0x24
            Assert.Equal(0x590DF1FFu, m.Take()); // 0x07

            // Capture #5's order with character creation: 0x22 +5, 0x21 +7, 0x1F +9, 0x07 +11.
            m.Seed(0x590DF1FA);
            for (int i = 0; i < 5; i++) m.Take();
            Assert.Equal(0x590DF1FAu + 11, m.Take());
        }

        [Fact]
        public void Lobby_SelectCharacterCarriesTheSelectionDigest()
        {
            byte[] login = new byte[LobbyPackets.LobbyLoginSize];
            LobbyClientPackets.WriteLobbyLogin(login, AuthToken, "30261102_0");
            byte[] authCode = LobbyClientPackets.AuthCodeField(login).ToArray();
            byte[] hash = PolHashes.LobbySessionHash(Convert.FromHexString("0123456789ABCDEFFEDCBA9876543210"), 0x590DF1FF);

            byte[] select = new byte[LobbyPackets.SelectCharacterSize];
            LobbyClientPackets.WriteSelectCharacter(select, 0x016B6984, 0x0065915E, "Knot", hash, authCode, 0x74F0058F);
            Assert.True(LobbyClientPackets.Verify(select));
            Assert.Equal(0x016B6984u, BinaryPrimitives.ReadUInt32LittleEndian(select.AsSpan(0x1C)));
            Assert.Equal(new byte[] { 0x5E, 0x91, 0, 0 }, select[0x20..0x24]); // low 16 bits of the slot id
            Assert.Equal("Knot", PolpPackets.ReadAscii(select.AsSpan(0x24, 16)));
            Assert.Equal(hash, select[0x34..0x44]);
            Assert.Equal(3u, BinaryPrimitives.ReadUInt32LittleEndian(select.AsSpan(0x44)));

            byte[] expected = MD5.HashData(authCode.Concat(hash).Concat(BitConverter.GetBytes(3u))
                .Concat(BitConverter.GetBytes(0x74F0058Fu)).Concat(BitConverter.GetBytes(0x016B6984u)).ToArray());
            Assert.Equal(expected, select[0x48..0x58]);
        }

        [Fact]
        public void Lobby_MapHandoffParsesTheSpecExample_WithTheSharedParser()
        {
            byte[] p = new byte[0x48];
            LobbyClientPackets.WriteHeader(p, LobbyCommand.ResponseNextLogin);
            new byte[] { 0x5E, 0x91, 0x0A, 0x00 }.CopyTo(p, 0x20);
            new byte[] { 0x7C, 0x96, 0x98, 0x42 }.CopyTo(p, 0x38);
            new byte[] { 0xE0, 0xD3, 0x00, 0x00 }.CopyTo(p, 0x3C);
            new byte[] { 0x7C, 0x96, 0x9A, 0x3E }.CopyTo(p, 0x40);
            new byte[] { 0xF2, 0xD2, 0x00, 0x00 }.CopyTo(p, 0x44);
            Assert.True(LobbyPackets.TryParseNextLogin(p, out var next));
            Assert.Equal(IPAddress.Parse("124.150.152.66"), new IPAddress(next.ServerAddress));
            Assert.Equal(54240u, next.ServerPort);
            Assert.Equal(IPAddress.Parse("124.150.154.62"), new IPAddress(next.CacheAddress));
            Assert.Equal(54002u, next.CachePort);
            Assert.Equal(0x000A915Eu, next.ServerId); // the map UniqueNo
            Assert.False(LobbyPackets.TryParseNextLogin(p.AsSpan(0, 0x44), out _));
        }

        // Section 4.6.1 with capture #5's values: the 0x00A checksum byte comes to 0xBF.
        [Fact]
        public void Map_FirstDatagramHasTheSpecLayout()
        {
            byte[] mapKey = Convert.FromHexString("919129145BDE35AFC2D02CEC08A5327B");
            byte[] d = RetailMapPackets.BuildFirstDatagram(0x000A915E, mapKey);
            Assert.Equal(136, d.Length);
            Assert.Equal(Convert.FromHexString("01000000C002000077000000" + "000000000000000000000000" + "24E8FE76"), d[..28]);
            byte[] packet = d[28..120];
            Assert.Equal(new byte[] { 0x0A, 0x2E, 0x01, 0x00, 0xBF }, packet[..5]);
            Assert.Equal(new byte[] { 0x5E, 0x91, 0x0A, 0x00 }, packet[0x0C..0x10]);
            Assert.Equal(new byte[] { 0x20, 0x00 }, packet[0x22..0x24]);
            Assert.All(packet[0x31..0x40], b => Assert.Equal(0, b));
            Assert.Equal("27673EA222A829989583118DF9DE80B3", Convert.ToHexString(packet[0x40..0x50]));
            Assert.Equal("WIN\0"u8.ToArray(), packet[0x54..0x58]);
            Assert.Equal(new byte[] { 1, 0, 0xFF, 0xFF }, packet[0x58..0x5C]);
            Assert.Equal(MD5.HashData(packet), d[120..]);
        }
    }
}
