// src/Gordian.Core/Network/LandSandBoat/XiloaderLobbyHandoff.cs
using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Gordian.Core.Diagnostics;
using Gordian.Core.Network.Lobby;
using Gordian.Core.Network.Retail;

namespace Gordian.Core.Network.LandSandBoat
{
    /// <summary>
    /// The xiloader side of a LandSandBoat lobby: what xiloader does for the legacy client in place of pol.exe. It writes
    /// the auth server's session hash into the header's +0x0C of every lobby request (xiloader's <c>send</c> hook), and
    /// answers LandSandBoat's data channel (xi_data, 54230): 0xFE with the session hash at connect, 0xA1 (account id)
    /// when the server prompts 0x01, 0xA2 (the session key) when it prompts 0x02. LandSandBoat ignores the AuthCode, the
    /// body hashes and the selection digest the client sends.
    /// <para>
    /// Flows (XiPackets lobby/Protocol.md; LandSandBoat src/login/view_session.cpp and data_session.cpp; xiloader
    /// src/network.cpp): a character list = view 0x1F -> data 0x01, data 0xA1 -> data 0x03 + view 0x20; select = view
    /// 0x07 -> data 0x02, data 0xA2 -> view 0x0B (the server then closes the view channel).
    /// </para>
    /// <para>
    /// <b>Session key accounting (LandSandBoat <c>data_session.cpp</c> / <c>view_session.cpp</c>).</b> Which key the map
    /// server decrypts with depends on the LandSandBoat version, so the ticket carries every candidate
    /// (<see cref="LsbSessionKeys.BuildCandidates"/>) and the map session picks the one the server's first datagram
    /// verifies with. Current LandSandBoat (since 393e176732) stores MD5(session hash) and the lobby key counter + 3,
    /// the counter following every view request and reply (<see cref="LobbyKeyCounter"/>). Older servers store the key
    /// the client sends in 0xA2 with byte 16 raised by 4 per deletion and per rename, 1 per refused "already logged
    /// in" selection and 6 when a character was created in this session (<see cref="KeyIncrement"/>).
    /// </para>
    /// </summary>
    public sealed class XiloaderLobbyHandoff : ILobbyHandoff
    {
        /// <summary>The client version sent in 0x26. LandSandBoat compares its first 6 characters with <c>login.CLIENT_VER</c>.</summary>
        public const string DefaultClientVersion = "30260904_1";

        /// <summary>The standard xiloader / retail base session key: 16 zero bytes, then 58 E0 5D AD.</summary>
        public static byte[] CreateBaseKey() => new byte[20] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0x58, 0xE0, 0x5D, 0xAD };

        /// <summary>How long a character list waits for the account prompt before sending its request again.</summary>
        public static readonly TimeSpan PromptRetryWait = TimeSpan.FromSeconds(1.5);

        /// <summary>A pause after a deletion's OK: LandSandBoat writes it before its database work (view_session.cpp 0x14).</summary>
        public static readonly TimeSpan AfterDeletePause = TimeSpan.FromMilliseconds(250);

        private readonly string _host;
        private readonly int _dataPort;
        private readonly uint _accountId;
        private readonly byte[] _sessionHash;
        private readonly byte[] _baseKey;
        private readonly Action<PacketLogEntry>? _log;
        private readonly TcpClient _dataClient = new();
        private readonly LobbyKeyCounter _keyCounter = new();
        private NetworkStream? _data;
        private readonly System.Threading.Channels.Channel<byte[]> _messages = System.Threading.Channels.Channel.CreateUnbounded<byte[]>(
            new System.Threading.Channels.UnboundedChannelOptions { SingleReader = true, SingleWriter = true });
        private Task? _pump;
        private bool _justCreatedCharacter;
        private int _keyIncrement;
        private uint _counterAtKey;
        private bool _broken;
        private bool _disposed;

        public XiloaderLobbyHandoff(string host, int dataPort, uint accountId, byte[] sessionHash, byte[]? customKey = null,
            string clientVersion = DefaultClientVersion, Action<PacketLogEntry>? log = null, TimeSpan? timeout = null)
        {
            ArgumentNullException.ThrowIfNull(host);
            ArgumentNullException.ThrowIfNull(sessionHash);
            if (sessionHash.Length != LobbyPackets.SessionHashLength) throw new ArgumentException("Session hash must be exactly 16 bytes.", nameof(sessionHash));
            _host = host;
            _dataPort = dataPort;
            _accountId = accountId;
            _sessionHash = sessionHash;
            _baseKey = CreateBaseKey();
            if (customKey is { Length: 20 }) customKey.CopyTo(_baseKey, 0);
            ClientVersion = clientVersion;
            _log = log;
            Timeout = timeout ?? TimeSpan.FromSeconds(10);
        }

        public string Name => "xiloader";

        /// <summary>How long a data channel message may take.</summary>
        public TimeSpan Timeout { get; }

        public bool IsConnected => !_broken && !_disposed && _dataClient.Connected;

        /// <summary>The body hashes use the session hash as the lobby key; LandSandBoat does not check them.</summary>
        public byte[] LobbyKey => _sessionHash;

        public string ClientVersion { get; }

        /// <summary>LandSandBoat does not check the selection digest.</summary>
        public uint ClientChecksum => 0;

        /// <summary>What LandSandBoat will add to byte 16 of the session key at selection (see the class remarks).</summary>
        public int KeyIncrement => _keyIncrement + (_justCreatedCharacter ? 6 : 0);

        /// <summary>The lobby key counter as current LandSandBoat follows it (see <see cref="LobbyKeyCounter"/>).</summary>
        public uint KeyCounter => _keyCounter.Value;

        /// <summary>Connects xi_data and sends 0xFE with the session hash, as xiloader does when the client starts.</summary>
        public async Task OpenAsync(CancellationToken ct)
        {
            await _dataClient.ConnectAsync(_host, _dataPort, ct).ConfigureAwait(false);
            _data = _dataClient.GetStream();
            _pump = Task.Run(() => PumpDataAsync(_data));
            var packet = new byte[LobbyPackets.DataPacketSize];
            LobbyPackets.WriteDataSessionHash(packet, _sessionHash);
            await SendDataAsync(packet, "GP_LOBBY_CONNECT", ct).ConfigureAwait(false);
        }

        /// <summary>LandSandBoat ignores the 0x26 AuthCode.</summary>
        public byte[] CreateAuthCode() => new byte[PolAuthCode.TokenLength];

        /// <summary>xiloader's send hook: the session hash over the header's packet MD5; the key counter follows the request.</summary>
        public void PrepareRequest(Span<byte> packet)
        {
            _sessionHash.CopyTo(packet.Slice(12, LobbyPackets.SessionHashLength));
            // A 0x1F counts when its 0x01 prompt arrives: LandSandBoat can lose one (see BeforeCharacterListAsync).
            byte command = packet[8];
            _keyCounter.OnRequest(command, deferStep: command == LobbyCommand.RequestGetChr);
        }

        public void OnReply(ReadOnlySpan<byte> packet) => _keyCounter.OnReply(packet);

        /// <summary>
        /// The 0x01 prompt, then 0xA1 and the data channel's 0x03. LandSandBoat can lose the request: its view session
        /// clears its read buffer when a write completes, and a request that arrived just before that is read as zeros and
        /// dropped (seen after a deletion, whose OK it writes before its database work). A request with no prompt after a
        /// short wait is sent once more; a late duplicate prompt is skipped later.
        /// </summary>
        public async Task BeforeCharacterListAsync(Func<CancellationToken, Task> resend, CancellationToken ct)
        {
            byte[]? prompt = await ReadDataCoreAsync("character list", ct, PromptRetryWait, expect: LobbyDataCommand.RequestAccount, breakOnTimeout: false).ConfigureAwait(false);
            if (prompt == null)
            {
                GordianLog.Warn(LogCategories.LsbLogin, $"No account prompt {PromptRetryWait.TotalSeconds:0.#} s after 0x1F; sending the character list request again.");
                await resend(ct).ConfigureAwait(false);
                prompt = await ReadDataCoreAsync("character list", ct, expect: LobbyDataCommand.RequestAccount).ConfigureAwait(false);
            }
            if (prompt!.Length == 0 || prompt[0] != LobbyDataCommand.RequestAccount)
                throw Broken(new LobbyRequestException("character list", 0, $"Data server sent 0x{(prompt.Length > 0 ? prompt[0] : 0):X2} instead of the account prompt (0x01)."));

            var a1 = new byte[LobbyPackets.DataPacketSize];
            LobbyPackets.WriteDataAccount(a1, _accountId, 0, _sessionHash);
            await SendDataAsync(a1, "GP_LOBBY_ACCOUNT", ct).ConfigureAwait(false);

            var ids = await ReadDataAsync("character list", ct).ConfigureAwait(false);
            if (ids.Length == 0 || ids[0] != LobbyDataCommand.CharacterIdList)
                throw Broken(new LobbyRequestException("character list", 0,
                    $"Unexpected response from data server: expected 0x03, received {ids.Length} bytes (0x{(ids.Length > 0 ? ids[0] : 0):X2}). The server may have rejected the session hash."));
        }

        /// <summary>The data channel's 0x02 key prompt, answered with 0xA2 (the session key and the character id).</summary>
        public async Task AfterSelectRequestAsync(LobbyCharacter character, CancellationToken ct)
        {
            var prompt = await ReadDataAsync("select", ct).ConfigureAwait(false);
            if (prompt.Length == 0 || prompt[0] != LobbyDataCommand.RequestKey)
                throw Broken(new LobbyRequestException("select", 0,
                    $"LandSandBoat character selection failed: the data server sent {prompt.Length} bytes (0x{(prompt.Length > 0 ? prompt[0] : 0):X2}) instead of the key prompt (0x02). Check LSB server console for details."));

            // LandSandBoat derives the counter key while handling 0xA2, from the counter as it stands then.
            _counterAtKey = _keyCounter.Value;
            var a2 = new byte[LobbyPackets.DataPacketSize];
            LobbyPackets.WriteDataKey(a2, _baseKey, character.ContentId);
            await SendDataAsync(a2, "GP_LOBBY_CHAR_SELECT_KEY", ct).ConfigureAwait(false);
        }

        public async Task OnCharacterChangedAsync(LobbyCharacterChange change, CancellationToken ct)
        {
            switch (change)
            {
                case LobbyCharacterChange.Created:
                    _justCreatedCharacter = true;
                    break;
                case LobbyCharacterChange.Deleted:
                    _keyIncrement += 4;
                    await Task.Delay(AfterDeletePause, ct).ConfigureAwait(false);
                    break;
                case LobbyCharacterChange.Renamed:
                    _keyIncrement += 4;
                    break;
            }
        }

        public void OnRefused(byte command, int code)
        {
            if (command == LobbyCommand.RequestSelectChr && code == LobbyErrorCode.CharacterAlreadyLoggedIn) _keyIncrement += 1;
        }

        public MapSessionTicket CreateTicket(LobbyCharacter character, in LobbyNextLogin next, uint selectModifier)
        {
            string zoneIp = next.ServerAddress == 0 ? _host : new IPAddress(next.ServerAddress).ToString();
            int zonePort = next.ServerPort != 0 ? (int)next.ServerPort : LsbLoginClient.DefaultDataPort;
            string name = next.Name.Length > 0 ? next.Name : character.Name;

            var key = LsbSessionKeys.DeriveLegacyKey(_baseKey, KeyIncrement);
            if (KeyIncrement != 0)
                GordianLog.Info(LogCategories.LsbLogin, $"Legacy session key byte 16 raised by {KeyIncrement} for this lobby session's creations/deletions/renames (old LandSandBoat key accounting).");
            var candidates = LsbSessionKeys.BuildCandidates(_sessionHash, _counterAtKey, _baseKey, KeyIncrement);
            if (_counterAtKey == 0)
                GordianLog.Warn(LogCategories.LsbLogin, "No lobby key counter (no 0x05 reply seen); the counter-style key keeps the loader's bytes 16-19, as LandSandBoat does.");
            GordianLog.Info(LogCategories.LsbLogin, $"Map session key candidates (lobby key counter {_counterAtKey:X8}, legacy increment {KeyIncrement}): " +
                string.Join("; ", candidates.Select(c => c.Describe())) + ". Compare with the connect server's \"key value\" / \"key counter\" line.");
            var ticket = new MapSessionTicket
            {
                AccountId = _accountId,
                CharacterId = character.ContentId,
                CharacterName = name,
                ZoneIp = zoneIp,
                ZonePort = zonePort,
                SessionHash = _sessionHash,
                BlowfishKey = key,
                KeyCandidates = candidates,
                LobbyKeyCounter = _counterAtKey,
            };
            _keyIncrement = 0;
            _justCreatedCharacter = false;
            return ticket;
        }

        private LobbyRequestException Broken(LobbyRequestException ex)
        {
            _broken = true;
            return ex;
        }

        private async Task SendDataAsync(ReadOnlyMemory<byte> packet, string name, CancellationToken ct)
        {
            if (_data == null) throw Broken(new LobbyRequestException(name, 0, "The data channel is not open."));
            try
            {
                await _data.WriteAsync(packet, ct).ConfigureAwait(false);
                await _data.FlushAsync(ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException)
            {
                throw Broken(new LobbyRequestException(name, 0, $"Lobby data connection lost while sending {name}.", ex));
            }
            Log(PacketDirection.Outbound, packet.Span, name);
        }

        private async Task<byte[]> ReadDataAsync(string request, CancellationToken ct) => (await ReadDataCoreAsync(request, ct).ConfigureAwait(false))!;

        /// <summary>
        /// Reads one data channel message from the reader's queue. Account prompts (0x01) are skipped unless
        /// <paramref name="expect"/> asks for one: a duplicate from a resent 0x1F can arrive late. With
        /// <paramref name="breakOnTimeout"/> false a timeout returns null instead of failing the session.
        /// </summary>
        private async Task<byte[]?> ReadDataCoreAsync(string request, CancellationToken ct, TimeSpan? timeout = null, byte expect = 0, bool breakOnTimeout = true)
        {
            if (_data == null) throw Broken(new LobbyRequestException(request, 0, "The data channel is not open."));
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeout ?? Timeout);
            try
            {
                while (true)
                {
                    // Waiting on the queue can time out safely; the socket read behind it is never cancelled.
                    if (!await _messages.Reader.WaitToReadAsync(cts.Token).ConfigureAwait(false) || !_messages.Reader.TryRead(out var message))
                        throw Broken(new LobbyRequestException(request, 0, $"Lobby {request}: the data server closed the connection without responding."));
                    // Each account prompt answers one 0x1F the view server read, a late duplicate included.
                    if (message[0] == LobbyDataCommand.RequestAccount) _keyCounter.ConfirmDeferredRequest();
                    if (message[0] == 0x24 && message.Length >= 34 && LobbyPackets.ReadCommand(message) == LobbyCommand.ResponseError)
                        throw Broken(LobbyRequestException.FromServer(request, LobbyPackets.ReadErrorCode(message)));
                    if (message[0] == LobbyDataCommand.RequestAccount && expect != LobbyDataCommand.RequestAccount)
                    {
                        GordianLog.Debug(LogCategories.LsbLogin, "Skipping a duplicate account prompt (0x01) on the data channel.");
                        continue;
                    }
                    return message;
                }
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                if (!breakOnTimeout) return null;
                throw Broken(new LobbyRequestException(request, 0, $"Lobby {request}: no reply from the data server (timed out after {(timeout ?? Timeout).TotalSeconds:0.#} s)."));
            }
            catch (Exception ex) when (ex is IOException or SocketException)
            {
                throw Broken(new LobbyRequestException(request, 0, $"Lobby {request}: data connection lost ({ex.Message}).", ex));
            }
        }

        /// <summary>
        /// Reads data channel messages (their length known from the first byte) into the queue until the connection
        /// ends. A cancelled socket read can lose bytes that were arriving, so reads here are never cancelled: the
        /// timeouts apply to the queue instead.
        /// </summary>
        private async Task PumpDataAsync(NetworkStream stream)
        {
            try
            {
                while (true)
                {
                    var first = new byte[1];
                    if (!await ReadExactlyAsync(stream, first, CancellationToken.None).ConfigureAwait(false)) break;
                    int length = LobbyPackets.DataMessageLength(first[0]);
                    byte[] message = first;
                    if (length > 1)
                    {
                        message = new byte[length];
                        message[0] = first[0];
                        if (!await ReadExactlyAsync(stream, message.AsMemory(1), CancellationToken.None).ConfigureAwait(false)) break;
                    }
                    Log(PacketDirection.Inbound, message, DataPacketName(message[0]));
                    _messages.Writer.TryWrite(message);
                }
            }
            catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException)
            {
                // The connection dropped or was disposed.
            }
            finally
            {
                _messages.Writer.TryComplete();
            }
        }

        private static async Task<bool> ReadExactlyAsync(NetworkStream stream, Memory<byte> buffer, CancellationToken ct)
        {
            int read = 0;
            while (read < buffer.Length)
            {
                int n = await stream.ReadAsync(buffer[read..], ct).ConfigureAwait(false);
                if (n == 0) return false;
                read += n;
            }
            return true;
        }

        private static string DataPacketName(byte command) => command switch
        {
            LobbyDataCommand.RequestAccount => "GP_LOBBY_ACCOUNT_PROMPT",
            LobbyDataCommand.RequestKey => "GP_LOBBY_CHAR_CONFIRM",
            LobbyDataCommand.CharacterIdList => "GP_LOBBY_CHAR_LIST",
            _ => $"GP_LOBBY_DATA_0x{command:X2}",
        };

        private void Log(PacketDirection direction, ReadOnlySpan<byte> data, string name)
        {
            if (_log == null) return;
            _log(new PacketLogEntry
            {
                Timestamp = DateTime.UtcNow,
                Direction = direction,
                PacketId = data.Length > 0 ? data[0] : (byte)0,
                PacketName = name,
                SequenceId = 0,
                Size = data.Length,
                RawBytes = data.ToArray(),
            });
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed) return;
            _disposed = true;
            try { _data?.Dispose(); } catch { }
            _dataClient.Dispose();
            if (_pump != null) await _pump.ConfigureAwait(false);
        }
    }
}
