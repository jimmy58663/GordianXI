// tests/Gordian.Core.Tests/Resources/SkeletonDecoderTests.cs
using System;
using System.Buffers.Binary;
using System.Numerics;
using Gordian.Core.Resources.Graphics;
using Gordian.Core.Resources.Models;
using Xunit;

namespace Gordian.Core.Tests.Resources
{
    public class SkeletonDecoderTests
    {
        [Fact]
        public void SkeletonDecoder_DecodeSkeleton_ParsesJointsAndReferences()
        {
            // Build synthetic Section 0x29 payload
            // Header: 2 bytes unk, 1 byte numJoints = 2, 1 byte unk
            // Joint 0: parent = 0 (root -> -1), 1 byte pad, 4 floats rot (0,0,0,1), 3 floats trans (1, 2, 3)
            // Joint 1: parent = 0 (child of joint 0), 1 byte pad, 4 floats rot (0,0,0,1), 3 floats trans (0, 10, 0)
            // After joints: 2 bytes numRefs = 1, 2 bytes pad
            // Ref 0: 2 bytes index = 126, 12 bytes unk, 3 floats offset (0, 0.5f, 0)
            int jointBytes = 2 * 30; // 60
            int refBytes = 4 + 26;   // 30
            int totalBytes = 4 + jointBytes + refBytes;

            byte[] payload = new byte[totalBytes];
            payload[2] = 2; // numJoints

            // Joint 0 (root)
            int j0 = 4;
            payload[j0] = 0; // parent = self => root -1
            payload[j0 + 1] = 0; // pad
            BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(j0 + 2, 4), 0f);  // qx
            BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(j0 + 6, 4), 0f);  // qy
            BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(j0 + 10, 4), 0f); // qz
            BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(j0 + 14, 4), 1f); // qw
            BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(j0 + 18, 4), 1f); // tx
            BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(j0 + 22, 4), 2f); // ty
            BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(j0 + 26, 4), 3f); // tz

            // Joint 1 (child)
            int j1 = j0 + 30;
            payload[j1] = 0; // parent = 0
            payload[j1 + 1] = 0; // pad
            BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(j1 + 2, 4), 0f);
            BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(j1 + 6, 4), 0f);
            BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(j1 + 10, 4), 0f);
            BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(j1 + 14, 4), 1f);
            BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(j1 + 18, 4), 0f);
            BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(j1 + 22, 4), 10f);
            BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(j1 + 26, 4), 0f);

            // References
            int refStart = j1 + 30;
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(refStart, 2), 1); // 1 ref

            int r0 = refStart + 4;
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(r0, 2), 126); // target joint index 126
            BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(r0 + 14, 4), 0f);
            BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(r0 + 18, 4), 0.5f);
            BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(r0 + 22, 4), 0f);

            var skeleton = SkeletonDecoder.DecodeSkeleton(payload);
            Assert.NotNull(skeleton);
            Assert.Equal(2, skeleton.Count);

            Assert.Equal(-1, skeleton.Joints[0].Parent);
            Assert.Equal(new Vector3(1f, 2f, 3f), skeleton.Joints[0].Translation);
            Assert.Equal(Quaternion.Identity, skeleton.Joints[0].Rotation);

            Assert.Equal(0, skeleton.Joints[1].Parent);
            Assert.Equal(new Vector3(0f, 10f, 0f), skeleton.Joints[1].Translation);

            Assert.Single(skeleton.References);
            Assert.Equal(126, skeleton.References[0].Index);
            Assert.Equal(new Vector3(0f, 0.5f, 0f), skeleton.References[0].Offset);
        }

        [Fact]
        public void SkeletonDecoder_DecodeSkeleton_HandlesTruncatedPayload()
        {
            byte[] truncated = new byte[3];
            Assert.Null(SkeletonDecoder.DecodeSkeleton(truncated));

            byte[] invalidLength = new byte[10];
            invalidLength[2] = 5; // claims 5 joints (requires 4 + 150 = 154 bytes)
            Assert.Null(SkeletonDecoder.DecodeSkeleton(invalidLength));
        }
    }
}
