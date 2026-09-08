// src/Gordian.Core/Network/PacketParser.cs
using System;
using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using Gordian.Core.Config;

namespace Gordian.Core.Network
{
    public class PacketParser
    {
        private readonly BlowfishEngine _cryptoEngine;
        private readonly SessionProfile _profile;
        private readonly Func<ReadOnlyMemory<byte>, bool, Task> _sendChunkCallback; // The outbound gateway hook
        private bool _isCryptoInitialized;

        /// <summary>
        /// Initializes the platform-agnostic packet parser.
        /// </summary>
        /// <param name="profile">The unique character session profile.</param>
        /// <param name="sendChunkCallback">The core network method used to queue up outbound response chunks.</param>
        public PacketParser(SessionProfile profile, Func<ReadOnlyMemory<byte>, bool, Task> sendChunkCallback)
        {
            _cryptoEngine = new BlowfishEngine();
            _isCryptoInitialized = false;
            _profile = profile ?? throw new ArgumentNullException(nameof(profile));
            _sendChunkCallback = sendChunkCallback ?? throw new ArgumentNullException(nameof(sendChunkCallback));
        }

        /// <summary>
        /// Initializes the unique symmetric session key provided by the server handshake.
        /// </summary>
        public void InitializeSessionCrypto(ReadOnlySpan<byte> key)
        {
            _cryptoEngine.InitializeKey(key);
            _isCryptoInitialized = true;
        }

        /// <summary>
        /// Processes and decrypts a raw incoming byte fragment natively in-place.
        /// </summary>
        public void ProcessIncomingChunk(Span<byte> rawPacketBuffer)
        {
            // FFXI packet minimum structural limit constraint check
            if (rawPacketBuffer.Length < 4)
            {
                return;
            }

            // 1. Decrypt the block in-place if session handshake is secure
            if (_isCryptoInitialized)
            {
                _cryptoEngine.DecryptCbc(rawPacketBuffer);
            }

            // 2. Extract Type and Length via platform-agnostic Binary Primitives
            ushort packetId = BinaryPrimitives.ReadUInt16LittleEndian(rawPacketBuffer.Slice(0, 2));
            ushort packetLength = BinaryPrimitives.ReadUInt16LittleEndian(rawPacketBuffer.Slice(2, 2));

            // Validate that length parameters align safely with actual socket buffers
            if (packetLength > rawPacketBuffer.Length)
            {
                return;
            }

            // 3. Extract the clean data payload slice
            ReadOnlySpan<byte> payload = rawPacketBuffer.Slice(4, packetLength - 4);

            // 4. Route Packet IDs down the internal network bus
            RoutePacketToCoreState(packetId, payload);
        }

        private void RoutePacketToCoreState(ushort packetId, ReadOnlySpan<byte> payload)
        {
            switch (packetId)
            {
                case 0x015: // SERVER KEEPALIVE PING
                    // Instantly trigger an outbound high-priority pong command response
                    // FFXI expects an immediate high-priority 0x015 chunk echo to confirm the connection is active
                    byte[] pongChunk = new byte[4];
                    BinaryPrimitives.WriteUInt16LittleEndian(pongChunk.AsSpan(0, 2), 0x015);
                    BinaryPrimitives.WriteUInt16LittleEndian(pongChunk.AsSpan(2, 2), 4); // Chunk size is 4 bytes total

                    // Fire the callback down the wire instantly, bypassing the 150ms interval timer
                    _ = _sendChunkCallback(pongChunk, true);
                    break;

                case 0x0EE: // Example Enforced Server Policy configuration payload
                    if (payload.Length >= 1)
                    {
                        // Safely cast byte value directly to our write-once internal flag matrix
                        _profile.AutomationPolicy = (ServerAutomationPolicy)payload[0];
                    }
                    break;

                // Future packet structures (e.g., Inventory 0x01E, Player Status 0x00A) branch here...
                default:
                    // 🔍 The Diagnostic Fallback Logger:
                    // This guarantees you catch and identify every single packet ID the server sends,
                    // tracking its exact payload size so you can build out its implementation later.
                    System.Diagnostics.Debug.WriteLine(
                        $"[NET_TRACE] Unhandled Packet ID: 0x{packetId:X3} | Payload Size: {payload.Length} bytes"
                    );

                    // For a console build, you can also log directly to the terminal output thread:
                    // Console.WriteLine($"[MISSING COMPONENT] Packet 0x{packetId:X3} ({payload.Length} bytes)");
                    break;
            }
        }

        /// <summary>
        /// Exposes the character-specific cryptography engine instance to allow the network manager to encrypt outbound streams.
        /// </summary>
        internal BlowfishEngine GetCryptoEngine()
        {
            return _cryptoEngine;
        }
    }

    /// <summary>
    /// A high-performance, non-allocating Blowfish engine optimized for 64-bit multi-character packet arrays.
    /// </summary>
    internal sealed class BlowfishEngine
    {
        private readonly uint[] _pBox = new uint[18];
        private readonly uint[][] _sBoxes = new uint[4][]
        {
            new uint[256], new uint[256], new uint[256], new uint[256]
        };

        // Standard Blowfish cryptographic initialization vector parameters
        private static readonly uint[] OrigP = new uint[18] {
            0x243f6a88, 0x85a308d3, 0x13198a2e, 0x03707344, 0xa4093822, 0x299f31d0, 0x082efa98, 0xec4e6c89,
            0x452821e6, 0x38d01377, 0xbe5466cf, 0x34e90c6c, 0xc0ac29b7, 0xc97c50dd, 0x3f84d5b5, 0xb5470917,
            0x9216d5d9, 0x8979fb1b
        };

        public void InitializeKey(ReadOnlySpan<byte> key)
        {
            // Reset state tables to default standard vector parameters
            Array.Copy(OrigP, _pBox, 18);
            // In a production build, initialize S-Boxes here using standardized values or LSB dumps.

            if (key.Length == 0) return;

            int keyIndex = 0;
            for (int i = 0; i < 18; i++)
            {
                uint data = 0;
                for (int j = 0; j < 4; j++)
                {
                    data = (data << 8) | key[keyIndex];
                    keyIndex = (keyIndex + 1) % key.Length;
                }
                _pBox[i] ^= data;
            }

            uint left = 0, right = 0;
            for (int i = 0; i < 18; i += 2)
            {
                EncryptBlock(ref left, ref right);
                _pBox[i] = left;
                _pBox[i + 1] = right;
            }

            for (int i = 0; i < 4; i++)
            {
                for (int j = 0; j < 256; j += 2)
                {
                    EncryptBlock(ref left, ref right);
                    _sBoxes[i][j] = left;
                    _sBoxes[i][j + 1] = right;
                }
            }
        }

        /// <summary>
        /// Encrypts a stream of bytes in-place using Cipher Block Chaining (CBC) rules.
        /// </summary>
        public void EncryptCbc(Span<byte> data)
        {
            // Blowfish operates strictly on 8-byte blocks
            int blocks = data.Length / 8;
            uint xL, xR;
            uint ivL = 0, ivR = 0; // Instance-level initialization vectors matching server handshake specs

            for (int i = 0; i < blocks; i++)
            {
                int offset = i * 8;

                // Read the raw 4-byte plaintext halves from the span
                xL = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, 4));
                xR = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset + 4, 4));

                // CBC Rule: XOR the plaintext block with the previous ciphertext block before encrypting
                xL ^= ivL;
                xR ^= ivR;

                // Mutate registers through the core Blowfish P-Box/S-Box network rounds
                EncryptBlock(ref xL, ref xR);

                // Capture the new ciphertext registers to serve as the feedback IV for the next loop block
                ivL = xL;
                ivR = xR;

                // Write the encrypted ciphertext directly back into the exact same source memory slice
                BinaryPrimitives.WriteUInt32LittleEndian(data.Slice(offset, 4), xL);
                BinaryPrimitives.WriteUInt32LittleEndian(data.Slice(offset + 4, 4), xR);
            }
        }

        /// <summary>
        /// Decrypts a stream of bytes in-place using Cipher Block Chaining (CBC) rules.
        /// </summary>
        public void DecryptCbc(Span<byte> data)
        {
            // Blowfish requires 8-byte chunk blocks to run cipher routines
            int blocks = data.Length / 8;
            uint xL, xR;
            uint nextIvL = 0, nextIvR = 0;
            uint ivL = 0, ivR = 0; // Maintain internal session Initialization Vectors

            for (int i = 0; i < blocks; i++)
            {
                int offset = i * 8;

                // Read 4-byte halves out of the span array
                xL = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, 4));
                xR = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset + 4, 4));

                // Save ciphertext for the next CBC frame chain link step
                nextIvL = xL;
                nextIvR = xR;

                DecryptBlock(ref xL, ref xR);

                // XOR operation against previous Ciphertext block (CBC rule)
                xL ^= ivL;
                xR ^= ivR;

                // Advance vectors
                ivL = nextIvL;
                ivR = nextIvR;

                // Write plain data right back into the exact same source memory slice
                BinaryPrimitives.WriteUInt32LittleEndian(data.Slice(offset, 4), xL);
                BinaryPrimitives.WriteUInt32LittleEndian(data.Slice(offset + 4, 4), xR);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void EncryptBlock(ref uint left, ref uint right)
        {
            for (int i = 0; i < 16; i++)
            {
                left ^= _pBox[i];
                right ^= F(left);

                // Swap registers
                uint temp = left;
                left = right;
                right = temp;
            }
            uint t = left; left = right; right = t; // Undo last swap
            right ^= _pBox[16];
            left ^= _pBox[17];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void DecryptBlock(ref uint left, ref uint right)
        {
            left ^= _pBox[17];
            right ^= _pBox[16];

            for (int i = 15; i >= 0; i--)
            {
                uint temp = left;
                left = right;
                right = temp;

                right ^= F(left);
                left ^= _pBox[i];
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private uint F(uint x)
        {
            ushort d = (ushort)(x & 0x00FF);
            ushort c = (ushort)((x >> 8) & 0x00FF);
            ushort b = (ushort)((x >> 16) & 0x00FF);
            ushort a = (ushort)((x >> 24) & 0x00FF);

            uint y = _sBoxes[0][a] + _sBoxes[1][b];
            y ^= _sBoxes[2][c];
            y += _sBoxes[3][d];
            return y;
        }
    }
}
