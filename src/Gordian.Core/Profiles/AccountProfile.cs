// src/Gordian.Core/Profiles/AccountProfile.cs
using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Gordian.Core.Profiles
{
    public sealed class AccountProfile
    {
        public string ProfileName { get; set; } = "Default Profile";
        public string BootloaderPath { get; set; } = string.Empty;
        public string Arguments { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the plaintext password in memory.
        /// Automatically encrypted via AES-GCM when serialized to disk.
        /// </summary>
        [JsonConverter(typeof(EncryptedJsonConverter))]
        public string Password { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the plaintext Time-Based Authenticator seed key in memory.
        /// Automatically encrypted via AES-GCM when serialized to disk.
        /// </summary>
        [JsonConverter(typeof(EncryptedJsonConverter))]
        public string OtpSeed { get; set; } = string.Empty;


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
