// tests/Gordian.Core.Tests/Resources/GearOcclusionTests.cs
using System;
using System.Buffers.Binary;
using System.Linq;
using System.Text;
using Gordian.Core.Resources;
using Gordian.Core.Resources.Containers;
using Gordian.Core.Resources.Graphics;
using Gordian.Core.Resources.Models;
using Xunit;

namespace Gordian.Core.Tests.Resources
{
    /// <summary>Gear occlusion: worn meshes' occludeTypes hide pieces by displayType (#88).</summary>
    public class GearOcclusionTests
    {
        private const int InstructionOffset = 130;

        /// <summary>
        /// A 3-vertex 0x2A mesh with one single-triangle piece per entry of <paramref name="displayTypes"/>, each preceded
        /// by a 0x8010 block carrying that displayType (null = no 0x8010 before the piece).
        /// </summary>
        private static byte[] BuildMesh(byte occludeType, params byte?[] displayTypes)
        {
            int size = InstructionOffset + displayTypes.Sum(d => (d.HasValue ? 46 : 0) + 34) + 2;
            byte[] p = new byte[size];
            p[3] = occludeType;
            BinaryPrimitives.WriteInt32LittleEndian(p.AsSpan(6, 4), InstructionOffset / 2);
            BinaryPrimitives.WriteInt32LittleEndian(p.AsSpan(18, 4), 42 / 2);  // vertex counts
            BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(22, 2), 2);
            BinaryPrimitives.WriteInt32LittleEndian(p.AsSpan(24, 4), 46 / 2);  // vertex joint mappings
            BinaryPrimitives.WriteInt32LittleEndian(p.AsSpan(30, 4), 58 / 2);  // vertex data
            BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(42, 2), 3);      // 3 single-joint vertices
            BinaryPrimitives.WriteSingleLittleEndian(p.AsSpan(58 + 24, 4), 1f);       // v1 = (1,0,0)
            BinaryPrimitives.WriteSingleLittleEndian(p.AsSpan(58 + 48 + 4, 4), 1f);   // v2 = (0,1,0)

            int pos = InstructionOffset;
            foreach (var displayType in displayTypes)
            {
                if (displayType.HasValue)
                {
                    BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(pos, 2), 0x8010);
                    p[pos + 2 + 13] = displayType.Value;
                    pos += 46;
                }
                BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(pos, 2), 0x0054);
                BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(pos + 2, 2), 1);
                BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(pos + 6, 2), 1);
                BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(pos + 8, 2), 2);
                pos += 34;
            }
            BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(pos, 2), 0xFFFF);
            return p;
        }

        private static byte[] Chunk(DatSectionType type, byte[] payload)
        {
            int units = (16 + payload.Length + 15) / 16;
            byte[] chunk = new byte[units * 16];
            Encoding.ASCII.GetBytes("test").CopyTo(chunk.AsSpan(0, 4));
            BinaryPrimitives.WriteUInt32LittleEndian(chunk.AsSpan(4, 4), (uint)type | (uint)(units << 7));
            payload.CopyTo(chunk.AsSpan(16));
            return chunk;
        }

        private static byte[] SkeletonDat()
        {
            byte[] skel = new byte[4 + 30];
            skel[2] = 1; // one joint, its own parent
            BinaryPrimitives.WriteSingleLittleEndian(skel.AsSpan(30, 4), 1f); // qw
            return Chunk(DatSectionType.Skeleton, skel);
        }

        private static int Triangles(EntityModel model) => model.AnimatedMeshGroups.Sum(g => g.Indices.Length) / 3;

        [Fact]
        public void Decoder_GivesEachPieceTheDisplayTypeOfThePreceding0x8010()
        {
            var mesh = SkeletonMeshDecoder.DecodeMesh(BuildMesh(0x11, null, 1, null, 7));

            Assert.NotNull(mesh);
            Assert.Equal(0x11, mesh!.OccludeType);
            Assert.Equal(new byte[] { 0, 1, 1, 7 }, mesh.Pieces.Select(p => p.DisplayType).ToArray());
        }

        [Theory]
        [InlineData(1, 0x02, true)]
        [InlineData(1, 0x06, true)]
        [InlineData(1, 0x01, false)]
        [InlineData(2, 0x03, false)]
        [InlineData(3, 0x04, true)]
        [InlineData(4, 0x04, false)]
        [InlineData(4, 0x05, true)]
        [InlineData(5, 0x12, true)]
        [InlineData(5, 0x11, false)]
        [InlineData(6, 0x32, true)]
        [InlineData(6, 0x31, false)]
        [InlineData(7, 0x22, true)]
        [InlineData(7, 0x21, false)]
        [InlineData(0, 0x05, false)]
        public void Hides_FollowsTheRuleTable(byte displayType, byte occludeType, bool hidden)
        {
            var occlusion = new GearOcclusion();
            occlusion.Add(occludeType);

            Assert.Equal(hidden, occlusion.Hides(displayType));
        }

        [Fact]
        public void AssembleModel_WithOcclusion_DropsHairUnderAFullHelm()
        {
            byte[] face = Chunk(DatSectionType.SkeletonMesh, BuildMesh(0x00, 1, 4, 0)); // hair, face, neck
            byte[] helm = Chunk(DatSectionType.SkeletonMesh, BuildMesh(0x05, 0));       // full helm
            var parts = new[] { (ReadOnlyMemory<byte>)face, helm };

            var plain = EntityModelLoader.AssembleModel(SkeletonDat(), parts, "plain");
            var occluded = EntityModelLoader.AssembleModel(SkeletonDat(), parts, "occluded", gearOcclusion: new GearOcclusion());

            Assert.Equal(4, Triangles(plain));
            Assert.Equal(2, Triangles(occluded)); // neck + helm
        }

        [Fact]
        public void AssembleModel_SeededOcclusion_CountsAnUndrawnPiece()
        {
            // A stowed ranged weapon is not drawn but its occludeType still counts.
            byte[] body = Chunk(DatSectionType.SkeletonMesh, BuildMesh(0x11, 0, 5)); // torso, wrists
            var seeded = new GearOcclusion();
            seeded.Add(0x12);

            var model = EntityModelLoader.AssembleModel(SkeletonDat(), new[] { (ReadOnlyMemory<byte>)body }, "seeded", gearOcclusion: seeded);

            Assert.Equal(1, Triangles(model));
        }
    }
}
