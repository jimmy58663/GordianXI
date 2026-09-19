// tests/Gordian.Core.Tests/Resources/EntityModelLoaderTests.cs
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Numerics;
using System.Text;
using Gordian.Core.Resources;
using Gordian.Core.Resources.Containers;
using Gordian.Core.Resources.Graphics;
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
            // Hands 0 for HumeMale is 7624
            Assert.Contains(7624, loadedFids);
            // Legs 0 for HumeMale is 7880
            Assert.Contains(7880, loadedFids);
            // Feet 0 for HumeMale is 8136
            Assert.Contains(8136, loadedFids);
        }

        [Fact]
        public void SkeletonPoseEvaluator_ComputeBindPose_WithParentOverrides_AdoptsTargetParentTransform()
        {
            // Create a skeleton with:
            // Joint 0: Root at (0, 0, 0)
            // Joint 1: Hand at (0, 1.5f, 0)
            // Joint 2: Weapon mount initially at (0, 0, 0)
            var joints = new List<SkeletonJoint>
            {
                new SkeletonJoint(-1, Quaternion.Identity, Vector3.Zero),
                new SkeletonJoint(0, Quaternion.Identity, new Vector3(0, 1.5f, 0)),
                new SkeletonJoint(0, Quaternion.Identity, Vector3.Zero)
            };
            var skeleton = new Skeleton(joints);

            // 1. Without overrides: Joint 2 remains at (0, 0, 0)
            var defaultPose = SkeletonPoseEvaluator.ComputeBindPose(skeleton);
            Assert.Equal(Vector3.Zero, defaultPose.Translations[2]);

            // 2. With override: Joint 2 re-parented to Joint 1
            var overrides = new Dictionary<int, int> { [2] = 1 };
            var overriddenPose = SkeletonPoseEvaluator.ComputeBindPose(skeleton, overrides);
            Assert.Equal(new Vector3(0, 1.5f, 0), overriddenPose.Translations[2]);
            Assert.Equal(overriddenPose.Rotations[1], overriddenPose.Rotations[2]);
        }

        [Fact]
        public void EntityModelLoader_ResolveWeaponParentOverrides_ResolvesGripToHandSocket()
        {
            var joints = new List<SkeletonJoint>();
            for (int i = 0; i < 94; i++)
            {
                joints.Add(new SkeletonJoint(i == 0 ? -1 : 0, Quaternion.Identity, Vector3.Zero));
            }

            var refs = new List<JointReference>();
            for (int i = 0; i < 128; i++)
            {
                if (i == 113)
                {
                    refs.Add(new JointReference(4, Vector3.Zero)); // Grip -> Joint 4
                }
                else if (i == 127)
                {
                    refs.Add(new JointReference(68, Vector3.Zero)); // Right Hand -> Joint 68
                }
                else if (i == 126)
                {
                    refs.Add(new JointReference(85, Vector3.Zero)); // Left Hand -> Joint 85
                }
                else
                {
                    refs.Add(new JointReference(0, Vector3.Zero));
                }
            }

            var skeleton = new Skeleton(joints, refs);

            // Create weapon DAT with Info chunk (0x45) where byte 6 = 113 (standardJointIndex)
            byte[] infoPayload = new byte[16];
            infoPayload[3] = 1;   // weapon animation type
            infoPayload[6] = 113; // standardJointIndex -> ref 113

            byte[] weaponDat = CreateChunk(DatSectionType.Info, infoPayload);

            var weaponDats = new List<(CharacterSlot Slot, ReadOnlyMemory<byte> Dat)>
            {
                (CharacterSlot.Main, (ReadOnlyMemory<byte>)weaponDat)
            };

            var overrides = EntityModelLoader.ResolveWeaponParentOverrides(skeleton, weaponDats);

            Assert.NotNull(overrides);
            Assert.True(overrides.ContainsKey(4));
            Assert.Equal(68, overrides[4]); // Joint 4 re-parented onto Right Hand (Joint 68)
        }

        [Fact]
        public void EntityModelLoader_AssembleModel_MatchesCompoundTextureNamesWithSpaces()
        {
            // 1. Primary DAT with Skeleton
            byte[] skelPayload = new byte[4 + 30];
            skelPayload[2] = 1;
            skelPayload[4] = 0;
            BinaryPrimitives.WriteSingleLittleEndian(skelPayload.AsSpan(30, 4), 1f);
            byte[] primaryDat = CreateChunk(DatSectionType.Skeleton, skelPayload);

            // 2. Texture payload (0x01, name "tim     em_h81_1", 2x2 32-bit RGBA)
            byte[] texPayload = new byte[64 + 16];
            texPayload[0] = 0x01;
            Encoding.ASCII.GetBytes("tim     em_h81_1").CopyTo(texPayload.AsSpan(1, 16));
            int tp = 21;
            BinaryPrimitives.WriteInt32LittleEndian(texPayload.AsSpan(tp, 4), 2); tp += 4;
            BinaryPrimitives.WriteInt32LittleEndian(texPayload.AsSpan(tp, 4), 2); tp += 4;
            tp += 2;
            BinaryPrimitives.WriteUInt16LittleEndian(texPayload.AsSpan(tp, 2), 32); tp += 2;
            byte[] texChunk = CreateChunk(DatSectionType.Texture, texPayload);

            // 3. Mesh payload with 0x8000 texture tag "tim     em_h81_1"
            byte[] meshPayload = new byte[188];
            BinaryPrimitives.WriteInt32LittleEndian(meshPayload.AsSpan(6, 4), 134 / 2);
            BinaryPrimitives.WriteInt32LittleEndian(meshPayload.AsSpan(18, 4), 42 / 2);
            BinaryPrimitives.WriteUInt16LittleEndian(meshPayload.AsSpan(22, 2), 2);
            BinaryPrimitives.WriteInt32LittleEndian(meshPayload.AsSpan(24, 4), 46 / 2);
            BinaryPrimitives.WriteInt32LittleEndian(meshPayload.AsSpan(30, 4), 54 / 2);
            BinaryPrimitives.WriteUInt16LittleEndian(meshPayload.AsSpan(42, 2), 3);

            BinaryPrimitives.WriteUInt16LittleEndian(meshPayload.AsSpan(134, 2), 0x8000);
            Encoding.ASCII.GetBytes("tim     em_h81_1").CopyTo(meshPayload.AsSpan(136, 16));
            BinaryPrimitives.WriteUInt16LittleEndian(meshPayload.AsSpan(152, 2), 0x0054);
            BinaryPrimitives.WriteUInt16LittleEndian(meshPayload.AsSpan(154, 2), 1);
            BinaryPrimitives.WriteUInt16LittleEndian(meshPayload.AsSpan(186, 2), 0xFFFF);
            byte[] meshChunk = CreateChunk(DatSectionType.SkeletonMesh, meshPayload);

            // Combined part DAT containing both texture chunk and mesh chunk
            byte[] partDat = new byte[texChunk.Length + meshChunk.Length];
            texChunk.CopyTo(partDat, 0);
            meshChunk.CopyTo(partDat, texChunk.Length);

            var model = EntityModelLoader.AssembleModel(primaryDat, new[] { (ReadOnlyMemory<byte>)partDat }, "test_elvaan");

            Assert.NotNull(model);
            Assert.Single(model.MeshGroups);
            Assert.Equal("tim     em_h81_1", model.MeshGroups[0].TextureName);
            // Verify both full name and short name are indexed
            Assert.True(model.Textures.ContainsKey("tim     em_h81_1"));
            Assert.True(model.Textures.ContainsKey("em_h81_1"));
        }

        [Fact]
        public void EntityModelLoader_LoadMonsterModel_ResolvesRetailFileIds()
        {
            // 1. Primary DAT with Skeleton
            byte[] skelPayload = new byte[4 + 30];
            skelPayload[2] = 1;
            skelPayload[4] = 0;
            BinaryPrimitives.WriteSingleLittleEndian(skelPayload.AsSpan(18, 4), 0f);
            BinaryPrimitives.WriteSingleLittleEndian(skelPayload.AsSpan(22, 4), 1f);
            BinaryPrimitives.WriteSingleLittleEndian(skelPayload.AsSpan(26, 4), 0f);
            BinaryPrimitives.WriteSingleLittleEndian(skelPayload.AsSpan(30, 4), 1f);
            byte[] primaryDat = CreateChunk(DatSectionType.Skeleton, skelPayload);

            var requestedFileIds = new List<int>();
            byte[]? MockResolver(int fid)
            {
                requestedFileIds.Add(fid);
                return primaryDat;
            }

            // Test Vanilla mob (Mandragora, modelId 300 -> fileId 1600)
            var mandy = EntityModelLoader.LoadMonsterModel(300, MockResolver);
            Assert.NotNull(mandy);
            Assert.Equal("Monster_300", mandy.Name);
            Assert.Equal(1600, requestedFileIds[0]);

            // Test Expansion mob (Lycopodium, modelId 2247 -> fileId 52542)
            var lyco = EntityModelLoader.LoadMonsterModel(2247, MockResolver);
            Assert.NotNull(lyco);
            Assert.Equal("Monster_2247", lyco.Name);
            Assert.Equal(52542, requestedFileIds[1]);

            // Test Zero model ID returns null
            var zero = EntityModelLoader.LoadMonsterModel(0, MockResolver);
            Assert.Null(zero);
        }
    }
}
