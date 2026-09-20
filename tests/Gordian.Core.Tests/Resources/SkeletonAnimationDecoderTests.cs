// tests/Gordian.Core.Tests/Resources/SkeletonAnimationDecoderTests.cs
using System;
using System.Buffers.Binary;
using System.Numerics;
using Gordian.Core.Resources.Graphics;
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
            Assert.Equal(2f / 30f, clip.DurationSeconds, 4);

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

            // Quarter-way through the clip -> frame 0.5 -> halfway between (0,2,3) and (5,2,3)
            float quarterTime = clip!.DurationSeconds * 0.25f;
            Assert.True(clip.TrySample(0, quarterTime, loop: true, out var rot, out var trans));
            Assert.Equal(Quaternion.Identity, rot);
            Assert.Equal(new Vector3(2.5f, 2f, 3f), trans);

            // No track for joint 5 -> caller should fall back to skeleton bind pose.
            Assert.False(clip.TrySample(5, quarterTime, loop: true, out _, out _));
        }

        [Fact]
        public void SkeletonAnimationDecoder_DecodeClip_SkipsBoneTrackOnNegativeOffset()
        {
            byte[] payload = BuildSingleJointClipPayload();
            const int entryStart = 10;

            // Rotation X offset = -1 -> whole rotation group dropped -> entire bone track skipped.
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(entryStart + 4, 4), -1);

            var clip = SkeletonAnimationDecoder.DecodeClip(payload, "idl0");

            Assert.NotNull(clip);
            Assert.False(clip!.Tracks.ContainsKey(0));
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
    }
}
