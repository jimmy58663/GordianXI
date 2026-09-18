// src/Gordian.Core/Resources/Graphics/SkeletonDecoder.cs
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Numerics;
using Gordian.Core.Resources.Models;

namespace Gordian.Core.Resources.Graphics
{
    /// <summary>
    /// Clean-room binary decoder for FFXI Section 0x29 Skeleton resources.
    /// Parses bone hierarchy, parent linkage, local rotation quaternions, translations, and joint references.
    /// Derived from community research in xi-model-viewer (https://github.com/vekien/xi-model-viewer) and xim.
    /// </summary>
    public static class SkeletonDecoder
    {
        /// <summary>
        /// Parses a Section 0x29 skeleton payload into a structured Skeleton model.
        /// </summary>
        public static Skeleton? DecodeSkeleton(ReadOnlySpan<byte> payload)
        {
            if (payload.Length < 4) return null;

            int numJoints = payload[2];
            int requiredJointsSize = 4 + (numJoints * 30);
            if (payload.Length < requiredJointsSize) return null;

            var joints = new List<SkeletonJoint>(numJoints);
            int pos = 4;

            for (int i = 0; i < numJoints; i++)
            {
                byte maybeParent = payload[pos];
                int parent = (maybeParent == i || maybeParent >= numJoints) ? -1 : maybeParent;
                pos += 2; // skip parent byte and 1 byte padding

                float qx = BinaryPrimitives.ReadSingleLittleEndian(payload.Slice(pos, 4));
                float qy = BinaryPrimitives.ReadSingleLittleEndian(payload.Slice(pos + 4, 4));
                float qz = BinaryPrimitives.ReadSingleLittleEndian(payload.Slice(pos + 8, 4));
                float qw = BinaryPrimitives.ReadSingleLittleEndian(payload.Slice(pos + 12, 4));
                pos += 16;

                float tx = BinaryPrimitives.ReadSingleLittleEndian(payload.Slice(pos, 4));
                float ty = BinaryPrimitives.ReadSingleLittleEndian(payload.Slice(pos + 4, 4));
                float tz = BinaryPrimitives.ReadSingleLittleEndian(payload.Slice(pos + 8, 4));
                pos += 12;

                var q = new Quaternion(qx, qy, qz, qw);
                if (q.LengthSquared() > 0.0001f)
                {
                    q = Quaternion.Normalize(q);
                }
                else
                {
                    q = Quaternion.Identity;
                }

                joints.Add(new SkeletonJoint(parent, q, new Vector3(tx, ty, tz)));
            }

            var references = new List<JointReference>();
            if (pos + 4 <= payload.Length)
            {
                ushort numRefs = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(pos, 2));
                pos += 4; // numRefs + 2 bytes padding

                for (int i = 0; i < numRefs && pos + 26 <= payload.Length; i++)
                {
                    ushort targetIndex = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(pos, 2));
                    pos += 14; // index + 12 bytes unk

                    float ox = BinaryPrimitives.ReadSingleLittleEndian(payload.Slice(pos, 4));
                    float oy = BinaryPrimitives.ReadSingleLittleEndian(payload.Slice(pos + 4, 4));
                    float oz = BinaryPrimitives.ReadSingleLittleEndian(payload.Slice(pos + 8, 4));
                    pos += 12;

                    references.Add(new JointReference(targetIndex, new Vector3(ox, oy, oz)));
                }
            }

            return new Skeleton(joints, references);
        }
    }
}
