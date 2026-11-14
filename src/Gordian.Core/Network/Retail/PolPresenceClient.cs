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

    /// <summary>Which session key a <c>0x04/0x05</c> request asks for (spec v2.1.23 section 4.3.5).</summary>
    public enum PolSessionKeyService
    {
        /// <summary>
        /// The Viewer's key, requested as the first PML page opens. It is not the PML digest key and its use is
        /// unverified; it is asked for only to match retail traffic.
        /// </summary>
        Viewer,

        /// <summary>The FFXI lobby key, requested between the lobby connect and <c>0x26</c>.</summary>
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
    /// Protocol referenced from the GordianXI retail login spec, docs/network/retail-login-spec.md v2.1.23, section 4.3.
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

        /// <summary>
        /// The <c>0x83</c> status for a rejected Square Enix login: a wrong ID (capture #10) or a wrong password digest (live
        /// run, spec v2.1.21). No other status has been seen.
        /// </summary>
        public const byte StatusBadAccount = 0x6F;
        public const byte FrameType = 0x02;
        public const byte CategoryAccount = 0x04;
        public const byte CommandSessionKey = 0x05;
        public const byte CommandAuthenticate = 0x07;

        /// <summary>
        /// Bytes <c>+0x10..+0x19</c> of the Viewer's <c>0x04/0x05</c> key request: selector <c>00 00 00 00</c>, then
        /// <c>E8 03 05 00 00 01</c> (spec v2.1.23 section 4.3.5, test vector 18).
        /// </summary>
        public static ReadOnlySpan<byte> ViewerKeySelector => [0x00, 0x00, 0x00, 0x00, 0xE8, 0x03, 0x05, 0x00, 0x00, 0x01];

        /// <summary>
        /// Bytes <c>+0x10..+0x19</c> of the FFXI lobby key request: selector <c>00 00 00 01</c>, then
        /// <c>01 00 05 00 00 01</c> (spec v2.1.23 section 4.3.5, test vector 18). The Viewer's selector also gets a
        /// key, but the lobby refuses an AuthCode built from it with 3301.
        /// </summary>
        public static ReadOnlySpan<byte> LobbyKeySelector => [0x00, 0x00, 0x00, 0x01, 0x01, 0x00, 0x05, 0x00, 0x00, 0x01];

        /// <summary>
        /// The offset in the key request payload of the word the FFXI request sets to the session cipher's keystream, so
        /// that it is zero on the wire (spec v2.1.23 section 4.3.5).
        /// </summary>
        public const int KeyRequestWireZeroOffset = 0x20;

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

        /// <summary>
        /// The plaintext 40-byte request header (encrypted by the caller with <c>reset_iv = 1</c>): frame type, category,
        /// command, payload length, 12 zero bytes at +0x0C, and the credential digest at +0x18.
        /// </summary>
        public static void WriteRequestHeader(Span<byte> header, byte category, byte command, int payloadLength, string polId, string polPassword, uint challengeSeed)
        {
            header = header[..RequestHeaderSize];
            header.Clear();
            header[0] = FrameType;
            header[1] = category;
            header[2] = command;
            BinaryPrimitives.WriteUInt32LittleEndian(header[4..], (uint)payloadLength);
            PolHashes.ChallengeDigest(polId, polPassword, challengeSeed).CopyTo(header[0x18..]);
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
        /// The plaintext 40-byte <c>0x04/0x05</c> session key request, checksum included: 16 zero bytes, the request's
        /// selector at <c>+0x10</c>, zeros, the word at <c>+0x20</c>, and the additive checksum over <c>+0x00..+0x23</c>
        /// (spec v2.1.23 section 4.3.5, test vector 18). <paramref name="wireZeroWord"/> is the word at <c>+0x20</c>: zero
        /// for the Viewer's request, and for the FFXI request the session cipher's keystream at that position
        /// (<see cref="KeystreamWordFor"/>), so it is zero on the wire.
        /// </summary>
        public static void WriteKeyRequestPayload(Span<byte> payload, PolSessionKeyService service, ReadOnlySpan<byte> wireZeroWord = default)
        {
            payload = payload[..KeyRequestPayloadSize];
            payload.Clear();
            (service == PolSessionKeyService.Lobby ? LobbyKeySelector : ViewerKeySelector).CopyTo(payload[0x10..]);
            if (!wireZeroWord.IsEmpty) wireZeroWord[..4].CopyTo(payload[KeyRequestWireZeroOffset..]);
            PolHashes.SealAdditive(payload);
        }

        /// <summary>
        /// The four bytes the FFXI key request puts at payload <c>+0x20</c>: what <paramref name="cipher"/> turns zero
        /// bytes into at that position of a frame of <paramref name="frameLength"/> bytes (the 40-byte header, then the
        /// payload, one <c>reset_iv = 1</c> frame). Written into the plaintext, they encrypt to <c>00 00 00 00</c> (spec
        /// v2.1.23 section 4.3.5: encrypt 40 zero bytes as the payload and copy ciphertext <c>+0x20..+0x23</c>). The
        /// cipher's keystream restarts on the next frame, so this does not disturb it.
        /// </summary>
        public static byte[] KeystreamWordFor(PolStreamCipher cipher, int frameLength = RequestHeaderSize + KeyRequestPayloadSize)
        {
            byte[] zeros = new byte[frameLength];
            cipher.TransformFrame(zeros);
            return zeros.AsSpan(RequestHeaderSize + KeyRequestWireZeroOffset, 4).ToArray();
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

            string sent = mode == SquareEnixAuthMode.Password ? "without a one-time code" : "with a one-time code";
            GordianLog.Info(LogCategories.RetailLogin, $"Square Enix login: mode {(byte)mode}, {sent}, server minute {_core.Clock.Now / 60 * 60}.");

            // The redirect header announces 8 payload bytes the server never sends: read only the header (section 4.3.6).
            (PolPresenceResponse header, byte[] _) result;
            try
            {
                result = await ExchangeAsync(host, port, PolPresencePackets.CommandAuthenticate, payload,
                    expectPayload: false, "account", ct).ConfigureAwait(false);
            }
            catch (RetailLoginException ex) when (ex.Code != 0)
            {
                // 0x6F covers at least a wrong ID and a wrong password digest, and no other status has been seen: any
                // non-zero status means the ID, password or one-time code was rejected (spec v2.1.21 section 7). The
                // message says which kind of login was sent.
                throw new RetailLoginException("account",
                    $"The Square Enix login was refused (status 0x{ex.Code:X2}, sent {sent}). Check the Square Enix ID and password" +
                    (mode == SquareEnixAuthMode.Password ? ", and whether the account has an authenticator (set the profile's 2FA)." : " and the one-time code."),
                    ex.Code, ex);
            }
            var header = result.header;
            _assigned = header.RedirectAddress;
            GordianLog.Info(LogCategories.RetailLogin, $"Square Enix account accepted; assigned presence server {_assigned}.");
        }

        /// <summary>Stages 6 and 10: a 16-byte session key from the assigned server: the FFXI lobby key, or the Viewer's key.</summary>
        public async Task<byte[]> RequestSessionKeyAsync(PolSessionKeyService service, CancellationToken ct, int port = PolPresencePackets.Port)
        {
            if (_assigned == null) throw new InvalidOperationException("Authenticate first: the account login assigns the key server.");
            byte[] payload = new byte[PolPresencePackets.KeyRequestPayloadSize];
            byte[]? wireZero = service == PolSessionKeyService.Lobby ? PolPresencePackets.KeystreamWordFor(_core.Cipher) : null;
            PolPresencePackets.WriteKeyRequestPayload(payload, service, wireZero);
            GordianLog.Debug(LogCategories.RetailLogin, $"{service} key request to {_assigned}: +0x10 {Convert.ToHexString(payload.AsSpan(0x10, 10))}" +
                (wireZero == null ? "." : ", +0x20 set to the keystream so it is zero on the wire."));

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
            PolPresencePackets.WriteHello(hello, _core.RoutingToken);
            await stream.WriteAsync(hello, token).ConfigureAwait(false);

            byte[] greeting = new byte[PolPresencePackets.ChallengeSize];
            await stream.ReadExactlyAsync(greeting, token).ConfigureAwait(false);
            if (!PolPresencePackets.TryReadChallenge(greeting, out uint seed))
                throw new RetailLoginException(stage, $"{host}:{port} did not send a challenge (type 0x{greeting[0]:X2}).");

            // Header (reset_iv = 1) and payload (continuing) as one write.
            byte[] request = new byte[PolPresencePackets.RequestHeaderSize + payload.Length];
            PolPresencePackets.WriteRequestHeader(request, PolPresencePackets.CategoryAccount, command, payload.Length, _core.PolId, _core.PolPassword, seed);
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
