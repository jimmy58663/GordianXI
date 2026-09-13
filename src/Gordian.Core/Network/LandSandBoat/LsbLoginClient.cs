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
        /// Connects to xi_data (TCP TLS), sends the session hash (0xFE), requests the character list (0xA1),
        /// selects the target character (0xA2) with a client-generated 20-byte Blowfish key,
        /// and receives the target zone IP and port (0x0B).
        /// </summary>
        public async Task<LsbSessionTicket> SelectCharacterAsync(
            string host,
            int dataPort,
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

            using var tcpClient = new TcpClient();
            await tcpClient.ConnectAsync(host, dataPort, ct).ConfigureAwait(false);

            using var sslStream = new SslStream(tcpClient.GetStream(), false, ValidateRemoteCertificate);
            await sslStream.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
            {
                TargetHost = host,
                EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13
            }, ct).ConfigureAwait(false);

            // Step 1: Send 0xFE Session Hash Announcement (28 bytes)
            // Offset 0: 0xFE
            // Offset 1..11: padding
            // Offset 12..27: 16-byte session hash
            byte[] fePacket = new byte[28];
            fePacket[0] = 0xFE;
            sessionHash.CopyTo(fePacket.AsSpan(12, 16));
            await sslStream.WriteAsync(fePacket, ct).ConfigureAwait(false);
            await sslStream.FlushAsync(ct).ConfigureAwait(false);

            // Step 2: Send 0xA1 Request Character List (28 bytes)
            // Offset 0: 0xA1
            // Offset 1..4: account_id (uint32 LE)
            // Offset 5..8: server_ip (uint32 LE, 0 or search ip)
            // Offset 9..11: padding
            // Offset 12..27: 16-byte session hash
            byte[] a1Packet = new byte[28];
            a1Packet[0] = 0xA1;
            BinaryPrimitives.WriteUInt32LittleEndian(a1Packet.AsSpan(1, 4), accountId);
            sessionHash.CopyTo(a1Packet.AsSpan(12, 16));
            await sslStream.WriteAsync(a1Packet, ct).ConfigureAwait(false);
            await sslStream.FlushAsync(ct).ConfigureAwait(false);

            // Step 3: Receive 0x03 Character List (0x148 = 328 bytes)
            byte[] recvBuffer = new byte[1024];
            int bytesRead = await sslStream.ReadAsync(recvBuffer, ct).ConfigureAwait(false);
            if (bytesRead < 2 || recvBuffer[0] != 0x03)
            {
                throw new InvalidOperationException($"Unexpected response from data server: expected 0x03, received {bytesRead} bytes.");
            }

            int charCount = recvBuffer[1];
            var characters = new List<LsbCharacterInfo>();

            for (int i = 0; i < charCount; i++)
            {
                int offset = 16 * (i + 1);
                if (offset + 8 <= bytesRead)
                {
                    uint contentId = BinaryPrimitives.ReadUInt32LittleEndian(recvBuffer.AsSpan(offset, 4));
                    ushort charIdMain = BinaryPrimitives.ReadUInt16LittleEndian(recvBuffer.AsSpan(offset + 4, 2));
                    byte worldId = recvBuffer[offset + 6];
                    byte charIdExtra = recvBuffer[offset + 7];

                    // Character ID reconstructed: (charIdExtra << 16) | charIdMain
                    uint charId = ((uint)charIdExtra << 16) | charIdMain;

                    // When charId is 0, contentId is typically used as charId
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

            if (characters.Count == 0)
            {
                throw new InvalidOperationException("No characters found on this LandSandBoat account. Please create a character first.");
            }

            // Determine target character ID
            uint selectedCharId = targetCharacterId;
            if (selectedCharId == 0)
            {
                selectedCharId = characters[0].CharacterId;
            }

            // Step 4: Send 0xA2 Character Selection with 20-byte Blowfish session key
            // Offset 0: 0xA2
            // Offset 1..20: 20-byte client Blowfish key (key3)
            // Offset 21..24: Target character ID (uint32 LE)
            byte[] a2Packet = new byte[28];
            a2Packet[0] = 0xA2;
            blowfishKey.CopyTo(a2Packet.AsSpan(1, 20));
            BinaryPrimitives.WriteUInt32LittleEndian(a2Packet.AsSpan(21, 4), selectedCharId);

            await sslStream.WriteAsync(a2Packet, ct).ConfigureAwait(false);
            await sslStream.FlushAsync(ct).ConfigureAwait(false);

            // Step 5: Read Response
            string resolvedZoneIp = host;
            int resolvedZonePort = DefaultDataPort;
            string resolvedCharName = targetCharacterName ?? string.Empty;

            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(1500); // Wait up to 1.5s for 0x0B response
                int replyLen = await sslStream.ReadAsync(recvBuffer, cts.Token).ConfigureAwait(false);
                if (replyLen >= 72 && recvBuffer[8] == 0x0B)
                {
                    uint srvIp = BinaryPrimitives.ReadUInt32LittleEndian(recvBuffer.AsSpan(44, 4));
                    uint srvPort = BinaryPrimitives.ReadUInt32LittleEndian(recvBuffer.AsSpan(48, 4));

                    if (srvIp != 0)
                    {
                        var ipAddr = new IPAddress(srvIp);
                        resolvedZoneIp = ipAddr.ToString();
                    }
                    if (srvPort != 0)
                    {
                        resolvedZonePort = (int)srvPort;
                    }

                    string parsedName = Encoding.ASCII.GetString(recvBuffer, 28, 16).TrimEnd('\0', ' ');
                    if (!string.IsNullOrWhiteSpace(parsedName))
                    {
                        resolvedCharName = parsedName;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Expected when view_session is not attached
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
        /// Performs full LandSandBoat login and character selection pipeline in one shot.
        /// </summary>
        public async Task<LsbSessionTicket> LoginAndSelectAsync(
            string host,
            string username,
            string password,
            string otp = "",
            int connectPort = DefaultConnectPort,
            int dataPort = DefaultDataPort,
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
