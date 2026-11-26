// src/Gordian.Core/Network/LandSandBoat/LsbLoginClient.cs
using System;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Gordian.Core.Diagnostics;
using Gordian.Core.Network.Lobby;

namespace Gordian.Core.Network.LandSandBoat
{
    /// <summary>
    /// One character of the xi_view 0x20 list: its 1-based slot (raw list position, free slots counted), id and name.
    /// </summary>
    public readonly record struct LsbCharacterSlot(int Slot, uint Id, string Name);

    /// <summary>
    /// Clean-room C# (.NET 10) login client for LandSandBoat private servers.
    /// Logs in to xi_connect (port 54231, TLS), then opens the lobby's xi_data (54230) and xi_view (54001) connections,
    /// over TLS on servers that require xiloader 3.0 and plain TCP before that (<see cref="LsbLobbyTls"/>), generating
    /// the Blowfish session key and acquiring the zone map endpoint without requiring a third-party bootloader.
    /// Protocol wire specifications referenced from LandSandBoat (https://github.com/LandSandBoat/server).
    /// </summary>
    public sealed partial class LsbLoginClient
    {
        public const int DefaultConnectPort = 54231;
        public const int DefaultDataPort = 54230;
        public const int DefaultViewPort = 54001;

        /// <summary>
        /// Fires whenever a lobby packet is sent or received, forwarding to the live packet inspector.
        /// </summary>
        public event EventHandler<PacketLogEntry>? PacketInspected;

        /// <summary>
        /// Fires when the login client status changes (e.g. during transient retry attempts).
        /// </summary>
        public event EventHandler<string>? StatusChanged;

        /// <summary>
        /// The auth server certificates the user approved (trust on first use, #445). Defaults to the shared store in
        /// the user's settings folder.
        /// </summary>
        public KnownServerCertificateStore CertificateStore { get; init; } = KnownServerCertificateStore.Default;

        /// <summary>
        /// The client version the lobby login (0x26) reports: the desktop shell always sets the installed FFXI version from
        /// <c>patch.cfg</c>; <see cref="XiloaderLobbyHandoff.DefaultClientVersion"/> is only the default for other callers. LandSandBoat compares
        /// its first 6 characters with <c>login.CLIENT_VER</c> and by default refuses older ones (<c>VER_LOCK = 2</c>).
        /// </summary>
        public string ClientVersion { get; set; } = XiloaderLobbyHandoff.DefaultClientVersion;

        /// <summary>
        /// Asked when an auth server's certificate does not verify and is not the one pinned for that server: return
        /// <see langword="true"/> to trust and pin it. Without a handler such a server is refused
        /// (<see cref="UntrustedServerCertificateException"/>). Credentials are only sent once the server is trusted.
        /// </summary>
        public Func<ServerCertificateInfo, CancellationToken, Task<bool>>? ConfirmServerCertificate { get; set; }

        /// <summary>
        /// The xiloader version GordianXI reports first: what current LandSandBoat requires (3.0 since 9b6df7c7e2, with the
        /// lobby over TLS from 778b74294a). An older server that wants another major.minor says so and the login is
        /// retried; the version it accepts decides whether the lobby uses TLS (<see cref="LsbLobbyTls.UsesTls"/>).
        /// </summary>
        public static readonly (byte Major, byte Minor, byte Patch) DefaultXiloaderVersion = (3, 0, 0);

        /// <summary>The xiloader version each auth server (host:port) accepted, for the rest of this process.</summary>
        private static readonly ConcurrentDictionary<string, (byte Major, byte Minor, byte Patch)> s_acceptedVersions =
            new(StringComparer.OrdinalIgnoreCase);

        private static string ServerKey(string host, int port) => $"{host}:{port}";

        /// <summary>The xiloader version that will be sent first to <paramref name="host"/>:<paramref name="port"/>.</summary>
        public static (byte Major, byte Minor, byte Patch) XiloaderVersionFor(string host, int port) =>
            s_acceptedVersions.TryGetValue(ServerKey(host, port), out var version) ? version : DefaultXiloaderVersion;

        private static readonly Regex s_requiredVersion = new(
            @"(?:requires version|update to version)\s*'?(?<major>\d{1,3})\.(?<minor>\d{1,3})",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>
        /// The xiloader major.minor an LSB auth rejection asks for: "Unsupported xiloader version a.b.c. This server
        /// requires version X.Y.x." (LandSandBoat since f660a273e5, Sep 2026) or "Your xiloader is too old. Please update to
        /// version 'X.Y.x'. ..." (before it). Message formats referenced from LandSandBoat
        /// (https://github.com/LandSandBoat/server, src/login/auth_session.cpp, commits 1d1c2320cc and f660a273e5).
        /// </summary>
        public static bool TryParseRequiredXiloaderVersion(string? message, out (byte Major, byte Minor) required)
        {
            required = default;
            if (string.IsNullOrEmpty(message)) return false;
            var match = s_requiredVersion.Match(message);
            if (!match.Success
                || !byte.TryParse(match.Groups["major"].Value, out byte major)
                || !byte.TryParse(match.Groups["minor"].Value, out byte minor))
            {
                return false;
            }
            required = (major, minor);
            return true;
        }

        /// <summary>
        /// Authenticates against xi_connect (TCP TLS) using JSON command 0x10 (LOGIN_ATTEMPT).
        /// Returns the account ID and 16-byte session hash, with the server's key and whether the lobby uses TLS. The
        /// server's version check is negotiated (<see cref="SendAuthCommandAsync"/>). <paramref name="transport"/>
        /// overrides the lobby transport the accepted version implies.
        /// </summary>
        public async Task<LsbAuthentication> AuthenticateAsync(
            string host,
            int port = DefaultConnectPort,
            string username = "",
            string password = "",
            string otp = "",
            CancellationToken ct = default,
            LsbLobbyTransport transport = LsbLobbyTransport.Auto)
        {
            ArgumentNullException.ThrowIfNull(host);

            var reply = await SendAuthCommandAsync(host, port, new LsbAuthRequest(LsbAuthCommand.Login, username, password, Otp: otp ?? string.Empty), ct).ConfigureAwait(false);
            if (reply.ErrorMessage != null)
            {
                throw new InvalidOperationException($"LandSandBoat auth error: {reply.ErrorMessage}");
            }
            if (reply.Result != LsbAuthResult.LoginSuccess)
            {
                // LandSandBoat answers 2 for a wrong name or password and for a refused two-factor code, including one
                // it already accepted (src/login/auth_session.cpp, otp_helpers.h validateTOTP).
                throw new InvalidOperationException(reply.Result == LsbAuthResult.Error && !string.IsNullOrEmpty(otp)
                    ? "The server refused the login (result 2): check the account name and password, and that the two-factor code is current and was not used for this account in the last 30 seconds."
                    : $"Authentication failed with result code {reply.Result}. Check username and password.");
            }

            var root = reply.Root;
            uint accountId = root.GetProperty("account_id").GetUInt32();
            byte[] sessionHash = new byte[16];
            int idx = 0;
            foreach (var b in root.GetProperty("session_hash").EnumerateArray())
            {
                if (idx < 16) sessionHash[idx++] = b.GetByte();
            }
            // The account id stays out of the log: logs are shared in bug reports (CodeQL cs/cleartext-storage, #441).
            GordianLog.Info(LogCategories.LsbLogin, $"Auth success! Session hash acquired ({sessionHash.Length} bytes).");

            var version = reply.Version;
            bool lobbyTls = LsbLobbyTls.UsesTls(version, transport);
            GordianLog.Info(LogCategories.LsbLogin, $"Auth server {host}:{port} accepted xiloader version {version.Major}.{version.Minor}.{version.Patch}; " +
                $"lobby over {(lobbyTls ? "TLS" : "plain TCP")}{(transport == LsbLobbyTransport.Auto ? "" : $" (profile override {transport})")}; server key {reply.ServerKey}.");
            return new LsbAuthentication(accountId, sessionHash, version, reply.ServerKey, lobbyTls);
        }

        /// <summary>
        /// Sends one xi_connect command and returns the server's JSON reply. The server's version check is negotiated:
        /// GordianXI reports <see cref="DefaultXiloaderVersion"/> (or the version this server last accepted) and, when the
        /// server answers that it requires another xiloader major.minor, sends the command once more reporting that
        /// version, then remembers the version the server accepted per server. Every command (login, account creation,
        /// password change, two-factor) goes through the same check (LandSandBoat src/login/auth_session.cpp).
        /// </summary>
        internal async Task<LsbAuthReply> SendAuthCommandAsync(string host, int port, LsbAuthRequest request, CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(host);
            var version = XiloaderVersionFor(host, port);
            var reply = await SendTrustedAsync(host, port, request, version, ct).ConfigureAwait(false);
            if (reply.ErrorMessage != null
                && TryParseRequiredXiloaderVersion(reply.ErrorMessage, out var required)
                && (required.Major, required.Minor) != (version.Major, version.Minor))
            {
                var retryVersion = (required.Major, required.Minor, (byte)0);
                GordianLog.Info(LogCategories.LsbLogin, $"Auth server {host}:{port} rejected xiloader version {version.Major}.{version.Minor}.{version.Patch} and requires {required.Major}.{required.Minor}.x; sending again as {retryVersion.Item1}.{retryVersion.Item2}.{retryVersion.Item3}.");
                version = retryVersion;
                reply = await SendTrustedAsync(host, port, request, version, ct).ConfigureAwait(false);
            }
            if (reply.ErrorMessage == null || !TryParseRequiredXiloaderVersion(reply.ErrorMessage, out _))
            {
                s_acceptedVersions[ServerKey(host, port)] = version;
            }
            return reply;
        }

        /// <summary>
        /// One command as <see cref="SendOnceAsync"/>; when the server's certificate is not trusted, asks
        /// <see cref="ConfirmServerCertificate"/> and, if the user trusts it, pins it and connects again.
        /// </summary>
        private async Task<LsbAuthReply> SendTrustedAsync(string host, int port, LsbAuthRequest request,
            (byte Major, byte Minor, byte Patch) version, CancellationToken ct)
        {
            try
            {
                return await SendOnceAsync(host, port, request, version, ct).ConfigureAwait(false);
            }
            catch (UntrustedServerCertificateException ex) when (ConfirmServerCertificate != null)
            {
                var info = ex.Certificate;
                GordianLog.Warn(LogCategories.LsbLogin, ex.Message);
                if (!await ConfirmServerCertificate(info, ct).ConfigureAwait(false)) throw;
                CertificateStore.Set(host, port, info.Sha256Fingerprint);
                GordianLog.Info(LogCategories.LsbLogin, $"Trusted the TLS certificate of {host}:{port} (SHA-256 {info.Sha256Fingerprint}).");
            }
            return await SendOnceAsync(host, port, request, version, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// One command over one TLS connection: the server's JSON reply. The TLS handshake fails with
        /// <see cref="UntrustedServerCertificateException"/>, before anything is sent, unless the server's certificate is
        /// trusted (<see cref="ServerCertificateTrust.Evaluate"/> against <see cref="CertificateStore"/>). The request is
        /// xiloader's JSON (command, username, password, new_password, otp, trust_token, trust_this_computer, version).
        /// </summary>
        private async Task<LsbAuthReply> SendOnceAsync(string host, int port, LsbAuthRequest request,
            (byte Major, byte Minor, byte Patch) version, CancellationToken ct)
        {
            GordianLog.Info(LogCategories.LsbLogin, $"Connecting to auth server {host}:{port} for user '{request.Username}' ({LsbAuthCommand.Describe(request.Command)}, xiloader version {version.Major}.{version.Minor}.{version.Patch})...");
            using var ctsAuth = CancellationTokenSource.CreateLinkedTokenSource(ct);
            ctsAuth.CancelAfter(10000); // 10 second timeout for auth server
            var authCt = ctsAuth.Token;

            using var tcpClient = new TcpClient();
            await tcpClient.ConnectAsync(host, port, authCt).ConfigureAwait(false);

            string? pinned = CertificateStore.Get(host, port);
            ServerCertificateInfo? refused = null;
            string serverKey = string.Empty;
            bool ValidateRemoteCertificate(object sender, System.Security.Cryptography.X509Certificates.X509Certificate? certificate,
                System.Security.Cryptography.X509Certificates.X509Chain? chain, SslPolicyErrors sslPolicyErrors)
            {
                if (certificate == null) return false;
                serverKey = LsbLobbyTls.KeyFingerprint(certificate);
                var decision = ServerCertificateTrust.Evaluate(sslPolicyErrors, ServerCertificateTrust.Fingerprint(certificate), pinned);
                if (decision == CertificateTrustDecision.Trusted) return true;
                refused = ServerCertificateInfo.From(host, port, certificate, sslPolicyErrors,
                    decision == CertificateTrustDecision.Changed ? pinned : null);
                return false;
            }

            using var sslStream = new SslStream(tcpClient.GetStream(), false, ValidateRemoteCertificate);
            try
            {
                await sslStream.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
                {
                    TargetHost = host,
                    EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13
                }, authCt).ConfigureAwait(false);
            }
            catch (AuthenticationException) when (refused != null)
            {
                throw new UntrustedServerCertificateException(refused);
            }

            var payload = new Dictionary<string, object>
            {
                ["command"] = request.Command,
                ["username"] = request.Username,
                ["password"] = request.Password,
                ["new_password"] = request.NewPassword,
                ["otp"] = request.Otp,
                ["trust_token"] = "",
                ["trust_this_computer"] = false,
                // LSB auth rejects any major.minor other than its SupportedXiloaderVersion (negotiated in SendAuthCommandAsync).
                ["version"] = new int[] { version.Major, version.Minor, version.Patch }
            };

            byte[] jsonBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload));
            await sslStream.WriteAsync(jsonBytes, authCt).ConfigureAwait(false);
            await sslStream.FlushAsync(authCt).ConfigureAwait(false);

            byte[] buffer = new byte[4096];
            int bytesRead = await sslStream.ReadAsync(buffer, authCt).ConfigureAwait(false);
            if (bytesRead <= 0)
            {
                throw new InvalidOperationException("LandSandBoat auth server closed the connection without responding.");
            }

            using var doc = JsonDocument.Parse(buffer.AsMemory(0, bytesRead));
            return LsbAuthReply.From(doc.RootElement.Clone(), serverKey, version);
        }

        /// <summary>
        /// <summary>Size of one character slot of the xi_view 0x20 list (lpkt_chr_info_sub2) and where the slots start.</summary>
        private const int CharacterSlotSize = 140, CharacterSlotsOffset = 32;

        /// <summary>
        /// The characters of an xi_view 0x20 list (lpkt_chr_info2: the header, a u32 count at 28, then 140-byte slots with
        /// ffxi_id at +0 and the 16-byte name at +12), skipping the free slots LandSandBoat fills with a single space.
        /// Each entry keeps its raw 1-based slot number (its position in the list, free slots included), so a character
        /// stays in "slot 3" even when slot 2 is empty.
        /// Packet structure referenced from LandSandBoat (https://github.com/LandSandBoat/server, src/login/login_packets.h)
        /// and XiPackets (https://github.com/atom0s/XiPackets, lobby/S2C_0x0020_ResponseChrInfo2.md).
        /// </summary>
        public static List<LsbCharacterSlot> ParseCharacterSlotList(ReadOnlySpan<byte> packet)
        {
            var slots = new List<LsbCharacterSlot>();
            if (packet.Length < CharacterSlotsOffset) return slots;
            int count = (int)Math.Min(16u, BinaryPrimitives.ReadUInt32LittleEndian(packet.Slice(28, 4)));
            for (int i = 0; i < count; i++)
            {
                int at = CharacterSlotsOffset + i * CharacterSlotSize;
                if (at + 28 > packet.Length) break;
                uint id = BinaryPrimitives.ReadUInt32LittleEndian(packet.Slice(at, 4));
                string name = Encoding.ASCII.GetString(packet.Slice(at + 12, 16)).TrimEnd('\0', ' ');
                if (id == 0 || string.IsNullOrWhiteSpace(name)) continue;
                slots.Add(new LsbCharacterSlot(i + 1, id, name));
            }
            return slots;
        }

        /// <summary>The characters of an xi_view 0x20 list as (id, name), free slots skipped; see <see cref="ParseCharacterSlotList"/>.</summary>
        public static List<(uint Id, string Name)> ParseCharacterSlots(ReadOnlySpan<byte> packet)
        {
            var result = new List<(uint Id, string Name)>();
            foreach (var slot in ParseCharacterSlotList(packet)) result.Add((slot.Id, slot.Name));
            return result;
        }

        /// <summary>
        /// The character to log in as, id and name from the same slot: the one named <paramref name="name"/> (any case),
        /// else the one in slot <paramref name="slot"/> (1-16, the raw list position), else the one with
        /// <paramref name="id"/>, else the first. With no slots the requested values are passed through.
        /// A <paramref name="slot"/> that is out of range or names a free slot throws instead of picking another character,
        /// because the user asked for that slot explicitly.
        /// </summary>
        public static LsbCharacterSlot ChooseCharacter(IReadOnlyList<LsbCharacterSlot> slots, string? name, uint id, int slot = 0)
        {
            if (!string.IsNullOrWhiteSpace(name))
            {
                foreach (var candidate in slots)
                {
                    if (string.Equals(candidate.Name, name.Trim(), StringComparison.OrdinalIgnoreCase)) return candidate;
                }
            }
            if (slot != 0)
            {
                foreach (var candidate in slots)
                {
                    if (candidate.Slot == slot) return candidate;
                }
                throw new InvalidOperationException(
                    $"Character slot {slot} is empty or not on this account (characters: {string.Join(", ", slots.Select(c => $"{c.Slot}={c.Name}"))}).");
            }
            if (id != 0)
            {
                foreach (var candidate in slots)
                {
                    if (candidate.Id == id) return candidate;
                }
            }
            if (slots.Count > 0)
            {
                if (!string.IsNullOrWhiteSpace(name) || id != 0)
                {
                    GordianLog.Warn(LogCategories.LsbLogin, $"Character '{name}' (ID {id}) is not on this account; logging in as '{slots[0].Name}'.");
                }
                return slots[0];
            }
            return new LsbCharacterSlot(0, id, name?.Trim() ?? string.Empty);
        }

        /// <summary>Tuple form of <see cref="ChooseCharacter(IReadOnlyList{LsbCharacterSlot}, string?, uint, int)"/> without a slot.</summary>
        public static (uint Id, string Name) ChooseCharacter(IReadOnlyList<(uint Id, string Name)> slots, string? name, uint id)
        {
            var list = new List<LsbCharacterSlot>(slots.Count);
            for (int i = 0; i < slots.Count; i++) list.Add(new LsbCharacterSlot(i + 1, slots[i].Id, slots[i].Name));
            var chosen = ChooseCharacter(list, name, id);
            return (chosen.Id, chosen.Name);
        }

        /// <summary>
        /// Connects to LandSandBoat xi_data (port 54230) and xi_view (port 54001), over TLS checked against
        /// <paramref name="lobbyServerKey"/> when it is given and plain TCP otherwise, requests character information,
        /// selects the target character, registers the Blowfish session key, and acquires the target zone map IP and UDP port.
        /// </summary>
        public async Task<MapSessionTicket> SelectCharacterAsync(
            string host,
            int dataPort,
            int viewPort,
            uint accountId,
            byte[] sessionHash,
            string? targetCharacterName = null,
            uint targetCharacterId = 0,
            byte[]? customBlowfishKey = null,
            CancellationToken ct = default,
            int targetCharacterSlot = 0,
            string? lobbyServerKey = null)
        {
            ArgumentNullException.ThrowIfNull(host);
            ArgumentNullException.ThrowIfNull(sessionHash);

            if (sessionHash.Length != 16)
            {
                throw new ArgumentException("Session hash must be exactly 16 bytes.", nameof(sessionHash));
            }

            await using var lobby = await OpenLobbyAsync(host, dataPort, viewPort, accountId, sessionHash, customBlowfishKey, ct, lobbyServerKey).ConfigureAwait(false);

            var slots = new List<LsbCharacterSlot>();
            foreach (var character in lobby.Characters)
            {
                if (!character.IsEmpty && character.ContentId != 0) slots.Add(new LsbCharacterSlot(character.Slot, character.ContentId, character.Name));
            }
            if (slots.Count == 0)
            {
                throw new InvalidOperationException("No characters found on this LandSandBoat account. Please create a character first.");
            }

            var chosen = ChooseCharacter(slots, targetCharacterName, targetCharacterId, targetCharacterSlot);
            GordianLog.Info(LogCategories.LsbLogin, $"Character choice: requested name='{targetCharacterName}' slot={targetCharacterSlot} id={targetCharacterId} -> slot {chosen.Slot} '{chosen.Name}' (ID {chosen.Id}) of {slots.Count} character(s).");
            var selected = lobby.Characters.First(c => c.Slot == chosen.Slot);
            return await lobby.SelectCharacterAsync(selected, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Opens a lobby session (the character select screen's connection) for an account that has authenticated:
        /// the data and view channels with the character list read. The caller owns and disposes it. With
        /// <paramref name="lobbyServerKey"/> (<see cref="LsbAuthentication.LobbyServerKey"/>) both channels run over TLS
        /// and must present that key; without it they are plain TCP.
        /// </summary>
        public Task<LobbySession> OpenLobbyAsync(string host, int dataPort, int viewPort, uint accountId, byte[] sessionHash,
            byte[]? customBlowfishKey = null, CancellationToken ct = default, string? lobbyServerKey = null)
        {
            Action<PacketLogEntry> log = entry => PacketInspected?.Invoke(this, entry);
            var handoff = new XiloaderLobbyHandoff(host, dataPort, accountId, sessionHash, customBlowfishKey, ClientVersion, log,
                serverKey: lobbyServerKey);
            Func<Stream, CancellationToken, Task<Stream>>? secure = lobbyServerKey == null ? null
                : (stream, token) => LsbLobbyTls.AuthenticateAsync(stream, host, viewPort, "view", lobbyServerKey, handoff.Timeout, token);
            return LobbySession.ConnectAsync(host, viewPort, handoff, log, ct: ct, secure: secure);
        }

        /// <summary>Opens the lobby for <paramref name="auth"/>, over TLS when it says so (see the overload taking the account id).</summary>
        public Task<LobbySession> OpenLobbyAsync(string host, int dataPort, int viewPort, LsbAuthentication auth, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(auth);
            return OpenLobbyAsync(host, dataPort, viewPort, auth.AccountId, auth.SessionHash, null, ct, auth.LobbyServerKey);
        }

        /// <summary>
        /// Authenticates and opens a lobby session (see <see cref="OpenLobbyAsync"/>), retrying once on a transient
        /// failure as <see cref="LoginAndSelectAsync"/> does.
        /// <paramref name="oneTimeCode"/>, when given, makes the two-factor code for each attempt (a retry must not resend
        /// a code the server already accepted, see <c>TotpCodeLedger</c>); otherwise <paramref name="otp"/> is sent.
        /// </summary>
        public async Task<LobbySession> LoginToLobbyAsync(
            string host,
            string username,
            string password,
            string otp = "",
            int connectPort = DefaultConnectPort,
            int dataPort = DefaultDataPort,
            int viewPort = DefaultViewPort,
            CancellationToken ct = default,
            LsbLobbyTransport transport = LsbLobbyTransport.Auto,
            Func<CancellationToken, Task<string>>? oneTimeCode = null)
        {
            const int maxAttempts = 2;
            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                try
                {
                    string code = oneTimeCode != null ? await oneTimeCode(ct).ConfigureAwait(false) : otp;
                    var auth = await AuthenticateAsync(host, connectPort, username, password, code, ct, transport).ConfigureAwait(false);
                    return await OpenLobbyAsync(host, dataPort, viewPort, auth, ct).ConfigureAwait(false);
                }
                catch (Exception ex) when (attempt < maxAttempts && IsTransientLobbyException(ex) && !ct.IsCancellationRequested)
                {
                    string retryMsg = $"Handshake retry: Re-authenticating with LandSandBoat (retrying in 750ms after transient issue: {ex.Message})...";
                    GordianLog.Warn(LogCategories.LsbLogin, retryMsg);
                    StatusChanged?.Invoke(this, retryMsg);
                    await Task.Delay(750, ct).ConfigureAwait(false);
                }
            }

            throw new InvalidOperationException("Failed to open the LandSandBoat lobby.");
        }

        /// <summary>
        /// Convenience overload for SelectCharacterAsync that uses DefaultViewPort.
        /// </summary>
        public Task<MapSessionTicket> SelectCharacterAsync(
            string host,
            int dataPort,
            uint accountId,
            byte[] sessionHash,
            string? targetCharacterName = null,
            uint targetCharacterId = 0,
            byte[]? customBlowfishKey = null,
            CancellationToken ct = default,
            int targetCharacterSlot = 0)
        {
            return SelectCharacterAsync(
                host,
                dataPort,
                DefaultViewPort,
                accountId,
                sessionHash,
                targetCharacterName,
                targetCharacterId,
                customBlowfishKey,
                ct,
                targetCharacterSlot);
        }

        /// <summary>
        /// Performs full LandSandBoat login and character selection pipeline in one shot.
        /// <paramref name="oneTimeCode"/>, when given, makes the two-factor code for each attempt, as in <see cref="LoginToLobbyAsync"/>.
        /// Includes automatic transient retry to gracefully handle server-side session hash collisions,
        /// lingering socket teardown, or timing races during reconnect.
        /// </summary>
        public async Task<MapSessionTicket> LoginAndSelectAsync(
            string host,
            string username,
            string password,
            string otp = "",
            int connectPort = DefaultConnectPort,
            int dataPort = DefaultDataPort,
            int viewPort = DefaultViewPort,
            string? targetCharacterName = null,
            uint targetCharacterId = 0,
            CancellationToken ct = default,
            int targetCharacterSlot = 0,
            LsbLobbyTransport transport = LsbLobbyTransport.Auto,
            Func<CancellationToken, Task<string>>? oneTimeCode = null)
        {
            const int maxAttempts = 2;
            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                try
                {
                    string code = oneTimeCode != null ? await oneTimeCode(ct).ConfigureAwait(false) : otp;
                    var auth = await AuthenticateAsync(
                        host,
                        connectPort,
                        username,
                        password,
                        code,
                        ct,
                        transport
                    ).ConfigureAwait(false);

                    return await SelectCharacterAsync(
                        host,
                        dataPort,
                        viewPort,
                        auth.AccountId,
                        auth.SessionHash,
                        targetCharacterName,
                        targetCharacterId,
                        null,
                        ct,
                        targetCharacterSlot,
                        auth.LobbyServerKey
                    ).ConfigureAwait(false);
                }
                catch (Exception ex) when (attempt < maxAttempts && IsTransientLobbyException(ex) && !ct.IsCancellationRequested)
                {
                    string retryMsg = $"Handshake retry: Re-authenticating with LandSandBoat (retrying in 750ms after transient issue: {ex.Message})...";
                    GordianLog.Warn(LogCategories.LsbLogin, retryMsg);
                    StatusChanged?.Invoke(this, retryMsg);
                    await Task.Delay(750, ct).ConfigureAwait(false);
                }
            }

            throw new InvalidOperationException("Failed to complete LandSandBoat login and character selection.");
        }

        private static bool IsTransientLobbyException(Exception ex)
        {
            if (ex is IOException or SocketException or TimeoutException or OperationCanceledException)
            {
                return true;
            }

            if (ex is InvalidOperationException invEx &&
                (invEx.Message.Contains("rejected", StringComparison.OrdinalIgnoreCase) ||
                 invEx.Message.Contains("closed the connection", StringComparison.OrdinalIgnoreCase) ||
                 invEx.Message.Contains("CHARACTER_ALREADY_LOGGED_IN", StringComparison.OrdinalIgnoreCase) ||
                 invEx.Message.Contains("Reconnecting", StringComparison.OrdinalIgnoreCase) ||
                 invEx.Message.Contains("timed out", StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            return false;
        }
    }
}
