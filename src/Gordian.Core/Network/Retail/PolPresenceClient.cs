// src/Gordian.Core/Network/Retail/PolPresenceClient.cs
using System;
using System.Buffers.Binary;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Gordian.Core.Diagnostics;

namespace Gordian.Core.Network.Retail
{
    /// <summary>How a <c>0x04/0x07</c> login proves the Square Enix account.</summary>
    public enum SquareEnixAuthMode : byte
    {
        /// <summary>No Square Enix account.</summary>
        None = 0,

        /// <summary>Square Enix ID and password.</summary>
        Password = 1,

        /// <summary>Square Enix ID, password and a 6-digit one-time password.</summary>
        PasswordAndOneTimeCode = 2,
    }

    /// <summary>Which session key a <c>0x04/0x05</c> request asks for.</summary>
    public enum PolSessionKeyService
    {
        /// <summary>
        /// The Viewer's service-1 key (ServiceType 1, flags 1000), requested around the PML pages. It is not the PML
        /// digest key and its use is unverified; it is asked for only to match retail traffic.
        /// </summary>
        Viewer,

        /// <summary>The FFXI lobby key: ServiceType 0, flags 1.</summary>
        Lobby,
    }

    /// <summary>A decrypted <c>0x83</c> response header.</summary>
    /// <param name="Status">0 for success; anything else is an error code.</param>
    /// <param name="PayloadLength">How many encrypted payload bytes follow.</param>
    /// <param name="Word8">+0x08: on the account login, the assigned <c>:51220</c> server's IPv4 address.</param>
    public readonly record struct PolPresenceResponse(byte Type, byte Status, uint PayloadLength, uint Word8)
    {
        /// <summary>The assigned server's address: the wire bytes are last octet first, so <see cref="Word8"/> read little-endian gives the dotted form most significant byte first.</summary>
        public IPAddress RedirectAddress => new(new[] { (byte)(Word8 >> 24), (byte)(Word8 >> 16), (byte)(Word8 >> 8), (byte)Word8 });
    }

    /// <summary>
    /// Builders and parsers for the PlayOnline presence / account servers on <c>:51220</c>: the plaintext hello and
    /// challenge, the encrypted request header and payloads, and the encrypted response header.
    /// <para>
    /// Protocol referenced from the GordianXI retail login spec, docs/network/retail-login-spec.md v2.1.18, section 4.3.
    /// </para>
    /// </summary>
    public static class PolPresencePackets
    {
        public const string AccountHost = "pp000.pol.com";
        public const int Port = 51220;

        public const int HelloSize = 40;
        public const int ChallengeSize = 24;
        public const int RequestHeaderSize = 40;
        public const int ResponseHeaderSize = 24;
        public const int AuthPayloadSize = 64;
        public const int KeyRequestPayloadSize = 40;
        public const int KeyResponsePayloadSize = 32;

        public const byte ChallengeType = 0x81;
        public const byte ResponseType = 0x83;
        public const byte FrameType = 0x02;
        public const byte CategoryAccount = 0x04;
        public const byte CommandSessionKey = 0x05;
        public const byte CommandAuthenticate = 0x07;

        /// <summary>The <c>0x04/0x05</c> flag of the Viewer's service-1 request.</summary>
        public const ushort KeyFlagsViewer = 1000;

        /// <summary>The <c>0x04/0x05</c> flag that asks for the FFXI lobby key.</summary>
        public const ushort KeyFlagsLobby = 1;

        /// <summary>The <c>0x04/0x05</c> ServiceType of the Viewer's service-1 request (spec v2.1.18 section 4.3.5).</summary>
        public const byte ServiceTypeViewer = 1;

        /// <summary>The <c>0x04/0x05</c> ServiceType of the lobby key request (spec v2.1.18 section 4.3.5).</summary>
        public const byte ServiceTypeLobby = 0;

        /// <summary>
        /// The 40-byte hello: zeros, the protocol version 1 (u16 LE at +0x04), the routing token at +0x06, and zeros from
        /// +0x0C (the server does not read +0x0C..+0x17).
        /// </summary>
        public static void WriteHello(Span<byte> hello, ReadOnlySpan<byte> routingToken)
        {
            hello = hello[..HelloSize];
            hello.Clear();
            BinaryPrimitives.WriteUInt16LittleEndian(hello[4..], 0x0001);
            routingToken[..6].CopyTo(hello[6..]);
        }

        /// <summary>The challenge seed of a 24-byte <c>0x81</c> greeting; false when it is not one.</summary>
        public static bool TryReadChallenge(ReadOnlySpan<byte> greeting, out uint seed)
        {
            seed = 0;
            if (greeting.Length < ChallengeSize || greeting[0] != ChallengeType) return false;
            seed = BinaryPrimitives.ReadUInt32LittleEndian(greeting[0x14..]);
            return true;
        }

        /// <summary>The plaintext 40-byte request header (encrypted by the caller with <c>reset_iv = 1</c>).</summary>
        public static void WriteRequestHeader(Span<byte> header, byte category, byte command, int payloadLength, PolCoreTicket ticket, uint challengeSeed)
        {
            header = header[..RequestHeaderSize];
            header.Clear();
            header[0] = FrameType;
            header[1] = category;
            header[2] = command;
            BinaryPrimitives.WriteUInt32LittleEndian(header[4..], (uint)payloadLength);
            ticket.AccountToken.CopyTo(header[0x0C..]);
            PolHashes.ChallengeDigest(ticket.SessionSecret, ticket.AccountToken, challengeSeed).CopyTo(header[0x18..]);
        }

        /// <summary>The plaintext 64-byte <c>0x04/0x07</c> Square Enix account payload, checksum included.</summary>
        public static void WriteAuthPayload(Span<byte> payload, SquareEnixAuthMode mode, string squareEnixId, string password, string oneTimeCode, long serverUnixSeconds)
        {
            payload = payload[..AuthPayloadSize];
            payload.Clear();
            payload[0] = (byte)mode;
            PolpPackets.WriteAscii(payload.Slice(0x01, 17), squareEnixId);
            if (mode == SquareEnixAuthMode.PasswordAndOneTimeCode) PolpPackets.WriteAscii(payload.Slice(0x12, 7), oneTimeCode);
            PolHashes.SquareEnixPasswordDigest(password, serverUnixSeconds).CopyTo(payload[0x20..]);
            PolHashes.SealAdditive(payload);
        }

        /// <summary>
        /// The plaintext 40-byte <c>0x04/0x05</c> session key request, checksum included. Retail sends ClientNonce 0 on
        /// most requests and the server accepts it (spec v2.1.18 section 4.3.5).
        /// </summary>
        public static void WriteKeyRequestPayload(Span<byte> payload, PolSessionKeyService service, uint clientNonce = 0)
        {
            payload = payload[..KeyRequestPayloadSize];
            payload.Clear();
            var (serviceType, flags) = service == PolSessionKeyService.Lobby ? (ServiceTypeLobby, KeyFlagsLobby) : (ServiceTypeViewer, KeyFlagsViewer);
            payload[0x13] = serviceType;
            BinaryPrimitives.WriteUInt16BigEndian(payload[0x14..], 0x0001);
            BinaryPrimitives.WriteUInt16BigEndian(payload[0x16..], 0x0005);
            BinaryPrimitives.WriteUInt16BigEndian(payload[0x18..], flags);
            BinaryPrimitives.WriteUInt32LittleEndian(payload[0x20..], clientNonce);
            PolHashes.SealAdditive(payload);
        }

        /// <summary>Reads a decrypted 24-byte response header.</summary>
        public static PolPresenceResponse ReadResponseHeader(ReadOnlySpan<byte> header) => new(
            header[0], header[1], BinaryPrimitives.ReadUInt32LittleEndian(header[4..]), BinaryPrimitives.ReadUInt32LittleEndian(header[8..]));
    }

    /// <summary>
    /// One login's conversations with the <c>:51220</c> servers: the Square Enix account login on
    /// <c>pp000.pol.com</c> (stage 4), then session key requests on the server it assigns (stages 5, 6 and 10). Every
    /// connection is one hello, one challenge, one request and one response, encrypted with the Core session's cipher.
    /// </summary>
    public sealed class PolPresenceClient
    {
        private readonly PolCoreSession _core;
        private IPAddress? _assigned;

        public PolPresenceClient(PolCoreSession core) => _core = core;

        /// <summary>The <c>:51220</c> server the account login assigned, once it has run.</summary>
        public IPAddress? AssignedServer => _assigned;

        /// <summary>How long one exchange may take.</summary>
        public static TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(15);

        /// <summary>Stage 4: logs the Square Enix account in and remembers the server it assigns.</summary>
        public async Task AuthenticateAsync(string squareEnixId, string password, string oneTimeCode, CancellationToken ct,
            string host = PolPresencePackets.AccountHost, int port = PolPresencePackets.Port)
        {
            var mode = string.IsNullOrEmpty(oneTimeCode) ? SquareEnixAuthMode.Password : SquareEnixAuthMode.PasswordAndOneTimeCode;
            byte[] payload = new byte[PolPresencePackets.AuthPayloadSize];
            PolPresencePackets.WriteAuthPayload(payload, mode, squareEnixId, password, oneTimeCode, _core.Clock.Now);

            // The redirect header announces 8 payload bytes the server never sends: read only the header (section 4.3.6).
            var (header, _) = await ExchangeAsync(host, port, PolPresencePackets.CommandAuthenticate, payload,
                expectPayload: false, "account", ct).ConfigureAwait(false);
            _assigned = header.RedirectAddress;
            GordianLog.Info("RETAIL_LOGIN", $"Square Enix account accepted; assigned presence server {_assigned}.");
        }

        /// <summary>Stages 6 and 10: a 16-byte session key from the assigned server: the FFXI lobby key, or the Viewer's service-1 key.</summary>
        public async Task<byte[]> RequestSessionKeyAsync(PolSessionKeyService service, CancellationToken ct, int port = PolPresencePackets.Port)
        {
            if (_assigned == null) throw new InvalidOperationException("Authenticate first: the account login assigns the key server.");
            byte[] payload = new byte[PolPresencePackets.KeyRequestPayloadSize];
            PolPresencePackets.WriteKeyRequestPayload(payload, service);

            var (_, response) = await ExchangeAsync(_assigned.ToString(), port, PolPresencePackets.CommandSessionKey, payload,
                expectPayload: true, "session key", ct).ConfigureAwait(false);
            if (response.Length < PolPresencePackets.KeyResponsePayloadSize || !PolHashes.VerifyAdditive(response.AsSpan(0, PolPresencePackets.KeyResponsePayloadSize)))
                throw new RetailLoginException("session key", "The session key reply failed its checksum.");
            return response.AsSpan(0, 16).ToArray();
        }

        private async Task<(PolPresenceResponse Header, byte[] Payload)> ExchangeAsync(string host, int port, byte command, byte[] payload, bool expectPayload, string stage, CancellationToken ct)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(Timeout);
            var token = timeout.Token;

            using var client = new TcpClient();
            await client.ConnectAsync(host, port, token).ConfigureAwait(false);
            var stream = client.GetStream();

            byte[] hello = new byte[PolPresencePackets.HelloSize];
            PolPresencePackets.WriteHello(hello, _core.Ticket.RoutingToken);
            await stream.WriteAsync(hello, token).ConfigureAwait(false);

            byte[] greeting = new byte[PolPresencePackets.ChallengeSize];
            await stream.ReadExactlyAsync(greeting, token).ConfigureAwait(false);
            if (!PolPresencePackets.TryReadChallenge(greeting, out uint seed))
                throw new RetailLoginException(stage, $"{host}:{port} did not send a challenge (type 0x{greeting[0]:X2}).");

            // Header (reset_iv = 1) and payload (continuing) as one write.
            byte[] request = new byte[PolPresencePackets.RequestHeaderSize + payload.Length];
            PolPresencePackets.WriteRequestHeader(request, PolPresencePackets.CategoryAccount, command, payload.Length, _core.Ticket, seed);
            payload.CopyTo(request, PolPresencePackets.RequestHeaderSize);
            _core.Cipher.TransformFrame(request);
            await stream.WriteAsync(request, token).ConfigureAwait(false);

            byte[] rawHeader = new byte[PolPresencePackets.ResponseHeaderSize];
            await stream.ReadExactlyAsync(rawHeader, token).ConfigureAwait(false);
            byte[] plainHeader = (byte[])rawHeader.Clone();
            _core.Cipher.TransformFrame(plainHeader);
            var header = PolPresencePackets.ReadResponseHeader(plainHeader);
            if (header.Type != PolPresencePackets.ResponseType)
                throw new RetailLoginException(stage, $"Unexpected response type 0x{header.Type:X2}.");
            if (header.Status != 0)
                throw new RetailLoginException(stage, $"The server refused the request (status 0x{header.Status:X2}).", header.Status);

            byte[] responsePayload = Array.Empty<byte>();
            if (expectPayload && header.PayloadLength is > 0 and <= 4096)
            {
                responsePayload = new byte[header.PayloadLength];
                await stream.ReadExactlyAsync(responsePayload, token).ConfigureAwait(false);
                _core.Cipher.Transform(responsePayload);
            }
            return (header, responsePayload);
        }
    }
}
