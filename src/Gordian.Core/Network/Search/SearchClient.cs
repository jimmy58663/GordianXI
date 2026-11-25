// src/Gordian.Core/Network/Search/SearchClient.cs
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Gordian.Core.Diagnostics;

namespace Gordian.Core.Network.Search
{
    /// <summary>One open connection to the search server: whole frames out and in.</summary>
    public interface ISearchConnection : IAsyncDisposable
    {
        /// <summary>Sends one frame.</summary>
        Task SendAsync(byte[] frame, CancellationToken cancellationToken);

        /// <summary>Reads the next whole frame (by its length word); null when the server closed the connection.</summary>
        Task<byte[]?> ReadFrameAsync(CancellationToken cancellationToken);
    }

    /// <summary>Opens connections to the search server; a seam for tests.</summary>
    public interface ISearchTransport
    {
        Task<ISearchConnection> ConnectAsync(string host, int port, CancellationToken cancellationToken);
    }

    /// <summary>The plain TCP transport: one socket, frames cut by the length word at their start.</summary>
    public sealed class TcpSearchTransport : ISearchTransport
    {
        public async Task<ISearchConnection> ConnectAsync(string host, int port, CancellationToken cancellationToken)
        {
            var client = new TcpClient { NoDelay = true };
            try
            {
                await client.ConnectAsync(host, port, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                client.Dispose();
                throw;
            }
            return new TcpConnection(client);
        }

        private sealed class TcpConnection : ISearchConnection
        {
            private readonly TcpClient _client;
            private readonly NetworkStream _stream;

            public TcpConnection(TcpClient client)
            {
                _client = client;
                _stream = client.GetStream();
            }

            public async Task SendAsync(byte[] frame, CancellationToken cancellationToken)
            {
                await _stream.WriteAsync(frame, cancellationToken).ConfigureAwait(false);
                await _stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            public async Task<byte[]?> ReadFrameAsync(CancellationToken cancellationToken)
            {
                var length = new byte[2];
                if (!await ReadExactAsync(length, cancellationToken).ConfigureAwait(false)) return null;
                int total = BinaryPrimitives.ReadUInt16LittleEndian(length);
                if (total < SearchFrame.HeaderSize + SearchFrame.TrailerSize || total > 4096)
                {
                    throw new InvalidDataException($"A search server frame of {total} bytes is not valid.");
                }

                var frame = new byte[total];
                length.CopyTo(frame, 0);
                if (!await ReadExactAsync(frame.AsMemory(2), cancellationToken).ConfigureAwait(false)) return null;
                return frame;
            }

            private async Task<bool> ReadExactAsync(Memory<byte> buffer, CancellationToken cancellationToken)
            {
                int read = 0;
                while (read < buffer.Length)
                {
                    int n = await _stream.ReadAsync(buffer.Slice(read), cancellationToken).ConfigureAwait(false);
                    if (n == 0) return false;
                    read += n;
                }
                return true;
            }

            public ValueTask DisposeAsync()
            {
                _stream.Dispose();
                _client.Dispose();
                return ValueTask.CompletedTask;
            }
        }
    }

    /// <summary>The decrypted answers to one request.</summary>
    /// <param name="Contents">The plain frame bytes from the frame start to the hash, one array per answer packet, in order.</param>
    /// <param name="Complete">True when the last packet carried the final flag; false when the wait ran out first.</param>
    public sealed record SearchExchange(IReadOnlyList<byte[]> Contents, bool Complete);

    /// <summary>An Auction House category list.</summary>
    public sealed record AuctionListResult(byte Category, int Total, IReadOnlyList<AuctionListItem> Items, bool Complete);

    /// <summary>An item's Auction House price history.</summary>
    public sealed record AuctionHistoryResult(ushort ItemId, bool Stack, uint Price, ushort Category, IReadOnlyList<AuctionHistoryEntry> Sales);

    /// <summary>The players an answer lists (a search, a party or a linkshell).</summary>
    public sealed record SearchPlayersResult(int Total, IReadOnlyList<SearchEntity> Players, bool Complete);

    /// <summary>
    /// A client for the search (cache) server, which LandSandBoat serves on its own TCP port (default 54002) next to the world
    /// UDP stream. It opens a connection per request, sends one encrypted request (see <see cref="SearchFrame"/>) and reads the
    /// answer packets until the final flag (asking for each next page on retail, <see cref="RequestsNextPages"/>), so a request
    /// never blocks the world session. It serves the Auction House item
    /// lists and price histories, <c>/sea</c> searches, search comments and the party and linkshell member lists.
    /// <para>
    /// Protocol referenced from LandSandBoat (https://github.com/LandSandBoat/server), <c>src/search/</c> and
    /// <c>settings/default/network.lua</c> (<c>SEARCH_PORT</c>); XiPackets (https://github.com/atom0s/XiPackets),
    /// <c>cache/README.md</c> for the server's role.
    /// </para>
    /// </summary>
    public sealed class SearchClient
    {
        /// <summary>LandSandBoat's <c>SEARCH_PORT</c>.</summary>
        public const int DefaultPort = 54002;

        private readonly ISearchTransport _transport;

        public SearchClient(string host, int port = DefaultPort, ISearchTransport? transport = null)
        {
            if (string.IsNullOrWhiteSpace(host)) throw new ArgumentException("The search server host is empty.", nameof(host));
            Host = host;
            Port = port;
            _transport = transport ?? new TcpSearchTransport();
        }

        public string Host { get; }
        public int Port { get; }

        /// <summary>How long a request waits for its answers; the server closes an idle connection after 10 seconds.</summary>
        public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(8);

        /// <summary>
        /// Ask for each further page of an answer that is not flagged final, as the retail client does (#643): after a page
        /// without the final flag it sends a <see cref="SearchRequestType.NextPage"/> request on the same connection, and the
        /// server answers with the next page; the page after the last one it serves comes flagged final (with no entries when
        /// the list was cut short, as retail cuts <c>/sea all</c> at 40 players). Set for retail's search server. LandSandBoat
        /// sends every page of an answer after the one request, the last flagged final, and takes request type 0x10 only as the
        /// Auction House's "more", so it is never asked.
        /// </summary>
        public bool RequestsNextPages { get; set; }

        /// <summary>The most next-page requests one exchange sends (retail served two pages of <c>/sea all</c> and an empty third).</summary>
        public const int MaxNextPageRequests = 8;

        /// <summary>
        /// Accept an answer whose hash fits no known layout when a key deciphers a header that answers the request (the answer
        /// type LandSandBoat sends for it, plausible data length). Set for retail's search server (#578) as a fallback: since
        /// round 2 its answers verify with the seed they end with (<see cref="SearchFrame.TryDecryptResponse"/>), so this only
        /// matters for an answer that fits no known layout. LandSandBoat's answers must verify.
        /// </summary>
        public bool AcceptUnverifiedAnswers { get; set; }

        /// <summary>
        /// Sends a request body (see <see cref="SearchRequestBuilder"/>) and collects its answers: until one comes flagged final,
        /// asking for each next page when <see cref="RequestsNextPages"/> is set.
        /// </summary>
        public async Task<SearchExchange> ExchangeAsync(byte[] body, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(body);
            byte requestType = body[0x0B];
            byte[] frame = SearchFrame.EncryptRequest(body, NewSeed(), out var answerKey);

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(Timeout);
            var contents = new List<byte[]>();
            int nextPageRequests = 0;
            try
            {
                await using var connection = await _transport.ConnectAsync(Host, Port, timeout.Token).ConfigureAwait(false);
                await connection.SendAsync(frame, timeout.Token).ConfigureAwait(false);

                while (true)
                {
                    byte[]? answer = await connection.ReadFrameAsync(timeout.Token).ConfigureAwait(false);
                    if (answer == null)
                    {
                        GordianLog.Debug(LogCategories.Search, $"The search server closed the connection after {contents.Count} answers to request type 0x{requestType:X2}.");
                        return new SearchExchange(contents, false);
                    }

                    byte[] wire = (byte[])answer.Clone();
                    byte[] plain;
                    if (SearchFrame.TryDecryptResponse(answer, answerKey, out var content))
                    {
                        plain = content.ToArray();
                    }
                    else
                    {
                        // #578: say how the answer differs from LandSandBoat's (sizes, header, the key and hash rules that fit;
                        // never payload or key bytes), and keep it when it is verified another way or plausibly answers.
                        var diagnosis = SearchAnswerDiagnosis.Diagnose(wire, answerKey, requestType);
                        // Only an answer whose header answers this request: a stray frame that verifies under its own trailer is not one.
                        bool keep = diagnosis.Plausible && (diagnosis.Verified || AcceptUnverifiedAnswers);
                        GordianLog.Warn(LogCategories.Search,
                            $"A search server answer failed the LandSandBoat hash check ({diagnosis.Describe(requestType, frame.Length)}); " +
                            (keep ? "kept." : "dropped."));
                        if (!keep) continue;
                        plain = diagnosis.Content!;
                    }
                    contents.Add(plain);
                    var header = new SearchAnswerHeader(plain);
                    // Header fields only (#578: what retail's answers say about paging); never the payload.
                    if (header.IsValid)
                    {
                        GordianLog.Debug(LogCategories.Search,
                            $"Search answer {contents.Count} to request type 0x{requestType:X2}: {answer.Length} bytes, type byte 0x{plain[0x0B]:X2}, " +
                            $"flags 0x{plain[0x0A]:X2} (final {header.IsFinal}), data length {header.DataLength}, total {header.Total}.");
                    }
                    if (header.IsFinal) return new SearchExchange(contents, true);
                    if (!RequestsNextPages) continue;
                    if (nextPageRequests == MaxNextPageRequests)
                    {
                        GordianLog.Debug(LogCategories.Search, $"Search request type 0x{requestType:X2}: no final answer after {nextPageRequests} next-page requests.");
                        return new SearchExchange(contents, false);
                    }

                    // #643: retail asks for the next page itself (the 2026-11-25 /sea all capture).
                    nextPageRequests++;
                    frame = SearchFrame.EncryptRequest(SearchRequestBuilder.BuildNextPage(body), NewSeed(), out answerKey);
                    await connection.SendAsync(frame, timeout.Token).ConfigureAwait(false);
                }
            }
            catch (InvalidDataException ex)
            {
                // A length word out of range: the frames after an answer whose length word does not cover it all (#578).
                GordianLog.Warn(LogCategories.Search, $"Search request type 0x{requestType:X2}: {ex.Message} (after {contents.Count} answers); the connection was dropped.");
                return new SearchExchange(contents, false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                GordianLog.Debug(LogCategories.Search, $"Search request type 0x{requestType:X2} timed out after {contents.Count} answers.");
                return new SearchExchange(contents, false);
            }
        }

        private static uint NewSeed() => (uint)RandomNumberGenerator.GetInt32(1, int.MaxValue);

        /// <summary>The items of an Auction House category, with how many single and stack listings each has.</summary>
        public async Task<AuctionListResult> GetAuctionListAsync(byte category, ReadOnlyMemory<byte> sortKeys = default, CancellationToken cancellationToken = default)
        {
            var exchange = await ExchangeAsync(SearchRequestBuilder.BuildAuctionList(category, sortKeys.Span), cancellationToken).ConfigureAwait(false);
            var items = new List<AuctionListItem>();
            int total = 0;
            foreach (byte[] content in exchange.Contents)
            {
                var page = new AuctionListPage(content);
                if (!page.IsValid) continue;
                total = page.Header.Total;
                for (int i = 0; i < page.Count; i++) items.Add(page.GetItem(i));
            }
            return new AuctionListResult(category, total, items, exchange.Complete);
        }

        /// <summary>An item's price history (up to 10 sales); null when the server did not answer.</summary>
        public async Task<AuctionHistoryResult?> GetAuctionHistoryAsync(ushort itemId, bool stack, CancellationToken cancellationToken = default)
        {
            var exchange = await ExchangeAsync(SearchRequestBuilder.BuildAuctionHistory(itemId, stack), cancellationToken).ConfigureAwait(false);
            if (exchange.Contents.Count == 0) return null;

            var page = new AuctionHistoryPage(exchange.Contents[0]);
            if (!page.IsValid) return null;
            var sales = new List<AuctionHistoryEntry>(page.Count);
            for (int i = 0; i < page.Count; i++) sales.Add(page.GetEntry(i));
            return new AuctionHistoryResult(page.ItemId, stack, page.Price, page.Category, sales);
        }

        /// <summary>A <c>/sea</c> or <c>/sea all</c> search.</summary>
        public async Task<SearchPlayersResult> SearchAsync(SearchQuery query, CancellationToken cancellationToken = default)
        {
            var exchange = await ExchangeAsync(SearchRequestBuilder.BuildSearch(query), cancellationToken).ConfigureAwait(false);
            return ReadPlayers(exchange);
        }

        /// <summary>The members of a party (and alliance); the server needs the party id from S2C 0x0E1.</summary>
        public async Task<SearchPlayersResult> GetPartyListAsync(uint partyId, uint allianceId = 0, CancellationToken cancellationToken = default)
        {
            var exchange = await ExchangeAsync(SearchRequestBuilder.BuildGroupList(partyId, allianceId, 0, 0), cancellationToken).ConfigureAwait(false);
            return ReadPlayers(exchange);
        }

        /// <summary>The members of a linkshell, by the linkshell id of the worn linkshell item.</summary>
        public async Task<SearchPlayersResult> GetLinkshellListAsync(uint linkshellId, CancellationToken cancellationToken = default)
        {
            var exchange = await ExchangeAsync(SearchRequestBuilder.BuildGroupList(0, 0, linkshellId, 0), cancellationToken).ConfigureAwait(false);
            return ReadPlayers(exchange);
        }

        /// <summary>A player's search comment; null when the server sent none (LandSandBoat sends nothing for an empty comment).</summary>
        public async Task<string?> GetSearchCommentAsync(uint playerId, CancellationToken cancellationToken = default)
        {
            var exchange = await ExchangeAsync(SearchRequestBuilder.BuildSearchComment(playerId), cancellationToken).ConfigureAwait(false);
            if (exchange.Contents.Count == 0) return null;
            var page = new SearchCommentPage(exchange.Contents[0]);
            return page.IsValid ? page.GetText() : null;
        }

        /// <summary>How many entries of a search have their fields logged at Debug (#578).</summary>
        private const int LoggedEntries = 3;

        private static SearchPlayersResult ReadPlayers(SearchExchange exchange)
        {
            var players = new List<SearchEntity>();
            int total = 0;
            for (int pageNumber = 0; pageNumber < exchange.Contents.Count; pageNumber++)
            {
                var page = new SearchEntityPage(exchange.Contents[pageNumber]);
                if (!page.IsValid) continue;
                total = page.Header.Total;
                int position = SearchEntityPage.EntriesOffset;
                int onPage = 0;
                while (true)
                {
                    // #578 rounds 3-5: retail writes a field code LandSandBoat does not (0x07, after Level, now read as 8 bits).
                    // The first entries' field order (with any other unknown code's width worked out) and what was read (no names).
                    string fields = players.Count < LoggedEntries ? page.DescribeEntryFields(position) : string.Empty;
                    if (!page.TryReadEntity(ref position, out var player)) break;
                    onPage++;
                    if (fields.Length > 0)
                    {
                        GordianLog.Debug(LogCategories.Search, $"Search entry {players.Count + 1} fields: {fields}; nation {player.Nation}, rank {player.Rank}, " +
                            $"job {player.MainJob}/{player.SubJob}, level {player.MainLevel}/{player.SubLevel}, race {player.Race}, field 07 {player.Unknown07}, flags 0x{player.Flags1:X4}, area {player.ZoneId}.");
                    }
                    players.Add(player);
                }
                GordianLog.Debug(LogCategories.Search, $"Search answer {pageNumber + 1}: {onPage} entries read, {players.Count} so far of {total}.");
            }
            return new SearchPlayersResult(total, players, exchange.Complete);
        }
    }
}
