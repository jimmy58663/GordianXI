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
    }
}
