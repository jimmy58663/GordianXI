// src/Gordian.Core/Network/LandSandBoat/LsbLobbySession.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Gordian.Core.Diagnostics;

namespace Gordian.Core.Network.LandSandBoat
{
    /// <summary>
    /// The lobby operations a character select screen drives: the character list, the world list, creation, deletion,
    /// renaming and selection. <see cref="LsbLobbySession"/> implements it against LandSandBoat; tests fake it.
    /// </summary>
    public interface ILobbyBackend : IAsyncDisposable
    {
        /// <summary>Every content id of the account (free slots included), in list order.</summary>
        IReadOnlyList<LobbyCharacter> Characters { get; }

        /// <summary>Whether the connection is still usable (LandSandBoat closes it on some refused requests).</summary>
        bool IsConnected { get; }

        Task<IReadOnlyList<LobbyCharacter>> RefreshCharactersAsync(CancellationToken ct = default);
        Task<IReadOnlyList<LobbyWorld>> GetWorldsAsync(CancellationToken ct = default);
        Task CheckNameAsync(LobbyCharacter freeSlot, string name, string worldName, CancellationToken ct = default);
        Task CreateCharacterAsync(LobbyCharacter freeSlot, LobbyCharacterCreation creation, CancellationToken ct = default);
        Task DeleteCharacterAsync(LobbyCharacter character, CancellationToken ct = default);
        Task<LsbSessionTicket> RenameAndSelectAsync(LobbyCharacter character, string newName, CancellationToken ct = default);
        Task<LsbSessionTicket> SelectCharacterAsync(LobbyCharacter character, CancellationToken ct = default);
    }

    /// <summary>
    /// A live LandSandBoat lobby connection: the data channel (xi_data, 54230) and the view channel (xi_view, 54001),
    /// kept open while the player is in the character select screen. Requests run one at a time.
    /// <para>
    /// Flows (XiPackets lobby/Protocol.md; LandSandBoat src/login/view_session.cpp and data_session.cpp):
    /// connect = data 0xFE, view 0x26 -> 0x05, data 0xA1 -> data 0x03 + view 0x20;
    /// refresh = view 0x1F -> data 0x01, data 0xA1 -> data 0x03 + view 0x20;
    /// worlds = view 0x24 -> 0x23; create = view 0x22 -> 0x03, view 0x21 -> 0x03, then refresh;
    /// delete = view 0x14 -> 0x03, then refresh; rename = view 0x28 -> 0x03, then select;
    /// select = view 0x07 -> data 0x02, data 0xA2 -> view 0x0B (the server then closes the view channel).
    /// Refusals come back as view 0x04 with an error code.
    /// </para>
    /// <para>
    /// <b>Session key accounting (LandSandBoat <c>data_session.cpp</c> / <c>view_session.cpp</c>):</b> the server stores
    /// the key the client sends in 0xA2 with byte 16 raised by 4 per deletion and per rename, 1 per refused "already
    /// logged in" selection and 6 when a character was created in this session; the map server then decrypts with
    /// that key. The client must use the same raised key for the map session, so the ticket's key carries the same
    /// increments while 0xA2 carries the base key.
    /// </para>
    /// </summary>
    public sealed class LsbLobbySession : ILobbyBackend, IDisposable
    {
        /// <summary>The client version sent in 0x26. LandSandBoat compares its first 6 characters with <c>login.CLIENT_VER</c>.</summary>
        public const string DefaultClientVersion = "30260904_1";

        /// <summary>The standard xiloader / retail base session key: 16 zero bytes, then 58 E0 5D AD.</summary>
        public static byte[] CreateBaseKey() => new byte[20] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0x58, 0xE0, 0x5D, 0xAD };

        private readonly string _host;
        private readonly uint _accountId;
        private readonly byte[] _sessionHash;
        private readonly byte[] _baseKey;
        private readonly TcpClient _dataClient;
        private readonly TcpClient _viewClient;
        private readonly NetworkStream _data;
        private readonly NetworkStream _view;
        private readonly SemaphoreSlim _gate = new(1, 1);
        private readonly Action<PacketLogEntry>? _log;
        private List<LobbyCharacter> _characters = new();
        private bool _justCreatedCharacter;
        private int _keyIncrement;
        private bool _broken;
        private bool _disposed;

        private LsbLobbySession(string host, uint accountId, byte[] sessionHash, byte[] baseKey, TcpClient data, TcpClient view,
            Action<PacketLogEntry>? log, TimeSpan timeout)
        {
            _host = host;
            _accountId = accountId;
            _sessionHash = sessionHash;
            _baseKey = baseKey;
            _dataClient = data;
            _viewClient = view;
            _data = data.GetStream();
            _view = view.GetStream();
            _log = log;
            Timeout = timeout;
        }

        /// <summary>How long a reply may take before the request fails.</summary>
        public TimeSpan Timeout { get; }

        public IReadOnlyList<LobbyCharacter> Characters => _characters;

        /// <summary>The server's expansions (0x05 <c>excode_server</c>) and features (<c>excode_server2</c>: bit 0 security token, 2-7 wardrobes 3-8).</summary>
        public uint ServerExpansions { get; private set; }
        public uint ServerFeatures { get; private set; }

        /// <summary>What LandSandBoat will add to byte 16 of the session key at selection (see the class remarks).</summary>
        public int KeyIncrement => _keyIncrement + (_justCreatedCharacter ? 6 : 0);

        public bool IsConnected => !_broken && !_disposed && _viewClient.Connected;

        /// <summary>
        /// Opens the data and view channels and reads the character list: data 0xFE, view 0x26 -> 0x05, data 0xA1 ->
        /// data 0x03 + view 0x20. Throws <see cref="LobbyRequestException"/> when the server refuses the login (a version
        /// lock or maintenance answers 0x04).
        /// </summary>
        public static async Task<LsbLobbySession> ConnectAsync(string host, int dataPort, int viewPort, uint accountId, byte[] sessionHash,
            byte[]? customKey = null, Action<PacketLogEntry>? log = null, string clientVersion = DefaultClientVersion,
            TimeSpan? timeout = null, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(host);
            ArgumentNullException.ThrowIfNull(sessionHash);
            if (sessionHash.Length != LobbyPackets.SessionHashLength)
            {
                throw new ArgumentException("Session hash must be exactly 16 bytes.", nameof(sessionHash));
            }

            var key = CreateBaseKey();
            if (customKey is { Length: 20 }) customKey.CopyTo(key, 0);

            var dataClient = new TcpClient();
            var viewClient = new TcpClient();
            LsbLobbySession? session = null;
            try
            {
                await dataClient.ConnectAsync(host, dataPort, ct).ConfigureAwait(false);
                await viewClient.ConnectAsync(host, viewPort, ct).ConfigureAwait(false);
                session = new LsbLobbySession(host, accountId, sessionHash, key, dataClient, viewClient, log, timeout ?? TimeSpan.FromSeconds(10));
                await session.LoginAsync(clientVersion, ct).ConfigureAwait(false);
                return session;
            }
            catch
            {
                if (session != null) session.Dispose();
                else
                {
                    dataClient.Dispose();
                    viewClient.Dispose();
                }
                throw;
            }
        }

        private async Task LoginAsync(string clientVersion, CancellationToken ct)
        {
            var buffer = new byte[LobbyPackets.LobbyLoginSize];
            LobbyPackets.WriteDataSessionHash(buffer, _sessionHash);
            await SendDataAsync(buffer.AsMemory(0, LobbyPackets.DataPacketSize), "GP_LOBBY_CONNECT", ct).ConfigureAwait(false);

            LobbyPackets.WriteLobbyLogin(buffer, _sessionHash, clientVersion);
            await SendViewAsync(buffer, "GP_LOBBY_LOGIN", ct).ConfigureAwait(false);
            var reply = await ReadViewAsync("login", ct).ConfigureAwait(false);
            byte command = LobbyPackets.ReadCommand(reply);
            if (command == LobbyCommand.ResponseError)
            {
                int code = LobbyPackets.ReadErrorCode(reply);
                throw new LobbyRequestException("login", code,
                    $"Lobby view server returned error code {code} during version handshake ({LobbyErrorCode.Describe(code)}).");
            }
            if (command != LobbyCommand.ResponseKey)
            {
                throw new LobbyRequestException("login", 0,
                    $"Lobby view server returned invalid response (command 0x{command:X2}, {reply.Length} bytes, expected 0x05). The server may have rejected the session hash.");
            }
            (_, uint expansions, uint features) = LobbyPackets.ReadKey(reply);
            ServerExpansions = expansions;
            ServerFeatures = features;

            await ReadCharacterListAsync(ct).ConfigureAwait(false);
        }

        /// <summary>Sends data 0xA1 and reads data 0x03 and the view 0x20 list that follows it.</summary>
        private async Task ReadCharacterListAsync(CancellationToken ct)
        {
            var a1 = new byte[LobbyPackets.DataPacketSize];
            LobbyPackets.WriteDataAccount(a1, _accountId, 0, _sessionHash);
            await SendDataAsync(a1, "GP_LOBBY_ACCOUNT", ct).ConfigureAwait(false);

            var ids = await ReadDataAsync("character list", ct).ConfigureAwait(false);
            if (ids.Length == 0 || ids[0] != LobbyDataCommand.CharacterIdList)
            {
                throw new LobbyRequestException("character list", 0,
                    $"Unexpected response from data server: expected 0x03, received {ids.Length} bytes (0x{(ids.Length > 0 ? ids[0] : 0):X2}). The server may have rejected the session hash.");
            }

            var list = await ReadViewAsync("character list", ct).ConfigureAwait(false);
            byte command = LobbyPackets.ReadCommand(list);
            if (command == LobbyCommand.ResponseError) throw LobbyRequestException.FromServer("character list", LobbyPackets.ReadErrorCode(list));
            if (command != LobbyCommand.ResponseChrInfo2)
            {
                throw new LobbyRequestException("character list", 0, $"Lobby view server sent command 0x{command:X2} instead of the character list (0x20).");
            }
            _characters = LobbyPackets.ParseCharacterList(list);
            GordianLog.Info("LSB_LOGIN", $"Lobby character list: {_characters.Count} content id(s): " +
                string.Join(", ", _characters.ConvertAll(c => c.IsEmpty ? $"{c.Slot}=(free)" : $"{c.Slot}={c.Name}")));
        }

        public async Task<IReadOnlyList<LobbyCharacter>> RefreshCharactersAsync(CancellationToken ct = default)
        {
            await _gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                await RefreshUnlockedAsync(ct).ConfigureAwait(false);
                return _characters;
            }
            finally { _gate.Release(); }
        }

        private async Task RefreshUnlockedAsync(CancellationToken ct)
        {
            EnsureUsable();
            var request = new byte[LobbyPackets.GetCharactersSize];
            LobbyPackets.WriteGetCharacters(request, _sessionHash);
            await SendViewAsync(request, "GP_LOBBY_GET_CHR", ct).ConfigureAwait(false);
            // LandSandBoat answers the view request on the data channel: 0x01 asks for the account id (0xA1).
            var prompt = await ReadDataAsync("character list", ct).ConfigureAwait(false);
            if (prompt.Length == 0 || prompt[0] != LobbyDataCommand.RequestAccount)
            {
                throw Broken(new LobbyRequestException("character list", 0, $"Data server sent 0x{(prompt.Length > 0 ? prompt[0] : 0):X2} instead of the account prompt (0x01)."));
            }
            await ReadCharacterListAsync(ct).ConfigureAwait(false);
        }

        public async Task<IReadOnlyList<LobbyWorld>> GetWorldsAsync(CancellationToken ct = default)
        {
            await _gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                EnsureUsable();
                var request = new byte[LobbyPackets.QueryWorldListSize];
                LobbyPackets.WriteQueryWorldList(request, _sessionHash);
                await SendViewAsync(request, "GP_LOBBY_QUERY_WORLD_LIST", ct).ConfigureAwait(false);
                var reply = await ReadViewAsync("world list", ct).ConfigureAwait(false);
                byte command = LobbyPackets.ReadCommand(reply);
                if (command == LobbyCommand.ResponseError) throw LobbyRequestException.FromServer("world list", LobbyPackets.ReadErrorCode(reply));
                if (command != LobbyCommand.ResponseWorldList) throw new LobbyRequestException("world list", 0, $"Lobby view server sent 0x{command:X2} instead of the world list (0x23).");
                return LobbyPackets.ParseWorldList(reply);
            }
            finally { _gate.Release(); }
        }

        public async Task CheckNameAsync(LobbyCharacter freeSlot, string name, string worldName, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(freeSlot);
            await _gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                await CheckNameUnlockedAsync(freeSlot, name, worldName, ct).ConfigureAwait(false);
            }
            finally { _gate.Release(); }
        }

        private async Task CheckNameUnlockedAsync(LobbyCharacter freeSlot, string name, string worldName, CancellationToken ct)
        {
            EnsureUsable();
            var request = new byte[LobbyPackets.CreateCharacterPreSize];
            LobbyPackets.WriteCreateCharacterPre(request, _sessionHash, freeSlot.ContentId, name, worldName);
            await SendViewAsync(request, "GP_LOBBY_CREATE_CHR_PRE", ct).ConfigureAwait(false);
            await ExpectOkAsync("name check", ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Creates a character in a free slot: the name check (0x22) and the creation (0x21), then the refreshed list.
        /// LandSandBoat drops the view connection when it refuses 0x21 (bad race, size, face or nation), so a refused
        /// creation leaves the session unusable.
        /// </summary>
        public async Task CreateCharacterAsync(LobbyCharacter freeSlot, LobbyCharacterCreation creation, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(freeSlot);
            await _gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                string world = freeSlot.WorldName.Length > 0 ? freeSlot.WorldName : string.Empty;
                await CheckNameUnlockedAsync(freeSlot, creation.Name, world, ct).ConfigureAwait(false);

                var request = new byte[LobbyPackets.CreateCharacterSize];
                LobbyPackets.WriteCreateCharacter(request, _sessionHash, freeSlot.ContentId, creation);
                await SendViewAsync(request, "GP_LOBBY_CREATE_CHR", ct).ConfigureAwait(false);
                await ExpectOkAsync("character creation", ct).ConfigureAwait(false);
                _justCreatedCharacter = true;
                GordianLog.Info("LSB_LOGIN", $"Created character '{creation.Name}' (race {creation.Race}, face {creation.CombinedFace}, job {creation.MainJob}, size {creation.Size}, nation {creation.Nation}).");
                await RefreshUnlockedAsync(ct).ConfigureAwait(false);
            }
            finally { _gate.Release(); }
        }

        /// <summary>Deletes a character (0x14 -> 0x03), then reads the refreshed list.</summary>
        public async Task DeleteCharacterAsync(LobbyCharacter character, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(character);
            await _gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                EnsureUsable();
                var request = new byte[LobbyPackets.DeleteCharacterSize];
                LobbyPackets.WriteDeleteCharacter(request, _sessionHash, character.ContentId, character.ServerId);
                await SendViewAsync(request, "GP_LOBBY_DELETE_CHR", ct).ConfigureAwait(false);
                await ExpectOkAsync("character deletion", ct).ConfigureAwait(false);
                _keyIncrement += 4;
                GordianLog.Info("LSB_LOGIN", $"Deleted character '{character.Name}' (content id {character.ContentId}).");
                await RefreshUnlockedAsync(ct).ConfigureAwait(false);
            }
            finally { _gate.Release(); }
        }

        /// <summary>Renames a character the server flagged for renaming (0x28 -> 0x03), then selects it under the new name.</summary>
        public async Task<LsbSessionTicket> RenameAndSelectAsync(LobbyCharacter character, string newName, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(character);
            await _gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                EnsureUsable();
                var request = new byte[LobbyPackets.RenameCharacterSize];
                LobbyPackets.WriteRenameCharacter(request, _sessionHash, character.ContentId, character.ServerId, newName);
                await SendViewAsync(request, "GP_LOBBY_RENAME_CHR", ct).ConfigureAwait(false);
                await ExpectOkAsync("rename", ct).ConfigureAwait(false);
                _keyIncrement += 4;
                return await SelectUnlockedAsync(character with { Name = newName, RenameRequired = false }, ct).ConfigureAwait(false);
            }
            finally { _gate.Release(); }
        }

        public async Task<LsbSessionTicket> SelectCharacterAsync(LobbyCharacter character, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(character);
            await _gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                return await SelectUnlockedAsync(character, ct).ConfigureAwait(false);
            }
            finally { _gate.Release(); }
        }

        private async Task<LsbSessionTicket> SelectUnlockedAsync(LobbyCharacter character, CancellationToken ct)
        {
            EnsureUsable();
            // The id and name must name the same character: xi_view looks the pair up (chars.charid AND charname) and
            // drops the connection on a mismatch ("tried to select a character id with a mismatched character name").
            var select = new byte[LobbyPackets.SelectCharacterSize];
            LobbyPackets.WriteSelectCharacter(select, _sessionHash, character.ContentId, character.ServerId, character.Name);
            GordianLog.Debug("LSB_LOGIN", $"Notifying xi_view of character selection (0x07) for '{character.Name}' (ID: {character.ContentId})...");
            await SendViewAsync(select, "GP_LOBBY_CHAR_SELECT", ct).ConfigureAwait(false);

            // The data channel answers with 0x02, the prompt for the session key.
            var prompt = await ReadDataAsync("select", ct).ConfigureAwait(false);
            if (prompt.Length == 0 || prompt[0] != LobbyDataCommand.RequestKey)
            {
                throw Broken(new LobbyRequestException("select", 0,
                    $"LandSandBoat character selection failed: the data server sent {prompt.Length} bytes (0x{(prompt.Length > 0 ? prompt[0] : 0):X2}) instead of the key prompt (0x02). Check LSB server console for details."));
            }

            var a2 = new byte[LobbyPackets.DataPacketSize];
            LobbyPackets.WriteDataKey(a2, _baseKey, character.ContentId);
            GordianLog.Debug("LSB_LOGIN", $"Transmitting 0xA2 character selection & Blowfish key to xi_data for char ID {character.ContentId}...");
            await SendDataAsync(a2, "GP_LOBBY_CHAR_SELECT_KEY", ct).ConfigureAwait(false);

            var reply = await ReadViewAsync("select", ct).ConfigureAwait(false);
            byte command = LobbyPackets.ReadCommand(reply);
            if (command == LobbyCommand.ResponseError)
            {
                int code = LobbyPackets.ReadErrorCode(reply);
                if (code == LobbyErrorCode.CharacterAlreadyLoggedIn) _keyIncrement += 1;
                throw new LobbyRequestException("select", code,
                    $"LandSandBoat character selection failed: server returned error code {code} ({LobbyErrorCode.Describe(code)}, FFXI-{LobbyErrorCode.DisplayCode(code)})."
                    + (code == LobbyErrorCode.CharacterAlreadyLoggedIn ? " Reconnecting..." : string.Empty));
            }
            if (!LobbyPackets.TryParseNextLogin(reply, out var next))
            {
                throw Broken(new LobbyRequestException("select", 0,
                    $"LandSandBoat character selection failed: server returned {reply.Length} bytes with code 0x{command:X2} (expected 72 bytes with 0x0B). Check LSB server console for details."));
            }

            string zoneIp = next.ServerAddress == 0 ? _host : new IPAddress(next.ServerAddress).ToString();
            int zonePort = next.ServerPort != 0 ? (int)next.ServerPort : LsbLoginClient.DefaultDataPort;
            string name = next.Name.Length > 0 ? next.Name : character.Name;

            var key = (byte[])_baseKey.Clone();
            key[16] = unchecked((byte)(key[16] + KeyIncrement));
            if (KeyIncrement != 0)
            {
                GordianLog.Info("LSB_LOGIN", $"Session key byte 16 raised by {KeyIncrement} for this lobby session's creations/deletions/renames (LandSandBoat key accounting).");
            }
            _keyIncrement = 0;
            _justCreatedCharacter = false;
            // The server closes the view channel after 0x0B; the lobby session is over.
            _broken = true;

            GordianLog.Info("LSB_LOGIN", $"Character selection SUCCESS! '{name}' (ID: {character.ContentId}). Target zone map server: {zoneIp}:{zonePort}");
            return new LsbSessionTicket
            {
                AccountId = _accountId,
                CharacterId = character.ContentId,
                CharacterName = name,
                ZoneIp = zoneIp,
                ZonePort = zonePort,
                SessionHash = _sessionHash,
                BlowfishKey = key,
            };
        }

        private async Task ExpectOkAsync(string request, CancellationToken ct)
        {
            var reply = await ReadViewAsync(request, ct).ConfigureAwait(false);
            byte command = LobbyPackets.ReadCommand(reply);
            if (command == LobbyCommand.ResponseError) throw LobbyRequestException.FromServer(request, LobbyPackets.ReadErrorCode(reply));
            if (command != LobbyCommand.ResponseOk)
            {
                throw Broken(new LobbyRequestException(request, 0, $"Lobby {request}: server sent 0x{command:X2} instead of OK (0x03)."));
            }
        }

        private void EnsureUsable()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_broken) throw new LobbyRequestException("request", 0, "The lobby connection was closed; log in again.");
        }

        private LobbyRequestException Broken(LobbyRequestException ex)
        {
            _broken = true;
            return ex;
        }

        private async Task SendViewAsync(ReadOnlyMemory<byte> packet, string name, CancellationToken ct)
        {
            try
            {
                await _view.WriteAsync(packet, ct).ConfigureAwait(false);
                await _view.FlushAsync(ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException)
            {
                throw Broken(new LobbyRequestException(name, 0, $"Lobby view connection lost while sending {name}.", ex));
            }
            Log(PacketDirection.Outbound, packet.Span.Length > 8 ? packet.Span[8] : (byte)0, packet.Span, name);
        }

        private async Task SendDataAsync(ReadOnlyMemory<byte> packet, string name, CancellationToken ct)
        {
            try
            {
                await _data.WriteAsync(packet, ct).ConfigureAwait(false);
                await _data.FlushAsync(ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException)
            {
                throw Broken(new LobbyRequestException(name, 0, $"Lobby data connection lost while sending {name}.", ex));
            }
            Log(PacketDirection.Outbound, packet.Span[0], packet.Span, name);
        }

        /// <summary>Reads one view packet: the u32 size first, then the rest.</summary>
        private async Task<byte[]> ReadViewAsync(string request, CancellationToken ct)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(Timeout);
            try
            {
                var header = new byte[4];
                if (!await ReadExactlyAsync(_view, header, cts.Token).ConfigureAwait(false))
                {
                    throw Broken(new LobbyRequestException(request, 0, $"Lobby {request}: the view server closed the connection without responding."));
                }
                uint size = LobbyPackets.ReadSize(header);
                if (size < 12 || size > 0x4000)
                {
                    throw Broken(new LobbyRequestException(request, 0, $"Lobby {request}: invalid view packet size {size}."));
                }
                var packet = new byte[size];
                header.CopyTo(packet, 0);
                if (!await ReadExactlyAsync(_view, packet.AsMemory(4), cts.Token).ConfigureAwait(false))
                {
                    throw Broken(new LobbyRequestException(request, 0, $"Lobby {request}: the view connection closed mid-packet."));
                }
                Log(PacketDirection.Inbound, packet[8], packet, ViewPacketName(packet[8]));
                return packet;
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                throw Broken(new LobbyRequestException(request, 0, $"Lobby {request}: no reply from the view server (timed out after {Timeout.TotalSeconds:0} s)."));
            }
            catch (Exception ex) when (ex is IOException or SocketException)
            {
                throw Broken(new LobbyRequestException(request, 0, $"Lobby {request}: view connection lost ({ex.Message}).", ex));
            }
        }

        /// <summary>Reads one data channel message, its length known from its first byte.</summary>
        private async Task<byte[]> ReadDataAsync(string request, CancellationToken ct)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(Timeout);
            try
            {
                var first = new byte[1];
                if (!await ReadExactlyAsync(_data, first, cts.Token).ConfigureAwait(false))
                {
                    throw Broken(new LobbyRequestException(request, 0, $"Lobby {request}: the data server closed the connection without responding."));
                }
                int length = LobbyPackets.DataMessageLength(first[0]);
                byte[] message;
                if (length > 1)
                {
                    message = new byte[length];
                    message[0] = first[0];
                    if (!await ReadExactlyAsync(_data, message.AsMemory(1), cts.Token).ConfigureAwait(false))
                    {
                        throw Broken(new LobbyRequestException(request, 0, $"Lobby {request}: the data connection closed mid-message."));
                    }
                }
                else
                {
                    message = first;
                }
                Log(PacketDirection.Inbound, message[0], message, DataPacketName(message[0]));
                if (message[0] == 0x24 && message.Length >= 34 && LobbyPackets.ReadCommand(message) == LobbyCommand.ResponseError)
                {
                    throw Broken(LobbyRequestException.FromServer(request, LobbyPackets.ReadErrorCode(message)));
                }
                return message;
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                throw Broken(new LobbyRequestException(request, 0, $"Lobby {request}: no reply from the data server (timed out after {Timeout.TotalSeconds:0} s)."));
            }
            catch (Exception ex) when (ex is IOException or SocketException)
            {
                throw Broken(new LobbyRequestException(request, 0, $"Lobby {request}: data connection lost ({ex.Message}).", ex));
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

        private static string ViewPacketName(byte command) => command switch
        {
            LobbyCommand.ResponseOk => "GP_LOBBY_OK",
            LobbyCommand.ResponseError => "GP_LOBBY_ERROR",
            LobbyCommand.ResponseKey => "GP_LOBBY_VERSION_REPLY",
            LobbyCommand.ResponseNextLogin => "GP_LOBBY_ZONE_TICKET",
            LobbyCommand.ResponseChrInfo2 => "GP_LOBBY_CHAR_INFO",
            LobbyCommand.ResponseWorldList => "GP_LOBBY_WORLD_LIST",
            _ => $"GP_LOBBY_0x{command:X2}",
        };

        private static string DataPacketName(byte command) => command switch
        {
            LobbyDataCommand.RequestAccount => "GP_LOBBY_ACCOUNT_PROMPT",
            LobbyDataCommand.RequestKey => "GP_LOBBY_CHAR_CONFIRM",
            LobbyDataCommand.CharacterIdList => "GP_LOBBY_CHAR_LIST",
            _ => $"GP_LOBBY_DATA_0x{command:X2}",
        };

        private void Log(PacketDirection direction, byte command, ReadOnlySpan<byte> data, string name)
        {
            if (_log == null) return;
            _log(new PacketLogEntry
            {
                Timestamp = DateTime.UtcNow,
                Direction = direction,
                PacketId = command,
                PacketName = name,
                SequenceId = 0,
                Size = data.Length,
                RawBytes = data.ToArray(),
            });
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { _view.Dispose(); } catch { }
            try { _data.Dispose(); } catch { }
            _viewClient.Dispose();
            _dataClient.Dispose();
            _gate.Dispose();
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
