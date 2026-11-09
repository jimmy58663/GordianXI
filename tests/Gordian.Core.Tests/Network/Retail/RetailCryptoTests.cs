// tests/Gordian.Core.Tests/Network/Retail/RetailCryptoTests.cs
using System;
using System.Buffers.Binary;
using System.Text;
using Gordian.Core.Network.Retail;
using Xunit;

namespace Gordian.Core.Tests.Network.Retail
{
    /// <summary>
    /// Retail login primitives against the worked vectors in docs/network/retail-login-spec.md v1.5.0 section 5.8, and
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
        public void Vector6_OfbKeyExpansion()
        {
            uint[] words = PolStreamCipher.ExpandKey(Convert.FromHexString("0123456789ABCDEF"));
            Assert.Equal(new uint[]
            {
                0x1F1DCDA1, 0x681049C6, 0x280710D9, 0x8944D83E, 0xA2C3FF4A,
                0x596F31BD, 0x44FFCD55, 0x153E128A, 0x67452301, 0xEFCDAB89,
            }, words);
            Assert.Equal(0xC7587E52u, PolStreamCipher.IvLow);
            Assert.Equal(0x329AC3D4u, PolStreamCipher.IvHigh);
        }

        [Fact]
        public void StreamCipher_KeystreamIsBlowfishOfTheFeedbackRegister_WithTheExpandedKey()
        {
            byte[] k8 = Convert.FromHexString("0102030405060708");
            uint[] words = PolStreamCipher.ExpandKey(k8);
            Assert.Equal(0x04030201u, words[8]);
            Assert.Equal(0x08070605u, words[9]);

            // The same schedule through the standard byte-key path: the 10 words as a 40-byte big-endian key.
            byte[] keyBytes = new byte[40];
            for (int i = 0; i < 10; i++) BinaryPrimitives.WriteUInt32BigEndian(keyBytes.AsSpan(i * 4), words[i]);
            var bf = StandardBlowfish.FromKeyBytes(keyBytes);

            uint lo = PolStreamCipher.IvLow, hi = PolStreamCipher.IvHigh;
            byte[] expected = new byte[16];
            for (int block = 0; block < 2; block++)
            {
                bf.Encrypt(ref lo, ref hi);
                BinaryPrimitives.WriteUInt32LittleEndian(expected.AsSpan(block * 8), lo);
                BinaryPrimitives.WriteUInt32LittleEndian(expected.AsSpan(block * 8 + 4), hi);
            }

            // Zero bytes encrypt to the keystream itself, except where the keystream byte is CR or LF.
            byte[] data = new byte[16];
            new PolStreamCipher(k8).TransformFrame(data);
            for (int i = 0; i < 16; i++)
            {
                byte want = expected[i] is 0x0A or 0x0D ? (byte)0 : expected[i];
                Assert.Equal(want, data[i]);
            }
        }

        [Fact]
        public void StreamCipher_RoundTripsAndKeepsLineEnds()
        {
            var cipher = new PolStreamCipher(Convert.FromHexString("A1B2C3D4E5F60718"));
            byte[] line = Encoding.ASCII.GetBytes("NICK 232JR2NC1:5f4dcc3b5aa765d61d8327deb882cf99:META\r\n");
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
        public void StreamCipher_AContinuationCarriesOnTheKeystream()
        {
            byte[] k8 = Convert.FromHexString("1122334455667788");
            byte[] whole = new byte[104];
            new PolStreamCipher(k8).TransformFrame(whole);

            var split = new PolStreamCipher(k8);
            byte[] header = new byte[40], payload = new byte[64];
            split.TransformFrame(header);
            split.Transform(payload);
            Assert.Equal(whole.AsSpan(0, 40).ToArray(), header);
            Assert.Equal(whole.AsSpan(40).ToArray(), payload);
        }

        [Fact]
        public void DiffieHellman_BothSidesAgree_AndTheKeyIsTheSecretsTailReversed()
        {
            var client = new PolDiffieHellman();
            var server = new PolDiffieHellman();
            byte[] s1 = client.ComputeSharedSecret(server.PublicValue);
            byte[] s2 = server.ComputeSharedSecret(client.PublicValue);
            Assert.Equal(s1, s2);
            Assert.Equal(32, s1.Length);

            byte[] key = PolDiffieHellman.ExtractKey(s1);
            for (int i = 0; i < 8; i++) Assert.Equal(s1[31 - i], key[i]);
        }

        [Fact]
        public void DiffieHellman_TinyExponentsAreRaised_AndBadServerValuesRefused()
        {
            byte[] one = new byte[32];
            one[31] = 1;
            // Exponents below 2 are raised by 2 (1 becomes 3), so A = 67^3.
            var dh = new PolDiffieHellman(one);
            Assert.Equal(67u * 67u * 67u, BinaryPrimitives.ReadUInt32BigEndian(dh.PublicValue.AsSpan(28)));
            Assert.Throws<System.Security.Cryptography.CryptographicException>(() => dh.ComputeSharedSecret(new byte[32]));
        }

        [Fact]
        public void Vector1_PolId() => Assert.Equal("232JR2NC1", PolBase32.EncodePolId("TEST0001"));

        [Fact]
        public void PolId_IsCaseInsensitive_AndMustBeEightAlphanumerics()
        {
            Assert.Equal("232JR2NC1", PolBase32.EncodePolId(" test0001 "));
            Assert.Throws<ArgumentException>(() => PolBase32.EncodePolId("TEST001"));
            Assert.Throws<ArgumentException>(() => PolBase32.EncodePolId("TEST-001"));
        }

        [Fact]
        public void Base32_RoundTripsBytes_AndPadsWithZeroCharacters()
        {
            byte[] data = Convert.FromHexString("0ECC10D59F4C00112233445566778899AABBCCDDEEFF");
            string text = PolBase32.Encode(data, minimumLength: 40);
            Assert.Equal(40, text.Length);
            Assert.EndsWith("0", text);
            Assert.Equal(data, PolBase32.Decode(text, data.Length));
            Assert.False(PolBase32.TryDecode("ab", new byte[1]));
            Assert.Equal(0, PolBase32.ValueOf('0'));
            Assert.Equal(6, PolBase32.ValueOf('A'));
            Assert.Equal(31, PolBase32.ValueOf('Z'));
        }

        [Fact]
        public void Vector2_PolPassword() => Assert.Equal("5f4dcc3b5aa765d61d8327deb882cf99", PolHashes.PolPasswordHex("password"));

        [Fact]
        public void Vector3_SquareEnixPassword()
        {
            Assert.Equal("2e6a0284491d4529a6d115832e6eb389af4abc56",
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
        public void Vector5_LobbySessionHash() =>
            Assert.Equal("a9b37731814689069446b8581bb145ea", Convert.ToHexStringLower(PolHashes.LobbySessionHash(LobbyKey, 0x590DF1FB)));

        [Fact]
        public void Vector5_MapKeyIsTheSelectSessionHash()
        {
            Assert.Equal("906fc2f1cc7f88ae1c06c215be8eb208", Convert.ToHexStringLower(PolHashes.LobbySessionHash(LobbyKey, 0x590DF1FF)));

            // The 20-byte raw key the map session cipher takes MD5s to the same hash.
            byte[] raw = RetailLobbyPackets.MapRawKey(LobbyKey, 0x590DF1FF);
            Assert.Equal(20, raw.Length);
            Assert.Equal("906fc2f1cc7f88ae1c06c215be8eb208", Convert.ToHexStringLower(System.Security.Cryptography.MD5.HashData(raw)));
        }

        [Fact]
        public void Vector8_MapLoginTicket() =>
            Assert.Equal("1388187572a8951b84a175e22a791eee", Convert.ToHexStringLower(
                RetailLobbyPackets.MapLoginTicket("testuser", Convert.FromHexString("e18b4a509c41dbdb8599a1de20f434d3"))));

        [Fact]
        public void Vector7_PmlDigest()
        {
            string response = PolHashes.PmlDigestResponse("testuser", "main@wh000.pol.com",
                Convert.FromHexString("112233445566778899AABBCCDDEEFF00"), "GET", "/pml/main/index.pml",
                "46characternoncevalue1234567890abcdefghijklmno", "00000001", "12345678", "auth");
            Assert.Equal("9c864dbe656e343b094ce9a5a02ea760", response);
        }

        [Fact]
        public void Crc32_IsIeee() =>
            Assert.Equal(0xCBF43926u, PolHashes.Crc32(Encoding.ASCII.GetBytes("123456789")));
    }
}
