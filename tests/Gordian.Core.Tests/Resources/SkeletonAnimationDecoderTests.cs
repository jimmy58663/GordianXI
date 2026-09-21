// tests/Gordian.Core.Tests/Resources/SkeletonAnimationDecoderTests.cs
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Numerics;
using Gordian.Core.Resources;
using Gordian.Core.Resources.Graphics;
using Gordian.Core.Resources.Models;
using Gordian.Core.Resources.Tables;
using Xunit;

namespace Gordian.Core.Tests.Resources
{
    public class SkeletonAnimationDecoderTests
    {
        // Builds a single-joint, 2-frame clip payload:
        // rotation constant identity, translation Y/Z constant (2, 3), translation X variable (0 -> 5),
        // scale constant (unused/undecoded, but still validity-checked).
        private static byte[] BuildSingleJointClipPayload()
        {
            const int boneTableStart = 10;
            const int entryStart = boneTableStart;
            const int variableDataOffset = entryStart + 84; // right after the one bone entry
            const int numFrames = 2;
            byte[] payload = new byte[variableDataOffset + (numFrames * 4)];

            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(2, 2), 1);   // numJoints
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(4, 2), numFrames);
            BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(6, 4), 1.0f); // keyFrameDuration

            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(entryStart, 4), 0); // jointIndex

            // rot_offsets[4] = all 0 (constant), rot_const[4] = identity (0,0,0,1)
            for (int i = 0; i < 4; i++)
            {
                BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(entryStart + 4 + (i * 4), 4), 0);
            }
            BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(entryStart + 20, 4), 0f);
            BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(entryStart + 24, 4), 0f);
            BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(entryStart + 28, 4), 0f);
            BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(entryStart + 32, 4), 1f);

            // trans_offsets[3]: X variable (offset in 4-byte units from boneTableStart), Y/Z constant
            int xOffsetUnits = (variableDataOffset - boneTableStart) / 4;
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(entryStart + 36, 4), xOffsetUnits);
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(entryStart + 40, 4), 0);
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(entryStart + 44, 4), 0);
            BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(entryStart + 48, 4), 0f); // unused (X is variable)
            BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(entryStart + 52, 4), 2f); // Y constant
            BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(entryStart + 56, 4), 3f); // Z constant

            // scale_offsets[3]: all 0 (constant) - values themselves are never read by the decoder
            for (int i = 0; i < 3; i++)
            {
                BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(entryStart + 60 + (i * 4), 4), 0);
            }

            // Variable translation-X keyframe data: frame0 = 0, frame1 = 5
            BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(variableDataOffset, 4), 0f);
            BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(variableDataOffset + 4, 4), 5f);

            return payload;
        }

        [Fact]
        public void SkeletonAnimationDecoder_DecodeClip_ParsesConstantAndVariableChannels()
        {
            byte[] payload = BuildSingleJointClipPayload();

            var clip = SkeletonAnimationDecoder.DecodeClip(payload, "wlk0");

            Assert.NotNull(clip);
            Assert.Equal("wlk0", clip!.Name);
            Assert.Equal(2, clip.NumFrames);
            Assert.Equal(1.0f, clip.KeyFrameDuration);
            Assert.Equal(1f / 30f, clip.DurationSeconds, 4);

            Assert.True(clip.Tracks.TryGetValue(0, out var track));
            Assert.Equal(Quaternion.Identity, track!.Rotations[0]);
            Assert.Equal(Quaternion.Identity, track.Rotations[1]);
            Assert.Equal(new Vector3(0f, 2f, 3f), track.Translations[0]);
            Assert.Equal(new Vector3(5f, 2f, 3f), track.Translations[1]);
        }

        [Fact]
        public void AnimationClip_TrySample_InterpolatesTranslationAndFallsBackForUntrackedJoint()
        {
            byte[] payload = BuildSingleJointClipPayload();
            var clip = SkeletonAnimationDecoder.DecodeClip(payload, "wlk0");
            Assert.NotNull(clip);

            // Halfway through the clip -> phase 0.5 -> halfway between (0,2,3) and (5,2,3)
            float halfTime = clip!.DurationSeconds * 0.5f;
            Assert.True(clip.TrySample(0, halfTime, loop: true, out var rot, out var trans));
            Assert.Equal(Quaternion.Identity, rot);
            Assert.Equal(new Vector3(2.5f, 2f, 3f), trans);

            // No track for joint 5 -> caller should fall back to skeleton bind pose.
            Assert.False(clip.TrySample(5, halfTime, loop: true, out _, out _));
        }

        [Fact]
        public void SkeletonAnimationDecoder_DecodeClip_GeneratesResetTrackOnNegativeOffset()
        {
            byte[] payload = BuildSingleJointClipPayload();
            const int entryStart = 10;

            // Rotation X offset = -1 -> negative offset signifies RESET track in FFXI protocol (pins bone to bind pose: identity rot, zero trans, unit scale)
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(entryStart + 4, 4), -1);

            var clip = SkeletonAnimationDecoder.DecodeClip(payload, "idl0");

            Assert.NotNull(clip);
            Assert.True(clip!.Tracks.TryGetValue(0, out var track));
            Assert.Equal(Quaternion.Identity, track!.Rotations[0]);
            Assert.Equal(Vector3.Zero, track.Translations[0]);
            Assert.Equal(Vector3.One, track.Scales[0]);
        }

        [Fact]
        public void SkeletonAnimationDecoder_DecodeClip_HandlesTruncatedPayload()
        {
            Assert.Null(SkeletonAnimationDecoder.DecodeClip(new byte[4], "idl0"));

            byte[] tooShortForBoneTable = new byte[12];
            BinaryPrimitives.WriteUInt16LittleEndian(tooShortForBoneTable.AsSpan(2, 2), 1);
            BinaryPrimitives.WriteUInt16LittleEndian(tooShortForBoneTable.AsSpan(4, 2), 2);
            BinaryPrimitives.WriteSingleLittleEndian(tooShortForBoneTable.AsSpan(6, 4), 1.0f);
            var clip = SkeletonAnimationDecoder.DecodeClip(tooShortForBoneTable, "idl0");

            // Header parses fine, but the (missing) bone entry is simply skipped - no crash, empty tracks.
            Assert.NotNull(clip);
            Assert.Empty(clip!.Tracks);
        }

        [Fact]
        public void SkeletonAnimationDecoder_DecodeClip_RejectsImplausibleHeaderValues()
        {
            // A mis-scanned/non-animation payload could produce wild numJoints/numFrames/keyFrameDuration
            // values; these must be rejected outright rather than decoded into a garbage clip that would
            // corrupt pose evaluation (this guards against the exact failure mode that collapsed a
            // character's mesh into a blob when speculative motion-pack files were parsed incorrectly).
            byte[] hugeJoints = new byte[12];
            BinaryPrimitives.WriteUInt16LittleEndian(hugeJoints.AsSpan(2, 2), 60000);
            BinaryPrimitives.WriteUInt16LittleEndian(hugeJoints.AsSpan(4, 2), 2);
            BinaryPrimitives.WriteSingleLittleEndian(hugeJoints.AsSpan(6, 4), 1.0f);
            Assert.Null(SkeletonAnimationDecoder.DecodeClip(hugeJoints, "idl0"));

            byte[] hugeFrames = new byte[12];
            BinaryPrimitives.WriteUInt16LittleEndian(hugeFrames.AsSpan(2, 2), 1);
            BinaryPrimitives.WriteUInt16LittleEndian(hugeFrames.AsSpan(4, 2), 60000);
            BinaryPrimitives.WriteSingleLittleEndian(hugeFrames.AsSpan(6, 4), 1.0f);
            Assert.Null(SkeletonAnimationDecoder.DecodeClip(hugeFrames, "idl0"));

            byte[] zeroDuration = new byte[12];
            BinaryPrimitives.WriteUInt16LittleEndian(zeroDuration.AsSpan(2, 2), 1);
            BinaryPrimitives.WriteUInt16LittleEndian(zeroDuration.AsSpan(4, 2), 2);
            BinaryPrimitives.WriteSingleLittleEndian(zeroDuration.AsSpan(6, 4), 0f);
            Assert.Null(SkeletonAnimationDecoder.DecodeClip(zeroDuration, "idl0"));

            byte[] nanDuration = new byte[12];
            BinaryPrimitives.WriteUInt16LittleEndian(nanDuration.AsSpan(2, 2), 1);
            BinaryPrimitives.WriteUInt16LittleEndian(nanDuration.AsSpan(4, 2), 2);
            BinaryPrimitives.WriteSingleLittleEndian(nanDuration.AsSpan(6, 4), float.NaN);
            Assert.Null(SkeletonAnimationDecoder.DecodeClip(nanDuration, "idl0"));
        }

        [Fact]
        public void SkeletonAnimationDecoder_DecodeClip_SkipsTrackWithNonFiniteValues()
        {
            byte[] payload = BuildSingleJointClipPayload();
            const int entryStart = 10;

            // Corrupt the constant translation-Y value with NaN, simulating a mis-parsed payload.
            BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(entryStart + 52, 4), float.NaN);

            var clip = SkeletonAnimationDecoder.DecodeClip(payload, "idl0");

            Assert.NotNull(clip);
            Assert.False(clip!.Tracks.ContainsKey(0));
        }

        private readonly Xunit.Abstractions.ITestOutputHelper _output;

        public SkeletonAnimationDecoderTests(Xunit.Abstractions.ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public void InspectRealGameAnimationFiles()
        {
            string gameDir = @"G:\Program Files (x86)\PlayOnline\SquareEnix\FINAL FANTASY XI";
            if (!System.IO.Directory.Exists(gameDir)) return;

            string[] paths = new[]
            {
                System.IO.Path.Combine(gameDir, "ROM", "27", "82.DAT"),
                System.IO.Path.Combine(gameDir, "ROM", "27", "83.DAT"),
                System.IO.Path.Combine(gameDir, "ROM", "27", "85.DAT"),
                System.IO.Path.Combine(gameDir, "ROM", "32", "13.DAT"),
            };

            foreach (var p in paths)
            {
                if (!System.IO.File.Exists(p)) continue;
                byte[] bytes = System.IO.File.ReadAllBytes(p);
                var headers = Gordian.Core.Resources.Containers.DatSectionWalker.ReadHeaders(bytes);
                int anims = 0;
                var clipNames = new List<string>();
                foreach (var h in headers)
                {
                    if (h.TypeCode == Gordian.Core.Resources.Containers.DatSectionType.SkeletonAnimation)
                    {
                        anims++;
                        var payload = bytes.AsSpan(h.DataOffset, h.DataSizeBytes);
                        var clip = SkeletonAnimationDecoder.DecodeClip(payload, h.DatId);
                        if (clip != null)
                        {
                            clipNames.Add($"{clip.Name}(f={clip.NumFrames},tr={clip.Tracks.Count})");
                        }
                    }
                }
                _output.WriteLine($"FILE: {System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(p))}/{System.IO.Path.GetFileName(p)} -> Anims: {clipNames.Count} / {anims}: {string.Join(", ", clipNames)}");
                Assert.True(clipNames.Count > 0, $"Expected clips in {p}, found {clipNames.Count} / {anims}.");
            }
        }

        [Fact]
        public void TestRealHumeMaleIdlePose()
        {
            string gameDir = @"G:\Program Files (x86)\PlayOnline\SquareEnix\FINAL FANTASY XI";
            if (!System.IO.Directory.Exists(gameDir)) return;

            string baseDatPath = System.IO.Path.Combine(gameDir, "ROM", "27", "82.DAT");
            if (!System.IO.File.Exists(baseDatPath)) return;

            byte[] baseBytes = System.IO.File.ReadAllBytes(baseDatPath);
            var baseContainer = Gordian.Core.Resources.EntityModelLoader.ParseDatContainer(baseBytes, "Base");
            Assert.NotNull(baseContainer.Skeleton);

            var bindPose = SkeletonPoseEvaluator.ComputeBindPose(baseContainer.Skeleton!);
            _output.WriteLine($"Skeleton joints: {baseContainer.Skeleton!.Count}");
            _output.WriteLine($"BindPose Joint 0 (root) trans: {bindPose.Translations[0]}");
            _output.WriteLine($"BindPose Joint 1 trans: {bindPose.Translations[1]}");
            _output.WriteLine($"BindPose Joint 10 trans: {bindPose.Translations[10]}");

            // Look up idl0 in baseContainer.Animations
            var idl0 = baseContainer.Animations.Find(a => a.Name == "idl0");
            Assert.NotNull(idl0);

            var animPose = SkeletonPoseEvaluator.EvaluatePose(baseContainer.Skeleton!, idl0, 0f, true);
            int collapsedCount = 0;
            for (int j = 0; j < baseContainer.Skeleton!.Count; j++)
            {
                var bp = bindPose.Translations[j];
                var ap = animPose.Translations[j];
                float bDist = bp.Length();
                float aDist = ap.Length();
                if (bDist > 0.05f && aDist < 0.001f)
                {
                    collapsedCount++;
                }
            }
            Assert.Equal(0, collapsedCount);

            var fileTable = new Gordian.Core.Resources.Tables.FileTableResolver();
            string ftablePath = System.IO.Path.Combine(gameDir, "FTABLE.DAT");
            string vtablePath = System.IO.Path.Combine(gameDir, "VTABLE.DAT");
            if (System.IO.File.Exists(ftablePath) && System.IO.File.Exists(vtablePath))
            {
                fileTable.LoadTablePair(System.IO.File.ReadAllBytes(ftablePath), System.IO.File.ReadAllBytes(vtablePath));
            }

            var original = EntityModelLoader.EnableSpeculativeMotionPacks;
            try
            {
                EntityModelLoader.EnableSpeculativeMotionPacks = true;
                var charModel = EntityModelLoader.AssembleCharacter(
                    CharacterRace.HumeMale,
                    0, // face 0
                    new ushort[] { 0, 0, 0, 0, 0, 0, 0, 0, 0 },
                    relPath =>
                    {
                        string fullPath = System.IO.Path.Combine(gameDir, relPath);
                        return System.IO.File.Exists(fullPath) ? System.IO.File.ReadAllBytes(fullPath) : null;
                    },
                    fid =>
                    {
                        if (fileTable.TryResolve(fid, out string relPath))
                        {
                            string fullPath = System.IO.Path.Combine(gameDir, relPath);
                            return System.IO.File.Exists(fullPath) ? System.IO.File.ReadAllBytes(fullPath) : null;
                        }
                        return null;
                    });

                Assert.NotNull(charModel);
                Assert.True(charModel!.Animations.ContainsKey("idl"));
                Assert.True(charModel.Animations.ContainsKey("wlk"));
                Assert.True(charModel.Animations.ContainsKey("run"));
                Assert.True(charModel.Animations.ContainsKey("btl"));
            }
            finally
            {
                EntityModelLoader.EnableSpeculativeMotionPacks = original;
            }
        }

        [Fact]
        public void TestRealMonsterModel()
        {
            string gameDir = @"G:\Program Files (x86)\PlayOnline\SquareEnix\FINAL FANTASY XI";
            if (!System.IO.Directory.Exists(gameDir)) return;

            var fileTable = new Gordian.Core.Resources.Tables.FileTableResolver();
            string ftablePath = System.IO.Path.Combine(gameDir, "FTABLE.DAT");
            string vtablePath = System.IO.Path.Combine(gameDir, "VTABLE.DAT");
            if (System.IO.File.Exists(ftablePath) && System.IO.File.Exists(vtablePath))
            {
                fileTable.LoadTablePair(System.IO.File.ReadAllBytes(ftablePath), System.IO.File.ReadAllBytes(vtablePath));
            }

            var monsterModel = EntityModelLoader.LoadMonsterModel(300, fid =>
            {
                if (fileTable.TryResolve(fid, out string relPath))
                {
                    string fullPath = System.IO.Path.Combine(gameDir, relPath);
                    return System.IO.File.Exists(fullPath) ? System.IO.File.ReadAllBytes(fullPath) : null;
                }
                return null;
            });

            Assert.NotNull(monsterModel);
            _output.WriteLine($"Monster model: {monsterModel!.Name}, meshes={monsterModel.AnimatedMeshGroups.Count}, skeleton={(monsterModel.Skeleton != null ? monsterModel.Skeleton.Count : 0)}");
            _output.WriteLine($"Monster animations ({monsterModel.Animations.Count}): {string.Join(", ", monsterModel.Animations.Keys)}");
            foreach (var anim in monsterModel.Animations.Values)
            {
                _output.WriteLine($"  Monster clip '{anim.Name}': frames={anim.NumFrames}, duration={anim.DurationSeconds}, tracks={anim.Tracks.Count}");
            }
        }
    }
}
