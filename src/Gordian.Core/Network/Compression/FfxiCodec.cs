// src/Gordian.Core/Network/Compression/FfxiCodec.cs
using System;
using System.Buffers.Binary;
using System.Runtime.CompilerServices;

namespace Gordian.Core.Network.Compression
{
    /// <summary>
    /// Implements the high-performance FFXI custom bitstream compressor and decompressor,
    /// matching LandSandBoat's zlib_compress and zlib_decompress.
    /// Operates entirely in-place with zero managed heap allocations during streaming.
    /// </summary>
    public sealed class FfxiCodec
    {
        private static readonly Lazy<FfxiCodec> _defaultInstance =
            new(() => new FfxiCodec(EmbeddedCompressionTableProvider.Instance));

        /// <summary>
        /// Gets the default shared FfxiCodec instance using embedded protocol tables.
        /// </summary>
        public static FfxiCodec Default => _defaultInstance.Value;

        private readonly uint[] _enc;
        private readonly int[] _jumps;

        // Fast 8-bit decode table: maps next 8 bits -> (symbol, length). If length == 0, code is > 8 bits.
        private readonly byte[] _dtabSymbol = new byte[256];
        private readonly byte[] _dtabLength = new byte[256];

        public FfxiCodec(IFfxiCompressionTableProvider provider)
        {
            ArgumentNullException.ThrowIfNull(provider);
            _enc = (uint[])provider.GetCompressTable().Clone();
            uint[] dec = provider.GetDecompressTable();

            _jumps = new int[dec.Length];
            PopulateJumpTable(dec);
            BuildFastDecodeTable();
        }

        private void PopulateJumpTable(uint[] dec)
        {
            uint baseVal = dec[0] - 4; // sizeof(uint32)

            for (int i = 0; i < dec.Length; i++)
            {
                if (dec[i] > 0xFF)
                {
                    _jumps[i] = (int)((dec[i] - baseVal) / 4);
                }
                else
                {
                    _jumps[i] = (int)dec[i];
                }
            }
        }

        private void BuildFastDecodeTable()
        {
            int root = _jumps[0];

            for (uint b = 0; b < 256; b++)
            {
                int pos = root;
                _dtabSymbol[b] = 0;
                _dtabLength[b] = 0;

                for (int bit = 0; bit < 8; bit++)
                {
                    int bitVal = (int)((b >> bit) & 1);
                    pos = _jumps[pos + bitVal];

                    if (_jumps[pos] == 0 && _jumps[pos + 1] == 0)
                    {
                        _dtabSymbol[b] = (byte)_jumps[pos + 3];
                        _dtabLength[b] = (byte)(bit + 1);
                        break;
                    }
                }
            }
        }

        /// <summary>
        /// Compresses uncompressed payload bytes into the FFXI packet payload format.
        /// First byte of destination is 0x01.
        /// Trailing 4 bytes contains the bit count uint32 (read + 8).
        /// Total written = (bitCount + 7) / 8 + 4.
        /// </summary>
        /// <param name="input">Source uncompressed payload.</param>
        /// <param name="destination">Destination memory buffer (must be at least input.Length + 16 bytes).</param>
        /// <returns>Total bytes written to destination.</returns>
        public int Compress(ReadOnlySpan<byte> input, Span<byte> destination)
        {
            if (destination.Length < 6)
            {
                throw new ArgumentException("Destination buffer is too small for compression.", nameof(destination));
            }

            if (input.IsEmpty)
            {
                destination[0] = 1;
                // 8 bits emitted (0 data bits + 8 header bits)
                BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(1, 4), 8);
                return 5;
            }

            uint maxSz = (uint)(destination.Length - 5) * 8;
            Span<byte> output = destination.Slice(1);

            ulong acc = 0;
            int accBits = 0;
            int outPos = 0;
            uint totalBitsEmitted = 0;

            for (int i = 0; i < input.Length; i++)
            {
                sbyte sb = (sbyte)input[i];
                uint elem = _enc[sb + 0x180];

                if (elem + totalBitsEmitted >= maxSz)
                {
                    throw new InvalidOperationException("Compression buffer overflowed max capacity.");
                }

                uint v = _enc[sb + 0x80];
                uint code = (elem >= 32) ? v : (v & ((1U << (int)elem) - 1));

                acc |= ((ulong)code) << accBits;
                accBits += (int)elem;
                totalBitsEmitted += elem;

                while (accBits >= 8)
                {
                    output[outPos++] = (byte)(acc & 0xFF);
                    acc >>= 8;
                    accBits -= 8;
                }
            }

            if (accBits > 0)
            {
                output[outPos++] = (byte)(acc & 0xFF);
            }

            destination[0] = 1;

            // In LSB: return read + 8 (total bits including 8 bits for the 0x01 marker)
            uint totalBitLen = totalBitsEmitted + 8;
            int compressedPayloadSize = (int)((totalBitLen + 7) / 8);

            // Append 4-byte bit count at compressedPayloadSize
            BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(compressedPayloadSize, 4), totalBitLen);

            return compressedPayloadSize + 4;
        }

        /// <summary>
        /// Decompresses an incoming FFXI packet payload.
        /// The last 4 bytes of compressedData contain the 32-bit bit count.
        /// </summary>
        /// <param name="compressedData">Compressed byte span (must begin with 0x01 marker and end with 4-byte bit count).</param>
        /// <param name="decompressed">Destination span to receive uncompressed data.</param>
        /// <returns>The number of uncompressed bytes produced.</returns>
        public int Decompress(ReadOnlySpan<byte> compressedData, Span<byte> decompressed)
        {
            if (compressedData.Length < 5 || compressedData[0] != 1)
            {
                throw new InvalidOperationException("Invalid compressed FFXI data (missing 0x01 marker or too short).");
            }

            // Extract the 32-bit bit length from the trailing 4 bytes
            uint totalBitLen = BinaryPrimitives.ReadUInt32LittleEndian(compressedData.Slice(compressedData.Length - 4, 4));
            
            // The bit count in LSB includes the 8 bits of marker 0x01, so payload bits = totalBitLen - 8
            if (totalBitLen < 8)
            {
                return 0;
            }

            uint bitCount = totalBitLen - 8;
            ReadOnlySpan<byte> data = compressedData.Slice(1, compressedData.Length - 5);

            int root = _jumps[0];
            int byteLen = data.Length;

            int bitpos = 0;
            int w = 0;
            int outSz = decompressed.Length;

            while (bitpos < bitCount && w < outSz)
            {
                int byteoff = bitpos >> 3;
                int bitoff = bitpos & 7;

                if (byteoff >= byteLen) break;

                uint currentByte = data[byteoff];
                uint hi = (byteoff + 1 < byteLen) ? (uint)data[byteoff + 1] : 0U;
                uint peek = ((currentByte >> bitoff) | (hi << (8 - bitoff))) & 0xFF;

                byte fastLen = _dtabLength[peek];
                if (fastLen != 0 && (bitpos + fastLen <= bitCount))
                {
                    decompressed[w++] = _dtabSymbol[peek];
                    bitpos += fastLen;
                }
                else
                {
                    // Code longer than 8 bits or near boundary: walk the tree bit by bit
                    int pos = root;
                    while (bitpos < bitCount)
                    {
                        int bIdx = bitpos >> 3;
                        int bShift = bitpos & 7;
                        int bitVal = (data[bIdx] >> bShift) & 1;
                        bitpos++;

                        pos = _jumps[pos + bitVal];

                        if (_jumps[pos] == 0 && _jumps[pos + 1] == 0)
                        {
                            decompressed[w++] = (byte)_jumps[pos + 3];
                            break;
                        }
                    }
                }
            }

            return w;
        }
    }
}
