// src/Gordian.Core/Network/Search/SearchService.cs
using System;
using System.Buffers.Binary;
using System.Threading;
using System.Threading.Tasks;
using Gordian.Core.Diagnostics;
using Gordian.Core.Network.Packets;
using Gordian.Core.World;

namespace Gordian.Core.Network.Search
{
    /// <summary>
    /// The session's door to the search (cache) server: finds the server (the host the session plays on and the default port,
    /// unless configured), gathers what a request needs from the session (the party's group id from S2C 0x0E1, a worn linkshell's
    /// id from its item) and keeps the answers in <see cref="SearchState"/>.
    /// </summary>
    public sealed class SearchService
    {
        private readonly SocialPacketModule? _social;
        private readonly PartyState? _party;
        private readonly InventoryState? _inventory;
        private readonly Func<string, int, SearchClient> _clientFactory;
        private SearchClient? _client;
        private bool _explicit;

        public SearchService(SocialPacketModule? social = null, PartyState? party = null, InventoryState? inventory = null,
            Func<string, int, SearchClient>? clientFactory = null)
        {
            _social = social;
            _party = party;
            _inventory = inventory;
            _clientFactory = clientFactory ?? ((host, port) => new SearchClient(host, port, Transport));
        }

        /// <summary>The transport new clients use; the plain TCP transport when null. A seam for tests and proxies.</summary>
        public ISearchTransport? Transport { get; set; }

        /// <summary>The cached answers.</summary>
        public SearchState State { get; } = new();

        /// <summary>How long a request waits for the world server's group id answer.</summary>
        public TimeSpan GroupIdTimeout { get; set; } = TimeSpan.FromSeconds(3);

        /// <summary>True when a search server address is known (configured, or defaulted to the world server's host).</summary>
        public bool IsConfigured => _client != null;

        /// <summary>The search server address, or null.</summary>
        public string? Host => _client?.Host;

        /// <summary>The search server port, or 0.</summary>
        public int Port => _client?.Port ?? 0;

        /// <summary>
        /// Sets the search server address (a server profile that runs it on another host or port). It replaces a default and
        /// is not replaced by later defaults.
        /// </summary>
        public void Configure(string host, int port = SearchClient.DefaultPort)
        {
            _client = _clientFactory(host, port);
            _explicit = true;
        }

        /// <summary>
        /// Sets the address to the host the world connection goes to, with the default port, unless <see cref="Configure"/> set one:
        /// LandSandBoat runs the search server on the same machine unless its settings say otherwise.
        /// </summary>
        public void ConfigureDefault(string host)
        {
            if (_explicit || string.IsNullOrWhiteSpace(host)) return;
            if (_client != null && _client.Host == host && _client.Port == SearchClient.DefaultPort) return;
            _client = _clientFactory(host, SearchClient.DefaultPort);
        }

        private SearchClient Client =>
            _client ?? throw new InvalidOperationException("No search server address is known yet; connect the session or call Configure.");

        /// <summary>Fetches an Auction House category list into <see cref="SearchState"/>.</summary>
        public async Task<AuctionListResult> RefreshAuctionListAsync(byte category, ReadOnlyMemory<byte> sortKeys = default, CancellationToken cancellationToken = default)
        {
            var result = await Client.GetAuctionListAsync(category, sortKeys, cancellationToken).ConfigureAwait(false);
            State.SetAuctionList(result);
            return result;
        }

        /// <summary>Fetches an item's price history into <see cref="SearchState"/>; null when the server did not answer.</summary>
        public async Task<AuctionHistoryResult?> RefreshAuctionHistoryAsync(ushort itemId, bool stack, CancellationToken cancellationToken = default)
        {
            var result = await Client.GetAuctionHistoryAsync(itemId, stack, cancellationToken).ConfigureAwait(false);
            if (result != null) State.SetAuctionHistory(result);
            return result;
        }

        /// <summary>Runs a <c>/sea</c> search into <see cref="SearchState.LastSearch"/>.</summary>
        public async Task<SearchPlayersResult> SearchAsync(SearchQuery query, CancellationToken cancellationToken = default)
        {
            var result = await Client.SearchAsync(query, cancellationToken).ConfigureAwait(false);
            State.SetSearch(result);
            return result;
        }

        /// <summary>Reads a player's search comment into <see cref="SearchState"/>; null when the player has none.</summary>
        public async Task<string?> GetCommentAsync(uint playerId, CancellationToken cancellationToken = default)
        {
            string? comment = await Client.GetSearchCommentAsync(playerId, cancellationToken).ConfigureAwait(false);
            State.SetComment(playerId, comment);
            return comment;
        }

        /// <summary>
        /// Fetches the party member list: asks the world server for the party's group id (C2S 0x078), then the search server for the
        /// members. Null when the character is not in a party or the group id never came. Alliance members are not requested: the
        /// world server's group id answer carries the party id only.
        /// </summary>
        public async Task<SearchPlayersResult?> RefreshPartyListAsync(CancellationToken cancellationToken = default)
        {
            uint? groupId = await RequestGroupIdAsync(cancellationToken).ConfigureAwait(false);
            if (groupId is null or 0) return null;

            var result = await Client.GetPartyListAsync(groupId.Value, 0, cancellationToken).ConfigureAwait(false);
            State.SetPartyList(result);
            return result;
        }

        /// <summary>
        /// Fetches the member list of the linkshell worn in slot 1 or 2 (the id comes from the linkshell item's extra data).
        /// Null when no linkshell is worn there or its id is unknown.
        /// </summary>
        public async Task<SearchPlayersResult?> RefreshLinkshellListAsync(int slot, CancellationToken cancellationToken = default)
        {
            uint id = GetWornLinkshellId(slot);
            if (id == 0) return null;
            return await RefreshLinkshellListByIdAsync(id, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>Fetches the member list of a linkshell by its id.</summary>
        public async Task<SearchPlayersResult> RefreshLinkshellListByIdAsync(uint linkshellId, CancellationToken cancellationToken = default)
        {
            var result = await Client.GetLinkshellListAsync(linkshellId, cancellationToken).ConfigureAwait(false);
            State.SetLinkshellList(linkshellId, result);
            return result;
        }

        /// <summary>
        /// The id of the linkshell worn in slot 1 or 2: the first word of the linkshell item's extra data (LandSandBoat's
        /// <c>Exdata::Linkshell.GroupId</c>); 0 when none.
        /// </summary>
        public uint GetWornLinkshellId(int slot)
        {
            var location = _party?.GetLinkshellItem(slot);
            if (location == null || _inventory == null) return 0;
            if (!_inventory.GetContainer(location.Container).TryGetItem(location.ItemIndex, out var item)) return 0;
            byte[]? ext = item.ExtData;
            return ext is { Length: >= 4 } ? BinaryPrimitives.ReadUInt32LittleEndian(ext) : 0;
        }

        private async Task<uint?> RequestGroupIdAsync(CancellationToken cancellationToken)
        {
            if (_social == null) return null;

            var answered = new TaskCompletionSource<uint>(TaskCreationOptions.RunContinuationsAsynchronously);
            void OnId(uint id) => answered.TrySetResult(id);
            _social.Social.GroupIdReceived += OnId;
            try
            {
                await _social.RequestGroupIdAsync().ConfigureAwait(false);
                var finished = await Task.WhenAny(answered.Task, Task.Delay(GroupIdTimeout, cancellationToken)).ConfigureAwait(false);
                if (finished != answered.Task)
                {
                    GordianLog.Debug("SEARCH", "The world server did not answer the party group id request.");
                    return null;
                }
                return await answered.Task.ConfigureAwait(false);
            }
            finally
            {
                _social.Social.GroupIdReceived -= OnId;
            }
        }
    }
}
