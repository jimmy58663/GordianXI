// src/Gordian.Core/Profiles/EncryptedJsonConverter.cs
using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Gordian.Core.Profiles
{
    /// <summary>
    /// A System.Text.Json converter that transparently encrypts sensitive strings using ProfileCrypto
    /// when saving to disk, and decrypts them when reading from disk.
    /// </summary>
    public sealed class EncryptedJsonConverter : JsonConverter<string>
    {
        public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            string? value = reader.GetString();
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            return ProfileCrypto.Decrypt(value);
        }

        public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
        {
            if (string.IsNullOrEmpty(value))
            {
                writer.WriteStringValue(string.Empty);
                return;
            }

            // Always write the encrypted ciphertext representation to disk
            string cipherText = ProfileCrypto.Encrypt(value);
            writer.WriteStringValue(cipherText);
        }
    }
}
