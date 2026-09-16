// src/Gordian.Core/Resources/Containers/DatSectionHeader.cs
using System;
using System.Buffers.Binary;
using System.Text;

namespace Gordian.Core.Resources.Containers
{
    /// <summary>
    /// Represents the standard 16-byte chunk header used throughout FFXI DAT files.
    /// Derived from community specifications in xi-model-viewer (https://github.com/vekien/xi-model-viewer)
    /// and xi-tools (xi/common/xi_section.py).
    /// </summary>
    public readonly record struct DatSectionHeader
    {
        public const int HeaderSize = 16;

        /// <summary>
        /// 4-character section identifier / tag, trimmed of trailing nulls and whitespace.
        /// </summary>
        public string DatId { get; }

        /// <summary>
        /// 7-bit section type discriminator code.
        /// </summary>
        public DatSectionType TypeCode { get; }

        /// <summary>
        /// Raw section type byte value.
        /// </summary>
        public byte RawTypeCode { get; }

        /// <summary>
        /// Total size of the section in bytes, including the 16-byte header itself.
        /// Stored as a 19-bit field in 16-byte units.
        /// </summary>
        public int SizeBytes { get; }

        /// <summary>
        /// Header flags extracted from bits 26-31 of the metadata DWORD (e.g. shadow, extracted, virtual).
        /// </summary>
        public byte Flags { get; }

        /// <summary>
        /// Byte offset in the source buffer where this section starts.
        /// </summary>
        public int Offset { get; }

        /// <summary>
        /// Byte offset in the source buffer where the section payload data starts (Offset + 16).
        /// </summary>
        public int DataOffset => Offset + HeaderSize;

        /// <summary>
        /// Size of the section payload in bytes (SizeBytes - 16).
        /// </summary>
        public int DataSizeBytes => Math.Max(0, SizeBytes - HeaderSize);

        public DatSectionHeader(string datId, DatSectionType typeCode, byte rawTypeCode, int sizeBytes, byte flags, int offset)
        {
            DatId = datId;
            TypeCode = typeCode;
            RawTypeCode = rawTypeCode;
            SizeBytes = sizeBytes;
            Flags = flags;
            Offset = offset;
        }

        /// <summary>
        /// Attempts to parse a 16-byte section header from the specified buffer slice.
        /// </summary>
        public static bool TryParse(ReadOnlySpan<byte> span, int absoluteOffset, out DatSectionHeader header)
        {
            if (span.Length < HeaderSize)
            {
                header = default;
                return false;
            }

            // Extract 4-byte ASCII ID
            Span<char> idChars = stackalloc char[4];
            int idLen = 0;
            for (int i = 0; i < 4; i++)
            {
                byte b = span[i];
                if (b == 0) break;
                idChars[idLen++] = (char)b;
            }
            string datId = new string(idChars.Slice(0, idLen)).TrimEnd();

            // Extract metadata DWORD at offset 4
            uint meta = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(4, 4));
            byte rawType = (byte)(meta & 0x7F);
            DatSectionType type = (DatSectionType)rawType;

            // 19-bit unit count: bits 7..25 (0x7FFFF) * 16 bytes
            uint units = (meta >> 7) & 0x7FFFF;
            int sizeBytes = (int)(units * HeaderSize);

            // Flags: bits 26..31
            byte flags = (byte)((meta >> 26) & 0x3F);

            if (sizeBytes < HeaderSize)
            {
                header = default;
                return false;
            }

            header = new DatSectionHeader(datId, type, rawType, sizeBytes, flags, absoluteOffset);
            return true;
        }
    }
}
