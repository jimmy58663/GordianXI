// tests/Gordian.Core.Tests/Resources/DMsgStringTableTests.cs
using System;
using System.Buffers.Binary;
using System.Text;
using Gordian.Core.Resources.Tables;
using Xunit;

namespace Gordian.Core.Tests.Resources
{
    public class DMsgStringTableTests
    {
        [Fact]
        public void DMsg_DecodeTextWithGlyphs_ConvertsElementIcons()
        {
            // Text: "Damage: " + [0xEF, 0x1F] (Fire) + " +5"
            byte[] textBytes = new byte[]
            {
                (byte)'D', (byte)'a', (byte)'m', (byte)'a', (byte)'g', (byte)'e', (byte)':', (byte)' ',
                0xEF, 0x1F, // Fire glyph
                (byte)' ', (byte)'+', (byte)'5', 0x00
            };

            string decoded = DMsgStringTable.DecodeTextWithGlyphs(textBytes);
            Assert.Equal("Damage: Fire +5", decoded);
        }

        [Fact]
        public void DMsg_Parse_FixedStride_WithXorDecryption()
        {
            // Construct a synthetic fixed-stride d_msg buffer
            // Header: 0x40 (64) bytes
            // Payload: 1 block of stride 64 bytes
            int stride = 64;
            int tableOffset = 64;
            int fileSize = tableOffset + stride;
            byte[] buffer = new byte[fileSize];

            // Signature: "d_msg"
            Encoding.ASCII.GetBytes("d_msg").CopyTo(buffer.AsSpan(0, 5));
            buffer[0x0A] = 0x01; // Enable XOR 0xFF mask

            BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(0x14, 4), (uint)fileSize);
            BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(0x18, 4), (uint)tableOffset);
            BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(0x1C, 4), 0); // tableSize = 0 => fixed stride
            BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(0x20, 4), (uint)stride);
            BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(0x28, 4), 1); // 1 entry

            // Construct decrypted block at tableOffset:
            // Word 0: 1 sub-string
            // Word 1: offset = 12, Word 2: flag = 0
            // At offset 12: marker = 1 (string), followed by 0x18 meta, followed by "Cure" null-terminated
            byte[] block = new byte[stride];
            BinaryPrimitives.WriteUInt32LittleEndian(block.AsSpan(0, 4), 1);
            BinaryPrimitives.WriteUInt32LittleEndian(block.AsSpan(4, 4), 12); // sub-offset
            BinaryPrimitives.WriteUInt32LittleEndian(block.AsSpan(8, 4), 0);  // flag

            int markerPos = 12;
            BinaryPrimitives.WriteUInt32LittleEndian(block.AsSpan(markerPos, 4), 1); // string marker
            int textPos = markerPos + 4 + 0x18; // 12 + 4 + 24 = 40
            Encoding.ASCII.GetBytes("Cure").CopyTo(block.AsSpan(textPos, 4));
            block[textPos + 4] = 0; // null terminator

            // Encrypt block with XOR 0xFF and copy into buffer
            for (int i = 0; i < block.Length; i++)
            {
                buffer[tableOffset + i] = (byte)(block[i] ^ 0xFF);
            }

            var table = DMsgStringTable.Parse(buffer, new[] { "spell_name" });

            Assert.NotNull(table);
            Assert.Equal(1, table.Count);
            Assert.Equal("Cure", table.Records[0].PrimaryText);
            Assert.Equal("Cure", table.Records[0].NamedFields["spell_name"]);
        }
    

        /// <summary>
        /// Builds a one-block fixed-stride table whose block holds a numeric sub-entry (value <paramref name="id"/>)
        /// followed by a text sub-entry, like a key-item row.
        /// </summary>
        private static byte[] BuildIdAndNameTable(params (uint Id, string Name)[] rows)
        {
            const int stride = 96;
            const int tableOffset = 64;
            int fileSize = tableOffset + stride * rows.Length;
            byte[] buffer = new byte[fileSize];

            Encoding.ASCII.GetBytes("d_msg").CopyTo(buffer.AsSpan(0, 5));
            BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(0x14, 4), (uint)fileSize);
            BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(0x18, 4), tableOffset);
            BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(0x20, 4), stride);
            BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(0x28, 4), (uint)rows.Length);

            for (int r = 0; r < rows.Length; r++)
            {
                var block = buffer.AsSpan(tableOffset + r * stride, stride);
                BinaryPrimitives.WriteUInt32LittleEndian(block.Slice(0, 4), 2);
                BinaryPrimitives.WriteUInt32LittleEndian(block.Slice(4, 4), 20);  // sub 0 at 20
                BinaryPrimitives.WriteUInt32LittleEndian(block.Slice(8, 4), 1);   // kind 1: number
                BinaryPrimitives.WriteUInt32LittleEndian(block.Slice(12, 4), 24); // sub 1 at 24
                BinaryPrimitives.WriteUInt32LittleEndian(block.Slice(16, 4), 0);  // kind 0: text
                BinaryPrimitives.WriteUInt32LittleEndian(block.Slice(20, 4), rows[r].Id);
                BinaryPrimitives.WriteUInt32LittleEndian(block.Slice(24, 4), 1);
                Encoding.ASCII.GetBytes(rows[r].Name).CopyTo(block.Slice(24 + 4 + 0x18));
            }

            return buffer;
        }

        [Fact]
        public void DMsg_Parse_NumericOne_StaysNumeric()
        {
            var table = DMsgStringTable.Parse(BuildIdAndNameTable((1, "Zeruhn report")), new[] { "id", "name" });

            Assert.NotNull(table);
            var record = table!.Records[0];
            Assert.True(record.TryGetNumber(0, out uint id));
            Assert.Equal(1u, id);
            Assert.Equal("1", record.NamedFields["id"]);
            Assert.Equal("Zeruhn report", record.NamedFields["name"]);
            Assert.False(record.TryGetNumber(1, out _));
        }

        [Fact]
        public void DMsg_TryGetById_UsesSubEntryZeroNotRow()
        {
            var table = DMsgStringTable.Parse(BuildIdAndNameTable((8, "airship pass"), (1, "Zeruhn report"), (3072, "Chocobo companion")),
                new[] { "id", "name" });

            Assert.NotNull(table);
            Assert.True(table!.TryGetById(1, out var zeruhn));
            Assert.Equal("Zeruhn report", zeruhn.NamedFields["name"]);
            Assert.True(table.TryGetById(3072, out var chocobo));
            Assert.Equal(2, chocobo.Index);
            Assert.False(table.TryGetById(2, out _));
        }
    }
}
