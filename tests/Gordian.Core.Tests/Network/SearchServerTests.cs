// tests/Gordian.Core.Tests/Network/SearchServerTests.cs
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Gordian.Core.Config;
using Gordian.Core.Actions;
using Gordian.Core.Network;
using Gordian.Core.Network.Packets;
using Gordian.Core.Network.Search;
using Gordian.Core.World;
using Xunit;

namespace Gordian.Core.Tests.Network
{
    /// <summary>
    /// #119: the search (cache) server client. Frames are checked by wrapping and unwrapping them with the server's view of the
    /// algorithm, requests byte by byte against the offsets LandSandBoat reads, and answers decoded from payloads laid out the
    /// way LandSandBoat writes them.
    /// </summary>
    public class SearchServerTests
    {
        // ---- answer builders (the server side, written independently of the decoders) ----

        private sealed class BitPacker
        {
            public readonly byte[] Buffer;
            public int Bit;

            public BitPacker(byte[] buffer, int startByte)
            {
                Buffer = buffer;
                Bit = startByte * 8;
            }

            public void Put(ulong value, int bits)
            {
                for (int i = 0; i < bits; i++)
                {
                    if (((value >> (bits - 1 - i)) & 1) != 0) Buffer[Bit >> 3] |= (byte)(0x80 >> (Bit & 7));
                    Bit++;
                }
            }
        }

        private static byte[] EntityAnswer(byte type, int total, bool final, bool linkshell, params (string Name, ushort Zone, byte Mjob, byte Sjob, byte Mlvl, byte Slvl,
            uint Flags1, uint Id, uint Comment)[] players)
        {
            var data = new byte[1024];
            var bits = new BitPacker(data, 0x18);
            foreach (var p in players)
            {
                int sizeByte = bits.Bit / 8;
                bits.Bit += 8;
                bits.Put(0, 5); bits.Put((ulong)p.Name.Length, 4);
                foreach (char c in p.Name) bits.Put(c, 7);
                bits.Put(1, 5); bits.Put(p.Zone, 10);
                if ((p.Flags1 & 0x4000) == 0)
                {
                    bits.Put(2, 5); bits.Put(1, 2);                                 // nation
                    bits.Put(3, 5); bits.Put(p.Mjob, 5); bits.Put(p.Sjob, 5);        // job
                    bits.Put(4, 5); bits.Put(p.Mlvl, 8); bits.Put(p.Slvl, 8);        // level
                    bits.Put(5, 5); bits.Put(3, 4);                                 // race
                    bits.Put(0x10, 5); bits.Put(7, 8);                              // rank
                }
                bits.Put(6, 5); bits.Put(p.Flags1, 16);
                bits.Put(8, 5); bits.Put(p.Id, 20);
                if (linkshell)
                {
                    bits.Put(0x0D, 5); bits.Put(3, 8); bits.Put(0, 8); bits.Put(0, 8);
                    bits.Put(0xAA55, 32); bits.Put(0, 32); bits.Put(0, 32);
                }
                bits.Put(0x0E, 5); bits.Put(0, 32);
                if (p.Comment != 0) { bits.Put(0x11, 5); bits.Put(p.Comment, 32); }
                bits.Put(0x16, 5); bits.Put(p.Flags1, 32);
                bits.Put(0x17, 5); bits.Put(5, 16);
                if ((bits.Bit & 7) != 0) bits.Bit += 8 - (bits.Bit & 7);
                data[sizeByte] = (byte)((bits.Bit / 8) - sizeByte - 1);
            }
            int length = bits.Bit / 8;
            BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(8, 2), (ushort)length);
            data[0x0A] = (byte)(final ? 0x80 : 0);
            data[0x0B] = (byte)(0x80 | type);
            BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(0x0E, 2), (ushort)total);
            return data.AsSpan(0, length).ToArray();
        }

        private static byte[] AuctionListAnswer(int total, bool final, params (ushort Item, uint Singles, uint Stacks)[] items)
        {
            var data = new byte[0x18 + (10 * items.Length)];
            BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(8, 2), (ushort)data.Length);
            data[0x0A] = (byte)(final ? 0x80 : 0);
            data[0x0B] = 0x95;
            BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(0x0E, 2), (ushort)total);
            for (int i = 0; i < items.Length; i++)
            {
                int o = 0x18 + (10 * i);
                BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(o, 2), items[i].Item);
                BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(o + 2, 4), items[i].Singles);
                BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(o + 6, 4), items[i].Stacks);
            }
            return data;
        }

        private static byte[] AuctionHistoryAnswer(ushort item, uint price, ushort category, params (uint Price, uint Date, string Seller, string Buyer)[] sales)
        {
            var data = new byte[0x20 + (40 * sales.Length)];
            data[0x0A] = 0x80;
            data[0x0B] = 0x85;
            BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(0x10, 2), item);
            BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(0x18, 2), item);
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0x1A, 4), price);
            BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(0x1E, 2), category);
            for (int i = 0; i < sales.Length; i++)
            {
                int o = 0x20 + (40 * i);
                BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(o, 4), sales[i].Price);
                BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(o + 4, 4), sales[i].Date);
                Encoding.ASCII.GetBytes(sales[i].Seller).CopyTo(data, o + 8);
                Encoding.ASCII.GetBytes(sales[i].Buyer).CopyTo(data, o + 24);
            }
            if (sales.Length > 0) BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(8, 2), (ushort)data.Length);
            return data;
        }

        private static byte[] CommentAnswer(uint player, string comment)
        {
            var data = new byte[0x9B];
            data[8] = 154;
            data[0x0A] = 0x80;
            data[0x0B] = 0x88;
            data[0x0E] = 1;
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0x18, 4), player);
            BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(0x1C, 2), 124);
            Encoding.ASCII.GetBytes(comment).CopyTo(data, 0x1E);
            for (int i = 0x1E + comment.Length; i < 0x1E + 123; i++) data[i] = (byte)' ';
            return data;
        }

        // ---- a fake search server over a transport ----

        private sealed class FakeTransport : ISearchTransport
        {
            public readonly List<byte[]> Bodies = new();
            public Func<byte[], IEnumerable<(byte[] Content, int Padding)>> Server = _ => Array.Empty<(byte[], int)>();
            public int Connects;
            /// <summary>Frames sent before the answers, encrypted with the wrong key (corrupt traffic).</summary>
            public int CorruptFramesFirst;

            public Task<ISearchConnection> ConnectAsync(string host, int port, CancellationToken cancellationToken)
            {
                Connects++;
                return Task.FromResult<ISearchConnection>(new Connection(this));
            }

            private sealed class Connection : ISearchConnection
            {
                private readonly FakeTransport _owner;
                private readonly Queue<byte[]> _answers = new();

                public Connection(FakeTransport owner) => _owner = owner;

                public Task SendAsync(byte[] frame, CancellationToken cancellationToken)
                {
                    var copy = (byte[])frame.Clone();
                    Assert.True(SearchFrame.TryDecryptRequest(copy, out var key));
                    byte[] body = copy.AsSpan(0, copy.Length - SearchFrame.TrailerSize).ToArray();
                    _owner.Bodies.Add(body);
                    for (int i = 0; i < _owner.CorruptFramesFirst; i++)
                        _answers.Enqueue(SearchFrame.EncryptResponse(new byte[0x28], new SearchFrame.AnswerKey(key.Seed + 1, key.PayloadTail), 8));
                    foreach (var (content, padding) in _owner.Server(body)) _answers.Enqueue(SearchFrame.EncryptResponse(content, key, padding));
                    return Task.CompletedTask;
                }

                public async Task<byte[]?> ReadFrameAsync(CancellationToken cancellationToken)
                {
                    if (_answers.Count > 0) return _answers.Dequeue();
                    // A server with nothing to say leaves the connection open until the client gives up.
                    await Task.Delay(Timeout.Infinite, cancellationToken);
                    return null;
                }

                public ValueTask DisposeAsync() => ValueTask.CompletedTask;
            }
        }

        private static SearchClient NewClient(FakeTransport transport, int timeoutMs = 2000) =>
            new("search.test", 54002, transport) { Timeout = TimeSpan.FromMilliseconds(timeoutMs) };

        // ---- frames ----

        [Fact]
        public void SearchFrame_RoundTripsARequestAndItsAnswer()
        {
            var body = new byte[0x20];
            body[0x0B] = 0x02;
            BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(0x10), 0x1234);
            BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(0x1C), 0xCAFEBABE);

            byte[] frame = SearchFrame.EncryptRequest(body, 0x0BADF00D, out var key);

            Assert.Equal(body.Length + 20, frame.Length);
            Assert.Equal(frame.Length, BinaryPrimitives.ReadUInt16LittleEndian(frame));
            Assert.Equal("IXFF", Encoding.ASCII.GetString(frame, 4, 4));
            Assert.Equal(0x0BADF00Du, BinaryPrimitives.ReadUInt32LittleEndian(frame.AsSpan(frame.Length - 4)));
            Assert.NotEqual(body[0x10..0x14], frame[0x10..0x14]);                        // the payload is enciphered
            Assert.Equal(0xCAFEBABEu, key.PayloadTail);

            var server = (byte[])frame.Clone();
            Assert.True(SearchFrame.TryDecryptRequest(server, out var serverKey));
            Assert.Equal(key, serverKey);
            Assert.Equal(0x1234u, BinaryPrimitives.ReadUInt32LittleEndian(server.AsSpan(0x10)));
            Assert.Equal(0x02, server[0x0B]);

            var answer = new byte[0x30];
            answer[0x0B] = 0x82;
            answer[0x28] = 0x77;
            byte[] answerFrame = SearchFrame.EncryptResponse(answer, key, padding: 8);
            Assert.True(SearchFrame.TryDecryptResponse(answerFrame, key, out var content));
            Assert.Equal(0x38, content.Length);          // the answer and its 8 bytes of padding
            Assert.Equal(0x77, content[0x28]);
        }

        [Fact]
        public void SearchFrame_RejectsTamperedAndMiskeyedFrames()
        {
            var body = new byte[0x18];
            byte[] frame = SearchFrame.EncryptRequest(body, 77, out var key);

            var tampered = (byte[])frame.Clone();
            tampered[10] ^= 0x40;
            Assert.False(SearchFrame.TryDecryptRequest(tampered, out _));

            byte[] answerFrame = SearchFrame.EncryptResponse(new byte[0x20], key);
            Assert.False(SearchFrame.TryDecryptResponse((byte[])answerFrame.Clone(), new SearchFrame.AnswerKey(key.Seed, key.PayloadTail + 1), out _));
            Assert.False(SearchFrame.TryDecryptResponse(new byte[10], key, out _));
            Assert.Throws<ArgumentException>(() => SearchFrame.EncryptRequest(new byte[0x19], 1, out _));
        }

        // ---- request layouts ----

        [Fact]
        public void BuildGroupList_PutsTheIdsAtTheOffsetsTheServerReads()
        {
            byte[] body = SearchRequestBuilder.BuildGroupList(11, 22, 33, 44);

            Assert.Equal(0x20, body.Length);
            Assert.Equal(0x02, body[0x0B]);
            Assert.Equal(11u, BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(0x10)));
            Assert.Equal(22u, BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(0x14)));
            Assert.Equal(33u, BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(0x18)));
            Assert.Equal(44u, BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(0x1C)));
        }

        [Fact]
        public void BuildAuctionRequests()
        {
            byte[] list = SearchRequestBuilder.BuildAuctionList(5, new byte[] { 2, 9 });
            Assert.Equal(0x15, list[0x0B]);
            Assert.Equal(2, list[0x12]);
            Assert.Equal(5, list[0x16]);
            Assert.Equal(2, list[0x18]);
            Assert.Equal(9, list[0x20]);
            Assert.Equal(0, (list.Length - 8) % 8);

            Assert.Equal(0x10, SearchRequestBuilder.BuildAuctionList(5, default, more: true)[0x0B]);
            Assert.Equal(0, SearchRequestBuilder.BuildAuctionList(5, default)[0x12]);

            byte[] single = SearchRequestBuilder.BuildAuctionHistory(4096, stack: false);
            Assert.Equal(0x05, single[0x0B]);
            Assert.Equal(4096, BinaryPrimitives.ReadUInt16LittleEndian(single.AsSpan(0x12)));
            Assert.Equal(0, single[0x15]);

            byte[] stack = SearchRequestBuilder.BuildAuctionHistory(4096, stack: true);
            Assert.Equal(0x06, stack[0x0B]);
            Assert.Equal(1, stack[0x15]);

            byte[] comment = SearchRequestBuilder.BuildSearchComment(0xABCD);
            Assert.Equal(0x08, comment[0x0B]);
            Assert.Equal(0xABCDu, BinaryPrimitives.ReadUInt32LittleEndian(comment.AsSpan(0x10)));
        }

        // The server's reader for the search bit stream, written from LandSandBoat's description: fields are read most
        // significant bit first.
        private static uint ReadBits(byte[] data, int start, ref int bit, int count)
        {
            ulong value = 0;
            for (int i = 0; i < count; i++)
            {
                int p = (start * 8) + bit + i;
                value = (value << 1) | (uint)((data[p >> 3] >> (7 - (p & 7))) & 1);
            }
            bit += count;
            return (uint)value;
        }

        [Fact]
        public void BuildSearch_WritesTheFieldsTheServerReads()
        {
            var query = new SearchQuery { Name = "Ay", Job = 3, Level = (70, 75), Flags1 = 0x8000 };
            query.Areas.Add(230);
            byte[] body = SearchRequestBuilder.BuildSearch(query);

            Assert.Equal(0x03, body[0x0B]);
            Assert.Equal(0, (body.Length - 8) % 8);
            int size = body[0x10];
            Assert.True(0x11 + size <= body.Length);

            int bit = 0;
            // Name: type 0, sort 0, present 1, 5-bit length, 7-bit characters.
            Assert.Equal(0u, ReadBits(body, 0x11, ref bit, 5));
            Assert.Equal(0u, ReadBits(body, 0x11, ref bit, 1));
            Assert.Equal(1u, ReadBits(body, 0x11, ref bit, 1));
            Assert.Equal(2u, ReadBits(body, 0x11, ref bit, 5));
            Assert.Equal((uint)'A', ReadBits(body, 0x11, ref bit, 7));
            Assert.Equal((uint)'y', ReadBits(body, 0x11, ref bit, 7));
            // Area
            Assert.Equal(1u, ReadBits(body, 0x11, ref bit, 5));
            ReadBits(body, 0x11, ref bit, 2);
            Assert.Equal(230u, ReadBits(body, 0x11, ref bit, 10));
            // Job
            Assert.Equal(3u, ReadBits(body, 0x11, ref bit, 5));
            ReadBits(body, 0x11, ref bit, 2);
            Assert.Equal(3u, ReadBits(body, 0x11, ref bit, 5));
            // Level
            Assert.Equal(4u, ReadBits(body, 0x11, ref bit, 5));
            ReadBits(body, 0x11, ref bit, 2);
            Assert.Equal(70u, ReadBits(body, 0x11, ref bit, 8));
            Assert.Equal(75u, ReadBits(body, 0x11, ref bit, 8));
            // Flags1
            Assert.Equal(6u, ReadBits(body, 0x11, ref bit, 5));
            ReadBits(body, 0x11, ref bit, 2);
            Assert.Equal(0x8000u, ReadBits(body, 0x11, ref bit, 16));
        }

        [Fact]
        public void BuildSearch_AllAreasAndTheFieldsWithoutAHeader()
        {
            var query = new SearchQuery { AllAreas = true, FriendsOnly = true, LinkshellId = 0x1234, Flags2 = 5, CommentType = 0x20 };
            query.Areas.Add(100);                         // ignored with AllAreas
            byte[] body = SearchRequestBuilder.BuildSearch(query);

            Assert.Equal(0x00, body[0x0B]);
            int bit = 0;
            Assert.Equal(0x11u, ReadBits(body, 0x11, ref bit, 5));      // comment: no sort or present bits
            Assert.Equal(0x20u, ReadBits(body, 0x11, ref bit, 32));
            Assert.Equal(0x0Bu, ReadBits(body, 0x11, ref bit, 5));      // linkshell
            Assert.Equal(0x1234u, ReadBits(body, 0x11, ref bit, 32));
            Assert.Equal(0x0Cu, ReadBits(body, 0x11, ref bit, 5));      // friend: nothing else
            Assert.Equal(0x16u, ReadBits(body, 0x11, ref bit, 5));      // flags2
            Assert.Equal(5u, ReadBits(body, 0x11, ref bit, 32));
        }

        // ---- answer decoders ----

        [Fact]
        public void SearchEntityPage_DecodesPlayersAsLandSandBoatPacksThem()
        {
            byte[] content = EntityAnswer(0, total: 3, final: true, linkshell: false,
                ("Ayame", 230, 3, 4, 75, 37, 0x8010, 0x1234, 0x20),
                ("Hidden", 231, 0, 0, 0, 0, 0x4000, 0x1235, 0));
            var page = new SearchEntityPage(content);

            Assert.True(page.IsValid);
            Assert.True(page.Header.IsFinal);
            Assert.Equal(3, page.Header.Total);
            Assert.Equal(0, page.Header.Type);

            int position = SearchEntityPage.EntriesOffset;
            Assert.True(page.TryReadEntity(ref position, out var ayame));
            Assert.Equal("Ayame", ayame.Name);
            Assert.Equal(230, ayame.ZoneId);
            Assert.Equal(1, ayame.Nation);
            Assert.Equal(3, ayame.MainJob);
            Assert.Equal(4, ayame.SubJob);
            Assert.Equal(75, ayame.MainLevel);
            Assert.Equal(37, ayame.SubLevel);
            Assert.Equal(3, ayame.Race);
            Assert.Equal(7, ayame.Rank);
            Assert.Equal(0x8010u, ayame.Flags1);
            Assert.Equal(0x8010u, ayame.Flags2);
            Assert.Equal(0x1234u, ayame.CharacterId);
            Assert.Equal(0x20u, ayame.CommentType);
            Assert.Equal(5, ayame.Languages);
            Assert.True(ayame.Flags.HasFlag(SearchFlags.SeekingParty));
            Assert.True(ayame.Flags.HasFlag(SearchFlags.HasComment));
            Assert.False(ayame.IsAnonymous);

            Assert.True(page.TryReadEntity(ref position, out var hidden));
            Assert.Equal("Hidden", hidden.Name);
            Assert.True(hidden.IsAnonymous);
            Assert.Equal(0, hidden.MainJob);
            Assert.Equal(231, hidden.ZoneId);

            Assert.False(page.TryReadEntity(ref position, out _));
        }

        [Fact]
        public void SearchEntityPage_ReadsTheLinkshellRankBlock()
        {
            byte[] content = EntityAnswer(2, total: 1, final: true, linkshell: true, ("Cybin", 100, 1, 2, 10, 5, 0, 0x99, 0));
            var page = new SearchEntityPage(content);
            int position = SearchEntityPage.EntriesOffset;

            Assert.True(page.TryReadEntity(ref position, out var player));
            Assert.Equal(3, player.LinkshellRank1);
            Assert.Equal(0xAA55u, player.LinkshellId1);
            Assert.Equal(0x99u, player.CharacterId);
            Assert.Equal(5, player.Languages);
        }

        [Fact]
        public void AuctionPages_Decode()
        {
            var list = new AuctionListPage(AuctionListAnswer(45, final: false, (4096, 3, 1), (4097, 0, 12)));
            Assert.True(list.IsValid);
            Assert.Equal(2, list.Count);
            Assert.Equal(45, list.Header.Total);
            Assert.False(list.Header.IsFinal);
            Assert.Equal(new AuctionListItem(4097, 0, 12), list.GetItem(1));
            Assert.Equal(default, list.GetItem(2));

            var history = new AuctionHistoryPage(AuctionHistoryAnswer(4096, 800, 7, (700, 1_700_000_000, "Ayame", "Cybin"), (650, 1_699_000_000, "Zed", "Ayame")));
            Assert.True(history.IsValid);
            Assert.Equal(4096, history.ItemId);
            Assert.Equal(800u, history.Price);
            Assert.Equal(7, history.Category);
            Assert.Equal(2, history.Count);
            Assert.Equal(new AuctionHistoryEntry(650, 1_699_000_000, "Zed", "Ayame"), history.GetEntry(1));

            Assert.Equal(0, new AuctionHistoryPage(AuctionHistoryAnswer(4096, 800, 7)).Count);   // no sales: the length word stays 0
        }

        [Fact]
        public void SearchCommentPage_DecodesAndTrimsThePadding()
        {
            var page = new SearchCommentPage(CommentAnswer(0x77, "Looking for a party"));

            Assert.True(page.IsValid);
            Assert.Equal(0x77u, page.PlayerId);
            Assert.Equal("Looking for a party", page.GetText());
        }

        // ---- the client ----

        [Fact]
        public async Task GetAuctionList_CollectsPagesUntilTheFinalOne()
        {
            var transport = new FakeTransport
            {
                Server = _ => new[]
                {
                    (AuctionListAnswer(3, false, (1, 1, 0), (2, 0, 2)), 8),
                    (AuctionListAnswer(3, true, (3, 5, 5)), 8)
                }
            };
            var client = NewClient(transport);

            var result = await client.GetAuctionListAsync(4, new byte[] { 9 });

            Assert.True(result.Complete);
            Assert.Equal(3, result.Total);
            Assert.Equal(new ushort[] { 1, 2, 3 }, result.Items.Select(i => i.ItemId).ToArray());
            var body = Assert.Single(transport.Bodies);
            Assert.Equal(0x15, body[0x0B]);
            Assert.Equal(4, body[0x16]);
            Assert.Equal(9, body[0x18]);
            Assert.Equal(1, transport.Connects);
        }

        [Fact]
        public async Task GetAuctionHistory_AndComment_AndNoAnswer()
        {
            var transport = new FakeTransport
            {
                Server = body => body[0x0B] switch
                {
                    0x05 or 0x06 => new[] { (AuctionHistoryAnswer(4096, 800, 7, (700, 5, "Ayame", "Cybin")), 8) },
                    0x08 => BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(0x10)) == 7
                        ? new[] { (CommentAnswer(7, "Hello"), 8) }
                        : Array.Empty<(byte[], int)>(),
                    _ => Array.Empty<(byte[], int)>()
                }
            };
            var client = NewClient(transport, timeoutMs: 150);

            var history = await client.GetAuctionHistoryAsync(4096, stack: true);
            Assert.NotNull(history);
            Assert.True(history!.Stack);
            Assert.Equal(700u, history.Sales[0].Price);
            Assert.Equal(6, transport.Bodies[0][0x0B]);

            Assert.Equal("Hello", await client.GetSearchCommentAsync(7));
            // LandSandBoat sends nothing for an empty comment: the request runs out its wait.
            Assert.Null(await client.GetSearchCommentAsync(8));
        }

        [Fact]
        public async Task Search_AndPartyAndLinkshellLists()
        {
            var transport = new FakeTransport
            {
                Server = body => body[0x0B] switch
                {
                    0x03 or 0x00 => new[]
                    {
                        (EntityAnswer(0, 2, false, false, ("Ayame", 230, 3, 4, 75, 37, 0, 1, 0)), 0),
                        (EntityAnswer(0, 2, true, false, ("Cybin", 230, 1, 2, 60, 30, 0, 2, 0)), 0)
                    },
                    0x02 when BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(0x10)) != 0 =>
                        new[] { (EntityAnswer(2, 1, true, false, ("Zed", 100, 6, 0, 50, 0, 0, 3, 0)), 0) },
                    0x02 => new[] { (EntityAnswer(2, 1, true, true, ("Moogle", 100, 6, 0, 50, 0, 0, 4, 0)), 0) },
                    _ => Array.Empty<(byte[], int)>()
                }
            };
            var client = NewClient(transport);

            var search = await client.SearchAsync(new SearchQuery { Name = "A", AllAreas = true });
            Assert.True(search.Complete);
            Assert.Equal(2, search.Total);
            Assert.Equal(new[] { "Ayame", "Cybin" }, search.Players.Select(p => p.Name).ToArray());
            Assert.Equal(0x00, transport.Bodies[0][0x0B]);

            var party = await client.GetPartyListAsync(0x42);
            Assert.Equal("Zed", Assert.Single(party.Players).Name);
            Assert.Equal(0x42u, BinaryPrimitives.ReadUInt32LittleEndian(transport.Bodies[1].AsSpan(0x10)));

            var linkshell = await client.GetLinkshellListAsync(0x99);
            Assert.Equal("Moogle", Assert.Single(linkshell.Players).Name);
            Assert.Equal(0x99u, BinaryPrimitives.ReadUInt32LittleEndian(transport.Bodies[2].AsSpan(0x18)));
        }

        [Fact]
        public async Task ExchangeAsync_DropsACorruptFrameAndKeepsReading()
        {
            var transport = new FakeTransport { CorruptFramesFirst = 1 };
            transport.Server = _ => new[] { (AuctionHistoryAnswer(1, 1, 1, (5, 6, "A", "B")), 8) };
            var client = NewClient(transport, timeoutMs: 500);

            var history = await client.GetAuctionHistoryAsync(1, false);

            Assert.NotNull(history);
            Assert.Equal(5u, history!.Sales[0].Price);
        }

        [Fact]
        public async Task TcpSearchTransport_TalksToALoopbackServer()
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;

            var server = Task.Run(async () =>
            {
                using var socket = await listener.AcceptTcpClientAsync();
                var stream = socket.GetStream();
                var request = new byte[64];
                int read = await stream.ReadAsync(request);
                var frame = request.AsSpan(0, read).ToArray();
                Assert.True(SearchFrame.TryDecryptRequest(frame, out var key));
                Assert.Equal(0x08, frame[0x0B]);
                // Two answers in one write: the client has to cut them by their length words.
                byte[] a = SearchFrame.EncryptResponse(CommentAnswer(5, "Hi"), key, 8);
                await stream.WriteAsync(a);
            });

            var client = new SearchClient("127.0.0.1", port) { Timeout = TimeSpan.FromSeconds(5) };
            string? comment = await client.GetSearchCommentAsync(5);
            await server;

            Assert.Equal("Hi", comment);
        }

        // ---- the service ----

        private sealed class Session
        {
            public PacketParser Parser = null!;
            public PacketDispatcher Dispatcher = null!;
            public FakeTransport Transport = null!;
            public readonly List<byte[]> Sent = new();
        }

        private static Session NewSession(Func<byte[], IEnumerable<(byte[] Content, int Padding)>> server)
        {
            var session = new Session { Dispatcher = new PacketDispatcher(), Transport = new FakeTransport { Server = server } };
            session.Parser = new PacketParser(new SessionProfile(), (data, _) =>
            {
                session.Sent.Add(data.ToArray());
                return Task.CompletedTask;
            }, dispatcher: session.Dispatcher);
            session.Parser.Search.Transport = session.Transport;
            session.Parser.Search.Configure("search.test", 54002);
            return session;
        }

        [Fact]
        public async Task SearchService_UsesAConfiguredOrDefaultAddress()
        {
            var parser = new PacketParser(new SessionProfile(), (_, _) => Task.CompletedTask);
            Assert.False(parser.Search.IsConfigured);
            await Assert.ThrowsAsync<InvalidOperationException>(() => parser.Search.SearchAsync(new SearchQuery()));

            parser.Search.ConfigureDefault("10.0.0.5");
            Assert.Equal("10.0.0.5", parser.Search.Host);
            Assert.Equal(SearchClient.DefaultPort, parser.Search.Port);

            parser.Search.Configure("search.example", 5000);
            parser.Search.ConfigureDefault("10.0.0.9");           // an explicit address wins
            Assert.Equal("search.example", parser.Search.Host);
            Assert.Equal(5000, parser.Search.Port);
        }

        [Fact]
        public async Task SearchService_FillsTheStateAndAsksTheWorldServerForTheGroupId()
        {
            var session = NewSession(body => body[0x0B] switch
            {
                0x02 => new[] { (EntityAnswer(2, 1, true, false, ("Zed", 100, 6, 0, 50, 0, 0, 3, 0)), 0) },
                0x15 => new[] { (AuctionListAnswer(1, true, (4096, 2, 0)), 8) },
                _ => Array.Empty<(byte[], int)>()
            });
            var service = session.Parser.Search;
            session.Parser.ActionService.SearchService = service;

            // No group id answer: the party list is not fetched.
            service.GroupIdTimeout = TimeSpan.FromMilliseconds(50);
            Assert.Null(await service.RefreshPartyListAsync());
            Assert.Equal(0x078, Header(session.Sent[^1]).Id);

            // The world server answers the group id request, and the search server's party list follows.
            service.GroupIdTimeout = TimeSpan.FromSeconds(3);
            var pending = service.RefreshPartyListAsync();
            await Task.Delay(50);
            var id = new byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(id, 0x4242);
            session.Dispatcher.Dispatch(new PacketHeader(0x0E1, 8, 1), id);
            var party = await pending;

            Assert.Equal("Zed", Assert.Single(party!.Players).Name);
            Assert.Equal(0x4242u, BinaryPrimitives.ReadUInt32LittleEndian(session.Transport.Bodies[^1].AsSpan(0x10)));
            Assert.Same(party, service.State.PartyList);

            var list = await service.RefreshAuctionListAsync(3);
            Assert.Equal(4096, service.State.GetAuctionList(3)!.Items[0].ItemId);
            Assert.Equal(new byte[] { 3 }, service.State.AuctionCategories.ToArray());
            Assert.Same(list, service.State.GetAuctionList(3));
        }

        private static (ushort Id, int Size, ushort Seq) Header(byte[] packet)
        {
            Assert.True(PacketHeader.TryParse(packet, out var h));
            return (h.PacketId, h.TotalSize, h.SequenceId);
        }

        [Fact]
        public async Task SearchService_FindsAWornLinkshellsIdInTheItemData()
        {
            var session = NewSession(body => body[0x0B] == 0x02
                ? new[] { (EntityAnswer(2, 1, true, true, ("Moogle", 100, 6, 0, 50, 0, 0, 4, 0)), 0) }
                : Array.Empty<(byte[], int)>());
            var service = session.Parser.Search;

            Assert.Null(await service.RefreshLinkshellListAsync(1));            // nothing worn

            var ext = new byte[24];
            BinaryPrimitives.WriteUInt32LittleEndian(ext, 0x5678);
            session.Parser.Inventory.GetContainer(ContainerId.Inventory)._items[9] =
                new InventoryItem(513, 1, 9, ContainerId.Inventory, 0, ItemLockFlag.Linkshell, ext);
            session.Parser.Party.SetLinkshellItem(1, 9, ContainerId.Inventory);

            Assert.Equal(0x5678u, service.GetWornLinkshellId(1));
            var members = await service.RefreshLinkshellListAsync(1);
            Assert.Equal("Moogle", Assert.Single(members!.Players).Name);
            Assert.Equal(0x5678u, BinaryPrimitives.ReadUInt32LittleEndian(session.Transport.Bodies[^1].AsSpan(0x18)));
            Assert.NotNull(service.State.GetLinkshellList(0x5678));
        }

        // ---- /sea ----

        [Fact]
        public void ParseSearchQuery_ReadsNamesJobsLevelsAndFlags()
        {
            var query = PlayerActionService.ParseSearchQuery("whm 70-75 party Ayame", 230);

            Assert.False(query.AllAreas);
            Assert.Equal(new ushort[] { 230 }, query.Areas.ToArray());
            Assert.Equal((byte)3, query.Job);
            Assert.Equal(((byte)70, (byte)75), query.Level);
            Assert.Equal((ushort)SearchFlags.SeekingParty, query.Flags1);
            Assert.Equal("Ayame", query.Name);

            var all = PlayerActionService.ParseSearchQuery("all lv99 friend", 230);
            Assert.True(all.AllAreas);
            Assert.Empty(all.Areas);
            Assert.Equal(((byte)99, (byte)99), all.Level);
            Assert.True(all.FriendsOnly);
            Assert.Null(all.Name);

            Assert.Equal(ChatCommandResultKind.PlayerSearch, ChatCommandRouter.Parse("/sea all whm").Kind);
            Assert.Equal("all whm", ChatCommandRouter.Parse("/sea all whm").Message);
        }

        [Fact]
        public async Task SeaCommand_ListsTheFoundPlayers()
        {
            var session = NewSession(_ => new[]
            {
                (EntityAnswer(0, 2, true, false, ("Ayame", 230, 3, 4, 75, 37, 0, 1, 0), ("Hidden", 230, 0, 0, 0, 0, 0x4000, 2, 0)), 0)
            });
            session.Parser.ActionService.SearchService = session.Parser.Search;

            var result = await session.Parser.ActionService.PlayerSearchAsync("all");

            Assert.Contains("Ayame (WhiteMage 75)", result.Message);
            Assert.Contains("Hidden", result.Message);
            Assert.Contains("2 player(s)", result.Message);
        }
    }
}
