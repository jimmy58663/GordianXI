// tests/Gordian.Core.Tests/Resources/ItemTableDecoderTests.cs
using System;
using System.Buffers.Binary;
using System.Text;
using Gordian.Core.Resources.Tables;
using Xunit;

namespace Gordian.Core.Tests.Resources
{
    public class ItemTableDecoderTests
    {
        [Fact]
        public void ItemTable_BitRotation_RoundTripsCorrectly()
        {
            byte[] original = new byte[] { 0x12, 0x34, 0xAB, 0xCD, 0x00, 0xFF, 0x55, 0xAA };
            byte[] decrypted = new byte[original.Length];

            ItemTableDecoder.DecryptBlock(original, decrypted);

            // Re-encrypt (rotate right by 3 / left by 5)
            byte[] reEncrypted = new byte[original.Length];
            for (int i = 0; i < decrypted.Length; i++)
            {
                byte b = decrypted[i];
                reEncrypted[i] = (byte)((b >> 3) | (b << 5));
            }

            Assert.Equal(original, reEncrypted);
        }

        [Fact]
        public void ItemTable_DetectStride_DistinguishesLegacyAndRetail()
        {
            byte[] legacyBuf = new byte[ItemTableDecoder.LegacyStride];
            legacyBuf[^1] = ItemTableDecoder.RecordTerminator;
            Assert.Equal(ItemTableDecoder.LegacyStride, ItemTableDecoder.DetectStride(legacyBuf));

            byte[] retailBuf = new byte[ItemTableDecoder.RetailStride];
            retailBuf[^1] = ItemTableDecoder.RecordTerminator;
            Assert.Equal(ItemTableDecoder.RetailStride, ItemTableDecoder.DetectStride(retailBuf));
        }

        [Fact]
        public void ItemTable_DecodeSingleRecord_ExtractsWeaponStatsAndDescription()
        {
            byte[] block = new byte[ItemTableDecoder.LegacyStride];
            block[^1] = ItemTableDecoder.RecordTerminator;

            // Header fields (Legacy)
            BinaryPrimitives.WriteUInt32LittleEndian(block.AsSpan(0, 4), 16420); // Kraken Club
            BinaryPrimitives.WriteUInt16LittleEndian(block.AsSpan(0x04, 2), 0x0400); // Can equip
            BinaryPrimitives.WriteUInt16LittleEndian(block.AsSpan(0x06, 2), 1);      // Stack 1
            BinaryPrimitives.WriteUInt16LittleEndian(block.AsSpan(0x08, 2), 4);      // Weapon

            // Equipment fields
            BinaryPrimitives.WriteUInt16LittleEndian(block.AsSpan(0x0E, 2), 63);     // Level 63
            BinaryPrimitives.WriteUInt16LittleEndian(block.AsSpan(0x10, 2), 0x0001); // Main slot
            BinaryPrimitives.WriteUInt32LittleEndian(block.AsSpan(0x14, 4), 0x007FFFFE); // All jobs

            // Weapon fields
            BinaryPrimitives.WriteUInt16LittleEndian(block.AsSpan(0x1C, 2), 11);     // DMG: 11
            BinaryPrimitives.WriteInt16LittleEndian(block.AsSpan(0x1E, 2), 264);      // Delay: 264
            block[0x22] = 11; // Club skill

            // Strings table at offset 0x38:
            // 4 strings (Name, LogName, LogPlural, Description)
            int strTablePos = 0x38;
            BinaryPrimitives.WriteUInt32LittleEndian(block.AsSpan(strTablePos, 4), 4);

            int namePos = 0x80;
            int descPos = 0xA0;

            // String 0 (Name): offset
            BinaryPrimitives.WriteUInt32LittleEndian(block.AsSpan(strTablePos + 4, 4), (uint)namePos);
            BinaryPrimitives.WriteUInt32LittleEndian(block.AsSpan(strTablePos + 8, 4), 0);

            // String 3 (Desc): offset
            BinaryPrimitives.WriteUInt32LittleEndian(block.AsSpan(strTablePos + 4 + (3 * 8), 4), (uint)descPos);
            BinaryPrimitives.WriteUInt32LittleEndian(block.AsSpan(strTablePos + 8 + (3 * 8), 4), 0);

            // Write Name text:
            Encoding.ASCII.GetBytes("Kraken Club").CopyTo(block.AsSpan(namePos));
            block[namePos + 11] = 0;

            // Write Description text with stats:
            Encoding.ASCII.GetBytes("DMG:11 Delay:264 Additional effect: Attacks 2-8 times").CopyTo(block.AsSpan(descPos));
            block[descPos + 51] = 0;

            var item = ItemTableDecoder.DecodeSingleRecord(block, isRetail: false);

            Assert.NotNull(item);
            Assert.Equal(16420u, item.ItemId);
            Assert.Equal("Kraken Club", item.Name);
            Assert.Equal(63, item.Level);
            Assert.Equal(11, item.Damage);
            Assert.Equal(264, item.Delay);
            Assert.Equal(11, item.Skill);

            // Verify extracted stats
            Assert.True(item.ExtractedStats.TryGetValue("DMG", out int dmg));
            Assert.Equal(11, dmg);
            Assert.True(item.ExtractedStats.TryGetValue("Delay", out int delay));
            Assert.Equal(264, delay);
        }
    }
}
