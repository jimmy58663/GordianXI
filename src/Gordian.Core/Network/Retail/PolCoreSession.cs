// src/Gordian.Core/Network/Retail/PolCoreSession.cs
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Gordian.Core.Diagnostics;

namespace Gordian.Core.Network.Retail
{
    /// <summary>What the Core IRC <c>001</c> reply hands the rest of the login (spec v1.2.0 section 4.2.2, step 5).</summary>
    /// <param name="RoutingToken">6 bytes, sent in every <c>:51220</c> hello.</param>
    /// <param name="AccountToken">12 bytes, in every <c>:51220</c> request header.</param>
    /// <param name="SessionSecret">8 bytes, in the <c>:51220</c> challenge digest.</param>
    /// <param name="FfxiAccountId">The FFXI account id the lobby's 0x26 carries.</param>
    public sealed record PolCoreTicket(byte[] RoutingToken, byte[] AccountToken, byte[] SessionSecret, uint FfxiAccountId)
    {
        /// <summary>The decoded <c>001</c> payload length.</summary>
        public const int PayloadLength = 30;
    }

    /// <summary>
    /// The server's clock as the login sees it: the Unix time in the gatekeeper's redirect token plus the time elapsed
    /// since. The Square Enix password digest is bound to it (spec v1.2.0 section 5.4).
    /// </summary>
    public sealed class PolServerClock
    {
        private readonly long _serverSeconds;
        private readonly Stopwatch _since = Stopwatch.StartNew();

        public PolServerClock(long serverUnixSeconds) => _serverSeconds = serverUnixSeconds;

        /// <summary>The server's current Unix time in seconds.</summary>
        public long Now => _serverSeconds + (long)_since.Elapsed.TotalSeconds;
    }

    /// <summary>
    /// Parsers and builders for the PlayOnline Core IRC lines (gatekeeper <c>ci000.pol.com:51240</c> and the session host
    /// on <c>:51241</c>-<c>:51244</c>).
    /// <para>
    /// Protocol referenced from the GordianXI retail login spec, docs/network/retail-login-spec.md v1.2.0, sections 4.2.1
    /// and 4.2.2.
    /// </para>
    /// </summary>
    public static class PolCoreProtocol
    {
        /// <summary>The gatekeeper and its port.</summary>
        public const string GatekeeperHost = "ci000.pol.com";
        public const int GatekeeperPort = 51240;

        /// <summary>The Diffie-Hellman key exchange mode byte (256-bit).</summary>
        public const byte KeyExchangeMode = 0x02;

        /// <summary>The length of the session tag that ends the 45- and 61-character tokens.</summary>
        public const int TagLength = 4;

        /// <summary>
        /// The Base32 length of a 35-byte Diffie-Hellman token. 35 bytes need only 56 characters; the spec gives 57, so the
        /// client pads to 57 (open question in docs/network/retail-login.md).
        /// </summary>
        public const int DhTokenChars = 57;

        /// <summary>Splits a <c>:&lt;ip&gt; &lt;numeric&gt; &lt;rest&gt;</c> line; false when it is not one.</summary>
        public static bool TryParseNumeric(string line, out string source, out int numeric, out string rest)
        {
            source = rest = string.Empty;
            numeric = 0;
            if (!line.StartsWith(':')) return false;
            string[] parts = line[1..].Split(' ', 3);
            if (parts.Length < 2 || parts[1].Length != 3 || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out numeric))
                return false;
            source = parts[0];
            rest = parts.Length == 3 ? parts[2] : string.Empty;
            return true;
        }

        /// <summary>The token of a <c>:&lt;ip&gt; 300 * &lt;token&gt;</c> line, or null.</summary>
        public static string? ReadGreetingToken(string line, out string source)
        {
            if (!TryParseNumeric(line, out source, out int numeric, out string rest) || numeric != 300) return null;
            if (!rest.StartsWith("* ", StringComparison.Ordinal)) return null;
            return rest[2..].Trim();
        }

        /// <summary>
        /// The gatekeeper's redirect token (41 Base32 characters for 24 bytes, then the 4-character suffix): the server's
        /// Unix time (bytes 0-3) and the session host's port (bytes 4-5).
        /// </summary>
        public static (uint ServerTime, ushort Port, string Suffix) ParseRedirectToken(string token)
        {
            if (token.Length < 41 + TagLength) throw new FormatException("The gatekeeper token is too short.");
            byte[] data = PolBase32.Decode(token.AsSpan(0, 41), 24);
            return (BinaryPrimitives.ReadUInt32LittleEndian(data), BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(4)), token.Substring(41, TagLength));
        }

        /// <summary>
        /// The session host's Diffie-Hellman greeting (57 Base32 characters for 35 bytes, then the 4-character tag): the mode
        /// byte and the server's 32-byte public value B.
        /// </summary>
        public static (byte Mode, byte[] ServerPublic, string Tag) ParseDhToken(string token)
        {
            if (token.Length < DhTokenChars + TagLength) throw new FormatException("The Diffie-Hellman token is too short.");
            byte[] data = PolBase32.Decode(token.AsSpan(0, DhTokenChars), 35);
            return (data[0], data.AsSpan(1, 32).ToArray(), token.Substring(DhTokenChars, TagLength));
        }

        /// <summary>The client's <c>USER x 8 * :&lt;token&gt;</c> line: [0x02] + A + [0, 0] in Base32, then the session tag.</summary>
        public static string BuildUserLine(ReadOnlySpan<byte> clientPublic, string tag)
        {
            if (clientPublic.Length != PolDiffieHellman.ValueLength) throw new ArgumentException("The public value is 32 bytes.", nameof(clientPublic));
            Span<byte> data = stackalloc byte[35];
            data.Clear();
            data[0] = KeyExchangeMode;
            clientPublic.CopyTo(data[1..]);
            return $"USER x 8 * :{PolBase32.Encode(data, DhTokenChars)}{tag}\r\n";
        }

        /// <summary>The plaintext <c>NICK</c> line: the encoded POL ID, MD5 hex of the POL password, the client metadata.</summary>
        public static string BuildNickLine(string polId, string polPassword, string clientMeta) =>
            $"NICK {PolBase32.EncodePolId(polId)}:{PolHashes.PolPasswordHex(polPassword)}:{clientMeta}\r\n";

        /// <summary>Decodes the <c>001</c> line's Base32 payload into the session ticket; false when it is not a <c>001</c>.</summary>
        public static bool TryParseWelcome(string line, out PolCoreTicket? ticket)
        {
            ticket = null;
            if (!TryParseNumeric(line, out _, out int numeric, out string rest) || numeric != 1) return false;
            string payload = rest.Split(' ', StringSplitOptions.RemoveEmptyEntries) is [var first, ..] ? first.TrimStart(':') : string.Empty;
            Span<byte> data = stackalloc byte[PolCoreTicket.PayloadLength];
            if (!PolBase32.TryDecode(payload, data)) return false;
            ticket = new PolCoreTicket(data[..6].ToArray(), data.Slice(6, 12).ToArray(), data.Slice(18, 8).ToArray(),
                BinaryPrimitives.ReadUInt32LittleEndian(data[26..]));
            return true;
        }
    }

    /// <summary>
    /// A logged-in PlayOnline Core session: the open IRC connection to the session host, the stream cipher its
    /// Diffie-Hellman exchange keyed (the <c>:51220</c> connections share it), the server clock and the <c>001</c>
    /// ticket. The connection stays open for the life of the login; disposing it closes it.
    /// </summary>
    public sealed class PolCoreSession : IAsyncDisposable
    {
        private readonly TcpClient _client;

        public PolStreamCipher Cipher { get; }
        public PolServerClock Clock { get; }
        public PolCoreTicket Ticket { get; }

        private PolCoreSession(TcpClient client, PolStreamCipher cipher, PolServerClock clock, PolCoreTicket ticket)
        {
            _client = client;
            Cipher = cipher;
            Clock = clock;
            Ticket = ticket;
        }

        /// <summary>How long one stage may wait for the server.</summary>
        public static TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(15);

        /// <summary>
        /// Runs stages 2 and 3: the gatekeeper redirect, the Diffie-Hellman exchange, the encrypted <c>NICK</c> and the
        /// welcome. <paramref name="clientMeta"/> is the third <c>NICK</c> field (spec gap: its contents are not
        /// specified yet).
        /// </summary>
        public static async Task<PolCoreSession> LoginAsync(string polId, string polPassword, string clientMeta, CancellationToken ct,
            string gatekeeperHost = PolCoreProtocol.GatekeeperHost, int gatekeeperPort = PolCoreProtocol.GatekeeperPort)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(Timeout);
            var token = timeout.Token;

            // Stage 2: the gatekeeper sends one redirect line and closes.
            string redirectIp;
            uint serverTime;
            ushort port;
            using (var gate = new TcpClient())
            {
                await gate.ConnectAsync(gatekeeperHost, gatekeeperPort, token).ConfigureAwait(false);
                var reader = new LineReader(gate.GetStream());
                string line = await reader.ReadLineAsync(token).ConfigureAwait(false) ?? throw Fail("The gatekeeper closed without a redirect.");
                string redirectToken = PolCoreProtocol.ReadGreetingToken(line, out redirectIp)
                    ?? throw Fail($"Unexpected gatekeeper line: {line}");
                (serverTime, port, _) = PolCoreProtocol.ParseRedirectToken(redirectToken);
            }
            var clock = new PolServerClock(serverTime);
            GordianLog.Info("RETAIL_LOGIN", $"Gatekeeper redirect to {redirectIp}:{port} (server time {serverTime}).");

            // Stage 3: Diffie-Hellman on the session host, then the encrypted NICK.
            var client = new TcpClient();
            try
            {
                await client.ConnectAsync(redirectIp, port, token).ConfigureAwait(false);
                var stream = client.GetStream();
                var reader = new LineReader(stream);

                string greeting = await reader.ReadLineAsync(token).ConfigureAwait(false) ?? throw Fail("The session host closed before its greeting.");
                string dhToken = PolCoreProtocol.ReadGreetingToken(greeting, out _) ?? throw Fail($"Unexpected session greeting: {greeting}");
                var (mode, serverPublic, tag) = PolCoreProtocol.ParseDhToken(dhToken);
                if (mode != PolCoreProtocol.KeyExchangeMode) throw Fail($"Unsupported key exchange mode 0x{mode:X2}.");

                var dh = new PolDiffieHellman();
                await WriteAsync(stream, Encoding.ASCII.GetBytes(PolCoreProtocol.BuildUserLine(dh.PublicValue, tag)), token).ConfigureAwait(false);
                var cipher = new PolStreamCipher(PolDiffieHellman.ExtractKey(dh.ComputeSharedSecret(serverPublic)));

                string confirm = await reader.ReadLineAsync(token).ConfigureAwait(false) ?? throw Fail("The session host closed during the key exchange.");
                if (PolCoreProtocol.ReadGreetingToken(confirm, out _) == null) throw Fail($"Unexpected key exchange reply: {confirm}");

                byte[] nick = Encoding.ASCII.GetBytes(PolCoreProtocol.BuildNickLine(polId, polPassword, clientMeta));
                cipher.TransformFrame(nick);
                await WriteAsync(stream, nick, token).ConfigureAwait(false);

                PolCoreTicket? ticket = null;
                while (true)
                {
                    byte[]? raw = await reader.ReadRawLineAsync(token).ConfigureAwait(false);
                    if (raw == null)
                    {
                        if (ticket != null) break;
                        throw Fail("The session host closed before the welcome (check the POL ID and password).");
                    }
                    cipher.TransformFrame(raw);
                    string line = Encoding.ASCII.GetString(raw);
                    if (PolCoreProtocol.TryParseWelcome(line, out var parsed)) ticket = parsed;
                    else if (PolCoreProtocol.TryParseNumeric(line, out _, out int numeric, out _) && numeric >= 400 && numeric != 422)
                        throw new RetailLoginException("core session", $"The server refused the POL login ({numeric}): {line}", numeric);
                    else if (ticket != null && line.Contains(" MODE ", StringComparison.Ordinal)) break;
                }

                GordianLog.Info("RETAIL_LOGIN", $"Core session up: FFXI account id {ticket!.FfxiAccountId}.");
                return new PolCoreSession(client, cipher, clock, ticket);
            }
            catch
            {
                client.Dispose();
                throw;
            }
        }

        private static RetailLoginException Fail(string message) => new("core session", message);

        private static async Task WriteAsync(NetworkStream stream, byte[] bytes, CancellationToken ct)
        {
            await stream.WriteAsync(bytes, ct).ConfigureAwait(false);
            await stream.FlushAsync(ct).ConfigureAwait(false);
        }

        public ValueTask DisposeAsync()
        {
            _client.Dispose();
            return ValueTask.CompletedTask;
        }
    }

    /// <summary>Reads CRLF-terminated lines from a stream, as text or as raw bytes (encrypted lines keep their CRLF).</summary>
    internal sealed class LineReader
    {
        private readonly Stream _stream;
        private readonly byte[] _buffer = new byte[4096];
        private int _start, _end;

        public LineReader(Stream stream) => _stream = stream;

        /// <summary>The next line's bytes without its CRLF, or null at end of stream.</summary>
        public async Task<byte[]?> ReadRawLineAsync(CancellationToken ct)
        {
            var line = new List<byte>();
            while (true)
            {
                while (_start < _end)
                {
                    byte b = _buffer[_start++];
                    if (b == (byte)'\n')
                    {
                        if (line.Count > 0 && line[^1] == (byte)'\r') line.RemoveAt(line.Count - 1);
                        return line.ToArray();
                    }
                    line.Add(b);
                }
                _start = 0;
                _end = await _stream.ReadAsync(_buffer, ct).ConfigureAwait(false);
                if (_end == 0) return line.Count > 0 ? line.ToArray() : null;
            }
        }

        /// <summary>The next line as ASCII without its CRLF, or null at end of stream.</summary>
        public async Task<string?> ReadLineAsync(CancellationToken ct)
        {
            byte[]? raw = await ReadRawLineAsync(ct).ConfigureAwait(false);
            return raw == null ? null : Encoding.ASCII.GetString(raw);
        }
    }
}
