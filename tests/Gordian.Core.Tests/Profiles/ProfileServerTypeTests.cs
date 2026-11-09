// tests/Gordian.Core.Tests/Profiles/ProfileServerTypeTests.cs
using System;
using System.IO;
using Gordian.Core.Profiles;
using Xunit;

namespace Gordian.Core.Tests.Profiles
{
    /// <summary>The profile rework (#527): server type, 2FA mode, retail fields, LandSandBoat ports and older profiles.</summary>
    public class ProfileServerTypeTests
    {
        [Theory]
        [InlineData("127.0.0.1", "", "", ProfileServerType.LandSandBoat)]
        [InlineData("", "C:/xi/xiloader.exe", "", ProfileServerType.LandSandBoat)]
        [InlineData("", "", "--server play.example.net", ProfileServerType.LandSandBoat)]
        [InlineData("", "C:/pol/pol.exe", "--windowed", ProfileServerType.Retail)]
        [InlineData("", "", "", ProfileServerType.Retail)]
        public void OlderProfiles_WithoutAType_GetTheOneTheirFieldsImply(string host, string bootloader, string args, ProfileServerType expected)
        {
            var profile = new AccountProfile { ServerHost = host, BootloaderPath = bootloader, Arguments = args };
            Assert.Null(profile.ServerType);
            Assert.Equal(expected, profile.EffectiveServerType);
        }

        [Fact]
        public void TheChosenType_Wins()
        {
            var profile = new AccountProfile { ServerHost = "127.0.0.1", ServerType = ProfileServerType.Retail };
            Assert.Equal(ProfileServerType.Retail, profile.EffectiveServerType);
        }

        [Fact]
        public void OlderProfiles_KeepTheHostAndPortsTheirArgumentsName()
        {
            var profile = new AccountProfile
            {
                BootloaderPath = "xiloader.exe",
                Arguments = "--server 10.1.2.3 --dataport 60230 --viewport 60001 --authport 60231",
                ServerPort = 0,
            };
            Assert.Equal("10.1.2.3", profile.EffectiveServerHost);
            Assert.Equal(60231, profile.EffectiveServerPort);
            Assert.Equal(60230, profile.EffectiveDataPort);
            Assert.Equal(60001, profile.EffectiveViewPort);

            // xiloader with no --server means the local machine, as before.
            Assert.Equal("127.0.0.1", new AccountProfile { BootloaderPath = "xiloader.exe" }.EffectiveServerHost);
        }

        [Fact]
        public void ExplicitPorts_WinOverArguments_AndDefaultsApplyOtherwise()
        {
            var profile = new AccountProfile { ServerHost = "h", Arguments = "--dataport 1", DataPort = 2 };
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
            Assert.Null(new AccountProfile { ServerType = ProfileServerType.Retail }.GetLaunchProblem());

            var typed = new AccountProfile { ServerType = ProfileServerType.Retail, TwoFactor = TwoFactorMode.EnterAtLaunch };
            Assert.Contains("6-digit", typed.GetLaunchProblem());
            typed.EnteredOneTimeCode = "12345";
            Assert.Contains("6-digit", typed.GetLaunchProblem());
            typed.EnteredOneTimeCode = "123456";
            Assert.Null(typed.GetLaunchProblem());

            var keyed = new AccountProfile { ServerType = ProfileServerType.Retail, TwoFactor = TwoFactorMode.AuthenticatorKey, OtpSeed = "short" };
            Assert.Equal("has no valid authenticator key", keyed.GetLaunchProblem());
        }

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
