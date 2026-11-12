// tests/Gordian.Core.Tests/Profiles/ProfileServerTypeTests.cs
using System;
using System.IO;
using System.Text.Json;
using Gordian.Core.Profiles;
using Xunit;

namespace Gordian.Core.Tests.Profiles
{
    /// <summary>The profile rework (#527): server type, 2FA mode, retail fields, LandSandBoat ports and older profiles.</summary>
    public class ProfileServerTypeTests
    {
        [Theory]
        [InlineData("127.0.0.1", ProfileServerType.LandSandBoat)]
        [InlineData("", ProfileServerType.Retail)]
        public void OlderProfiles_WithoutAType_AreLandSandBoatWhenTheyNameAHost(string host, ProfileServerType expected)
        {
            var profile = new AccountProfile { ServerHost = host };
            Assert.Null(profile.ServerType);
            Assert.Equal(expected, profile.EffectiveServerType);
        }

        [Fact]
        public void TheRemovedBootloaderFields_AreIgnoredOnLoad_AndDroppedOnSave()
        {
            var profile = JsonSerializer.Deserialize<AccountProfile>(
                "{\"ProfileName\":\"Old\",\"ServerHost\":\"127.0.0.1\",\"BootloaderPath\":\"\",\"Arguments\":\"--hairpin\"}")!;
            Assert.Equal(ProfileServerType.LandSandBoat, profile.EffectiveServerType);
            string json = JsonSerializer.Serialize(profile);
            Assert.DoesNotContain("BootloaderPath", json);
            Assert.DoesNotContain("Arguments", json);
        }

        [Fact]
        public void TheChosenType_Wins()
        {
            var profile = new AccountProfile { ServerHost = "127.0.0.1", ServerType = ProfileServerType.Retail };
            Assert.Equal(ProfileServerType.Retail, profile.EffectiveServerType);
        }

        [Fact]
        public void ExplicitPorts_Win_AndDefaultsApplyOtherwise()
        {
            var profile = new AccountProfile { ServerHost = "h", DataPort = 2 };
            Assert.Equal(2, profile.EffectiveDataPort);
            Assert.Equal(AccountProfile.DefaultLsbViewPort, profile.EffectiveViewPort);
            Assert.Equal(AccountProfile.DefaultLsbAuthPort, profile.EffectiveServerPort);
            Assert.Equal(string.Empty, new AccountProfile().EffectiveServerHost);
        }

        [Fact]
        public void OlderProfiles_UseTheirKeyWhenTheyHaveOne()
        {
            Assert.Equal(TwoFactorMode.None, new AccountProfile().EffectiveTwoFactorMode);
            Assert.Equal(TwoFactorMode.AuthenticatorKey, new AccountProfile { OtpSeed = "HXDMVJECJJWSRB3D" }.EffectiveTwoFactorMode);
            Assert.Equal(TwoFactorMode.None, new AccountProfile { OtpSeed = "HXDMVJECJJWSRB3D", TwoFactor = TwoFactorMode.None }.EffectiveTwoFactorMode);
        }

        [Fact]
        public void ResolveOneTimeCode_FollowsTheMode()
        {
            var keyed = new AccountProfile { OtpSeed = "HXDMVJECJJWSRB3D", TwoFactor = TwoFactorMode.AuthenticatorKey };
            Assert.True(AccountProfile.IsValidOneTimeCode(keyed.ResolveOneTimeCode()));

            var typed = new AccountProfile { OtpSeed = "HXDMVJECJJWSRB3D", TwoFactor = TwoFactorMode.EnterAtLaunch, EnteredOneTimeCode = " 654321 " };
            Assert.Equal("654321", typed.ResolveOneTimeCode());

            var none = new AccountProfile { OtpSeed = "HXDMVJECJJWSRB3D", TwoFactor = TwoFactorMode.None };
            Assert.Equal(string.Empty, none.ResolveOneTimeCode());
        }

        [Fact]
        public void GetLaunchProblem_NamesWhatTheProfileIsMissing()
        {
            Assert.Equal("has no server host", new AccountProfile { ServerType = ProfileServerType.LandSandBoat }.GetLaunchProblem());
            Assert.Null(new AccountProfile { ServerType = ProfileServerType.LandSandBoat, ServerHost = "127.0.0.1" }.GetLaunchProblem());
            Assert.Null(Retail().GetLaunchProblem());

            var typed = Retail();
            typed.TwoFactor = TwoFactorMode.EnterAtLaunch;
            Assert.Contains("6-digit", typed.GetLaunchProblem());
            typed.EnteredOneTimeCode = "12345";
            Assert.Contains("6-digit", typed.GetLaunchProblem());
            typed.EnteredOneTimeCode = "123456";
            Assert.Null(typed.GetLaunchProblem());

            var keyed = Retail();
            keyed.TwoFactor = TwoFactorMode.AuthenticatorKey;
            keyed.OtpSeed = "short";
            Assert.Equal("has no valid authenticator key", keyed.GetLaunchProblem());
        }

        [Fact]
        public void GetLaunchProblem_DirectRetailNeedsBothAccounts()
        {
            Assert.Null(Retail().GetLaunchProblem());

            var lowerCase = Retail();
            lowerCase.PolId = " abcd1234 ";
            Assert.Null(lowerCase.GetLaunchProblem());

            var noPolId = Retail();
            noPolId.PolId = "ABC12345";
            Assert.Contains("POL ID", noPolId.GetLaunchProblem());

            var noPolPassword = Retail();
            noPolPassword.PolPassword = string.Empty;
            Assert.Equal("has no POL password", noPolPassword.GetLaunchProblem());

            var noSquareEnix = Retail();
            noSquareEnix.Password = string.Empty;
            Assert.Equal("has no Square Enix ID and password", noSquareEnix.GetLaunchProblem());

        }

        private static AccountProfile Retail() => new()
        {
            ServerType = ProfileServerType.Retail,
            PolId = "ABCD1234",
            PolPassword = "password",
            Username = "square_enix_id",
            Password = "se-password",
        };

        [Theory]
        [InlineData("HXDMVJECJJWSRB3D", true)]
        [InlineData("hxdm vjec jjws rb3d", true)]
        [InlineData("HXDMVJECJJWSRB3D====", true)]
        [InlineData("HXDMVJECJJWSRB3", false)]
        [InlineData("HXDMVJECJJWSRB3D1", false)]
        [InlineData("", false)]
        [InlineData(null, false)]
        public void IsValidSecret_AcceptsBase32KeysAsAuthenticatorsShowThem(string? key, bool expected)
        {
            Assert.Equal(expected, TotpEngine.IsValidSecret(key));
        }

        [Fact]
        public void NewFields_RoundTripThroughTheFile_PasswordsEncrypted_AndTheTypedCodeIsNeverSaved()
        {
            string dir = Path.Combine(Path.GetTempPath(), "GordianXI_ServerType_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                var profile = new AccountProfile
                {
                    ProfileName = "Retail",
                    ServerType = ProfileServerType.Retail,
                    PolId = "ABCD1234",
                    PolPassword = "PolSecret99",
                    GameDirectory = Path.Combine(dir, "FINAL FANTASY XI"),
                    TwoFactor = TwoFactorMode.EnterAtLaunch,
                    EnteredOneTimeCode = "123456",
                    DataPort = 60230,
                    ViewPort = 60001,
                };
                profile.SaveToFile(dir);

                string json = File.ReadAllText(Path.Combine(dir, "Retail.json"));
                Assert.DoesNotContain("PolSecret99", json);
                Assert.DoesNotContain("123456", json);

                var loaded = AccountProfile.LoadFromFile(Path.Combine(dir, "Retail.json"))!;
                Assert.Equal(ProfileServerType.Retail, loaded.ServerType);
                Assert.Equal("ABCD1234", loaded.PolId);
                Assert.Equal("PolSecret99", loaded.PolPassword);
                Assert.Equal(profile.GameDirectory, loaded.GameDirectory);
                Assert.Equal(TwoFactorMode.EnterAtLaunch, loaded.TwoFactor);
                Assert.Equal(string.Empty, loaded.EnteredOneTimeCode);
                Assert.Equal(60230, loaded.DataPort);
                Assert.Equal(60001, loaded.ViewPort);

                var copy = loaded.Clone();
                Assert.Equal("PolSecret99", copy.PolPassword);
                Assert.Equal(TwoFactorMode.EnterAtLaunch, copy.TwoFactor);
                Assert.Equal(profile.GameDirectory, copy.GameDirectory);
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }
    }
}
