// src/Gordian.Core/Network/Retail/PolLobbyHandoff.cs
using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Gordian.Core.Network.LandSandBoat;
using Gordian.Core.Network.Lobby;

namespace Gordian.Core.Network.Retail
{
    /// <summary>
    /// What the retail login needs from the PlayOnline login for the lobby: the Core session's greeting time, K8 and
    /// routing token (the AuthCode's inputs), the installed client version (patch.cfg) and FFXiMain.dll's checksum.
    /// </summary>
    public sealed record RetailLobbyLogin(uint GreetingTimestamp, byte[] SessionKey8, byte[] RoutingToken, string ClientVersion, uint ClientChecksum);

    /// <summary>
    /// The pol.exe side of a retail lobby (<c>ffxi00.pol.com:54001</c>): the lobby key from the assigned <c>:51220</c>
    /// server (requested once the lobby connection is open, as retail does), the AuthCode token, the install values, the
    /// packet MD5 left in the header's +0x0C, and the map key for the ticket. Retail has no data channel.
    /// <para>
    /// Protocol referenced from the GordianXI retail login spec, docs/network/retail-login-spec.md v2.1.19, sections 4.5,
    /// 4.6, 5.5 and 5.6 (test vectors 5, 8, 11 and 13).
    /// </para>
    /// </summary>
    public sealed class PolLobbyHandoff : ILobbyHandoff
    {
        public const string Host = "ffxi00.pol.com";
        public const int Port = 54001;

        private readonly RetailLobbyLogin _login;
        private readonly Func<CancellationToken, Task<byte[]>> _lobbyKey;
        private byte[]? _key;

        /// <param name="lobbyKey">Fetches the 16-byte lobby session key (the <c>:51220</c> service 0 request).</param>
        public PolLobbyHandoff(RetailLobbyLogin login, Func<CancellationToken, Task<byte[]>> lobbyKey)
        {
            _login = login ?? throw new ArgumentNullException(nameof(login));
            _lobbyKey = lobbyKey ?? throw new ArgumentNullException(nameof(lobbyKey));
        }

        public string Name => "PlayOnline";

        public bool IsConnected => true;

        public byte[] LobbyKey => _key ?? throw new InvalidOperationException("The lobby key has not been fetched yet.");

        public string ClientVersion => _login.ClientVersion;

        public uint ClientChecksum => _login.ClientChecksum;

        public async Task OpenAsync(CancellationToken ct)
        {
            byte[] key = await _lobbyKey(ct).ConfigureAwait(false);
            if (key.Length != 16) throw new RetailLoginException("lobby", "The lobby session key is not 16 bytes.");
            _key = key;
        }

        /// <summary>
        /// The AuthCode token. Its inputs, D28 and H16 are logged (spec v2.1.22 section 9 item 8): they are this login's
        /// session keys, worthless once the session ends, and are what a 3301 (session-correlation failure) is checked against.
        /// </summary>
        public byte[] CreateAuthCode()
        {
            byte[] preimage = new byte[PolAuthCode.PreimageLength];
            PolAuthCode.WritePreimage(preimage, LobbyKey, _login.GreetingTimestamp, _login.SessionKey8);
            Diagnostics.GordianLog.Debug("RETAIL_LOGIN",
                $"AuthCode inputs: lobby key {Convert.ToHexString(LobbyKey)}, greeting time 0x{_login.GreetingTimestamp:X8}, " +
                $"K8 {Convert.ToHexString(_login.SessionKey8)}; D28 {Convert.ToHexString(preimage)}, H16 {Convert.ToHexString(System.Security.Cryptography.MD5.HashData(preimage))}.");
            return PolAuthCode.Generate(LobbyKey, _login.GreetingTimestamp, _login.SessionKey8, _login.RoutingToken);
        }

        /// <summary>The client's packet MD5 stays in the header (the packet writers seal every request).</summary>
        public void PrepareRequest(Span<byte> packet) => LobbyClientPackets.Seal(packet);

        public void OnReply(ReadOnlySpan<byte> packet)
        {
            if (!LobbyClientPackets.Verify(packet))
                Diagnostics.GordianLog.Warning("RETAIL_LOGIN", $"Lobby packet 0x{packet[8]:X2} failed its MD5 check.");
        }

        public Task BeforeCharacterListAsync(Func<CancellationToken, Task> resend, CancellationToken ct) => Task.CompletedTask;

        public Task AfterSelectRequestAsync(LobbyCharacter character, CancellationToken ct) => Task.CompletedTask;

        public Task OnCharacterChangedAsync(LobbyCharacterChange change, CancellationToken ct) => Task.CompletedTask;

        public void OnRefused(byte command, int code) { }

        /// <summary>
        /// The ticket: the map server from 0x0B (first octet first), the 20-byte raw map key (the map session MD5s it), and a
        /// builder for the first map datagram whose <c>UniqueNo</c> is 0x0B's +0x20 and whose ticket follows the key across
        /// zone changes (+2 each, spec section 5.6). The search server is the one 0x0B names.
        /// </summary>
        public MapSessionTicket CreateTicket(LobbyCharacter character, in LobbyNextLogin next, uint selectModifier)
        {
            byte[] lobbyKey = LobbyKey;
            uint uniqueNo = next.ServerId;
            return new MapSessionTicket
            {
                CharacterId = character.ServerId,
                CharacterName = next.Name.Length > 0 ? next.Name : character.Name,
                ZoneIp = new IPAddress(next.ServerAddress).ToString(),
                ZonePort = (int)next.ServerPort,
                BlowfishKey = RetailMapPackets.MapRawKey(lobbyKey, selectModifier),
                LoginDatagramBuilder = zoneChanges => RetailMapPackets.BuildFirstDatagram(uniqueNo,
                    RetailMapPackets.MapKey(lobbyKey, unchecked(selectModifier + 2u * (uint)zoneChanges))),
                SearchIp = next.CacheAddress != 0 ? new IPAddress(next.CacheAddress).ToString() : null,
                SearchPort = (int)next.CachePort,
                RetailMapProtocol = true,
                RetailClientAddress = RetailMapPackets.ClientAddressFromRoutingToken(_login.RoutingToken),
            };
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
