// src/Gordian.Core/Network/SessionNetworkManager.cs
using System;
using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Gordian.Core.Config;
using Gordian.Core.Network.Compression;
using Gordian.Core.Network.Crypto;

namespace Gordian.Core.Network
{
    /// <summary>
    /// Manages the low-level asynchronous UDP connection pipeline, inbound parsing loops,
    /// and a high-performance outbound chunk-bundling ring queue for a single character session.
    /// </summary>
    public sealed class SessionNetworkManager : IDisposable
    {
        private const int FfxiHeaderSize = 28;
        private const int MaxDatagramSize = 1360; // FFXI MTU-safe datagram window
        private const int NetworkTickIntervalMs = 250; // 4Hz standard FFXI update tick frequency

        private readonly string _serverAddress;
        private readonly int _serverPort;
        private readonly PacketParser _parser;
        private readonly FfxiCodec _codec;

        private Socket? _udpSocket;
        private EndPoint? _serverEndpoint;
        private CancellationTokenSource? _cts;
        private Task? _readTask;
        private Task? _writeFlushTask;
        private bool _isDisposed;

        // ?? Outbound Network Buffering
        private readonly SemaphoreSlim _writeLock = new SemaphoreSlim(1, 1);
        private readonly byte[] _outboundQueueBuffer = new byte[MaxDatagramSize];
        private int _currentBufferLength = 0;

        private ushort _serverPacketIdSequence = 0;
        private ushort _clientPacketIdSequence = 0;

        /// <summary>
        /// Gets the isolated, instance-level configuration matrix for this specific character session.
        /// </summary>
        public SessionProfile Profile { get; } = new SessionProfile();

        /// <summary>
        /// Gets the real-time operational lifecycle status of this active network connection.
        /// </summary>
        public SessionState CurrentState { get; internal set; } = SessionState.Disconnected;

        /// <summary>
        /// Gets the packet parser handling this session.
        /// </summary>
        public PacketParser Parser => _parser;

        /// <summary>
        /// Unique Character ID assigned by the server database.
        /// </summary>
        public uint CharacterId { get; set; }

        /// <summary>
        /// Display name of the active character.
        /// </summary>
        public string CharacterName { get; set; } = string.Empty;

        /// <summary>
        /// Account login username.
        /// </summary>
        public string AccountName { get; set; } = string.Empty;

        /// <summary>
        /// Optional authentication session ticket.
        /// </summary>
        public byte[] Ticket { get; set; } = Array.Empty<byte>();

        /// <summary>
        /// Raised whenever a sub-packet is parsed from an inbound stream or queued for outbound dispatch.
        /// </summary>
        public event EventHandler<PacketLogEntry>? PacketInspected;

        public SessionNetworkManager(
            string serverAddress,
            int serverPort,
            IPacketCryptoSuite? cryptoSuite = null,
            FfxiCodec? codec = null)
        {
            _serverAddress = serverAddress ?? throw new ArgumentNullException(nameof(serverAddress));
            _serverPort = serverPort;
            _codec = codec ?? FfxiCodec.Default;
            _parser = new PacketParser(this.Profile, this.QueueChunkAsync, cryptoSuite, _codec);
            _parser.PacketInspected += (s, e) => PacketInspected?.Invoke(this, e);
            _parser.HandshakeCompleted += () => CurrentState = SessionState.ActiveInWorld;
        }

        /// <summary>
        /// Establishes the non-blocking UDP socket and initializes background processing worker tasks.
        /// </summary>
        public async Task ConnectAsync()
        {
            if (CurrentState != SessionState.Disconnected)
            {
                throw new InvalidOperationException("This network session is already active or processing an authentication task.");
            }

            _cts = new CancellationTokenSource();

            try
            {
                CurrentState = SessionState.ConnectingToGameServer;

                // Resolve target server IP
                IPAddress targetIp;
                if (!IPAddress.TryParse(_serverAddress, out targetIp!))
                {
                    IPAddress[] addresses = await Dns.GetHostAddressesAsync(_serverAddress, _cts.Token).ConfigureAwait(false);
                    if (addresses.Length == 0)
                    {
                        throw new SocketException((int)SocketError.HostNotFound);
                    }
                    targetIp = addresses[0];
                }

                _serverEndpoint = new IPEndPoint(targetIp, _serverPort);
                _udpSocket = new Socket(_serverEndpoint.AddressFamily, SocketType.Dgram, ProtocolType.Udp);

                // Bind client socket to any available local port
                EndPoint localBind = new IPEndPoint(_serverEndpoint.AddressFamily == AddressFamily.InterNetworkV6 ? IPAddress.IPv6Any : IPAddress.Any, 0);
                _udpSocket.Bind(localBind);

                // 1. Send the initial unencrypted 0x00A login handshake datagram
                if (CharacterId != 0 || !string.IsNullOrEmpty(CharacterName))
                {
                    byte[] loginDatagram = HandshakePackets.BuildLoginDatagram(
                        CharacterId,
                        CharacterName,
                        AccountName,
                        Ticket,
                        clientVersion: 1,
                        clientPacketSeq: 1
                    );

                    ReadOnlySpan<byte> loginSubPacket = loginDatagram.AsSpan(HandshakePackets.FfxiHeaderSize, HandshakePackets.LoginSubPacketSize);
                    _parser.LogPacket(PacketDirection.Outbound, 0x00A, 0, loginSubPacket);

                    await _udpSocket.SendToAsync(loginDatagram, SocketFlags.None, _serverEndpoint, _cts.Token).ConfigureAwait(false);
                }

                CurrentState = SessionState.ExchangingCryptoKeys;

                // Launch parallel background workers
                _readTask = Task.Run(() => InboundNetworkReadLoopAsync(_udpSocket, _cts.Token), _cts.Token);
                _writeFlushTask = Task.Run(() => OutboundNetworkFlushLoopAsync(_udpSocket, _cts.Token), _cts.Token);
            }
            catch (Exception)
            {
                Disconnect();
                throw;
            }
        }

        /// <summary>
        /// Bitpacks a logical command chunk into the outbound staging buffer queue thread-safely.
        /// Can be flagged as high priority to bypass the standard network tick interval and flush instantly.
        /// </summary>
        public async Task QueueChunkAsync(ReadOnlyMemory<byte> chunkData, bool isHighPriority = false)
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);

            if (CurrentState == SessionState.Disconnected || _udpSocket == null || _serverEndpoint == null)
            {
                throw new InvalidOperationException("Cannot queue action payloads; the network socket is disconnected.");
            }

            await _writeLock.WaitAsync().ConfigureAwait(false);
            try
            {
                if (_currentBufferLength + chunkData.Length > _outboundQueueBuffer.Length)
                {
                    await FlushBundledPacketAsync(_udpSocket, _serverEndpoint, _cts!.Token).ConfigureAwait(false);
                }

                chunkData.Span.CopyTo(_outboundQueueBuffer.AsSpan(_currentBufferLength));
                _currentBufferLength += chunkData.Length;

                if (isHighPriority)
                {
                    await FlushBundledPacketAsync(_udpSocket, _serverEndpoint, _cts!.Token).ConfigureAwait(false);
                }
            }
            finally
            {
                _writeLock.Release();
            }
        }

        /// <summary>
        /// Consolidates, compresses, encrypts, and transmits all bundled staging chunks down the UDP socket.
        /// </summary>
        private async Task FlushBundledPacketAsync(Socket socket, EndPoint remoteEndpoint, CancellationToken token)
        {
            if (_currentBufferLength == 0) return;

            try
            {
                using var scratchBuffer = new PacketBuffer(MaxDatagramSize);
                ReadOnlySpan<byte> bundledChunks = _outboundQueueBuffer.AsSpan(0, _currentBufferLength);

                // 1. Compress the bundled sub-packets starting after 28-byte FFXI header
                Span<byte> compressionDestination = scratchBuffer.WritableData.Slice(FfxiHeaderSize);
                int compressedBytes = _codec.Compress(bundledChunks, compressionDestination);

                // 2. Build 28-byte FFXI header
                Span<byte> headerSpan = scratchBuffer.WritableData.Slice(0, FfxiHeaderSize);
                headerSpan.Clear();

                BinaryPrimitives.WriteUInt16LittleEndian(headerSpan.Slice(0, 2), _serverPacketIdSequence);
                BinaryPrimitives.WriteUInt16LittleEndian(headerSpan.Slice(2, 2), ++_clientPacketIdSequence);

                uint timestamp = (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                BinaryPrimitives.WriteUInt32LittleEndian(headerSpan.Slice(8, 4), timestamp);

                // 3. Encrypt and sign payload + trailing MD5 hash in-place
                int datagramLength = _parser.CryptoSuite.EncryptAndSign(
                    scratchBuffer.WritableData,
                    FfxiHeaderSize,
                    compressedBytes
                );

                // 4. Send datagram over UDP wire
                ReadOnlyMemory<byte> datagramMemory = scratchBuffer.WritableMemory.Slice(0, datagramLength);
                await socket.SendToAsync(datagramMemory, SocketFlags.None, remoteEndpoint, token).ConfigureAwait(false);
            }
            finally
            {
                _currentBufferLength = 0;
            }
        }

        private async Task OutboundNetworkFlushLoopAsync(Socket socket, CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    await Task.Delay(NetworkTickIntervalMs, token).ConfigureAwait(false);

                    if (_serverEndpoint == null) continue;

                    await _writeLock.WaitAsync(token).ConfigureAwait(false);
                    try
                    {
                        await FlushBundledPacketAsync(socket, _serverEndpoint, token).ConfigureAwait(false);
                    }
                    finally
                    {
                        _writeLock.Release();
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[NET_TRACE] Outbound UDP flush failure: {ex.Message}");
            }
        }

        private async Task InboundNetworkReadLoopAsync(Socket socket, CancellationToken token)
        {
            const int readBufferSize = 2048;

            try
            {
                while (!token.IsCancellationRequested)
                {
                    using var packetBuffer = new PacketBuffer(readBufferSize);
                    SocketReceiveFromResult result = await socket.ReceiveFromAsync(
                        packetBuffer.WritableMemory,
                        SocketFlags.None,
                        _serverEndpoint!,
                        token
                    ).ConfigureAwait(false);

                    if (result.ReceivedBytes == 0) break;

                    Span<byte> activeChunk = packetBuffer.WritableData.Slice(0, result.ReceivedBytes);

                    // Track server packet ID sequence from incoming datagram header
                    if (activeChunk.Length >= 2)
                    {
                        _serverPacketIdSequence = BinaryPrimitives.ReadUInt16LittleEndian(activeChunk.Slice(0, 2));
                    }

                    _parser.ProcessIncomingChunk(activeChunk);

                    if (CurrentState == SessionState.LoadingWorldData)
                    {
                        CurrentState = SessionState.ActiveInWorld;
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[NET_TRACE] Inbound UDP read loop failure: {ex.Message}");
            }
            finally
            {
                CurrentState = SessionState.Disconnecting;
                ExecuteSocketCleanup();
                CurrentState = SessionState.Disconnected;
            }
        }

        public void Disconnect()
        {
            if (CurrentState == SessionState.Disconnected) return;

            CurrentState = SessionState.Disconnecting;
            _cts?.Cancel();
            ExecuteSocketCleanup();
            CurrentState = SessionState.Disconnected;
        }

        private void ExecuteSocketCleanup()
        {
            _udpSocket?.Dispose();
            _udpSocket = null;

            _cts?.Dispose();
            _cts = null;
        }

        public void Dispose()
        {
            if (_isDisposed) return;

            Disconnect();
            _parser.CryptoSuite.Dispose();
            _writeLock.Dispose();
            _isDisposed = true;
        }
    }
}
