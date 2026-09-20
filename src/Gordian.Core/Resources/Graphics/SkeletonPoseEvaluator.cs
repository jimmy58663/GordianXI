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
            public Vector3[] Scales { get; }

            public EvaluatedPose(Quaternion[] rotations, Vector3[] translations, Vector3[]? scales = null)
            {
                Rotations = rotations;
                Translations = translations;
                Scales = scales ?? Array.Empty<Vector3>();
            }
        }

        /// <summary>
        /// Computes bind-pose world rotation and translation for each joint in the skeleton hierarchy.
        /// Optionally accepts parentOverrides mapping jointIndex -> replacementParentJointIndex (e.g. for re-parenting weapon grip joints to hands).
        /// </summary>
        public static EvaluatedPose ComputeBindPose(Skeleton skeleton, IReadOnlyDictionary<int, int>? parentOverrides = null)
        {
            return EvaluatePose(skeleton, null, 0f, false, parentOverrides);
        }

        /// <summary>
        /// Evaluates world rotation/translation/scale for each joint at a given clip playback time.
        /// When a clip is active, keyframe translation deltas are added onto the skeleton's static bind-pose
        /// local translation, keyframe rotation deltas are applied onto the bind-pose local rotation,
        /// and keyframe scales are accumulated down the hierarchy.
        /// Joints without a track in the clip (or when clip is null) retain their static bind-pose transform.
        /// Reference: xi-model-viewer (https://github.com/vekien/xi-model-viewer) pose.js.
        /// </summary>
        public static EvaluatedPose EvaluatePose(Skeleton skeleton, AnimationClip? clip, float timeSeconds, bool loop, IReadOnlyDictionary<int, int>? parentOverrides = null)
        {
            int n = skeleton.Count;
            if (n == 0)
            {
                return new EvaluatedPose(Array.Empty<Quaternion>(), Array.Empty<Vector3>(), Array.Empty<Vector3>());
            }

            var rot = new Quaternion[n];
            var trans = new Vector3[n];
            var scale = new Vector3[n];
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

                    // Hand re-parenting override: the joint adopts the replacement parent transform wholesale
                    if (parentOverrides != null && parentOverrides.TryGetValue(i, out int overrideParent))
                    {
                        if (overrideParent >= 0 && overrideParent < n && !computed[overrideParent])
                        {
                            missing = true;
                            continue;
                        }

                        if (overrideParent >= 0 && overrideParent < n)
                        {
                            rot[i] = rot[overrideParent];
                            trans[i] = trans[overrideParent];
                            scale[i] = scale[overrideParent];
                        }
                        else
                        {
                            rot[i] = Quaternion.Identity;
                            trans[i] = Vector3.Zero;
                            scale[i] = Vector3.One;
                        }

                        computed[i] = true;
                        progressed = true;
                        continue;
                    }

                    var joint = skeleton.Joints[i];
                    int parent = joint.Parent;

                    if (parent >= 0 && parent < n && !computed[parent])
                    {
                        missing = true;
                        continue;
                    }

                    Vector3 t = joint.Translation;
                    Quaternion r = joint.Rotation;
                    Vector3 s = Vector3.One;
                    if (clip != null && clip.TrySample(i, timeSeconds, loop, out var animRot, out var animTrans, out var animScale))
                    {
                        t += animTrans;
                        // Protocol spec referenced from xi-model-viewer (https://github.com/vekien/xi-model-viewer) pose.js:
                        // anim applied after bind rotation: rotation = qMul(s.q, rotation).
                        // In .NET Quaternion.Multiply(a, b), animRot * r directly matches qMul(s.q, rotation).
                        r = animRot * r;
                        if (i != 0)
                        {
                            s = animScale; // root scale ignored per FFXI spec
                        }
                    }

                    if (i == 0)
                    {
                        // FFXI root translation coordinate space: (x, y, z) -> (-z, y, x)
                        t = new Vector3(-t.Z, t.Y, t.X);
                        rot[i] = r;
                        trans[i] = t;
                        scale[i] = s;
                    }
                    else if (parent < 0 || parent >= n)
                    {
                        rot[i] = r;
                        trans[i] = t;
                        scale[i] = s;
                    }
                    else
                    {
                        // Child joint: accumulate parent scale, rotation and translation
                        Vector3 ps = scale[parent];
                        Vector3 scaled = ps * t;
                        Vector3 rotated = Vector3.Transform(scaled, rot[parent]);
                        trans[i] = trans[parent] + rotated;
                        scale[i] = ps * s;
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
                            scale[i] = Vector3.One;
                            computed[i] = true;
                        }
                    }
                    break;
                }
            } while (missing && passes < n + 2);

            return new EvaluatedPose(rot, trans, scale);
        }

        /// <summary>
        /// Evaluates world rotation/translation/scale for each joint by interpolating between two animation clips
        /// in local joint space using vector Lerp (translation/scale) and quaternion NLERP (rotation) prior to
        /// hierarchy accumulation.
        /// When blendWeight is 0.0f, the pose exactly matches clipA; when blendWeight is 1.0f, it matches clipB.
        /// Reference: xi-model-viewer (https://github.com/vekien/xi-model-viewer) pose.js.
        /// </summary>
        public static EvaluatedPose EvaluateBlendedPose(
            Skeleton skeleton,
            AnimationClip? clipA,
            float timeA,
            bool loopA,
            AnimationClip? clipB,
            float timeB,
            bool loopB,
            float blendWeight,
            IReadOnlyDictionary<int, int>? parentOverrides = null)
        {
            if (blendWeight <= 0f || clipB == null)
            {
                return EvaluatePose(skeleton, clipA, timeA, loopA, parentOverrides);
            }
            if (blendWeight >= 1f || clipA == null)
            {
                return EvaluatePose(skeleton, clipB, timeB, loopB, parentOverrides);
            }

            int n = skeleton.Count;
            if (n == 0)
            {
                return new EvaluatedPose(Array.Empty<Quaternion>(), Array.Empty<Vector3>(), Array.Empty<Vector3>());
            }

            var rot = new Quaternion[n];
            var trans = new Vector3[n];
            var scale = new Vector3[n];
            var computed = new bool[n];

            float w = Math.Clamp(blendWeight, 0f, 1f);

            bool missing;
            int passes = 0;
            do
            {
                missing = false;
                bool progressed = false;

                for (int i = 0; i < n; i++)
                {
                    if (computed[i]) continue;

                    // Hand re-parenting override: the joint adopts the replacement parent transform wholesale
                    if (parentOverrides != null && parentOverrides.TryGetValue(i, out int overrideParent))
                    {
                        if (overrideParent >= 0 && overrideParent < n && !computed[overrideParent])
                        {
                            missing = true;
                            continue;
                        }

                        if (overrideParent >= 0 && overrideParent < n)
                        {
                            rot[i] = rot[overrideParent];
                            trans[i] = trans[overrideParent];
                            scale[i] = scale[overrideParent];
                        }
                        else
                        {
                            rot[i] = Quaternion.Identity;
                            trans[i] = Vector3.Zero;
                            scale[i] = Vector3.One;
                        }

                        computed[i] = true;
                        progressed = true;
                        continue;
                    }

                    var joint = skeleton.Joints[i];
                    int parent = joint.Parent;

                    if (parent >= 0 && parent < n && !computed[parent])
                    {
                        missing = true;
                        continue;
                    }

                    Vector3 t = joint.Translation;
                    Quaternion r = joint.Rotation;
                    Vector3 s = Vector3.One;

                    bool hasA = clipA.TrySample(i, timeA, loopA, out var rotA, out var transA, out var scaleA);
                    bool hasB = clipB.TrySample(i, timeB, loopB, out var rotB, out var transB, out var scaleB);

                    if (hasA || hasB)
                    {
                        Vector3 transDelta;
                        Quaternion blendedDeltaRot;
                        Vector3 blendedScale;

                        if (hasA && hasB)
                        {
                            transDelta = Vector3.Lerp(transA, transB, w);
                            if (Quaternion.Dot(rotA, rotB) < 0f)
                            {
                                rotB = -rotB;
                            }
                            blendedDeltaRot = Quaternion.Normalize(Quaternion.Lerp(rotA, rotB, w));
                            blendedScale = Vector3.Lerp(scaleA, scaleB, w);
                        }
                        else if (hasA)
                        {
                            transDelta = Vector3.Lerp(transA, Vector3.Zero, w);
                            Quaternion idRot = Quaternion.Identity;
                            if (Quaternion.Dot(rotA, idRot) < 0f)
                            {
                                idRot = -idRot;
                            }
                            blendedDeltaRot = Quaternion.Normalize(Quaternion.Lerp(rotA, idRot, w));
                            blendedScale = Vector3.Lerp(scaleA, Vector3.One, w);
                        }
                        else
                        {
                            transDelta = Vector3.Lerp(Vector3.Zero, transB, w);
                            Quaternion idRot = Quaternion.Identity;
                            if (Quaternion.Dot(idRot, rotB) < 0f)
                            {
                                rotB = -rotB;
                            }
                            blendedDeltaRot = Quaternion.Normalize(Quaternion.Lerp(idRot, rotB, w));
                            blendedScale = Vector3.Lerp(Vector3.One, scaleB, w);
                        }

                        t += transDelta;
                        r = blendedDeltaRot * r;
                        if (i != 0)
                        {
                            s = blendedScale;
                        }
                    }

                    if (i == 0)
                    {
                        // FFXI root translation coordinate space: (x, y, z) -> (-z, y, x)
                        t = new Vector3(-t.Z, t.Y, t.X);
                        rot[i] = r;
                        trans[i] = t;
                        scale[i] = s;
                    }
                    else if (parent < 0 || parent >= n)
                    {
                        rot[i] = r;
                        trans[i] = t;
                        scale[i] = s;
                    }
                    else
                    {
                        // Child joint: accumulate parent scale, rotation and translation
                        Vector3 ps = scale[parent];
                        Vector3 scaled = ps * t;
                        Vector3 rotated = Vector3.Transform(scaled, rot[parent]);
                        trans[i] = trans[parent] + rotated;
                        scale[i] = ps * s;
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
                            scale[i] = Vector3.One;
                            computed[i] = true;
                        }
                    }
                    break;
                }
            } while (missing && passes < n + 2);

            return new EvaluatedPose(rot, trans, scale);
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
            Vector3 s0 = j0 < pose.Scales.Length ? pose.Scales[j0] : Vector3.One;
            Vector3 p0 = Vector3.Transform(s0 * v.Position0, pose.Rotations[j0]);
            Vector3 t0 = pose.Translations[j0];
            Vector3 n0 = Vector3.Transform(v.Normal0, pose.Rotations[j0]);

            if (v.Joint1 < 0 || pose.Rotations.Length == 1)
            {
                Vector3 finalNormal = n0.LengthSquared() > 0.0001f ? Vector3.Normalize(n0) : Vector3.UnitY;
                return (t0 + p0, finalNormal);
            }

            int j1 = Math.Clamp(v.Joint1, 0, pose.Rotations.Length - 1);
            Vector3 s1 = j1 < pose.Scales.Length ? pose.Scales[j1] : Vector3.One;
            Vector3 p1 = Vector3.Transform(s1 * v.Position1, pose.Rotations[j1]);
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

        /// <summary>
        /// Unrolls a SkeletonMeshGroup into GPU-ready AnimatedMeshGroup batches carrying raw
        /// per-vertex joint/weight/position/normal data for real-time GPU skinning, instead of
        /// baking a final world-space position. bindPoseForBounds is used only to compute an
        /// approximate frustum-culling AABB per submesh; the position it yields is otherwise discarded.
        /// </summary>
        public static List<AnimatedMeshGroup> BuildAnimatedMeshGroups(SkeletonMeshGroup meshGroup, in EvaluatedPose bindPoseForBounds)
        {
            var results = new List<AnimatedMeshGroup>();
            if (meshGroup.Pieces.Count == 0 || meshGroup.Vertices.Length == 0)
            {
                return results;
            }

            // Copied to a local so the local function below (ToAnimated) can capture it -
            // 'in' parameters cannot be captured by closures.
            EvaluatedPose bindPose = bindPoseForBounds;

            for (int p = 0; p < meshGroup.Pieces.Count; p++)
            {
                var piece = meshGroup.Pieces[p];
                if (piece.Corners.Length < 3) continue;

                var vertPool = piece.Mirrored && meshGroup.FlippedVertices != null
                    ? meshGroup.FlippedVertices
                    : meshGroup.Vertices;

                var outVerts = new List<AnimatedMeshVertex>();
                var outIndices = new List<int>();

                Vector3 minBounds = new(float.MaxValue);
                Vector3 maxBounds = new(float.MinValue);

                AnimatedMeshVertex ToAnimated(int vi, in MeshCorner corner)
                {
                    ref var sv = ref vertPool[vi];
                    var (boundsPos, _) = SkinVertex(sv, bindPose);
                    minBounds = Vector3.Min(minBounds, boundsPos);
                    maxBounds = Vector3.Max(maxBounds, boundsPos);

                    return new AnimatedMeshVertex(
                        sv.Position0, sv.Position1,
                        sv.Normal0, sv.Normal1,
                        sv.Weight0, sv.Weight1,
                        sv.Joint0, sv.Joint1,
                        corner.TexCoord, corner.ColorRgba);
                }

                if (piece.Topology == MeshTopology.TriangleList)
                {
                    for (int i = 0; i + 2 < piece.Corners.Length; i += 3)
                    {
                        int baseIdx = outVerts.Count;
                        for (int k = 0; k < 3; k++)
                        {
                            var corner = piece.Corners[i + k];
                            int vi = Math.Clamp(corner.VertexIndex, 0, vertPool.Length - 1);
                            outVerts.Add(ToAnimated(vi, corner));
                        }

                        outIndices.Add(baseIdx);
                        outIndices.Add(baseIdx + 1);
                        outIndices.Add(baseIdx + 2);
                    }
                }
                else if (piece.Topology == MeshTopology.TriangleStrip)
                {
                    int vertBase = outVerts.Count;
                    for (int i = 0; i < piece.Corners.Length; i++)
                    {
                        var corner = piece.Corners[i];
                        int vi = Math.Clamp(corner.VertexIndex, 0, vertPool.Length - 1);
                        outVerts.Add(ToAnimated(vi, corner));
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
                    results.Add(new AnimatedMeshGroup
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
