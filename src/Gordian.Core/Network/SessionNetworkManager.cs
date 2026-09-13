// src/Gordian.Core/Network/SessionNetworkManager.cs
using System;
using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Gordian.Core.Diagnostics;
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
        /// Raised when the session lifecycle state changes.
        /// </summary>
        public event EventHandler<SessionState>? StateChanged;

        private SessionState _currentState = SessionState.Disconnected;

        /// <summary>
        /// Gets the real-time operational lifecycle status of this active network connection.
        /// </summary>
        public SessionState CurrentState
        {
            get => _currentState;
            internal set
            {
                if (_currentState != value)
                {
                    _currentState = value;
                    GordianLog.Debug("NET", $"Session state changed to: {_currentState}");
                    StateChanged?.Invoke(this, _currentState);
                }
            }
        }

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
        /// Current X position coordinate in world space.
        /// </summary>
        public float PositionX { get; set; }

        /// <summary>
        /// Current Y (altitude/height) position coordinate in world space.
        /// </summary>
        public float PositionY { get; set; }

        /// <summary>
        /// Current Z position coordinate in world space.
        /// </summary>
        public float PositionZ { get; set; }

        /// <summary>
        /// Current character facing direction / rotation (0..255).
        /// </summary>
        public byte Direction { get; set; }

        /// <summary>
        /// Current target index / actor index in zone.
        /// </summary>
        public ushort TargetIndex { get; set; }

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
            _parser = new PacketParser(this.Profile, this.QueueChunkAsync, cryptoSuite, _codec)
            {
                LogOutboundOnRoute = false
            };
            _parser.PacketInspected += (s, e) => PacketInspected?.Invoke(this, e);
            _parser.HandshakeCompleted += () => CurrentState = SessionState.ActiveInWorld;
            _parser.PlayerPositionUpdated += (x, y, z, dir, actIndex) =>
            {
                PositionX = x;
                PositionY = y;
                PositionZ = z;
                Direction = dir;
                TargetIndex = actIndex;
                GordianLog.Debug("NET", $"Initial position captured: X={x:F2}, Y={y:F2}, Z={z:F2}, Dir={dir}, TargetIndex={actIndex}");
            };
        }

        /// <summary>
        /// Establishes the non-blocking UDP socket and initializes background processing worker tasks.
        /// </summary>
        public async Task ConnectAsync()
        {
            if (CurrentState != SessionState.Disconnected && CurrentState != SessionState.ConnectingToGameServer)
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

                // On Windows, disable SIO_UDP_CONNRESET so ICMP Port Unreachable packets do not trigger WSAECONNRESET (10054)
                if (OperatingSystem.IsWindows())
                {
                    const int SIO_UDP_CONNRESET = -1744830452;
                    try
                    {
                        _udpSocket.IOControl((IOControlCode)SIO_UDP_CONNRESET, new byte[] { 0, 0, 0, 0 }, null);
                    }
                    catch
                    {
                        // Non-critical if unsupported by network interface
                    }
                }

                // Bind client socket to any available local port
                EndPoint localBind = new IPEndPoint(_serverEndpoint.AddressFamily == AddressFamily.InterNetworkV6 ? IPAddress.IPv6Any : IPAddress.Any, 0);
                _udpSocket.Bind(localBind);

                GordianLog.Info("NET", $"UDP socket bound to {_udpSocket.LocalEndPoint}. Target map server: {_serverEndpoint}");

                // Launch parallel background workers before transmitting initial datagram
                _readTask = Task.Run(() => InboundNetworkReadLoopAsync(_udpSocket, _cts.Token), _cts.Token);
                _writeFlushTask = Task.Run(() => OutboundNetworkFlushLoopAsync(_udpSocket, _cts.Token), _cts.Token);

                // Send the initial unencrypted 0x00A login handshake datagram with retransmission
                if (CharacterId != 0 || !string.IsNullOrEmpty(CharacterName))
                {
                    _clientPacketIdSequence = 1;
                    byte[] loginDatagram = HandshakePackets.BuildLoginDatagram(
                        CharacterId,
                        CharacterName,
                        AccountName,
                        Ticket,
                        clientVersion: 1,
                        clientPacketSeq: _clientPacketIdSequence
                    );

                    ReadOnlySpan<byte> loginSubPacket = loginDatagram.AsSpan(HandshakePackets.FfxiHeaderSize, HandshakePackets.LoginSubPacketSize);
                    _parser.LogPacket(PacketDirection.Outbound, 0x00A, _clientPacketIdSequence, loginSubPacket);

                    CurrentState = SessionState.ExchangingCryptoKeys;

                    // Periodically retransmit 0x00A until server responds or session moves to ActiveInWorld
                    _ = Task.Run(async () =>
                    {
                        int attempt = 0;
                        while (!_cts.IsCancellationRequested && CurrentState == SessionState.ExchangingCryptoKeys && attempt < 25)
                        {
                            attempt++;
                            GordianLog.Debug("NET", $"Transmitting 0x00A login handshake attempt #{attempt} ({loginDatagram.Length} bytes) to {_serverEndpoint} for '{CharacterName}' (ID: {CharacterId})...");
                            try
                            {
                                await _udpSocket.SendToAsync(loginDatagram, SocketFlags.None, _serverEndpoint, _cts.Token).ConfigureAwait(false);
                            }
                            catch (Exception ex)
                            {
                                GordianLog.Warning("NET", $"Failed to send 0x00A attempt #{attempt}: {ex.Message}");
                                break;
                            }

                            // Wait 500ms between attempts for server to process and respond
                            try
                            {
                                await Task.Delay(500, _cts.Token).ConfigureAwait(false);
                            }
                            catch (OperationCanceledException)
                            {
                                break;
                            }
                        }
                    }, _cts.Token);
                }
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
                    throw new InvalidOperationException("Outbound staging queue buffer overflow.");
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

                // 1. Advance client packet sequence number
                ushort clientSeq = ++_clientPacketIdSequence;

                // 2. Patch sequenceId (offset 2..3) of each bundled sub-packet with clientSeq and log outbound subpacket
                int subOffset = 0;
                while (subOffset + 4 <= _currentBufferLength)
                {
                    int subSize = (_outboundQueueBuffer[subOffset + 1] & 0xFE) * 2;
                    if (subSize < 4 || subOffset + subSize > _currentBufferLength) break;

                    BinaryPrimitives.WriteUInt16LittleEndian(_outboundQueueBuffer.AsSpan(subOffset + 2, 2), clientSeq);

                    ushort rawTypeAndSize = BinaryPrimitives.ReadUInt16LittleEndian(_outboundQueueBuffer.AsSpan(subOffset, 2));
                    ushort packetId = (ushort)(rawTypeAndSize & 0x1FF);
                    ReadOnlySpan<byte> fullSubPacket = _outboundQueueBuffer.AsSpan(subOffset, subSize);
                    _parser.LogPacket(PacketDirection.Outbound, packetId, clientSeq, fullSubPacket);

                    subOffset += subSize;
                }

                ReadOnlySpan<byte> bundledChunks = _outboundQueueBuffer.AsSpan(0, _currentBufferLength);

                // 3. Compress the bundled sub-packets starting after 28-byte FFXI header
                Span<byte> compressionDestination = scratchBuffer.WritableData.Slice(FfxiHeaderSize);
                int compressedBytes = _codec.Compress(bundledChunks, compressionDestination);

                // 4. Build 28-byte FFXI header
                // Byte 0..1: ClientPacketId (Client outgoing sequence)
                // Byte 2..3: ServerPacketId (ACK of last received server packet)
                Span<byte> headerSpan = scratchBuffer.WritableData.Slice(0, FfxiHeaderSize);
                headerSpan.Clear();

                BinaryPrimitives.WriteUInt16LittleEndian(headerSpan.Slice(0, 2), clientSeq);
                BinaryPrimitives.WriteUInt16LittleEndian(headerSpan.Slice(2, 2), _serverPacketIdSequence);

                uint timestamp = (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                BinaryPrimitives.WriteUInt32LittleEndian(headerSpan.Slice(8, 4), timestamp);

                // 5. Encrypt and sign payload + trailing MD5 hash in-place
                int datagramLength = _parser.CryptoSuite.EncryptAndSign(
                    scratchBuffer.WritableData,
                    FfxiHeaderSize,
                    compressedBytes
                );

                // 6. Send datagram over UDP wire
                ReadOnlyMemory<byte> datagramMemory = scratchBuffer.WritableMemory.Slice(0, datagramLength);
                await socket.SendToAsync(datagramMemory, SocketFlags.None, remoteEndpoint, token).ConfigureAwait(false);
                GordianLog.Debug("NET", $"Outbound UDP datagram transmitted: Seq={clientSeq}, Ack={_serverPacketIdSequence}, {datagramLength} bytes to {remoteEndpoint}");
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
                        // In ActiveInWorld or LoadingWorldData, if no outbound packets are queued,
                        // generate a 0x015 GP_CLI_POS keepalive heartbeat datagram.
                        if (_currentBufferLength == 0 && (CurrentState == SessionState.ActiveInWorld || CurrentState == SessionState.LoadingWorldData))
                        {
                            byte[] posPacket = HandshakePackets.BuildPosPingPongSubPacket(
                                sequenceId: 0, // will be stamped to clientSeq in FlushBundledPacketAsync
                                x: PositionX,
                                y: PositionY,
                                z: PositionZ,
                                dir: Direction
                            );

                            posPacket.CopyTo(_outboundQueueBuffer.AsSpan());
                            _currentBufferLength = posPacket.Length;
                        }

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
                GordianLog.Error("NET", "Outbound UDP flush failure", ex);
            }
        }

        private async Task InboundNetworkReadLoopAsync(Socket socket, CancellationToken token)
        {
            const int readBufferSize = 2048;

            try
            {
                EndPoint anySender = new IPEndPoint(IPAddress.Any, 0);
                GordianLog.Debug("NET", $"InboundNetworkReadLoopAsync started on local endpoint {socket.LocalEndPoint}");
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        using var packetBuffer = new PacketBuffer(readBufferSize);
                        SocketReceiveFromResult result = await socket.ReceiveFromAsync(
                            packetBuffer.WritableMemory,
                            SocketFlags.None,
                            anySender,
                            token
                        ).ConfigureAwait(false);

                        if (result.ReceivedBytes == 0)
                        {
                            GordianLog.Warning("NET", $"Inbound UDP datagram of 0 bytes received from {result.RemoteEndPoint}; ignoring.");
                            continue;
                        }

                        GordianLog.Debug("NET", $"Inbound UDP datagram received: {result.ReceivedBytes} bytes from {result.RemoteEndPoint}");

                        Span<byte> activeChunk = packetBuffer.WritableData.Slice(0, result.ReceivedBytes);

                        // Track server packet ID sequence from incoming datagram header
                        if (activeChunk.Length >= 2)
                        {
                            _serverPacketIdSequence = BinaryPrimitives.ReadUInt16LittleEndian(activeChunk.Slice(0, 2));
                        }

                        bool parsed = _parser.ProcessIncomingChunk(activeChunk);

                        if (parsed && CurrentState == SessionState.ExchangingCryptoKeys)
                        {
                            CurrentState = SessionState.LoadingWorldData;
                        }
                    }
                    catch (SocketException ex) when (ex.SocketErrorCode == SocketError.ConnectionReset)
                    {
                        // On Windows UDP, ICMP Port Unreachable throws WSAECONNRESET (10054).
                        // Ignore it and continue waiting for incoming server datagrams.
                        GordianLog.Debug("NET", "Ignored UDP ICMP ConnectionReset (10054); awaiting server datagram...");
                    }
                    catch (Exception ex) when (!token.IsCancellationRequested)
                    {
                        GordianLog.Error("NET", "Inbound UDP packet processing error inside loop", ex);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                GordianLog.Debug("NET", "InboundNetworkReadLoopAsync cancelled via CancellationToken.");
            }
            catch (Exception ex)
            {
                GordianLog.Error("NET", "Inbound UDP read loop failure", ex);
            }
            finally
            {
                GordianLog.Debug("NET", $"InboundNetworkReadLoopAsync finally block reached. CurrentState={CurrentState}, TokenCancelled={token.IsCancellationRequested}");
                CurrentState = SessionState.Disconnecting;
                ExecuteSocketCleanup();
                CurrentState = SessionState.Disconnected;
            }
        }

        public void Disconnect()
        {
            GordianLog.Info("NET", $"Disconnect() called directly. CurrentState={CurrentState}");
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
