// src/Gordian.Core/Network/Lobby/MapSessionTicket.cs
using System;
using System.Collections.Generic;
using System.Linq;
using Gordian.Core.Network.LandSandBoat;

namespace Gordian.Core.Network.Lobby
{
    /// <summary>
    /// The map session a lobby login hands over: the character, the map server, its key and how to speak to it. Both the
    /// LandSandBoat (<c>LsbLoginClient</c>, <c>XiloaderLobbyHandoff</c>) and the retail (<c>PolLobbyHandoff</c>) lobbies make one.
    /// </summary>
    public sealed class MapSessionTicket
    {
        public uint AccountId { get; init; }
        public uint CharacterId { get; init; }
        public string CharacterName { get; init; } = string.Empty;
        public string ZoneIp { get; init; } = "127.0.0.1";
        public int ZonePort { get; init; } = 54230;
        public byte[] SessionHash { get; init; } = Array.Empty<byte>();

        /// <summary>
        /// The old-style (<see cref="LsbKeyStyle.Legacy"/>) map session key: the key sent in 0xA2 with LandSandBoat's
        /// old per-operation increments. The key the server really uses is one of <see cref="KeyCandidates"/>.
        /// </summary>
        public byte[] BlowfishKey { get; init; } = Array.Empty<byte>();

        /// <summary>Every map session key the server may use, newest LandSandBoat scheme first (see <see cref="LsbSessionKeys"/>).</summary>
        public IReadOnlyList<LsbSessionKeyCandidate> KeyCandidates { get; init; } = Array.Empty<LsbSessionKeyCandidate>();

        /// <summary>The lobby key counter when 0xA2 was sent (the counter key's bytes 16-19 are this + 3); 0 if never set.</summary>
        public uint LobbyKeyCounter { get; init; }

        /// <summary>The first map datagram's builder for the map session, or null for LandSandBoat's (retail login sets it).</summary>
        public MapLoginDatagramBuilder? LoginDatagramBuilder { get; init; }

        /// <summary>Whether the map server is retail's (spec v2.1.24 section 4.6): see <c>SessionNetworkManager.RetailMapProtocol</c>.</summary>
        public bool RetailMapProtocol { get; init; }

        /// <summary>
        /// Our public IPv4 address as the retail login saw it, in network byte order, for the map header's State word 2;
        /// null for LandSandBoat. See <c>SessionNetworkManager.RetailClientAddress</c>.
        /// </summary>
        public byte[]? RetailClientAddress { get; init; }

        /// <summary>The search server the lobby named (retail's 0x0B does), or null to use the map server's host.</summary>
        public string? SearchIp { get; init; }

        /// <summary>The search server's port, used with <see cref="SearchIp"/>.</summary>
        public int SearchPort { get; init; }

        /// <summary>The candidates for the packet crypto suite, or the single <see cref="BlowfishKey"/> when there are none.</summary>
        public IReadOnlyList<Gordian.Core.Network.Crypto.SessionKeyCandidate> CryptoKeyCandidates =>
            KeyCandidates.Count > 0
                ? KeyCandidates.Select(c => c.ToCryptoCandidate()).ToList()
                : new[] { new Gordian.Core.Network.Crypto.SessionKeyCandidate(nameof(LsbKeyStyle.Legacy), BlowfishKey) };
    }
}
