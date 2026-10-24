// src/Gordian.Core/Resources/Models/Skeleton.cs
using System;
using System.Collections.Generic;
using System.Numerics;

namespace Gordian.Core.Resources.Models
{
    /// <summary>
    /// Represents an individual bone or joint within an FFXI skeleton hierarchy.
    /// Skeleton and joint hierarchy structures referenced from xi-model-viewer (https://github.com/vekien/xi-model-viewer).
    /// </summary>
    public sealed class SkeletonJoint
    {
        public int Parent { get; }
        public Quaternion Rotation { get; }
        public Vector3 Translation { get; }

        public SkeletonJoint(int parent, Quaternion rotation, Vector3 translation)
        {
            Parent = parent;
            Rotation = rotation;
            Translation = translation;
        }

        public override string ToString() => $"Joint (Parent: {Parent}, Trans: {Translation}, Rot: {Rotation})";
    }

    /// <summary>
    /// Named attachment reference point into the skeleton (e.g., weapon grips, hands, head).
    /// </summary>
    public readonly record struct JointReference(ushort Index, Vector3 Offset);

    /// <summary>
    /// Complete skeletal structure containing joint nodes and socket references.
    /// </summary>
    public sealed class Skeleton
    {
        public IReadOnlyList<SkeletonJoint> Joints { get; }
        public IReadOnlyList<JointReference> References { get; }

        public Skeleton(IReadOnlyList<SkeletonJoint> joints, IReadOnlyList<JointReference>? references = null)
        {
            Joints = joints ?? Array.Empty<SkeletonJoint>();
            References = references ?? Array.Empty<JointReference>();
        }

        public int Count => Joints.Count;

        /// <summary>
        /// Position of skeleton joint reference <paramref name="referenceIndex"/> in model space for a posed skeleton:
        /// the reference's joint plus its offset (scaled and turned by the joint). Returns the model origin when the
        /// skeleton has no such reference or the pose lacks the joint.
        /// </summary>
        public static Vector3 ResolveJointReference(
            Skeleton? skeleton,
            ReadOnlySpan<Vector3> translations,
            ReadOnlySpan<Quaternion> rotations,
            ReadOnlySpan<Vector3> scales,
            int referenceIndex)
        {
            if (skeleton == null || referenceIndex < 0 || referenceIndex >= skeleton.References.Count) return Vector3.Zero;
            var reference = skeleton.References[referenceIndex];
            int joint = reference.Index;
            if (joint >= translations.Length) return Vector3.Zero;
            Vector3 offset = reference.Offset * (joint < scales.Length ? scales[joint] : Vector3.One);
            if (joint < rotations.Length) offset = Vector3.Transform(offset, rotations[joint]);
            return translations[joint] + offset;
        }
    }
}
