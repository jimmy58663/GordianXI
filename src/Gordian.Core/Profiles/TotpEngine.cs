// src/Gordian.Core/Profiles/TotpEngine.cs
using System;
using System.Security.Cryptography;

namespace Gordian.Core.Profiles
{
    public static class TotpEngine
    {
        /// <summary>
        /// Generates a synchronized 6-digit Time-Based One-Time Password from a Base32 authentication secret string.
        /// </summary>
        public static string GenerateCurrentCode(string base32Secret)
        {
            if (string.IsNullOrEmpty(base32Secret)) return "000000";

            byte[] secretBytes = Base32Decode(base32Secret);
            
            // Calculate Unix timestamp 30-second window step index parameters
            long unixTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            long timeStep = unixTime / 30;

            byte[] stepBytes = BitConverter.GetBytes(timeStep);
            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(stepBytes); // Force Big-Endian network tracking byte format
            }

            byte[] hash;
            using (var hmac = new HMACSHA1(secretBytes))
            {
                hash = hmac.ComputeHash(stepBytes);
            }

            // Perform dynamic truncation selection properties matching standard RFC 4226 / 6238 protocols
            int offset = hash[hash.Length - 1] & 0x0F;
            int binaryCode = ((hash[offset] & 0x7F) << 24) |
                             ((hash[offset + 1] & 0xFF) << 16) |
                             ((hash[offset + 2] & 0xFF) << 8) |
                             (hash[offset + 3] & 0xFF);

            int otpValue = binaryCode % 1000000;
            return otpValue.ToString("D6");
        }

        private static byte[] Base32Decode(string base32Value)
        {
            base32Value = base32Value.Trim().ToUpperInvariant().Replace("-", "");
            if (string.IsNullOrEmpty(base32Value)) return Array.Empty<byte>();

            const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
            int outputLength = base32Value.Length * 5 / 8;
            byte[] result = new byte[outputLength];

            int buffer = 0;
            int bitsLeft = 0;
            int resultIndex = 0;

            for (int i = 0; i < base32Value.Length; i++)
            {
                int charValue = alphabet.IndexOf(base32Value[i]);
                if (charValue < 0) continue; // Skip illegal characters gracefully

                buffer = (buffer << 5) | charValue;
                bitsLeft += 5;

                if (bitsLeft >= 8)
                {
                    bitsLeft -= 8;
                    result[resultIndex++] = (byte)((buffer >> bitsLeft) & 0xFF);
                }
            }

            return result;
        }
    }
}
