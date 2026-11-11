// src/Gordian.Core/Profiles/AccountProfile.cs
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Gordian.Core.Profiles
{
    /// <summary>How a LandSandBoat profile picks its character at launch.</summary>
    public enum ProfileLoginMode
    {
        /// <summary>The character select screen (the lobby), as the retail client always shows.</summary>
        CharacterSelect,

        /// <summary>Straight into the game as the character named by <see cref="AccountProfile.CharacterName"/> or <see cref="AccountProfile.CharacterSlot"/>.</summary>
        AutoLogin,
    }

    /// <summary>Which kind of server a profile logs in to; it decides which fields the profile editor asks for.</summary>
    public enum ProfileServerType
    {
        /// <summary>Square Enix's retail service: POL ID and password, then the Square Enix account (docs/network/retail-login-spec.md).</summary>
        Retail,

        /// <summary>A LandSandBoat private server: account and password at a server host and port.</summary>
        LandSandBoat,
    }

    /// <summary>Where a profile's one-time password (the 6-digit authenticator code) comes from at login.</summary>
    public enum TwoFactorMode
    {
        /// <summary>The account has no authenticator; no code is sent.</summary>
        None,

        /// <summary>GordianXI computes the code from the stored authenticator key (<see cref="AccountProfile.OtpSeed"/>).</summary>
        AuthenticatorKey,

        /// <summary>The player types the current code on the profile's card before launching; it is never saved.</summary>
        EnterAtLaunch,
    }

    public sealed class AccountProfile
    {
        /// <summary>LandSandBoat's default auth (login) port.</summary>
        public const int DefaultLsbAuthPort = 54231;

        /// <summary>LandSandBoat's default data (xi_data) port.</summary>
        public const int DefaultLsbDataPort = 54230;

        /// <summary>LandSandBoat's default view (xi_view) port.</summary>
        public const int DefaultLsbViewPort = 54001;

        public string ProfileName { get; set; } = "Default Profile";
        public string CharacterName { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the LandSandBoat character slot (1-16, the position in the xi_view 0x20 character list, as the
        /// retail select screen shows it) to log in as. 0 (also what an older profile file without this field loads as)
        /// means "not set": the character is picked by <see cref="CharacterName"/>. The name wins when both are set.
        /// </summary>
        public int CharacterSlot { get; set; }

        /// <summary>
        /// The login mode the player chose, or null for a profile saved before the choice existed: such a profile logs in
        /// automatically when it names a character (name or slot) and opens character select otherwise
        /// (<see cref="EffectiveLoginMode"/>).
        /// </summary>
        public ProfileLoginMode? LoginMode { get; set; }

        /// <summary>Whether the profile names a character to log in as (a name, or a slot 1-16).</summary>
        [JsonIgnore]
        public bool HasCharacter => !string.IsNullOrWhiteSpace(CharacterName) || CharacterSlot is >= 1 and <= 16;

        /// <summary>
        /// The mode in effect: Auto-Login only when chosen (or, for an older profile, implied) and a character is named;
        /// otherwise character select.
        /// </summary>
        [JsonIgnore]
        public ProfileLoginMode EffectiveLoginMode =>
            (LoginMode ?? (HasCharacter ? ProfileLoginMode.AutoLogin : ProfileLoginMode.CharacterSelect)) == ProfileLoginMode.AutoLogin && HasCharacter
                ? ProfileLoginMode.AutoLogin
                : ProfileLoginMode.CharacterSelect;

        /// <summary>
        /// The server type the player chose, or null for a profile saved before the choice existed: such a profile is a
        /// LandSandBoat profile when it names a server host (or used xiloader or a server argument) and retail otherwise
        /// (<see cref="EffectiveServerType"/>).
        /// </summary>
        public ProfileServerType? ServerType { get; set; }

        /// <summary>The server type in effect: the chosen one, or for an older profile the one its fields imply.</summary>
        [JsonIgnore]
        public ProfileServerType EffectiveServerType => ServerType ?? InferLegacyServerType();

        /// <summary>
        /// The bootloader of the old retail launch path (pol.exe through the <c>ProxyStager</c> COM proxy). No longer
        /// edited: it is kept so profiles that have one still launch that way until direct retail login is proven.
        /// </summary>
        public string BootloaderPath { get; set; } = string.Empty;

        /// <summary>
        /// The old bootloader's command line. No longer edited; still read for the server host and ports older
        /// LandSandBoat profiles carried in it (<c>--server</c>, <c>--authport</c>, <c>--dataport</c>, <c>--viewport</c>).
        /// </summary>
        public string Arguments { get; set; } = string.Empty;

        /// <summary>The account name: the LandSandBoat account, or on retail the Square Enix ID.</summary>
        public string Username { get; set; } = string.Empty;

        /// <summary>The retail PlayOnline ID (8 characters). Retail profiles only.</summary>
        public string PolId { get; set; } = string.Empty;

        /// <summary>
        /// The retail PlayOnline password (up to 16 characters) in memory. Retail profiles only. Encrypted with AES-GCM
        /// when serialized to disk.
        /// </summary>
        [JsonConverter(typeof(EncryptedJsonConverter))]
        public string PolPassword { get; set; } = string.Empty;

        /// <summary>
        /// The FFXI install this profile plays with, or empty for the one found in the registry. A private server can
        /// ship its own install with changed DATs (Horizon does, applied through XIPivot), and it can lag behind retail.
        /// </summary>
        public string GameDirectory { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the folder group path for this profile (e.g. "" for root, "Test1", "Test2/Test3"). Set by
        /// dragging the profile in the launch list.
        /// </summary>
        public string Folder { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the target game server host or IP (e.g. "127.0.0.1" or "play.myserver.net").
        /// </summary>
        public string ServerHost { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the authentication/connect port for the target game server (default 54231).
        /// </summary>
        public int ServerPort { get; set; } = DefaultLsbAuthPort;

        /// <summary>The LandSandBoat data (xi_data) port, or 0 for the default (or the one an older profile's arguments name).</summary>
        public int DataPort { get; set; }

        /// <summary>The LandSandBoat view (xi_view) port, or 0 for the default (or the one an older profile's arguments name).</summary>
        public int ViewPort { get; set; }

        /// <summary>The server host in effect: <see cref="ServerHost"/>, or an older profile's <c>--server</c> argument.</summary>
        [JsonIgnore]
        public string EffectiveServerHost =>
            !string.IsNullOrWhiteSpace(ServerHost) ? ServerHost.Trim() : LegacyServerHost();

        /// <summary>The auth port in effect: <see cref="ServerPort"/>, an older profile's <c>--authport</c>, or 54231.</summary>
        [JsonIgnore]
        public int EffectiveServerPort =>
            ServerPort > 0 ? ServerPort : ParsePortArgument(Arguments, "--authport", DefaultLsbAuthPort);

        /// <summary>The data port in effect: <see cref="DataPort"/>, an older profile's <c>--dataport</c>, or 54230.</summary>
        [JsonIgnore]
        public int EffectiveDataPort =>
            DataPort > 0 ? DataPort : ParsePortArgument(Arguments, "--dataport", DefaultLsbDataPort);

        /// <summary>The view port in effect: <see cref="ViewPort"/>, an older profile's <c>--viewport</c>, or 54001.</summary>
        [JsonIgnore]
        public int EffectiveViewPort =>
            ViewPort > 0 ? ViewPort : ParsePortArgument(Arguments, "--viewport", DefaultLsbViewPort);

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
        /// The 2FA mode the player chose, or null for a profile saved before the choice existed: such a profile uses its
        /// authenticator key when it has one and no code otherwise (<see cref="EffectiveTwoFactorMode"/>).
        /// </summary>
        public TwoFactorMode? TwoFactor { get; set; }

        /// <summary>The 2FA mode in effect.</summary>
        [JsonIgnore]
        public TwoFactorMode EffectiveTwoFactorMode =>
            TwoFactor ?? (string.IsNullOrWhiteSpace(OtpSeed) ? TwoFactorMode.None : TwoFactorMode.AuthenticatorKey);

        /// <summary>
        /// The code the player typed on the profile's card for <see cref="TwoFactorMode.EnterAtLaunch"/>. Memory only:
        /// never saved, and cleared once a launch has used it.
        /// </summary>
        [JsonIgnore]
        public string EnteredOneTimeCode { get; set; } = string.Empty;

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
        /// The one-time password to send at login for the profile's 2FA mode: the computed code, the typed one, or empty.
        /// </summary>
        public string ResolveOneTimeCode() => EffectiveTwoFactorMode switch
        {
            TwoFactorMode.AuthenticatorKey => string.IsNullOrWhiteSpace(OtpSeed) ? string.Empty : CurrentTwoFactorCode,
            TwoFactorMode.EnterAtLaunch => EnteredOneTimeCode.Trim(),
            _ => string.Empty,
        };

        /// <summary>Whether <paramref name="code"/> is a 6-digit one-time password.</summary>
        public static bool IsValidOneTimeCode(string? code) =>
            code is { Length: 6 } && code.All(char.IsAsciiDigit);

        /// <summary>
        /// Why the profile cannot be launched as it is, or null. Checks only what the profile itself holds (the server,
        /// the code for the chosen 2FA mode), not whether the account is already online.
        /// </summary>
        public string? GetLaunchProblem()
        {
            if (EffectiveServerType == ProfileServerType.LandSandBoat && string.IsNullOrWhiteSpace(EffectiveServerHost))
                return "has no server host";
            // A retail profile without the old bootloader logs in directly: it needs both accounts.
            if (EffectiveServerType == ProfileServerType.Retail && string.IsNullOrWhiteSpace(BootloaderPath))
            {
                if (!Network.Retail.PolIdEncoding.IsValid((PolId ?? string.Empty).Trim().ToUpperInvariant()))
                    return "has no valid POL ID (4 letters, then 4 digits)";
                if (string.IsNullOrEmpty(PolPassword)) return "has no POL password";
                if (string.IsNullOrWhiteSpace(Username) || string.IsNullOrEmpty(Password)) return "has no Square Enix ID and password";
            }
            return EffectiveTwoFactorMode switch
            {
                TwoFactorMode.AuthenticatorKey when !TotpEngine.IsValidSecret(OtpSeed) => "has no valid authenticator key",
                TwoFactorMode.EnterAtLaunch when !IsValidOneTimeCode(EnteredOneTimeCode.Trim()) =>
                    "needs the 6-digit authenticator code entered on its card",
                _ => null,
            };
        }

        private ProfileServerType InferLegacyServerType()
        {
            if (!string.IsNullOrWhiteSpace(ServerHost)) return ProfileServerType.LandSandBoat;
            if ((BootloaderPath ?? string.Empty).Contains("xiloader", StringComparison.OrdinalIgnoreCase)) return ProfileServerType.LandSandBoat;
            string args = Arguments ?? string.Empty;
            if (args.Contains("127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
                args.Contains("localhost", StringComparison.OrdinalIgnoreCase) ||
                args.Contains("--server", StringComparison.OrdinalIgnoreCase))
            {
                return ProfileServerType.LandSandBoat;
            }
            return ProfileServerType.Retail;
        }

        /// <summary>
        /// The host an older profile's bootloader command line names: the <c>--server</c> (or <c>-s</c>) value, or
        /// 127.0.0.1 as xiloader assumes. Empty for a profile with neither a bootloader nor arguments.
        /// </summary>
        private string LegacyServerHost()
        {
            if (string.IsNullOrWhiteSpace(Arguments) && string.IsNullOrWhiteSpace(BootloaderPath)) return string.Empty;
            string[] parts = SplitArguments(Arguments);
            for (int i = 0; i < parts.Length - 1; i++)
            {
                if (string.Equals(parts[i], "--server", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(parts[i], "-s", StringComparison.OrdinalIgnoreCase))
                {
                    return parts[i + 1];
                }
            }
            return "127.0.0.1";
        }

        /// <summary>The value of an older profile's port argument (<paramref name="name"/>), or <paramref name="fallback"/>.</summary>
        internal static int ParsePortArgument(string? arguments, string name, int fallback)
        {
            string[] parts = SplitArguments(arguments);
            for (int i = 0; i < parts.Length - 1; i++)
            {
                if (string.Equals(parts[i], name, StringComparison.OrdinalIgnoreCase) &&
                    int.TryParse(parts[i + 1], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int port) &&
                    port is > 0 and <= 65535)
                {
                    return port;
                }
            }
            return fallback;
        }

        private static string[] SplitArguments(string? arguments) =>
            (arguments ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        /// <summary>
        /// Creates a deep copy of this account profile with an optional new name.
        /// </summary>
        public AccountProfile Clone(string? newProfileName = null)
        {
            return new AccountProfile
            {
                ProfileName = newProfileName ?? ProfileName,
                CharacterName = CharacterName,
                CharacterSlot = CharacterSlot,
                LoginMode = LoginMode,
                ServerType = ServerType,
                BootloaderPath = BootloaderPath,
                Arguments = Arguments,
                Username = Username,
                PolId = PolId,
                PolPassword = PolPassword,
                GameDirectory = GameDirectory,
                Folder = Folder,
                ServerHost = ServerHost,
                ServerPort = ServerPort,
                DataPort = DataPort,
                ViewPort = ViewPort,
                Password = Password,
                OtpSeed = OtpSeed,
                TwoFactor = TwoFactor,
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
