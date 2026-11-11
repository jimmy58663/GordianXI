// tests/Gordian.Core.Tests/Network/Retail/RetailCryptoTests.cs
using System;
using System.Buffers.Binary;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using Gordian.Core.Network.Lobby;
using Gordian.Core.Network.Retail;
using Xunit;

namespace Gordian.Core.Tests.Network.Retail
{
    /// <summary>
    /// Retail login primitives against the worked vectors in docs/network/retail-login-spec.md v2.1.19 section 5.8, and
    /// Blowfish against Schneier's published test vectors.
    /// </summary>
    public class RetailCryptoTests
    {
        private static (uint L, uint R) Block(string hex)
        {
            byte[] b = Convert.FromHexString(hex);
            return (BinaryPrimitives.ReadUInt32BigEndian(b), BinaryPrimitives.ReadUInt32BigEndian(b.AsSpan(4)));
        }

        [Theory]
        [InlineData("0000000000000000", "0000000000000000", "4EF997456198DD78")]
        [InlineData("FFFFFFFFFFFFFFFF", "FFFFFFFFFFFFFFFF", "51866FD5B85ECB8A")]
        [InlineData("3000000000000000", "1000000000000001", "7D856F9A613063F2")]
        [InlineData("FEDCBA9876543210", "0123456789ABCDEF", "0ACEAB0FC6A0A28D")]
        public void Blowfish_MatchesSchneiersVectors(string key, string plain, string cipher)
        {
            var bf = StandardBlowfish.FromKeyBytes(Convert.FromHexString(key));
            var (l, r) = Block(plain);
            bf.Encrypt(ref l, ref r);
            Assert.Equal(Block(cipher), (l, r));
        }

        [Fact]
        public void Blowfish_CyclesALongKey()
        {
            // Schneier's variable-key vector: key "abcdefghijklmnopqrstuvwxyz", plaintext "BLOWFISH".
            var bf = StandardBlowfish.FromKeyBytes(Encoding.ASCII.GetBytes("abcdefghijklmnopqrstuvwxyz"));
            var (l, r) = Block(Convert.ToHexString(Encoding.ASCII.GetBytes("BLOWFISH")));
            bf.Encrypt(ref l, ref r);
            Assert.Equal(Block("324ED0FEF413A203"), (l, r));
        }

        [Fact]
        public void ZeroStateMd5_DiffersFromMd5OnlyInItsStartingState()
        {
            // With the standard state it would be MD5; with the zero state the empty message gives a fixed digest that is not MD5's.
            Span<byte> digest = stackalloc byte[16];
            ZeroStateMd5.Hash(Array.Empty<byte>(), digest);
            Assert.NotEqual(MD5.HashData(Array.Empty<byte>()), digest.ToArray());
            ZeroStateMd5.Hash("abc"u8, digest);
            Assert.NotEqual(MD5.HashData("abc"u8), digest.ToArray());
        }

        private static readonly byte[] Capture3Key = Convert.FromHexString("311DD37445716B05");
        private static readonly byte[] Capture3Iv = Convert.FromHexString("95C6216A8DE51384");

        [Fact]
        public void Vector6_SessionKeySetupAndKeystream()
        {
            var cipher = new PolStreamCipher(Capture3Key, Capture3Iv);
            var bf = cipher.Blowfish;
            Assert.Equal(new uint[] { 0x4314243A, 0xAF81F8CD, 0x73814FE5, 0xA98502F8 }, new[] { bf.P(0), bf.P(1), bf.P(2), bf.P(3) });
            Assert.Equal(new uint[] { 0x561D0A90, 0x08D37945, 0x561D0A90, 0x08D37945 }, new[] { bf.S(0, 0), bf.S(0, 1), bf.S(0, 2), bf.S(0, 3) });

            byte[] keystream = new byte[16];
            cipher.TransformFrame(keystream);
            Assert.Equal("1635582F2C3E21032E4EAEA578A3EF84", Convert.ToHexString(keystream));
        }

        [Fact]
        public void StreamCipher_RoundTripsAndKeepsLineEnds()
        {
            var cipher = new PolStreamCipher(Convert.FromHexString("A1B2C3D4E5F60718"), Capture3Iv);
            byte[] line = Encoding.ASCII.GetBytes(PolCoreProtocol.BuildNickLine("ABCD1234", "password", "WCMH3HGNNN4NI55OBMGAE21NNNNNNNNNY0QE3N1NvfrA"));
            byte[] wire = (byte[])line.Clone();
            cipher.TransformFrame(wire);

            Assert.NotEqual(line, wire);
            Assert.Equal((byte)'\r', wire[^2]);
            Assert.Equal((byte)'\n', wire[^1]);
            foreach (byte b in wire.AsSpan(0, wire.Length - 2)) Assert.False(b is 0x0A or 0x0D);

            cipher.TransformFrame(wire);
            Assert.Equal(line, wire);
        }

        [Fact]
        public void StreamCipher_AContinuationCarriesOnTheKeystream_AndAForkHasItsOwnState()
        {
            byte[] whole = new byte[104];
            new PolStreamCipher(Capture3Key, Capture3Iv).TransformFrame(whole);

            var split = new PolStreamCipher(Capture3Key, Capture3Iv);
            var fork = split.Fork();
            byte[] header = new byte[40], payload = new byte[64], other = new byte[16];
            split.TransformFrame(header);
            fork.TransformFrame(other); // must not disturb the continuation below
            split.Transform(payload);
            Assert.Equal(whole.AsSpan(0, 40).ToArray(), header);
            Assert.Equal(whole.AsSpan(40).ToArray(), payload);
            Assert.Equal(whole.AsSpan(0, 16).ToArray(), other);
        }

        // Test vector 10: both captures' moduli were factored, so their exchanges can be replayed.
        [Theory]
        [InlineData("176532316352809151093929119389468516639", "188753493582645005437843730230043414099",
            "0DAFB7FD00E0F76BAAF9A736517804 29F8A2FBA73581083BF05D8C3CC511AB49",
            "OVfQuRJX05g@gOVwNJfiQTFGD6qHOLzU5BhYusDSWFYCiQM", "6A73510EC552C733")]
        [InlineData("174407169407696451849510974594705576147", "189127122418810726457008666384871172599",
            "95C6216A8DE51384AADBDF6AA1169B1A106980FE43FCEF2CD8E94D5875EBEC48",
            "OlyyM2LoViSrkkvTTuyCUsDQPvhUHOKSbQImR@QqeX3n]E|", "311DD37445716B05")]
        public void Vector10_RsaExchangeGivesTheCapturedSessionKey(string p, string q, string modulusHex, string ciphertextToken, string key8)
        {
            var rsa = PolRsaKeyExchange.FromPrimes(BigInteger.Parse(p), BigInteger.Parse(q));
            Assert.Equal(Convert.FromHexString(modulusHex.Replace(" ", "")), rsa.Modulus);
            byte[] c = PolCoreProtocol.ParseCiphertextToken(ciphertextToken);
            Assert.Equal(key8, Convert.ToHexString(rsa.DecryptSessionKey(c)));
        }

        [Fact]
        public void Rsa_NewKeyPairsWorkWithAServerThatWrapsAKey()
        {
            var rsa = new PolRsaKeyExchange();
            Assert.Equal(32, rsa.Modulus.Length);
            // 255 bits like both captured retail moduli (top byte 0x48, 0x49): bit 255 clear, bit 254 set.
            Assert.InRange(rsa.Modulus[31], 0x40, 0x7F);
            byte[] key = Convert.FromHexString("0102030405060708");
            byte[] c = PolRsaKeyExchange.Encrypt(rsa.Modulus, PolRsaKeyExchange.WrapSessionKey(key));
            Assert.Equal(key, rsa.DecryptSessionKey(c));

            // A block without PKCS#1 type-2 padding is refused.
            byte[] bad = new byte[32];
            bad[31] = 5;
            Assert.Throws<CryptographicException>(() => rsa.DecryptSessionKey(PolRsaKeyExchange.Encrypt(rsa.Modulus, bad)));
        }

        [Fact]
        public void Vector1_PolId()
        {
            // The spec's capture vector is redacted; this is the synthetic replacement (spec section 5.8, test vector 1).
            Assert.Equal("US77G53VV", PolIdEncoding.Encode("ABCD1234"));
            Assert.Equal("ABCD1234", PolIdEncoding.Decode("US77G53VV"));
        }

        [Fact]
        public void PolId_RoundTrips_AndMustBeFourLettersThenFourDigits()
        {
            foreach (string id in new[] { "ABCD1234", "ZZZZ9999", "AAAA0000", "QRST5678" })
            {
                string encoded = PolIdEncoding.Encode(id);
                Assert.Equal(9, encoded.Length);
                Assert.StartsWith("U", encoded);
                Assert.Equal(id, PolIdEncoding.Decode(encoded));
            }
            Assert.Equal(PolIdEncoding.Encode("ABCD1234"), PolIdEncoding.Encode(" abcd1234 "));
            Assert.Throws<ArgumentException>(() => PolIdEncoding.Encode("ABC12345"));
            Assert.Throws<ArgumentException>(() => PolIdEncoding.Encode("ABCD123"));
            Assert.Throws<ArgumentException>(() => PolIdEncoding.Encode("1234ABCD"));
        }

        [Fact]
        public void Vector12_RedirectTokens()
        {
            var t5 = PolCoreToken.Parse("WCMH3HGNNN4NI55OBMGAE21NNNNNNNNNY0QE3N1NvfrA");
            // The routing token: LE16(client port at +0x14) + LE32(client address at +0x04).
            Assert.Equal(0x4B27, t5.ClientPort);
            Assert.Equal("274B0A0200C0", Convert.ToHexString(t5.RoutingToken()));
            Assert.Equal(0x6AF25117u, t5.ServerTimestamp);
            Assert.Equal("192.0.2.10", t5.ClientAddress.ToString());
            Assert.Equal("202.67.54.125", t5.RedirectAddress.ToString());
            Assert.Equal(51241, t5.RedirectPort);
            Assert.Equal("6AF25117C000020ACA43367DC8290000000000004B27010100",
                Convert.ToHexString(PolBase32.Decode("WCMH3HGNNN4NI55OBMGAE21NNNNNNNNNY0QE3N1N", 25)));

            var t6 = PolCoreToken.Parse("WCM2B3GNNN4NI55OBU3AECZNNNNNNNNNXCVN3N1N}HNM");
            Assert.Equal(0x6AF2A30Bu, t6.ServerTimestamp);
            Assert.Equal("202.67.54.133", t6.RedirectAddress.ToString());
            Assert.Equal(51247, t6.RedirectPort);
        }

        [Fact]
        public void Base32_RoundTripsBytes()
        {
            byte[] data = Convert.FromHexString("6AF25117C000020ACA43367DC8290000000000004B27010100");
            Assert.Equal(data, PolBase32.Decode(PolBase32.Encode(data), data.Length));
            Assert.False(PolBase32.TryDecode("NN", new byte[2]));
            Assert.False(PolBase32.TryDecode("aaaa", new byte[2]));
            Assert.Equal(0, PolBase32.ValueOf('N'));
            Assert.Equal(31, PolBase32.ValueOf('K'));
        }

        [Fact]
        public void ClientMetaSuffix_IsTheLineWordSumInFourBiasedSlices()
        {
            // A synthetic 80-byte line: 20 little-endian words of 0x7E7E7E7E.
            byte[] line = new byte[80];
            Array.Fill(line, (byte)0x7E);
            uint expected = unchecked(20u * 0x7E7E7E7E);
            string suffix = PolClientMeta.Suffix(line);
            Assert.Equal(new string(new[]
            {
                (char)(((expected >> 26) & 0x3F) + 0x3F), (char)(((expected >> 20) & 0x3F) + 0x3F),
                (char)(((expected >> 14) & 0x3F) + 0x3F), (char)(((expected >> 8) & 0x3F) + 0x3F),
            }), suffix);
            Assert.Equal(86, PolCoreProtocol.BuildNickLine("ABCD1234", "password", "WCMH3HGNNN4NI55OBMGAE21NNNNNNNNNY0QE3N1NvfrA").Length);
        }

        [Fact]
        public void Vector2_PassResponse() =>
            Assert.Equal("fcd51ab80c161c6c3636c9d2bf5f2bc7", PolCoreProtocol.PassResponse("WCMH3HGNNN4NI55OBMGAE21NNNNNNNNNY0QE3N1NvfrA", "password"));

        [Fact]
        public void Vector16_ClientMetaBodyComesFromThePolId()
        {
            Assert.Equal("20FBE79048D7A031", Convert.ToHexString(PolClientMeta.PolIdToken("ABCD1234")));
            Assert.Equal("TTTTTAISTTTTTTTTTTTTTHBy5pjMnc0G", PolClientMeta.Body("ABCD1234"));
            byte[] body = new byte[24];
            Assert.True(PolCoreBase64.TryDecode(PolClientMeta.Body("ABCD1234"), body));
            Assert.Equal("00000002010100000000000000000000" + "96DA931B2D146D42", Convert.ToHexString(body));
        }

        // Section 4.2.2 step 5's synthetic example: the whole NICK for ABCD1234 / "password" / the vector 12 greeting.
        [Fact]
        public void NickLine_SyntheticExampleEndsInItsSuffix() =>
            Assert.Equal("NICK US77G53VV:fcd51ab80c161c6c3636c9d2bf5f2bc7:TTTTTAISTTTTTTTTTTTTTHBy5pjMnc0GpglH\r\n",
                PolCoreProtocol.BuildNickLine("ABCD1234", "password", "WCMH3HGNNN4NI55OBMGAE21NNNNNNNNNY0QE3N1NvfrA"));

        [Fact]
        public void Vector15_RequestHeaderDigest() =>
            Assert.Equal("589CFD5BEA27F8814BC2B376DC5AA173", Convert.ToHexString(PolHashes.ChallengeDigest("ABCD1234", "password", 0x6AF3F7E9)));

        [Fact]
        public void Vector3_SquareEnixPassword()
        {
            // Spec v2.1.21: the password key adds the "playonline" step; the pre-2.1.21 digest 2e6a0284... was refused live.
            Assert.Equal("2c47dceb3d437c6d414dfff87765f1b1fa72f880",
                Convert.ToHexStringLower(PolHashes.SquareEnixPasswordKey("SecretPass123")));
            Assert.Equal("9bce4cfc21dbbaa94b003314cffebd562a4a60d5",
                Convert.ToHexStringLower(PolHashes.SquareEnixPasswordDigest("SecretPass123", 1731110400)));
            // Rounded down to the minute.
            Assert.Equal(PolHashes.SquareEnixPasswordDigest("SecretPass123", 1731110400),
                PolHashes.SquareEnixPasswordDigest("SecretPass123", 1731110459));
        }

        [Fact]
        public void Vector4_AdditiveChecksum()
        {
            byte[] payload = new byte[40];
            for (int i = 0; i < 36; i++) payload[i] = (byte)(i + 1);
            Assert.Equal(0xB4ABA299u, PolHashes.AdditiveChecksum(payload.AsSpan(0, 36)));
            PolHashes.SealAdditive(payload);
            Assert.Equal(new byte[] { 0x99, 0xA2, 0xAB, 0xB4 }, payload[36..]);
            Assert.True(PolHashes.VerifyAdditive(payload));
            Assert.Equal(0x00030201u, PolHashes.AdditiveChecksum(new byte[] { 1, 2, 3 }));
        }

        private static readonly byte[] LobbyKey = Convert.FromHexString("0123456789ABCDEFFEDCBA9876543210");

        [Fact]
        public void Vector5_LobbySessionHashes()
        {
            Assert.Equal("a9b37731814689069446b8581bb145ea", Convert.ToHexStringLower(PolHashes.LobbySessionHash(LobbyKey, 0x590DF1FB)));
            Assert.Equal("906fc2f1cc7f88ae1c06c215be8eb208", Convert.ToHexStringLower(PolHashes.LobbySessionHash(LobbyKey, 0x590DF1FF)));
            Assert.Equal("67d581eb124a444564343b0c9b4ff8d4", Convert.ToHexStringLower(PolHashes.LobbySessionHash(LobbyKey, 0x590DF209)));
        }

        [Fact]
        public void MapKey_IsTheSessionHashFourModifiersAfterTheSelect()
        {
            // 0x07 at M0 + 11 gives the map key SessionHash(M0 + 15) (spec section 5.6, test vector 5).
            Assert.Equal("67d581eb124a444564343b0c9b4ff8d4", Convert.ToHexStringLower(RetailMapPackets.MapKey(LobbyKey, 0x590DF205)));
            byte[] raw = RetailMapPackets.MapRawKey(LobbyKey, 0x590DF205);
            Assert.Equal(20, raw.Length);
            Assert.Equal(RetailMapPackets.MapKey(LobbyKey, 0x590DF205), MD5.HashData(raw));
        }

        [Fact]
        public void Vector8_MapTicket() =>
            Assert.Equal("27673EA222A829989583118DF9DE80B3",
                Convert.ToHexString(RetailMapPackets.Ticket(Convert.FromHexString("919129145BDE35AFC2D02CEC08A5327B"))));

        [Fact]
        public void Vector7_PmlDigest()
        {
            string userName = PolHashes.PmlUserName(Convert.FromHexString("50C30A0200C0"));
            Assert.Equal("oz5vKRTDoxJpo8i0o8TtoT", userName);
            Assert.Equal("Iq3a9nCbEJA", PolCoreBase64.Encode(Convert.FromHexString("1122334455667788")));
            Assert.Equal("346a5b8e4e59be3d3142f8ac592340af", PolHashes.PmlDigestResponse(userName, "POL",
                Convert.FromHexString("1122334455667788"), "/pml/main/index.pml", "AAAAAA==0123456789abcdef0123456789abcdef01234567"));
        }

        [Fact]
        public void Vector11_SelectionDigest()
        {
            byte[] authCode = new byte[64];
            for (int i = 0; i < 64; i++) authCode[i] = (byte)i;
            byte[] hash = Convert.FromHexString("0123456789ABCDEF0123456789ABCDEF");
            Assert.Equal("32792248BEC991750B2050FABA185707",
                Convert.ToHexString(LobbyClientPackets.SelectionDigest(authCode, hash, 0x43FA0718, 0x016B6984)));
        }

        // Test vector 13 (capture #9, synthetic client address).
        [Fact]
        public void Vector13_AuthCodeToken()
        {
            byte[] token = PolAuthCode.GenerateWithContext(
                Convert.FromHexString("65D35CEDE299B936360CD53A60A3B677"), 0x6AF35D54, Convert.FromHexString("B96C1B71F21C375A"),
                Convert.FromHexString("010050C30A0200C0CDCCCC3E0000000000000000"));
            Assert.Equal("29B62E434CB57649EF53C174C01029A44C35BBA07932E82EDACC30F1FA30D00563F28D49E2DD54C1F11A6C9994937B0A8CB962C3",
                Convert.ToHexString(token));
        }

        [Theory]
        [InlineData("E0C5F1B0AFE100E336911696B3AED99C", "2AB52E434CB57649545 07A77C3132AA74F36B8A37A31EB2DD9CF33F269568A3B4174B1FEE6B0459C4E74FF3C4736A8AF5F1CB166")]
        [InlineData("37E09F966EF7FD1D461FD478E31E71A9", "2AB52E434CB5764962504C77C3132AA74F36B8A37A31EB2DD9CF33F26D3286F79388946DBE6D1768B8D32DD451DFBE4649F5A78F")]
        [InlineData("5488D2BF36E3AFA09FC4BAC5B9BEEDA7", "2AB52E434CB576492C500277C3132AA74F36B8A37A31EB2DD9CF33F2881154A86EC36C33621FA896A106D27AD97136E8C15B2F21")]
        public void Vector13_TheOtherBuilderCalls(string lobbyKey, string expected)
        {
            byte[] token = PolAuthCode.GenerateWithContext(Convert.FromHexString(lobbyKey), 0x6AF35D54, Convert.FromHexString("B96C1B71F21C375A"),
                Convert.FromHexString("010050C30A0200C0CDCCCC3E0000000000000000"));
            Assert.Equal(expected.Replace(" ", ""), Convert.ToHexString(token));
        }

        [Fact]
        public void AuthCode_ContextIsTheProtocolVersionTheRoutingTokenAndZeros()
        {
            byte[] lobbyKey = Convert.FromHexString("65D35CEDE299B936360CD53A60A3B677");
            byte[] k8 = Convert.FromHexString("B96C1B71F21C375A");
            byte[] routing = Convert.FromHexString("50C30A0200C0");
            Assert.Equal(PolAuthCode.GenerateWithContext(lobbyKey, 0x6AF35D54, k8, Convert.FromHexString("010050C30A0200C0000000000000000000000000")),
                PolAuthCode.Generate(lobbyKey, 0x6AF35D54, k8, routing));
        }

        [Fact]
        public void Vector14_ClientFileChecksum()
        {
            byte[] data = new byte[256];
            for (int i = 0; i < 256; i++) data[i] = (byte)i;
            Assert.Equal(0x159EE5E0u, ClientFileChecksum.Compute(data, ClientFileChecksum.FfxiMainPeriod));
        }

        // Spec v2.1.20 section 5.10 and test vector 9: the tail goes in the high bytes of the last word.
        [Fact]
        public void LineChecksum_PutsTheTailInTheHighBytes()
        {
            byte[] head = Encoding.ASCII.GetBytes("USER x 8 * :rYsUyi_rIJqPf66Pi9yBcUS4Ag18XONjfOrkmWZ@lIA");
            Assert.Equal(55, head.Length);
            Assert.Equal(0x33629167u, PolHashes.LineChecksum(head));
            Assert.Equal("KuIP", PolHashes.LineSuffix(head));
            // A 3-byte tail is u32_le(00 01 02 03), not the :51220 checksum's u32_le(01 02 03 00).
            Assert.Equal(0x03020100u, PolHashes.LineChecksum(new byte[] { 1, 2, 3 }));
            Assert.Equal(0x00030201u, PolHashes.AdditiveChecksum(new byte[] { 1, 2, 3 }));
        }

        [Theory]
        [InlineData("USER x 8 * :8yu_X78A6fDPuyYfeZAIFVxxuhY0A7AlNnfogoe9Pd5UDWB")]
        [InlineData("USER x 8 * :rYsUyi_rIJqPf66Pi9yBcUS4Ag18XONjfOrkmWZ@lIAKuIP")]
        [InlineData(":pol-1044-51244.pol.com 300 * OVfQuRJX05g@gOVwNJfiQTFGD6qHOLzU5BhYusDSWFYCiQM")]
        [InlineData(":202.67.54.126 300 * OlyyM2LoViSrkkvTTuyCUsDQPvhUHOKSbQImR@QqeX3n]E|")]
        public void LineChecksum_MatchesCapturedLines(string line) => Assert.True(PolHashes.HasValidLineSuffix(line));
    }
}
