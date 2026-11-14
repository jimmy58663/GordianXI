// src/Gordian.Core/Network/Lobby/LobbySession.cs
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Gordian.Core.Diagnostics;
using Gordian.Core.Network.LandSandBoat;
using Gordian.Core.Network.Retail;

namespace Gordian.Core.Network.Lobby
{
    /// <summary>
    /// The lobby operations a character select screen drives: the character list, the world list, creation, deletion,
    /// renaming and selection. <see cref="LobbySession"/> implements it for retail and LandSandBoat; tests fake it.
    /// </summary>
    public interface ILobbyBackend : IAsyncDisposable
    {
        /// <summary>Every content id of the account (free slots included), in list order.</summary>
        IReadOnlyList<LobbyCharacter> Characters { get; }

        /// <summary>Whether the connection is still usable (a server can close it on some refused requests).</summary>
        bool IsConnected { get; }

        /// <summary>
        /// The account's expansions as the server reported them (S2C 0x05 <c>excode_server</c>: bit 1 Rise of the Zilart
        /// through bit 11 Seekers of Adoulin, LandSandBoat's <c>EXPANSION_DISPLAY</c>), or null when not known. The title
        /// menu dims the banners of the ones the account lacks.
        /// </summary>
        uint? AccountExpansions => null;

        Task<IReadOnlyList<LobbyCharacter>> RefreshCharactersAsync(CancellationToken ct = default);
        Task<IReadOnlyList<LobbyWorld>> GetWorldsAsync(CancellationToken ct = default);
        Task CheckNameAsync(LobbyCharacter freeSlot, string name, string worldName, CancellationToken ct = default);
        Task CreateCharacterAsync(LobbyCharacter freeSlot, LobbyCharacterCreation creation, CancellationToken ct = default);
        Task DeleteCharacterAsync(LobbyCharacter character, CancellationToken ct = default);
        Task<MapSessionTicket> RenameAndSelectAsync(LobbyCharacter character, string newName, CancellationToken ct = default);
        Task<MapSessionTicket> SelectCharacterAsync(LobbyCharacter character, CancellationToken ct = default);
    }

    /// <summary>A character change the lobby confirmed (0x03), which some handoffs account for.</summary>
    public enum LobbyCharacterChange
    {
        Created,
        Deleted,
        Renamed,
    }

    /// <summary>
    /// What stands between the legacy client and the lobby: pol.exe on retail (<c>PolLobbyHandoff</c>), xiloader on
    /// LandSandBoat (<c>XiloaderLobbyHandoff</c>). The lobby conversation itself is the same; the handoff supplies the
    /// lobby key, the AuthCode and the install values, fills the header's +0x0C as each request leaves (the packet's
    /// MD5, or xiloader's session hash), runs xiloader's data channel where LandSandBoat needs it, and makes the map
    /// session key for the ticket.
    /// </summary>
    public interface ILobbyHandoff : IAsyncDisposable
    {
        /// <summary>A short name for logs ("PlayOnline", "xiloader").</summary>
        string Name { get; }

        /// <summary>Whether the handoff's own connections are still usable.</summary>
        bool IsConnected { get; }

        /// <summary>The 16-byte lobby session key the request bodies are hashed with (valid after <see cref="OpenAsync"/>).</summary>
        byte[] LobbyKey { get; }

        /// <summary>The client version sent in 0x26.</summary>
        string ClientVersion { get; }

        /// <summary>The FFXiMain.dll checksum in the selection digest.</summary>
        uint ClientChecksum { get; }

        /// <summary>Runs once the lobby connection is open, before 0x26: fetch the lobby key, or open the data channel.</summary>
        Task OpenAsync(CancellationToken ct);

        /// <summary>The 52-byte AuthCode token for 0x26.</summary>
        byte[] CreateAuthCode();

        /// <summary>Called on every request just before it is sent (and again if it is sent again).</summary>
        void PrepareRequest(Span<byte> packet);

        /// <summary>Called on every reply read.</summary>
        void OnReply(ReadOnlySpan<byte> packet);

        /// <summary>
        /// Runs after the character list request (0x1F) is sent and before the list (0x20) is read. <paramref name="resend"/>
        /// sends the request again.
        /// </summary>
        Task BeforeCharacterListAsync(Func<CancellationToken, Task> resend, CancellationToken ct);

        /// <summary>Runs after the selection (0x07) is sent and before its reply is read.</summary>
        Task AfterSelectRequestAsync(LobbyCharacter character, CancellationToken ct);

        /// <summary>Runs after the lobby confirmed a character change, before the list is read again.</summary>
        Task OnCharacterChangedAsync(LobbyCharacterChange change, CancellationToken ct);

        /// <summary>Called when the lobby refuses a request with 0x04.</summary>
        void OnRefused(byte command, int code);

        /// <summary>The game session's ticket for the selected character, from the 0x0B the lobby sent.</summary>
        MapSessionTicket CreateTicket(LobbyCharacter character, in LobbyNextLogin next, uint selectModifier);
    }

    /// <summary>
    /// A live lobby connection (port 54001), kept open while the player is in the character select screen. It speaks the
    /// lobby as the legacy client (FFXiMain.dll) does, for retail and LandSandBoat alike; the <see cref="ILobbyHandoff"/>
    /// plays the part of pol.exe or xiloader. Requests run one at a time.
    /// <para>
    /// Flow (spec v2.1.19 sections 4.5 and 7.2; LandSandBoat src/login/view_session.cpp): 0x26 -> 0x05 (the key modifier
    /// seed), 0x1F -> 0x20 (characters), 0x24 -> 0x23 (worlds); then on demand 0x22 / 0x21 / 0x14 / 0x28 -> 0x03 (each
    /// change followed by a fresh list), and 0x07 -> 0x0B, after which the server closes the connection. A refusal is 0x04
    /// with an error code and leaves the connection usable, so the screen can retry (3307 on retail: the character is still
    /// logged in). Every request that carries a session hash takes the next modifier (<see cref="LobbyKeyModifier"/>),
    /// refused ones included.
    /// </para>
    /// </summary>
    public sealed class LobbySession : ILobbyBackend, IDisposable
    {
        private readonly TcpClient _client;
        private readonly NetworkStream _stream;
        private readonly ILobbyHandoff _handoff;
        private readonly byte[] _lobbyLoginPacket = new byte[LobbyPackets.LobbyLoginSize];
        private readonly LobbyKeyModifier _modifier = new();
        private readonly SemaphoreSlim _gate = new(1, 1);
        private readonly Action<PacketLogEntry>? _log;
        private List<LobbyCharacter> _characters = new();
        private IReadOnlyList<LobbyWorld> _worlds = Array.Empty<LobbyWorld>();
        private bool _broken;
        private bool _disposed;

        private LobbySession(TcpClient client, ILobbyHandoff handoff, Action<PacketLogEntry>? log, TimeSpan timeout)
        {
            _client = client;
            _stream = client.GetStream();
            _handoff = handoff;
            _log = log;
            Timeout = timeout;
        }

        /// <summary>How long a reply may take before the request fails.</summary>
        public TimeSpan Timeout { get; }

        /// <summary>The pol.exe or xiloader side of this lobby.</summary>
        public ILobbyHandoff Handoff => _handoff;

        public IReadOnlyList<LobbyCharacter> Characters => _characters;

        /// <summary>The world list the login read.</summary>
        public IReadOnlyList<LobbyWorld> Worlds => _worlds;

        /// <summary>The account's expansions and features from 0x05 (not the installed ones 0x26 reports).</summary>
        public uint ServerExpansions { get; private set; }
        public uint ServerFeatures { get; private set; }

        /// <inheritdoc/>
        public uint? AccountExpansions => _expansionsKnown ? ServerExpansions : null;
        private bool _expansionsKnown;

        public bool IsConnected => !_broken && !_disposed && _client.Connected && _handoff.IsConnected;

        /// <summary>
        /// Connects to the lobby, lets the handoff open its side, logs in with 0x26 and reads the character and world
        /// lists. The session owns <paramref name="handoff"/> and disposes it. Throws <see cref="LobbyRequestException"/>
        /// when the lobby refuses the login (a version lock or maintenance answers 0x04).
        /// </summary>
        public static async Task<LobbySession> ConnectAsync(string host, int port, ILobbyHandoff handoff, Action<PacketLogEntry>? log = null,
            TimeSpan? timeout = null, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(host);
            ArgumentNullException.ThrowIfNull(handoff);
            var client = new TcpClient();
            LobbySession? session = null;
            try
            {
                long start = Stopwatch.GetTimestamp();
                await client.ConnectAsync(host, port, ct).ConfigureAwait(false);
                GordianLog.Info(LogCategories.Lobby, $"Lobby connected to {client.Client.RemoteEndPoint} in {Stopwatch.GetElapsedTime(start).TotalMilliseconds:0} ms.");
                session = new LobbySession(client, handoff, log, timeout ?? TimeSpan.FromSeconds(10));
                start = Stopwatch.GetTimestamp();
                await handoff.OpenAsync(ct).ConfigureAwait(false);
                GordianLog.Info(LogCategories.Lobby, $"Lobby handoff ({handoff.Name}) ready in {Stopwatch.GetElapsedTime(start).TotalMilliseconds:0} ms.");
                await session.LoginAsync(ct).ConfigureAwait(false);
                return session;
            }
            catch
            {
                if (session != null) await session.DisposeAsync().ConfigureAwait(false);
                else
                {
                    client.Dispose();
                    await handoff.DisposeAsync().ConfigureAwait(false);
                }
                throw;
            }
        }

        private async Task LoginAsync(CancellationToken ct)
        {
            LobbyClientPackets.WriteLobbyLogin(_lobbyLoginPacket, _handoff.CreateAuthCode(), _handoff.ClientVersion);
            await SendAsync(_lobbyLoginPacket, "GP_LOBBY_LOGIN", ct).ConfigureAwait(false);
            long sent = Stopwatch.GetTimestamp();
            // The AuthCode (+0x34..+0x73) carries the public address, so only the fields around it are logged.
            GordianLog.Debug(LogCategories.Lobby, $"Lobby login (0x26) sent ({_handoff.Name}): +0x00 {Convert.ToHexString(_lobbyLoginPacket, 0, 0x34)} ... +0x74 {Convert.ToHexString(_lobbyLoginPacket, 0x74, _lobbyLoginPacket.Length - 0x74)}");

            byte[] reply = await ReadAsync("login", ct).ConfigureAwait(false);
            byte command = LobbyPackets.ReadCommand(reply);
            GordianLog.Info(LogCategories.Lobby, $"Lobby login answered with 0x{command:X2} ({reply.Length} bytes) after {Stopwatch.GetElapsedTime(sent).TotalMilliseconds:0} ms.");
            if (command == LobbyCommand.ResponseError)
            {
                int code = LobbyPackets.ReadErrorCode(reply);
                GordianLog.Debug(LogCategories.Lobby, $"Lobby error reply: {Convert.ToHexString(reply)}");
                throw Broken(new LobbyRequestException("login", code,
                    $"The lobby refused the login: error code {code} ({LobbyErrorCode.Describe(code)}, FFXI-{LobbyErrorCode.DisplayCode(code)})."));
            }
            if (command != LobbyCommand.ResponseKey)
                throw Broken(new LobbyRequestException("login", 0, $"The lobby answered the login with 0x{command:X2} ({reply.Length} bytes) instead of 0x05."));
            (uint seed, uint expansions, uint features) = LobbyPackets.ReadKey(reply);
            _modifier.Seed(seed);
            ServerExpansions = expansions;
            _expansionsKnown = true;
            ServerFeatures = features;
            GordianLog.Info(LogCategories.Lobby, $"Lobby login accepted ({_handoff.Name}; account expansions 0x{expansions:X}, features 0x{features:X}).");

            await ReadCharacterListAsync(ct).ConfigureAwait(false);
            _worlds = await ReadWorldListAsync(ct).ConfigureAwait(false);
        }

        private async Task ReadCharacterListAsync(CancellationToken ct)
        {
            byte[] request = new byte[LobbyPackets.GetCharactersSize];
            LobbyClientPackets.WriteHashOnlyRequest(request, LobbyCommand.RequestGetChr, NextHash());
            await SendAsync(request, "GP_LOBBY_GET_CHR", ct).ConfigureAwait(false);
            await _handoff.BeforeCharacterListAsync(token => SendAsync(request, "GP_LOBBY_GET_CHR", token), ct).ConfigureAwait(false);
            byte[] list = await ExpectAsync("character list", LobbyCommand.RequestGetChr, LobbyCommand.ResponseChrInfo2, ct).ConfigureAwait(false);
            _characters = LobbyPackets.ParseCharacterList(list);
            GordianLog.Info(LogCategories.Lobby, $"Lobby character list: {_characters.Count} content id(s): " +
                string.Join(", ", _characters.Select(c => c.IsEmpty ? $"{c.Slot}=(free)" : $"{c.Slot}={c.Name}")));
        }

        private async Task<IReadOnlyList<LobbyWorld>> ReadWorldListAsync(CancellationToken ct)
        {
            byte[] request = new byte[LobbyPackets.QueryWorldListSize];
            LobbyClientPackets.WriteHashOnlyRequest(request, LobbyCommand.RequestQueryWorldList, NextHash());
            await SendAsync(request, "GP_LOBBY_QUERY_WORLD_LIST", ct).ConfigureAwait(false);
            byte[] reply = await ExpectAsync("world list", LobbyCommand.RequestQueryWorldList, LobbyCommand.ResponseWorldList, ct).ConfigureAwait(false);
            return LobbyPackets.ParseWorldList(reply);
        }

        public async Task<IReadOnlyList<LobbyCharacter>> RefreshCharactersAsync(CancellationToken ct = default)
        {
            await _gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                EnsureUsable();
                await ReadCharacterListAsync(ct).ConfigureAwait(false);
                return _characters;
            }
            finally { _gate.Release(); }
        }

        public async Task<IReadOnlyList<LobbyWorld>> GetWorldsAsync(CancellationToken ct = default)
        {
            await _gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                EnsureUsable();
                _worlds = await ReadWorldListAsync(ct).ConfigureAwait(false);
                return _worlds;
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
            byte[] request = new byte[LobbyPackets.CreateCharacterPreSize];
            LobbyClientPackets.WriteCheckName(request, freeSlot.ContentId, name, worldName, NextHash());
            await SendAsync(request, "GP_LOBBY_CREATE_CHR_PRE", ct).ConfigureAwait(false);
            await ExpectAsync("name check", LobbyCommand.RequestCreateChrPre, LobbyCommand.ResponseOk, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// The name check (0x22) and the creation (0x21), then the refreshed list (capture #5's order). LandSandBoat drops
        /// the connection when it refuses 0x21 (bad race, size, face or nation), so a refused creation can leave the
        /// session unusable.
        /// </summary>
        public async Task CreateCharacterAsync(LobbyCharacter freeSlot, LobbyCharacterCreation creation, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(freeSlot);
            await _gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                await CheckNameUnlockedAsync(freeSlot, creation.Name, freeSlot.WorldName, ct).ConfigureAwait(false);
                byte[] request = new byte[LobbyPackets.CreateCharacterSize];
                LobbyClientPackets.WriteCreateCharacter(request, freeSlot.ContentId, creation, NextHash());
                await SendAsync(request, "GP_LOBBY_CREATE_CHR", ct).ConfigureAwait(false);
                await ExpectAsync("character creation", LobbyCommand.RequestCreateChr, LobbyCommand.ResponseOk, ct).ConfigureAwait(false);
                GordianLog.Info(LogCategories.Lobby, $"Created character '{creation.Name}' (race {creation.Race}, face {creation.CombinedFace}, job {creation.MainJob}, size {creation.Size}, nation {creation.Nation}).");
                await _handoff.OnCharacterChangedAsync(LobbyCharacterChange.Created, ct).ConfigureAwait(false);
                await ReadCharacterListAsync(ct).ConfigureAwait(false);
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
                byte[] request = new byte[LobbyPackets.DeleteCharacterSize];
                LobbyClientPackets.WriteDeleteCharacter(request, character.ContentId, character.ServerId, NextHash());
                await SendAsync(request, "GP_LOBBY_DELETE_CHR", ct).ConfigureAwait(false);
                await ExpectAsync("character deletion", LobbyCommand.RequestDeleteChr, LobbyCommand.ResponseOk, ct).ConfigureAwait(false);
                GordianLog.Info(LogCategories.Lobby, $"Deleted character '{character.Name}' (content id {character.ContentId}).");
                await _handoff.OnCharacterChangedAsync(LobbyCharacterChange.Deleted, ct).ConfigureAwait(false);
                await ReadCharacterListAsync(ct).ConfigureAwait(false);
            }
            finally { _gate.Release(); }
        }

        /// <summary>Renames a character the server flagged for renaming (0x28 -> 0x03), then selects it under the new name.</summary>
        public async Task<MapSessionTicket> RenameAndSelectAsync(LobbyCharacter character, string newName, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(character);
            await _gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                EnsureUsable();
                byte[] request = new byte[LobbyPackets.RenameCharacterSize];
                LobbyClientPackets.WriteRenameCharacter(request, character.ContentId, character.ServerId, newName, NextHash());
                await SendAsync(request, "GP_LOBBY_RENAME_CHR", ct).ConfigureAwait(false);
                await ExpectAsync("rename", LobbyCommand.RequestRenameChr, LobbyCommand.ResponseOk, ct).ConfigureAwait(false);
                await _handoff.OnCharacterChangedAsync(LobbyCharacterChange.Renamed, ct).ConfigureAwait(false);
                return await SelectUnlockedAsync(character with { Name = newName, RenameRequired = false }, ct).ConfigureAwait(false);
            }
            finally { _gate.Release(); }
        }

        public async Task<MapSessionTicket> SelectCharacterAsync(LobbyCharacter character, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(character);
            await _gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                return await SelectUnlockedAsync(character, ct).ConfigureAwait(false);
            }
            finally { _gate.Release(); }
        }

        /// <summary>0x07 -> 0x0B; the handoff turns the 0x0B into the game session's ticket.</summary>
        private async Task<MapSessionTicket> SelectUnlockedAsync(LobbyCharacter character, CancellationToken ct)
        {
            EnsureUsable();
            // LandSandBoat looks the id and name up together and drops the connection on a mismatch.
            uint selectModifier = _modifier.Take();
            byte[] hash = PolHashes.LobbySessionHash(_handoff.LobbyKey, selectModifier);
            byte[] request = new byte[LobbyPackets.SelectCharacterSize];
            LobbyClientPackets.WriteSelectCharacter(request, character.ContentId, character.ServerId, character.Name, hash,
                LobbyClientPackets.AuthCodeField(_lobbyLoginPacket), _handoff.ClientChecksum);
            GordianLog.Debug(LogCategories.Lobby, $"Selecting '{character.Name}' (content id {character.ContentId}, 0x07)...");
            await SendAsync(request, "GP_LOBBY_CHAR_SELECT", ct).ConfigureAwait(false);
            await _handoff.AfterSelectRequestAsync(character, ct).ConfigureAwait(false);

            byte[] reply = await ExpectAsync("select", LobbyCommand.RequestSelectChr, LobbyCommand.ResponseNextLogin, ct).ConfigureAwait(false);
            if (!LobbyPackets.TryParseNextLogin(reply, out var next))
                throw Broken(new LobbyRequestException("select", 0, $"The lobby's 0x0B was {reply.Length} bytes, too short for a map server handoff."));
            // The server closes the lobby connection after 0x0B.
            _broken = true;

            var ticket = _handoff.CreateTicket(character, next, selectModifier);
            GordianLog.Info(LogCategories.Lobby, $"Character '{ticket.CharacterName}' selected (content id {character.ContentId}): map server {ticket.ZoneIp}:{ticket.ZonePort}.");
            return ticket;
        }

        private byte[] NextHash() => PolHashes.LobbySessionHash(_handoff.LobbyKey, _modifier.Take());

        /// <summary>Reads a reply: the expected command, or a refusal (0x04) thrown with its code; the connection stays usable after a refusal.</summary>
        private async Task<byte[]> ExpectAsync(string request, byte sent, byte expected, CancellationToken ct)
        {
            byte[] reply = await ReadAsync(request, ct).ConfigureAwait(false);
            byte command = LobbyPackets.ReadCommand(reply);
            if (command == LobbyCommand.ResponseError)
            {
                int code = LobbyPackets.ReadErrorCode(reply);
                _handoff.OnRefused(sent, code);
                throw new LobbyRequestException(request, code,
                    $"Lobby {request} failed: the server returned error code {code} ({LobbyErrorCode.Describe(code)}, FFXI-{LobbyErrorCode.DisplayCode(code)}).");
            }
            if (command != expected)
                throw Broken(new LobbyRequestException(request, 0, $"Lobby {request}: the server sent 0x{command:X2} instead of 0x{expected:X2}."));
            return reply;
        }

        private void EnsureUsable()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_broken || !_handoff.IsConnected) throw new LobbyRequestException("request", 0, "The lobby connection was closed; log in again.");
        }

        private LobbyRequestException Broken(LobbyRequestException ex)
        {
            _broken = true;
            return ex;
        }

        private async Task SendAsync(byte[] packet, string name, CancellationToken ct)
        {
            _handoff.PrepareRequest(packet);
            try
            {
                await _stream.WriteAsync(packet, ct).ConfigureAwait(false);
                await _stream.FlushAsync(ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException)
            {
                throw Broken(new LobbyRequestException(name, 0, $"Lobby connection lost while sending {name}.", ex));
            }
            Log(PacketDirection.Outbound, packet, name);
        }

        /// <summary>Reads one packet: the u32 size, then the rest.</summary>
        private async Task<byte[]> ReadAsync(string request, CancellationToken ct)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(Timeout);
            try
            {
                byte[] header = new byte[4];
                await _stream.ReadExactlyAsync(header, cts.Token).ConfigureAwait(false);
                uint size = LobbyPackets.ReadSize(header);
                if (size < 12 || size > 0x4000)
                    throw Broken(new LobbyRequestException(request, 0, $"Lobby {request}: invalid packet size {size}."));
                byte[] packet = new byte[size];
                header.CopyTo(packet, 0);
                await _stream.ReadExactlyAsync(packet.AsMemory(4), cts.Token).ConfigureAwait(false);
                Log(PacketDirection.Inbound, packet, PacketName(packet[8]));
                _handoff.OnReply(packet);
                return packet;
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                throw Broken(new LobbyRequestException(request, 0, $"Lobby {request}: no reply (timed out after {Timeout.TotalSeconds:0} s)."));
            }
            catch (EndOfStreamException ex)
            {
                throw Broken(new LobbyRequestException(request, 0, $"Lobby {request}: the server closed the connection without responding.", ex));
            }
            catch (Exception ex) when (ex is IOException or SocketException)
            {
                throw Broken(new LobbyRequestException(request, 0, $"Lobby {request}: connection lost ({ex.Message}).", ex));
            }
        }

        private static string PacketName(byte command) => command switch
        {
            LobbyCommand.ResponseOk => "GP_LOBBY_OK",
            LobbyCommand.ResponseError => "GP_LOBBY_ERROR",
            LobbyCommand.ResponseKey => "GP_LOBBY_VERSION_REPLY",
            LobbyCommand.ResponseNextLogin => "GP_LOBBY_ZONE_TICKET",
            LobbyCommand.ResponseChrInfo2 => "GP_LOBBY_CHAR_INFO",
            LobbyCommand.ResponseWorldList => "GP_LOBBY_WORLD_LIST",
            _ => $"GP_LOBBY_0x{command:X2}",
        };

        private void Log(PacketDirection direction, byte[] packet, string name)
        {
            if (_log == null) return;
            _log(new PacketLogEntry
            {
                Timestamp = DateTime.UtcNow,
                Direction = direction,
                PacketId = packet.Length > 8 ? packet[8] : (byte)0,
                PacketName = name,
                Size = packet.Length,
                RawBytes = (byte[])packet.Clone(),
            });
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { _stream.Dispose(); } catch { }
            _client.Dispose();
            _gate.Dispose();
            try { _handoff.DisposeAsync().AsTask().GetAwaiter().GetResult(); } catch { }
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
