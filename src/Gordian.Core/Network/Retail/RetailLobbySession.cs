// src/Gordian.Core/Network/Retail/RetailLobbySession.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Gordian.Core.Diagnostics;
using Gordian.Core.Network.LandSandBoat;

namespace Gordian.Core.Network.Retail
{
    /// <summary>
    /// What the retail lobby login needs from the PlayOnline login: the Core session's greeting time, K8 and routing
    /// token (the AuthCode's inputs), the installed client version (patch.cfg) and FFXiMain.dll's checksum.
    /// </summary>
    public sealed record RetailLobbyLogin(uint GreetingTimestamp, byte[] SessionKey8, byte[] RoutingToken, string ClientVersion, uint ClientChecksum);

    /// <summary>
    /// A live connection to the retail FFXI lobby (<c>ffxi00.pol.com:54001</c>), kept open while the player is in the
    /// character select screen. Requests run one at a time.
    /// <para>
    /// Flow (spec v2.1.19 sections 3, 4.5 and 7.2): connect, fetch the lobby session key from the assigned <c>:51220</c>
    /// server, 0x26 -> 0x05 (the modifier seed), 0x1F -> 0x20 (characters), 0x24 -> 0x23 (worlds); then on demand
    /// 0x22 / 0x21 / 0x14 / 0x28 -> 0x03, and 0x07 -> 0x0B, after which the server closes the connection. A refusal is
    /// 0x04 with an error code; the connection stays usable, so the player can retry (3307, the character still logged
    /// in, clears about a minute after a client was killed). Every request that carries a session hash takes the next
    /// modifier (<see cref="RetailLobbyModifier"/>), refused ones included.
    /// </para>
    /// <para>
    /// Protocol referenced from the GordianXI retail login spec, docs/network/retail-login-spec.md v2.1.19.
    /// </para>
    /// </summary>
    public sealed class RetailLobbySession : ILobbyBackend, IDisposable
    {
        private readonly TcpClient _client;
        private readonly NetworkStream _stream;
        private readonly byte[] _lobbyKey;
        private readonly RetailLobbyLogin _login;
        private readonly byte[] _lobbyLoginPacket = new byte[LobbyPackets.LobbyLoginSize];
        private readonly RetailLobbyModifier _modifier = new();
        private readonly SemaphoreSlim _gate = new(1, 1);
        private readonly Action<PacketLogEntry>? _log;
        private List<LobbyCharacter> _characters = new();
        private IReadOnlyList<LobbyWorld> _worlds = Array.Empty<LobbyWorld>();
        private bool _broken;
        private bool _disposed;

        private RetailLobbySession(TcpClient client, byte[] lobbyKey, RetailLobbyLogin login, Action<PacketLogEntry>? log, TimeSpan timeout)
        {
            _client = client;
            _stream = client.GetStream();
            _lobbyKey = lobbyKey;
            _login = login;
            _log = log;
            Timeout = timeout;
        }

        /// <summary>How long a reply may take before the request fails.</summary>
        public TimeSpan Timeout { get; }

        public IReadOnlyList<LobbyCharacter> Characters => _characters;

        /// <summary>The world list the login read.</summary>
        public IReadOnlyList<LobbyWorld> Worlds => _worlds;

        /// <summary>The account's expansions and feature flags from 0x05 (not the installed ones 0x26 reports).</summary>
        public uint ServerExpansions { get; private set; }
        public uint ServerFeatures { get; private set; }

        public bool IsConnected => !_broken && !_disposed && _client.Connected;

        /// <summary>
        /// Connects, fetches the lobby key (<paramref name="lobbyKey"/>, the <c>:51220</c> service 0 request, made after
        /// the connection as retail does), logs in with 0x26 and reads the character and world lists. Throws
        /// <see cref="LobbyRequestException"/> when the lobby refuses the login.
        /// </summary>
        public static async Task<RetailLobbySession> ConnectAsync(RetailLobbyLogin login, Func<CancellationToken, Task<byte[]>> lobbyKey,
            string host = RetailLobbyPackets.Host, int port = RetailLobbyPackets.Port, Action<PacketLogEntry>? log = null,
            TimeSpan? timeout = null, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(login);
            ArgumentNullException.ThrowIfNull(lobbyKey);
            var client = new TcpClient();
            try
            {
                await client.ConnectAsync(host, port, ct).ConfigureAwait(false);
                byte[] key = await lobbyKey(ct).ConfigureAwait(false);
                if (key.Length != 16) throw new RetailLoginException("lobby", "The lobby session key is not 16 bytes.");
                var session = new RetailLobbySession(client, key, login, log, timeout ?? TimeSpan.FromSeconds(15));
                try
                {
                    await session.LoginAsync(ct).ConfigureAwait(false);
                    return session;
                }
                catch
                {
                    session.Dispose();
                    throw;
                }
            }
            catch
            {
                client.Dispose();
                throw;
            }
        }

        private async Task LoginAsync(CancellationToken ct)
        {
            byte[] authCode = PolAuthCode.Generate(_lobbyKey, _login.GreetingTimestamp, _login.SessionKey8, _login.RoutingToken);
            RetailLobbyPackets.WriteLobbyLogin(_lobbyLoginPacket, authCode, _login.ClientVersion);
            await SendAsync(_lobbyLoginPacket, "GP_LOBBY_LOGIN", ct).ConfigureAwait(false);

            byte[] reply = await ReadAsync("login", ct).ConfigureAwait(false);
            byte command = LobbyPackets.ReadCommand(reply);
            if (command == LobbyCommand.ResponseError) throw Broken(LobbyRequestException.FromServer("login", LobbyPackets.ReadErrorCode(reply)));
            if (command != LobbyCommand.ResponseKey)
                throw Broken(new LobbyRequestException("login", 0, $"The lobby answered the login with 0x{command:X2} instead of 0x05."));
            (uint seed, uint expansions, uint features) = LobbyPackets.ReadKey(reply);
            _modifier.Seed(seed);
            ServerExpansions = expansions;
            ServerFeatures = features;
            GordianLog.Info("RETAIL_LOGIN", $"Lobby login accepted (account expansions 0x{expansions:X}, features 0x{features:X}).");

            await ReadCharacterListAsync(ct).ConfigureAwait(false);
            _worlds = await ReadWorldListAsync(ct).ConfigureAwait(false);
        }

        private async Task ReadCharacterListAsync(CancellationToken ct)
        {
            byte[] request = new byte[LobbyPackets.GetCharactersSize];
            RetailLobbyPackets.WriteHashOnlyRequest(request, LobbyCommand.RequestGetChr, NextHash());
            await SendAsync(request, "GP_LOBBY_GET_CHR", ct).ConfigureAwait(false);
            byte[] list = await ExpectAsync("character list", LobbyCommand.ResponseChrInfo2, ct).ConfigureAwait(false);
            _characters = LobbyPackets.ParseCharacterList(list);
            GordianLog.Info("RETAIL_LOGIN", $"Lobby character list: {_characters.Count} content id(s): " +
                string.Join(", ", _characters.Select(c => c.IsEmpty ? $"{c.Slot}=(free)" : $"{c.Slot}={c.Name}")));
        }

        private async Task<IReadOnlyList<LobbyWorld>> ReadWorldListAsync(CancellationToken ct)
        {
            byte[] request = new byte[LobbyPackets.QueryWorldListSize];
            RetailLobbyPackets.WriteHashOnlyRequest(request, LobbyCommand.RequestQueryWorldList, NextHash());
            await SendAsync(request, "GP_LOBBY_QUERY_WORLD_LIST", ct).ConfigureAwait(false);
            byte[] reply = await ExpectAsync("world list", LobbyCommand.ResponseWorldList, ct).ConfigureAwait(false);
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
            RetailLobbyPackets.WriteCheckName(request, freeSlot.ContentId, name, worldName, NextHash());
            await SendAsync(request, "GP_LOBBY_CREATE_CHR_PRE", ct).ConfigureAwait(false);
            await ExpectAsync("name check", LobbyCommand.ResponseOk, ct).ConfigureAwait(false);
        }

        /// <summary>The name check (0x22) and the creation (0x21), then the refreshed list (spec section 4.5.5 capture #5's order).</summary>
        public async Task CreateCharacterAsync(LobbyCharacter freeSlot, LobbyCharacterCreation creation, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(freeSlot);
            await _gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                await CheckNameUnlockedAsync(freeSlot, creation.Name, freeSlot.WorldName, ct).ConfigureAwait(false);
                byte[] request = new byte[LobbyPackets.CreateCharacterSize];
                RetailLobbyPackets.WriteCreateCharacter(request, freeSlot.ContentId, creation, NextHash());
                await SendAsync(request, "GP_LOBBY_CREATE_CHR", ct).ConfigureAwait(false);
                await ExpectAsync("character creation", LobbyCommand.ResponseOk, ct).ConfigureAwait(false);
                GordianLog.Info("RETAIL_LOGIN", $"Created character '{creation.Name}'.");
                await ReadCharacterListAsync(ct).ConfigureAwait(false);
            }
            finally { _gate.Release(); }
        }

        public async Task DeleteCharacterAsync(LobbyCharacter character, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(character);
            await _gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                EnsureUsable();
                byte[] request = new byte[LobbyPackets.DeleteCharacterSize];
                RetailLobbyPackets.WriteDeleteCharacter(request, character.ContentId, character.ServerId, NextHash());
                await SendAsync(request, "GP_LOBBY_DELETE_CHR", ct).ConfigureAwait(false);
                await ExpectAsync("character deletion", LobbyCommand.ResponseOk, ct).ConfigureAwait(false);
                GordianLog.Info("RETAIL_LOGIN", $"Deleted character '{character.Name}'.");
                await ReadCharacterListAsync(ct).ConfigureAwait(false);
            }
            finally { _gate.Release(); }
        }

        public async Task<LsbSessionTicket> RenameAndSelectAsync(LobbyCharacter character, string newName, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(character);
            await _gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                EnsureUsable();
                byte[] request = new byte[LobbyPackets.RenameCharacterSize];
                RetailLobbyPackets.WriteRenameCharacter(request, character.ContentId, character.ServerId, newName, NextHash());
                await SendAsync(request, "GP_LOBBY_RENAME_CHR", ct).ConfigureAwait(false);
                await ExpectAsync("rename", LobbyCommand.ResponseOk, ct).ConfigureAwait(false);
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

        /// <summary>
        /// 0x07 -> 0x0B. The ticket carries the map server, the 20-byte raw map key (the map session MD5s it) and a builder
        /// for the first map datagram, whose ticket follows the key across zone changes (+2 each, spec section 5.6).
        /// </summary>
        private async Task<LsbSessionTicket> SelectUnlockedAsync(LobbyCharacter character, CancellationToken ct)
        {
            EnsureUsable();
            uint selectModifier = _modifier.Take();
            byte[] hash = PolHashes.LobbySessionHash(_lobbyKey, selectModifier);
            byte[] request = new byte[RetailLobbyPackets.SelectCharacterSize];
            RetailLobbyPackets.WriteSelectCharacter(request, character.ContentId, character.ServerId, character.Name, hash,
                RetailLobbyPackets.AuthCodeField(_lobbyLoginPacket), _login.ClientChecksum);
            await SendAsync(request, "GP_LOBBY_CHAR_SELECT", ct).ConfigureAwait(false);

            byte[] reply = await ExpectAsync("select", LobbyCommand.ResponseNextLogin, ct).ConfigureAwait(false);
            if (!RetailLobbyPackets.TryParseMapHandoff(reply, out var handoff))
                throw Broken(new LobbyRequestException("select", 0, $"The lobby's 0x0B was {reply.Length} bytes, too short for a map server handoff."));
            // The server closes the lobby connection after 0x0B.
            _broken = true;

            byte[] lobbyKey = _lobbyKey;
            uint uniqueNo = handoff.UniqueNo;
            GordianLog.Info("RETAIL_LOGIN", $"Character '{character.Name}' selected: map server {handoff.MapServer}:{handoff.MapPort}, search server {handoff.SearchServer}:{handoff.SearchPort}.");
            return new LsbSessionTicket
            {
                CharacterId = character.ServerId,
                CharacterName = character.Name,
                ZoneIp = handoff.MapServer.ToString(),
                ZonePort = handoff.MapPort,
                BlowfishKey = RetailLobbyPackets.MapRawKey(lobbyKey, selectModifier),
                LoginDatagramBuilder = zoneChanges => RetailMapPackets.BuildFirstDatagram(uniqueNo,
                    RetailLobbyPackets.MapKey(lobbyKey, unchecked(selectModifier + 2u * (uint)zoneChanges))),
                SearchIp = handoff.SearchServer.ToString(),
                SearchPort = handoff.SearchPort,
            };
        }

        private byte[] NextHash() => PolHashes.LobbySessionHash(_lobbyKey, _modifier.Take());

        /// <summary>Reads a reply: the expected command, or a refusal (0x04) thrown with its code; the connection stays usable after a refusal.</summary>
        private async Task<byte[]> ExpectAsync(string request, byte expected, CancellationToken ct)
        {
            byte[] reply = await ReadAsync(request, ct).ConfigureAwait(false);
            byte command = LobbyPackets.ReadCommand(reply);
            if (command == LobbyCommand.ResponseError)
            {
                int code = LobbyPackets.ReadErrorCode(reply);
                throw new LobbyRequestException(request, code,
                    $"Lobby {request} failed: the server returned error code {code} (FFXI-{LobbyErrorCode.DisplayCode(code)}).");
            }
            if (command != expected)
                throw Broken(new LobbyRequestException(request, 0, $"Lobby {request}: the server sent 0x{command:X2} instead of 0x{expected:X2}."));
            return reply;
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

        private async Task SendAsync(byte[] packet, string name, CancellationToken ct)
        {
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

        /// <summary>Reads one packet: the u32 size, then the rest. A packet whose MD5 does not check out is logged and used.</summary>
        private async Task<byte[]> ReadAsync(string request, CancellationToken ct)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(Timeout);
            try
            {
                byte[] header = new byte[4];
                await _stream.ReadExactlyAsync(header, cts.Token).ConfigureAwait(false);
                uint size = LobbyPackets.ReadSize(header);
                if (size < LobbyPackets.HeaderSize || size > 0x1000)
                    throw Broken(new LobbyRequestException(request, 0, $"Lobby {request}: invalid packet size {size}."));
                byte[] packet = new byte[size];
                header.CopyTo(packet, 0);
                await _stream.ReadExactlyAsync(packet.AsMemory(4), cts.Token).ConfigureAwait(false);
                if (!RetailLobbyPackets.Verify(packet))
                    GordianLog.Warning("RETAIL_LOGIN", $"Lobby packet 0x{packet[8]:X2} failed its MD5 check.");
                Log(PacketDirection.Inbound, packet, $"GP_LOBBY_0x{packet[8]:X2}");
                return packet;
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                throw Broken(new LobbyRequestException(request, 0, $"Lobby {request}: no reply (timed out after {Timeout.TotalSeconds:0} s)."));
            }
            catch (EndOfStreamException ex)
            {
                throw Broken(new LobbyRequestException(request, 0, $"Lobby {request}: the server closed the connection.", ex));
            }
            catch (Exception ex) when (ex is IOException or SocketException)
            {
                throw Broken(new LobbyRequestException(request, 0, $"Lobby {request}: connection lost ({ex.Message}).", ex));
            }
        }

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
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
