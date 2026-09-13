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
    /// </summary>
    public sealed class LsbLoginClient
    {
        public const int DefaultConnectPort = 54231;
        public const int DefaultDataPort = 54230;
        public const int DefaultViewPort = 54001;

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

            using var tcpClient = new TcpClient();
            await tcpClient.ConnectAsync(host, port, ct).ConfigureAwait(false);

            using var sslStream = new SslStream(tcpClient.GetStream(), false, ValidateRemoteCertificate);
            await sslStream.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
            {
                TargetHost = host,
                EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13
            }, ct).ConfigureAwait(false);

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

            await sslStream.WriteAsync(jsonBytes, ct).ConfigureAwait(false);
            await sslStream.FlushAsync(ct).ConfigureAwait(false);

            byte[] buffer = new byte[4096];
            int bytesRead = await sslStream.ReadAsync(buffer, ct).ConfigureAwait(false);
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

            // Generate 20-byte Blowfish key matching LandSandBoat expectations
            byte[] blowfishKey = new byte[20];
            if (customBlowfishKey != null && customBlowfishKey.Length == 20)
            {
                customBlowfishKey.CopyTo(blowfishKey, 0);
            }
            else
            {
                RandomNumberGenerator.Fill(blowfishKey);
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

            // Read 0x05 response from view_session (40 bytes)
            byte[] viewBuffer = new byte[512];
            int viewRead = await viewStream.ReadAsync(viewBuffer, ct).ConfigureAwait(false);
            if (viewRead < 8 || viewBuffer[8] != 0x05)
            {
                // If the version lock rejected, viewBuffer[8] will be 0x04 (error)
                if (viewRead >= 34 && viewBuffer[8] == 0x04)
                {
                    ushort errCode = BinaryPrimitives.ReadUInt16LittleEndian(viewBuffer.AsSpan(32, 2));
                    throw new InvalidOperationException($"Lobby view server returned error code {errCode} during version handshake.");
                }
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

            // Step 4: Receive 0x03 Character List from xi_data (328 bytes)
            byte[] dataBuffer = new byte[1024];
            int bytesRead = await dataStream.ReadAsync(dataBuffer, ct).ConfigureAwait(false);
            if (bytesRead < 2 || dataBuffer[0] != 0x03)
            {
                throw new InvalidOperationException($"Unexpected response from data server: expected 0x03, received {bytesRead} bytes.");
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
            string? viewCharName = null;
            uint viewCharId = 0;
            try
            {
                using var cts20 = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts20.CancelAfter(2000);
                int viewBytes = await viewStream.ReadAsync(viewBuffer, cts20.Token).ConfigureAwait(false);
                if (viewBytes >= 60 && viewBuffer[8] == 0x20)
                {
                    viewCharId = BinaryPrimitives.ReadUInt32LittleEndian(viewBuffer.AsSpan(32, 4));
                    string parsedViewName = Encoding.ASCII.GetString(viewBuffer, 44, 16).TrimEnd('\0', ' ');
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

            await viewStream.WriteAsync(view07Packet, ct).ConfigureAwait(false);
            await viewStream.FlushAsync(ct).ConfigureAwait(false);

            // Step 6: Send 0xA2 Character Selection & Blowfish Key to xi_data (28 bytes)
            // Offset 0: 0xA2
            // Offset 1..20: 20-byte client Blowfish key (key3)
            // Offset 21..24: Target character ID (uint32 LE)
            byte[] a2Packet = new byte[28];
            a2Packet[0] = 0xA2;
            blowfishKey.CopyTo(a2Packet.AsSpan(1, 20));
            BinaryPrimitives.WriteUInt32LittleEndian(a2Packet.AsSpan(21, 4), selectedCharId);

            await dataStream.WriteAsync(a2Packet, ct).ConfigureAwait(false);
            await dataStream.FlushAsync(ct).ConfigureAwait(false);

            // Step 7: Receive 0x0B Response from xi_view (0x48 = 72 bytes)
            // In LandSandBoat, data_session::read_func (0xA2) writes lpkt_next_login (0x0B) to viewSession!
            string resolvedZoneIp = host;
            int resolvedZonePort = DefaultDataPort;
            string resolvedCharName = selectedCharName;

            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(3000); // Wait up to 3 seconds for 0x0B response
                int replyLen = await viewStream.ReadAsync(viewBuffer, cts.Token).ConfigureAwait(false);
                if (replyLen >= 72 && viewBuffer[8] == 0x0B)
                {
                    // lpkt_next_login:
                    // Offset 28: ffxi_id (uint32)
                    // Offset 32: ffxi_id_world (uint32)
                    // Offset 36: character_name (16 bytes)
                    // Offset 52: server_id (uint32)
                    // Offset 56: server_ip (uint32)
                    // Offset 60: server_port (uint32)
                    uint srvIp = BinaryPrimitives.ReadUInt32LittleEndian(viewBuffer.AsSpan(56, 4));
                    uint srvPort = BinaryPrimitives.ReadUInt32LittleEndian(viewBuffer.AsSpan(60, 4));

                    if (!viewBuffer.AsSpan(56, 4).SequenceEqual(stackalloc byte[4]))
                    {
                        var ipAddr = new IPAddress(viewBuffer.AsSpan(56, 4));
                        resolvedZoneIp = ipAddr.ToString();
                    }
                    if (srvPort != 0)
                    {
                        resolvedZonePort = (int)srvPort;
                    }

                    string parsedName = Encoding.ASCII.GetString(viewBuffer, 36, 16).TrimEnd('\0', ' ');
                    if (!string.IsNullOrWhiteSpace(parsedName))
                    {
                        resolvedCharName = parsedName;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Fallback to defaults if 0x0B read times out
            }

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
    }
}
