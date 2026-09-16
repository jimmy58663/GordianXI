// src/Gordian.Core/Network/PacketParser.cs
using System;
using System.Buffers.Binary;
using System.Net;
using System.Diagnostics;
using System.Threading.Tasks;
using Gordian.Core.Config;
using Gordian.Core.Diagnostics;
using Gordian.Core.Network.Compression;
using Gordian.Core.Network.Crypto;
using Gordian.Core.Network.Packets;
using Gordian.Core.World;

namespace Gordian.Core.Network
{
    /// <summary>
    /// Decrypts, verifies, decompresses, and routes incoming FFXI datagram envelopes and sub-packets.
    /// Delegates sub-packet routing to a direct-indexed O(1) <see cref="PacketDispatcher"/>.
    /// Operates entirely with zero heap allocation per packet frame.
    /// </summary>
    public class PacketParser
    {
        public const int FfxiHeaderSize = 28; // 0x1C bytes

        private readonly IPacketCryptoSuite _cryptoSuite;
        private readonly FfxiCodec _codec;
        private readonly SessionProfile _profile;
        private readonly Func<ReadOnlyMemory<byte>, bool, Task> _sendChunkCallback;
        private readonly PacketDispatcher _dispatcher;
        private readonly LifecyclePacketModule _lifecycleModule;
        private readonly WorldState _world;
        private readonly LocalPlayerState _localPlayer;
        private readonly EntityPacketModule _entityModule;
        private readonly ChatPacketModule _chatModule;
        private readonly PartyState _party;
        private readonly PartyPacketModule _partyModule;
        private readonly ProgressionState _progression;
        private readonly ProgressionPacketModule _progressionModule;
        private readonly InventoryState _inventory;
        private readonly InventoryPacketModule _inventoryModule;
        private readonly CombatState _combat;
        private readonly CombatPacketModule _combatModule;
        private readonly Actions.PlayerActionService _actionService;

        // Reusable scratch buffer for decompression to avoid GC allocations
        private readonly byte[] _decompressionScratch = new byte[8192];

        public PacketParser(
            SessionProfile profile,
            Func<ReadOnlyMemory<byte>, bool, Task> sendChunkCallback,
            IPacketCryptoSuite? cryptoSuite = null,
            FfxiCodec? codec = null,
            PacketDispatcher? dispatcher = null,
            WorldState? world = null,
            LocalPlayerState? localPlayer = null,
            PartyState? party = null,
            ProgressionState? progression = null,
            InventoryState? inventory = null,
            CombatState? combat = null)
        {
            _profile = profile ?? throw new ArgumentNullException(nameof(profile));
            _sendChunkCallback = sendChunkCallback ?? throw new ArgumentNullException(nameof(sendChunkCallback));
            _cryptoSuite = cryptoSuite ?? new LegacyBlowfishCryptoSuite();
            _codec = codec ?? FfxiCodec.Default;
            _dispatcher = dispatcher ?? new PacketDispatcher();
            _world = world ?? new WorldState();
            _localPlayer = localPlayer ?? new LocalPlayerState();
            _party = party ?? new PartyState();
            _progression = progression ?? new ProgressionState();
            _inventory = inventory ?? new InventoryState();
            _combat = combat ?? new CombatState();

            _lifecycleModule = new LifecyclePacketModule(_profile, _sendChunkCallback, LogPacket);
            _lifecycleModule.Register(_dispatcher);

            _entityModule = new EntityPacketModule(_world, _localPlayer, _sendChunkCallback, LogPacket);
            _entityModule.Register(_dispatcher);

            _chatModule = new ChatPacketModule(_sendChunkCallback, LogPacket);
            _chatModule.Register(_dispatcher);

            _partyModule = new PartyPacketModule(_party, _sendChunkCallback, LogPacket);
            _partyModule.Register(_dispatcher);

            _progressionModule = new ProgressionPacketModule(_progression, _localPlayer, _sendChunkCallback, LogPacket);
            _progressionModule.Register(_dispatcher);

            _inventoryModule = new InventoryPacketModule(_inventory, _localPlayer, _sendChunkCallback, LogPacket);
            _inventoryModule.Register(_dispatcher);

            _combatModule = new CombatPacketModule(_combat, _localPlayer, _sendChunkCallback, LogPacket);
            _combatModule.Register(_dispatcher);

            _actionService = new Actions.PlayerActionService(
                _profile,
                _world,
                _localPlayer,
                _combatModule,
                _chatModule,
                _partyModule,
                _entityModule,
                _lifecycleModule,
                _sendChunkCallback);

            _dispatcher.UnhandledPacket += (header, payload) =>
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[NET_TRACE] Unhandled Packet ID: 0x{header.PacketId:X3} | Seq: 0x{header.SequenceId:X4} | Size: {header.TotalSize} bytes"
                );
            };
        }

        /// <summary>
        /// Gets the unified player action service for coordinating user commands, combat, movement, and automation.
        /// </summary>
        public Actions.PlayerActionService ActionService => _actionService;

        /// <summary>
        /// Gets the direct-indexed packet dispatcher used by this parser.
        /// Callers can register domain-specific packet handlers into this dispatcher.
        /// </summary>
        public IPacketDispatcher Dispatcher => _dispatcher;

        /// <summary>
        /// Gets the lifecycle and handshake handler module.
        /// </summary>
        public LifecyclePacketModule LifecycleModule => _lifecycleModule;

        /// <summary>
        /// Gets the thread-safe active game world state.
        /// </summary>
        public WorldState World => _world;

        /// <summary>
        /// Gets the active character statistics and vitals state.
        /// </summary>
        public LocalPlayerState LocalPlayer => _localPlayer;

        /// <summary>
        /// Gets the entity packet handling module.
        /// </summary>
        public EntityPacketModule EntityModule => _entityModule;

        /// <summary>
        /// Gets the communication and chat packet handling module.
        /// </summary>
        public ChatPacketModule ChatModule => _chatModule;

        /// <summary>
        /// Gets the active party and alliance state model.
        /// </summary>
        public PartyState Party => _party;

        /// <summary>
        /// Gets the party packet handling module.
        /// </summary>
        public PartyPacketModule PartyModule => _partyModule;

        /// <summary>
        /// Gets the active story progression, quest, merit, and minigame state model.
        /// </summary>
        public ProgressionState Progression => _progression;

        /// <summary>
        /// Gets the progression, quest, cutscene, and mog house packet handling module.
        /// </summary>
        public ProgressionPacketModule ProgressionModule => _progressionModule;

        /// <summary>
        /// Gets the active multi-container inventory, currency, and trade state model.
        /// </summary>
        public InventoryState Inventory => _inventory;

        /// <summary>
        /// Gets the inventory, trade, shop, and bazaar packet handling module.
        /// </summary>
        public InventoryPacketModule InventoryModule => _inventoryModule;

        /// <summary>
        /// Gets the active session combat, targeting, recast, and action history state model.
        /// </summary>
        public CombatState Combat => _combat;

        /// <summary>
        /// Gets the combat, spell casting, ability, and emote packet handling module.
        /// </summary>
        public CombatPacketModule CombatModule => _combatModule;

        /// <summary>
        /// Gets or sets the performance and telemetry tracker for recording packet counts and dispatch latency.
        /// </summary>
        public SessionPerformanceTracker? Performance { get; set; }

        /// <summary>
        /// Raised whenever a sub-packet is parsed from an inbound stream or queued for outbound dispatch.
        /// </summary>
        public event EventHandler<PacketLogEntry>? PacketInspected;

        /// <summary>
        /// Raised when the handshake has fully completed (after GP_SERV_ENTERZONE 0x008 and GP_CLI_NETEND 0x00D).
        /// </summary>
        public event Action? HandshakeCompleted
        {
            add => _lifecycleModule.HandshakeCompleted += value;
            remove => _lifecycleModule.HandshakeCompleted -= value;
        }

        /// <summary>
        /// Raised when player initial position and heading is parsed from GP_SERV_LOGIN (0x00A).
        /// Parameters: x, y, z, dir, actIndex.
        /// </summary>
        public event Action<float, float, float, byte, ushort>? PlayerPositionUpdated
        {
            add => _lifecycleModule.PlayerPositionUpdated += value;
            remove => _lifecycleModule.PlayerPositionUpdated -= value;
        }

        /// <summary>
        /// Raised when the server responds with a zone transition or logout directive (0x00B).
        /// Parameters: LogoutState, TargetIp, TargetPort, ErrorCode.
        /// </summary>
        public event Action<LogoutState, IPAddress, ushort, uint>? ZoneTransitionReceived
        {
            add => _lifecycleModule.ZoneTransitionReceived += value;
            remove => _lifecycleModule.ZoneTransitionReceived -= value;
        }

        /// <summary>
        /// Controls whether the lifecycle module logs outbound response packets directly.
        /// When false, outbound packets are logged by the network layer on actual transmission.
        /// Defaults to true for standalone parser testing.
        /// </summary>
        public bool LogOutboundOnRoute
        {
            get => _lifecycleModule.LogOutboundOnRoute;
            set
            {
                _lifecycleModule.LogOutboundOnRoute = value;
                _entityModule.LogOutboundOnRoute = value;
                _partyModule.LogOutboundOnRoute = value;
            }
        }

        /// <summary>
        /// Gets the active cryptographic suite configured for this session.
        /// </summary>
        public IPacketCryptoSuite CryptoSuite => _cryptoSuite;

        /// <summary>
        /// Initializes the symmetric session key provided by the server handshake.
        /// </summary>
        public void InitializeSessionCrypto(ReadOnlySpan<byte> key)
        {
            _cryptoSuite.InitializeKey(key);
        }

        /// <summary>
        /// Emits an inspected packet event without allocations if no listeners are attached.
        /// </summary>
        public void LogPacket(PacketDirection direction, ushort packetId, ushort sequenceId, ReadOnlySpan<byte> fullSubPacket)
        {
            if (PacketInspected == null) return;

            var entry = new PacketLogEntry
            {
                Timestamp = DateTime.UtcNow,
                Direction = direction,
                PacketId = packetId,
                PacketName = PacketLogEntry.ResolvePacketName(packetId, direction),
                SequenceId = sequenceId,
                Size = fullSubPacket.Length,
                RawBytes = fullSubPacket.ToArray()
            };
            PacketInspected.Invoke(this, entry);
        }

        /// <summary>
        /// Processes an incoming raw UDP datagram buffer.
        /// Performs decryption, MD5 checksum validation, custom zlib decompression,
        /// and iterative sub-packet dispatching via <see cref="PacketDispatcher"/>.
        /// </summary>
        public bool ProcessIncomingChunk(Span<byte> rawPacketBuffer)
        {
            long startTicks = Stopwatch.GetTimestamp();

            // Minimum FFXI datagram envelope: 28-byte header + 16-byte MD5 checksum
            if (rawPacketBuffer.Length < FfxiHeaderSize + 16)
            {
                GordianLog.Debug("PARSER", $"Raw datagram buffer too short ({rawPacketBuffer.Length} bytes). Minimum: {FfxiHeaderSize + 16}");
                return false;
            }

            // 1. Decrypt and verify packet integrity via pluggable crypto suite
            int payloadLength;
            if (_cryptoSuite.IsKeyInitialized)
            {
                if (!_cryptoSuite.TryDecryptAndVerify(rawPacketBuffer, FfxiHeaderSize, out payloadLength))
                {
                    GordianLog.Warning("PARSER", "Inbound datagram failed Blowfish decryption or MD5 checksum verification.");
                    // Log undecrypted raw packet to inspector for diagnostic inspection
                    var undecryptedEntry = new PacketLogEntry
                    {
                        Timestamp = DateTime.UtcNow,
                        Direction = PacketDirection.Inbound,
                        PacketId = 0,
                        PacketName = "GP_RAW_UDP_UNDECRYPTED",
                        SequenceId = 0,
                        Size = rawPacketBuffer.Length,
                        RawBytes = rawPacketBuffer.ToArray()
                    };
                    PacketInspected?.Invoke(this, undecryptedEntry);
                    return false;
                }
            }
            else
            {
                // Unencrypted handshake packets (e.g. login 0x00A)
                payloadLength = rawPacketBuffer.Length - (FfxiHeaderSize + 16);
            }

            if (payloadLength <= 0)
            {
                GordianLog.Debug("PARSER", $"Inbound payload length non-positive ({payloadLength}).");
                return false;
            }

            // 2. Decompress payload starting after 28-byte header
            ReadOnlySpan<byte> compressedPayload = rawPacketBuffer.Slice(FfxiHeaderSize, payloadLength);
            int decompressedBytes;

            try
            {
                decompressedBytes = _codec.Decompress(compressedPayload, _decompressionScratch);
            }
            catch (Exception ex)
            {
                GordianLog.Warning("PARSER", $"Payload decompression error: {ex.Message}");
                return false;
            }

            if (decompressedBytes < 4)
            {
                GordianLog.Debug("PARSER", $"Decompressed length ({decompressedBytes}) < 4.");
                return false;
            }

            // 3. Iterate concatenated sub-packets and dispatch
            ReadOnlySpan<byte> subPackets = _decompressionScratch.AsSpan(0, decompressedBytes);
            int offset = 0;
            int inboundSubPacketCount = 0;

            while (offset + 4 <= subPackets.Length)
            {
                ReadOnlySpan<byte> current = subPackets.Slice(offset);

                if (!PacketHeader.TryParse(current, out PacketHeader header) ||
                    header.TotalSize < 4 ||
                    offset + header.TotalSize > subPackets.Length)
                {
                    break;
                }

                ReadOnlySpan<byte> fullSubPacket = current.Slice(0, header.TotalSize);
                ReadOnlySpan<byte> packetPayload = current.Slice(4, header.TotalSize - 4);

                LogPacket(PacketDirection.Inbound, header.PacketId, header.SequenceId, fullSubPacket);
                _dispatcher.Dispatch(header, packetPayload);
                inboundSubPacketCount++;
                offset += header.TotalSize;
            }

            if (inboundSubPacketCount > 0)
            {
                Performance?.RecordInboundPackets(inboundSubPacketCount);
            }

            long elapsedTicks = Stopwatch.GetTimestamp() - startTicks;
            Performance?.RecordDispatchLatencyTicks(elapsedTicks);

            return true;
        }
    }
}
