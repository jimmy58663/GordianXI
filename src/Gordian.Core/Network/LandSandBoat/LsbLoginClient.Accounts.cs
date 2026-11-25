// src/Gordian.Core/Network/LandSandBoat/LsbLoginClient.Accounts.cs
using System;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Gordian.Core.Diagnostics;

namespace Gordian.Core.Network.LandSandBoat
{
    /// <summary>
    /// The xi_connect JSON commands (<c>command</c> field). Values referenced from LandSandBoat
    /// (https://github.com/LandSandBoat/server, src/login/auth_session.h <c>login_cmd</c>) and xiloader
    /// (https://github.com/LandSandBoat/xiloader, src/enums/login_command.h).
    /// </summary>
    public static class LsbAuthCommand
    {
        public const byte Login = 0x10;
        public const byte CreateAccount = 0x20;
        public const byte ChangePassword = 0x30;
        public const byte CreateTotp = 0x31;
        public const byte RemoveTotp = 0x32;
        public const byte RegenerateRecoveryCode = 0x33;
        public const byte VerifyTotp = 0x34;

        /// <summary>A short name for logs.</summary>
        public static string Describe(byte command) => command switch
        {
            Login => "login",
            CreateAccount => "create account",
            ChangePassword => "change password",
            CreateTotp => "set up two-factor",
            RemoveTotp => "remove two-factor",
            RegenerateRecoveryCode => "new recovery code",
            VerifyTotp => "verify two-factor",
            _ => $"command 0x{command:X2}",
        };
    }

    /// <summary>
    /// The xi_connect reply's <c>result</c> codes. Values referenced from LandSandBoat
    /// (https://github.com/LandSandBoat/server, src/login/auth_session.h <c>login_result</c>).
    /// </summary>
    public static class LsbAuthResult
    {
        public const int Fail = 0x00;
        public const int LoginSuccess = 0x01;
        public const int Error = 0x02;
        public const int CreateSuccess = 0x03;
        public const int CreateNameTaken = 0x04;
        public const int ChangePasswordSuccess = 0x06;
        public const int ChangePasswordError = 0x07;
        public const int CreateDisabled = 0x08;
        public const int CreateError = 0x09;
        public const int AlreadyLoggedIn = 0x0A;
        public const int VersionUnsupported = 0x0B;
        public const int CreateTotpSuccess = 0x10;
        public const int VerifyTotpSuccess = 0x11;
        public const int RemoveTotpSuccess = 0x12;
        public const int TrustTokenInvalid = 0x13;
    }

    /// <summary>One xi_connect request: the command and xiloader's JSON fields.</summary>
    internal readonly record struct LsbAuthRequest(byte Command, string Username, string Password, string NewPassword = "", string Otp = "");

    /// <summary>
    /// xi_connect's JSON reply: <c>error_message</c> (a refusal, including the version check), or <c>result</c> with
    /// the command's fields. <see cref="Version"/> is the xiloader version it was sent with, <see cref="ServerKey"/> the
    /// server's key fingerprint (<see cref="LsbLobbyTls.KeyFingerprint"/>).
    /// </summary>
    internal sealed record LsbAuthReply(JsonElement Root, string? ErrorMessage, int Result, string ServerKey, (byte Major, byte Minor, byte Patch) Version)
    {
        public static LsbAuthReply From(JsonElement root, string serverKey, (byte Major, byte Minor, byte Patch) version)
        {
            string? error = root.ValueKind == JsonValueKind.Object && root.TryGetProperty("error_message", out var e) && e.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(e.GetString()) ? e.GetString() : null;
            int result = root.ValueKind == JsonValueKind.Object && root.TryGetProperty("result", out var r) && r.TryGetInt32(out int value) ? value : LsbAuthResult.Fail;
            return new LsbAuthReply(root, error, result, serverKey, version);
        }

        public string? GetString(string name) =>
            Root.ValueKind == JsonValueKind.Object && Root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    }

    /// <summary>Why an LSB account request failed (<see cref="LsbAccountException"/>).</summary>
    public enum LsbAccountError
    {
        /// <summary>The input breaks LandSandBoat's limits; nothing was sent (the server would not have answered).</summary>
        InvalidInput,

        /// <summary>The account name is in use.</summary>
        NameTaken,

        /// <summary>The server has account creation turned off (<c>login.ACCOUNT_CREATION</c>).</summary>
        CreationDisabled,

        /// <summary>The server failed to create the account (a database error on its side).</summary>
        CreationFailed,

        /// <summary>The server refused the account name, password or two-factor code.</summary>
        CredentialsRejected,

        /// <summary>Any other refusal or an answer GordianXI does not understand; the message says which.</summary>
        ServerError,
    }

    /// <summary>A refused LSB account request, with a message to show the user.</summary>
    public sealed class LsbAccountException : InvalidOperationException
    {
        public LsbAccountError Error { get; }

        public LsbAccountException(LsbAccountError error, string message) : base(message)
        {
            Error = error;
        }
    }

    /// <summary>
    /// A two-factor setup the server started (<see cref="LsbLoginClient.BeginTotpSetupAsync"/>): the Base32 secret, and an
    /// <c>otpauth://</c> link for authenticator apps. The setup is not active until a code from it is verified
    /// (<see cref="LsbLoginClient.ConfirmTotpSetupAsync"/>).
    /// </summary>
    public sealed record LsbTotpSetup(string Secret, string Issuer, string AccountName, string OtpAuthUri);

    /// <summary>
    /// The LSB account requests a client can make on xi_connect (<see cref="LsbLoginClient"/> implements them), so the
    /// account screen can be driven without a server in tests.
    /// </summary>
    public interface ILsbAccountClient
    {
        Task CreateAccountAsync(string host, int port, string username, string password, CancellationToken ct = default);
        Task ChangePasswordAsync(string host, int port, string username, string currentPassword, string newPassword, string otp = "", CancellationToken ct = default);
        Task<LsbTotpSetup> BeginTotpSetupAsync(string host, int port, string username, string password, CancellationToken ct = default);
        Task<string> ConfirmTotpSetupAsync(string host, int port, string username, string password, string code, CancellationToken ct = default);
        Task RemoveTotpAsync(string host, int port, string username, string password, string codeOrRecoveryCode, CancellationToken ct = default);
        Task<string> RegenerateRecoveryCodeAsync(string host, int port, string username, string password, string codeOrRecoveryCode, CancellationToken ct = default);
    }

    public sealed partial class LsbLoginClient : ILsbAccountClient
    {
        /// <summary>LandSandBoat's limits: account names 1-16 bytes, passwords 1-32, printable ASCII (<c>isStringMalformed</c>).</summary>
        public const int MaxUsernameLength = 16, MaxPasswordLength = 32;

        /// <summary>The shortest new password, as xiloader asks for ("6-32 characters"); the server itself accepts 1.</summary>
        public const int MinNewPasswordLength = 6;

        /// <summary>
        /// Why <paramref name="username"/> can't be sent, or null. LandSandBoat drops a request whose name or password is
        /// empty, too long or has a byte below 0x20 without any reply; with its signed <c>char</c>, any non-ASCII byte
        /// counts as one (src/login/login_helpers.cpp <c>isStringMalformed</c>), so only printable ASCII is allowed here.
        /// </summary>
        public static string? CheckUsername(string? username)
        {
            if (string.IsNullOrEmpty(username)) return "Enter an account name.";
            if (username.Length > MaxUsernameLength) return $"The account name is at most {MaxUsernameLength} characters.";
            if (!IsPrintableAscii(username)) return "The account name can use only English letters, digits, spaces and punctuation.";
            return null;
        }

        /// <summary>Why <paramref name="password"/> can't be sent, or null; a new password must also be <see cref="MinNewPasswordLength"/> long.</summary>
        public static string? CheckPassword(string? password, bool isNew = false)
        {
            if (string.IsNullOrEmpty(password)) return "Enter a password.";
            if (isNew && password.Length < MinNewPasswordLength) return $"The password must be at least {MinNewPasswordLength} characters.";
            if (password.Length > MaxPasswordLength) return $"The password is at most {MaxPasswordLength} characters.";
            if (!IsPrintableAscii(password)) return "The password can use only English letters, digits, spaces and punctuation.";
            return null;
        }

        private static bool IsPrintableAscii(string value)
        {
            foreach (char c in value)
            {
                if (c < 0x20 || c > 0x7E) return false;
            }
            return true;
        }

        private static void Require(string? problem)
        {
            if (problem != null) throw new LsbAccountException(LsbAccountError.InvalidInput, problem);
        }

        /// <summary>
        /// Creates a LandSandBoat account (command 0x20). Answers 0x03 on success, 0x04 for a name in use, 0x08 when
        /// the server has account creation off and 0x09 for a database error (src/login/auth_session.cpp LOGIN_CREATE).
        /// </summary>
        public async Task CreateAccountAsync(string host, int port, string username, string password, CancellationToken ct = default)
        {
            Require(CheckUsername(username));
            Require(CheckPassword(password, isNew: true));
            var reply = await SendAuthCommandAsync(host, port, new LsbAuthRequest(LsbAuthCommand.CreateAccount, username, password), ct).ConfigureAwait(false);
            ThrowOnErrorMessage(reply);
            switch (reply.Result)
            {
                case LsbAuthResult.CreateSuccess:
                    GordianLog.Info(LogCategories.LsbLogin, $"Created account '{username}' on {host}:{port}.");
                    return;
                case LsbAuthResult.CreateNameTaken:
                    throw new LsbAccountException(LsbAccountError.NameTaken, $"The account name '{username}' is already taken on this server.");
                case LsbAuthResult.CreateDisabled:
                    throw new LsbAccountException(LsbAccountError.CreationDisabled, "This server does not allow creating accounts from the client. Ask its operator for an account.");
                case LsbAuthResult.CreateError:
                    throw new LsbAccountException(LsbAccountError.CreationFailed, "The server could not create the account. Try again, or ask its operator.");
                default:
                    throw Unexpected(reply, "create the account");
            }
        }

        /// <summary>
        /// Changes an account's password (command 0x30, <c>new_password</c>). An account with two-factor needs the current
        /// code in <paramref name="otp"/>. The server answers 0x06 on success and 0x07 for a wrong password or code; on
        /// success it also forgets every trusted computer of the account.
        /// </summary>
        public async Task ChangePasswordAsync(string host, int port, string username, string currentPassword, string newPassword,
            string otp = "", CancellationToken ct = default)
        {
            Require(CheckUsername(username));
            Require(CheckPassword(currentPassword));
            Require(CheckPassword(newPassword, isNew: true));
            var reply = await SendAuthCommandAsync(host, port,
                new LsbAuthRequest(LsbAuthCommand.ChangePassword, username, currentPassword, newPassword, otp ?? string.Empty), ct).ConfigureAwait(false);
            ThrowOnErrorMessage(reply);
            switch (reply.Result)
            {
                case LsbAuthResult.ChangePasswordSuccess:
                    GordianLog.Info(LogCategories.LsbLogin, $"Changed the password of account '{username}' on {host}:{port}.");
                    return;
                case LsbAuthResult.ChangePasswordError:
                    throw new LsbAccountException(LsbAccountError.CredentialsRejected,
                        "The server refused the change: check the account name, current password and (if the account has one) the two-factor code.");
                default:
                    throw Unexpected(reply, "change the password");
            }
        }

        /// <summary>
        /// Starts two-factor setup (command 0x31): the server makes a new secret and answers 0x10 with
        /// <c>TOTP_uri</c> = <c>otpauth://totp/{server}:{account}?secret=...&amp;issuer={server}&amp;algorithm=SHA1&amp;digits=6&amp;period=30</c>.
        /// It is refused ("Failed to validate credentials") for a wrong password or an account that already has two-factor.
        /// Nothing changes for the login until <see cref="ConfirmTotpSetupAsync"/> verifies a code; starting again replaces
        /// an unconfirmed secret.
        /// </summary>
        public async Task<LsbTotpSetup> BeginTotpSetupAsync(string host, int port, string username, string password, CancellationToken ct = default)
        {
            Require(CheckUsername(username));
            Require(CheckPassword(password));
            var reply = await SendAuthCommandAsync(host, port, new LsbAuthRequest(LsbAuthCommand.CreateTotp, username, password), ct).ConfigureAwait(false);
            ThrowOnErrorMessage(reply, "Check the account name and password. If the account already has two-factor, remove it first.");
            if (reply.Result != LsbAuthResult.CreateTotpSuccess) throw Unexpected(reply, "start two-factor setup");
            var setup = ParseOtpAuthUri(reply.GetString("TOTP_uri"), username)
                ?? throw new LsbAccountException(LsbAccountError.ServerError, "The server started two-factor setup but sent no usable key.");
            GordianLog.Info(LogCategories.LsbLogin, $"Two-factor setup started for account '{username}' on {host}:{port}.");
            return setup;
        }

        /// <summary>
        /// Finishes two-factor setup (command 0x34) with a current code from the new secret. The server answers 0x11 with
        /// the account's <c>recovery_code</c>, which removes two-factor when the authenticator is lost; it is shown once.
        /// </summary>
        public async Task<string> ConfirmTotpSetupAsync(string host, int port, string username, string password, string code, CancellationToken ct = default)
        {
            Require(CheckUsername(username));
            Require(CheckPassword(password));
            Require(string.IsNullOrWhiteSpace(code) ? "Enter the 6-digit code from the authenticator." : null);
            var reply = await SendAuthCommandAsync(host, port, new LsbAuthRequest(LsbAuthCommand.VerifyTotp, username, password, Otp: code.Trim()), ct).ConfigureAwait(false);
            ThrowOnErrorMessage(reply, "Check the password and enter the current code (codes change every 30 seconds).");
            if (reply.Result != LsbAuthResult.VerifyTotpSuccess) throw Unexpected(reply, "verify the two-factor code");
            GordianLog.Info(LogCategories.LsbLogin, $"Two-factor is now on for account '{username}' on {host}:{port}.");
            return reply.GetString("recovery_code") ?? string.Empty;
        }

        /// <summary>
        /// Turns two-factor off (command 0x32) with a current code or the recovery code (compared without case). The
        /// server answers 0x12 and forgets every trusted computer of the account.
        /// </summary>
        public async Task RemoveTotpAsync(string host, int port, string username, string password, string codeOrRecoveryCode, CancellationToken ct = default)
        {
            Require(CheckUsername(username));
            Require(CheckPassword(password));
            Require(string.IsNullOrWhiteSpace(codeOrRecoveryCode) ? "Enter a current code or the recovery code." : null);
            var reply = await SendAuthCommandAsync(host, port,
                new LsbAuthRequest(LsbAuthCommand.RemoveTotp, username, password, Otp: codeOrRecoveryCode.Trim()), ct).ConfigureAwait(false);
            ThrowOnErrorMessage(reply, "Check the account name, password and code (a current code or the recovery code).");
            if (reply.Result != LsbAuthResult.RemoveTotpSuccess) throw Unexpected(reply, "remove two-factor");
            GordianLog.Info(LogCategories.LsbLogin, $"Two-factor removed from account '{username}' on {host}:{port}.");
        }

        /// <summary>
        /// Replaces the recovery code (command 0x33) given a current code or the old recovery code. The server answers
        /// with result 0x11 and the new <c>recovery_code</c>.
        /// </summary>
        public async Task<string> RegenerateRecoveryCodeAsync(string host, int port, string username, string password, string codeOrRecoveryCode,
            CancellationToken ct = default)
        {
            Require(CheckUsername(username));
            Require(CheckPassword(password));
            Require(string.IsNullOrWhiteSpace(codeOrRecoveryCode) ? "Enter a current code or the recovery code." : null);
            var reply = await SendAuthCommandAsync(host, port,
                new LsbAuthRequest(LsbAuthCommand.RegenerateRecoveryCode, username, password, Otp: codeOrRecoveryCode.Trim()), ct).ConfigureAwait(false);
            ThrowOnErrorMessage(reply, "Check the account name, password and code (a current code or the recovery code).");
            string? code = reply.GetString("recovery_code");
            if (reply.Result != LsbAuthResult.VerifyTotpSuccess || string.IsNullOrEmpty(code)) throw Unexpected(reply, "make a new recovery code");
            GordianLog.Info(LogCategories.LsbLogin, $"New recovery code made for account '{username}' on {host}:{port}.");
            return code;
        }

        /// <summary>
        /// The secret, issuer and account of LandSandBoat's <c>TOTP_uri</c>, with a well-formed <c>otpauth://</c> link
        /// rebuilt from them: LandSandBoat formats the link without escaping, so a server name with spaces makes it invalid.
        /// Null when the link has no Base32 secret.
        /// </summary>
        public static LsbTotpSetup? ParseOtpAuthUri(string? uri, string fallbackAccount)
        {
            if (string.IsNullOrWhiteSpace(uri)) return null;
            int query = uri.IndexOf('?');
            string secret = string.Empty, issuer = string.Empty;
            if (query >= 0)
            {
                foreach (string pair in uri[(query + 1)..].Split('&'))
                {
                    int eq = pair.IndexOf('=');
                    if (eq <= 0) continue;
                    string key = pair[..eq], value = Uri.UnescapeDataString(pair[(eq + 1)..]);
                    if (key.Equals("secret", StringComparison.OrdinalIgnoreCase)) secret = value.Trim().Replace(" ", string.Empty).ToUpperInvariant();
                    else if (key.Equals("issuer", StringComparison.OrdinalIgnoreCase)) issuer = value.Trim();
                }
            }
            if (!Profiles.TotpEngine.IsValidSecret(secret)) return null;

            const string prefix = "otpauth://totp/";
            string label = uri.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                ? Uri.UnescapeDataString(uri[prefix.Length..(query >= 0 ? query : uri.Length)])
                : string.Empty;
            string account = label.Contains(':') ? label[(label.IndexOf(':') + 1)..] : label;
            if (string.IsNullOrWhiteSpace(account)) account = fallbackAccount;
            if (string.IsNullOrWhiteSpace(issuer)) issuer = label.Contains(':') ? label[..label.IndexOf(':')] : "LandSandBoat";

            string link = new StringBuilder(prefix)
                .Append(Uri.EscapeDataString(issuer)).Append(':').Append(Uri.EscapeDataString(account))
                .Append("?secret=").Append(secret)
                .Append("&issuer=").Append(Uri.EscapeDataString(issuer))
                .Append("&algorithm=SHA1&digits=6&period=30")
                .ToString();
            return new LsbTotpSetup(secret, issuer, account, link);
        }

        /// <summary>Turns the server's <c>error_message</c> into an exception (its "Failed to validate credentials" with <paramref name="credentialsHint"/>).</summary>
        private static void ThrowOnErrorMessage(LsbAuthReply reply, string? credentialsHint = null)
        {
            if (reply.ErrorMessage == null) return;
            if (reply.ErrorMessage.Contains("validate credentials", StringComparison.OrdinalIgnoreCase))
            {
                throw new LsbAccountException(LsbAccountError.CredentialsRejected,
                    "The server refused the request." + (credentialsHint != null ? " " + credentialsHint : string.Empty));
            }
            throw new LsbAccountException(LsbAccountError.ServerError, $"The server refused the request: {reply.ErrorMessage}");
        }

        private static LsbAccountException Unexpected(LsbAuthReply reply, string action) =>
            new(LsbAccountError.ServerError, $"The server did not {action} (it answered result {reply.Result}).");
    }
}
