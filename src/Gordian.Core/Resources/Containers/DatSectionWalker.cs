// src/Gordian.Core/Resources/Containers/DatSectionWalker.cs
using System;
using System.Collections.Generic;

namespace Gordian.Core.Resources.Containers
{
    /// <summary>
    /// Fast scanner and enumerator for 16-byte chunked FFXI DAT file streams.
    /// Derived from community specifications in xi-model-viewer (https://github.com/vekien/xi-model-viewer).
    /// </summary>
    public static class DatSectionWalker
    {
        /// <summary>
        /// Scans the entire binary buffer and returns a list of all parsed section headers.
        /// </summary>
        public static List<DatSectionHeader> ReadHeaders(ReadOnlySpan<byte> data)
        {
            var results = new List<DatSectionHeader>();
            int pos = 0;
            int length = data.Length;

            while (pos + DatSectionHeader.HeaderSize <= length)
            {
                if (!DatSectionHeader.TryParse(data.Slice(pos), pos, out var header))
                {
                    break;
                }

                if (pos + header.SizeBytes > length)
                {
                    // Section declares a size exceeding remaining file bounds
                    break;
                }

                results.Add(header);
                pos += header.SizeBytes;
            }

            return results;
        }

        /// <summary>
        /// Checks if the binary buffer has the structure of a section-based FFXI DAT container
        /// by verifying valid headers cover at least 80% of the total file length.
        /// </summary>
        public static bool IsSectionContainer(ReadOnlySpan<byte> data)
        {
            if (data.Length < DatSectionHeader.HeaderSize)
            {
                return false;
            }

            int pos = 0;
            int count = 0;
            int length = data.Length;

            while (pos + DatSectionHeader.HeaderSize <= length)
            {
                if (!DatSectionHeader.TryParse(data.Slice(pos), pos, out var header))
                {
                    break;
                }

                if (pos + header.SizeBytes > length)
                {
                    break;
                }

                count++;
                pos += header.SizeBytes;
            }

            return count > 0 && pos >= (int)(length * 0.80);
        }

        /// <summary>
        /// Gets the payload data for the specified section header from the source memory buffer.
        /// </summary>
        public static ReadOnlyMemory<byte> GetSectionPayload(ReadOnlyMemory<byte> source, DatSectionHeader header)
        {
            if (header.DataOffset + header.DataSizeBytes > source.Length)
            {
                return ReadOnlyMemory<byte>.Empty;
            }

            return source.Slice(header.DataOffset, header.DataSizeBytes);
        }
    }
}
