// tests/Gordian.Core.Tests/Profiles/AccountProfileTests.cs
using System;
using System.IO;
using Gordian.Core.Profiles;
using Xunit;

namespace Gordian.Core.Tests.Profiles
{
    public sealed class AccountProfileTests : IDisposable
    {
        private readonly string _tempDirectory;

        public AccountProfileTests()
        {
            _tempDirectory = Path.Combine(Path.GetTempPath(), "GordianXI_UnitTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDirectory);
        }

        public void Dispose()
        {
            if (Directory.Exists(_tempDirectory))
            {
                try
                {
                    Directory.Delete(_tempDirectory, true);
                }
                catch
                {
                    // Clean up best effort
                }
            }
        }

        [Fact]
        public void SaveToFile_DoesNotExposePlaintextCredentialsOnDisk()
        {
            // Arrange
            const string plaintextPassword = "SuperSecretPassword#2026!";
            const string plaintextOtpSeed = "HXDMVJECJJWSRB3D";

            var profile = new AccountProfile
            {
                ProfileName = "TestWarrior",
                Username = "warrior_main",
                Password = plaintextPassword,
                OtpSeed = plaintextOtpSeed,
                BootloaderPath = @"C:\GordianXI\xiloader.exe",
                Arguments = "--server 127.0.0.1 --hairpin"
            };

            // Act
            profile.SaveToFile(_tempDirectory);
            string savedFilePath = Path.Combine(_tempDirectory, "TestWarrior.json");

            // Assert
            Assert.True(File.Exists(savedFilePath), "Profile JSON file was not created on disk.");
            string rawJson = File.ReadAllText(savedFilePath);

            // Plaintext credentials MUST NOT be written to disk
            Assert.DoesNotContain(plaintextPassword, rawJson);
            Assert.DoesNotContain(plaintextOtpSeed, rawJson);

            // Legacy duplicate properties MUST NOT be written to disk
            Assert.DoesNotContain("ProtectedPassword", rawJson);
            Assert.DoesNotContain("ProtectedOtpSeed", rawJson);

            // Encrypted properties MUST be present in JSON
            Assert.Contains("\"Password\":", rawJson);
            Assert.Contains("\"OtpSeed\":", rawJson);
        }

        [Fact]
        public void LoadFromFile_DecryptsCredentialsInMemory()
        {
            // Arrange
            const string plaintextPassword = "AnotherSecretPassword@999";
            const string plaintextOtpSeed = "JBSWY3DPEHPK3PXP";

            var original = new AccountProfile
            {
                ProfileName = "TestMage",
                Username = "black_mage",
                Password = plaintextPassword,
                OtpSeed = plaintextOtpSeed
            };

            original.SaveToFile(_tempDirectory);
            string savedFilePath = Path.Combine(_tempDirectory, "TestMage.json");

            // Act
            var loaded = AccountProfile.LoadFromFile(savedFilePath);

            // Assert
            Assert.NotNull(loaded);
            Assert.Equal("TestMage", loaded.ProfileName);
            Assert.Equal("black_mage", loaded.Username);
            Assert.Equal(plaintextPassword, loaded.Password);
            Assert.Equal(plaintextOtpSeed, loaded.OtpSeed);
        }

        [Fact]
        public void CurrentTwoFactorCode_CalculatesValidSixDigitTotp()
        {
            // Arrange
            var profile = new AccountProfile
            {
                ProfileName = "OtpTest",
                OtpSeed = "HXDMVJECJJWSRB3D"
            };

            // Act
            string totpCode = profile.CurrentTwoFactorCode;

            // Assert
            Assert.NotNull(totpCode);
            Assert.Equal(6, totpCode.Length);
            Assert.True(int.TryParse(totpCode, out _), "TOTP code should be a 6-digit numeric string.");
        }

        [Fact]
        public void ProfileCrypto_RoundTripsSuccessfully()
        {
            // Arrange
            const string originalText = "TestCryptographicPayload-1234567890!@#$%^&*()";

            // Act
            string encrypted = ProfileCrypto.Encrypt(originalText);
            string decrypted = ProfileCrypto.Decrypt(encrypted);

            // Assert
            Assert.NotEqual(originalText, encrypted);
            Assert.Equal(originalText, decrypted);
        }

        [Fact]
        public void ProfileCrypto_EmptyString_ReturnsEmpty()
        {
            Assert.Equal(string.Empty, ProfileCrypto.Encrypt(string.Empty));
            Assert.Equal(string.Empty, ProfileCrypto.Decrypt(string.Empty));
        }
    }
}
