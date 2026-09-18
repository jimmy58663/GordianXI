// tests/Gordian.Core.Tests/Resources/EntityModelLoaderTests.cs
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Numerics;
using System.Text;
using Gordian.Core.Resources;
using Gordian.Core.Resources.Containers;
using Gordian.Core.Resources.Models;
using Gordian.Core.Resources.Tables;
using Xunit;

namespace Gordian.Core.Tests.Resources
{
    public class EntityModelLoaderTests
    {
        private static byte[] CreateChunk(DatSectionType type, byte[] payload)
        {
            // 16-byte chunk header: 4-char ID, u32 metadata (bits 0..6 type, bits 7..26 size in 16-byte units)
            int dataLen = payload.Length;
            int totalBytes = 16 + dataLen;
            int unitsOf16 = (totalBytes + 15) / 16;
            int paddedTotal = unitsOf16 * 16;

            byte[] chunk = new byte[paddedTotal];
            Encoding.ASCII.GetBytes("test").CopyTo(chunk.AsSpan(0, 4));

            uint meta = (uint)type | (uint)(unitsOf16 << 7);
            BinaryPrimitives.WriteUInt32LittleEndian(chunk.AsSpan(4, 4), meta);

            payload.CopyTo(chunk.AsSpan(16));
            return chunk;
        }

        [Fact]
        public void EntityModelLoader_AssembleModel_StitchesSkeletonAndMeshes()
        {
            // 1. Primary DAT with Skeleton
            byte[] skelPayload = new byte[4 + 30]; // 1 joint
            skelPayload[2] = 1; // numJoints = 1
            skelPayload[4] = 0; // parent = self => -1
            BinaryPrimitives.WriteSingleLittleEndian(skelPayload.AsSpan(18, 4), 0f);
            BinaryPrimitives.WriteSingleLittleEndian(skelPayload.AsSpan(22, 4), 1f);
            BinaryPrimitives.WriteSingleLittleEndian(skelPayload.AsSpan(26, 4), 0f);
            BinaryPrimitives.WriteSingleLittleEndian(skelPayload.AsSpan(30, 4), 1f); // qw

            byte[] primaryDat = CreateChunk(DatSectionType.Skeleton, skelPayload);

            // 2. Extra DAT with SkeletonMesh (triangle list)
            int meshSize = 188;
            byte[] meshPayload = new byte[meshSize];
            BinaryPrimitives.WriteInt32LittleEndian(meshPayload.AsSpan(6, 4), 134 / 2); // insOffset
            BinaryPrimitives.WriteInt32LittleEndian(meshPayload.AsSpan(18, 4), 42 / 2);  // vertCountOffset
            BinaryPrimitives.WriteUInt16LittleEndian(meshPayload.AsSpan(22, 2), 2);
            BinaryPrimitives.WriteInt32LittleEndian(meshPayload.AsSpan(24, 4), 46 / 2);  // vertJointOffset
            BinaryPrimitives.WriteInt32LittleEndian(meshPayload.AsSpan(30, 4), 54 / 2);  // vertDataOffset

            BinaryPrimitives.WriteUInt16LittleEndian(meshPayload.AsSpan(42, 2), 3); // 3 single verts
            BinaryPrimitives.WriteUInt16LittleEndian(meshPayload.AsSpan(44, 2), 0); // 0 double

            // Joint mapping for 3 verts
            for (int i = 0; i < 3; i++)
            {
                BinaryPrimitives.WriteUInt16LittleEndian(meshPayload.AsSpan(46 + (i * 4), 2), 0); // joint 0
            }

            // Vert 0 (0,0,0), Vert 1 (1,0,0), Vert 2 (0,1,0)
            BinaryPrimitives.WriteSingleLittleEndian(meshPayload.AsSpan(54, 4), 0f);
            BinaryPrimitives.WriteSingleLittleEndian(meshPayload.AsSpan(78, 4), 1f);
            BinaryPrimitives.WriteSingleLittleEndian(meshPayload.AsSpan(106, 4), 1f);

            // Instructions: Texture name "armor_tex" and 1 tri
            BinaryPrimitives.WriteUInt16LittleEndian(meshPayload.AsSpan(134, 2), 0x8000);
            Encoding.ASCII.GetBytes("armor_tex").CopyTo(meshPayload.AsSpan(136, 9));

            BinaryPrimitives.WriteUInt16LittleEndian(meshPayload.AsSpan(152, 2), 0x0054);
            BinaryPrimitives.WriteUInt16LittleEndian(meshPayload.AsSpan(154, 2), 1); // 1 tri
            BinaryPrimitives.WriteUInt16LittleEndian(meshPayload.AsSpan(156, 2), 0);
            BinaryPrimitives.WriteUInt16LittleEndian(meshPayload.AsSpan(158, 2), 1);
            BinaryPrimitives.WriteUInt16LittleEndian(meshPayload.AsSpan(160, 2), 2);
            BinaryPrimitives.WriteUInt16LittleEndian(meshPayload.AsSpan(186, 2), 0xFFFF);

            byte[] extraDat = CreateChunk(DatSectionType.SkeletonMesh, meshPayload);

            var model = EntityModelLoader.AssembleModel(primaryDat, new[] { (ReadOnlyMemory<byte>)extraDat }, "test_assembled");

            Assert.NotNull(model);
            Assert.Equal("test_assembled", model.Name);
            Assert.NotNull(model.Skeleton);
            Assert.Single(model.MeshGroups);

            var mg = model.MeshGroups[0];
            Assert.Equal("armor_tex", mg.TextureName);
            Assert.Equal(3, mg.Vertices.Length);
            Assert.Equal(3, mg.Indices.Length);
            Assert.Equal(1, mg.TriangleCount);
        }

        [Fact]
        public void EntityModelLoader_AssembleCharacter_LoadsBaseAndParts()
        {
            var loadedPaths = new List<string>();
            var loadedFids = new List<int>();

            byte[] dummyDat = CreateChunk(DatSectionType.Skeleton, new byte[34]);

            byte[]? PathResolver(string path)
            {
                loadedPaths.Add(path);
                return dummyDat;
            }

            byte[]? FidResolver(int fid)
            {
                loadedFids.Add(fid);
                return dummyDat;
            }

            ushort[] grap = new ushort[9];
            grap[0] = 0; // face 0
            grap[1] = 0x1000 | 1; // head model 1
            grap[2] = 0x2000 | 2; // body model 2

            var model = EntityModelLoader.AssembleCharacter(
                CharacterRace.HumeMale,
                0,
                grap,
                PathResolver,
                FidResolver);

            Assert.NotNull(model);
            // Verify base skeleton path was requested
            Assert.Contains(Path.Combine("ROM", "27", "82.DAT"), loadedPaths);
            // Face 0 for HumeMale is 7080
            Assert.Contains(7080, loadedFids);
            // Head 1 for HumeMale is 7112 + 1 = 7113
            Assert.Contains(7113, loadedFids);
            // Body 2 for HumeMale is 7368 + 2 = 7370
            Assert.Contains(7370, loadedFids);
        }
    }
}
