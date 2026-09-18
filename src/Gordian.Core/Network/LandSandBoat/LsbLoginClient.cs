// src/Gordian.Core/Network/LandSandBoat/LsbLoginClient.cs
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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
        public byte[] BlowfishKey { get; init; } = Array.Empty<byte>();
    }

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

        private void LogLobbyPacket(PacketDirection direction, ushort cmd, ReadOnlySpan<byte> data, string? customName = null)
        {
            if (PacketInspected == null) return;
            var entry = new PacketLogEntry
            {
                Timestamp = DateTime.UtcNow,
                Direction = direction,
                PacketId = cmd,
                PacketName = customName ?? PacketLogEntry.ResolvePacketName(cmd, direction),
                SequenceId = 0,
                Size = data.Length,
                RawBytes = data.ToArray()
            };
            PacketInspected.Invoke(this, entry);
        }

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
        /// Authenticates against xi_connect (TCP TLS) using JSON command 0x10 (LOGIN_ATTEMPT).
        /// Returns the account ID and 16-byte session hash.
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

            GordianLog.Info("LSB_LOGIN", $"Connecting to auth server {host}:{port} for user '{username}'...");
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
                ["version"] = new int[] { 2, 1, 0 }
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
                throw new InvalidOperationException($"LandSandBoat auth error: {errorElem.GetString()}");
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
            return (accountId, sessionHash);
        }

        /// <summary>
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
            CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(host);
            ArgumentNullException.ThrowIfNull(sessionHash);

            if (sessionHash.Length != 16)
            {
                throw new ArgumentException("Session hash must be exactly 16 bytes.", nameof(sessionHash));
            }

            // Standard xiloader / retail 20-byte Blowfish key: 16 null bytes followed by 58 E0 5D AD
            byte[] blowfishKey = new byte[20]
            {
                0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
                0x58, 0xE0, 0x5D, 0xAD
            };
            if (customBlowfishKey != null && customBlowfishKey.Length == 20)
            {
                customBlowfishKey.CopyTo(blowfishKey, 0);
            }

            // Step 1: Connect to xi_data (Port 54230, plain TCP, NOT TLS!)
            using var dataTcpClient = new TcpClient();
            await dataTcpClient.ConnectAsync(host, dataPort, ct).ConfigureAwait(false);
            using var dataStream = dataTcpClient.GetStream();

            // Send 0xFE Session Hash Announcement (28 bytes)
            // Offset 0: 0xFE
            // Offset 1..11: 0x00
            // Offset 12..27: 16-byte session hash
            byte[] fePacket = new byte[28];
            fePacket[0] = 0xFE;
            sessionHash.CopyTo(fePacket.AsSpan(12, 16));
            await dataStream.WriteAsync(fePacket, ct).ConfigureAwait(false);
            await dataStream.FlushAsync(ct).ConfigureAwait(false);
            LogLobbyPacket(PacketDirection.Outbound, 0xFE, fePacket, "GP_LOBBY_CONNECT");

            // Step 2: Connect to xi_view (Port 54001, plain TCP, NOT TLS!)
            using var viewTcpClient = new TcpClient();
            await viewTcpClient.ConnectAsync(host, viewPort, ct).ConfigureAwait(false);
            using var viewStream = viewTcpClient.GetStream();

            // Send 0x26 Version / Expansions request to view_session
            // Offset 0..3: packet_size = 0x80 (128 bytes)
            // Offset 4..7: "IXFF" (0x46465849)
            // Offset 8..11: command = 0x26
            // Offset 12..27: 16-byte sessionHash
            // Offset 0x74 (116): client version string, e.g. "30260904_1"
            byte[] view26Packet = new byte[128];
            BinaryPrimitives.WriteUInt32LittleEndian(view26Packet.AsSpan(0, 4), 128);
            view26Packet[4] = 0x49; // I
            view26Packet[5] = 0x58; // X
            view26Packet[6] = 0x46; // F
            view26Packet[7] = 0x46; // F
            view26Packet[8] = 0x26;
            sessionHash.CopyTo(view26Packet.AsSpan(12, 16));

            byte[] verBytes = Encoding.ASCII.GetBytes("30260904_1");
            verBytes.CopyTo(view26Packet.AsSpan(0x74, Math.Min(verBytes.Length, 10)));

            await viewStream.WriteAsync(view26Packet, ct).ConfigureAwait(false);
            await viewStream.FlushAsync(ct).ConfigureAwait(false);
            LogLobbyPacket(PacketDirection.Outbound, 0x26, view26Packet, "GP_LOBBY_VERSION_CHECK");

            // Read 0x05 response from view_session (40 bytes)
            using var cts05 = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts05.CancelAfter(5000);
            byte[] viewBuffer = new byte[512];
            int viewRead = await viewStream.ReadAsync(viewBuffer, cts05.Token).ConfigureAwait(false);
            if (viewRead > 0)
            {
                LogLobbyPacket(PacketDirection.Inbound, (ushort)(viewRead > 8 ? viewBuffer[8] : 0x05), viewBuffer.AsSpan(0, viewRead), "GP_LOBBY_VERSION_REPLY");
            }
            if (viewRead < 8 || viewBuffer[8] != 0x05)
            {
                // If the version lock rejected, viewBuffer[8] will be 0x04 (error)
                if (viewRead >= 34 && viewBuffer[8] == 0x04)
                {
                    ushort errCode = BinaryPrimitives.ReadUInt16LittleEndian(viewBuffer.AsSpan(32, 2));
                    throw new InvalidOperationException($"Lobby view server returned error code {errCode} during version handshake.");
                }
                throw new InvalidOperationException($"Lobby view server returned invalid response (received {viewRead} bytes, expected 0x05). The server may have rejected the session hash.");
            }

            // Step 3: Request Character List from xi_data (0xA1, 28 bytes)
            // Offset 0: 0xA1
            // Offset 1..4: account_id (uint32 LE)
            // Offset 5..8: server_ip (uint32 LE, 0)
            // Offset 9..11: padding
            // Offset 12..27: 16-byte session hash
            byte[] a1Packet = new byte[28];
            a1Packet[0] = 0xA1;
            BinaryPrimitives.WriteUInt32LittleEndian(a1Packet.AsSpan(1, 4), accountId);
            sessionHash.CopyTo(a1Packet.AsSpan(12, 16));
            await dataStream.WriteAsync(a1Packet, ct).ConfigureAwait(false);
            await dataStream.FlushAsync(ct).ConfigureAwait(false);
            LogLobbyPacket(PacketDirection.Outbound, 0xA1, a1Packet, "GP_LOBBY_AUTH_REQUEST");

            // Step 4: Receive 0x03 Character List from xi_data (328 bytes)
            using var cts03 = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts03.CancelAfter(5000);
            byte[] dataBuffer = new byte[1024];
            int bytesRead = await dataStream.ReadAsync(dataBuffer, cts03.Token).ConfigureAwait(false);
            if (bytesRead > 0)
            {
                LogLobbyPacket(PacketDirection.Inbound, (ushort)(bytesRead > 0 ? dataBuffer[0] : 0x03), dataBuffer.AsSpan(0, bytesRead), "GP_LOBBY_CHAR_LIST");
            }
            if (bytesRead < 2 || dataBuffer[0] != 0x03)
            {
                throw new InvalidOperationException($"Unexpected response from data server: expected 0x03, received {bytesRead} bytes. The server may have rejected the session hash.");
            }

            int charCount = dataBuffer[1];
            var characters = new List<LsbCharacterInfo>();

            for (int i = 0; i < charCount; i++)
            {
                int offset = 16 * (i + 1);
                if (offset + 8 <= bytesRead)
                {
                    uint contentId = BinaryPrimitives.ReadUInt32LittleEndian(dataBuffer.AsSpan(offset, 4));
                    ushort charIdMain = BinaryPrimitives.ReadUInt16LittleEndian(dataBuffer.AsSpan(offset + 4, 2));
                    byte worldId = dataBuffer[offset + 6];
                    byte charIdExtra = dataBuffer[offset + 7];

                    // Character ID reconstructed: (charIdExtra << 16) | charIdMain
                    uint charId = ((uint)charIdExtra << 16) | charIdMain;
                    if (charId == 0)
                    {
                        charId = contentId;
                    }

                    characters.Add(new LsbCharacterInfo
                    {
                        CharacterId = charId,
                        ContentId = contentId,
                        CharIdMain = charIdMain,
                        WorldId = worldId,
                        CharIdExtra = charIdExtra
                    });
                }
            }

            // Step 4b: Check for 0x20 Character Info response on xi_view (contains in-game character names)
            // Note: In LandSandBoat, xi_view sends lpkt_chr_info2 which is up to 2272 bytes (16 slots * 140 bytes + 32 header).
            // We must completely drain this packet from viewStream so subsequent reads (e.g. 0x0B) receive clean data.
            string? viewCharName = null;
            uint viewCharId = 0;
            try
            {
                using var cts20 = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts20.CancelAfter(3000);
                byte[] viewChrBuffer = new byte[4096];
                int viewBytes = await viewStream.ReadAsync(viewChrBuffer, cts20.Token).ConfigureAwait(false);

                if (viewBytes >= 4)
                {
                    uint totalExpected = BinaryPrimitives.ReadUInt32LittleEndian(viewChrBuffer.AsSpan(0, 4));
                    if (totalExpected > 0 && totalExpected <= 4096)
                    {
                        while (viewBytes < (int)totalExpected)
                        {
                            int chunk = await viewStream.ReadAsync(viewChrBuffer.AsMemory(viewBytes, (int)totalExpected - viewBytes), cts20.Token).ConfigureAwait(false);
                            if (chunk == 0) break;
                            viewBytes += chunk;
                        }
                    }
                }

                if (viewBytes >= 60 && viewChrBuffer[8] == 0x20)
                {
                    viewCharId = BinaryPrimitives.ReadUInt32LittleEndian(viewChrBuffer.AsSpan(32, 4));
                    string parsedViewName = Encoding.ASCII.GetString(viewChrBuffer, 44, 16).TrimEnd('\0', ' ');
                    if (!string.IsNullOrWhiteSpace(parsedViewName))
                    {
                        viewCharName = parsedViewName;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Optional 0x20 read timed out
            }

            if (characters.Count == 0 && viewCharId == 0)
            {
                throw new InvalidOperationException("No characters found on this LandSandBoat account. Please create a character first.");
            }

            // Determine target character ID
            uint selectedCharId = targetCharacterId;
            if (selectedCharId == 0)
            {
                if (viewCharId != 0)
                {
                    selectedCharId = viewCharId;
                }
                else if (characters.Count > 0)
                {
                    selectedCharId = characters[0].CharacterId;
                }
            }

            string selectedCharName = targetCharacterName ?? string.Empty;
            if (string.IsNullOrWhiteSpace(selectedCharName) && !string.IsNullOrWhiteSpace(viewCharName))
            {
                selectedCharName = viewCharName;
            }

            // Step 5: Notify xi_view of Character Selection (0x07, 64 bytes)
            // Offset 0..3: packet_size = 0x40 (64 bytes)
            // Offset 4..7: "IXFF"
            // Offset 8..11: command = 0x07
            // Offset 12..27: 16-byte sessionHash
            // Offset 28..31: character ID (uint32 LE)
            // Offset 36..51: character name (ASCII, null-terminated)
            byte[] view07Packet = new byte[64];
            BinaryPrimitives.WriteUInt32LittleEndian(view07Packet.AsSpan(0, 4), 64);
            view07Packet[4] = 0x49; // I
            view07Packet[5] = 0x58; // X
            view07Packet[6] = 0x46; // F
            view07Packet[7] = 0x46; // F
            view07Packet[8] = 0x07;
            sessionHash.CopyTo(view07Packet.AsSpan(12, 16));
            BinaryPrimitives.WriteUInt32LittleEndian(view07Packet.AsSpan(28, 4), selectedCharId);

            if (!string.IsNullOrEmpty(selectedCharName))
            {
                byte[] nameBytes = Encoding.ASCII.GetBytes(selectedCharName);
                int copyLen = Math.Min(nameBytes.Length, 15);
                nameBytes.AsSpan(0, copyLen).CopyTo(view07Packet.AsSpan(36, copyLen));
            }

            GordianLog.Debug("LSB_LOGIN", $"Notifying xi_view of character selection (0x07) for '{selectedCharName}' (ID: {selectedCharId})...");
            await viewStream.WriteAsync(view07Packet, ct).ConfigureAwait(false);
            await viewStream.FlushAsync(ct).ConfigureAwait(false);
            LogLobbyPacket(PacketDirection.Outbound, 0x07, view07Packet, "GP_LOBBY_CHAR_SELECT");

            // Step 5b: Await 5-byte confirmation (0x02) from xi_data to ensure session.requestedCharacterID is populated
            byte[] data07Confirm = new byte[16];
            using var cts07 = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts07.CancelAfter(3000);
            int confirmBytes = await dataStream.ReadAsync(data07Confirm, cts07.Token).ConfigureAwait(false);
            GordianLog.Debug("LSB_LOGIN", $"xi_data confirmation after 0x07: {confirmBytes} bytes (cmd: 0x{(confirmBytes > 0 ? data07Confirm[0] : 0):X2})");
            if (confirmBytes > 0)
            {
                LogLobbyPacket(PacketDirection.Inbound, (ushort)(confirmBytes > 0 ? data07Confirm[0] : 0x02), data07Confirm.AsSpan(0, confirmBytes), "GP_LOBBY_CHAR_CONFIRM");
            }

            // Step 6: Send 0xA2 Character Selection & Blowfish Key to xi_data (28 bytes)
            // Offset 0: 0xA2
            // Offset 1..20: 20-byte client Blowfish key (key3)
            // Offset 21..24: Target character ID (uint32 LE)
            byte[] a2Packet = new byte[28];
            a2Packet[0] = 0xA2;
            blowfishKey.CopyTo(a2Packet.AsSpan(1, 20));
            BinaryPrimitives.WriteUInt32LittleEndian(a2Packet.AsSpan(21, 4), selectedCharId);

            GordianLog.Debug("LSB_LOGIN", $"Transmitting 0xA2 character selection & Blowfish key to xi_data for char ID {selectedCharId}...");
            await dataStream.WriteAsync(a2Packet, ct).ConfigureAwait(false);
            await dataStream.FlushAsync(ct).ConfigureAwait(false);
            LogLobbyPacket(PacketDirection.Outbound, 0xA2, a2Packet, "GP_LOBBY_CHAR_SELECT_KEY");

            // Step 7: Receive 0x0B Response from xi_view (0x48 = 72 bytes)
            // In LandSandBoat, data_session::read_func (0xA2) writes lpkt_next_login (0x0B) to viewSession!
            using var cts0B = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts0B.CancelAfter(5000); // Wait up to 5 seconds for 0x0B response
            byte[] reply0BBuffer = new byte[512];
            int replyLen = 0;
            while (replyLen < 72)
            {
                int chunk = await viewStream.ReadAsync(reply0BBuffer.AsMemory(replyLen, reply0BBuffer.Length - replyLen), cts0B.Token).ConfigureAwait(false);
                if (chunk == 0) break;
                replyLen += chunk;
            }

            byte replyCmd = replyLen > 8 ? reply0BBuffer[8] : (byte)0;
            GordianLog.Debug("LSB_LOGIN", $"xi_view response to 0xA2: {replyLen} bytes (command: 0x{replyCmd:X2})");
            if (replyLen > 0)
            {
                LogLobbyPacket(PacketDirection.Inbound, replyCmd != 0 ? replyCmd : (ushort)0x0B, reply0BBuffer.AsSpan(0, replyLen), "GP_LOBBY_ZONE_TICKET");
            }

            if (replyLen < 72 || reply0BBuffer[8] != 0x0B)
            {
                if (replyLen >= 34 && reply0BBuffer[8] == 0x04)
                {
                    ushort errCode = BinaryPrimitives.ReadUInt16LittleEndian(reply0BBuffer.AsSpan(32, 2));
                    throw new InvalidOperationException($"LandSandBoat character selection failed: server returned error code {errCode} (CHARACTER_ALREADY_LOGGED_IN or server busy). Reconnecting...");
                }
                throw new InvalidOperationException($"LandSandBoat character selection failed: server returned {replyLen} bytes with code 0x{replyCmd:X2} (expected 72 bytes with 0x0B). Check LSB server console for details.");
            }

            // lpkt_next_login:
            // Offset 28: ffxi_id (uint32)
            // Offset 32: ffxi_id_world (uint32)
            // Offset 36: character_name (16 bytes)
            // Offset 52: server_id (uint32)
            // Offset 56: server_ip (uint32)
            // Offset 60: server_port (uint32)
            uint srvIp = BinaryPrimitives.ReadUInt32LittleEndian(reply0BBuffer.AsSpan(56, 4));
            uint srvPort = BinaryPrimitives.ReadUInt32LittleEndian(reply0BBuffer.AsSpan(60, 4));

            string resolvedZoneIp = host;
            if (!reply0BBuffer.AsSpan(56, 4).SequenceEqual(stackalloc byte[4]))
            {
                var ipAddr = new IPAddress(reply0BBuffer.AsSpan(56, 4));
                resolvedZoneIp = ipAddr.ToString();
            }
            int resolvedZonePort = srvPort != 0 ? (int)srvPort : DefaultDataPort;

            string resolvedCharName = selectedCharName;
            string parsedName = Encoding.ASCII.GetString(reply0BBuffer, 36, 16).TrimEnd('\0', ' ');
            if (!string.IsNullOrWhiteSpace(parsedName))
            {
                resolvedCharName = parsedName;
            }

            GordianLog.Info("LSB_LOGIN", $"Character selection SUCCESS! '{resolvedCharName}' (ID: {selectedCharId}). Target zone map server: {resolvedZoneIp}:{resolvedZonePort}");

            return new LsbSessionTicket
            {
                AccountId = accountId,
                CharacterId = selectedCharId,
                CharacterName = resolvedCharName,
                ZoneIp = resolvedZoneIp,
                ZonePort = resolvedZonePort,
                SessionHash = sessionHash,
                BlowfishKey = blowfishKey
            };
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
            CancellationToken ct = default)
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
                ct);
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
            CancellationToken ct = default)
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
                        ct
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
