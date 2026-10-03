// tests/Gordian.Core.Tests/Resources/EntityModelLoaderTests.cs
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
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
            Assert.Single(model.AnimatedMeshGroups);

            var mg = model.AnimatedMeshGroups[0];
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

            // Locomotion and battle motion packs are enabled by default and loaded via path (and fileId fallback).
            Assert.Contains(Path.Combine("ROM", "27", "83.DAT"), loadedPaths);
            Assert.Contains(Path.Combine("ROM", "27", "85.DAT"), loadedPaths);
            Assert.Contains(Path.Combine("ROM", "32", "13.DAT"), loadedPaths);
        }

        [Fact]
        public void EntityModelLoader_AssembleCharacter_HonorsDisableMotionPacksSwitch()
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

            EntityModelLoader.EnableSpeculativeMotionPacks = false;
            try
            {
                var model = EntityModelLoader.AssembleCharacter(
                    CharacterRace.HumeMale,
                    0,
                    new ushort[9],
                    PathResolver,
                    FidResolver);

                Assert.NotNull(model);
                Assert.Contains(Path.Combine("ROM", "27", "82.DAT"), loadedPaths);
                Assert.DoesNotContain(Path.Combine("ROM", "27", "83.DAT"), loadedPaths);
                Assert.DoesNotContain(Path.Combine("ROM", "27", "85.DAT"), loadedPaths);
                Assert.DoesNotContain(Path.Combine("ROM", "32", "13.DAT"), loadedPaths);
            }
            finally
            {
                EntityModelLoader.EnableSpeculativeMotionPacks = true;
            }
        }

        [Fact]
        public void EntityModelLoader_AssembleCharacter_LoadsWeaponSpecificBattlePack()
        {
            var loadedPaths = new List<string>();
            var loadedFids = new List<int>();

            byte[] dummyDat = CreateChunk(DatSectionType.Skeleton, new byte[34]);

            // Weapon DAT with Info section (0x45): animTypeByte at offset 3 = 1 (Dagger)
            byte[] infoPayload = new byte[16];
            infoPayload[3] = 1; // 1 = Dagger
            byte[] daggerDat = CreateChunk(DatSectionType.Info, infoPayload);

            byte[]? PathResolver(string path)
            {
                loadedPaths.Add(path);
                return dummyDat;
            }

            byte[]? FidResolver(int fid)
            {
                loadedFids.Add(fid);
                // Main weapon for TaruMale is 21096 + modelId
                if (fid == 21096 + 5)
                {
                    return daggerDat;
                }
                return dummyDat;
            }

            ushort[] grap = new ushort[9];
            grap[0] = 0; // face 0
            grap[6] = 5; // Main weapon model ID 5

            var model = EntityModelLoader.AssembleCharacter(
                CharacterRace.TaruMale,
                0,
                grap,
                PathResolver,
                FidResolver);

            Assert.NotNull(model);
            // Verify dagger battle pack path was requested for TaruMale (ROM/51/20.DAT, NOT H2H ROM/51/19.DAT)
            Assert.Contains(Path.Combine("ROM", "51", "20.DAT"), loadedPaths);
            Assert.DoesNotContain(Path.Combine("ROM", "51", "19.DAT"), loadedPaths);
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
        public void EntityModelLoader_ResolveWeaponParentOverrides_ExcludesShieldsFromHandOverride()
        {
            var joints = new List<SkeletonJoint>();
            for (int i = 0; i < 94; i++)
            {
                joints.Add(new SkeletonJoint(i == 0 ? -1 : 0, Quaternion.Identity, Vector3.Zero));
            }

            var refs = new List<JointReference>();
            for (int i = 0; i < 128; i++)
            {
                if (i == 125)
                {
                    refs.Add(new JointReference(24, Vector3.Zero)); // Shield back mount -> Joint 24
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

            // Shield DAT with AnimType=255 and stdJoint=125
            byte[] shieldInfoPayload = new byte[16];
            shieldInfoPayload[3] = 255; // Shield anim type
            shieldInfoPayload[6] = 125; // stdJoint 125 (shield mount)

            byte[] shieldDat = CreateChunk(DatSectionType.Info, shieldInfoPayload);

            var weaponDats = new List<(CharacterSlot Slot, ReadOnlyMemory<byte> Dat)>
            {
                (CharacterSlot.Sub, (ReadOnlyMemory<byte>)shieldDat)
            };

            var overrides = EntityModelLoader.ResolveWeaponParentOverrides(skeleton, weaponDats);

            Assert.NotNull(overrides);
            Assert.Empty(overrides); // Shields must NOT be re-parented to the hand
        }

        [Fact]
        public void EntityModelLoader_ResolveWeaponParentOverrides_ResolvesDualWieldSubWeaponToLeftHand()
        {
            var joints = new List<SkeletonJoint>();
            for (int i = 0; i < 94; i++)
            {
                joints.Add(new SkeletonJoint(i == 0 ? -1 : 0, Quaternion.Identity, Vector3.Zero));
            }

            var refs = new List<JointReference>();
            for (int i = 0; i < 128; i++)
            {
                if (i == 124)
                {
                    refs.Add(new JointReference(23, Vector3.Zero)); // Off-hand grip -> Joint 23
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

            // Off-hand weapon DAT with weapon AnimType=7 and stdJoint=124
            byte[] offhandPayload = new byte[16];
            offhandPayload[3] = 7;   // weapon animation type
            offhandPayload[6] = 124; // offhand grip -> ref 124

            byte[] offhandDat = CreateChunk(DatSectionType.Info, offhandPayload);

            var weaponDats = new List<(CharacterSlot Slot, ReadOnlyMemory<byte> Dat)>
            {
                (CharacterSlot.Sub, (ReadOnlyMemory<byte>)offhandDat)
            };

            var overrides = EntityModelLoader.ResolveWeaponParentOverrides(skeleton, weaponDats);

            Assert.NotNull(overrides);
            Assert.True(overrides.ContainsKey(23));
            Assert.Equal(85, overrides[23]); // Joint 23 re-parented onto Left Hand (Joint 85)
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
            Assert.Single(model.AnimatedMeshGroups);
            Assert.Equal("tim     em_h81_1", model.AnimatedMeshGroups[0].TextureName);
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

        [Fact]
        public void EntityModelLoader_MergeLocomotionCategories_PreservesLowerBodyAndCreatesCombatClips()
        {
            var model = new EntityModel { Name = "test_player" };

            // 1. Base locomotion (e.g. wlk0, run0): contains lower-body tracks (joints 1, 2)
            var lowerTrack1 = new BoneAnimationTrack { JointIndex = 1 };
            var lowerTrack2 = new BoneAnimationTrack { JointIndex = 2 };
            var baseWlk0 = new AnimationClip
            {
                Name = "wlk0",
                NumFrames = 30,
                KeyFrameDuration = 0.033f,
                Tracks = new Dictionary<int, BoneAnimationTrack> { [1] = lowerTrack1, [2] = lowerTrack2 }
            };

            var baseRun0 = new AnimationClip
            {
                Name = "run0",
                NumFrames = 20,
                KeyFrameDuration = 0.033f,
                Tracks = new Dictionary<int, BoneAnimationTrack> { [1] = lowerTrack1, [2] = lowerTrack2 }
            };

            // 2. Base out-of-combat upper body (e.g. wlk1, run1): contains upper-body tracks (joints 10, 11)
            var outOfCombatUpperTrack10 = new BoneAnimationTrack { JointIndex = 10 };
            var outOfCombatUpperTrack11 = new BoneAnimationTrack { JointIndex = 11 };
            var baseWlk1 = new AnimationClip
            {
                Name = "wlk1",
                NumFrames = 30,
                KeyFrameDuration = 0.033f,
                Tracks = new Dictionary<int, BoneAnimationTrack> { [10] = outOfCombatUpperTrack10, [11] = outOfCombatUpperTrack11 }
            };

            var baseRun1 = new AnimationClip
            {
                Name = "run1",
                NumFrames = 20,
                KeyFrameDuration = 0.033f,
                Tracks = new Dictionary<int, BoneAnimationTrack> { [10] = outOfCombatUpperTrack10, [11] = outOfCombatUpperTrack11 }
            };

            // 3. Battle pack combat upper body (e.g. wlk1, run1 from weapon pack): holds weapon (joints 10, 11)
            var combatUpperTrack10 = new BoneAnimationTrack { JointIndex = 10 };
            var combatUpperTrack11 = new BoneAnimationTrack { JointIndex = 11 };
            var battleWlk1 = new AnimationClip
            {
                Name = "wlk1",
                NumFrames = 30,
                KeyFrameDuration = 0.033f,
                Tracks = new Dictionary<int, BoneAnimationTrack> { [10] = combatUpperTrack10, [11] = combatUpperTrack11 }
            };

            var battleRun1 = new AnimationClip
            {
                Name = "run1",
                NumFrames = 20,
                KeyFrameDuration = 0.033f,
                Tracks = new Dictionary<int, BoneAnimationTrack> { [10] = combatUpperTrack10, [11] = combatUpperTrack11 }
            };

            var baseClips = new List<AnimationClip> { baseWlk0, baseRun0 };
            var overlaySources = new List<List<AnimationClip>> { new List<AnimationClip> { baseWlk1, baseRun1 } };
            var battleAnims = new List<AnimationClip> { battleWlk1, battleRun1 };

            foreach (var clip in baseClips)
            {
                model.Animations[clip.Name] = clip;
            }

            EntityModelLoader.MergeLocomotionCategories(model, overlaySources, battleAnims);

            // 1. Resting clips (wlk, run) exist with out-of-combat upper body + lower body
            Assert.True(model.Animations.ContainsKey("wlk"));
            Assert.True(model.Animations.ContainsKey("run"));
            Assert.Same(lowerTrack1, model.Animations["wlk"].Tracks[1]);
            Assert.Same(outOfCombatUpperTrack10, model.Animations["wlk"].Tracks[10]);

            // 2. Combat clips (cwlk, crun) exist with combat upper body + lower body
            Assert.True(model.Animations.ContainsKey("cwlk"));
            Assert.True(model.Animations.ContainsKey("crun"));
            Assert.Same(lowerTrack1, model.Animations["cwlk"].Tracks[1]);
            Assert.Same(lowerTrack2, model.Animations["cwlk"].Tracks[2]);
            Assert.Same(combatUpperTrack10, model.Animations["cwlk"].Tracks[10]);
            Assert.Same(combatUpperTrack11, model.Animations["cwlk"].Tracks[11]);
        }
    
        private static AnimationClip RegionClip(string name, int frames, params int[] joints)
        {
            var tracks = new Dictionary<int, BoneAnimationTrack>();
            foreach (int j in joints) tracks[j] = new BoneAnimationTrack { JointIndex = j };
            return new AnimationClip { Name = name, NumFrames = frames, KeyFrameDuration = 1f, Tracks = tracks };
        }

        /// <summary>
        /// #163: a fixed NPC model stores a motion as body-region parts (wlk0 legs, wlk1 upper body, wlk2 waist); the stem
        /// plays them together. Same-stem clips that move the same joints (a monster's swings) stay separate motions.
        /// </summary>
        [Fact]
        public void BodyRegionParts_AreJoinedUnderTheirStem_SwingsAreNot()
        {
            var model = new EntityModel();
            var clips = new List<AnimationClip>
            {
                RegionClip("run2", 13, 20, 21), RegionClip("run1", 13, 5, 6, 7, 8), RegionClip("run0", 13, 0, 1),
                RegionClip("at0", 30, 0, 1, 2), RegionClip("at1", 40, 0, 1, 2),
            };
            foreach (var clip in clips)
            {
                model.Animations[clip.Name] = clip;
                model.Animations.TryAdd(clip.Name[..^1], clip);
            }
            EntityModelLoader.MergeBodyRegionParts(model, clips);

            Assert.Equal(new[] { 0, 1, 5, 6, 7, 8, 20, 21 }, model.Animations["run"].Tracks.Keys.OrderBy(k => k).ToArray());
            Assert.Equal(13, model.Animations["run"].NumFrames);
            Assert.Same(clips[3], model.Animations["at"]);
            Assert.Same(clips[4], model.Animations["at1"]);
        }

        /// <summary>
        /// #197: a model that stores every part twice (the Tarutaru of Lower Jeuno event 70, models 1443-1445) still joins
        /// them, from the last copy of each part, rather than leaving the stem on the first part in the file (the waist).
        /// </summary>
        [Fact]
        public void BodyRegionParts_StoredTwice_StillJoin()
        {
            var model = new EntityModel();
            var clips = new List<AnimationClip>
            {
                RegionClip("run2", 11, 20, 21), RegionClip("run0", 11, 0, 1),
                RegionClip("run0", 11, 0, 1), RegionClip("run1", 11, 5, 6, 7), RegionClip("run2", 11, 20, 21),
            };
            foreach (var clip in clips)
            {
                model.Animations[clip.Name] = clip;
                model.Animations.TryAdd(clip.Name[..^1], clip);
            }
            EntityModelLoader.MergeBodyRegionParts(model, clips);

            Assert.Equal(new[] { 0, 1, 5, 6, 7, 20, 21 }, model.Animations["run"].Tracks.Keys.OrderBy(k => k).ToArray());
            Assert.Same(clips[4].Tracks[20], model.Animations["run"].Tracks[20]);
        }

        /// <summary>
        /// The retail models of #163: Curilla (model 69) and Prince Trion (model 64) walk and stand with every joint
        /// animated (99), not the legs or waist part alone. Skipped without the game install.
        /// </summary>
        /// <summary>
        /// #163: a fixed NPC model's init routine hides a weapon slot (op 0x75, followed through a blocking link), so the
        /// slot's wepN meshes are left out; the last command for a slot wins.
        /// </summary>
        [Fact]
        public void InitRoutine_HidesWeaponSlots()
        {
            static MotionRoutineCommand ShowHide(int slot, bool hide) => new(0x75, 0, 1, string.Empty, 0, 0, 1, 1f, -1, 0, slot, hide);
            var routines = new Dictionary<string, RawMotionRoutine>
            {
                ["init"] = new() { Name = "init", Commands = new[] { new MotionRoutineCommand(0x3B, 0, 0, "wof4", 0, 0, 1, 1f, -1, 0), ShowHide(2, true) } },
                ["wof4"] = new() { Name = "wof4", Commands = new[] { ShowHide(4, true), ShowHide(2, false) } },
            };
            Assert.Equal(new[] { 2, 4 }, EntityModelLoader.InitialHiddenWeaponSlots(routines).OrderBy(s => s).ToArray());
            Assert.Equal(4, EntityModelLoader.WeaponSlotOf("wep4"));
            Assert.Null(EntityModelLoader.WeaponSlotOf("hh_b"));
        }

        /// <summary>
        /// Every decoded texture carries where it came from (#163): two fixed NPC models with a texture of the same name
        /// keep different sources (cached apart), and the same gear section loaded for two characters keeps one (shared).
        /// Skipped without the game install.
        /// </summary>
        [Fact]
        public void Textures_CarryTheirSource()
        {
            const string dir = @"G:\Program Files (x86)\PlayOnline\SquareEnix\FINAL FANTASY XI";
            if (!System.IO.Directory.Exists(dir)) return;
            var rm = new ResourceManager(dir);
            rm.InitializeFileTable();
            var sources = new Dictionary<string, HashSet<string>>();
            for (uint id = 1; id < 200; id++)
            {
                var model = EntityModelLoader.LoadMonsterModel(id, rm.LoadDatBytesByFileId);
                if (model == null) continue;
                foreach (var texture in model.Textures.Values)
                {
                    Assert.StartsWith($"file{CharacterEquipmentResolver.GetMonsterFileId(id)}@", texture.Source);
                    if (!sources.TryGetValue(texture.Name, out var set)) sources[texture.Name] = set = new HashSet<string>();
                    set.Add(texture.Source);
                }
            }
            Assert.Contains(sources.Values, set => set.Count > 1); // names repeat across models, sources do not

            ushort[] grap = { 0, 0x1001, 0x2001, 0x3001, 0x4001, 0x5001, 0, 0, 0 };
            var a = EntityModelLoader.AssembleCharacter(CharacterRace.ElvaanMale, 2, grap, rm.LoadDatBytes, rm.LoadDatBytesByFileId)!;
            var b = EntityModelLoader.AssembleCharacter(CharacterRace.ElvaanMale, 3, grap, rm.LoadDatBytes, rm.LoadDatBytesByFileId)!;
            var bodyA = a.Textures.Values.Where(t => t.Source.Length > 0).Select(t => t.Source).ToHashSet();
            var bodyB = b.Textures.Values.Where(t => t.Source.Length > 0).Select(t => t.Source).ToHashSet();
            Assert.NotEmpty(bodyA.Intersect(bodyB)); // the same gear DATs: shared sources
            Assert.NotEqual(bodyA, bodyB);            // different faces: their own
        }

        /// <summary>
        /// The event motion bank Curilla talks with in the Southern San d'Oria intro (bank 140, file 32244) stores its
        /// gestures as body-region parts; a gesture plays them all, so her arms and sheathed sword keep their place (#163).
        /// Skipped without the game install.
        /// </summary>
        [Fact]
        public void EventMotionBank_GesturesMoveTheWholeSkeleton()
        {
            const string dir = @"G:\Program Files (x86)\PlayOnline\SquareEnix\FINAL FANTASY XI";
            if (!System.IO.Directory.Exists(dir)) return;
            var rm = new ResourceManager(dir);
            rm.InitializeFileTable();
            var bank = Gordian.Core.Animation.EventMotionBank.Parse(rm.LoadDatBytesByFileId(32244)!, 32244)!;
            var clip = bank.Clips[bank.Routines["tlk0"].Segments[0].ClipName];
            Assert.Equal(99, clip.Tracks.Count);
            Assert.True(clip.Tracks.ContainsKey(84) && clip.Tracks.ContainsKey(96)); // the sword's joints
        }

        /// <summary>Prince Trion's model 64: init runs wof4, which hides its wep4 sword and scabbard. Skipped without the game install.</summary>
        [Fact]
        public void TrionModel_InitHidesItsWeapon()
        {
            const string dir = @"G:\Program Files (x86)\PlayOnline\SquareEnix\FINAL FANTASY XI";
            if (!System.IO.Directory.Exists(dir)) return;
            var rm = new ResourceManager(dir);
            rm.InitializeFileTable();
            var raw = EntityModelLoader.ParseDatContainer(rm.LoadDatBytesByFileId(CharacterEquipmentResolver.GetMonsterFileId(64))!);
            var routines = raw.Routines.ToDictionary(r => r.Name);
            Assert.Equal(new[] { 4 }, EntityModelLoader.InitialHiddenWeaponSlots(routines).ToArray());
            Assert.Equal(2, raw.Meshes.Count(m => m.SectionName == "wep4"));
            var curilla = EntityModelLoader.ParseDatContainer(rm.LoadDatBytesByFileId(CharacterEquipmentResolver.GetMonsterFileId(69))!);
            Assert.Empty(EntityModelLoader.InitialHiddenWeaponSlots(curilla.Routines.ToDictionary(r => r.Name))); // her sword stays
        }

        [Theory]
        [InlineData(69u)]
        [InlineData(64u)]
        public void FixedNpcModels_WalkWithTheirWholeSkeleton(uint modelId)
        {
            const string dir = @"G:\Program Files (x86)\PlayOnline\SquareEnix\FINAL FANTASY XI";
            if (!System.IO.Directory.Exists(dir)) return;
            var rm = new ResourceManager(dir);
            rm.InitializeFileTable();
            var model = EntityModelLoader.LoadMonsterModel(modelId, rm.LoadDatBytesByFileId)!;
            Assert.Equal(model.Skeleton!.Count, model.Animations["wlk"].Tracks.Count);
            Assert.Equal(model.Skeleton.Count, model.Animations["idl"].Tracks.Count);
        }

        /// <summary>
        /// #75: where a stem's parts overlap (alternative variants, not body regions) the stem plays part 0, whatever
        /// the file order; a stem never replaces a clip of that exact name, and disjoint parts still join.
        /// </summary>
        [Fact]
        public void Stems_NamePartZero_WhenThePartsOverlap()
        {
            var model = new EntityModel();
            var realAt0 = RegionClip("at0", 20, 0, 1, 2);
            var clips = new List<AnimationClip>
            {
                RegionClip("idl1", 16, 0, 1, 2), RegionClip("idl0", 16, 0, 1, 2), // ROM/97/61 (model 11) order
                RegionClip("at00", 20, 0), RegionClip("at01", 20, 1), realAt0,
                RegionClip("wlk2", 8, 9), RegionClip("wlk0", 8, 0, 1), RegionClip("wlk1", 8, 5),
            };
            EntityModelLoader.AddClipsWithStems(model, clips);
            Assert.Same(clips[1], model.Animations["idl"]);
            Assert.Same(clips[0], model.Animations["idl1"]);
            Assert.Same(realAt0, model.Animations["at0"]);
            Assert.Same(clips[6], model.Animations["wlk"]);

            EntityModelLoader.MergeBodyRegionParts(model, clips);
            Assert.Same(clips[1], model.Animations["idl"]);
            Assert.Equal(new[] { 0, 1, 5, 9 }, model.Animations["wlk"].Tracks.Keys.OrderBy(k => k).ToArray());
        }

        /// <summary>
        /// #75, retail: Trusts and notorious monsters whose idle is stored as body-region parts play every part. Shantotto
        /// (3000: idl0 14 joints, idl1 69, idl2 10), Naji (3001), Alexander (1834: idl0 74, idl1 7) and a Doppelganger
        /// (547) drive every part through the idle stance, not part 0 alone with the upper body in its bind pose.
        /// Skipped without the game install.
        /// </summary>
        [Theory]
        [InlineData(3000u)]
        [InlineData(3001u)]
        [InlineData(1834u)]
        [InlineData(547u)]
        public void SplitIdle_DrivesEveryPart(uint modelId)
        {
            const string dir = @"G:\Program Files (x86)\PlayOnline\SquareEnix\FINAL FANTASY XI";
            if (!System.IO.Directory.Exists(dir)) return;
            var rm = new ResourceManager(dir);
            rm.InitializeFileTable();
            var raw = EntityModelLoader.ParseDatContainer(rm.LoadDatBytesByFileId(CharacterEquipmentResolver.GetMonsterFileId(modelId))!);
            var parts = raw.Animations.Where(a => a.Name is "idl0" or "idl1" or "idl2").ToList();
            Assert.True(parts.Count >= 2, $"model {modelId} has {parts.Count} idle parts");
            var union = parts.SelectMany(p => p.Tracks.Keys).ToHashSet();
            Assert.Equal(parts.Sum(p => p.Tracks.Count), union.Count); // disjoint body regions

            var model = EntityModelLoader.LoadMonsterModel(modelId, rm.LoadDatBytesByFileId)!;
            Assert.True(union.SetEquals(model.Animations["idl"].Tracks.Keys));
            var stance = Gordian.Core.Animation.NpcStanceResolver.ResolveTargetClip(model, Gordian.Core.Animation.AnimationCategory.Idle)!;
            Assert.True(union.SetEquals(stance.Tracks.Keys));
            if (modelId is 3000 or 3001) Assert.Equal(model.Skeleton!.Count, union.Count);
        }

        /// <summary>#75, retail: the elemental model 11 (ROM/97/61) stores two overlapping idles, idl1 first; it idles on part 0.</summary>
        [Fact]
        public void ElementalModel11_IdleIsPartZero()
        {
            const string dir = @"G:\Program Files (x86)\PlayOnline\SquareEnix\FINAL FANTASY XI";
            if (!System.IO.Directory.Exists(dir)) return;
            var rm = new ResourceManager(dir);
            rm.InitializeFileTable();
            var model = EntityModelLoader.LoadMonsterModel(11, rm.LoadDatBytesByFileId)!;
            Assert.Same(model.Animations["idl0"], model.Animations["idl"]);
        }

        /// <summary>
        /// #76, retail CPU-skin check: Moblin model 1735 (ROM/258/91) carries a bow bound to joints 104 and 107, which its
        /// idle, walk, run and battle stance hold at scale 0; samurai model 1182 (ROM/151/126) hides a second blade the
        /// same way. Skinned with the decoded scale every vertex bound only to such joints collapses onto its joint; at
        /// scale 1 (the old decoder) the part has its full size (the bow lay at the Moblin's feet). Skipped without the
        /// game install.
        /// </summary>
        [Theory]
        [InlineData(1735u)]
        [InlineData(1182u)]
        public void ZeroScaleStance_HidesThePart(uint modelId)
        {
            const string dir = @"G:\Program Files (x86)\PlayOnline\SquareEnix\FINAL FANTASY XI";
            if (!System.IO.Directory.Exists(dir)) return;
            var rm = new ResourceManager(dir);
            rm.InitializeFileTable();
            var raw = EntityModelLoader.ParseDatContainer(rm.LoadDatBytesByFileId(CharacterEquipmentResolver.GetMonsterFileId(modelId))!);
            var model = EntityModelLoader.LoadMonsterModel(modelId, rm.LoadDatBytesByFileId)!;
            var pose = SkeletonPoseEvaluator.EvaluatePose(raw.Skeleton!, model.Animations["idl"], 0f, true);
            var unit = new SkeletonPoseEvaluator.EvaluatedPose(pose.Rotations, pose.Translations,
                Enumerable.Repeat(Vector3.One, pose.Scales.Length).ToArray());
            bool Hidden(int j) => j >= 0 && j < pose.Scales.Length && pose.Scales[j].Length() <= 1e-4f;

            int hiddenVertices = 0;
            Vector3 min = new(float.MaxValue), max = new(float.MinValue);
            foreach (var mesh in raw.Meshes)
            {
                foreach (var v in mesh.Vertices)
                {
                    if (!Hidden(v.Joint0) || (v.Joint1 >= 0 && !Hidden(v.Joint1))) continue;
                    hiddenVertices++;
                    var (p, n) = SkeletonPoseEvaluator.SkinVertex(v, pose);
                    Assert.True(float.IsFinite(p.X) && float.IsFinite(p.Y) && float.IsFinite(p.Z) && float.IsFinite(n.X));
                    if (v.Joint1 < 0) Assert.True(Vector3.Distance(pose.Translations[v.Joint0], p) < 1e-3f);
                    var (q, _) = SkeletonPoseEvaluator.SkinVertex(v, unit);
                    min = Vector3.Min(min, q);
                    max = Vector3.Max(max, q);
                }
            }
            Assert.True(hiddenVertices > 20, $"model {modelId}: {hiddenVertices} hidden vertices");
            Assert.True((max - min).Length() > 0.2f, $"at scale 1 the hidden part spans {(max - min).Length()}");
        }
    }
}
