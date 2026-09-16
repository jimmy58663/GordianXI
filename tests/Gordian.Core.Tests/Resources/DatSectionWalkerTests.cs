// tests/Gordian.Core.Tests/Resources/DatSectionWalkerTests.cs
using System;
using System.Buffers.Binary;
using System.Text;
using Gordian.Core.Resources.Containers;
using Xunit;

namespace Gordian.Core.Tests.Resources
{
    public class DatSectionWalkerTests
    {
        [Fact]
        public void DatSectionHeader_TryParse_DecodesValidHeader()
        {
            byte[] headerBytes = new byte[16];
            Encoding.ASCII.GetBytes("test").CopyTo(headerBytes.AsSpan(0, 4));

            // Type = 0x20 (Texture), Size = 64 bytes (4 units of 16), Flags = 3
            uint type = (uint)DatSectionType.Texture;
            uint units = 4;
            uint flags = 3;
            uint meta = type | (units << 7) | (flags << 26);
            BinaryPrimitives.WriteUInt32LittleEndian(headerBytes.AsSpan(4, 4), meta);

            bool ok = DatSectionHeader.TryParse(headerBytes, 100, out var header);

            Assert.True(ok);
            Assert.Equal("test", header.DatId);
            Assert.Equal(DatSectionType.Texture, header.TypeCode);
            Assert.Equal(64, header.SizeBytes);
            Assert.Equal(48, header.DataSizeBytes);
            Assert.Equal(flags, header.Flags);
            Assert.Equal(100, header.Offset);
            Assert.Equal(116, header.DataOffset);
        }

        [Fact]
        public void DatSectionHeader_TryParse_FailsOnTruncatedBuffer()
        {
            byte[] shortBytes = new byte[10];
            bool ok = DatSectionHeader.TryParse(shortBytes, 0, out _);
            Assert.False(ok);
        }

        [Fact]
        public void DatSectionWalker_ReadHeaders_EnumeratesContiguousSections()
        {
            // Create buffer with 2 sections: 32 bytes (Dir) and 48 bytes (Texture)
            byte[] buffer = new byte[80];

            // Section 1: Dir, 32 bytes (2 units)
            Encoding.ASCII.GetBytes("dir1").CopyTo(buffer.AsSpan(0, 4));
            uint meta1 = (uint)DatSectionType.Directory | (2u << 7);
            BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(4, 4), meta1);

            // Section 2: Texture, 48 bytes (3 units)
            Encoding.ASCII.GetBytes("tex1").CopyTo(buffer.AsSpan(32, 4));
            uint meta2 = (uint)DatSectionType.Texture | (3u << 7);
            BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(36, 4), meta2);

            var headers = DatSectionWalker.ReadHeaders(buffer);

            Assert.Equal(2, headers.Count);
            Assert.Equal("dir1", headers[0].DatId);
            Assert.Equal(DatSectionType.Directory, headers[0].TypeCode);
            Assert.Equal(32, headers[0].SizeBytes);
            Assert.Equal(0, headers[0].Offset);

            Assert.Equal("tex1", headers[1].DatId);
            Assert.Equal(DatSectionType.Texture, headers[1].TypeCode);
            Assert.Equal(48, headers[1].SizeBytes);
            Assert.Equal(32, headers[1].Offset);
        }

        [Fact]
        public void DatDirectoryTree_BuildsHierarchy_AndResolvesScopedLinks()
        {
            // Buffer layout:
            // 0..31:   Dir "main" (Type 0x01, size 32)
            // 32..63:  Mesh "mesh1" (Type 0x2A, size 32)
            // 64..95:  Dir "sub" (Type 0x01, size 32)
            // 96..127: Texture "tex1" (Type 0x20, size 32)
            // 128..143: End (Type 0x00, size 16)
            // 144..159: End (Type 0x00, size 16)
            byte[] buffer = new byte[160];

            // 1. Dir "main"
            Encoding.ASCII.GetBytes("main").CopyTo(buffer.AsSpan(0, 4));
            BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(4, 4), (uint)DatSectionType.Directory | (2u << 7));

            // 2. Mesh "mesh1"
            Encoding.ASCII.GetBytes("msh1").CopyTo(buffer.AsSpan(32, 4));
            BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(36, 4), (uint)DatSectionType.SkeletonMesh | (2u << 7));

            // 3. Dir "sub"
            Encoding.ASCII.GetBytes("sub").CopyTo(buffer.AsSpan(64, 4));
            BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(68, 4), (uint)DatSectionType.Directory | (2u << 7));

            // 4. Texture "tex1"
            Encoding.ASCII.GetBytes("tex1").CopyTo(buffer.AsSpan(96, 4));
            BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(100, 4), (uint)DatSectionType.Texture | (2u << 7));

            // 5. End sub
            BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(132, 4), (uint)DatSectionType.End | (1u << 7));

            // 6. End main
            BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(148, 4), (uint)DatSectionType.End | (1u << 7));

            var root = DatDirectoryTree.Build(buffer);

            Assert.Single(root.SubDirectories);
            var mainDir = root.SubDirectories[0];
            Assert.Equal("main", mainDir.DatId);
            Assert.Single(mainDir.Resources);
            Assert.Equal("msh1", mainDir.Resources[0].DatId);

            Assert.Single(mainDir.SubDirectories);
            var subDir = mainDir.SubDirectories[0];
            Assert.Equal("sub", subDir.DatId);
            Assert.Single(subDir.Resources);
            Assert.Equal("tex1", subDir.Resources[0].DatId);

            // Scoped resolution:
            // From subDir, finding tex1 locally
            var resLocal = subDir.SearchLocalAndParents("tex1", DatSectionType.Texture);
            Assert.NotNull(resLocal);
            Assert.Equal("tex1", resLocal.DatId);

            // From subDir, finding msh1 in parent
            var resParent = subDir.SearchLocalAndParents("msh1", DatSectionType.SkeletonMesh);
            Assert.NotNull(resParent);
            Assert.Equal("msh1", resParent.DatId);

            // Global search
            var resGlobal = root.FindFirstInEntireTree("tex1", DatSectionType.Texture);
            Assert.NotNull(resGlobal);
            Assert.Equal("tex1", resGlobal.DatId);

            // Collect recursive
            var allTextures = root.CollectByTypeRecursive(DatSectionType.Texture);
            Assert.Single(allTextures);
            Assert.Equal("tex1", allTextures[0].DatId);
        }
    }
}
