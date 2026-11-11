// src/Gordian.Core/Network/Retail/PolPmlClient.cs
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Gordian.Core.Network.Retail
{
    /// <summary>What the FFXI PML page said about launching: the Play button flag and the status line, when present.</summary>
    /// <param name="PlayEnabled"><c>$playbutton_enable</c>: false during maintenance, null when the page did not carry it.</param>
    /// <param name="Status"><c>$status</c>, the text the Viewer shows, or null.</param>
    public readonly record struct PmlLaunchStatus(bool? PlayEnabled, string? Status);

    /// <summary>
    /// The PlayOnline Viewer's PML pages on <c>wh000.pol.com:51304</c>, fetched the way retail fetches them between the
    /// Square Enix login and the FFXI version check: <c>/pml/main/index.pml</c> (answering its x-MD5-pol digest
    /// challenge), then the FFXI page, on one keep-alive connection. The client does not render them; the FFXI page's
    /// launch flags tell it whether the game is open (maintenance is signalled only there).
    /// <para>
    /// Protocol referenced from the GordianXI retail login spec, docs/network/retail-login-spec.md v2.1.19, sections 4.4
    /// (headers in wire order, the two-request sequence, the launch flags) and 5.7 (the digest).
    /// </para>
    /// </summary>
    public static class PolPmlClient
    {
        public const string Host = "wh000.pol.com";
        public const int Port = 51304;
        public const string MainPage = "/pml/main/index.pml";
        public const string FfxiPage = "/pml/game/ff11/index.pml?SC=0&PF=WIN";
        public const string UserAgent = "PlayOnline-PML-Viewer/1.00 [en] (Windows XP)";
        public const string Accept = "text/x-playonline-pml, image/x-playonline-ang, image/png, image/jpeg, */*";

        /// <summary>
        /// The cache date on the first, unauthenticated request. Retail sends its cached page's date; this is the value
        /// in the spec's capture (section 4.4), since GordianXI keeps no PML cache.
        /// </summary>
        public const string FirstIfModifiedSince = "Thu, 16 Sep 2014 02:19:19 GMT";

        /// <summary>The Viewer version file in the Viewer's install root: its text follows <c>"Ver."</c> in a header.</summary>
        public const string VersionFileName = "version.dat";

        private const int MaxHeaderBytes = 16 * 1024;
        private const int MaxBodyBytes = 4 * 1024 * 1024;

        /// <summary>The Viewer version in <paramref name="viewerRoot"/>'s <c>version.dat</c> (<c>"1.18.15f"</c>), or null.</summary>
        public static string? ReadViewerVersion(string? viewerRoot)
        {
            if (string.IsNullOrWhiteSpace(viewerRoot)) return null;
            string path = Path.Combine(viewerRoot, VersionFileName);
            if (!File.Exists(path)) return null;
            string text = File.ReadAllText(path, Encoding.ASCII).Trim('\0', ' ', '\r', '\n', '\t');
            return text.Length == 0 ? null : text;
        }

        /// <summary>
        /// <c>X-PlayOnline-Want-Hello</c>: <see cref="PolCoreBase64"/> of LE32(t) twice, 11 characters, for the Unix time
        /// <paramref name="unixSeconds"/> when the connection's first request is built.
        /// </summary>
        public static string WantHello(long unixSeconds)
        {
            Span<byte> value = stackalloc byte[8];
            BinaryPrimitives.WriteUInt32LittleEndian(value, (uint)unixSeconds);
            BinaryPrimitives.WriteUInt32LittleEndian(value[4..], (uint)unixSeconds);
            return PolCoreBase64.Encode(value);
        }

        /// <summary>The <c>Authorization</c> value: the fields in retail's order, no cnonce, nc or qop.</summary>
        public static string Authorization(string userName, string realm, string nonce, string uri, string response) =>
            $"Digest userName=\"{userName}\", realm=\"{realm}\", nonce=\"{nonce}\", uri=\"{uri}\", response=\"{response}\", algorithm=\"x-MD5-pol\"";

        /// <summary>A request with the headers in the order retail sends them.</summary>
        public static string BuildRequest(string uri, string viewerVersion, string wantHello, string? ifModifiedSince, string? authorization)
        {
            var sb = new StringBuilder();
            sb.Append("GET ").Append(uri).Append(" HTTP/1.1\r\n");
            sb.Append("Host: ").Append(Host).Append("\r\n");
            sb.Append("User-Agent: ").Append(UserAgent).Append("\r\n");
            sb.Append("X-POL-VIEWER-VERSION: Ver.").Append(viewerVersion).Append("\r\n");
            sb.Append("Accept: ").Append(Accept).Append("\r\n");
            sb.Append("Accept-Language: en-US\r\n");
            sb.Append("Connection: Keep-Alive\r\n");
            if (ifModifiedSince != null) sb.Append("If-Modified-Since: ").Append(ifModifiedSince).Append("\r\n");
            sb.Append("X-PlayOnline-Want-Hello: ").Append(wantHello).Append("\r\n");
            if (authorization != null) sb.Append("Authorization: ").Append(authorization).Append("\r\n");
            sb.Append("\r\n");
            return sb.ToString();
        }

        /// <summary>The quoted parameters of a <c>WWW-Authenticate: Digest ...</c> value (realm, nonce, ...), names in lower case.</summary>
        public static Dictionary<string, string> ParseChallenge(string value)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (Match m in Regex.Matches(value, "([A-Za-z]+)\\s*=\\s*\"([^\"]*)\""))
                result[m.Groups[1].Value] = m.Groups[2].Value;
            return result;
        }

        /// <summary>
        /// The launch flags of an FFXI PML page: <c>$playbutton_enable</c> ("1" or "0") and <c>$status</c>, each read as the
        /// first <c>= "value"</c> after its name. The exact <c>&lt;define&gt;</c> syntax is not in the spec, so this reads
        /// leniently and reports null for a flag it cannot find.
        /// </summary>
        public static PmlLaunchStatus ParseLaunchStatus(string page)
        {
            bool? play = null;
            var playMatch = Regex.Match(page, "\\$playbutton_enable\\b[^\\r\\n<>]{0,60}?=\\s*\"?([01])\\b");
            if (playMatch.Success) play = playMatch.Groups[1].Value == "1";
            var statusMatch = Regex.Match(page, "\\$status\\b[^\\r\\n<>]{0,60}?=\\s*\"([^\"]*)\"");
            return new PmlLaunchStatus(play, statusMatch.Success ? statusMatch.Groups[1].Value : null);
        }

        /// <summary>Fetches the two pages for a logged-in Core session (stage 7).</summary>
        public static Task<PmlLaunchStatus> VisitAsync(PolCoreSession core, string viewerVersion, CancellationToken ct,
            string host = Host, int port = Port) =>
            VisitAsync(core.RoutingToken, core.SessionKey, core.Clock.Now, viewerVersion, ct, host, port);

        /// <summary>
        /// Fetches <see cref="MainPage"/> (unauthenticated first, then again answering the digest challenge) and
        /// <see cref="FfxiPage"/> with the same <c>Authorization</c>, reconnecting only if the server closes. Returns the
        /// FFXI page's launch flags.
        /// </summary>
        internal static async Task<PmlLaunchStatus> VisitAsync(byte[] routingToken, byte[] sessionKey, long unixSeconds, string viewerVersion,
            CancellationToken ct, string host = Host, int port = Port)
        {
            string wantHello = WantHello(unixSeconds);
            string userName = PolHashes.PmlUserName(routingToken);
            var connection = await HttpConnection.OpenAsync(host, port, ct).ConfigureAwait(false);
            try
            {
                var first = await connection.SendAsync(BuildRequest(MainPage, viewerVersion, wantHello, FirstIfModifiedSince, null), ct).ConfigureAwait(false);
                Diagnostics.GordianLog.Info("RETAIL_LOGIN", $"PML {MainPage}: {first.Status} ({first.Body.Length} bytes).");
                if (first.Status != 401 || !first.Headers.TryGetValue("WWW-Authenticate", out string? challengeText))
                    throw new RetailLoginException("PlayOnline pages", $"{MainPage} answered {first.Status}, not a digest challenge.", first.Status);
                var challenge = ParseChallenge(challengeText);
                if (!challenge.TryGetValue("nonce", out string? nonce) || !challenge.TryGetValue("realm", out string? realm))
                    throw new RetailLoginException("PlayOnline pages", "The digest challenge has no realm or nonce.");

                string Authorize(string uri) => Authorization(userName, realm, nonce, uri, PolHashes.PmlDigestResponse(userName, realm, sessionKey, uri, nonce));

                if (first.CloseAfter) { connection.Dispose(); connection = await HttpConnection.OpenAsync(host, port, ct).ConfigureAwait(false); }
                var main = await connection.SendAsync(BuildRequest(MainPage, viewerVersion, wantHello, null, Authorize(MainPage)), ct).ConfigureAwait(false);
                Diagnostics.GordianLog.Info("RETAIL_LOGIN", $"PML {MainPage}: {main.Status} ({main.Body.Length} bytes).");
                if (main.Status != 200)
                    throw new RetailLoginException("PlayOnline pages", $"{MainPage} answered {main.Status} to the digest.", main.Status);

                if (main.CloseAfter) { connection.Dispose(); connection = await HttpConnection.OpenAsync(host, port, ct).ConfigureAwait(false); }
                var ffxi = await connection.SendAsync(BuildRequest(FfxiPage, viewerVersion, wantHello, null, Authorize(FfxiPage)), ct).ConfigureAwait(false);
                var status = ParseLaunchStatus(Encoding.Latin1.GetString(ffxi.Body));
                Diagnostics.GordianLog.Info("RETAIL_LOGIN",
                    $"PML {FfxiPage}: {ffxi.Status} ({ffxi.Body.Length} bytes), playbutton_enable {(status.PlayEnabled is { } p ? (p ? "1" : "0") : "not found")}.");
                if (ffxi.Status != 200)
                    throw new RetailLoginException("PlayOnline pages", $"The FFXI page answered {ffxi.Status}.", ffxi.Status);
                return status;
            }
            finally
            {
                connection.Dispose();
            }
        }

        /// <summary>An HTTP response: status, headers (case-insensitive) and body.</summary>
        internal sealed record HttpResponse(int Status, Dictionary<string, string> Headers, byte[] Body)
        {
            /// <summary>Whether the server closes the connection after this response.</summary>
            public bool CloseAfter => Headers.TryGetValue("Connection", out string? c) && c.Contains("close", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>One keep-alive HTTP/1.1 connection: writes a request and reads one response (Content-Length, chunked, or to close).</summary>
        internal sealed class HttpConnection : IDisposable
        {
            private readonly TcpClient _client;
            private readonly NetworkStream _stream;
            private readonly byte[] _buffer = new byte[8192];
            private int _start, _end;

            private HttpConnection(TcpClient client)
            {
                _client = client;
                _stream = client.GetStream();
            }

            public static async Task<HttpConnection> OpenAsync(string host, int port, CancellationToken ct)
            {
                var client = new TcpClient();
                try
                {
                    await client.ConnectAsync(host, port, ct).ConfigureAwait(false);
                    return new HttpConnection(client);
                }
                catch
                {
                    client.Dispose();
                    throw;
                }
            }

            public async Task<HttpResponse> SendAsync(string request, CancellationToken ct)
            {
                await _stream.WriteAsync(Encoding.ASCII.GetBytes(request), ct).ConfigureAwait(false);
                await _stream.FlushAsync(ct).ConfigureAwait(false);

                string statusLine = await ReadLineAsync(ct).ConfigureAwait(false) ?? throw new EndOfStreamException("The PML server closed before answering.");
                string[] parts = statusLine.Split(' ', 3);
                if (parts.Length < 2 || !parts[0].StartsWith("HTTP/", StringComparison.Ordinal) ||
                    !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out int status))
                    throw new RetailLoginException("PlayOnline pages", "The PML server's answer is not HTTP.");

                var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                int headerBytes = 0;
                while (true)
                {
                    string line = await ReadLineAsync(ct).ConfigureAwait(false) ?? throw new EndOfStreamException("The PML server closed inside its headers.");
                    if (line.Length == 0) break;
                    headerBytes += line.Length;
                    if (headerBytes > MaxHeaderBytes) throw new RetailLoginException("PlayOnline pages", "The PML server's headers are too long.");
                    int colon = line.IndexOf(':');
                    if (colon > 0) headers[line[..colon].Trim()] = line[(colon + 1)..].Trim();
                }

                byte[] body;
                if (status is 204 or 304 || status < 200) body = [];
                else if (headers.TryGetValue("Transfer-Encoding", out string? te) && te.Contains("chunked", StringComparison.OrdinalIgnoreCase))
                    body = await ReadChunkedAsync(ct).ConfigureAwait(false);
                else if (headers.TryGetValue("Content-Length", out string? cl) && int.TryParse(cl, NumberStyles.None, CultureInfo.InvariantCulture, out int length))
                    body = await ReadExactAsync(Math.Min(length, MaxBodyBytes), ct).ConfigureAwait(false);
                else body = await ReadToEndAsync(ct).ConfigureAwait(false);
                return new HttpResponse(status, headers, body);
            }

            private async Task<bool> FillAsync(CancellationToken ct)
            {
                if (_start < _end) return true;
                _start = 0;
                _end = await _stream.ReadAsync(_buffer, ct).ConfigureAwait(false);
                return _end > 0;
            }

            private async Task<string?> ReadLineAsync(CancellationToken ct)
            {
                var line = new List<byte>();
                while (await FillAsync(ct).ConfigureAwait(false))
                {
                    byte b = _buffer[_start++];
                    if (b == (byte)'\n')
                    {
                        if (line.Count > 0 && line[^1] == (byte)'\r') line.RemoveAt(line.Count - 1);
                        return Encoding.Latin1.GetString(line.ToArray());
                    }
                    line.Add(b);
                    if (line.Count > MaxHeaderBytes) throw new RetailLoginException("PlayOnline pages", "A PML header line is too long.");
                }
                return line.Count > 0 ? Encoding.Latin1.GetString(line.ToArray()) : null;
            }

            private async Task<byte[]> ReadExactAsync(int count, CancellationToken ct)
            {
                var data = new byte[count];
                int read = 0;
                while (read < count)
                {
                    if (!await FillAsync(ct).ConfigureAwait(false)) throw new EndOfStreamException("The PML server closed inside a body.");
                    int n = Math.Min(count - read, _end - _start);
                    Array.Copy(_buffer, _start, data, read, n);
                    _start += n;
                    read += n;
                }
                return data;
            }

            private async Task<byte[]> ReadChunkedAsync(CancellationToken ct)
            {
                using var body = new MemoryStream();
                while (true)
                {
                    string sizeLine = await ReadLineAsync(ct).ConfigureAwait(false) ?? throw new EndOfStreamException("The PML server closed inside a chunked body.");
                    int semicolon = sizeLine.IndexOf(';');
                    if (!int.TryParse(semicolon >= 0 ? sizeLine[..semicolon].Trim() : sizeLine.Trim(), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int size) || size < 0)
                        throw new RetailLoginException("PlayOnline pages", "A PML chunk size is not readable.");
                    if (size == 0)
                    {
                        while (!string.IsNullOrEmpty(await ReadLineAsync(ct).ConfigureAwait(false))) { } // trailers
                        return body.ToArray();
                    }
                    if (body.Length + size > MaxBodyBytes) throw new RetailLoginException("PlayOnline pages", "A PML page is too large.");
                    body.Write(await ReadExactAsync(size, ct).ConfigureAwait(false));
                    await ReadLineAsync(ct).ConfigureAwait(false); // the CRLF after the chunk
                }
            }

            private async Task<byte[]> ReadToEndAsync(CancellationToken ct)
            {
                using var body = new MemoryStream();
                while (await FillAsync(ct).ConfigureAwait(false))
                {
                    if (body.Length + (_end - _start) > MaxBodyBytes) throw new RetailLoginException("PlayOnline pages", "A PML page is too large.");
                    body.Write(_buffer, _start, _end - _start);
                    _start = _end;
                }
                return body.ToArray();
            }

            public void Dispose() => _client.Dispose();
        }
    }
}
