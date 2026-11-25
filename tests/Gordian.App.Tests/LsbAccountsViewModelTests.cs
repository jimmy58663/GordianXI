// tests/Gordian.App.Tests/LsbAccountsViewModelTests.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Gordian.App.ViewModels;
using Gordian.Core.Network.LandSandBoat;
using Gordian.Core.Profiles;
using Xunit;

namespace Gordian.App.Tests
{
    /// <summary>The LSB Accounts tab (#652) against a recording stand-in for the xi_connect account commands.</summary>
    public class LsbAccountsViewModelTests
    {
        private const string Secret = "JBSWY3DPEHPK3PXPJBSWY3DPEHPK3PXP";

        private sealed class FakeClient : ILsbAccountClient
        {
            public List<string> Calls { get; } = new();
            public Exception? Fail { get; set; }

            private Task Record(string call)
            {
                Calls.Add(call);
                return Fail != null ? Task.FromException(Fail) : Task.CompletedTask;
            }

            public Task CreateAccountAsync(string host, int port, string username, string password, CancellationToken ct = default) =>
                Record($"create {host}:{port} {username} {password}");

            public Task ChangePasswordAsync(string host, int port, string username, string currentPassword, string newPassword, string otp = "", CancellationToken ct = default) =>
                Record($"password {username} {currentPassword}->{newPassword} otp={otp}");

            public async Task<LsbTotpSetup> BeginTotpSetupAsync(string host, int port, string username, string password, CancellationToken ct = default)
            {
                await Record($"totp-begin {username}");
                return new LsbTotpSetup(Secret, "Test", username, $"otpauth://totp/Test:{username}?secret={Secret}&issuer=Test&algorithm=SHA1&digits=6&period=30");
            }

            public async Task<string> ConfirmTotpSetupAsync(string host, int port, string username, string password, string code, CancellationToken ct = default)
            {
                await Record($"totp-verify {username} {code}");
                return "RECOVERY1";
            }

            public Task RemoveTotpAsync(string host, int port, string username, string password, string codeOrRecoveryCode, CancellationToken ct = default) =>
                Record($"totp-remove {username} {codeOrRecoveryCode}");

            public async Task<string> RegenerateRecoveryCodeAsync(string host, int port, string username, string password, string codeOrRecoveryCode, CancellationToken ct = default)
            {
                await Record($"recovery {username} {codeOrRecoveryCode}");
                return "RECOVERY2";
            }
        }

        private static AccountProfile Lsb(string name, string user, string host = "play.example", int port = 54231, string password = "oldpass1") => new()
        {
            ProfileName = name,
            ServerType = ProfileServerType.LandSandBoat,
            ServerHost = host,
            ServerPort = port,
            Username = user,
            Password = password,
        };

        private sealed class Harness
        {
            public FakeClient Client { get; } = new();
            public List<AccountProfile> Profiles { get; } = new();
            public List<AccountProfile> Opened { get; } = new();
            public List<AccountProfile> Saved { get; } = new();
            public LsbAccountsViewModel Vm { get; }

            public Harness(params AccountProfile[] profiles)
            {
                Profiles.AddRange(profiles);
                Vm = new LsbAccountsViewModel(Client, () => Profiles, Opened.Add, Saved.Add);
            }
        }

        [Fact]
        public async Task CreateAccount_ChecksTheFormThenOpensAPrefilledProfile()
        {
            var h = new Harness();
            h.Vm.ServerHost = " play.example ";
            h.Vm.Username = "newbie";
            h.Vm.NewPassword = "secret12";
            h.Vm.ConfirmPassword = "secret13";
            await h.Vm.CreateAccountAsync();
            Assert.True(h.Vm.IsError);
            Assert.Empty(h.Client.Calls);

            h.Vm.ConfirmPassword = "secret12";
            await h.Vm.CreateAccountAsync();
            Assert.False(h.Vm.IsError);
            Assert.Equal("create play.example:54231 newbie secret12", Assert.Single(h.Client.Calls));
            var template = Assert.Single(h.Opened);
            Assert.Equal(("newbie", "play.example", 54231, "newbie", "secret12", ProfileServerType.LandSandBoat),
                (template.ProfileName, template.ServerHost, template.ServerPort, template.Username, template.Password, template.EffectiveServerType));
            Assert.Equal(string.Empty, h.Vm.NewPassword);
        }

        [Fact]
        public async Task CreateAccount_ShowsTheServersRefusal()
        {
            var h = new Harness();
            h.Client.Fail = new LsbAccountException(LsbAccountError.NameTaken, "The account name 'newbie' is already taken on this server.");
            h.Vm.ServerHost = "play.example";
            h.Vm.Username = "newbie";
            h.Vm.NewPassword = h.Vm.ConfirmPassword = "secret12";
            await h.Vm.CreateAccountAsync();
            Assert.True(h.Vm.IsError);
            Assert.Contains("already taken", h.Vm.StatusMessage);
            Assert.Empty(h.Opened);
            Assert.False(h.Vm.IsBusy);
        }

        [Fact]
        public async Task ChangePassword_UpdatesOnlyThisAccountsProfilesAndUsesTheirKey()
        {
            var main = Lsb("Main", "knot");
            main.OtpSeed = Secret;
            main.TwoFactor = TwoFactorMode.AuthenticatorKey;
            var mule = Lsb("Mule", "KNOT");
            var otherServer = Lsb("Other", "knot", host: "other.example");
            var otherAccount = Lsb("Alt", "alt");
            var h = new Harness(main, mule, otherServer, otherAccount);
            h.Vm.IsChangePassword = true;
            h.Vm.SourceProfile = main;
            Assert.Equal(("play.example", 54231, "knot", "oldpass1"), (h.Vm.ServerHost, h.Vm.ServerPort, h.Vm.Username, h.Vm.Password));
            Assert.Equal(2, h.Vm.MatchingProfiles.Count);

            h.Vm.NewPassword = h.Vm.ConfirmPassword = "newpass1";
            await h.Vm.ChangePasswordAsync();

            Assert.False(h.Vm.IsError, h.Vm.StatusMessage);
            // No code typed: the profile's authenticator key made one.
            Assert.Equal($"password knot oldpass1->newpass1 otp={TotpEngine.GenerateCurrentCode(Secret)}", Assert.Single(h.Client.Calls));
            Assert.Equal(new[] { main, mule }, h.Saved);
            Assert.Equal(("newpass1", "newpass1", "oldpass1"), (main.Password, mule.Password, otherServer.Password));
        }

        [Fact]
        public async Task ChangePassword_LeavesProfilesAloneWhenAskedTo()
        {
            var main = Lsb("Main", "knot");
            var h = new Harness(main);
            h.Vm.SourceProfile = main;
            h.Vm.UpdateProfiles = false;
            h.Vm.NewPassword = h.Vm.ConfirmPassword = "newpass1";
            await h.Vm.ChangePasswordAsync();
            Assert.Empty(h.Saved);
            Assert.Equal("oldpass1", main.Password);
        }

        [Fact]
        public async Task TwoFactor_SetupShowsTheQrCodeThenSavesTheKeyAndShowsTheRecoveryCode()
        {
            var main = Lsb("Main", "knot");
            var h = new Harness(main);
            h.Vm.IsTwoFactor = true;
            h.Vm.SourceProfile = main;

            await h.Vm.BeginTotpSetupAsync();
            Assert.True(h.Vm.HasPendingSetup);
            Assert.Equal("JBSW Y3DP EHPK 3PXP JBSW Y3DP EHPK 3PXP", h.Vm.PendingKeyText);
            Assert.NotNull(h.Vm.QrCodePng);
            Assert.Equal(new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G' }, h.Vm.QrCodePng!.Take(4).ToArray());
            Assert.True(h.Vm.CanFillCodeFromProfile);

            h.Vm.FillCode();
            Assert.Equal(TotpEngine.GenerateCurrentCode(Secret), h.Vm.Code);
            await h.Vm.ConfirmTotpSetupAsync();

            Assert.False(h.Vm.IsError, h.Vm.StatusMessage);
            Assert.False(h.Vm.HasPendingSetup);
            Assert.Null(h.Vm.QrCodePng);
            Assert.Equal("RECOVERY1", h.Vm.RecoveryCode);
            Assert.Equal((Secret, TwoFactorMode.AuthenticatorKey), (main.OtpSeed, main.TwoFactor));
            Assert.Equal(main, Assert.Single(h.Saved));
        }

        [Fact]
        public async Task TwoFactor_CancelLeavesNothingPending()
        {
            var h = new Harness(Lsb("Main", "knot"));
            h.Vm.SourceProfile = h.Profiles[0];
            await h.Vm.BeginTotpSetupAsync();
            h.Vm.CancelTotpSetup();
            Assert.False(h.Vm.HasPendingSetup);
            Assert.Empty(h.Saved);
        }

        [Fact]
        public async Task TwoFactor_RemoveClearsTheProfilesKey()
        {
            var main = Lsb("Main", "knot");
            main.OtpSeed = Secret;
            main.TwoFactor = TwoFactorMode.AuthenticatorKey;
            var h = new Harness(main);
            h.Vm.SourceProfile = main;
            h.Vm.Code = "recovery1";
            await h.Vm.RemoveTotpAsync();
            Assert.Equal("totp-remove knot recovery1", Assert.Single(h.Client.Calls));
            Assert.Equal((string.Empty, TwoFactorMode.None), (main.OtpSeed, main.TwoFactor));
        }

        [Fact]
        public async Task TwoFactor_NewRecoveryCodeIsShownNotSaved()
        {
            var h = new Harness(Lsb("Main", "knot"));
            h.Vm.SourceProfile = h.Profiles[0];
            h.Vm.Code = "123456";
            await h.Vm.RegenerateRecoveryCodeAsync();
            Assert.Equal("RECOVERY2", h.Vm.RecoveryCode);
            Assert.Empty(h.Saved);
        }

        [Fact]
        public async Task Requests_NeedAServer()
        {
            var h = new Harness();
            h.Vm.Username = "knot";
            h.Vm.NewPassword = h.Vm.ConfirmPassword = "secret12";
            await h.Vm.CreateAccountAsync();
            Assert.True(h.Vm.IsError);
            Assert.Empty(h.Client.Calls);
        }
    }
}
