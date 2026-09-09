// src/Gordian.Core/Profiles/ProfileCrypto.cs
using System;
using System.Security.Cryptography;
using System.Text;

namespace Gordian.Core.Profiles
{
    public static class ProfileCrypto
    {
        private const int NonceSizeInBytes = 12;
        private const int TagSizeInBytes = 16;
        private const int SaltSizeInBytes = 16;
        private const int Pbkdf2Iterations = 100000;

        /// <summary>
        /// Encrypts sensitive plaintext strings using unique salts and the OS-Vault Master Key.
        /// </summary>
        public static string Encrypt(string plainText)
        {
            if (string.IsNullOrEmpty(plainText)) return string.Empty;

            byte[] plaintextBytes = Encoding.UTF8.GetBytes(plainText);
            byte[] salt = new byte[SaltSizeInBytes];
            byte[] nonce = new byte[NonceSizeInBytes];
            byte[] tag = new byte[TagSizeInBytes];
            byte[] ciphertext = new byte[plaintextBytes.Length];

            // Fill parameters with unique, cryptographically random entropy data
            RandomNumberGenerator.Fill(salt);
            RandomNumberGenerator.Fill(nonce);

            // Derive a unique encryption key specific *only* to this operation using your unguessable Master Key
            byte[] derivedEncryptionKey = Rfc2898DeriveBytes.Pbkdf2(
                MasterKeyStore.MasterKey,
                salt,
                Pbkdf2Iterations,
                HashAlgorithmName.SHA256,
                outputLength: 32
            );

            using (var aesGcm = new AesGcm(derivedEncryptionKey, TagSizeInBytes))
            {
                aesGcm.Encrypt(nonce, plaintextBytes, ciphertext, tag);
            }

            // Pack the unique Salt along with the standard parameters into the serialized string
            byte[] combinedPacket = new byte[salt.Length + nonce.Length + tag.Length + ciphertext.Length];
            Buffer.BlockCopy(salt, 0, combinedPacket, 0, salt.Length);
            Buffer.BlockCopy(nonce, 0, combinedPacket, salt.Length, nonce.Length);
            Buffer.BlockCopy(tag, 0, combinedPacket, salt.Length + nonce.Length, tag.Length);
            Buffer.BlockCopy(ciphertext, 0, combinedPacket, salt.Length + nonce.Length + tag.Length, ciphertext.Length);

            return Convert.ToBase64String(combinedPacket);
        }

        /// <summary>
        /// Decrypts a profile cipher block using its embedded unique salt tracking vector.
        /// </summary>
        public static string Decrypt(string base64Ciphertext)
        {
            if (string.IsNullOrEmpty(base64Ciphertext)) return string.Empty;

            byte[] combinedPacket = Convert.FromBase64String(base64Ciphertext);

            int overheadSize = SaltSizeInBytes + NonceSizeInBytes + TagSizeInBytes;
            int ciphertextLength = combinedPacket.Length - overheadSize;
            if (ciphertextLength <= 0) return string.Empty;

            byte[] salt = new byte[SaltSizeInBytes];
            byte[] nonce = new byte[NonceSizeInBytes];
            byte[] tag = new byte[TagSizeInBytes];
            byte[] ciphertext = new byte[ciphertextLength];

            // Extract the embedded unique salt structure
            Buffer.BlockCopy(combinedPacket, 0, salt, 0, SaltSizeInBytes);
            Buffer.BlockCopy(combinedPacket, SaltSizeInBytes, nonce, 0, NonceSizeInBytes);
            Buffer.BlockCopy(combinedPacket, SaltSizeInBytes + NonceSizeInBytes, tag, 0, TagSizeInBytes);
            Buffer.BlockCopy(combinedPacket, SaltSizeInBytes + NonceSizeInBytes + TagSizeInBytes, ciphertext, 0, ciphertextLength);

            // Re-derive the exact identical key layout using the embedded unique salt
            byte[] derivedEncryptionKey = Rfc2898DeriveBytes.Pbkdf2(
                MasterKeyStore.MasterKey,
                salt,
                Pbkdf2Iterations,
                HashAlgorithmName.SHA256,
                outputLength: 32
            );

            byte[] decryptedBytes = new byte[ciphertextLength];

            using (var aesGcm = new AesGcm(derivedEncryptionKey, TagSizeInBytes))
            {
                aesGcm.Decrypt(nonce, ciphertext, tag, decryptedBytes);
            }

            return Encoding.UTF8.GetString(decryptedBytes);
        }
    }
}
