// src/Gordian.App/ViewModels/LsbAccountsViewModel.cs
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Gordian.App.Common;
using Gordian.Core.Diagnostics;
using Gordian.Core.Network.LandSandBoat;
using Gordian.Core.Profiles;
using QRCoder;

namespace Gordian.App.ViewModels
{
    /// <summary>Which LandSandBoat account task the LSB Accounts tab shows.</summary>
    public enum LsbAccountTask
    {
        CreateAccount,
        ChangePassword,
        TwoFactor,
    }

    /// <summary>
    /// The LSB Accounts tab (#652): create a LandSandBoat account, change its password, and set up or remove two-factor,
    /// through the same xi_connect commands xiloader sends (<see cref="ILsbAccountClient"/>). Retail accounts are managed
    /// on Square Enix's website, not here. Passwords and keys are only kept where a saved profile already keeps them
    /// (encrypted); the recovery code is shown once and never saved.
    /// </summary>
    public sealed class LsbAccountsViewModel : ViewModelBase
    {
        private readonly ILsbAccountClient _client;
        private readonly Func<IEnumerable<AccountProfile>> _profiles;
        private readonly Action<AccountProfile> _openProfileEditor;
        private readonly Action<AccountProfile> _saveProfile;
        private readonly TotpCodeLedger _ledger;

        private string _serverHost = string.Empty;
        private int _serverPort = AccountProfile.DefaultLsbAuthPort;
        private string _username = string.Empty;
        private string _password = string.Empty;
        private string _newPassword = string.Empty;
        private string _confirmPassword = string.Empty;
        private string _code = string.Empty;
        private bool _showPasswords;
        private bool _updateProfiles = true;
        private bool _setUpTwoFactorOnCreate;
        private AccountProfile? _pendingNewProfile;
        private LsbAccountTask _task = LsbAccountTask.CreateAccount;
        private AccountProfile? _sourceProfile;
        private LsbTotpSetup? _pendingSetup;
        private byte[]? _qrCodePng;
        private string _recoveryCode = string.Empty;
        private string _statusMessage = string.Empty;
        private bool _isError;
        private bool _isBusy;

        public LsbAccountsViewModel(ILsbAccountClient client, Func<IEnumerable<AccountProfile>> profiles,
            Action<AccountProfile> openProfileEditor, Action<AccountProfile> saveProfile, TotpCodeLedger? ledger = null)
        {
            _ledger = ledger ?? TotpCodeLedger.Shared;
            _client = client;
            _profiles = profiles;
            _openProfileEditor = openProfileEditor;
            _saveProfile = saveProfile;
            CreateAccountCommand = new RelayCommand(() => _ = CreateAccountAsync());
            ChangePasswordCommand = new RelayCommand(() => _ = ChangePasswordAsync());
            BeginTotpSetupCommand = new RelayCommand(() => _ = BeginTotpSetupAsync());
            ConfirmTotpSetupCommand = new RelayCommand(() => _ = ConfirmTotpSetupAsync());
            CancelTotpSetupCommand = new RelayCommand(CancelTotpSetup);
            RemoveTotpCommand = new RelayCommand(() => _ = RemoveTotpAsync());
            RegenerateRecoveryCodeCommand = new RelayCommand(() => _ = RegenerateRecoveryCodeAsync());
            FillCodeCommand = new RelayCommand(FillCode);
            CopyKeyCommand = new RelayCommand(() => _ = CopyAsync(_pendingSetup?.Secret, "key"));
            CopyRecoveryCodeCommand = new RelayCommand(() => _ = CopyAsync(_recoveryCode, "recovery code"));
            ContinueToNewProfileCommand = new RelayCommand(ContinueToNewProfile);
            RefreshProfiles();
        }

        /// <summary>Copies text to the clipboard; set by the window.</summary>
        public Func<string, Task>? CopyToClipboard { get; set; }

        // ---- Server and account -------------------------------------------------------------------------------

        /// <summary>The LandSandBoat profiles, to fill the server and account from.</summary>
        public ObservableCollection<AccountProfile> SourceProfiles { get; } = new();

        /// <summary>A profile picked to fill the server and account fields from; it stays null after filling.</summary>
        public AccountProfile? SourceProfile
        {
            get => _sourceProfile;
            set
            {
                if (!SetProperty(ref _sourceProfile, value) || value == null) return;
                ServerHost = value.EffectiveServerHost;
                ServerPort = value.EffectiveServerPort;
                Username = value.Username;
                Password = value.Password;
            }
        }

        public string ServerHost
        {
            get => _serverHost;
            set { if (SetProperty(ref _serverHost, value ?? string.Empty)) OnAccountChanged(); }
        }

        public int ServerPort
        {
            get => _serverPort;
            set { if (SetProperty(ref _serverPort, value)) OnAccountChanged(); }
        }

        public string Username
        {
            get => _username;
            set { if (SetProperty(ref _username, value ?? string.Empty)) OnAccountChanged(); }
        }

        /// <summary>The account's password (the current one, for a password change or two-factor).</summary>
        public string Password
        {
            get => _password;
            set => SetProperty(ref _password, value ?? string.Empty);
        }

        public string NewPassword
        {
            get => _newPassword;
            set => SetProperty(ref _newPassword, value ?? string.Empty);
        }

        public string ConfirmPassword
        {
            get => _confirmPassword;
            set => SetProperty(ref _confirmPassword, value ?? string.Empty);
        }

        /// <summary>A two-factor code, or the recovery code where the server takes one.</summary>
        public string Code
        {
            get => _code;
            set => SetProperty(ref _code, value ?? string.Empty);
        }

        public bool ShowPasswords
        {
            get => _showPasswords;
            set => SetProperty(ref _showPasswords, value);
        }

        /// <summary>Whether a password change or two-factor change also updates the saved profiles of the account.</summary>
        public bool UpdateProfiles
        {
            get => _updateProfiles;
            set => SetProperty(ref _updateProfiles, value);
        }

        /// <summary>Whether Create Account also sets up two-factor before opening the new profile.</summary>
        public bool SetUpTwoFactorOnCreate
        {
            get => _setUpTwoFactorOnCreate;
            set => SetProperty(ref _setUpTwoFactorOnCreate, value);
        }

        /// <summary>
        /// The profile a just-created account opens once its two-factor setup is done (confirmed, cancelled or failed), or
        /// null outside that flow.
        /// </summary>
        public bool HasPendingNewProfile => _pendingNewProfile != null;

        private void SetPendingNewProfile(AccountProfile? profile)
        {
            _pendingNewProfile = profile;
            OnPropertyChanged(nameof(HasPendingNewProfile));
            OnPropertyChanged(nameof(ShowsCreateButton));
        }

        /// <summary>The saved LandSandBoat profiles of this account on this server (same host, auth port and account name).</summary>
        public IReadOnlyList<AccountProfile> MatchingProfiles =>
            string.IsNullOrWhiteSpace(ServerHost) || string.IsNullOrWhiteSpace(Username)
                ? Array.Empty<AccountProfile>()
                : _profiles().Where(IsThisAccount).ToList();

        /// <summary>The matching profiles by path (<c>Horizon/Gordian</c>): names repeat across launch-list folders (#638).</summary>
        public string MatchingProfilesText => MatchingProfiles.Count switch
        {
            0 => "No saved profile uses this account.",
            1 => $"1 saved profile uses this account: {ProfilePath.Format(MatchingProfiles[0])}.",
            int n => $"{n} saved profiles use this account: {string.Join(", ", MatchingProfiles.Select(ProfilePath.Format))}.",
        };

        private bool IsThisAccount(AccountProfile p) =>
            p.EffectiveServerType == ProfileServerType.LandSandBoat
            && string.Equals(p.EffectiveServerHost, ServerHost.Trim(), StringComparison.OrdinalIgnoreCase)
            && p.EffectiveServerPort == ServerPort
            && string.Equals(p.Username.Trim(), Username.Trim(), StringComparison.OrdinalIgnoreCase);

        private void OnAccountChanged()
        {
            OnPropertyChanged(nameof(MatchingProfiles));
            OnPropertyChanged(nameof(MatchingProfilesText));
            OnPropertyChanged(nameof(CanFillCodeFromProfile));
        }

        /// <summary>Reloads <see cref="SourceProfiles"/> from the saved profiles.</summary>
        public void RefreshProfiles()
        {
            SourceProfiles.Clear();
            foreach (var p in _profiles().Where(p => p.EffectiveServerType == ProfileServerType.LandSandBoat)) SourceProfiles.Add(p);
            OnAccountChanged();
        }

        // ---- Task selection -----------------------------------------------------------------------------------

        public LsbAccountTask SelectedTask
        {
            get => _task;
            set
            {
                if (!SetProperty(ref _task, value)) return;
                OnPropertyChanged(nameof(IsCreateAccount));
                OnPropertyChanged(nameof(IsChangePassword));
                OnPropertyChanged(nameof(IsTwoFactor));
                OnPropertyChanged(nameof(NewPasswordLabel));
                OnPropertyChanged(nameof(ShowsCodeField));
                OnPropertyChanged(nameof(ShowsCreateButton));
                SetStatus(string.Empty);
            }
        }

        public bool IsCreateAccount { get => SelectedTask == LsbAccountTask.CreateAccount; set { if (value) SelectedTask = LsbAccountTask.CreateAccount; } }
        public bool IsChangePassword { get => SelectedTask == LsbAccountTask.ChangePassword; set { if (value) SelectedTask = LsbAccountTask.ChangePassword; } }
        /// <summary>The new-password field's label: the account's password when creating, the new one when changing.</summary>
        public string NewPasswordLabel => IsCreateAccount ? "Password:" : "New Password:";

        /// <summary>The code field shows for every task but creation, and during a setup started from creation.</summary>
        public bool ShowsCodeField => !IsCreateAccount || HasPendingSetup;

        /// <summary>Create Account hides while its two-factor setup runs.</summary>
        public bool ShowsCreateButton => IsCreateAccount && !HasPendingSetup && !HasPendingNewProfile;

        public bool IsTwoFactor { get => SelectedTask == LsbAccountTask.TwoFactor; set { if (value) SelectedTask = LsbAccountTask.TwoFactor; } }

        // ---- Status --------------------------------------------------------------------------------------------

        public string StatusMessage
        {
            get => _statusMessage;
            private set => SetProperty(ref _statusMessage, value);
        }

        public bool IsError
        {
            get => _isError;
            private set => SetProperty(ref _isError, value);
        }

        public bool IsBusy
        {
            get => _isBusy;
            private set { if (SetProperty(ref _isBusy, value)) OnPropertyChanged(nameof(IsIdle)); }
        }

        public bool IsIdle => !IsBusy;

        private void SetStatus(string message, bool error = false)
        {
            StatusMessage = message;
            IsError = error;
        }

        // ---- Two-factor state ----------------------------------------------------------------------------------

        /// <summary>The setup the server started and not yet confirmed.</summary>
        public LsbTotpSetup? PendingSetup
        {
            get => _pendingSetup;
            private set
            {
                if (!SetProperty(ref _pendingSetup, value)) return;
                QrCodePng = value == null ? null : RenderQrCode(value.OtpAuthUri);
                OnPropertyChanged(nameof(HasPendingSetup));
                OnPropertyChanged(nameof(PendingKeyText));
                OnPropertyChanged(nameof(ShowsCodeField));
                OnPropertyChanged(nameof(ShowsCreateButton));
            }
        }

        public bool HasPendingSetup => PendingSetup != null;

        /// <summary>The pending key in groups of four, for typing into an app.</summary>
        public string PendingKeyText => PendingSetup == null ? string.Empty
            : string.Join(' ', Enumerable.Range(0, (PendingSetup.Secret.Length + 3) / 4)
                .Select(i => PendingSetup.Secret.Substring(i * 4, Math.Min(4, PendingSetup.Secret.Length - i * 4))));

        /// <summary>The pending setup's <c>otpauth://</c> link as a PNG QR code, for phone authenticator apps.</summary>
        public byte[]? QrCodePng
        {
            get => _qrCodePng;
            private set => SetProperty(ref _qrCodePng, value);
        }

        /// <summary>The recovery code the server just sent; shown once, never saved.</summary>
        public string RecoveryCode
        {
            get => _recoveryCode;
            private set
            {
                if (SetProperty(ref _recoveryCode, value)) OnPropertyChanged(nameof(HasRecoveryCode));
            }
        }

        public bool HasRecoveryCode => !string.IsNullOrEmpty(RecoveryCode);

        /// <summary>Whether a code can be filled in by GordianXI: from the pending setup's key or a matching profile's key.</summary>
        public bool CanFillCodeFromProfile => PendingSetup != null || ProfileKey() != null;

        private string? ProfileKey() =>
            MatchingProfiles.Select(p => p.OtpSeed?.Trim()).FirstOrDefault(TotpEngine.IsValidSecret);

        /// <summary>A QR code PNG for <paramref name="text"/>.</summary>
        public static byte[] RenderQrCode(string text)
        {
            using var generator = new QRCodeGenerator();
            using var data = generator.CreateQrCode(text, QRCodeGenerator.ECCLevel.M);
            return new PngByteQRCode(data).GetGraphic(6);
        }

        // ---- Commands ------------------------------------------------------------------------------------------

        public ICommand CreateAccountCommand { get; }
        public ICommand ChangePasswordCommand { get; }
        public ICommand BeginTotpSetupCommand { get; }
        public ICommand ConfirmTotpSetupCommand { get; }
        public ICommand CancelTotpSetupCommand { get; }
        public ICommand RemoveTotpCommand { get; }
        public ICommand RegenerateRecoveryCodeCommand { get; }
        public ICommand FillCodeCommand { get; }
        public ICommand CopyKeyCommand { get; }
        public ICommand CopyRecoveryCodeCommand { get; }
        public ICommand ContinueToNewProfileCommand { get; }

        private string? CheckServer()
        {
            if (string.IsNullOrWhiteSpace(ServerHost)) return "Enter the server host.";
            if (ServerPort is < 1 or > 65535) return "The auth port must be between 1 and 65535.";
            return null;
        }

        private string? CheckNewPassword()
        {
            if (LsbLoginClient.CheckPassword(NewPassword, isNew: true) is { } problem) return problem;
            if (NewPassword != ConfirmPassword) return "The passwords don't match.";
            return null;
        }

        /// <summary>Runs one server request with the busy flag and the error shown; returns whether it succeeded.</summary>
        private async Task<bool> RunAsync(string working, Func<Task> request)
        {
            if (IsBusy) return false;
            IsBusy = true;
            SetStatus(working);
            try
            {
                await request();
                return true;
            }
            catch (LsbAccountException ex)
            {
                SetStatus(ex.Message, error: true);
            }
            catch (UntrustedServerCertificateException)
            {
                SetStatus("Stopped: the server's certificate was not trusted.", error: true);
            }
            catch (Exception ex)
            {
                GordianLog.Warn(LogCategories.LsbLogin, $"LSB account request failed: {ex.Message}");
                SetStatus($"Could not reach the server at {ServerHost.Trim()}:{ServerPort}: {ex.Message}", error: true);
            }
            finally
            {
                IsBusy = false;
            }
            return false;
        }

        private bool Fail(string? problem)
        {
            if (problem == null) return false;
            SetStatus(problem, error: true);
            return true;
        }

        /// <summary>
        /// Creates the account, then opens the profile editor filled in with it (server, ports, account, password and the
        /// default install), as the player picked (#652). With <see cref="SetUpTwoFactorOnCreate"/> it first runs the
        /// two-factor setup (QR code and key, then a code to confirm); the new profile then gets the key, and opens after
        /// the recovery code was shown (<see cref="ContinueToNewProfile"/>), so it is not lost.
        /// </summary>
        public async Task CreateAccountAsync()
        {
            if (Fail(CheckServer() ?? LsbLoginClient.CheckUsername(Username.Trim()) ?? CheckNewPassword())) return;
            string host = ServerHost.Trim(), user = Username.Trim(), password = NewPassword;
            if (!await RunAsync($"Creating account '{user}'...", () => _client.CreateAccountAsync(host, ServerPort, user, password))) return;

            Password = password;
            NewPassword = ConfirmPassword = string.Empty;
            var profile = new AccountProfile
            {
                ProfileName = user,
                ServerType = ProfileServerType.LandSandBoat,
                ServerHost = host,
                ServerPort = ServerPort,
                Username = user,
                Password = password,
            };
            if (!SetUpTwoFactorOnCreate)
            {
                SetStatus($"Account '{user}' created on {host}. Opening a new profile for it.");
                _openProfileEditor(profile);
                return;
            }

            SetPendingNewProfile(profile);
            await BeginTotpSetupAsync();
            if (PendingSetup != null)
            {
                SetStatus($"Account '{user}' created. Scan the QR code (or type the key) into an authenticator app if you want one on " +
                    "your phone, enter a code (or use Fill code) and confirm. Cancel skips two-factor.");
            }
            else
            {
                // The setup didn't start: the account exists, so open its profile and say why two-factor is off.
                string why = StatusMessage;
                ContinueToNewProfile();
                SetStatus($"Account '{user}' created, but two-factor was not set up ({why}). Set it up later in Two-factor.", error: true);
            }
        }

        /// <summary>Opens the profile editor for the account just created (after its two-factor setup) and ends that flow.</summary>
        public void ContinueToNewProfile()
        {
            var profile = _pendingNewProfile;
            if (profile == null) return;
            SetPendingNewProfile(null);
            RecoveryCode = string.Empty;
            SetStatus($"Account '{profile.Username}' created on {profile.EffectiveServerHost}. Opening a new profile for it.");
            _openProfileEditor(profile);
        }

        /// <summary>Changes the password and, if chosen, saves it to the account's profiles.</summary>
        public async Task ChangePasswordAsync()
        {
            if (Fail(CheckServer() ?? LsbLoginClient.CheckUsername(Username.Trim()) ?? LsbLoginClient.CheckPassword(Password) ?? CheckNewPassword())) return;
            string host = ServerHost.Trim(), user = Username.Trim(), current = Password, next = NewPassword, code = CodeOrProfileCode();
            if (!await RunAsync("Changing the password...", () => _client.ChangePasswordAsync(host, ServerPort, user, current, next, code))) return;

            Password = next;
            NewPassword = ConfirmPassword = Code = string.Empty;
            int updated = UpdateMatchingProfiles(p => p.Password = next);
            SetStatus($"Password changed for '{user}'." + UpdatedText(updated) + " The server forgot this account's trusted computers.");
        }

        /// <summary>Starts two-factor setup: shows the key and QR code, and fills in the first code if GordianXI can.</summary>
        public async Task BeginTotpSetupAsync()
        {
            if (Fail(CheckServer() ?? LsbLoginClient.CheckUsername(Username.Trim()) ?? LsbLoginClient.CheckPassword(Password))) return;
            string host = ServerHost.Trim(), user = Username.Trim(), password = Password;
            LsbTotpSetup? setup = null;
            if (!await RunAsync("Starting two-factor setup...", async () => setup = await _client.BeginTotpSetupAsync(host, ServerPort, user, password))) return;

            RecoveryCode = string.Empty;
            PendingSetup = setup;
            Code = string.Empty;
            OnPropertyChanged(nameof(CanFillCodeFromProfile));
            SetStatus("Scan the QR code (or type the key) into an authenticator app, then enter its current code and confirm. " +
                "To let GordianXI make the codes, keep \"Update saved profiles\" on and use Fill code.");
        }

        public void CancelTotpSetup()
        {
            PendingSetup = null;
            Code = string.Empty;
            OnPropertyChanged(nameof(CanFillCodeFromProfile));
            if (HasPendingNewProfile)
            {
                // The account exists already: go on to its profile without two-factor.
                ContinueToNewProfile();
                SetStatus("Two-factor setup cancelled; the new account logs in with its password only. Opening its profile.");
                return;
            }
            SetStatus("Two-factor setup cancelled; the account's login is unchanged.");
        }

        /// <summary>Confirms the pending setup with a code, shows the recovery code and saves the key to the profiles if chosen.</summary>
        public async Task ConfirmTotpSetupAsync()
        {
            var setup = PendingSetup;
            if (setup == null) { SetStatus("Start two-factor setup first.", error: true); return; }
            if (Fail(CheckServer() ?? LsbLoginClient.CheckPassword(Password))) return;
            string host = ServerHost.Trim(), user = Username.Trim(), password = Password, code = Code.Trim();
            if (code.Length == 0) { SetStatus("Enter the current code from the authenticator, or use Fill code.", error: true); return; }
            string recovery = string.Empty;
            // A code from the new key: the server takes its step for the account, so a login right after must use another.
            NoteSent(code, setup.Secret);
            if (!await RunAsync("Confirming two-factor...", async () => recovery = await _client.ConfirmTotpSetupAsync(host, ServerPort, user, password, code))) return;

            PendingSetup = null;
            Code = string.Empty;
            RecoveryCode = recovery;
            OnPropertyChanged(nameof(CanFillCodeFromProfile));
            if (_pendingNewProfile is { } newProfile)
            {
                newProfile.OtpSeed = setup.Secret;
                newProfile.TwoFactor = TwoFactorMode.AuthenticatorKey;
                SetStatus("Two-factor is on, and the new profile will make the codes itself. Write the recovery code down " +
                    "(it is not shown again), then continue to the new profile.");
                return;
            }
            int updated = UpdateMatchingProfiles(p =>
            {
                p.OtpSeed = setup.Secret;
                p.TwoFactor = TwoFactorMode.AuthenticatorKey;
            });
            SetStatus("Two-factor is on." + UpdatedText(updated, "now make the codes themselves") +
                " Write the recovery code down: it is the only way to remove two-factor without the authenticator, and it is not shown again.");
        }

        /// <summary>Turns two-factor off and, if chosen, removes the key from the account's profiles.</summary>
        public async Task RemoveTotpAsync()
        {
            if (Fail(CheckServer() ?? LsbLoginClient.CheckUsername(Username.Trim()) ?? LsbLoginClient.CheckPassword(Password))) return;
            string host = ServerHost.Trim(), user = Username.Trim(), password = Password, code = CodeOrProfileCode();
            if (code.Length == 0) { SetStatus("Enter a current code or the recovery code.", error: true); return; }
            if (!await RunAsync("Removing two-factor...", () => _client.RemoveTotpAsync(host, ServerPort, user, password, code))) return;

            Code = string.Empty;
            RecoveryCode = string.Empty;
            int updated = UpdateMatchingProfiles(p =>
            {
                p.OtpSeed = string.Empty;
                p.TwoFactor = TwoFactorMode.None;
            });
            OnPropertyChanged(nameof(CanFillCodeFromProfile));
            SetStatus($"Two-factor removed from '{user}'." + UpdatedText(updated) + " The server forgot this account's trusted computers.");
        }

        /// <summary>Replaces the recovery code and shows the new one.</summary>
        public async Task RegenerateRecoveryCodeAsync()
        {
            if (Fail(CheckServer() ?? LsbLoginClient.CheckUsername(Username.Trim()) ?? LsbLoginClient.CheckPassword(Password))) return;
            string host = ServerHost.Trim(), user = Username.Trim(), password = Password, code = CodeOrProfileCode();
            if (code.Length == 0) { SetStatus("Enter a current code or the recovery code.", error: true); return; }
            string recovery = string.Empty;
            if (!await RunAsync("Making a new recovery code...", async () => recovery = await _client.RegenerateRecoveryCodeAsync(host, ServerPort, user, password, code))) return;

            Code = string.Empty;
            RecoveryCode = recovery;
            SetStatus("New recovery code made; the old one no longer works. Write it down: it is not shown again.");
        }

        private string LedgerAccount => TotpCodeLedger.AccountKey(ServerHost, ServerPort, Username);

        /// <summary>
        /// Fills <see cref="Code"/> from the pending setup's key, else a matching profile's key, with a code the server has
        /// not taken yet for this account (<see cref="TotpCodeLedger"/>: LandSandBoat refuses a code twice).
        /// </summary>
        public void FillCode()
        {
            string? key = PendingSetup?.Secret ?? ProfileKey();
            if (key == null)
            {
                SetStatus("No authenticator key to make a code from: start setup, or save the key in a profile of this account.", error: true);
                return;
            }
            string? code = _ledger.TryTake(LedgerAccount, key, DateTimeOffset.UtcNow, out var wait);
            if (code == null)
            {
                SetStatus($"The last codes were just used; try again in {Math.Ceiling(wait.TotalSeconds):0} s.", error: true);
                return;
            }
            Code = code;
        }

        /// <summary>
        /// The typed code (noted as sent, when it is one of a known key's), or when none was typed an unused code of a
        /// matching profile's key; empty if neither.
        /// </summary>
        private string CodeOrProfileCode()
        {
            string? key = ProfileKey();
            if (!string.IsNullOrWhiteSpace(Code))
            {
                NoteSent(Code, PendingSetup?.Secret ?? key);
                return Code.Trim();
            }
            if (key == null) return string.Empty;
            return _ledger.TryTake(LedgerAccount, key, DateTimeOffset.UtcNow, out _) ?? string.Empty;
        }

        /// <summary>Records a code sent for this account so a login right after doesn't resend it.</summary>
        private void NoteSent(string code, string? secret)
        {
            if (secret != null) _ledger.NoteSent(LedgerAccount, secret, code, DateTimeOffset.UtcNow);
        }

        /// <summary>Applies <paramref name="change"/> to each matching profile and saves it, when <see cref="UpdateProfiles"/> is on.</summary>
        private int UpdateMatchingProfiles(Action<AccountProfile> change)
        {
            if (!UpdateProfiles) return 0;
            int count = 0;
            foreach (var profile in MatchingProfiles)
            {
                change(profile);
                _saveProfile(profile);
                count++;
            }
            return count;
        }

        private static string UpdatedText(int count, string? what = null) => count switch
        {
            0 => string.Empty,
            1 => what == null ? " Updated 1 saved profile." : $" 1 saved profile will {what}.",
            _ => what == null ? $" Updated {count} saved profiles." : $" {count} saved profiles will {what}.",
        };

        private async Task CopyAsync(string? text, string what)
        {
            if (string.IsNullOrEmpty(text) || CopyToClipboard == null) return;
            try
            {
                await CopyToClipboard(text);
                SetStatus($"Copied the {what}.");
            }
            catch (Exception ex)
            {
                SetStatus($"Could not copy the {what}: {ex.Message}", error: true);
            }
        }
    }
}
