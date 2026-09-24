// src/Gordian.Core/Network/SessionNetworkManager.cs
using System;
using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Gordian.Core.Diagnostics;
using Gordian.Core.Config;
using Gordian.Core.Network.Compression;
using Gordian.Core.Network.Crypto;
using Gordian.Core.Network.Packets;
using Gordian.Core.World;

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

        private string _serverAddress;
        private int _serverPort;
        private readonly PacketParser _parser;
        private readonly FfxiCodec _codec;

        private Socket? _udpSocket;
        private EndPoint? _serverEndpoint;
        private CancellationTokenSource? _cts;
        private Task? _readTask;
        private Task? _writeFlushTask;
        private bool _isDisposed;

        //  Outbound Network Buffering
        private readonly SemaphoreSlim _writeLock = new SemaphoreSlim(1, 1);
        private readonly byte[] _outboundQueueBuffer = new byte[MaxDatagramSize];
        private int _currentBufferLength = 0;

        private ushort _serverPacketIdSequence = 0;
        private ushort _clientPacketIdSequence = 0;
        private ushort _lastProcessedSequence = 0;
        private bool _hasProcessedAnySequence = false;
        private ulong _sequenceHistoryBitmask = 0;
        /// <summary>
        /// Constant MoveFlame / Run Count sent in 0x015 while stationary (0x0001 per retail protocol captures).
        /// </summary>
        public const ushort StationaryRunCount = 1;

        /// <summary>
        /// Initial MoveFlame / Run Count sent in 0x015 upon beginning locomotion (0x0009 per retail protocol captures).
        /// </summary>
        public const ushort InitialRunCount = 9;

        private ushort _moveFrame = StationaryRunCount;
        private bool _isWalking = false;
        private bool _isMoving = false;
        private long _movementStartTimestamp = 0;
        private ushort[]? _cachedPlayerAppearance;

        /// <summary>
        /// Gets the current movement animation frame counter (Run Count) sent in 0x015 packets.
        /// </summary>
        public ushort MoveFrame => _moveFrame;

        /// <summary>
        /// Gets whether the local character is currently walking rather than running.
        /// </summary>
        public bool IsWalking => _isWalking;

        /// <summary>
        /// Gets or sets whether duplicate/retransmitted incoming server datagrams should be dropped
        /// while maintaining updated sequence ACK state. Defaults to true.
        /// </summary>
        public bool EnableSequenceDeduplication { get; set; } = true;

        /// <summary>
        /// Gets the latest acknowledged server packet ID sequence.
        /// </summary>
        public ushort ServerPacketIdSequence => Volatile.Read(ref _serverPacketIdSequence);

        private readonly SessionPerformanceTracker _performance = new SessionPerformanceTracker();

        /// <summary>
        /// Gets the real-time datagram throughput, packet rates, and memory telemetry tracker for this session.
        /// </summary>
        public SessionPerformanceTracker Performance => _performance;

        /// <summary>
        /// Gets the isolated, instance-level configuration matrix for this specific character session.
        /// </summary>
        public SessionProfile Profile { get; } = new SessionProfile();

        /// <summary>
        /// Optional test and simulation hook to intercept outbound chunks before UDP staging.
        /// </summary>
        public Func<ReadOnlyMemory<byte>, bool, Task>? OutboundChunkOverride { get; set; }

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
        /// Gets the thread-safe active game world state.
        /// </summary>
        public WorldState World => _parser.World;

        /// <summary>
        /// Gets the active character statistics and vitals state.
        /// </summary>
        public LocalPlayerState LocalPlayer => _parser.LocalPlayer;

        /// <summary>
        /// Gets the entity packet handling module.
        /// </summary>
        public EntityPacketModule EntityModule => _parser.EntityModule;

        /// <summary>
        /// Gets the communication and chat packet handling module.
        /// </summary>
        public ChatPacketModule ChatModule => _parser.ChatModule;

        /// <summary>
        /// Gets the active party and alliance state model.
        /// </summary>
        public PartyState Party => _parser.Party;

        /// <summary>
        /// Gets the party packet handling module.
        /// </summary>
        public PartyPacketModule PartyModule => _parser.PartyModule;

        /// <summary>
        /// Gets the active story progression, quest, merit, and minigame state model.
        /// </summary>
        public ProgressionState Progression => _parser.Progression;

        /// <summary>
        /// Gets the progression, quest, cutscene, and mog house packet handling module.
        /// </summary>
        public ProgressionPacketModule ProgressionModule => _parser.ProgressionModule;

        /// <summary>
        /// Gets the active multi-container inventory, currency, and trade state model.
        /// </summary>
        public InventoryState Inventory => _parser.Inventory;

        /// <summary>
        /// Gets the inventory, trade, shop, and bazaar packet handling module.
        /// </summary>
        public InventoryPacketModule InventoryModule => _parser.InventoryModule;

        /// <summary>
        /// Gets the active session combat, targeting, recast, and action history state model.
        /// </summary>
        public CombatState Combat => _parser.Combat;

        /// <summary>
        /// Gets the combat, spell casting, ability, and emote packet handling module.
        /// </summary>
        public CombatPacketModule CombatModule => _parser.CombatModule;

        /// <summary>
        /// Gets the unified player action coordinator service.
        /// </summary>
        public Actions.PlayerActionService ActionService => _parser.ActionService;

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
        /// Current character facing direction / rotation (0..255), in wire convention (see <see cref="WorldEntity.Direction"/>).
        /// </summary>
        public byte Direction { get; set; }

        /// <summary>
        /// Current target index / actor index in zone.
        /// </summary>
        public ushort TargetIndex { get; set; }

        /// <summary>
        /// Gets the current target map server IP address or hostname.
        /// </summary>
        public string ServerAddress => _serverAddress;

        /// <summary>
        /// Gets the current target map server UDP port.
        /// </summary>
        public int ServerPort => _serverPort;

        /// <summary>
        /// Gets the active remote server endpoint.
        /// </summary>
        public EndPoint? CurrentEndpoint => _serverEndpoint;

        /// <summary>
        /// Raised when a dynamic zone transition to a new map server is triggered.
        /// Parameters: Target IP, Target Port.
        /// </summary>
        public event Action<IPAddress, int>? ZoneTransitionStarted;

        /// <summary>
        /// Raised whenever a sub-packet is parsed from an inbound stream or queued for outbound dispatch.
        /// </summary>
        public event EventHandler<PacketLogEntry>? PacketInspected
        {
            add => _parser.PacketInspected += value;
            remove => _parser.PacketInspected -= value;
        }

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
                LogOutboundOnRoute = false,
                Performance = _performance
            };
            _parser.HandshakeCompleted += () =>
            {
                if (CharacterId != 0)
                {
                    _parser.LocalPlayer.ServerId = CharacterId;
                }
                EnsureLocalPlayerEntity(PositionX, PositionY, PositionZ, Direction, TargetIndex);
                CurrentState = SessionState.ActiveInWorld;
            };
            _parser.PlayerPositionUpdated += (x, y, z, dir, actIndex) =>
            {
                PositionX = x;
                PositionY = y;
                PositionZ = z;
                Direction = dir;
                TargetIndex = actIndex;
                EnsureLocalPlayerEntity(x, y, z, dir, actIndex);
                GordianLog.Debug("NET", $"Initial position captured: X={x:F2}, Y={y:F2}, Z={z:F2}, Dir={dir}, TargetIndex={actIndex}");
            };
            _parser.LoginAppearanceReceived += (sid, grap, name) =>
            {
                if (sid != 0)
                {
                    _parser.LocalPlayer.ServerId = sid;
                    CharacterId = sid;
                }
                if (!string.IsNullOrEmpty(name))
                {
                    CharacterName = name;
                }
                _cachedPlayerAppearance = grap;

                EnsureLocalPlayerEntity(PositionX, PositionY, PositionZ, Direction, TargetIndex);

                uint targetSid = sid != 0 ? sid : CharacterId;
                if (targetSid != 0 && _parser.World.TryGetByServerId(targetSid, out var existing) && existing is PlayerEntity pe)
                {
                    pe.Appearance.CopyFrom(grap);
                    if (!string.IsNullOrEmpty(name)) pe.Name = name;
                }
                GordianLog.Info("NET", $"Captured local player login appearance (Face/Race: 0x{grap[0]:X4}, Name: {name})");
            };
            _parser.ActionService.LocalPlayerMoved += (pos, dir) =>
            {
                PositionX = pos.X;
                PositionY = pos.Y;
                PositionZ = pos.Z;
                Direction = dir;
            };
            _parser.LifecycleModule.PositionProvider = () => (PositionX, PositionY, PositionZ, Direction, TargetIndex, _moveFrame, _isWalking);
            _parser.ZoneTransitionReceived += (state, targetIp, targetPort, errCode) =>
            {
                GordianLog.Info("NET", $"ZoneTransitionReceived: State={state}, Target={targetIp}:{targetPort}, Err={errCode}");
                if ((state == LogoutState.ZoneChange || state == LogoutState.MyRoom) && ZoneTransitionPending)
                {
                    // The server resends 0x00B until the client reappears on the new map server; act on it once.
                    GordianLog.Debug("NET", "Ignoring repeated zone change while a transition is already in progress.");
                    return;
                }
                if (state == LogoutState.ZoneChange || state == LogoutState.MyRoom)
                {
                    World.Clear();
                    _ = HandleZoneTransitionAsync(targetIp, targetPort);
                }
                else if (state == LogoutState.Logout || state == LogoutState.PolExit || state == LogoutState.End)
                {
                    World.Clear();
                    Disconnect();
                }
            };
        }

        private void EnsureLocalPlayerEntity(float x, float y, float z, byte dir, ushort actIndex)
        {
            uint sid = _parser.LocalPlayer.ServerId != 0 ? _parser.LocalPlayer.ServerId : CharacterId;
            if (sid == 0) return;

            _parser.LocalPlayer.ServerId = sid;
            if (_parser.World.TryGetByServerId(sid, out var existing) && existing is PlayerEntity pe)
            {
                pe.Position = new Vector3(x, y, z);
                pe.Direction = dir;
                pe.TargetIndex = actIndex;
                pe.IsSpawned = true;
                if (!string.IsNullOrEmpty(CharacterName)) pe.Name = CharacterName;
                if (_cachedPlayerAppearance != null)
                {
                    pe.Appearance.CopyFrom(_cachedPlayerAppearance);
                }
            }
            else
            {
                var newEntity = new PlayerEntity(sid, actIndex)
                {
                    Position = new Vector3(x, y, z),
                    Direction = dir,
                    IsSpawned = true,
                    Name = CharacterName,
                    Speed = (byte)(_parser.LocalPlayer.Speed > 0 ? Math.Min((ushort)255, _parser.LocalPlayer.Speed) : 50),
                    SpeedBase = _parser.LocalPlayer.SpeedBase > 0 ? _parser.LocalPlayer.SpeedBase : (byte)50
                };
                if (_cachedPlayerAppearance != null)
                {
                    newEntity.Appearance.CopyFrom(_cachedPlayerAppearance);
                }
                _parser.World.UpsertEntity(newEntity);
            }
        }

        /// <summary>
        /// Receives real-time locomotion telemetry from PlayerLocomotionController.
        /// When movement starts or stops, immediately transmits a high-priority 0x015 datagram to eliminate motion latency.
        /// </summary>
        public void NotifyLocomotionChanged(Vector3 position, byte direction, byte speed)
        {
            PositionX = position.X;
            PositionY = position.Y;
            PositionZ = position.Z;
            Direction = direction;

            bool isMoving = speed > 0;
            bool wasMoving = _isMoving;
            _isMoving = isMoving;
            _isWalking = isMoving && speed < 40;

            if (isMoving)
            {
                if (!wasMoving || _movementStartTimestamp == 0)
                {
                    _movementStartTimestamp = System.Diagnostics.Stopwatch.GetTimestamp();
                    _moveFrame = InitialRunCount;
                }
                else
                {
                    double elapsedSec = System.Diagnostics.Stopwatch.GetElapsedTime(_movementStartTimestamp).TotalSeconds;
                    _moveFrame = (ushort)Math.Min(ushort.MaxValue, InitialRunCount + (uint)(elapsedSec * 60.0));
                }
            }
            else
            {
                _movementStartTimestamp = 0;
                _moveFrame = StationaryRunCount;
            }

            uint sid = _parser.LocalPlayer.ServerId != 0 ? _parser.LocalPlayer.ServerId : CharacterId;
            if (sid != 0 && _parser.World.TryGetByServerId(sid, out var ent) && ent is PlayerEntity localPe)
            {
                localPe.Position = position;
                localPe.Direction = direction;
                localPe.Speed = speed;
            }

            if (isMoving != wasMoving)
            {
                _ = TriggerImmediatePosUpdateAsync();
            }
        }

        private async Task TriggerImmediatePosUpdateAsync()
        {
            try
            {
                if (CurrentState != SessionState.ActiveInWorld && CurrentState != SessionState.LoadingWorldData) return;
                if (_udpSocket == null || _serverEndpoint == null) return;

                byte[] posPacket = HandshakePackets.BuildPosPingPongSubPacket(
                    sequenceId: 0,
                    x: PositionX,
                    y: PositionY,
                    z: PositionZ,
                    dir: Direction,
                    moveFrame: _moveFrame,
                    isWalking: _isWalking,
                    targetIndex: TargetIndex
                );

                await QueueChunkAsync(posPacket, isHighPriority: true).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                GordianLog.Error("NET", "Immediate position update flush failed", ex);
            }
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
                                _performance.RecordOutboundDatagram(loginDatagram.Length);
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

            if (OutboundChunkOverride != null)
            {
                await OutboundChunkOverride(chunkData, isHighPriority).ConfigureAwait(false);
                return;
            }

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
                int outboundSubPacketCount = 0;
                while (subOffset + 4 <= _currentBufferLength)
                {
                    int subSize = (_outboundQueueBuffer[subOffset + 1] & 0xFE) * 2;
                    if (subSize < 4 || subOffset + subSize > _currentBufferLength) break;

                    BinaryPrimitives.WriteUInt16LittleEndian(_outboundQueueBuffer.AsSpan(subOffset + 2, 2), clientSeq);

                    ushort rawTypeAndSize = BinaryPrimitives.ReadUInt16LittleEndian(_outboundQueueBuffer.AsSpan(subOffset, 2));
                    ushort packetId = (ushort)(rawTypeAndSize & 0x1FF);
                    ReadOnlySpan<byte> fullSubPacket = _outboundQueueBuffer.AsSpan(subOffset, subSize);
                    _parser.LogPacket(PacketDirection.Outbound, packetId, clientSeq, fullSubPacket);

                    outboundSubPacketCount++;
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
                BinaryPrimitives.WriteUInt16LittleEndian(headerSpan.Slice(2, 2), Volatile.Read(ref _serverPacketIdSequence));

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
                _performance.RecordOutboundDatagram(datagramLength);
                if (outboundSubPacketCount > 0)
                {
                    _performance.RecordOutboundPackets(outboundSubPacketCount);
                }
                GordianLog.Debug("NET", $"Outbound UDP datagram transmitted: Seq={clientSeq}, Ack={Volatile.Read(ref _serverPacketIdSequence)}, {datagramLength} bytes to {remoteEndpoint}");
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
                    try
                    {
                        await Task.Delay(NetworkTickIntervalMs, token).ConfigureAwait(false);

                        if (_serverEndpoint == null) continue;

                        await _writeLock.WaitAsync(token).ConfigureAwait(false);
                        try
                        {
                            // In ActiveInWorld or LoadingWorldData, ensure the 0x015 GP_CLI_POS keepalive heartbeat
                            // is bundled into outbound transmission with active MoveFlame and RunMode flags.
                            if (CurrentState == SessionState.ActiveInWorld || CurrentState == SessionState.LoadingWorldData)
                            {
                                // Pull latest position, heading, and locomotion speed from WorldState if available
                                if (_parser.LocalPlayer.ServerId != 0 &&
                                    _parser.World.TryGetByServerId(_parser.LocalPlayer.ServerId, out var localEnt) &&
                                    localEnt != null)
                                {
                                    PositionX = localEnt.Position.X;
                                    PositionY = localEnt.Position.Y;
                                    PositionZ = localEnt.Position.Z;
                                    Direction = localEnt.Direction;

                                    bool isMoving = localEnt.Speed > 0;
                                    bool wasMoving = _isMoving;
                                    _isMoving = isMoving;
                                    _isWalking = isMoving && localEnt.Speed < 40;

                                    if (isMoving)
                                    {
                                        if (!wasMoving || _movementStartTimestamp == 0)
                                        {
                                            _movementStartTimestamp = System.Diagnostics.Stopwatch.GetTimestamp();
                                            _moveFrame = InitialRunCount;
                                        }
                                        else
                                        {
                                            double elapsedSec = System.Diagnostics.Stopwatch.GetElapsedTime(_movementStartTimestamp).TotalSeconds;
                                            _moveFrame = (ushort)(InitialRunCount + (uint)(elapsedSec * 60.0));
                                            if (_moveFrame <= 1) _moveFrame = InitialRunCount;
                                        }
                                    }
                                    else
                                    {
                                        _movementStartTimestamp = 0;
                                        _moveFrame = StationaryRunCount;
                                    }
                                }
                                else
                                {
                                    _movementStartTimestamp = 0;
                                    _isMoving = false;
                                    _moveFrame = StationaryRunCount;
                                    _isWalking = false;
                                }

                                // Check if a 0x015 packet is already queued in the outbound buffer
                                bool hasPosPacket = false;
                                int scanOffset = 0;
                                while (scanOffset + 4 <= _currentBufferLength)
                                {
                                    ushort rawHeader = BinaryPrimitives.ReadUInt16LittleEndian(_outboundQueueBuffer.AsSpan(scanOffset, 2));
                                    ushort packetId = (ushort)(rawHeader & 0x1FF);
                                    if (packetId == 0x015)
                                    {
                                        hasPosPacket = true;
                                        break;
                                    }
                                    int subSize = (_outboundQueueBuffer[scanOffset + 1] & 0xFE) * 2;
                                    if (subSize < 4 || scanOffset + subSize > _currentBufferLength) break;
                                    scanOffset += subSize;
                                }

                                if (hasPosPacket && scanOffset + 32 <= _currentBufferLength)
                                {
                                    // Overwrite the queued 0x015 in-place with latest coordinates, heading, and slide frames
                                    Span<byte> existingPos = _outboundQueueBuffer.AsSpan(scanOffset, 32);
                                    BinaryPrimitives.WriteSingleLittleEndian(existingPos.Slice(4, 4), PositionX);
                                    BinaryPrimitives.WriteSingleLittleEndian(existingPos.Slice(8, 4), PositionZ); // Wire offset 8 is Elevation
                                    BinaryPrimitives.WriteSingleLittleEndian(existingPos.Slice(12, 4), PositionY); // Wire offset 12 is North/South
                                    BinaryPrimitives.WriteUInt16LittleEndian(existingPos.Slice(16, 2), 0); // MovTime: Always 0 on retail FFXI protocol
                                    BinaryPrimitives.WriteUInt16LittleEndian(existingPos.Slice(18, 2), _moveFrame); // MoveFlame / Run Count: accumulating frame counter when moving, 1 when stationary
                                    existingPos[20] = Direction;
                                    byte modes = (byte)((TargetIndex != 0 ? 0x01 : 0x00) | (_isWalking ? 0x02 : 0x00));
                                    existingPos[21] = modes;
                                    BinaryPrimitives.WriteUInt16LittleEndian(existingPos.Slice(22, 2), TargetIndex);
                                    BinaryPrimitives.WriteUInt32LittleEndian(existingPos.Slice(24, 4), (uint)Environment.TickCount);
                                }
                                else if (!hasPosPacket)
                                {
                                    byte[] posPacket = HandshakePackets.BuildPosPingPongSubPacket(
                                        sequenceId: 0, // will be stamped to clientSeq in FlushBundledPacketAsync
                                        x: PositionX,
                                        y: PositionY,
                                        z: PositionZ,
                                        dir: Direction,
                                        moveFrame: _moveFrame,
                                        isWalking: _isWalking,
                                        targetIndex: TargetIndex
                                    );

                                    if (_currentBufferLength + posPacket.Length <= MaxDatagramSize)
                                    {
                                        posPacket.CopyTo(_outboundQueueBuffer.AsSpan(_currentBufferLength));
                                        _currentBufferLength += posPacket.Length;
                                    }
                                }
                            }

                            await FlushBundledPacketAsync(socket, _serverEndpoint, token).ConfigureAwait(false);
                        }
                        finally
                        {
                            _writeLock.Release();
                        }
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex) when (!token.IsCancellationRequested)
                    {
                        GordianLog.Error("NET", "Transient error in outbound flush loop", ex);
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
                        _performance.RecordInboundDatagram(result.ReceivedBytes);

                        Span<byte> activeChunk = packetBuffer.WritableData.Slice(0, result.ReceivedBytes);
                        ProcessInboundDatagram(activeChunk);
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

        /// <summary>
        /// Handles incoming server UDP datagrams: tracks server sequence IDs, acknowledges packets,
        /// drops retransmitted duplicates via sliding window bitmask, and dispatches sub-packets.
        /// Returns true if the datagram was processed and parsed, or false if dropped as a duplicate or invalid.
        /// </summary>
        public bool ProcessInboundDatagram(Span<byte> activeChunk)
        {
            if (activeChunk.Length < 2)
            {
                return false;
            }

            ushort newSeq = BinaryPrimitives.ReadUInt16LittleEndian(activeChunk.Slice(0, 2));

            // Mid zone transition, stragglers from the old map server (which restarts nothing and keeps its own
            // sequence numbers) still arrive. Only a datagram that parses with the new zone's key may set the new
            // server's sequence baseline and our ACK; otherwise the new server's packets (starting again at 1) would
            // look ancient and be discarded as duplicates.
            if (ZoneTransitionPending)
            {
                // Until the key has advanced, anything arriving is still old-zone traffic.
                if (!_zoneKeyAdvanced) return false;

                bool accepted = _parser.ProcessIncomingChunk(activeChunk);
                if (accepted)
                {
                    ResetSequenceTracking();
                    CheckAndTrackSequence(newSeq);
                    Volatile.Write(ref _serverPacketIdSequence, newSeq);
                    ZoneTransitionPending = false;
                    GordianLog.Info("NET", $"First datagram from the new map server accepted (Seq={newSeq}); zone transition complete.");
                }
                return accepted;
            }

            Volatile.Write(ref _serverPacketIdSequence, newSeq);

            if (EnableSequenceDeduplication && CheckAndTrackSequence(newSeq))
            {
                _performance.RecordDuplicateDatagram();
                GordianLog.Debug("NET", $"Dropped retransmitted/duplicate server datagram Seq={newSeq}. ACK updated.");
                return false;
            }

            bool parsed = _parser.ProcessIncomingChunk(activeChunk);

            if (parsed && CurrentState == SessionState.ExchangingCryptoKeys)
            {
                CurrentState = SessionState.LoadingWorldData;
            }

            return parsed;
        }

        private bool CheckAndTrackSequence(ushort seq)
        {
            if (!_hasProcessedAnySequence)
            {
                _lastProcessedSequence = seq;
                _sequenceHistoryBitmask = 1UL;
                _hasProcessedAnySequence = true;
                return false;
            }

            short diff = unchecked((short)(seq - _lastProcessedSequence));

            if (diff > 0)
            {
                if (diff > 1)
                {
                    _performance.RecordSequenceDiscrepancy();
                }

                if (diff < 64)
                {
                    _sequenceHistoryBitmask = (_sequenceHistoryBitmask << diff) | 1UL;
                }
                else
                {
                    _sequenceHistoryBitmask = 1UL;
                }

                _lastProcessedSequence = seq;
                return false;
            }
            else
            {
                int age = -diff;
                if (age < 64)
                {
                    ulong bit = 1UL << age;
                    if ((_sequenceHistoryBitmask & bit) != 0)
                    {
                        return true; // Already processed! Retransmitted duplicate!
                    }

                    _sequenceHistoryBitmask |= bit;
                    return false;
                }

                return true; // Older than sliding window (age >= 64); treat as obsolete duplicate
            }
        }

        private void ResetSequenceTracking()
        {
            _lastProcessedSequence = 0;
            _hasProcessedAnySequence = false;
            _sequenceHistoryBitmask = 0;
        }

        private volatile bool _zoneTransitionPending;
        private volatile bool _zoneKeyAdvanced;

        /// <summary>
        /// True from the start of a zone transition until the first datagram from the new map server is accepted.
        /// </summary>
        public bool ZoneTransitionPending
        {
            get => _zoneTransitionPending;
            private set => _zoneTransitionPending = value;
        }

        private async Task HandleZoneTransitionAsync(IPAddress targetIp, ushort targetPort)
        {
            try
            {
                IPAddress resolvedIp = targetIp;
                if (resolvedIp.Equals(IPAddress.Any) || resolvedIp.ToString() == "0.0.0.0")
                {
                    if (_serverEndpoint is IPEndPoint currentIpep)
                    {
                        resolvedIp = currentIpep.Address;
                    }
                    else if (IPAddress.TryParse(_serverAddress, out var parsed))
                    {
                        resolvedIp = parsed;
                    }
                }

                int resolvedPort = targetPort != 0 ? targetPort : _serverPort;
                await PerformZoneTransitionAsync(resolvedIp, resolvedPort).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                GordianLog.Error("NET", "Unhandled exception during zone transition handling", ex);
            }
        }

        /// <summary>
        /// Dynamically transitions the active network session to a target map server:
        /// re-targeting the UDP endpoint, advancing the Blowfish cipher key, resetting packet sequences,
        /// and re-executing the 0x00A login handshake seamlessly without dropping session state.
        /// </summary>
        public async Task PerformZoneTransitionAsync(IPAddress targetIp, int targetPort, CancellationToken ct = default)
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);

            GordianLog.Info("NET", $"Starting dynamic zone transition to {targetIp}:{targetPort} for character '{CharacterName}'...");
            _zoneKeyAdvanced = false;
            ZoneTransitionPending = true;
            CurrentState = SessionState.LoadingWorldData;
            World.Clear();
            ZoneTransitionStarted?.Invoke(targetIp, targetPort);

            await _writeLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                _serverAddress = targetIp.ToString();
                _serverPort = targetPort;
                _serverEndpoint = new IPEndPoint(targetIp, targetPort);
                _currentBufferLength = 0;

                // Advance cryptographic session key (LandSandBoat adds 2 to the fifth 32-bit key word)
                bool advanced = _parser.CryptoSuite.AdvanceZoneKey();
                GordianLog.Debug("NET", $"Session crypto key advanced for zone transition: {advanced}");

                // Reset packet sequence numbers for new map server
                _clientPacketIdSequence = 1;
                Volatile.Write(ref _serverPacketIdSequence, (ushort)0);
                ResetSequenceTracking();
                _zoneKeyAdvanced = true;
            }
            finally
            {
                _writeLock.Release();
            }

            // Transmit unencrypted 0x00A login datagram with retransmission loop to target map server
            if (CharacterId != 0 || !string.IsNullOrEmpty(CharacterName))
            {
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

                var token = _cts?.Token ?? ct;
                _ = Task.Run(async () =>
                {
                    int attempt = 0;
                    while (!token.IsCancellationRequested && CurrentState == SessionState.LoadingWorldData && attempt < 25)
                    {
                        attempt++;
                        GordianLog.Debug("NET", $"Transmitting zone transition 0x00A attempt #{attempt} ({loginDatagram.Length} bytes) to {_serverEndpoint} for '{CharacterName}' (ID: {CharacterId})...");
                        try
                        {
                            if (_udpSocket != null && _serverEndpoint != null)
                            {
                                await _udpSocket.SendToAsync(loginDatagram, SocketFlags.None, _serverEndpoint, token).ConfigureAwait(false);
                            }
                        }
                        catch (Exception ex)
                        {
                            GordianLog.Warning("NET", $"Failed to send zone transition 0x00A attempt #{attempt}: {ex.Message}");
                            break;
                        }

                        try
                        {
                            await Task.Delay(500, token).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException)
                        {
                            break;
                        }
                    }
                }, token);
            }
        }

        /// <summary>
        /// Transmits a C2S 0x05E MapRect packet requesting to cross a zoneline by FourCC tag.
        /// </summary>
        public Task RequestZoneTransitionByZonelineAsync(string rectTag)
        {
            return _parser.LifecycleModule.RequestZoneChangeAsync(rectTag, PositionX, PositionY, PositionZ, TargetIndex);
        }

        /// <summary>
        /// Transmits a C2S 0x05E MapRect packet requesting to cross a zoneline by numeric Rect ID.
        /// </summary>
        public Task RequestZoneTransitionByZonelineAsync(uint rectId)
        {
            return _parser.LifecycleModule.RequestZoneChangeAsync(rectId, PositionX, PositionY, PositionZ, TargetIndex);
        }

        /// <summary>
        /// Transmits a C2S 0x05E MapRect packet requesting to exit Mog House to a specified city area or mode.
        /// </summary>
        public Task RequestMogHouseExitAsync(MogHouseExitBit exitBit, MogHouseExitMode exitMode)
        {
            return _parser.LifecycleModule.RequestMogHouseExitAsync(exitBit, exitMode, PositionX, PositionY, PositionZ, TargetIndex);
        }

        /// <summary>
        /// Transmits a C2S 0x0E7 ReqLogout packet requesting logout or shutdown.
        /// </summary>
        public Task RequestLogoutAsync(bool shutdown = false)
        {
            var kind = shutdown ? ReqLogoutKind.Shutdown : ReqLogoutKind.Logout;
            var mode = shutdown ? ReqLogoutMode.ShutdownOn : ReqLogoutMode.LogoutOn;
            return _parser.LifecycleModule.RequestLogoutAsync(mode, kind);
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

            ResetSequenceTracking();
            Volatile.Write(ref _serverPacketIdSequence, (ushort)0);
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
