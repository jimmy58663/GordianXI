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

namespace Gordian.Core.Network.LandSandBoat
{
    /// <summary>
    /// Holds the results of a successful LandSandBoat login and character selection handshake.
    /// </summary>
    public sealed class LsbSessionTicket
    {
        public uint AccountId { get; init; }
        public uint CharacterId { get; init; }
        public string CharacterName { get; init; } = string.Empty;
        public string ZoneIp { get; init; } = "127.0.0.1";
        public int ZonePort { get; init; } = 54230;
        public byte[] SessionHash { get; init; } = Array.Empty<byte>();

        /// <summary>
        /// The old-style (<see cref="LsbKeyStyle.Legacy"/>) map session key: the key sent in 0xA2 with LandSandBoat's
        /// old per-operation increments. The key the server really uses is one of <see cref="KeyCandidates"/>.
        /// </summary>
        public byte[] BlowfishKey { get; init; } = Array.Empty<byte>();

        /// <summary>Every map session key the server may use, newest LandSandBoat scheme first (see <see cref="LsbSessionKeys"/>).</summary>
        public IReadOnlyList<LsbSessionKeyCandidate> KeyCandidates { get; init; } = Array.Empty<LsbSessionKeyCandidate>();

        /// <summary>The lobby key counter when 0xA2 was sent (the counter key's bytes 16-19 are this + 3); 0 if never set.</summary>
        public uint LobbyKeyCounter { get; init; }

        /// <summary>The candidates for the packet crypto suite, or the single <see cref="BlowfishKey"/> when there are none.</summary>
        public IReadOnlyList<Gordian.Core.Network.Crypto.SessionKeyCandidate> CryptoKeyCandidates =>
            KeyCandidates.Count > 0
                ? KeyCandidates.Select(c => c.ToCryptoCandidate()).ToList()
                : new[] { new Gordian.Core.Network.Crypto.SessionKeyCandidate(nameof(LsbKeyStyle.Legacy), BlowfishKey) };
    }

    /// <summary>
    /// One character of the xi_view 0x20 list: its 1-based slot (raw list position, free slots counted), id and name.
    /// </summary>
    public readonly record struct LsbCharacterSlot(int Slot, uint Id, string Name);

    /// <summary>
    /// Information about an available character returned by the LandSandBoat data server.
    /// </summary>
    public sealed class LsbCharacterInfo
    {
        public uint CharacterId { get; init; }
        public uint ContentId { get; init; }
        public string CharacterName { get; init; } = string.Empty;
        public ushort CharIdMain { get; init; }
        public byte WorldId { get; init; }
        public byte CharIdExtra { get; init; }
    }

    /// <summary>
    /// Clean-room C# (.NET 10) login client for LandSandBoat private servers.
    /// Interacts directly with xi_connect (port 54231) and xi_data (port 54230) via TLS,
    /// generating the Blowfish session key and acquiring the zone map endpoint without
    /// requiring third-party bootloaders or DLL file swapping.
    /// Protocol wire specifications referenced from LandSandBoat (https://github.com/LandSandBoat/server).
    /// </summary>
    public sealed class LsbLoginClient
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
        /// Remote server certificate validation callback that accepts self-signed LandSandBoat certificates.
        /// </summary>
        private static bool ValidateRemoteCertificate(
            object sender,
            System.Security.Cryptography.X509Certificates.X509Certificate? certificate,
            System.Security.Cryptography.X509Certificates.X509Chain? chain,
            SslPolicyErrors sslPolicyErrors)
        {
            // Accept self-signed / local LandSandBoat certificates
            return true;
        }

        /// <summary>
        /// The xiloader version GordianXI reports first: what current LandSandBoat requires (2.2 since 08ae16d27d, the
        /// xi_profile transport). An older server that wants another major.minor says so and the login is retried.
        /// </summary>
        public static readonly (byte Major, byte Minor, byte Patch) DefaultXiloaderVersion = (2, 2, 0);

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
        /// Returns the account ID and 16-byte session hash. The server's version check is negotiated: GordianXI reports
        /// <see cref="DefaultXiloaderVersion"/> (or the version this server last accepted) and, when the server answers
        /// that it requires another xiloader major.minor, logs in once more reporting that version.
        /// </summary>
        public async Task<(uint AccountId, byte[] SessionHash)> AuthenticateAsync(
            string host,
            int port = DefaultConnectPort,
            string username = "",
            string password = "",
            string otp = "",
            CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(host);

            var version = XiloaderVersionFor(host, port);
            var (result, error) = await AuthenticateOnceAsync(host, port, username, password, otp, version, ct).ConfigureAwait(false);
            if (error != null
                && TryParseRequiredXiloaderVersion(error, out var required)
                && (required.Major, required.Minor) != (version.Major, version.Minor))
            {
                var retryVersion = (required.Major, required.Minor, (byte)0);
                GordianLog.Info("LSB_LOGIN", $"Auth server {host}:{port} rejected xiloader version {version.Major}.{version.Minor}.{version.Patch} and requires {required.Major}.{required.Minor}.x; logging in again as {retryVersion.Item1}.{retryVersion.Item2}.{retryVersion.Item3}.");
                version = retryVersion;
                (result, error) = await AuthenticateOnceAsync(host, port, username, password, otp, version, ct).ConfigureAwait(false);
            }
            if (error != null)
            {
                throw new InvalidOperationException($"LandSandBoat auth error: {error}");
            }

            s_acceptedVersions[ServerKey(host, port)] = version;
            GordianLog.Info("LSB_LOGIN", $"Auth server {host}:{port} accepted xiloader version {version.Major}.{version.Minor}.{version.Patch}.");
            return result;
        }

        /// <summary>One login attempt: the result, or the server's <c>error_message</c>.</summary>
        private async Task<((uint AccountId, byte[] SessionHash) Result, string? Error)> AuthenticateOnceAsync(
            string host, int port, string username, string password, string otp, (byte Major, byte Minor, byte Patch) version,
            CancellationToken ct)
        {
            GordianLog.Info("LSB_LOGIN", $"Connecting to auth server {host}:{port} for user '{username}' (xiloader version {version.Major}.{version.Minor}.{version.Patch})...");
            using var ctsAuth = CancellationTokenSource.CreateLinkedTokenSource(ct);
            ctsAuth.CancelAfter(10000); // 10 second timeout for auth server
            var authCt = ctsAuth.Token;

            using var tcpClient = new TcpClient();
            await tcpClient.ConnectAsync(host, port, authCt).ConfigureAwait(false);

            using var sslStream = new SslStream(tcpClient.GetStream(), false, ValidateRemoteCertificate);
            await sslStream.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
            {
                TargetHost = host,
                EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13
            }, authCt).ConfigureAwait(false);

            // Command 0x10 = LOGIN_ATTEMPT
            var authPayload = new Dictionary<string, object>
            {
                ["command"] = 0x10,
                ["username"] = username,
                ["password"] = password,
                ["new_password"] = "",
                ["otp"] = otp ?? string.Empty,
                ["trust_token"] = "",
                ["trust_this_computer"] = false,
                // LSB auth rejects any major.minor other than its SupportedXiloaderVersion (negotiated in AuthenticateAsync).
                ["version"] = new int[] { version.Major, version.Minor, version.Patch }
            };

            string jsonString = JsonSerializer.Serialize(authPayload);
            byte[] jsonBytes = Encoding.UTF8.GetBytes(jsonString);

            await sslStream.WriteAsync(jsonBytes, authCt).ConfigureAwait(false);
            await sslStream.FlushAsync(authCt).ConfigureAwait(false);

            byte[] buffer = new byte[4096];
            int bytesRead = await sslStream.ReadAsync(buffer, authCt).ConfigureAwait(false);
            if (bytesRead <= 0)
            {
                throw new InvalidOperationException("LandSandBoat auth server closed the connection without responding.");
            }

            string replyJson = Encoding.UTF8.GetString(buffer, 0, bytesRead);
            using var doc = JsonDocument.Parse(replyJson);
            var root = doc.RootElement;

            if (root.TryGetProperty("error_message", out var errorElem) && !string.IsNullOrWhiteSpace(errorElem.GetString()))
            {
                return (default, errorElem.GetString());
            }

            int result = root.TryGetProperty("result", out var resElem) ? resElem.GetInt32() : 0;
            if (result != 1) // 1 = LOGIN_SUCCESS
            {
                throw new InvalidOperationException($"Authentication failed with result code {result}. Check username and password.");
            }

            uint accountId = root.GetProperty("account_id").GetUInt32();
            var hashArray = root.GetProperty("session_hash");

            byte[] sessionHash = new byte[16];
            int idx = 0;
            foreach (var b in hashArray.EnumerateArray())
            {
                if (idx < 16)
                {
                    sessionHash[idx++] = b.GetByte();
                }
            }

            GordianLog.Info("LSB_LOGIN", $"Auth success! Account ID: {accountId}. Session hash acquired ({sessionHash.Length} bytes).");
            return ((accountId, sessionHash), null);
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
                    GordianLog.Warning("LSB_LOGIN", $"Character '{name}' (ID {id}) is not on this account; logging in as '{slots[0].Name}'.");
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
        /// Connects to LandSandBoat xi_data (port 54230, plain TCP) and xi_view (port 54001, plain TCP),
        /// requests character information, selects the target character, registers the Blowfish session key,
        /// and acquires the target zone map IP and UDP port.
        /// </summary>
        public async Task<LsbSessionTicket> SelectCharacterAsync(
            string host,
            int dataPort,
            int viewPort,
            uint accountId,
            byte[] sessionHash,
            string? targetCharacterName = null,
            uint targetCharacterId = 0,
            byte[]? customBlowfishKey = null,
            CancellationToken ct = default,
            int targetCharacterSlot = 0)
        {
            ArgumentNullException.ThrowIfNull(host);
            ArgumentNullException.ThrowIfNull(sessionHash);

            if (sessionHash.Length != 16)
            {
                throw new ArgumentException("Session hash must be exactly 16 bytes.", nameof(sessionHash));
            }

            await using var lobby = await OpenLobbyAsync(host, dataPort, viewPort, accountId, sessionHash, customBlowfishKey, ct).ConfigureAwait(false);

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
            GordianLog.Info("LSB_LOGIN", $"Character choice: requested name='{targetCharacterName}' slot={targetCharacterSlot} id={targetCharacterId} -> slot {chosen.Slot} '{chosen.Name}' (ID {chosen.Id}) of {slots.Count} character(s).");
            var selected = lobby.Characters.First(c => c.Slot == chosen.Slot);
            return await lobby.SelectCharacterAsync(selected, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Opens a lobby session (the character select screen's connection) for an account that has authenticated:
        /// the data and view channels with the character list read. The caller owns and disposes it.
        /// </summary>
        public Task<LsbLobbySession> OpenLobbyAsync(string host, int dataPort, int viewPort, uint accountId, byte[] sessionHash,
            byte[]? customBlowfishKey = null, CancellationToken ct = default) =>
            LsbLobbySession.ConnectAsync(host, dataPort, viewPort, accountId, sessionHash, customBlowfishKey,
                entry => PacketInspected?.Invoke(this, entry), ct: ct);

        /// <summary>
        /// Authenticates and opens a lobby session (see <see cref="OpenLobbyAsync"/>), retrying once on a transient
        /// failure as <see cref="LoginAndSelectAsync"/> does.
        /// </summary>
        public async Task<LsbLobbySession> LoginToLobbyAsync(
            string host,
            string username,
            string password,
            string otp = "",
            int connectPort = DefaultConnectPort,
            int dataPort = DefaultDataPort,
            int viewPort = DefaultViewPort,
            CancellationToken ct = default)
        {
            const int maxAttempts = 2;
            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                try
                {
                    var (accountId, sessionHash) = await AuthenticateAsync(host, connectPort, username, password, otp, ct).ConfigureAwait(false);
                    return await OpenLobbyAsync(host, dataPort, viewPort, accountId, sessionHash, null, ct).ConfigureAwait(false);
                }
                catch (Exception ex) when (attempt < maxAttempts && IsTransientLobbyException(ex) && !ct.IsCancellationRequested)
                {
                    string retryMsg = $"Handshake retry: Re-authenticating with LandSandBoat (retrying in 750ms after transient issue: {ex.Message})...";
                    GordianLog.Warning("LSB_LOGIN", retryMsg);
                    StatusChanged?.Invoke(this, retryMsg);
                    await Task.Delay(750, ct).ConfigureAwait(false);
                }
            }

            throw new InvalidOperationException("Failed to open the LandSandBoat lobby.");
        }

        /// <summary>
        /// Convenience overload for SelectCharacterAsync that uses DefaultViewPort.
        /// </summary>
        public Task<LsbSessionTicket> SelectCharacterAsync(
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
        /// Includes automatic transient retry to gracefully handle server-side session hash collisions,
        /// lingering socket teardown, or timing races during reconnect.
        /// </summary>
        public async Task<LsbSessionTicket> LoginAndSelectAsync(
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
            int targetCharacterSlot = 0)
        {
            const int maxAttempts = 2;
            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                try
                {
                    var (accountId, sessionHash) = await AuthenticateAsync(
                        host,
                        connectPort,
                        username,
                        password,
                        otp,
                        ct
                    ).ConfigureAwait(false);

                    return await SelectCharacterAsync(
                        host,
                        dataPort,
                        viewPort,
                        accountId,
                        sessionHash,
                        targetCharacterName,
                        targetCharacterId,
                        null,
                        ct,
                        targetCharacterSlot
                    ).ConfigureAwait(false);
                }
                catch (Exception ex) when (attempt < maxAttempts && IsTransientLobbyException(ex) && !ct.IsCancellationRequested)
                {
                    string retryMsg = $"Handshake retry: Re-authenticating with LandSandBoat (retrying in 750ms after transient issue: {ex.Message})...";
                    GordianLog.Warning("LSB_LOGIN", retryMsg);
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
