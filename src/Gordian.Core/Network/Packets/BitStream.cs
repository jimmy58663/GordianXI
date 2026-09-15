// src/Gordian.Core/Network/Packets/BitStream.cs
// Protocol specification referenced from LandSandBoat (https://github.com/LandSandBoat/server/blob/base/src/common/utils.cpp)
// and Atom0s XiPackets research (https://github.com/atom0s/XiPackets/tree/main/world/server/0x0028).

using System;

namespace Gordian.Core.Network.Packets
{
    /// <summary>
    /// High-performance, zero-allocation reader for bit-packed FFXI network streams.
    /// Operates directly on <see cref="ReadOnlySpan{T}"/> matching FFXI's little-endian bit-packing schema.
    /// </summary>
    public ref struct BitStreamReader
    {
        private readonly ReadOnlySpan<byte> _data;
        private int _bitOffset;

        public BitStreamReader(ReadOnlySpan<byte> data, int initialBitOffset = 0)
        {
            _data = data;
            _bitOffset = initialBitOffset;
        }

        /// <summary>
        /// Gets or sets the current bit position within the underlying span.
        /// </summary>
        public int BitOffset
        {
            readonly get => _bitOffset;
            set => _bitOffset = value;
        }

        /// <summary>
        /// Gets the number of bits remaining in the buffer from the current bit offset.
        /// </summary>
        public readonly int RemainingBits => Math.Max(0, (_data.Length * 8) - _bitOffset);

        /// <summary>
        /// Reads up to 64 bits from the stream and advances the bit offset.
        /// </summary>
        /// <param name="lengthInBits">Number of bits to read (1 to 64).</param>
        /// <returns>Extracted bits as a 64-bit unsigned integer.</returns>
        public ulong ReadBits(int lengthInBits)
        {
            if (lengthInBits <= 0) return 0;
            if (lengthInBits > 64)
            {
                throw new ArgumentOutOfRangeException(nameof(lengthInBits), "Cannot read more than 64 bits in a single call.");
            }

            int byteOffset = _bitOffset >> 3;
            int bitOffset = _bitOffset & 7;

            int actualBytes = (bitOffset + lengthInBits + 7) / 8;
            if (byteOffset >= _data.Length)
            {
                _bitOffset += lengthInBits;
                return 0;
            }

            if (byteOffset + actualBytes > _data.Length)
            {
                actualBytes = _data.Length - byteOffset;
            }

            ulong data = 0;
            for (int i = 0; i < actualBytes; i++)
            {
                data |= ((ulong)_data[byteOffset + i]) << (8 * i);
            }

            ulong mask = (lengthInBits == 64) ? ulong.MaxValue : ((1UL << lengthInBits) - 1UL);
            ulong result = (data >> bitOffset) & mask;

            _bitOffset += lengthInBits;
            return result;
        }

        /// <summary>
        /// Reads up to 32 bits from the stream.
        /// </summary>
        public uint ReadUInt32(int lengthInBits = 32) => (uint)ReadBits(lengthInBits);

        /// <summary>
        /// Reads up to 16 bits from the stream.
        /// </summary>
        public ushort ReadUInt16(int lengthInBits = 16) => (ushort)ReadBits(lengthInBits);

        /// <summary>
        /// Reads up to 8 bits from the stream.
        /// </summary>
        public byte ReadByte(int lengthInBits = 8) => (byte)ReadBits(lengthInBits);

        /// <summary>
        /// Reads a single bit as a boolean.
        /// </summary>
        public bool ReadBool() => ReadBits(1) != 0;

        /// <summary>
        /// Peeks up to 64 bits from the stream without advancing the bit offset.
        /// </summary>
        public readonly ulong PeekBits(int lengthInBits)
        {
            var copy = this;
            return copy.ReadBits(lengthInBits);
        }
    }

    /// <summary>
    /// High-performance, zero-allocation writer for bit-packed FFXI network streams.
    /// Operates directly on <see cref="Span{T}"/> matching FFXI's little-endian bit-packing schema.
    /// </summary>
    public ref struct BitStreamWriter
    {
        private readonly Span<byte> _buffer;
        private int _bitOffset;

        public BitStreamWriter(Span<byte> buffer, int initialBitOffset = 0)
        {
            _buffer = buffer;
            _bitOffset = initialBitOffset;
        }

        /// <summary>
        /// Gets or sets the current bit position within the underlying span.
        /// </summary>
        public int BitOffset
        {
            readonly get => _bitOffset;
            set => _bitOffset = value;
        }

        /// <summary>
        /// Gets the total number of bytes written (rounded up to the nearest byte boundary).
        /// </summary>
        public readonly int TotalBytesWritten => (_bitOffset + 7) >> 3;

        /// <summary>
        /// Writes up to 64 bits into the buffer and advances the bit offset.
        /// </summary>
        /// <param name="value">The value whose lower <paramref name="lengthInBits"/> will be written.</param>
        /// <param name="lengthInBits">Number of bits to write (1 to 64).</param>
        public void WriteBits(ulong value, int lengthInBits)
        {
            if (lengthInBits <= 0) return;
            if (lengthInBits > 64)
            {
                throw new ArgumentOutOfRangeException(nameof(lengthInBits), "Cannot write more than 64 bits in a single call.");
            }

            int byteOffset = _bitOffset >> 3;
            int bitOffset = _bitOffset & 7;

            int actualBytes = (bitOffset + lengthInBits + 7) / 8;
            if (byteOffset + actualBytes > _buffer.Length)
            {
                throw new InvalidOperationException($"Buffer overflow: requires {byteOffset + actualBytes} bytes but buffer length is {_buffer.Length}.");
            }

            if (bitOffset + lengthInBits <= 56)
            {
                ulong bitmask = (lengthInBits == 64) ? ulong.MaxValue : ((1UL << lengthInBits) - 1UL);
                bitmask <<= bitOffset;

                value = (value << bitOffset) & bitmask;
                ulong invertedMask = ~bitmask;

                ulong data = 0;
                for (int i = 0; i < actualBytes; i++)
                {
                    data |= ((ulong)_buffer[byteOffset + i]) << (8 * i);
                }

                data &= invertedMask;
                data |= value;

                for (int i = 0; i < actualBytes; i++)
                {
                    _buffer[byteOffset + i] = (byte)((data >> (8 * i)) & 0xFF);
                }
            }
            else
            {
                // Bit-by-bit fallback for boundary conditions > 56 bits across unaligned byte offset
                for (int i = 0; i < lengthInBits; i++)
                {
                    int curByte = (_bitOffset + i) >> 3;
                    int curBit = (_bitOffset + i) & 7;
                    byte bitVal = (byte)((value >> i) & 1);
                    if (bitVal != 0)
                    {
                        _buffer[curByte] |= (byte)(1 << curBit);
                    }
                    else
                    {
                        _buffer[curByte] &= (byte)~(1 << curBit);
                    }
                }
            }

            _bitOffset += lengthInBits;
        }

        /// <summary>
        /// Writes up to 32 bits into the buffer.
        /// </summary>
        public void WriteUInt32(uint value, int lengthInBits = 32) => WriteBits(value, lengthInBits);

        /// <summary>
        /// Writes up to 16 bits into the buffer.
        /// </summary>
        public void WriteUInt16(ushort value, int lengthInBits = 16) => WriteBits(value, lengthInBits);

        /// <summary>
        /// Writes up to 8 bits into the buffer.
        /// </summary>
        public void WriteByte(byte value, int lengthInBits = 8) => WriteBits(value, lengthInBits);

        /// <summary>
        /// Writes a single bit boolean into the buffer.
        /// </summary>
        public void WriteBool(bool value) => WriteBits(value ? 1UL : 0UL, 1);
    }
}
