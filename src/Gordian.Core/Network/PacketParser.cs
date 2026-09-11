// src/Gordian.Core/Network/PacketParser.cs
using System;
using System.Buffers.Binary;
using System.Threading.Tasks;
using Gordian.Core.Config;
using Gordian.Core.Network.Compression;
using Gordian.Core.Network.Crypto;

namespace Gordian.Core.Network
{
    /// <summary>
    /// Decrypts, verifies, decompresses, and routes incoming FFXI datagram envelopes and sub-packets.
    /// Operates entirely with zero heap allocation per packet frame.
    /// </summary>
    public class PacketParser
    {
        public const int FfxiHeaderSize = 28; // 0x1C bytes

        private readonly IPacketCryptoSuite _cryptoSuite;
        private readonly FfxiCodec _codec;
        private readonly SessionProfile _profile;
        private readonly Func<ReadOnlyMemory<byte>, bool, Task> _sendChunkCallback;

        // Reusable scratch buffer for decompression to avoid GC allocations
        private readonly byte[] _decompressionScratch = new byte[8192];

        public PacketParser(
            SessionProfile profile,
            Func<ReadOnlyMemory<byte>, bool, Task> sendChunkCallback,
            IPacketCryptoSuite? cryptoSuite = null,
            FfxiCodec? codec = null)
        {
            _profile = profile ?? throw new ArgumentNullException(nameof(profile));
            _sendChunkCallback = sendChunkCallback ?? throw new ArgumentNullException(nameof(sendChunkCallback));
            _cryptoSuite = cryptoSuite ?? new LegacyBlowfishCryptoSuite();
            _codec = codec ?? FfxiCodec.Default;
        }

        /// <summary>
        /// Raised whenever a sub-packet is parsed from an inbound stream or queued for outbound dispatch.
        /// </summary>
        public event EventHandler<PacketLogEntry>? PacketInspected;

        /// <summary>
        /// Raised when the handshake has fully completed (after GP_SERV_ENTERZONE 0x008 and GP_CLI_NETEND 0x00D).
        /// </summary>
        public event Action? HandshakeCompleted;

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
        /// and iterative sub-packet dispatching.
        /// </summary>
        public void ProcessIncomingChunk(Span<byte> rawPacketBuffer)
        {
            // Minimum FFXI datagram envelope: 28-byte header + 16-byte MD5 checksum
            if (rawPacketBuffer.Length < FfxiHeaderSize + 16)
            {
                return;
            }

            // 1. Decrypt and verify packet integrity via pluggable crypto suite
            int payloadLength;
            if (_cryptoSuite.IsKeyInitialized)
            {
                if (!_cryptoSuite.TryDecryptAndVerify(rawPacketBuffer, FfxiHeaderSize, out payloadLength))
                {
                    System.Diagnostics.Debug.WriteLine("[NET_TRACE] Inbound datagram failed crypto decryption or MD5 checksum verification.");
                    return;
                }
            }
            else
            {
                // Unencrypted handshake packets (e.g. login 0x00A)
                payloadLength = rawPacketBuffer.Length - (FfxiHeaderSize + 16);
            }

            if (payloadLength <= 0)
            {
                return;
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
                System.Diagnostics.Debug.WriteLine($"[NET_TRACE] Payload decompression error: {ex.Message}");
                return;
            }

            if (decompressedBytes < 4)
            {
                return;
            }

            // 3. Iterate concatenated sub-packets
            ReadOnlySpan<byte> subPackets = _decompressionScratch.AsSpan(0, decompressedBytes);
            int offset = 0;

            while (offset + 4 <= subPackets.Length)
            {
                ReadOnlySpan<byte> current = subPackets.Slice(offset);

                // In LandSandBoat:
                // SmallPD_Type = ref<uint16>(ptr, 0) & 0x1FF;
                // SmallPD_Size = (ref<uint8>(ptr, 1) & 0xFE) * 2;
                ushort rawTypeAndSize = BinaryPrimitives.ReadUInt16LittleEndian(current.Slice(0, 2));
                ushort packetId = (ushort)(rawTypeAndSize & 0x1FF);
                int packetSize = (current[1] & 0xFE) * 2;

                if (packetSize < 4 || offset + packetSize > subPackets.Length)
                {
                    break;
                }

                ushort sequenceId = BinaryPrimitives.ReadUInt16LittleEndian(current.Slice(2, 2));
                ReadOnlySpan<byte> fullSubPacket = current.Slice(0, packetSize);
                ReadOnlySpan<byte> packetPayload = current.Slice(4, packetSize - 4);

                LogPacket(PacketDirection.Inbound, packetId, sequenceId, fullSubPacket);
                RoutePacketToCoreState(packetId, sequenceId, packetPayload);
                offset += packetSize;
            }
        }

        private void RoutePacketToCoreState(ushort packetId, ushort sequenceId, ReadOnlySpan<byte> payload)
        {
            switch (packetId)
            {
                case 0x00A: // GP_SERV_LOGIN (Server Login Acknowledgment)
                    // The server confirmed our initial login and initialized Blowfish.
                    // Respond with GP_CLI_GAMEOK (0x00C) to request zone entry packets.
                    byte[] gameOk = HandshakePackets.BuildGameOkSubPacket(sequenceId: 0);
                    LogPacket(PacketDirection.Outbound, 0x00C, 0, gameOk);
                    _ = _sendChunkCallback(gameOk, true);
                    break;

                case 0x008: // GP_SERV_ENTERZONE
                    // Server streamed zone entrance data.
                    // Release the loading state by sending GP_CLI_NETEND (0x00D).
                    byte[] netEnd = HandshakePackets.BuildNetEndSubPacket(sequenceId: 0);
                    LogPacket(PacketDirection.Outbound, 0x00D, 0, netEnd);
                    _ = _sendChunkCallback(netEnd, true);
                    HandshakeCompleted?.Invoke();
                    break;

                case 0x015: // SERVER KEEPALIVE PING / POS
                    // Echo back high-priority keepalive chunk.
                    byte[] posPong = HandshakePackets.BuildPosPingPongSubPacket(sequenceId: sequenceId);
                    LogPacket(PacketDirection.Outbound, 0x015, sequenceId, posPong);
                    _ = _sendChunkCallback(posPong, true);
                    break;

                case 0x0EE: // Server Automation Policy
                    if (payload.Length >= 1)
                    {
                        _profile.AutomationPolicy = (ServerAutomationPolicy)payload[0];
                    }
                    break;

                default:
                    System.Diagnostics.Debug.WriteLine(
                        $"[NET_TRACE] Unhandled Packet ID: 0x{packetId:X3} | Seq: 0x{sequenceId:X4} | Size: {payload.Length + 4} bytes"
                    );
                    break;
            }
        }
    }
}
