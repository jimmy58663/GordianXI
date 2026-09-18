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
    }
}
