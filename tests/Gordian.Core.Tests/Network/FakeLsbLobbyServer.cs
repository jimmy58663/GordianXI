// tests/Gordian.Core.Tests/Network/FakeLsbLobbyServer.cs
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Gordian.Core.Network.LandSandBoat;

namespace Gordian.Core.Tests.Network
{
    /// <summary>
    /// A loopback stand-in for LandSandBoat's xi_data / xi_view lobby services, written from the wire behaviour
    /// documented in docs/network/session-and-packets.md (LandSandBoat src/login/view_session.cpp and
    /// data_session.cpp): which channel answers which request, the free-slot convention, the error replies, the
    /// connections it drops and the session-key accounting. One client session at a time.
    /// </summary>
    internal sealed class FakeLsbLobbyServer : IDisposable
    {
        public sealed class Character
        {
            public uint Id;
            public string Name = string.Empty;
            public byte Race = 1, Face, Job = 1, Size, Nation;
            public ushort Zone = 230;
            public ushort Head, Body;
            public bool Rename;
        }

        private readonly TcpListener _data = new(IPAddress.Loopback, 0);
        private readonly TcpListener _view = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _cts = new();
        private NetworkStream? _dataStream, _viewStream;
        private TcpClient? _viewClient;
        private uint _requestedCharacter;
        private string _requestedNewName = string.Empty;
        private bool _justCreated;
        private int _increment;
        private readonly object _sync = new();

        public FakeLsbLobbyServer(uint accountId, byte[] sessionHash)
        {
            AccountId = accountId;
            SessionHash = sessionHash;
            _data.Start();
            _view.Start();
            _ = AcceptDataAsync();
            _ = AcceptViewAsync();
        }

        public uint AccountId { get; }
        public byte[] SessionHash { get; }
        public int DataPort => ((IPEndPoint)_data.LocalEndpoint).Port;
        public int ViewPort => ((IPEndPoint)_view.LocalEndpoint).Port;
        public int ContentIds { get; set; } = 4;
        public string WorldName { get; set; } = "Gordian";
        public bool VersionLocked { get; set; }
        public bool DeletionEnabled { get; set; } = true;
        public bool AlreadyLoggedInOnce { get; set; }

        /// <summary>
        /// LandSandBoat's lost request after a deletion: its view session writes the delete OK before the database work and
        /// clears its read buffer when that write completes, so a request that arrived in between is read as zeros and
        /// dropped without a reply. When set, the first view request after a deletion is dropped that way.
        /// </summary>
        public bool LoseRequestAfterDelete { get; set; }

        /// <summary>When set, the first view request after a deletion is answered only after this delay (a slow server).</summary>
        public int DelayRequestAfterDeleteMs { get; set; }
        private bool _delayNextRequest;
        private bool _dropNextRequest;
        public int DroppedRequests { get; private set; }
        public List<Character> Characters { get; } = new();
        public List<byte> ViewCommands { get; } = new();
        public List<byte> DataCommands { get; } = new();

        /// <summary>The key the server stored for the map session (the 0xA2 key with byte 16 raised).</summary>
        public byte[]? StoredKey { get; private set; }

        private async Task AcceptDataAsync()
        {
            try
            {
                var client = await _data.AcceptTcpClientAsync(_cts.Token);
                _dataStream = client.GetStream();
                var buffer = new byte[28];
                while (!_cts.IsCancellationRequested)
                {
                    if (!await ReadExactly(_dataStream, buffer)) return;
                    lock (_sync) DataCommands.Add(buffer[0]);
                    switch (buffer[0])
                    {
                        case 0xFE:
                            break;
                        case 0xA1:
                            if (BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(1)) != AccountId) break;
                            await SendCharacterListAsync();
                            break;
                        case 0xA2:
                            await HandleKeyAsync(buffer);
                            break;
                    }
                }
            }
            catch (Exception) { }
        }

        private async Task AcceptViewAsync()
        {
            try
            {
                while (!_cts.IsCancellationRequested)
                {
                    _viewClient = await _view.AcceptTcpClientAsync(_cts.Token);
                    _viewStream = _viewClient.GetStream();
                    await ServeViewAsync(_viewStream);
                }
            }
            catch (Exception) { }
        }

        private async Task ServeViewAsync(NetworkStream stream)
        {
            var header = new byte[4];
            while (!_cts.IsCancellationRequested)
            {
                if (!await ReadExactly(stream, header)) return;
                int size = (int)BinaryPrimitives.ReadUInt32LittleEndian(header);
                var packet = new byte[size];
                header.CopyTo(packet, 0);
                if (!await ReadExactly(stream, packet.AsMemory(4))) return;
                if (!packet.AsSpan(12, 16).SequenceEqual(SessionHash)) return;
                if (_delayNextRequest)
                {
                    _delayNextRequest = false;
                    await Task.Delay(DelayRequestAfterDeleteMs);
                }
                if (_dropNextRequest)
                {
                    _dropNextRequest = false;
                    DroppedRequests++;
                    continue;
                }
                byte command = packet[8];
                lock (_sync) ViewCommands.Add(command);
                switch (command)
                {
                    case 0x26:
                        if (VersionLocked) { await WriteView(Error(331)); break; }
                        var key = Header(0x28, 0x05);
                        BinaryPrimitives.WriteUInt32LittleEndian(key.AsSpan(28), 0xAD5DE04F);
                        BinaryPrimitives.WriteUInt32LittleEndian(key.AsSpan(32), 0x0FFF);
                        BinaryPrimitives.WriteUInt32LittleEndian(key.AsSpan(36), 0x04);
                        await WriteView(key);
                        break;
                    case 0x1F:
                        await WriteData(new byte[] { 0x01, 0, 0, 0, 0 });
                        break;
                    case 0x24:
                        var worlds = Header(32 + 20, 0x23);
                        BinaryPrimitives.WriteUInt32LittleEndian(worlds.AsSpan(28), 1);
                        BinaryPrimitives.WriteUInt32LittleEndian(worlds.AsSpan(32), 0x20);
                        Encoding.ASCII.GetBytes(WorldName).CopyTo(worlds, 36);
                        await WriteView(worlds);
                        break;
                    case 0x22:
                        {
                            string name = ReadName(packet, 32);
                            if (!ValidName(name)) { await WriteView(Error(313)); break; }
                            _requestedNewName = name;
                            await WriteView(Header(0x20, 0x03));
                            break;
                        }
                    case 0x21:
                        {
                            byte race = packet[48], size2 = packet[57], face = packet[60], nation = packet[54];
                            if (!ValidName(_requestedNewName) || race is < 1 or > 8 || size2 > 2 || face > 15 || nation > 2) { _viewClient?.Close(); return; }
                            lock (_sync)
                            {
                                Characters.Add(new Character
                                {
                                    Id = (Characters.Count == 0 ? 100u : Characters.Max(c => c.Id) + 1),
                                    Name = _requestedNewName, Race = race, Face = face, Job = Math.Clamp(packet[50], (byte)1, (byte)6), Size = size2, Nation = nation,
                                });
                            }
                            _justCreated = true;
                            _requestedNewName = string.Empty;
                            await WriteView(Header(0x20, 0x03));
                            break;
                        }
                    case 0x14:
                        {
                            if (!DeletionEnabled) { await WriteView(Error(332)); break; }
                            await WriteView(Header(0x20, 0x03));
                            uint id = BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(28));
                            lock (_sync) Characters.RemoveAll(c => c.Id == id);
                            _increment += 4;
                            if (LoseRequestAfterDelete) _dropNextRequest = true;
                            if (DelayRequestAfterDeleteMs > 0) _delayNextRequest = true;
                            break;
                        }
                    case 0x28:
                        {
                            uint id = BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(28));
                            string name = ReadName(packet, 36);
                            var character = Characters.FirstOrDefault(c => c.Id == id);
                            if (character == null || !character.Rename) { _viewClient?.Close(); return; }
                            if (!ValidName(name)) { await WriteView(Error(313)); break; }
                            character.Name = name;
                            character.Rename = false;
                            _requestedCharacter = id;
                            _increment += 4;
                            await WriteView(Header(0x20, 0x03));
                            break;
                        }
                    case 0x07:
                        {
                            uint id = BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(28));
                            string name = ReadName(packet, 36);
                            if (!Characters.Any(c => c.Id == id && c.Name == name)) { _viewClient?.Close(); return; }
                            _requestedCharacter = id;
                            await WriteData(new byte[] { 0x02, 0, 0, 0, 0 });
                            break;
                        }
                }
            }
        }

        private bool ValidName(string name) =>
            name.Length is >= 3 and <= 15 && name.All(char.IsAsciiLetter) && !Characters.Any(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));

        private async Task HandleKeyAsync(byte[] a2)
        {
            if (AlreadyLoggedInOnce)
            {
                AlreadyLoggedInOnce = false;
                _increment += 1;
                await WriteView(Error(201));
                return;
            }
            var key = a2.AsSpan(1, 20).ToArray();
            if (_justCreated) key[16] += 6;
            key[16] += (byte)_increment;
            StoredKey = key;
            var character = Characters.First(c => c.Id == _requestedCharacter);
            var next = Header(0x48, 0x0B);
            BinaryPrimitives.WriteUInt32LittleEndian(next.AsSpan(28), character.Id);
            BinaryPrimitives.WriteUInt32LittleEndian(next.AsSpan(32), character.Id & 0xFFFF);
            Encoding.ASCII.GetBytes(character.Name).CopyTo(next, 36);
            next[56] = 127; next[59] = 1;
            BinaryPrimitives.WriteUInt32LittleEndian(next.AsSpan(60), 54230);
            await WriteView(next);
            _increment = 0;
            _justCreated = false;
            _viewClient?.Close();
        }

        private async Task SendCharacterListAsync()
        {
            List<Character> characters;
            lock (_sync) characters = Characters.ToList();
            var ids = new byte[0x148];
            ids[0] = 0x03;
            int total = Math.Max(characters.Count, ContentIds);
            ids[1] = (byte)total;
            var list = Header(32 + 140 * total, 0x20);
            BinaryPrimitives.WriteUInt32LittleEndian(list.AsSpan(28), (uint)total);
            for (int i = 0; i < total; i++)
            {
                int at = 32 + i * 140;
                if (i < characters.Count)
                {
                    var c = characters[i];
                    BinaryPrimitives.WriteUInt32LittleEndian(ids.AsSpan(16 * (i + 1)), c.Id);
                    BinaryPrimitives.WriteUInt32LittleEndian(list.AsSpan(at), c.Id);
                    BinaryPrimitives.WriteUInt16LittleEndian(list.AsSpan(at + 4), (ushort)(c.Id & 0xFFFF));
                    BinaryPrimitives.WriteUInt16LittleEndian(list.AsSpan(at + 8), 1);
                    list[at + 10] = (byte)(c.Rename ? 1 : 0);
                    list[at + 11] = (byte)((c.Id >> 16) & 0xFF);
                    Encoding.ASCII.GetBytes(c.Name).CopyTo(list, at + 12);
                    Encoding.ASCII.GetBytes(WorldName).CopyTo(list, at + 28);
                    int info = at + 44;
                    BinaryPrimitives.WriteUInt16LittleEndian(list.AsSpan(info), c.Race);
                    list[info + 2] = c.Job;
                    BinaryPrimitives.WriteUInt16LittleEndian(list.AsSpan(info + 4), c.Face);
                    list[info + 6] = c.Nation;
                    list[info + 8] = c.Face;
                    list[info + 9] = c.Size;
                    // LandSandBoat's raw look values: the combined face, then model ids without slot bits.
                    BinaryPrimitives.WriteUInt16LittleEndian(list.AsSpan(info + 12), c.Face);
                    BinaryPrimitives.WriteUInt16LittleEndian(list.AsSpan(info + 14), c.Head);
                    BinaryPrimitives.WriteUInt16LittleEndian(list.AsSpan(info + 16), c.Body);
                    list[info + 28] = (byte)c.Zone;
                    list[info + 29] = 1;
                    list[info + 35] = (byte)((c.Zone >> 8) & 1);
                }
                else
                {
                    BinaryPrimitives.WriteUInt16LittleEndian(list.AsSpan(at + 8), 1);
                    list[at + 12] = 0x20; // a free slot: a one-space name
                }
            }
            await WriteData(ids);
            await WriteView(list);
        }

        private static byte[] Header(int size, byte command)
        {
            var packet = new byte[size];
            BinaryPrimitives.WriteUInt32LittleEndian(packet, (uint)size);
            BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(4), LobbyPackets.Terminator);
            packet[8] = command;
            return packet;
        }

        private static byte[] Error(ushort code)
        {
            var packet = Header(0x24, 0x04);
            packet[28] = 0x10;
            BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(32), code);
            return packet;
        }

        private static string ReadName(byte[] packet, int offset)
        {
            int end = Array.IndexOf(packet, (byte)0, offset, 16);
            return Encoding.ASCII.GetString(packet, offset, (end < 0 ? offset + 15 : end) - offset);
        }

        private async Task WriteView(byte[] packet)
        {
            if (_viewStream == null) return;
            await _viewStream.WriteAsync(packet);
        }

        private async Task WriteData(byte[] packet)
        {
            if (_dataStream == null) return;
            await _dataStream.WriteAsync(packet);
        }

        private static async Task<bool> ReadExactly(NetworkStream stream, Memory<byte> buffer)
        {
            int read = 0;
            while (read < buffer.Length)
            {
                int n;
                try { n = await stream.ReadAsync(buffer[read..]); }
                catch (Exception) { return false; }
                if (n == 0) return false;
                read += n;
            }
            return true;
        }

        public void Dispose()
        {
            _cts.Cancel();
            _data.Stop();
            _view.Stop();
            _viewClient?.Dispose();
        }
    }
}
