// src/Gordian.Core/Network/Retail/RetailLoginClient.cs
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Gordian.Core.Diagnostics;
using Gordian.Core.Network.Lobby;

namespace Gordian.Core.Network.Retail
{
    /// <summary>
    /// A retail profile's login inputs: the PlayOnline ID and password, the Square Enix account, the 6-digit
    /// authenticator code (empty for an account without one), the FFXI install the client plays with, and the PlayOnline
    /// Viewer install whose versions the login reports (null: <c>PlayOnlineViewer</c> beside the FFXI folder).
    /// </summary>
    public sealed record RetailCredentials(string PolId, string PolPassword, string SquareEnixId, string SquareEnixPassword,
        string OneTimeCode, string FfxiDirectory, string? ViewerDirectory = null);

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

        /// <summary>
        /// Stages 9-13: connects to the FFXI lobby (the same <see cref="LobbySession"/> LandSandBoat uses) with this
        /// session as its pol.exe side, a fresh lobby key, and reads the character list.
        /// </summary>
        public Task<LobbySession> OpenLobbyAsync(CancellationToken ct = default,
            string host = PolLobbyHandoff.Host, int port = PolLobbyHandoff.Port)
        {
            ObjectDisposedException.ThrowIf(IsDisposed, this);
            var login = new RetailLobbyLogin(_core.GreetingTimestamp, _core.SessionKey, _core.RoutingToken, ClientVersion, ClientChecksum);
            var handoff = new PolLobbyHandoff(login, token => _presence.RequestSessionKeyAsync(PolSessionKeyService.Lobby, token));
            return RetailLoginClient.Stage("FFXI lobby", () => LobbySession.ConnectAsync(host, port, handoff, _log, TimeSpan.FromSeconds(15), ct), ct);
        }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            GordianLog.Info("RETAIL_LOGIN", "Ending the PlayOnline session.");
            await _core.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Runs the retail login stages in retail's order (spec v2.1.19 section 3), so the traffic matches a retail login:
    /// the PlayOnline Viewer version check, the Core IRC session (gatekeeper, RSA exchange, <c>NICK</c>), the Square Enix
    /// account login, the Viewer's service-1 key request, the PML pages, and the FFXI version check. The versions come
    /// from the installs' patch.cfg files. The lobby follows through <see cref="RetailAccountSession.OpenLobbyAsync"/>.
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

            // The install values first, read from disk before anything is sent: a missing install fails here.
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
            string viewerRoot = ResolveViewerDirectory(credentials);
            string? viewerVersion = PatchConfigFile.ReadInstalledVersion(viewerRoot);
            string? viewerFileVersion = PolPmlClient.ReadViewerVersion(viewerRoot);
            if (viewerVersion == null || viewerFileVersion == null)
                throw new RetailLoginException("version check",
                    $"No PlayOnline Viewer install in '{viewerRoot}' ({PatchConfigFile.FileName} and {PolPmlClient.VersionFileName}). The login sends the Viewer's version as retail does; set the Viewer folder or install PlayOnline.");

            // Retail's order (spec v2.1.19 section 3): the Viewer version check, the Core session, the Square Enix login,
            // the Viewer's service-1 key, the PML pages, then the FFXI version check. Every stage runs, even those whose
            // answers the client does not need, so the traffic matches a retail login.
            Report($"Checking the PlayOnline Viewer version ({viewerVersion})...");
            await CheckVersionAsync("PlayOnline Viewer", viewerVersion, PolpPackets.ViewerHost, PolpPackets.ViewerPort, PolpPackets.ViewerProduct,
                PolpPackets.ViewerProductId, ct).ConfigureAwait(false);

            Report("Logging in to PlayOnline...");
            var core = await Stage("PlayOnline login", () => PolCoreSession.LoginAsync(credentials.PolId, credentials.PolPassword, ct), ct).ConfigureAwait(false);
            try
            {
                var presence = new PolPresenceClient(core);
                Report("Logging in to the Square Enix account...");
                await Stage("Square Enix login", async () =>
                {
                    await presence.AuthenticateAsync(credentials.SquareEnixId, credentials.SquareEnixPassword, credentials.OneTimeCode, ct).ConfigureAwait(false);
                    return true;
                }, ct).ConfigureAwait(false);

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

                // Stage 7: the PML pages. Maintenance shows only here (the Play button flag), so it stops the login; any
                // other failure only warns, since the later stages do not depend on the pages.
                Report("Opening the PlayOnline pages...");
                PmlLaunchStatus? launch = null;
                try
                {
                    using var pmlTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    pmlTimeout.CancelAfter(VersionCheckTimeout);
                    launch = await Stage("PlayOnline pages", () => PolPmlClient.VisitAsync(core, viewerFileVersion, pmlTimeout.Token), ct).ConfigureAwait(false);
                }
                catch (RetailLoginException ex) when (!ct.IsCancellationRequested)
                {
                    GordianLog.Warning("RETAIL_LOGIN", $"{ex.Message} Continuing.");
                }
                if (launch is { PlayEnabled: false } closed)
                    throw new RetailLoginException("PlayOnline pages",
                        $"FINAL FANTASY XI is not open for play{(string.IsNullOrWhiteSpace(closed.Status) ? " (maintenance)" : $": {closed.Status}")}.");

                Report($"Checking the FFXI version ({version})...");
                await CheckVersionAsync("FFXI", version, PolpPackets.FfxiHost, PolpPackets.FfxiPort, PolpPackets.FfxiProduct,
                    PolpPackets.FfxiProductId, ct).ConfigureAwait(false);

                return new RetailAccountSession(core, presence, version, checksum, PacketLog);
            }
            catch
            {
                await core.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }

        /// <summary>
        /// The PlayOnline Viewer's install root: the profile's, else <c>PlayOnlineViewer</c> beside the FFXI folder (both
        /// sit under <c>PlayOnline\SquareEnix</c> in a standard install).
        /// </summary>
        internal static string ResolveViewerDirectory(RetailCredentials credentials)
        {
            if (!string.IsNullOrWhiteSpace(credentials.ViewerDirectory)) return credentials.ViewerDirectory.Trim();
            string ffxi = credentials.FfxiDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return Path.Combine(Path.GetDirectoryName(ffxi) ?? ffxi, DefaultViewerFolderName);
        }

        /// <summary>The Viewer's folder name in a standard install.</summary>
        public const string DefaultViewerFolderName = "PlayOnlineViewer";

        /// <summary>
        /// One POLP version check (spec section 4.1). A reply that cannot be read only warns; an explicit "outdated"
        /// answer stops the login.
        /// </summary>
        private static async Task CheckVersionAsync(string product, string installed, string host, int port, string productCode, string productId, CancellationToken ct)
        {
            PolpVersionResponse? answer = null;
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                timeout.CancelAfter(VersionCheckTimeout);
                try
                {
                    answer = await PolpVersionChecker.CheckAsync(host, port, productCode, PatchConfigFile.PolpVersion(productId, installed), timeout.Token).ConfigureAwait(false);
                }
                catch (Exception ex) when (!ct.IsCancellationRequested &&
                    ex is RetailLoginException or IOException or System.Net.Sockets.SocketException or OperationCanceledException)
                {
                    GordianLog.Warning("RETAIL_LOGIN", $"The {product} version check could not be read ({ex.GetType().Name}: {ex.Message}); continuing without it.");
                }
            }
            if (answer is { IsUpToDate: false } outdated)
                throw new RetailLoginException("version check",
                    $"{product} {installed} is not the current version (server status {outdated.Status}, current {outdated.CurrentVersion}). Update it in PlayOnline Viewer, then log in again.",
                    (int)outdated.Status);
            if (answer is { } ok)
                GordianLog.Info("RETAIL_LOGIN", $"{product} version {installed} accepted (current {ok.CurrentVersion}).");
        }

        /// <summary>
        /// Runs a stage and names it in a connection failure: a server closing or timing out surfaces as a
        /// <see cref="RetailLoginException"/> that says which stage it was, not a bare stream error.
        /// </summary>
        internal static async Task<T> Stage<T>(string stage, Func<Task<T>> run, CancellationToken ct)
        {
            try
            {
                return await run().ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is EndOfStreamException or IOException or System.Net.Sockets.SocketException)
            {
                throw new RetailLoginException(stage, $"The server closed or refused the connection ({ex.Message}).", inner: ex);
            }
            catch (OperationCanceledException ex) when (!ct.IsCancellationRequested)
            {
                throw new RetailLoginException(stage, "The server did not answer in time.", inner: ex);
            }
        }

        private void Report(string status)
        {
            GordianLog.Info("RETAIL_LOGIN", status);
            StatusChanged?.Invoke(this, status);
        }
    }
}
