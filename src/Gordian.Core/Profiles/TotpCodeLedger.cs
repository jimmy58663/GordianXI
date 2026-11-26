// src/Gordian.Core/Profiles/TotpCodeLedger.cs
using System;
using System.Collections.Generic;

namespace Gordian.Core.Profiles
{
    /// <summary>
    /// Which 30-second TOTP step GordianXI last sent for each account, so it never sends a code the server already took.
    /// LandSandBoat accepts the code of the current step or one step either side (clock drift, RFC 6238 5.2) and, since
    /// a77b16aa96 (2026-09-22, "Prevent TOTP code reuse"), refuses any step at or before the last one it accepted for the
    /// account, with login result 2. A second login within the same 30 seconds (Log Out and back, a lobby retry, a login
    /// right after the two-factor setup's verify) therefore sends the next step's code, which the server's window
    /// already accepts; for a third one in the same window <see cref="TryTake"/> says how long until the next step. Older LandSandBoat builds
    /// accept the same window without the reuse check. Behaviour referenced from LandSandBoat
    /// (https://github.com/LandSandBoat/server, src/login/otp_helpers.h <c>validateTOTP</c>).
    /// </summary>
    public sealed class TotpCodeLedger
    {
        private readonly Dictionary<string, long> _lastSent = new(StringComparer.Ordinal);
        private readonly object _gate = new();

        /// <summary>The ledger every login and account request of this process shares.</summary>
        public static TotpCodeLedger Shared { get; } = new();

        /// <summary>The ledger key of an account: server host (any case), auth port and account name (any case).</summary>
        public static string AccountKey(string host, int port, string username) =>
            $"{(host ?? string.Empty).Trim().ToLowerInvariant()}:{port}:{(username ?? string.Empty).Trim().ToLowerInvariant()}";

        /// <summary>
        /// The code to send now for <paramref name="account"/> from <paramref name="secret"/>, and records its step as
        /// sent: the current step's code if it was not sent yet, else the next unsent step's code while that is at most
        /// one step ahead. Null when even that was sent; <paramref name="retryAfter"/> then says when the next code can go.
        /// </summary>
        public string? TryTake(string account, string secret, DateTimeOffset now, out TimeSpan retryAfter)
        {
            retryAfter = TimeSpan.Zero;
            long current = TotpEngine.StepAt(now);
            lock (_gate)
            {
                long step = current;
                if (_lastSent.TryGetValue(account, out long last) && last >= step) step = last + 1;
                if (step > current + 1)
                {
                    // The step before it must begin before this one is inside the server's window; a second's margin.
                    retryAfter = TotpEngine.StepStart(step - 1) - now + TimeSpan.FromSeconds(1);
                    return null;
                }
                _lastSent[account] = step;
                return TotpEngine.GenerateCode(secret, step);
            }
        }

        /// <summary>
        /// Records that <paramref name="code"/> was sent for <paramref name="account"/>, when it is the code of
        /// <paramref name="secret"/> for a step inside the server's window (a code typed from an app, or filled earlier).
        /// A code that matches no step there is ignored.
        /// </summary>
        public void NoteSent(string account, string secret, string code, DateTimeOffset now)
        {
            if (string.IsNullOrWhiteSpace(code) || !TotpEngine.IsValidSecret(secret)) return;
            long current = TotpEngine.StepAt(now);
            for (long step = current + 1; step >= current - 1; step--)
            {
                if (TotpEngine.GenerateCode(secret, step) != code.Trim()) continue;
                lock (_gate)
                {
                    if (!_lastSent.TryGetValue(account, out long last) || last < step) _lastSent[account] = step;
                }
                return;
            }
        }
    }
}
