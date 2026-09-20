// src/Gordian.Core/Resources/Graphics/SkeletonAnimationDecoder.cs
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Numerics;
using Gordian.Core.Resources.Models;

namespace Gordian.Core.Resources.Graphics
{
    /// <summary>
    /// Clean-room binary decoder for FFXI Section 0x2B SkeletonAnimation resources.
    /// Parses per-joint rotation/translation keyframe tracks (scale channels are decoded but
    /// not surfaced, as no consuming code path uses bone scale).
    /// Derived from community research in xi-tools (https://github.com/vekien/xi-tools) docs/anim/format.md.
    /// </summary>
    public static class SkeletonAnimationDecoder
    {
        private const int AnimationHeaderSize = 10;
        private const int BoneEntryStride = 84;

        /// <summary>
        /// Decodes a Section 0x2B skeleton animation payload into a structured AnimationClip.
        /// The payload passed in is already past the generic 16-byte DAT block header
        /// (consistent with SkeletonDecoder/SkeletonMeshDecoder), so all offsets below are
        /// relative to that DatSectionWalker-stripped payload, not the raw block start.
        /// </summary>
        public static AnimationClip? DecodeClip(ReadOnlySpan<byte> payload, string clipName)
        {
            if (payload.Length < AnimationHeaderSize) return null;

            ushort numJoints = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(2, 2));
            ushort numFrames = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(4, 2));
            float keyFrameDuration = BinaryPrimitives.ReadSingleLittleEndian(payload.Slice(6, 4));

            // Sanity bounds: real clips have at most a few hundred joints/frames. Rejecting
            // implausible values here prevents mis-aligned/non-animation payloads (e.g. a DAT
            // that isn't actually a 0x2B section but got scanned as one) from being decoded into
            // a garbage clip that would otherwise silently corrupt pose evaluation downstream.
            if (numJoints == 0 || numFrames == 0 || numJoints > 512 || numFrames > 4096) return null;
            if (!float.IsFinite(keyFrameDuration) || keyFrameDuration <= 0f || keyFrameDuration > 1000f) return null;

            int boneTableStart = AnimationHeaderSize;
            var tracks = new Dictionary<int, BoneAnimationTrack>(numJoints);

            Span<int> rotOffsets = stackalloc int[4];
            Span<float> rotConst = stackalloc float[4];
            Span<int> transOffsets = stackalloc int[3];
            Span<float> transConst = stackalloc float[3];
            Span<int> scaleOffsets = stackalloc int[3];

            for (int b = 0; b < numJoints; b++)
            {
                int entryStart = boneTableStart + (b * BoneEntryStride);
                if (entryStart + BoneEntryStride > payload.Length) break;

                int jointIndex = BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(entryStart, 4));

                for (int i = 0; i < 4; i++)
                {
                    rotOffsets[i] = BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(entryStart + 4 + (i * 4), 4));
                }
                for (int i = 0; i < 4; i++)
                {
                    rotConst[i] = BinaryPrimitives.ReadSingleLittleEndian(payload.Slice(entryStart + 20 + (i * 4), 4));
                }

                for (int i = 0; i < 3; i++)
                {
                    transOffsets[i] = BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(entryStart + 36 + (i * 4), 4));
                }
                for (int i = 0; i < 3; i++)
                {
                    transConst[i] = BinaryPrimitives.ReadSingleLittleEndian(payload.Slice(entryStart + 48 + (i * 4), 4));
                }

                for (int i = 0; i < 3; i++)
                {
                    scaleOffsets[i] = BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(entryStart + 60 + (i * 4), 4));
                }

                if (!TryResolveChannelGroup(payload, boneTableStart, rotOffsets, numFrames, out float[]?[] rotChannels))
                {
                    continue; // rotation group dropped -> entire bone track skipped
                }

                if (!TryResolveChannelGroup(payload, boneTableStart, transOffsets, numFrames, out float[]?[] transChannels))
                {
                    continue; // translation group dropped -> entire bone track skipped
                }

                // Scale values themselves are not stored/applied by any consuming code yet, but per
                // the documented format the scale group still participates in the drop/skip decision:
                // a negative/absent scale channel means the whole bone track is skipped.
                if (!TryResolveChannelGroup(payload, boneTableStart, scaleOffsets, numFrames, out _))
                {
                    continue;
                }

                var rotations = new Quaternion[numFrames];
                var translations = new Vector3[numFrames];
                bool allFinite = true;

                for (int f = 0; f < numFrames; f++)
                {
                    float qx = rotChannels[0]?[f] ?? rotConst[0];
                    float qy = rotChannels[1]?[f] ?? rotConst[1];
                    float qz = rotChannels[2]?[f] ?? rotConst[2];
                    float qw = rotChannels[3]?[f] ?? rotConst[3];

                    var q = new Quaternion(qx, qy, qz, qw);
                    rotations[f] = q.LengthSquared() > 0.0001f ? Quaternion.Normalize(q) : Quaternion.Identity;

                    float tx = transChannels[0]?[f] ?? transConst[0];
                    float ty = transChannels[1]?[f] ?? transConst[1];
                    float tz = transChannels[2]?[f] ?? transConst[2];
                    translations[f] = new Vector3(tx, ty, tz);

                    if (!float.IsFinite(tx) || !float.IsFinite(ty) || !float.IsFinite(tz) ||
                        !float.IsFinite(qx) || !float.IsFinite(qy) || !float.IsFinite(qz) || !float.IsFinite(qw))
                    {
                        allFinite = false;
                    }
                }

                // Non-finite (NaN/Infinity) values indicate a mis-parsed/garbage payload rather than
                // a real clip - skip this bone track entirely instead of feeding NaN into pose evaluation.
                if (!allFinite || jointIndex < 0 || jointIndex > 4096)
                {
                    continue;
                }

                tracks[jointIndex] = new BoneAnimationTrack
                {
                    JointIndex = jointIndex,
                    Rotations = rotations,
                    Translations = translations
                };
            }

            return new AnimationClip
            {
                Name = clipName,
                NumFrames = numFrames,
                KeyFrameDuration = keyFrameDuration,
                Tracks = tracks
            };
        }

        /// <summary>
        /// Resolves a channel group (rotation or translation) into per-component per-frame arrays.
        /// Returns false if any offset in the group is negative (including int.MinValue), which per
        /// the documented format means the whole group - and therefore the entire bone track - is dropped.
        /// A null per-component array means "use the constant value for every frame".
        /// </summary>
        private static bool TryResolveChannelGroup(ReadOnlySpan<byte> payload, int boneTableStart, ReadOnlySpan<int> offsets, int numFrames, out float[]?[] channels)
        {
            channels = new float[]?[offsets.Length];

            for (int c = 0; c < offsets.Length; c++)
            {
                int offset = offsets[c];
                if (offset < 0)
                {
                    return false;
                }

                if (offset == 0)
                {
                    channels[c] = null; // constant channel
                    continue;
                }

                int dataStart = boneTableStart + (offset * 4);
                int bytesNeeded = numFrames * 4;
                if (dataStart < 0 || dataStart + bytesNeeded > payload.Length)
                {
                    return false;
                }

                var values = new float[numFrames];
                for (int f = 0; f < numFrames; f++)
                {
                    values[f] = BinaryPrimitives.ReadSingleLittleEndian(payload.Slice(dataStart + (f * 4), 4));
                }
                channels[c] = values;
            }

            return true;
        }
    }
}
