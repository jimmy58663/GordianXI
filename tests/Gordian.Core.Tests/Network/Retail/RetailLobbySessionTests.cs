// tests/Gordian.Core.Tests/Network/Retail/RetailLobbySessionTests.cs
using System;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Gordian.Core.Network.LandSandBoat;
using Gordian.Core.Network.Lobby;
using Gordian.Core.Network.Retail;
using Xunit;

namespace Gordian.Core.Tests.Network.Retail
{
    /// <summary>The lobby session with the PlayOnline handoff against a loopback lobby that answers as spec v2.1.19 section 4.5 describes.</summary>
    public class RetailLobbySessionTests
    {
        private const uint Seed = 0x590DF1FA;
        private const uint ContentId = 0x016B6984;
        private const uint CharacterId = 0x000A915E; // the slot's id: low 16 bits 0x915E, byte 11 0x0A
        private static readonly byte[] LobbyKey = Enumerable.Range(1, 16).Select(i => (byte)(i * 7)).ToArray();
        private static readonly byte[] K8 = Convert.FromHexString("B96C1B71F21C375A");
        private static readonly byte[] Routing = Convert.FromHexString("50C30A0200C0");
        private static readonly RetailLobbyLogin Login = new(0x6AF35D54, K8, Routing, "30261102_0", 0x74F0058F);

        [Fact]
        public async Task Login_SendsTheAuthCodeAndReadsTheCharacterAndWorldLists()
        {
            await using var lobby = new FakeLobby();
            int keyRequests = 0;
            await using var session = await Connect(lobby, () => keyRequests++);

            Assert.Equal(1, keyRequests);
            Assert.Equal(2, session.Characters.Count);
            Assert.Equal("Testchar", session.Characters[0].Name);
            Assert.Equal(CharacterId, session.Characters[0].ServerId);
            Assert.True(session.Characters[1].IsEmpty);
            Assert.Single(session.Worlds);
            Assert.Equal(1u, session.ServerExpansions);

            byte[] login = lobby.Received[0];
            Assert.Equal(0x26, login[8]);
            byte[] authField = PolAuthCode.Generate(LobbyKey, Login.GreetingTimestamp, K8, Routing).Concat(new byte[12]).ToArray();
            Assert.Equal(authField, login[0x34..0x74]);
            Assert.Equal("30261102_0", Encoding.ASCII.GetString(login, 0x74, 10));

            // Each hashed request takes the next modifier: 0x1F M0+1, 0x24 M0+3.
            Assert.Equal(PolHashes.LobbySessionHash(LobbyKey, Seed + 1), lobby.Received[1][0x1C..0x2C]);
            Assert.Equal(PolHashes.LobbySessionHash(LobbyKey, Seed + 3), lobby.Received[2][0x1C..0x2C]);
            Assert.True(session.IsConnected);
        }

        [Fact]
        public async Task Select_ARefusalKeepsTheLobby_AndTheRetryTakesTheNextModifier()
        {
            await using var lobby = new FakeLobby { RefuseFirstSelect = 307 };
            await using var session = await Connect(lobby);
            var character = session.Characters[0];

            var refused = await Assert.ThrowsAsync<LobbyRequestException>(() => session.SelectCharacterAsync(character));
            Assert.Equal(307, refused.ErrorCode);
            Assert.True(session.IsConnected);

            var ticket = await session.SelectCharacterAsync(character);

            byte[] first = lobby.Received[3], second = lobby.Received[4];
            Assert.Equal(PolHashes.LobbySessionHash(LobbyKey, Seed + 5), first[0x34..0x44]);
            Assert.Equal(PolHashes.LobbySessionHash(LobbyKey, Seed + 7), second[0x34..0x44]);
            Assert.Equal(0x915Eu, BinaryPrimitives.ReadUInt32LittleEndian(second.AsSpan(0x20)));
            Assert.Equal(LobbyClientPackets.SelectionDigest(lobby.Received[0].AsSpan(0x34, 64), second.AsSpan(0x34, 16), Login.ClientChecksum, ContentId),
                second[0x48..0x58]);

            // The ticket: the map server, the raw map key (modifier + 4) and the first datagram's builder.
            Assert.Equal("127.0.0.1", ticket.ZoneIp);
            Assert.Equal(54240, ticket.ZonePort);
            Assert.Equal("127.0.0.2", ticket.SearchIp);
            Assert.Equal(54002, ticket.SearchPort);
            Assert.Equal(CharacterId, ticket.CharacterId);
            Assert.Equal(RetailMapPackets.MapRawKey(LobbyKey, Seed + 7), ticket.BlowfishKey);
            Assert.Equal(RetailMapPackets.BuildFirstDatagram(CharacterId, RetailMapPackets.MapKey(LobbyKey, Seed + 7)), ticket.LoginDatagramBuilder!(0));
            // A zone change adds 2 to the map key's counter, and the next first datagram's ticket follows it.
            byte[] afterZone = ticket.LoginDatagramBuilder!(1);
            Assert.Equal(RetailMapPackets.Ticket(RetailMapPackets.MapKey(LobbyKey, Seed + 9)),
                afterZone[(RetailMapPackets.TransportHeaderSize + 0x40)..(RetailMapPackets.TransportHeaderSize + 0x50)]);
            // The 0x00A after a zone change says the client was in the map already (+0x06 = 1); the first one does not.
            Assert.Equal(0, ticket.LoginDatagramBuilder!(0)[RetailMapPackets.TransportHeaderSize + 6]);
            Assert.Equal(1, afterZone[RetailMapPackets.TransportHeaderSize + 6]);

            Assert.False(session.IsConnected); // the server closes after 0x0B
        }

        [Fact]
        public async Task Login_ARefusedLoginThrowsItsCode()
        {
            await using var lobby = new FakeLobby { RefuseLogin = 331 };
            var ex = await Assert.ThrowsAsync<LobbyRequestException>(() => Connect(lobby));
            Assert.Equal(331, ex.ErrorCode);
        }

        [Fact]
        public async Task CreateCharacter_ChecksTheNameCreatesAndRefreshes()
        {
            await using var lobby = new FakeLobby();
            await using var session = await Connect(lobby);
            var creation = new LobbyCharacterCreation("Newchar", Race: 1, Face: 0, Hair: 0, MainJob: 1, Size: 0, Nation: 0);

            await session.CreateCharacterAsync(session.Characters[1], creation);

            byte[] check = lobby.Received[3], create = lobby.Received[4], refresh = lobby.Received[5];
            Assert.Equal(0x22, check[8]);
            Assert.Equal(PolHashes.LobbySessionHash(LobbyKey, Seed + 5), check[0x30..0x40]);
            Assert.Equal(0, check[0x50]);
            Assert.All(check[0x51..0x5F], b => Assert.Equal(0xFF, b));
            Assert.Equal(0, check[0x5F]);
            Assert.Equal(0x21, create[8]);
            Assert.Equal(PolHashes.LobbySessionHash(LobbyKey, Seed + 7), create[0x20..0x30]);
            Assert.Equal(0x1F, refresh[8]);
            Assert.Equal(PolHashes.LobbySessionHash(LobbyKey, Seed + 9), refresh[0x1C..0x2C]);
        }

        // The same LobbySession LandSandBoat uses, with the pol.exe side.
        private static Task<LobbySession> Connect(FakeLobby lobby, Action? keyRequested = null) =>
            LobbySession.ConnectAsync("127.0.0.1", lobby.Port, new PolLobbyHandoff(Login, _ =>
            {
                keyRequested?.Invoke();
                return Task.FromResult((byte[])LobbyKey.Clone());
            }), timeout: TimeSpan.FromSeconds(5));

        /// <summary>A loopback retail lobby: checks each packet's MD5, records it, and answers it.</summary>
        private sealed class FakeLobby : IAsyncDisposable
        {
            private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
            private readonly Task _serve;
            private readonly CancellationTokenSource _stop = new();
            private readonly ConcurrentQueue<byte[]> _received = new();

            public FakeLobby()
            {
                _listener.Start();
                _serve = Task.Run(ServeAsync);
            }

            public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;
            public int RefuseLogin { get; init; }
            public int RefuseFirstSelect { get; init; }
            public byte[][] Received => _received.ToArray();

            private async Task ServeAsync()
            {
                try
                {
                    using var client = await _listener.AcceptTcpClientAsync(_stop.Token);
                    var stream = client.GetStream();
                    bool refusedSelect = false;
                    while (true)
                    {
                        byte[] header = new byte[4];
                        await stream.ReadExactlyAsync(header, _stop.Token);
                        byte[] packet = new byte[BinaryPrimitives.ReadUInt32LittleEndian(header)];
                        header.CopyTo(packet, 0);
                        await stream.ReadExactlyAsync(packet.AsMemory(4), _stop.Token);
                        Assert.True(LobbyClientPackets.Verify(packet));
                        _received.Enqueue(packet);

                        byte[] reply;
                        switch (packet[8])
                        {
                            case 0x26 when RefuseLogin != 0: reply = Error(RefuseLogin); break;
                            case 0x26: reply = Reply(0x05, 0x28, p => { Put(p, 0x1C, Seed); Put(p, 0x20, 1); Put(p, 0x24, 2); }); break;
                            case 0x1F: reply = CharacterList(); break;
                            case 0x24: reply = Reply(0x23, 0x34, p => { Put(p, 0x1C, 1); Put(p, 0x20, 0x65); Ascii(p, 0x24, "Shiva"); }); break;
                            case 0x22:
                            case 0x21: reply = Reply(0x03, 0x20, p => Put(p, 0x1C, 0x0C)); break;
                            case 0x07 when RefuseFirstSelect != 0 && !refusedSelect:
                                refusedSelect = true;
                                reply = Error(RefuseFirstSelect);
                                break;
                            case 0x07:
                                reply = Reply(0x0B, 0x48, p =>
                                {
                                    Put(p, 0x1C, ContentId);
                                    Put(p, 0x20, CharacterId);
                                    Ascii(p, 0x24, "Testchar");
                                    new byte[] { 127, 0, 0, 1 }.CopyTo(p, 0x38);
                                    Put(p, 0x3C, 54240);
                                    new byte[] { 127, 0, 0, 2 }.CopyTo(p, 0x40);
                                    Put(p, 0x44, 54002);
                                });
                                await stream.WriteAsync(reply, _stop.Token);
                                return; // the server closes after 0x0B
                            default: return;
                        }
                        await stream.WriteAsync(reply, _stop.Token);
                    }
                }
                catch (Exception ex) when (ex is OperationCanceledException or System.IO.IOException or System.IO.EndOfStreamException) { }
            }

            private static byte[] CharacterList() => Reply(0x20, 0x20 + 2 * 0x8C, p =>
            {
                Put(p, 0x1C, 2);
                int at = 0x20;
                Put(p, at, ContentId);
                BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(at + 4), 0x915E);
                BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(at + 6), 0x65);
                p[at + 8] = 1;
                p[at + 11] = 0x0A;
                Ascii(p, at + 12, "Testchar");
                Ascii(p, at + 28, "Shiva");
                at += 0x8C;
                Put(p, at, ContentId + 1);
                p[at + 8] = 1;
                Ascii(p, at + 12, " ");
                Ascii(p, at + 28, " ");
            });

            private static byte[] Error(int code) => Reply(0x04, 0x24, p => Put(p, 0x20, (uint)code));

            private static byte[] Reply(byte command, int size, Action<byte[]> fill)
            {
                byte[] packet = new byte[size];
                LobbyClientPackets.WriteHeader(packet, command);
                fill(packet);
                LobbyClientPackets.Seal(packet);
                return packet;
            }

            private static void Put(byte[] packet, int at, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(at), value);
            private static void Ascii(byte[] packet, int at, string text) => Encoding.ASCII.GetBytes(text).CopyTo(packet, at);

            public async ValueTask DisposeAsync()
            {
                _stop.Cancel();
                _listener.Stop();
                try { await _serve; } catch { }
                _stop.Dispose();
            }
        }
    }
}
