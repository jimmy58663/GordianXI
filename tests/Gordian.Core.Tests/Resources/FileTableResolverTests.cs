// tests/Gordian.Core.Tests/Resources/FileTableResolverTests.cs
using System.Buffers.Binary;
using System.IO;
using Gordian.Core.Resources.Tables;
using Xunit;

namespace Gordian.Core.Tests.Resources
{
    public class FileTableResolverTests
    {
        [Fact]
        public void FileTableResolver_LoadTablePair_ResolvesFilePaths()
        {
            // ID 0: Unregistered in VTable (0)
            // ID 1: ROM root (1), ftVal: subdir 118, file 106 => ROM/118/106.DAT
            // ID 2: ROM2 root (2), ftVal: subdir 2, file 41 => ROM2/2/41.DAT
            byte[] ftable = new byte[6]; // 3 entries * 2 bytes
            byte[] vtable = new byte[3]; // 3 entries * 1 byte

            // Entry 0: unregistered
            vtable[0] = 0;

            // Entry 1: ROM root (1), subdir 118, file 106
            vtable[1] = 1;
            ushort ft1 = (ushort)((118 << 7) | 106);
            BinaryPrimitives.WriteUInt16LittleEndian(ftable.AsSpan(2, 2), ft1);

            // Entry 2: ROM2 root (2), subdir 2, file 41
            vtable[2] = 2;
            ushort ft2 = (ushort)((2 << 7) | 41);
            BinaryPrimitives.WriteUInt16LittleEndian(ftable.AsSpan(4, 2), ft2);

            var resolver = new FileTableResolver();
            resolver.LoadTablePair(ftable, vtable);

            Assert.Equal(2, resolver.Count);

            Assert.False(resolver.TryResolve(0, out _));

            Assert.True(resolver.TryResolve(1, out string path1));
            Assert.Equal(Path.Combine("ROM", "118", "106.DAT"), path1);

            Assert.True(resolver.TryResolve(2, out string path2));
            Assert.Equal(Path.Combine("ROM2", "2", "41.DAT"), path2);
        }

        [Fact]
        public void FileTableResolver_LowestTableWins_PreservesBaseRomEntries()
        {
            var resolver = new FileTableResolver();

            // Base game (ROM1) registers File ID 10 -> ROM/5/12.DAT
            byte[] ft1 = new byte[22];
            byte[] vt1 = new byte[11];
            vt1[10] = 1;
            ushort val1 = (ushort)((5 << 7) | 12);
            BinaryPrimitives.WriteUInt16LittleEndian(ft1.AsSpan(20, 2), val1);

            resolver.LoadTablePair(ft1, vt1);

            // Expansion (ROM2) attempts to register File ID 10 -> ROM2/99/88.DAT
            byte[] ft2 = new byte[22];
            byte[] vt2 = new byte[11];
            vt2[10] = 2;
            ushort val2 = (ushort)((99 << 7) | 88);
            BinaryPrimitives.WriteUInt16LittleEndian(ft2.AsSpan(20, 2), val2);

            // Expansion (ROM2) also registers new File ID 11 -> ROM2/1/2.DAT
            byte[] ft2Ext = new byte[24];
            byte[] vt2Ext = new byte[12];
            vt2Ext[10] = 2;
            BinaryPrimitives.WriteUInt16LittleEndian(ft2Ext.AsSpan(20, 2), val2);
            vt2Ext[11] = 2;
            ushort val3 = (ushort)((1 << 7) | 2);
            BinaryPrimitives.WriteUInt16LittleEndian(ft2Ext.AsSpan(22, 2), val3);

            resolver.LoadTablePair(ft2Ext, vt2Ext);

            // Lowest table wins: ID 10 should still point to ROM/5/12.DAT (not overwritten)
            Assert.True(resolver.TryResolve(10, out string resolved10));
            Assert.Equal(Path.Combine("ROM", "5", "12.DAT"), resolved10);

            // Newly added ID 11 from ROM2 should be registered
            Assert.True(resolver.TryResolve(11, out string resolved11));
            Assert.Equal(Path.Combine("ROM2", "1", "2.DAT"), resolved11);
        }
    }
}
