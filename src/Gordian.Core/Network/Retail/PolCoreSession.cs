// src/Gordian.Core/Network/Retail/PolCoreSession.cs
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Gordian.Core.Diagnostics;

namespace Gordian.Core.Network.Retail
{
    /// <summary>
    /// The server's clock as the login sees it: the Unix time in the gatekeeper's redirect token plus the time elapsed
    /// since on a monotonic clock. The Square Enix password digest is bound to it (spec v2.1.19 section 5.4).
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
    /// it names, usually on <c>:51241</c>-<c>:51247</c>).
    /// <para>
    /// Protocol referenced from the GordianXI retail login spec, docs/network/retail-login-spec.md v2.1.20, sections 4.2.1,
    /// 4.2.2 and 5.10 (the 4-character line checksum that ends every token line).
    /// </para>
    /// </summary>
    public static class PolCoreProtocol
    {
        /// <summary>The gatekeeper and its port.</summary>
        public const string GatekeeperHost = "ci000.pol.com";
        public const int GatekeeperPort = 51240;

        /// <summary>The body of a <c>USER</c> or ciphertext token: 32 bytes in 43 characters (the last ends in 2 zero bits).</summary>
        public const int KeyBodyChars = 43;

        /// <summary>The <c>USER</c> and ciphertext tokens: the 43-character body and the 4-character line checksum.</summary>
        public const int KeyTokenChars = 47;

        /// <summary>Splits a <c>:&lt;source&gt; &lt;numeric&gt; &lt;rest&gt;</c> line; false when it is not one.</summary>
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

        /// <summary>The token of a <c>:&lt;source&gt; 300 * &lt;token&gt;</c> line, or null.</summary>
        public static string? ReadGreetingToken(string line, out string source)
        {
            if (!TryParseNumeric(line, out source, out int numeric, out string rest) || numeric != 300) return null;
            if (!rest.StartsWith("* ", StringComparison.Ordinal)) return null;
            return rest[2..].Trim();
        }

        /// <summary>
        /// The client's 61-byte <c>USER x 8 * :&lt;body&gt;&lt;suffix&gt;</c> line: the modulus (little-endian) as a
        /// 43-character body, then the line checksum of the 55 bytes before it (<see cref="PolHashes.LineSuffix"/>). The
        /// server does not answer a line whose suffix is wrong (spec v2.1.20 section 4.2.2 step 2, test vector 9).
        /// </summary>
        public static string BuildUserLine(ReadOnlySpan<byte> modulus)
        {
            if (modulus.Length != PolRsaKeyExchange.ValueLength) throw new ArgumentException("The modulus is 32 bytes.", nameof(modulus));
            string head = $"USER x 8 * :{PolCoreBase64.Encode(modulus)}";
            return head + PolHashes.LineSuffix(Encoding.ASCII.GetBytes(head)) + "\r\n";
        }

        /// <summary>The modulus a <c>USER</c> token carries (what the server reads).</summary>
        public static byte[] ParseUserToken(string token) => DecodeKeyToken(token);

        /// <summary>
        /// The server's ciphertext line <c>:&lt;source&gt; 300 * &lt;body&gt;&lt;suffix&gt;</c> for a 32-byte big-endian
        /// ciphertext, without CRLF (what the server sends; the suffix covers the line from its leading colon).
        /// </summary>
        public static string BuildCiphertextLine(string source, ReadOnlySpan<byte> ciphertextBigEndian)
        {
            string head = $":{source} 300 * {PolCoreBase64.Encode(ciphertextBigEndian[..PolRsaKeyExchange.ValueLength])}";
            return head + PolHashes.LineSuffix(Encoding.ASCII.GetBytes(head));
        }

        /// <summary>
        /// The 32-byte big-endian ciphertext of the server's second <c>300 *</c> token: its 43-character body. The 4
        /// characters after it are the line checksum, often outside the alphabet, and are not decoded.
        /// </summary>
        public static byte[] ParseCiphertextToken(string token) => DecodeKeyToken(token);

        private static byte[] DecodeKeyToken(string token)
        {
            var value = new byte[PolRsaKeyExchange.ValueLength];
            if (token.Length < KeyBodyChars || !PolCoreBase64.TryDecode(token.AsSpan(0, KeyBodyChars), value))
                throw new FormatException("The key exchange token is not 32 bytes of the polcore encoding.");
            return value;
        }

        /// <summary>
        /// The <c>NICK</c> password field: MD5 hex of the greeting token's 40-character body (as text, not decoded) followed
        /// by the plaintext POL password (spec section 4.2.2 step 5, test vector 2). It changes with every connection.
        /// </summary>
        public static string PassResponse(string greetingToken, string polPassword)
        {
            if (greetingToken.Length < PolCoreToken.BodyLength) throw new ArgumentException("The greeting token is shorter than 40 characters.", nameof(greetingToken));
            byte[] input = Encoding.ASCII.GetBytes(greetingToken[..PolCoreToken.BodyLength] + (polPassword ?? string.Empty));
            return Convert.ToHexStringLower(System.Security.Cryptography.MD5.HashData(input));
        }

        /// <summary>
        /// The plaintext 86-byte <c>NICK</c> line: the encoded POL ID, the password response to this connection's greeting,
        /// the client metadata body computed from the POL ID and its checksum suffix (<see cref="PolClientMeta"/>), then
        /// CRLF; no tabs.
        /// </summary>
        public static string BuildNickLine(string polId, string polPassword, string greetingToken)
        {
            string head = $"NICK {PolIdEncoding.Encode(polId)}:{PassResponse(greetingToken, polPassword)}:{PolClientMeta.Body(polId)}";
            return head + PolClientMeta.Suffix(Encoding.ASCII.GetBytes(head)) + "\r\n";
        }

        /// <summary>The <c>PONG</c> answer to a decrypted <c>PING</c> line, or null when the line is not a ping.</summary>
        /// <summary>
        /// A line's type for diagnostics, never its contents: its numeric ("001"), its command ("MODE", "NOTICE",
        /// "PING"), or "unreadable" when it does not decrypt to printable text (a wrong session key or IV).
        /// </summary>
        public static string DescribeLine(string line)
        {
            string text = line.TrimEnd('\r', '\n');
            foreach (char c in text)
            {
                if (c < 0x20 || c > 0x7E) return $"unreadable ({text.Length} bytes)";
            }
            if (TryParseNumeric(text, out _, out int numeric, out _)) return numeric.ToString("D3", System.Globalization.CultureInfo.InvariantCulture);
            string[] words = text.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0) return "empty";
            string command = words[0].StartsWith(':') && words.Length > 1 ? words[1] : words[0];
            return command.Length <= 12 && command.All(char.IsAsciiLetterUpper) ? command : "other";
        }

        public static string? AnswerPing(string line)
        {
            string command = line;
            if (command.StartsWith(':'))
            {
                int space = command.IndexOf(' ');
                if (space < 0) return null;
                command = command[(space + 1)..];
            }
            if (command == "PING") return "PONG\r\n";
            return command.StartsWith("PING ", StringComparison.Ordinal) ? $"PONG {command[5..]}\r\n" : null;
        }
    }

    /// <summary>
    /// A logged-in PlayOnline Core session: the open IRC connection to the session host, the stream cipher the RSA
    /// exchange keyed (the <c>:51220</c> connections use it too), the 8-byte session key, what the greeting told the
    /// client (its server time and the client's address), the server clock, and the POL credentials the <c>:51220</c>
    /// digests need. The connection must stay open: the server pings it and drops the session when the pings go
    /// unanswered (spec v2.1.19 section 9, item 6), so the session answers them until disposed.
    /// </summary>
    public sealed class PolCoreSession : IAsyncDisposable
    {
        private readonly TcpClient _client;
        private readonly CancellationTokenSource _stop = new();
        private readonly Task _keepAlive;

        public PolStreamCipher Cipher { get; }
        public PolServerClock Clock { get; }

        /// <summary>The client's address as the session host saw it: LE16(port) + LE32(IPv4), from the greeting token.</summary>
        public byte[] RoutingToken { get; }

        /// <summary>The POL ID and password, kept for the <c>:51220</c> request-header digest.</summary>
        internal string PolId { get; }
        internal string PolPassword { get; }

        /// <summary>K8, the 8-byte session key: the AuthCode and the PML digest use it.</summary>
        public byte[] SessionKey { get; }

        /// <summary>The session host greeting's server time: the AuthCode's timestamp input.</summary>
        public uint GreetingTimestamp { get; }

        private PolCoreSession(TcpClient client, LineReader reader, PolStreamCipher cipher, PolServerClock clock, PolCoreToken greeting,
            byte[] sessionKey, string polId, string polPassword)
        {
            _client = client;
            Cipher = cipher;
            Clock = clock;
            RoutingToken = greeting.RoutingToken();
            PolId = polId;
            PolPassword = polPassword;
            SessionKey = sessionKey;
            GreetingTimestamp = greeting.ServerTimestamp;
            _keepAlive = Task.Run(() => AnswerPingsAsync(client.GetStream(), reader, cipher.Fork(), _stop.Token));
        }

        /// <summary>How long one stage may wait for the server.</summary>
        public static TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(15);

        /// <summary>
        /// Runs stages 2 and 3: the gatekeeper redirect, the RSA key exchange, the encrypted <c>NICK</c> and the welcome.
        /// </summary>
        public static async Task<PolCoreSession> LoginAsync(string polId, string polPassword, CancellationToken ct,
            string gatekeeperHost = PolCoreProtocol.GatekeeperHost, int gatekeeperPort = PolCoreProtocol.GatekeeperPort)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(Timeout);
            var token = timeout.Token;
            // Where the login is, and what came back after NICK (line types only, never their contents): a timeout
            // reports both, so the status line shows which step the server stopped answering at.
            var progress = new LoginProgress();
            try
            {
                return await LoginStepsAsync(polId, polPassword, gatekeeperHost, gatekeeperPort, progress, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new RetailLoginException("PlayOnline login",
                    $"No answer within {Timeout.TotalSeconds:0} s while {progress.Step}." +
                    (progress.Received is { PartialLength: > 0 } partial ? $" {partial.PartialLength} bytes arrived without a line end." : string.Empty) +
                    (progress.Replies.Count > 0 ? $" Lines received after NICK: {string.Join(", ", progress.Replies)}." : string.Empty));
            }
        }

        /// <summary>The step a login is on and the reply types seen after <c>NICK</c>, for its timeout message.</summary>
        private sealed class LoginProgress
        {
            public string Step = "connecting to the gatekeeper";
            public readonly List<string> Replies = new();

            /// <summary>The session host's reader, to tell a silent server from one whose reply has no line end.</summary>
            public LineReader? Received;

            public void Enter(string step)
            {
                Step = step;
                GordianLog.Info("RETAIL_LOGIN", $"Core session: {step}.");
            }
        }

        private static async Task<PolCoreSession> LoginStepsAsync(string polId, string polPassword, string gatekeeperHost, int gatekeeperPort,
            LoginProgress progress, CancellationToken token)
        {
            // Stage 2: the gatekeeper sends one redirect line and closes.
            string redirectIp;
            PolCoreToken redirect;
            using (var gate = new TcpClient())
            {
                progress.Enter($"connecting to the gatekeeper {gatekeeperHost}:{gatekeeperPort}");
                await gate.ConnectAsync(gatekeeperHost, gatekeeperPort, token).ConfigureAwait(false);
                progress.Enter("waiting for the gatekeeper's redirect");
                var reader = new LineReader(gate.GetStream());
                string line = await reader.ReadLineAsync(token).ConfigureAwait(false) ?? throw Fail("The gatekeeper closed without a redirect.");
                CheckSuffix("gatekeeper redirect", line);
                string redirectToken = PolCoreProtocol.ReadGreetingToken(line, out redirectIp)
                    ?? throw Fail($"Unexpected gatekeeper line: {line}");
                redirect = PolCoreToken.Parse(redirectToken);
            }
            var clock = new PolServerClock(redirect.ServerTimestamp);
            GordianLog.Info("RETAIL_LOGIN", $"Gatekeeper redirect to {redirectIp}:{redirect.RedirectPort} (server time {redirect.ServerTimestamp}).");

            // Stage 3: the greeting, the RSA exchange, then the encrypted NICK.
            var client = new TcpClient();
            try
            {
                progress.Enter($"connecting to the session host {redirectIp}:{redirect.RedirectPort}");
                await client.ConnectAsync(redirectIp, redirect.RedirectPort, token).ConfigureAwait(false);
                var stream = client.GetStream();
                var reader = new LineReader(stream);
                progress.Enter("waiting for the session host's greeting");

                string greeting = await reader.ReadLineAsync(token).ConfigureAwait(false) ?? throw Fail("The session host closed before its greeting.");
                CheckSuffix("greeting", greeting);
                string greetingToken = PolCoreProtocol.ReadGreetingToken(greeting, out _) ?? throw Fail($"Unexpected session greeting: {greeting}");
                var greetingFields = PolCoreToken.Parse(greetingToken);

                progress.Received = reader;
                GordianLog.Info("RETAIL_LOGIN", $"Core session: greeting of {greeting.Length} characters.");

                var rsa = new PolRsaKeyExchange();
                // The USER line carries only this connection's throwaway public key, so it is logged in full.
                string userLine = PolCoreProtocol.BuildUserLine(rsa.Modulus);
                GordianLog.Info("RETAIL_LOGIN", $"Core session: sending {userLine.TrimEnd()}");
                await WriteAsync(stream, Encoding.ASCII.GetBytes(userLine), token).ConfigureAwait(false);
                progress.Enter("waiting for the key exchange reply to USER");

                string keyLine = await reader.ReadLineAsync(token).ConfigureAwait(false) ?? throw Fail("The session host closed during the key exchange.");
                GordianLog.Info("RETAIL_LOGIN", "Core session: key exchange reply received.");
                CheckSuffix("key exchange reply", keyLine);
                string keyToken = PolCoreProtocol.ReadGreetingToken(keyLine, out _) ?? throw Fail($"Unexpected key exchange reply: {keyLine}");
                byte[] sessionKey;
                try
                {
                    sessionKey = rsa.DecryptSessionKey(PolCoreProtocol.ParseCiphertextToken(keyToken));
                }
                catch (Exception ex) when (ex is FormatException or System.Security.Cryptography.CryptographicException)
                {
                    throw Fail($"The key exchange failed: {ex.Message}");
                }
                var cipher = new PolStreamCipher(sessionKey, rsa.Modulus.AsSpan(0, 8));

                byte[] nick = Encoding.ASCII.GetBytes(PolCoreProtocol.BuildNickLine(polId, polPassword, greetingToken));
                cipher.TransformFrame(nick);
                await WriteAsync(stream, nick, token).ConfigureAwait(false);
                progress.Enter("waiting for the welcome after NICK (001, then MODE)");

                // The welcome: 300, 001 (carries nothing the client uses), 422, MODE, then presence NOTICEs (section 4.2.2 step 6).
                bool welcomed = false;
                while (true)
                {
                    byte[]? raw = await reader.ReadRawLineAsync(token).ConfigureAwait(false);
                    if (raw == null) throw Fail("The session host closed before the welcome (check the POL ID and password).");
                    cipher.TransformFrame(raw);
                    string line = Encoding.ASCII.GetString(raw);
                    string kind = PolCoreProtocol.DescribeLine(line);
                    progress.Replies.Add(kind);
                    GordianLog.Info("RETAIL_LOGIN", $"Core session: received {kind} after NICK.");
                    if (PolCoreProtocol.TryParseNumeric(line, out _, out int numeric, out _))
                    {
                        if (numeric == 1) welcomed = true;
                        else if (numeric >= 400 && numeric != 422)
                            throw new RetailLoginException("core session", $"The server refused the POL login ({numeric}): {line}", numeric);
                    }
                    else if (welcomed && line.Contains(" MODE ", StringComparison.Ordinal)) break;
                }

                GordianLog.Info("RETAIL_LOGIN", "Core session up.");
                return new PolCoreSession(client, reader, cipher, clock, greetingFields, sessionKey, polId, polPassword);
            }
            catch
            {
                client.Dispose();
                throw;
            }
        }

        /// <summary>Reads the idle connection and answers each <c>PING</c> with a <c>PONG</c>, every line its own frame.</summary>
        private static async Task AnswerPingsAsync(NetworkStream stream, LineReader reader, PolStreamCipher cipher, CancellationToken ct)
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    byte[]? raw = await reader.ReadRawLineAsync(ct).ConfigureAwait(false);
                    if (raw == null)
                    {
                        GordianLog.Warn("RETAIL_LOGIN", "The Core session host closed the connection.");
                        return;
                    }
                    cipher.TransformFrame(raw);
                    string? pong = PolCoreProtocol.AnswerPing(Encoding.ASCII.GetString(raw));
                    if (pong == null) continue;
                    byte[] reply = Encoding.ASCII.GetBytes(pong);
                    cipher.TransformFrame(reply);
                    await WriteAsync(stream, reply, ct).ConfigureAwait(false);
                }
            }
            catch (Exception ex) when (ex is OperationCanceledException or IOException or ObjectDisposedException)
            {
                // Disposed, or the connection dropped.
            }
        }

        private static RetailLoginException Fail(string message) => new("core session", message);

        /// <summary>Logs a server token line whose line checksum does not match (spec section 5.10); nothing depends on it.</summary>
        private static void CheckSuffix(string what, string line)
        {
            if (!PolHashes.HasValidLineSuffix(line))
                GordianLog.Warning("RETAIL_LOGIN", $"Core session: the {what} line's checksum suffix does not match.");
        }

        private static async Task WriteAsync(NetworkStream stream, byte[] bytes, CancellationToken ct)
        {
            await stream.WriteAsync(bytes, ct).ConfigureAwait(false);
            await stream.FlushAsync(ct).ConfigureAwait(false);
        }

        public async ValueTask DisposeAsync()
        {
            _stop.Cancel();
            _client.Dispose();
            try { await _keepAlive.ConfigureAwait(false); } catch { }
            _stop.Dispose();
        }
    }

    /// <summary>Reads CRLF-terminated lines from a stream, as text or as raw bytes (encrypted lines keep their CRLF).</summary>
    internal sealed class LineReader
    {
        private readonly Stream _stream;
        private readonly byte[] _buffer = new byte[4096];
        private int _start, _end;

        public LineReader(Stream stream) => _stream = stream;

        /// <summary>The bytes of the line being read that have arrived so far (non-zero while a reply lacks its line end).</summary>
        public int PartialLength { get; private set; }

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
                        PartialLength = 0;
                        if (line.Count > 0 && line[^1] == (byte)'\r') line.RemoveAt(line.Count - 1);
                        return line.ToArray();
                    }
                    line.Add(b);
                }
                PartialLength = line.Count;
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
