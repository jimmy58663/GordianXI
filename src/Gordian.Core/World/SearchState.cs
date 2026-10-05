// src/Gordian.Core/World/SearchState.cs
using System;
using System.Collections.Generic;
using System.Linq;
using Gordian.Core.Network.Search;

namespace Gordian.Core.World
{
    /// <summary>
    /// What the search (cache) server last answered: the Auction House category lists and item price histories, the last
    /// <c>/sea</c> result, the party and linkshell member lists and the search comments read. The Auction House window and the
    /// party list window read it; <see cref="SearchService"/> fills it.
    /// </summary>
    public sealed class SearchState
    {
        private readonly object _lock = new();
        private readonly Dictionary<byte, AuctionListResult> _auctionLists = new();
        private readonly Dictionary<(ushort ItemId, bool Stack), AuctionHistoryResult> _auctionHistory = new();
        private readonly Dictionary<uint, SearchPlayersResult> _linkshells = new();
        private readonly Dictionary<uint, string?> _comments = new();

        public event Action<byte>? AuctionListUpdated;
        public event Action<ushort, bool>? AuctionHistoryUpdated;
        public event Action? SearchResultUpdated;
        public event Action? PartyListUpdated;
        public event Action<uint>? LinkshellListUpdated;
        public event Action<uint>? CommentUpdated;

        /// <summary>The last <c>/sea</c> result, or null.</summary>
        public SearchPlayersResult? LastSearch { get; private set; }

        /// <summary>The last party (and alliance) member list, or null.</summary>
        public SearchPlayersResult? PartyList { get; private set; }

        /// <summary>The Auction House list of a category, or null before it was fetched.</summary>
        public AuctionListResult? GetAuctionList(byte category)
        {
            lock (_lock) return _auctionLists.TryGetValue(category, out var r) ? r : null;
        }

        /// <summary>The categories whose list was fetched.</summary>
        public IReadOnlyList<byte> AuctionCategories
        {
            get { lock (_lock) return _auctionLists.Keys.OrderBy(k => k).ToArray(); }
        }

        /// <summary>An item's price history, or null before it was fetched.</summary>
        public AuctionHistoryResult? GetAuctionHistory(ushort itemId, bool stack)
        {
            lock (_lock) return _auctionHistory.TryGetValue((itemId, stack), out var r) ? r : null;
        }

        /// <summary>The members of a linkshell by its id, or null before it was fetched.</summary>
        public SearchPlayersResult? GetLinkshellList(uint linkshellId)
        {
            lock (_lock) return _linkshells.TryGetValue(linkshellId, out var r) ? r : null;
        }

        /// <summary>A player's search comment; null when unknown or empty.</summary>
        public string? GetComment(uint playerId)
        {
            lock (_lock) return _comments.TryGetValue(playerId, out var c) ? c : null;
        }

        public void SetAuctionList(AuctionListResult result)
        {
            lock (_lock) _auctionLists[result.Category] = result;
            AuctionListUpdated?.Invoke(result.Category);
        }

        public void SetAuctionHistory(AuctionHistoryResult result)
        {
            lock (_lock) _auctionHistory[(result.ItemId, result.Stack)] = result;
            AuctionHistoryUpdated?.Invoke(result.ItemId, result.Stack);
        }

        public void SetSearch(SearchPlayersResult result)
        {
            lock (_lock) LastSearch = result;
            SearchResultUpdated?.Invoke();
        }

        public void SetPartyList(SearchPlayersResult result)
        {
            lock (_lock) PartyList = result;
            PartyListUpdated?.Invoke();
        }

        public void SetLinkshellList(uint linkshellId, SearchPlayersResult result)
        {
            lock (_lock) _linkshells[linkshellId] = result;
            LinkshellListUpdated?.Invoke(linkshellId);
        }

        public void SetComment(uint playerId, string? comment)
        {
            lock (_lock) _comments[playerId] = comment;
            CommentUpdated?.Invoke(playerId);
        }

        /// <summary>Forgets the price caches (the Auction House prices change by the minute).</summary>
        public void ClearAuction()
        {
            lock (_lock)
            {
                _auctionLists.Clear();
                _auctionHistory.Clear();
            }
        }
    }
}
