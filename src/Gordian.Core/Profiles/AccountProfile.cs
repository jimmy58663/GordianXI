// src/Gordian.Core/Profiles/AccountProfile.cs
using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Gordian.Core.Profiles
{
    public sealed class AccountProfile
    {
        private string _rawPassword = string.Empty;
        private string _rawOtpSeed = string.Empty;

        public string ProfileName { get; set; } = "Default Profile";
        public string BootloaderPath { get; set; } = string.Empty;
        public string Arguments { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the password string. Backed by machine-isolated symmetric encryption.
        /// </summary>
        public string Password
        {
            get => ProfileCrypto.Decrypt(_rawPassword);
            set => _rawPassword = ProfileCrypto.Encrypt(value);
        }

        /// <summary>
        /// Gets or sets the Time-Based Authenticator seed key string. Backed by machine-isolated symmetric encryption.
        /// </summary>
        public string OtpSeed
        {
            get => ProfileCrypto.Decrypt(_rawOtpSeed);
            set => _rawOtpSeed = ProfileCrypto.Encrypt(value);
        }

        // Intercept standard serialization tasks to write encrypted blocks to disk
        [JsonPropertyName("ProtectedPassword")]
        public string EncryptedPasswordSerialized
        {
            get => _rawPassword;
            set => _rawPassword = value;
        }

        [JsonPropertyName("ProtectedOtpSeed")]
        public string EncryptedOtpSeedSerialized
        {
            get => _rawOtpSeed;
            set => _rawOtpSeed = value;
        }

        [JsonIgnore]
        public bool IsSelectedForLaunch { get; set; }

        /// <summary>
        /// Natively compiles the dynamic, live 6-digit login token.
        /// </summary>
        [JsonIgnore]
        public string CurrentTwoFactorCode => TotpEngine.GenerateCurrentCode(OtpSeed);

        public void SaveToFile(string folderPath)
        {
            string cleanName = string.Concat(ProfileName.Split(Path.GetInvalidFileNameChars()));
            string targetPath = Path.Combine(folderPath, $"{cleanName}.json");
            
            string json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(targetPath, json);
        }

        public static AccountProfile? LoadFromFile(string filePath)
        {
            if (!File.Exists(filePath)) return null;
            
            string json = File.ReadAllText(filePath);
            return JsonSerializer.Deserialize<AccountProfile>(json);
        }
    }
}
