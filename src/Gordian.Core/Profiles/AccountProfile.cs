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
        public string CharacterName { get; set; } = string.Empty;
        public string BootloaderPath { get; set; } = string.Empty;
        public string Arguments { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the folder group path for this profile (e.g. "" for root, "Test1", "Test2/Test3").
        /// </summary>
        public string Folder { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the target game server host or IP (e.g. "127.0.0.1" or "play.myserver.net").
        /// </summary>
        public string ServerHost { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the authentication/connect port for the target game server (default 54231).
        /// </summary>
        public int ServerPort { get; set; } = 54231;

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

        /// <summary>
        /// Gets or sets whether this profile is checked for launching.
        /// Persisted across client restarts.
        /// </summary>
        public bool IsSelectedForLaunch { get; set; }

        /// <summary>
        /// Natively compiles the dynamic, live 6-digit login token.
        /// </summary>
        [JsonIgnore]
        public string CurrentTwoFactorCode => TotpEngine.GenerateCurrentCode(OtpSeed);

        /// <summary>
        /// Creates a deep copy of this account profile with an optional new name.
        /// </summary>
        public AccountProfile Clone(string? newProfileName = null)
        {
            return new AccountProfile
            {
                ProfileName = newProfileName ?? ProfileName,
                CharacterName = CharacterName,
                BootloaderPath = BootloaderPath,
                Arguments = Arguments,
                Username = Username,
                Folder = Folder,
                ServerHost = ServerHost,
                ServerPort = ServerPort,
                Password = Password,
                OtpSeed = OtpSeed,
                IsSelectedForLaunch = IsSelectedForLaunch
            };
        }

        /// <summary>
        /// Computes the relative file path for this profile within the profiles directory.
        /// </summary>
        public string GetRelativeFilePath()
        {
            string cleanName = string.Concat(ProfileName.Split(Path.GetInvalidFileNameChars()));
            if (string.IsNullOrWhiteSpace(Folder))
            {
                return $"{cleanName}.json";
            }

            string[] parts = Folder.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
            return Path.Combine(Path.Combine(parts), $"{cleanName}.json");
        }

        public void SaveToFile(string folderPath)
        {
            string relPath = GetRelativeFilePath();
            string targetPath = Path.Combine(folderPath, relPath);
            string? targetDir = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrEmpty(targetDir) && !Directory.Exists(targetDir))
            {
                Directory.CreateDirectory(targetDir);
            }

            string json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(targetPath, json);
        }

        public void DeleteFile(string folderPath)
        {
            string relPath = GetRelativeFilePath();
            string targetPath = Path.Combine(folderPath, relPath);
            if (File.Exists(targetPath))
            {
                File.Delete(targetPath);
            }

            // Clean up any legacy root file if moving out of root or renaming
            string cleanName = string.Concat(ProfileName.Split(Path.GetInvalidFileNameChars()));
            string rootPath = Path.Combine(folderPath, $"{cleanName}.json");
            if (!string.Equals(targetPath, rootPath, StringComparison.OrdinalIgnoreCase) && File.Exists(rootPath))
            {
                File.Delete(rootPath);
            }
        }

        public static AccountProfile? LoadFromFile(string filePath, string? baseDirectory = null)
        {
            if (!File.Exists(filePath)) return null;

            try
            {
                string json = File.ReadAllText(filePath);
                var profile = JsonSerializer.Deserialize<AccountProfile>(json);
                if (profile != null && !string.IsNullOrEmpty(baseDirectory))
                {
                    string relDir = Path.GetRelativePath(baseDirectory, Path.GetDirectoryName(filePath) ?? baseDirectory);
                    if (relDir != "." && !string.IsNullOrWhiteSpace(relDir) && string.IsNullOrWhiteSpace(profile.Folder))
                    {
                        profile.Folder = relDir.Replace('\\', '/');
                    }
                }
                return profile;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AccountProfile] Failed to load profile from '{filePath}': {ex.Message}");
                return null;
            }
        }
    }
}
