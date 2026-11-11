// src/Gordian.Core/Network/Retail/RetailLoginClient.cs
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Gordian.Core.Diagnostics;

namespace Gordian.Core.Network.Retail
{
    /// <summary>
    /// A retail profile's login inputs: the PlayOnline ID and password, the Square Enix account, the 6-digit
    /// authenticator code (empty for an account without one) and the FFXI install the client plays with.
    /// </summary>
    public sealed record RetailCredentials(string PolId, string PolPassword, string SquareEnixId, string SquareEnixPassword,
        string OneTimeCode, string FfxiDirectory);

    /// <summary>
    /// A logged-in PlayOnline account: the Core session (kept open and answering pings while the game runs, spec section
    /// 9 item 6), the presence client that hands out lobby keys, and the install values the lobby sends. Each lobby
    /// connection (<see cref="OpenLobbyAsync"/>) asks for a fresh lobby key, so a Log Out can go back to character
    /// select on the same account session. Disposing it ends the PlayOnline session.
    /// </summary>
    public sealed class RetailAccountSession : IAsyncDisposable
    {
        private readonly PolCoreSession _core;
        private readonly PolPresenceClient _presence;
        private readonly Action<PacketLogEntry>? _log;
        private int _disposed;

        internal RetailAccountSession(PolCoreSession core, PolPresenceClient presence, string clientVersion, uint clientChecksum, Action<PacketLogEntry>? log)
        {
            _core = core;
            _presence = presence;
            ClientVersion = clientVersion;
            ClientChecksum = clientChecksum;
            _log = log;
        }

        /// <summary>The installed FFXI version sent in the lobby login (patch.cfg).</summary>
        public string ClientVersion { get; }

        /// <summary><see cref="ClientFileChecksum"/> of the install's FFXiMain.dll, sent in the selection digest.</summary>
        public uint ClientChecksum { get; }

        /// <summary>Whether <see cref="DisposeAsync"/> has ended the session.</summary>
        public bool IsDisposed => Volatile.Read(ref _disposed) != 0;

        /// <summary>Stages 9-13: connects to the FFXI lobby with a fresh lobby key and reads the character list.</summary>
        public Task<RetailLobbySession> OpenLobbyAsync(CancellationToken ct = default,
            string host = RetailLobbyPackets.Host, int port = RetailLobbyPackets.Port)
        {
            ObjectDisposedException.ThrowIf(IsDisposed, this);
            var login = new RetailLobbyLogin(_core.GreetingTimestamp, _core.SessionKey, _core.RoutingToken, ClientVersion, ClientChecksum);
            return RetailLobbySession.ConnectAsync(login,
                token => _presence.RequestSessionKeyAsync(PolSessionKeyService.Lobby, token), host, port, _log, ct: ct);
        }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            GordianLog.Info("RETAIL_LOGIN", "Ending the PlayOnline session.");
            await _core.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Runs the retail login stages in order (spec v2.1.19 section 3): the FFXI version check against the install's
    /// patch.cfg, the Core IRC session (gatekeeper, RSA exchange, <c>NICK</c>), the Square Enix account login, and the
    /// Viewer's service-1 key request. The Viewer version check (stage 1) and the PML pages (stage 7) are skipped: the
    /// login servers do not check that they ran (spec section 9 item 5, not verified). The lobby follows through
    /// <see cref="RetailAccountSession.OpenLobbyAsync"/>.
    /// </summary>
    public sealed class RetailLoginClient
    {
        /// <summary>Raised with a short progress line for the launcher's status bar.</summary>
        public event EventHandler<string>? StatusChanged;

        /// <summary>Receives every lobby packet sent and read, for the packet inspector.</summary>
        public Action<PacketLogEntry>? PacketLog { get; set; }

        /// <summary>How long the version check may take.</summary>
        public static TimeSpan VersionCheckTimeout { get; set; } = TimeSpan.FromSeconds(15);

        /// <summary>Logs the PlayOnline and Square Enix accounts in. Throws <see cref="RetailLoginException"/> on a refusal.</summary>
        public async Task<RetailAccountSession> LoginAsync(RetailCredentials credentials, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(credentials);
            if (!PolIdEncoding.IsValid(credentials.PolId))
                throw new RetailLoginException("credentials", "The POL ID must be 4 letters followed by 4 digits.");
            if (string.IsNullOrEmpty(credentials.PolPassword) || credentials.PolPassword.Length > 16)
                throw new RetailLoginException("credentials", "The POL password must be 1 to 16 characters.");
            if (string.IsNullOrWhiteSpace(credentials.SquareEnixId) || string.IsNullOrEmpty(credentials.SquareEnixPassword))
                throw new RetailLoginException("credentials", "The profile needs a Square Enix ID and password.");

            // The install values first: a missing or outdated install fails before any credentials are sent.
            string root = credentials.FfxiDirectory;
            string version = PatchConfigFile.ReadInstalledVersion(root)
                ?? throw new RetailLoginException("version check", $"No FFXI version in '{Path.Combine(root, PatchConfigFile.FileName)}'.");
            uint checksum;
            try
            {
                checksum = ClientFileChecksum.ComputeFfxiMain(root);
            }
            catch (IOException ex)
            {
                throw new RetailLoginException("version check", $"Could not read {ClientFileChecksum.FfxiMainFileName} in '{root}': {ex.Message}", inner: ex);
            }

            Report($"Checking the FFXI version ({version})...");
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                timeout.CancelAfter(VersionCheckTimeout);
                var answer = await PolpVersionChecker.CheckAsync(PolpPackets.FfxiHost, PolpPackets.FfxiPort, PolpPackets.FfxiProduct,
                    PatchConfigFile.PolpVersion(PolpPackets.FfxiProductId, version), timeout.Token).ConfigureAwait(false);
                if (!answer.IsUpToDate)
                    throw new RetailLoginException("version check",
                        $"FFXI {version} is not the current version (server status {answer.Status}, current {answer.CurrentVersion}). Update it in PlayOnline Viewer, then log in again.",
                        (int)answer.Status);
            }

            Report("Logging in to PlayOnline...");
            var core = await PolCoreSession.LoginAsync(credentials.PolId, credentials.PolPassword, ct).ConfigureAwait(false);
            try
            {
                var presence = new PolPresenceClient(core);
                Report("Logging in to the Square Enix account...");
                await presence.AuthenticateAsync(credentials.SquareEnixId, credentials.SquareEnixPassword, credentials.OneTimeCode, ct).ConfigureAwait(false);

                // Retail asks for the Viewer's service-1 key next; its use is unknown (spec section 1.1 O14), so a failure only warns.
                try
                {
                    await presence.RequestSessionKeyAsync(PolSessionKeyService.Viewer, ct).ConfigureAwait(false);
                }
                catch (Exception ex) when (!ct.IsCancellationRequested &&
                    ex is RetailLoginException or IOException or System.Net.Sockets.SocketException or OperationCanceledException)
                {
                    GordianLog.Warning("RETAIL_LOGIN", $"The Viewer key request failed ({ex.Message}); continuing.");
                }

                return new RetailAccountSession(core, presence, version, checksum, PacketLog);
            }
            catch
            {
                await core.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }

        private void Report(string status)
        {
            GordianLog.Info("RETAIL_LOGIN", status);
            StatusChanged?.Invoke(this, status);
        }
    }
}
