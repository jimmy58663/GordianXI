// src/Gordian.Core/Audio/Atrac3/Atrac3BitReader.cs
using System;

namespace Gordian.Core.Audio.Atrac3
{
    /// <summary>
    /// Reads one ATRAC3 sound unit as a bit string: most significant bit of each byte first, bytes in address order, no
    /// alignment between fields (spec <c>docs/audio/atrac3.md</c> section 4). Reading past the end sets
    /// <see cref="Overrun"/> and yields zero bits, so a malformed unit is detected without exceptions.
    /// </summary>
    internal ref struct Atrac3BitReader
    {
        private readonly ReadOnlySpan<byte> _data;
        private int _position;

        /// <summary>Starts reading at the first bit of <paramref name="data"/>.</summary>
        public Atrac3BitReader(ReadOnlySpan<byte> data)
        {
            _data = data;
            _position = 0;
            Overrun = false;
        }

        /// <summary>True once a read went past the last bit.</summary>
        public bool Overrun { get; private set; }

        /// <summary>The bit position of the next read.</summary>
        public readonly int Position => _position;

        /// <summary>Bits left before the end.</summary>
        public readonly int Remaining => _data.Length * 8 - _position;

        /// <summary><c>u(n)</c>: the next <paramref name="count"/> bits (0-24) as an unsigned integer, first bit most significant.</summary>
        public int Read(int count)
        {
            int value = Peek(count);
            Skip(count);
            return value;
        }

        /// <summary><c>s(n)</c>: the next <paramref name="count"/> bits (1-24) as a two's complement integer.</summary>
        public int ReadSigned(int count)
        {
            int value = Read(count);
            int shift = 32 - count;
            return (value << shift) >> shift;
        }

        /// <summary>The next <paramref name="count"/> bits (0-24) without consuming them; bits past the end read as 0.</summary>
        public readonly int Peek(int count)
        {
            if (count == 0)
            {
                return 0;
            }

            int byteIndex = _position >> 3;
            uint window = 0;
            for (int i = 0; i < 4; i++)
            {
                window <<= 8;
                int at = byteIndex + i;
                if ((uint)at < (uint)_data.Length)
                {
                    window |= _data[at];
                }
            }

            window <<= _position & 7;
            return (int)(window >> (32 - count));
        }

        /// <summary>Consumes <paramref name="count"/> bits.</summary>
        public void Skip(int count)
        {
            _position += count;
            if (_position > _data.Length * 8)
            {
                Overrun = true;
            }
        }
    }
}
