// tests/Gordian.Core.Tests/Network/LegacyBlowfishCryptoSuiteTests.cs
using System;
using System.Text;
using Gordian.Core.Network.Crypto;
using Xunit;

namespace Gordian.Core.Tests.Network
{
    public class LegacyBlowfishCryptoSuiteTests
    {
        [Fact]
        public void LegacyBlowfish_EncryptAndDecrypt_RoundTripsAccurately()
        {
            using var suite = new LegacyBlowfishCryptoSuite();
            byte[] key = Encoding.ASCII.GetBytes("SecretSessionKey");
            suite.InitializeKey(key);

            Assert.True(suite.IsKeyInitialized);

            const int headerSize = 28;
            byte[] payload = Encoding.ASCII.GetBytes("TestPayloadDataForFFXIBlowfishECBRegionVerification!");
            byte[] datagram = new byte[headerSize + payload.Length + 64];

            // Put header bytes
            for (int i = 0; i < headerSize; i++)
            {
                datagram[i] = (byte)(i + 1);
            }
            // Copy payload after header
            payload.CopyTo(datagram, headerSize);

            int totalLength = suite.EncryptAndSign(datagram, headerSize, payload.Length);
            Assert.Equal(headerSize + payload.Length + 16, totalLength);

            // Verify payload is encrypted (does not match original plaintext)
            Assert.NotEqual(payload, datagram.AsSpan(headerSize, payload.Length).ToArray());

            // Decrypt and verify
            bool success = suite.TryDecryptAndVerify(datagram.AsSpan(0, totalLength), headerSize, out int decryptedLength);

            Assert.True(success);
            Assert.Equal(payload.Length, decryptedLength);
            Assert.Equal(payload, datagram.AsSpan(headerSize, decryptedLength).ToArray());
        }

        [Fact]
        public void LegacyBlowfish_TamperedChecksum_FailsVerification()
        {
            using var suite = new LegacyBlowfishCryptoSuite();
            byte[] key = Encoding.ASCII.GetBytes("Key123");
            suite.InitializeKey(key);

            const int headerSize = 28;
            byte[] payload = Encoding.ASCII.GetBytes("TamperTestPayload");
            byte[] datagram = new byte[headerSize + payload.Length + 32];
            payload.CopyTo(datagram, headerSize);

            int totalLength = suite.EncryptAndSign(datagram, headerSize, payload.Length);

            // Tamper with one byte in the encrypted region
            datagram[headerSize + 2] ^= 0xFF;

            bool success = suite.TryDecryptAndVerify(datagram.AsSpan(0, totalLength), headerSize, out int decryptedLength);
            Assert.False(success);
            Assert.Equal(0, decryptedLength);
        }

        [Fact]
        public void LegacyBlowfish_BinaryKeyWithHighBytes_EncryptAndDecrypt_RoundTrips()
        {
            using var suite = new LegacyBlowfishCryptoSuite();
            // Binary 20-byte key containing bytes >= 0x80
            byte[] binaryKey = Convert.FromHexString("7CE2BA9C4525C8078DE4FDE134FD83BA40F29BA9");
            suite.InitializeKey(binaryKey);

            Assert.True(suite.IsKeyInitialized);

            const int headerSize = 28;
            byte[] payload = Encoding.ASCII.GetBytes("BinaryKeyVerificationPayload1234567890!");
            byte[] datagram = new byte[headerSize + payload.Length + 64];
            payload.CopyTo(datagram, headerSize);

            int totalLength = suite.EncryptAndSign(datagram, headerSize, payload.Length);
            bool success = suite.TryDecryptAndVerify(datagram.AsSpan(0, totalLength), headerSize, out int decryptedLength);

            Assert.True(success);
            Assert.Equal(payload.Length, decryptedLength);
            Assert.Equal(payload, datagram.AsSpan(headerSize, decryptedLength).ToArray());
        }
    }
}
