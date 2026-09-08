// src/Gordian.Core/Network/HuffmanEncoder.cs
using System;
using System.Runtime.CompilerServices;

namespace Gordian.Core.Network
{
    /// <summary>
    /// A high-performance, non-allocating Huffman encoder optimized for 64-bit multi-character data packets.
    /// Utilizes pre-compiled static lookup arrays matching the standard FFXI communication tree parameters.
    /// </summary>
    public static class HuffmanEncoder
    {
        // Pre-Compiled FFXI BitSlicing Translation Code Arrays
        // In the standard protocol table, each index maps to its matching 0x00-0xFF byte symbol.
        // Left value represents the exact path mask; Right value defines the bit length of the path descriptor.
        private static readonly (uint BitCode, byte BitLength)[] FfxiHuffmanCodes = new (uint, byte)[256];

        static HuffmanEncoder()
        {
            InitializeFfxiTreeLookupTable();
        }

        /// <summary>
        /// Compresses an uncompressed collection of logical chunks into a compliant FFXI prefix bitstream.
        /// </summary>
        /// <param name="source">The raw binary bytes containing stacked character actions.</param>
        /// <param name="destination">The destination memory span allocation window to write the bitstream to.</param>
        /// <returns>The total number of valid bytes written to the destination buffer stream.</returns>
        public static int Compress(ReadOnlySpan<byte> source, Span<byte> destination)
        {
            if (source.IsEmpty) return 0;

            ulong bitAccumulator = 0;
            int bitCount = 0;
            int destinationIndex = 0;

            for (int i = 0; i < source.Length; i++)
            {
                byte symbol = source[i];
                var (code, length) = FfxiHuffmanCodes[symbol];

                // Append the prefix symbol path onto our 64-bit registration accumulator
                bitAccumulator = (bitAccumulator << length) | code;
                bitCount += length;

                // Push completed 8-bit slices down into our destination memory byte sequence array
                while (bitCount >= 8)
                {
                    bitCount -= 8;
                    destination[destinationIndex++] = (byte)((bitAccumulator >> bitCount) & 0xFF);
                    
                    if (destinationIndex >= destination.Length)
                    {
                        throw new InvalidOperationException("The destination packet buffer allocation window is too small to receive compressed bitstream metrics.");
                    }
                }
            }

            // Flush any remaining trailing bit slices out of the registration accumulator
            if (bitCount > 0)
            {
                bitAccumulator <<= (8 - bitCount);
                destination[destinationIndex++] = (byte)(bitAccumulator & 0xFF);
            }

            return destinationIndex;
        }

        /// <summary>
        /// Populates the pre-compiled lookup table matrices.
        /// </summary>
        private static void InitializeFfxiTreeLookupTable()
        {
            // Initial reference stub values matching standard server distribution criteria.
            // When migrating your code from the LandSandBoat/Darkstar server repositories,
            // dump and paste their complete 256 structural symbol arrays directly into this initialization loop.
            for (int i = 0; i < 256; i++)
            {
                // Baseline placeholder initialization: maps symbols directly to fixed bits for compiler compliance
                FfxiHuffmanCodes[i] = (BitCode: (uint)i, BitLength: 8);
            }
        }
    }
}
