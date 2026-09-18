// src/Gordian.Core/Resources/Graphics/SkeletonPoseEvaluator.cs
using System;
using System.Collections.Generic;
using System.Numerics;
using Gordian.Core.Resources.Models;

namespace Gordian.Core.Resources.Graphics
{
    /// <summary>
    /// Evaluates skeletal joint hierarchies into bind-pose world transforms and CPU-skins
    /// SkeletonMeshGroup geometry into renderable MeshGroup batches.
    /// Clean-room implementation referencing FFXI skinning specifications in xi-model-viewer (https://github.com/vekien/xi-model-viewer) and xim.
    /// </summary>
    public static class SkeletonPoseEvaluator
    {
        public readonly struct EvaluatedPose
        {
            public Quaternion[] Rotations { get; }
            public Vector3[] Translations { get; }

            public EvaluatedPose(Quaternion[] rotations, Vector3[] translations)
            {
                Rotations = rotations;
                Translations = translations;
            }
        }

        /// <summary>
        /// Computes bind-pose world rotation and translation for each joint in the skeleton hierarchy.
        /// </summary>
        public static EvaluatedPose ComputeBindPose(Skeleton skeleton)
        {
            int n = skeleton.Count;
            if (n == 0)
            {
                return new EvaluatedPose(Array.Empty<Quaternion>(), Array.Empty<Vector3>());
            }

            var rot = new Quaternion[n];
            var trans = new Vector3[n];
            var computed = new bool[n];

            // Resolve joint transforms in dependency order
            bool missing;
            int passes = 0;
            do
            {
                missing = false;
                bool progressed = false;

                for (int i = 0; i < n; i++)
                {
                    if (computed[i]) continue;

                    var joint = skeleton.Joints[i];
                    int parent = joint.Parent;

                    if (parent >= 0 && parent < n && !computed[parent])
                    {
                        missing = true;
                        continue;
                    }

                    Vector3 t = joint.Translation;
                    Quaternion r = joint.Rotation;

                    if (i == 0)
                    {
                        // FFXI root translation coordinate space: (x, y, z) -> (-z, y, x)
                        t = new Vector3(-t.Z, t.Y, t.X);
                        rot[i] = r;
                        trans[i] = t;
                    }
                    else if (parent < 0 || parent >= n)
                    {
                        rot[i] = r;
                        trans[i] = t;
                    }
                    else
                    {
                        // Child joint: accumulate parent rotation and parent translation
                        Vector3 rotated = Vector3.Transform(t, rot[parent]);
                        trans[i] = trans[parent] + rotated;
                        rot[i] = Quaternion.Normalize(rot[parent] * r);
                    }

                    computed[i] = true;
                    progressed = true;
                }

                passes++;
                if (missing && !progressed)
                {
                    // Break possible cyclic or orphaned joints
                    for (int i = 0; i < n; i++)
                    {
                        if (!computed[i])
                        {
                            rot[i] = Quaternion.Identity;
                            trans[i] = Vector3.Zero;
                            computed[i] = true;
                        }
                    }
                    break;
                }
            } while (missing && passes < n + 2);

            return new EvaluatedPose(rot, trans);
        }

        /// <summary>
        /// CPU-skins a single SkinnedVertex into world position and normal using the evaluated bind pose.
        /// </summary>
        public static (Vector3 Position, Vector3 Normal) SkinVertex(in SkinnedVertex v, in EvaluatedPose pose)
        {
            if (pose.Rotations.Length == 0)
            {
                return (v.Position0, v.Normal0);
            }

            int j0 = Math.Clamp(v.Joint0, 0, pose.Rotations.Length - 1);
            Vector3 p0 = Vector3.Transform(v.Position0, pose.Rotations[j0]);
            Vector3 t0 = pose.Translations[j0];
            Vector3 n0 = Vector3.Transform(v.Normal0, pose.Rotations[j0]);

            if (v.Joint1 < 0 || pose.Rotations.Length == 1)
            {
                Vector3 finalNormal = n0.LengthSquared() > 0.0001f ? Vector3.Normalize(n0) : Vector3.UnitY;
                return (t0 + p0, finalNormal);
            }

            int j1 = Math.Clamp(v.Joint1, 0, pose.Rotations.Length - 1);
            Vector3 p1 = Vector3.Transform(v.Position1, pose.Rotations[j1]);
            Vector3 t1 = pose.Translations[j1];
            Vector3 n1 = Vector3.Transform(v.Normal1, pose.Rotations[j1]);

            // Double joint: positions are pre-weighted: p_i = w_i * local
            Vector3 finalPos = p0 + (v.Weight0 * t0) + p1 + (v.Weight1 * t1);
            Vector3 normSum = (n0 * v.Weight0) + (n1 * v.Weight1);
            Vector3 finalNorm = normSum.LengthSquared() > 0.0001f ? Vector3.Normalize(normSum) : Vector3.UnitY;

            return (finalPos, finalNorm);
        }

        /// <summary>
        /// Evaluates an entire SkeletonMeshGroup into one or more renderable MeshGroup instances.
        /// </summary>
        public static List<MeshGroup> EvaluateMeshGroup(SkeletonMeshGroup meshGroup, in EvaluatedPose pose)
        {
            var results = new List<MeshGroup>();
            if (meshGroup.Pieces.Count == 0 || meshGroup.Vertices.Length == 0)
            {
                return results;
            }

            for (int p = 0; p < meshGroup.Pieces.Count; p++)
            {
                var piece = meshGroup.Pieces[p];
                if (piece.Corners.Length < 3) continue;

                var vertPool = piece.Mirrored && meshGroup.FlippedVertices != null
                    ? meshGroup.FlippedVertices
                    : meshGroup.Vertices;

                var outVerts = new List<MeshVertex>();
                var outIndices = new List<int>();

                Vector3 minBounds = new(float.MaxValue);
                Vector3 maxBounds = new(float.MinValue);

                if (piece.Topology == MeshTopology.TriangleList)
                {
                    for (int i = 0; i + 2 < piece.Corners.Length; i += 3)
                    {
                        int baseIdx = outVerts.Count;
                        for (int k = 0; k < 3; k++)
                        {
                            var corner = piece.Corners[i + k];
                            int vi = Math.Clamp(corner.VertexIndex, 0, vertPool.Length - 1);
                            var (pos, norm) = SkinVertex(vertPool[vi], pose);

                            minBounds = Vector3.Min(minBounds, pos);
                            maxBounds = Vector3.Max(maxBounds, pos);

                            outVerts.Add(new MeshVertex(pos, norm, corner.TexCoord, corner.ColorRgba));
                        }

                        outIndices.Add(baseIdx);
                        outIndices.Add(baseIdx + 1);
                        outIndices.Add(baseIdx + 2);
                    }
                }
                else if (piece.Topology == MeshTopology.TriangleStrip)
                {
                    // Triangle Strip unrolling
                    int vertBase = outVerts.Count;
                    for (int i = 0; i < piece.Corners.Length; i++)
                    {
                        var corner = piece.Corners[i];
                        int vi = Math.Clamp(corner.VertexIndex, 0, vertPool.Length - 1);
                        var (pos, norm) = SkinVertex(vertPool[vi], pose);

                        minBounds = Vector3.Min(minBounds, pos);
                        maxBounds = Vector3.Max(maxBounds, pos);

                        outVerts.Add(new MeshVertex(pos, norm, corner.TexCoord, corner.ColorRgba));
                    }

                    for (int i = 0; i < piece.Corners.Length - 2; i++)
                    {
                        int i0 = vertBase + i;
                        int i1 = vertBase + i + 1;
                        int i2 = vertBase + i + 2;

                        // Alternating winding order for strips
                        if ((i & 1) == 0)
                        {
                            outIndices.Add(i0);
                            outIndices.Add(i1);
                            outIndices.Add(i2);
                        }
                        else
                        {
                            outIndices.Add(i1);
                            outIndices.Add(i0);
                            outIndices.Add(i2);
                        }
                    }
                }

                if (outIndices.Count > 0)
                {
                    results.Add(new MeshGroup
                    {
                        Name = $"{piece.TextureName}_{(piece.Mirrored ? "Mirror" : "Normal")}_{p}",
                        TextureName = piece.TextureName,
                        Vertices = outVerts.ToArray(),
                        Indices = outIndices.ToArray(),
                        MinBounds = minBounds,
                        MaxBounds = maxBounds
                    });
                }
            }

            return results;
        }
    }
}
