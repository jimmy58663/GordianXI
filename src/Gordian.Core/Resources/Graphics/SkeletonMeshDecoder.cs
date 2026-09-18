// src/Gordian.Core/Resources/Graphics/SkeletonMeshDecoder.cs
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Numerics;
using System.Text;
using Gordian.Core.Resources.Models;

namespace Gordian.Core.Resources.Graphics
{
    /// <summary>
    /// Clean-room binary decoder for FFXI Section 0x2A SkeletonMesh resources.
    /// Decodes single and double-joint skinned vertices, vertex-joint bindings, and primitive triangle instruction streams.
    /// Derived from community research in xi-model-viewer (https://github.com/vekien/xi-model-viewer) and xim.
    /// </summary>
    public static class SkeletonMeshDecoder
    {
        private readonly record struct JointRef(int Index, int FlippedIndex, int FlipAxis);

        private static JointRef UnpackJointRef(ushort data)
        {
            return new JointRef(
                Index: data & 0x7F,
                FlippedIndex: (data >> 7) & 0x7F,
                FlipAxis: (data >> 14) & 0x03
            );
        }

        private static Vector3 FlipVec(Vector3 v, int axis)
        {
            return axis switch
            {
                1 => new Vector3(-v.X, v.Y, v.Z),
                2 => new Vector3(v.X, -v.Y, v.Z),
                3 => new Vector3(v.X, v.Y, -v.Z),
                _ => v
            };
        }

        /// <summary>
        /// Decodes a Section 0x2A skeleton mesh payload into a structured SkeletonMeshGroup.
        /// </summary>
        public static SkeletonMeshGroup? DecodeMesh(ReadOnlySpan<byte> payload, string sourcePath = "")
        {
            if (payload.Length < 42) return null;

            byte flags3 = payload[2];
            bool clothEffect = (flags3 & 0x01) != 0;
            bool useJointArray = (flags3 & 0x80) != 0;
            bool hasNormals = !clothEffect;
            byte occludeType = payload[3];
            bool symmetric = payload[4] == 0x01;

            int instructionOffset = 2 * BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(6, 4));
            int jointArrayOffset = 2 * BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(12, 4));
            ushort numJoints = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(16, 2));

            int vertexCountsOffset = 2 * BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(18, 4));
            ushort numVertexCounts = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(22, 2));

            int vertexJointMappingOffset = 2 * BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(24, 4));
            int vertexDataOffset = 2 * BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(30, 4));

            // Joint Array mapping
            ushort[] jointArray = Array.Empty<ushort>();
            if (numJoints > 0 && jointArrayOffset >= 0 && jointArrayOffset + (numJoints * 2) <= payload.Length)
            {
                jointArray = new ushort[numJoints];
                for (int i = 0; i < numJoints; i++)
                {
                    jointArray[i] = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(jointArrayOffset + (i * 2), 2));
                }
            }

            int MapJoint(int idx)
            {
                if (useJointArray)
                {
                    return (idx >= 0 && idx < jointArray.Length) ? jointArray[idx] : 0;
                }
                return idx;
            }

            // Vertex counts
            if (vertexCountsOffset < 0 || vertexCountsOffset + 4 > payload.Length || numVertexCounts != 2)
            {
                return null;
            }

            ushort singleCount = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(vertexCountsOffset, 2));
            ushort doubleCount = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(vertexCountsOffset + 2, 2));
            int totalVertices = singleCount + doubleCount;
            if (totalVertices <= 0) return null;

            var vertices = new SkinnedVertex[totalVertices];
            var refs0 = new JointRef[totalVertices];
            var refs1 = new JointRef[totalVertices];

            // Vertex joint mappings
            if (vertexJointMappingOffset >= 0 && vertexJointMappingOffset + (totalVertices * 4) <= payload.Length)
            {
                int mapPos = vertexJointMappingOffset;
                for (int i = 0; i < singleCount; i++)
                {
                    refs0[i] = UnpackJointRef(BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(mapPos, 2)));
                    refs1[i] = UnpackJointRef(BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(mapPos + 2, 2)));
                    mapPos += 4;

                    vertices[i].Joint0 = MapJoint(refs0[i].Index);
                    vertices[i].Joint1 = -1;
                    vertices[i].Weight0 = 1.0f;
                    vertices[i].Weight1 = 0.0f;
                }

                for (int i = singleCount; i < totalVertices; i++)
                {
                    refs0[i] = UnpackJointRef(BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(mapPos, 2)));
                    refs1[i] = UnpackJointRef(BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(mapPos + 2, 2)));
                    mapPos += 4;

                    vertices[i].Joint0 = MapJoint(refs0[i].Index);
                    vertices[i].Joint1 = MapJoint(refs1[i].Index);
                }
            }

            // Vertex coordinates
            if (vertexDataOffset >= 0 && vertexDataOffset < payload.Length)
            {
                int dataPos = vertexDataOffset;
                for (int i = 0; i < singleCount && dataPos + (hasNormals ? 24 : 12) <= payload.Length; i++)
                {
                    float px = BinaryPrimitives.ReadSingleLittleEndian(payload.Slice(dataPos, 4));
                    float py = BinaryPrimitives.ReadSingleLittleEndian(payload.Slice(dataPos + 4, 4));
                    float pz = BinaryPrimitives.ReadSingleLittleEndian(payload.Slice(dataPos + 8, 4));
                    dataPos += 12;
                    vertices[i].Position0 = new Vector3(px, py, pz);

                    if (hasNormals)
                    {
                        float nx = BinaryPrimitives.ReadSingleLittleEndian(payload.Slice(dataPos, 4));
                        float ny = BinaryPrimitives.ReadSingleLittleEndian(payload.Slice(dataPos + 4, 4));
                        float nz = BinaryPrimitives.ReadSingleLittleEndian(payload.Slice(dataPos + 8, 4));
                        dataPos += 12;
                        vertices[i].Normal0 = new Vector3(nx, ny, nz);
                    }
                }

                for (int i = singleCount; i < totalVertices && dataPos + (hasNormals ? 56 : 32) <= payload.Length; i++)
                {
                    float p0x = BinaryPrimitives.ReadSingleLittleEndian(payload.Slice(dataPos, 4));
                    float p1x = BinaryPrimitives.ReadSingleLittleEndian(payload.Slice(dataPos + 4, 4));
                    float p0y = BinaryPrimitives.ReadSingleLittleEndian(payload.Slice(dataPos + 8, 4));
                    float p1y = BinaryPrimitives.ReadSingleLittleEndian(payload.Slice(dataPos + 12, 4));
                    float p0z = BinaryPrimitives.ReadSingleLittleEndian(payload.Slice(dataPos + 16, 4));
                    float p1z = BinaryPrimitives.ReadSingleLittleEndian(payload.Slice(dataPos + 20, 4));
                    float w0 = BinaryPrimitives.ReadSingleLittleEndian(payload.Slice(dataPos + 24, 4));
                    float w1 = BinaryPrimitives.ReadSingleLittleEndian(payload.Slice(dataPos + 28, 4));
                    dataPos += 32;

                    vertices[i].Position0 = new Vector3(p0x, p0y, p0z);
                    vertices[i].Position1 = new Vector3(p1x, p1y, p1z);
                    vertices[i].Weight0 = w0;
                    vertices[i].Weight1 = w1;

                    if (hasNormals)
                    {
                        float n0x = BinaryPrimitives.ReadSingleLittleEndian(payload.Slice(dataPos, 4));
                        float n1x = BinaryPrimitives.ReadSingleLittleEndian(payload.Slice(dataPos + 4, 4));
                        float n0y = BinaryPrimitives.ReadSingleLittleEndian(payload.Slice(dataPos + 8, 4));
                        float n1y = BinaryPrimitives.ReadSingleLittleEndian(payload.Slice(dataPos + 12, 4));
                        float n0z = BinaryPrimitives.ReadSingleLittleEndian(payload.Slice(dataPos + 16, 4));
                        float n1z = BinaryPrimitives.ReadSingleLittleEndian(payload.Slice(dataPos + 20, 4));
                        dataPos += 24;

                        vertices[i].Normal0 = new Vector3(n0x, n0y, n0z);
                        vertices[i].Normal1 = new Vector3(n1x, n1y, n1z);
                    }
                }
            }

            // Flipped vertex pool for symmetric meshes
            SkinnedVertex[]? flippedVertices = null;
            if (symmetric)
            {
                flippedVertices = new SkinnedVertex[totalVertices];
                for (int i = 0; i < totalVertices; i++)
                {
                    ref var src = ref vertices[i];
                    flippedVertices[i] = new SkinnedVertex
                    {
                        Position0 = FlipVec(src.Position0, refs0[i].FlipAxis),
                        Position1 = FlipVec(src.Position1, refs1[i].FlipAxis),
                        Normal0 = FlipVec(src.Normal0, refs0[i].FlipAxis),
                        Normal1 = FlipVec(src.Normal1, refs1[i].FlipAxis),
                        Weight0 = src.Weight0,
                        Weight1 = src.Weight1,
                        Joint0 = MapJoint(refs0[i].FlippedIndex),
                        Joint1 = src.Joint1 == -1 ? -1 : MapJoint(refs1[i].FlippedIndex)
                    };
                }
            }

            var group = new SkeletonMeshGroup
            {
                Vertices = vertices,
                FlippedVertices = flippedVertices,
                Symmetric = symmetric,
                OccludeType = occludeType,
                SourcePath = sourcePath
            };

            // Instruction stream parsing
            if (instructionOffset >= 0 && instructionOffset < payload.Length)
            {
                int insPos = instructionOffset;
                string currentTexture = string.Empty;

                while (insPos + 2 <= payload.Length)
                {
                    ushort op = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(insPos, 2));
                    insPos += 2;

                    if (op == 0xFFFF)
                    {
                        break;
                    }

                    switch (op)
                    {
                        case 0x8000: // Texture name
                            if (insPos + 16 <= payload.Length)
                            {
                                currentTexture = ReadCString(payload.Slice(insPos, 16));
                                insPos += 16;
                            }
                            break;

                        case 0x8010: // Render props (44 bytes: tFactor, f0, f1, flags, ambientMultiplier, unk, specular)
                            if (insPos + 44 <= payload.Length)
                            {
                                insPos += 44;
                            }
                            break;

                        case 0x5453: // Textured Triangle Strip
                            if (insPos + 2 <= payload.Length)
                            {
                                ushort numTris = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(insPos, 2));
                                insPos += 2;

                                int cornerCount = numTris + 2;
                                int bytesNeeded = (3 * 2) + (6 * 4) + ((numTris - 1) * (2 + 8));
                                if (insPos + bytesNeeded <= payload.Length)
                                {
                                    var corners = new MeshCorner[cornerCount];
                                    ushort i0 = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(insPos, 2));
                                    ushort i1 = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(insPos + 2, 2));
                                    ushort i2 = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(insPos + 4, 2));
                                    insPos += 6;

                                    float u0 = BinaryPrimitives.ReadSingleLittleEndian(payload.Slice(insPos, 4));
                                    float v0 = BinaryPrimitives.ReadSingleLittleEndian(payload.Slice(insPos + 4, 4));
                                    float u1 = BinaryPrimitives.ReadSingleLittleEndian(payload.Slice(insPos + 8, 4));
                                    float v1 = BinaryPrimitives.ReadSingleLittleEndian(payload.Slice(insPos + 12, 4));
                                    float u2 = BinaryPrimitives.ReadSingleLittleEndian(payload.Slice(insPos + 16, 4));
                                    float v2 = BinaryPrimitives.ReadSingleLittleEndian(payload.Slice(insPos + 20, 4));
                                    insPos += 24;

                                    corners[0] = new MeshCorner(i0, new Vector2(u0, v0), 0xFF808080);
                                    corners[1] = new MeshCorner(i1, new Vector2(u1, v1), 0xFF808080);
                                    corners[2] = new MeshCorner(i2, new Vector2(u2, v2), 0xFF808080);

                                    for (int t = 1; t < numTris; t++)
                                    {
                                        ushort vi = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(insPos, 2));
                                        float u = BinaryPrimitives.ReadSingleLittleEndian(payload.Slice(insPos + 2, 4));
                                        float v = BinaryPrimitives.ReadSingleLittleEndian(payload.Slice(insPos + 6, 4));
                                        insPos += 10;
                                        corners[t + 2] = new MeshCorner(vi, new Vector2(u, v), 0xFF808080);
                                    }

                                    group.Pieces.Add(new SkeletonMeshPiece
                                    {
                                        Topology = MeshTopology.TriangleStrip,
                                        Corners = corners,
                                        TextureName = currentTexture,
                                        Mirrored = false
                                    });

                                    if (symmetric)
                                    {
                                        group.Pieces.Add(new SkeletonMeshPiece
                                        {
                                            Topology = MeshTopology.TriangleStrip,
                                            Corners = corners,
                                            TextureName = currentTexture,
                                            Mirrored = true
                                        });
                                    }
                                }
                            }
                            break;

                        case 0x0054: // Textured Triangle Mesh (List)
                            if (insPos + 2 <= payload.Length)
                            {
                                ushort numTris = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(insPos, 2));
                                insPos += 2;

                                int cornerCount = numTris * 3;
                                int bytesNeeded = numTris * ((3 * 2) + (6 * 4));
                                if (insPos + bytesNeeded <= payload.Length)
                                {
                                    var corners = new MeshCorner[cornerCount];
                                    for (int t = 0; t < numTris; t++)
                                    {
                                        ushort i0 = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(insPos, 2));
                                        ushort i1 = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(insPos + 2, 2));
                                        ushort i2 = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(insPos + 4, 2));
                                        insPos += 6;

                                        float u0 = BinaryPrimitives.ReadSingleLittleEndian(payload.Slice(insPos, 4));
                                        float v0 = BinaryPrimitives.ReadSingleLittleEndian(payload.Slice(insPos + 4, 4));
                                        float u1 = BinaryPrimitives.ReadSingleLittleEndian(payload.Slice(insPos + 8, 4));
                                        float v1 = BinaryPrimitives.ReadSingleLittleEndian(payload.Slice(insPos + 12, 4));
                                        float u2 = BinaryPrimitives.ReadSingleLittleEndian(payload.Slice(insPos + 16, 4));
                                        float v2 = BinaryPrimitives.ReadSingleLittleEndian(payload.Slice(insPos + 20, 4));
                                        insPos += 24;

                                        corners[t * 3 + 0] = new MeshCorner(i0, new Vector2(u0, v0), 0xFF808080);
                                        corners[t * 3 + 1] = new MeshCorner(i1, new Vector2(u1, v1), 0xFF808080);
                                        corners[t * 3 + 2] = new MeshCorner(i2, new Vector2(u2, v2), 0xFF808080);
                                    }

                                    group.Pieces.Add(new SkeletonMeshPiece
                                    {
                                        Topology = MeshTopology.TriangleList,
                                        Corners = corners,
                                        TextureName = currentTexture,
                                        Mirrored = false
                                    });

                                    if (symmetric)
                                    {
                                        group.Pieces.Add(new SkeletonMeshPiece
                                        {
                                            Topology = MeshTopology.TriangleList,
                                            Corners = corners,
                                            TextureName = currentTexture,
                                            Mirrored = true
                                        });
                                    }
                                }
                            }
                            break;

                        case 0x0043: // Untextured Triangle Mesh (per-triangle BGRA)
                            if (insPos + 2 <= payload.Length)
                            {
                                ushort numTris = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(insPos, 2));
                                insPos += 2;

                                int cornerCount = numTris * 3;
                                int bytesNeeded = numTris * ((3 * 2) + 4);
                                if (insPos + bytesNeeded <= payload.Length)
                                {
                                    var corners = new MeshCorner[cornerCount];
                                    for (int t = 0; t < numTris; t++)
                                    {
                                        ushort i0 = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(insPos, 2));
                                        ushort i1 = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(insPos + 2, 2));
                                        ushort i2 = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(insPos + 4, 2));
                                        uint color = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(insPos + 6, 4));
                                        insPos += 10;

                                        corners[t * 3 + 0] = new MeshCorner(i0, Vector2.Zero, color);
                                        corners[t * 3 + 1] = new MeshCorner(i1, Vector2.Zero, color);
                                        corners[t * 3 + 2] = new MeshCorner(i2, Vector2.Zero, color);
                                    }

                                    group.Pieces.Add(new SkeletonMeshPiece
                                    {
                                        Topology = MeshTopology.TriangleList,
                                        Corners = corners,
                                        TextureName = string.Empty,
                                        Mirrored = false
                                    });

                                    if (symmetric)
                                    {
                                        group.Pieces.Add(new SkeletonMeshPiece
                                        {
                                            Topology = MeshTopology.TriangleList,
                                            Corners = corners,
                                            TextureName = string.Empty,
                                            Mirrored = true
                                        });
                                    }
                                }
                            }
                            break;

                        case 0x4353: // Untextured Triangle Strip
                            if (insPos + 2 <= payload.Length)
                            {
                                ushort numTris = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(insPos, 2));
                                insPos += 2;

                                int cornerCount = numTris + 2;
                                int bytesNeeded = (3 * 2) + 4 + ((numTris - 1) * 2);
                                if (insPos + bytesNeeded <= payload.Length)
                                {
                                    var corners = new MeshCorner[cornerCount];
                                    ushort i0 = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(insPos, 2));
                                    ushort i1 = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(insPos + 2, 2));
                                    ushort i2 = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(insPos + 4, 2));
                                    uint color = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(insPos + 6, 4));
                                    insPos += 10;

                                    corners[0] = new MeshCorner(i0, Vector2.Zero, color);
                                    corners[1] = new MeshCorner(i1, Vector2.Zero, color);
                                    corners[2] = new MeshCorner(i2, Vector2.Zero, color);

                                    for (int t = 1; t < numTris; t++)
                                    {
                                        ushort vi = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(insPos, 2));
                                        insPos += 2;
                                        corners[t + 2] = new MeshCorner(vi, Vector2.Zero, color);
                                    }

                                    group.Pieces.Add(new SkeletonMeshPiece
                                    {
                                        Topology = MeshTopology.TriangleStrip,
                                        Corners = corners,
                                        TextureName = string.Empty,
                                        Mirrored = false
                                    });

                                    if (symmetric)
                                    {
                                        group.Pieces.Add(new SkeletonMeshPiece
                                        {
                                            Topology = MeshTopology.TriangleStrip,
                                            Corners = corners,
                                            TextureName = string.Empty,
                                            Mirrored = true
                                        });
                                    }
                                }
                            }
                            break;

                        default:
                            // Unknown op - break to prevent desync
                            goto DoneInstructions;
                    }
                }
            }

        DoneInstructions:
            return group;
        }

        private static string ReadCString(ReadOnlySpan<byte> span)
        {
            int end = 0;
            while (end < span.Length && span[end] != 0)
            {
                end++;
            }
            return Encoding.ASCII.GetString(span.Slice(0, end)).Trim();
        }
    }
}
