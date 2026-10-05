// src/Gordian.Core/Network/Search/SearchPackets.cs
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;

namespace Gordian.Core.Network.Search
{
    #region Enums and models

    /// <summary>
    /// The request type of a search server packet (the byte at offset 0x0B of the request). An answer carries the same value
    /// with bit 7 set; every answer to a search request has type 0.
    /// Values referenced from LandSandBoat (https://github.com/LandSandBoat/server), <c>src/search/search_handler.h</c>
    /// (<c>TCPREQUESTTYPE</c>).
    /// </summary>
    public enum SearchRequestType : byte
    {
        /// <summary><c>/sea all</c>: search every zone.</summary>
        SearchAll = 0x00,
        /// <summary>The party, alliance or linkshell member list.</summary>
        GroupList = 0x02,
        /// <summary><c>/sea</c>: search one area.</summary>
        Search = 0x03,
        /// <summary>Price history of single items.</summary>
        AuctionHistorySingle = 0x05,
        /// <summary>Price history of stacks.</summary>
        AuctionHistoryStack = 0x06,
        /// <summary>A player's search comment.</summary>
        SearchComment = 0x08,
        /// <summary>The next part of an Auction House item list (LandSandBoat sends the whole list again).</summary>
        AuctionRequestMore = 0x10,
        /// <summary>The Auction House item list of a category.</summary>
        AuctionRequest = 0x15
    }

    /// <summary>
    /// The field types of the bit stream that carries a search (request) or a player entry (answer). Each is a 5-bit code.
    /// Values referenced from LandSandBoat (https://github.com/LandSandBoat/server), <c>src/search/enums/search_type.h</c>.
    /// </summary>
    public enum SearchFieldType : byte
    {
        Name = 0x00,
        Area = 0x01,
        Nation = 0x02,
        Job = 0x03,
        Level = 0x04,
        Race = 0x05,
        Flags1 = 0x06,
        Id = 0x08,
        Party = 0x0A,
        Linkshell = 0x0B,
        Friend = 0x0C,
        LinkshellRank = 0x0D,
        Unknown0E = 0x0E,
        Rank = 0x10,
        Comment = 0x11,
        Linkshell2 = 0x13,
        Flags2 = 0x16,
        Language = 0x17
    }

    /// <summary>
    /// The flags of a search entry's <c>Flags1</c> (and <c>Flags2</c>, which LandSandBoat sets to the same value), also the mask
    /// bits of a search's flag filter.
    /// Values referenced from LandSandBoat (https://github.com/LandSandBoat/server), <c>src/search/data_loader.cpp</c>.
    /// </summary>
    [Flags]
    public enum SearchFlags : uint
    {
        None = 0,
        Mentor = 0x00000001,
        PartyLeader = 0x00000008,
        HasComment = 0x00000010,
        Away = 0x00000100,
        Disconnecting = 0x00000800,
        InParty = 0x00002000,
        /// <summary>The player hides their job, level, race and rank (the entry carries none of them).</summary>
        Anonymous = 0x00004000,
        /// <summary>The player is seeking a party (the invite flag).</summary>
        SeekingParty = 0x00008000,
        Muted = 0x20000000
    }

    /// <summary>One player of a search, party or linkshell answer.</summary>
    /// <param name="Name">The character name (at most 15 characters).</param>
    /// <param name="ZoneId">The zone the player is in (10 bits).</param>
    /// <param name="Nation">The home nation: 0 San d'Oria, 1 Bastok, 2 Windurst; absent for an anonymous player.</param>
    /// <param name="MainJob">The main job id (absent for an anonymous player).</param>
    /// <param name="SubJob">The sub job id.</param>
    /// <param name="MainLevel">The main level.</param>
    /// <param name="SubLevel">The sub level.</param>
    /// <param name="Race">The race (4 bits).</param>
    /// <param name="Rank">The nation rank.</param>
    /// <param name="Flags1">The flag word, see <see cref="SearchFlags"/>.</param>
    /// <param name="Flags2">The second flag word (LandSandBoat repeats <paramref name="Flags1"/>).</param>
    /// <param name="CharacterId">The low 20 bits of the character id.</param>
    /// <param name="CommentType">The search comment type; 0 when the player has none.</param>
    /// <param name="Languages">The languages the player speaks (a bit mask).</param>
    /// <param name="LinkshellRank1">Linkshell 1 rank: 1 holder, 2 pearlsack, 3 pearl (linkshell lists only).</param>
    /// <param name="LinkshellRank2">Linkshell 2 rank.</param>
    /// <param name="LinkshellId1">Linkshell 1 id (linkshell lists only).</param>
    /// <param name="LinkshellId2">Linkshell 2 id.</param>
    public sealed record SearchEntity(
        string Name, ushort ZoneId, byte Nation, byte MainJob, byte SubJob, byte MainLevel, byte SubLevel, byte Race, byte Rank,
        uint Flags1, uint Flags2, uint CharacterId, uint CommentType, ushort Languages,
        byte LinkshellRank1, byte LinkshellRank2, uint LinkshellId1, uint LinkshellId2)
    {
        /// <summary>The flags as <see cref="SearchFlags"/>.</summary>
        public SearchFlags Flags => (SearchFlags)Flags1;

        /// <summary>True when the player hides their job information (<see cref="SearchFlags.Anonymous"/>).</summary>
        public bool IsAnonymous => (Flags1 & (uint)SearchFlags.Anonymous) != 0;
    }

    /// <summary>One item of an Auction House category list: how many single and stack listings it has.</summary>
    /// <param name="ItemId">The item.</param>
    /// <param name="SingleCount">The number of single-item listings.</param>
    /// <param name="StackCount">The number of stack listings.</param>
    public readonly record struct AuctionListItem(ushort ItemId, uint SingleCount, uint StackCount);

    /// <summary>One sale in an item's price history.</summary>
    /// <param name="Price">The sale price in gil.</param>
    /// <param name="Data">The sale's second word (LandSandBoat: the sale date, a Unix time).</param>
    /// <param name="Seller">The seller's name.</param>
    /// <param name="Buyer">The buyer's name.</param>
    public readonly record struct AuctionHistoryEntry(uint Price, uint Data, string Seller, string Buyer);

    /// <summary>The filters of a <c>/sea</c> search. Fields left null are not sent.</summary>
    public sealed class SearchQuery
    {
        /// <summary>Search every zone (<c>/sea all</c>) instead of only <see cref="Areas"/>.</summary>
        public bool AllAreas { get; set; }

        /// <summary>A name or name prefix (up to 15 ASCII characters).</summary>
        public string? Name { get; set; }

        /// <summary>The zones to search (up to 15) when <see cref="AllAreas"/> is false.</summary>
        public List<ushort> Areas { get; } = new();

        /// <summary>The home nation: 0 San d'Oria, 1 Bastok, 2 Windurst.</summary>
        public byte? Nation { get; set; }

        /// <summary>A main job id (1-22).</summary>
        public byte? Job { get; set; }

        /// <summary>A level range.</summary>
        public (byte Min, byte Max)? Level { get; set; }

        /// <summary>A race id (0-7).</summary>
        public byte? Race { get; set; }

        /// <summary>A nation rank range.</summary>
        public (byte Min, byte Max)? Rank { get; set; }

        /// <summary>The 16-bit flag filter (<see cref="SearchFlags"/>), e.g. seeking a party.</summary>
        public ushort? Flags1 { get; set; }

        /// <summary>The 32-bit flag filter.</summary>
        public uint? Flags2 { get; set; }

        /// <summary>A search comment type.</summary>
        public uint? CommentType { get; set; }

        /// <summary>A linkshell id (<c>/sea linkshell</c>).</summary>
        public uint? LinkshellId { get; set; }

        /// <summary>Only friends (<c>/sea friend</c>); a field with no value.</summary>
        public bool FriendsOnly { get; set; }
    }

    #endregion

    #region Bit stream (MSB first)

    /// <summary>
    /// Reads the bit stream of a search answer: values are read most significant bit first, bits taken from each byte from the
    /// high end, as LandSandBoat's <c>packBitsLE</c> writes them.
    /// </summary>
    internal ref struct SearchBitReader
    {
        private readonly ReadOnlySpan<byte> _data;
        private int _position;

        public SearchBitReader(ReadOnlySpan<byte> data)
        {
            _data = data;
            _position = 0;
        }

        public int BitsLeft => (_data.Length * 8) - _position;

        public uint Read(int bits)
        {
            ulong value = 0;
            for (int i = 0; i < bits; i++)
            {
                int p = _position + i;
                int bit = (p >> 3) < _data.Length ? (_data[p >> 3] >> (7 - (p & 7))) & 1 : 0;
                value = (value << 1) | (uint)bit;
            }
            _position += bits;
            return (uint)value;
        }
    }

    /// <summary>
    /// Writes the bit stream of a search request: values are written most significant bit first, the same bit order the server
    /// reads a request in (<c>unpackBitsLE</c>).
    /// </summary>
    internal sealed class SearchBitWriter
    {
        private readonly List<byte> _bytes = new();
        private int _position;

        public int BitCount => _position;

        public void Write(ulong value, int bits)
        {
            for (int i = 0; i < bits; i++)
            {
                int bit = (int)((value >> (bits - 1 - i)) & 1);
                if ((_position >> 3) >= _bytes.Count) _bytes.Add(0);
                if (bit != 0) _bytes[_position >> 3] |= (byte)(0x80 >> (_position & 7));
                _position++;
            }
        }

        /// <summary>The written bits, padded with zeros to a whole byte.</summary>
        public byte[] ToArray() => _bytes.ToArray();
    }

    #endregion

    #region Request builders

    /// <summary>
    /// Builds the plain request bodies of the search server (8 header bytes, then the payload; <see cref="SearchFrame"/> wraps
    /// them for the wire). Offsets in the docs are from the start of the body, as LandSandBoat reads them.
    /// <para>
    /// Request layouts referenced from LandSandBoat (https://github.com/LandSandBoat/server), <c>src/search/search_handler.cpp</c>
    /// (<c>HandleGroupListRequest</c>, <c>HandleAuctionHouseRequest</c>, <c>HandleAuctionHouseHistory</c>,
    /// <c>HandleSearchComment</c>, <c>_HandleSearchRequest</c>); XiPackets (https://github.com/atom0s/XiPackets),
    /// <c>cache/README.md</c> lists the request families without layouts. The bytes 0x08-0x0A and 0x0C-0x0F, which the server
    /// ignores, are the content length and zero.
    /// </para>
    /// </summary>
    public static class SearchRequestBuilder
    {
        /// <summary>The most areas a search names.</summary>
        public const int MaxAreas = 15;

        private static byte[] NewBody(SearchRequestType type, int length)
        {
            int rounded = (length + 7) & ~7;
            var body = new byte[rounded];
            BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(8, 2), (ushort)length);
            body[0x0B] = (byte)type;
            return body;
        }

        /// <summary>
        /// A group list request: the party (and alliance) id for a party member list, or the linkshell ids for a linkshell member
        /// list; the server uses the party when either party id is non-zero. Body: 0x10 party id, 0x14 alliance id, 0x18 and
        /// 0x1C the two linkshell ids.
        /// </summary>
        public static byte[] BuildGroupList(uint partyId, uint allianceId, uint linkshellId1, uint linkshellId2)
        {
            var body = NewBody(SearchRequestType.GroupList, 0x20);
            BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(0x10, 4), partyId);
            BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(0x14, 4), allianceId);
            BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(0x18, 4), linkshellId1);
            BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(0x1C, 4), linkshellId2);
            return body;
        }

        /// <summary>
        /// An Auction House category list request. Body: 0x12 the number of sort keys, 0x16 the category, and from 0x18 one 8-byte
        /// entry per key whose first byte is the key (2 level, 5 damage, 6 delay, 9 name; the server ignores the rest).
        /// </summary>
        public static byte[] BuildAuctionList(byte category, ReadOnlySpan<byte> sortKeys, bool more = false)
        {
            var body = NewBody(more ? SearchRequestType.AuctionRequestMore : SearchRequestType.AuctionRequest, 0x18 + (sortKeys.Length * 8));
            body[0x12] = (byte)sortKeys.Length;
            body[0x16] = category;
            for (int i = 0; i < sortKeys.Length; i++) body[0x18 + (8 * i)] = sortKeys[i];
            return body;
        }

        /// <summary>An item's price history. Body: 0x12 the item id, 0x15 the stack flag (1 for stacks).</summary>
        public static byte[] BuildAuctionHistory(ushort itemId, bool stack)
        {
            var body = NewBody(stack ? SearchRequestType.AuctionHistoryStack : SearchRequestType.AuctionHistorySingle, 0x18);
            BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(0x12, 2), itemId);
            body[0x15] = (byte)(stack ? 1 : 0);
            return body;
        }

        /// <summary>A player's search comment. Body: 0x10 the player id.</summary>
        public static byte[] BuildSearchComment(uint playerId)
        {
            var body = NewBody(SearchRequestType.SearchComment, 0x18);
            BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(0x10, 4), playerId);
            return body;
        }

        /// <summary>
        /// A <c>/sea</c> search. Body: 0x10 the length of the bit stream, 0x11 the stream. The stream is a list of fields, each a
        /// 5-bit <see cref="SearchFieldType"/> code, then (except Friend, Linkshell, Linkshell2, Comment and Flags2) a sort-descending
        /// bit and a present bit, then the value: Name 5-bit length and 7-bit characters, Area 10 bits (one field per area),
        /// Nation 2, Job 5, Level 8+8, Race 4, Rank 8+8, Flags1 16, Comment / Linkshell / Flags2 32. A zero byte ends it.
        /// </summary>
        public static byte[] BuildSearch(SearchQuery query)
        {
            ArgumentNullException.ThrowIfNull(query);
            var bits = new SearchBitWriter();

            void Field(SearchFieldType type, bool withHeader, bool present)
            {
                bits.Write((ulong)type, 5);
                if (withHeader)
                {
                    bits.Write(0, 1);                  // sort descending
                    bits.Write(present ? 1UL : 0UL, 1); // present
                }
            }

            if (!string.IsNullOrEmpty(query.Name))
            {
                string name = query.Name!.Length > 15 ? query.Name.Substring(0, 15) : query.Name;
                Field(SearchFieldType.Name, true, true);
                bits.Write((ulong)name.Length, 5);
                foreach (char c in name) bits.Write((ulong)(c & 0x7F), 7);
            }
            if (!query.AllAreas)
            {
                int n = Math.Min(query.Areas.Count, MaxAreas);
                for (int i = 0; i < n; i++)
                {
                    Field(SearchFieldType.Area, true, true);
                    bits.Write(query.Areas[i], 10);
                }
            }
            if (query.Nation is byte nation)
            {
                Field(SearchFieldType.Nation, true, true);
                bits.Write(nation, 2);
            }
            if (query.Job is byte job)
            {
                Field(SearchFieldType.Job, true, true);
                bits.Write(job, 5);
            }
            if (query.Level is (byte minLevel, byte maxLevel))
            {
                Field(SearchFieldType.Level, true, true);
                bits.Write(minLevel, 8);
                bits.Write(maxLevel, 8);
            }
            if (query.Race is byte race)
            {
                Field(SearchFieldType.Race, true, true);
                bits.Write(race, 4);
            }
            if (query.Rank is (byte minRank, byte maxRank))
            {
                Field(SearchFieldType.Rank, true, true);
                bits.Write(minRank, 8);
                bits.Write(maxRank, 8);
            }
            if (query.Flags1 is ushort flags1)
            {
                Field(SearchFieldType.Flags1, true, true);
                bits.Write(flags1, 16);
            }
            if (query.CommentType is uint comment)
            {
                Field(SearchFieldType.Comment, false, true);
                bits.Write(comment, 32);
            }
            if (query.LinkshellId is uint linkshell)
            {
                Field(SearchFieldType.Linkshell, false, true);
                bits.Write(linkshell, 32);
            }
            if (query.FriendsOnly)
            {
                Field(SearchFieldType.Friend, false, true);
            }
            if (query.Flags2 is uint flags2)
            {
                Field(SearchFieldType.Flags2, false, true);
                bits.Write(flags2, 32);
            }
            // The server stops before an entry that starts in the last 5 bits, so a field of no value (Friend) or a last field
            // needs a byte of padding after it; zero bits read as an absent name.
            bits.Write(0, 8);

            byte[] stream = bits.ToArray();
            if (stream.Length > 255) throw new ArgumentException("The search is too long for one request.", nameof(query));

            var body = NewBody(query.AllAreas ? SearchRequestType.SearchAll : SearchRequestType.Search, 0x11 + stream.Length);
            body[0x10] = (byte)stream.Length;
            stream.CopyTo(body.AsSpan(0x11));
            return body;
        }
    }

    #endregion

    #region Answer decoders (readonly ref struct)

    /// <summary>
    /// The 16-byte start of every search server answer (content offsets): 0x08 the length of the data, 0x0A the final flag (bit
    /// 7 set on the last answer of a request), 0x0B the type (the request type with bit 7 set), 0x0E the total result count.
    /// Answer layout referenced from LandSandBoat (https://github.com/LandSandBoat/server), <c>src/search/packets/</c>.
    /// </summary>
    public readonly ref struct SearchAnswerHeader
    {
        public const int Size = 16;

        public bool IsValid { get; }

        /// <summary>The length of the data in the frame (offset 0x08), counted from the frame start.</summary>
        public int DataLength { get; }

        /// <summary>True on the last answer of a request (bit 7 of byte 0x0A).</summary>
        public bool IsFinal { get; }

        /// <summary>The request type this answers (byte 0x0B without bit 7).</summary>
        public byte Type { get; }

        /// <summary>The total number of results of the request (offset 0x0E), also when this answer carries only some.</summary>
        public int Total { get; }

        public SearchAnswerHeader(ReadOnlySpan<byte> content)
        {
            if (content.Length < Size)
            {
                IsValid = false;
                DataLength = 0;
                IsFinal = false;
                Type = 0;
                Total = 0;
                return;
            }

            DataLength = BinaryPrimitives.ReadUInt16LittleEndian(content.Slice(0x08, 2));
            IsFinal = (content[0x0A] & 0x80) != 0;
            Type = (byte)(content[0x0B] & 0x7F);
            Total = BinaryPrimitives.ReadUInt16LittleEndian(content.Slice(0x0E, 2));
            IsValid = true;
        }
    }

    /// <summary>
    /// A page of an Auction House category list (answer type 0x15): up to 20 items of 10 bytes from 0x18 (item id, single count,
    /// stack count). <see cref="SearchAnswerHeader.Total"/> is the item count of the whole category.
    /// Layout referenced from LandSandBoat (https://github.com/LandSandBoat/server), <c>src/search/packets/auction_list.cpp</c>.
    /// </summary>
    public readonly ref struct AuctionListPage
    {
        public const int ItemsOffset = 0x18;
        public const int ItemSize = 10;

        private readonly ReadOnlySpan<byte> _content;
        public SearchAnswerHeader Header { get; }
        public int Count { get; }
        public bool IsValid => Header.IsValid;

        public AuctionListPage(ReadOnlySpan<byte> content)
        {
            _content = content;
            Header = new SearchAnswerHeader(content);
            Count = Header.IsValid ? Math.Max(0, Math.Min((Header.DataLength - ItemsOffset) / ItemSize, (content.Length - ItemsOffset) / ItemSize)) : 0;
        }

        public AuctionListItem GetItem(int index)
        {
            if (index < 0 || index >= Count) return default;
            int o = ItemsOffset + (index * ItemSize);
            return new AuctionListItem(
                BinaryPrimitives.ReadUInt16LittleEndian(_content.Slice(o, 2)),
                BinaryPrimitives.ReadUInt32LittleEndian(_content.Slice(o + 2, 4)),
                BinaryPrimitives.ReadUInt32LittleEndian(_content.Slice(o + 6, 4)));
        }
    }

    /// <summary>
    /// An item's price history (answer type 0x05): the item at 0x18, its current value at 0x1A and its category at 0x1E, then up
    /// to 10 sales of 40 bytes from 0x20 (price, a second word, seller at +8, buyer at +24, 16 bytes each).
    /// Layout referenced from LandSandBoat (https://github.com/LandSandBoat/server), <c>src/search/packets/auction_history.cpp</c>.
    /// </summary>
    public readonly ref struct AuctionHistoryPage
    {
        public const int EntriesOffset = 0x20;
        public const int EntrySize = 40;

        private readonly ReadOnlySpan<byte> _content;
        public SearchAnswerHeader Header { get; }
        public bool IsValid { get; }

        /// <summary>The item the history is for (offset 0x18).</summary>
        public ushort ItemId { get; }

        /// <summary>The item's value as LandSandBoat fills it (offset 0x1A): the listed price of singles or of stacks.</summary>
        public uint Price { get; }

        /// <summary>The item's category (offset 0x1E).</summary>
        public ushort Category { get; }

        /// <summary>The number of sales in the answer.</summary>
        public int Count { get; }

        public AuctionHistoryPage(ReadOnlySpan<byte> content)
        {
            _content = content;
            Header = new SearchAnswerHeader(content);
            if (!Header.IsValid || content.Length < EntriesOffset)
            {
                IsValid = false;
                ItemId = 0;
                Price = 0;
                Category = 0;
                Count = 0;
                return;
            }

            ItemId = BinaryPrimitives.ReadUInt16LittleEndian(content.Slice(0x18, 2));
            Price = BinaryPrimitives.ReadUInt32LittleEndian(content.Slice(0x1A, 4));
            Category = BinaryPrimitives.ReadUInt16LittleEndian(content.Slice(0x1E, 2));
            // 0x08 is 0x20 + 40 x count, and stays 0 for a history with no sales.
            int byLength = Header.DataLength >= EntriesOffset ? (Header.DataLength - EntriesOffset) / EntrySize : 0;
            Count = Math.Min(byLength, Math.Min(10, (content.Length - EntriesOffset) / EntrySize));
            IsValid = true;
        }

        public AuctionHistoryEntry GetEntry(int index)
        {
            if (index < 0 || index >= Count) return default;
            int o = EntriesOffset + (index * EntrySize);
            return new AuctionHistoryEntry(
                BinaryPrimitives.ReadUInt32LittleEndian(_content.Slice(o, 4)),
                BinaryPrimitives.ReadUInt32LittleEndian(_content.Slice(o + 4, 4)),
                ReadText(_content.Slice(o + 8, 16)),
                ReadText(_content.Slice(o + 24, 16)));
        }

        internal static string ReadText(ReadOnlySpan<byte> field)
        {
            int nul = field.IndexOf((byte)0);
            if (nul >= 0) field = field.Slice(0, nul);
            return Encoding.ASCII.GetString(field);
        }
    }

    /// <summary>
    /// A player's search comment (answer type 0x08): the player id at 0x18, the text length at 0x1C and up to 123 characters from
    /// 0x1E, padded with spaces. LandSandBoat sends nothing for a player with no comment.
    /// Layout referenced from LandSandBoat (https://github.com/LandSandBoat/server), <c>src/search/packets/search_comment.cpp</c>.
    /// </summary>
    public readonly ref struct SearchCommentPage
    {
        public const int TextOffset = 0x1E;
        public const int MaxLength = 123;

        private readonly ReadOnlySpan<byte> _content;
        public bool IsValid { get; }
        public uint PlayerId { get; }

        public SearchCommentPage(ReadOnlySpan<byte> content)
        {
            _content = content;
            if (content.Length < TextOffset)
            {
                IsValid = false;
                PlayerId = 0;
                return;
            }

            PlayerId = BinaryPrimitives.ReadUInt32LittleEndian(content.Slice(0x18, 4));
            IsValid = true;
        }

        /// <summary>The comment text without the padding spaces.</summary>
        public string GetText()
        {
            if (!IsValid) return string.Empty;
            int declared = BinaryPrimitives.ReadUInt16LittleEndian(_content.Slice(0x1C, 2));
            int length = Math.Min(Math.Min(declared, MaxLength), _content.Length - TextOffset);
            if (length <= 0) return string.Empty;
            return AuctionHistoryPage.ReadText(_content.Slice(TextOffset, length)).TrimEnd();
        }
    }

    /// <summary>
    /// A page of players (answer type 0x00 for a search, 0x02 for a party or linkshell list): from 0x18 a run of entries, each a
    /// size byte and that many bytes of bit stream (see <see cref="SearchRequestBuilder.BuildSearch"/> for the field types; an
    /// entry has Name, Area, then unless anonymous Nation, Job, Level, Race and Rank, then Flags1, Id, a linkshell rank block in
    /// linkshell lists, Unknown0E, a Comment when the player has one, Flags2 and Language). <see cref="SearchAnswerHeader.Total"/>
    /// is the result count of the whole search; LandSandBoat splits long lists over several answers and flags the last.
    /// Layout referenced from LandSandBoat (https://github.com/LandSandBoat/server), <c>src/search/packets/search_list.cpp</c>,
    /// <c>party_list.cpp</c> and <c>linkshell_list.cpp</c>.
    /// </summary>
    public readonly ref struct SearchEntityPage
    {
        public const int EntriesOffset = 0x18;

        private readonly ReadOnlySpan<byte> _content;
        public SearchAnswerHeader Header { get; }
        public bool IsValid => Header.IsValid;

        public SearchEntityPage(ReadOnlySpan<byte> content)
        {
            _content = content;
            Header = new SearchAnswerHeader(content);
        }

        /// <summary>
        /// Reads the entry that starts at <paramref name="position"/> (start with <see cref="EntriesOffset"/>) and moves the
        /// position past it. False at the end of the data or on a malformed entry.
        /// </summary>
        public bool TryReadEntity(ref int position, out SearchEntity entity)
        {
            entity = null!;
            int end = Math.Min(Header.DataLength, _content.Length);
            if (!IsValid || position >= end) return false;

            int size = _content[position];
            if (size == 0 || position + 1 + size > end) return false;

            var reader = new SearchBitReader(_content.Slice(position + 1, size));
            string name = string.Empty;
            ushort zone = 0;
            byte nation = 0, mainJob = 0, subJob = 0, mainLevel = 0, subLevel = 0, race = 0, rank = 0;
            uint flags1 = 0, flags2 = 0, id = 0, comment = 0, ls1 = 0, ls2 = 0;
            ushort languages = 0;
            byte lsRank1 = 0, lsRank2 = 0;
            Span<char> chars = stackalloc char[15];

            // The shortest field is a 5-bit code and a 2-bit value.
            while (reader.BitsLeft >= 7)
            {
                var type = (SearchFieldType)reader.Read(5);
                bool ok = true;
                switch (type)
                {
                    case SearchFieldType.Name:
                        if (reader.BitsLeft < 4) { ok = false; break; }
                        int length = (int)reader.Read(4);
                        if (reader.BitsLeft < length * 7) { ok = false; break; }
                        for (int i = 0; i < length; i++) chars[i] = (char)reader.Read(7);
                        name = new string(chars.Slice(0, Math.Min(length, 15)));
                        break;
                    case SearchFieldType.Area: ok = Need(ref reader, 10); if (ok) zone = (ushort)reader.Read(10); break;
                    case SearchFieldType.Nation: ok = Need(ref reader, 2); if (ok) nation = (byte)reader.Read(2); break;
                    case SearchFieldType.Job:
                        ok = Need(ref reader, 10);
                        if (ok) { mainJob = (byte)reader.Read(5); subJob = (byte)reader.Read(5); }
                        break;
                    case SearchFieldType.Level:
                        ok = Need(ref reader, 16);
                        if (ok) { mainLevel = (byte)reader.Read(8); subLevel = (byte)reader.Read(8); }
                        break;
                    case SearchFieldType.Race: ok = Need(ref reader, 4); if (ok) race = (byte)reader.Read(4); break;
                    case SearchFieldType.Rank: ok = Need(ref reader, 8); if (ok) rank = (byte)reader.Read(8); break;
                    case SearchFieldType.Flags1: ok = Need(ref reader, 16); if (ok) flags1 = reader.Read(16); break;
                    case SearchFieldType.Id: ok = Need(ref reader, 20); if (ok) id = reader.Read(20); break;
                    case SearchFieldType.LinkshellRank:
                        ok = Need(ref reader, 24 + 96);
                        if (ok)
                        {
                            lsRank1 = (byte)reader.Read(8);
                            lsRank2 = (byte)reader.Read(8);
                            reader.Read(8);
                            ls1 = reader.Read(32);
                            ls2 = reader.Read(32);
                            reader.Read(32);
                        }
                        break;
                    case SearchFieldType.Unknown0E: ok = Need(ref reader, 32); if (ok) reader.Read(32); break;
                    case SearchFieldType.Comment: ok = Need(ref reader, 32); if (ok) comment = reader.Read(32); break;
                    case SearchFieldType.Flags2: ok = Need(ref reader, 32); if (ok) flags2 = reader.Read(32); break;
                    case SearchFieldType.Language: ok = Need(ref reader, 16); if (ok) languages = (ushort)reader.Read(16); break;
                    default: ok = false; break;
                }
                if (!ok) break;
            }

            position += 1 + size;
            entity = new SearchEntity(name, zone, nation, mainJob, subJob, mainLevel, subLevel, race, rank, flags1, flags2, id, comment,
                languages, lsRank1, lsRank2, ls1, ls2);
            return true;

            static bool Need(ref SearchBitReader r, int bits) => r.BitsLeft >= bits;
        }
    }

    #endregion
}
