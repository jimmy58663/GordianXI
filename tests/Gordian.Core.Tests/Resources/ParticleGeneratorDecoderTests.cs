// tests/Gordian.Core.Tests/Resources/ParticleGeneratorDecoderTests.cs
using System;
using System.Buffers.Binary;
using System.Numerics;
using System.Text;
using Gordian.Core.Resources.Graphics;
using Xunit;

namespace Gordian.Core.Tests.Resources
{
    public class ParticleGeneratorDecoderTests
    {
        private static byte[] BuildSyntheticGeneratorPayload(
            ParticleAttachType attachType = ParticleAttachType.None,
            bool continuousSingleton = false,
            bool autoRun = true,
            bool batched = false,
            string linkedMeshId = "clod",
            Vector2 uvScroll = default,
            Vector3 basePos = default)
        {
            // Section 0x05 payload layout (starts at offset 16 from section start)
            // Minimum header size: 0x80 (128 bytes)
            // Stream 1 (Gen Updaters) at 0x90 (sec-relative 0xA0)
            // Stream 2 (Initializers) at 0xA0 (sec-relative 0xB0)
            // Stream 3 (Updaters) at 0xE0 (sec-relative 0xF0)
            byte[] payload = new byte[0x120];

            // Offset +0x00: Attach flags
            ushort attachFlags = (ushort)((byte)attachType & 0x0F);
            attachFlags |= (ushort)(4 << 4); // Joint 0 = 4
            attachFlags |= (ushort)(8 << 10); // Joint 1 = 8
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(0x00, 2), attachFlags);

            // Offset +0x02: Additional attach flags
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(0x02, 2), 0x0001); // attachSourceOriented

            // Offset +0x10: Actor scale params
            BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(0x10, 4), 1.5f);
            BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(0x14, 4), 2.0f);

            // Offset +0x50: UnkId + EnvironmentId
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(0x50, 4), 0x12345678);
            Encoding.ASCII.GetBytes("ev01").CopyTo(payload.AsSpan(0x54, 4));

            // Offset +0x64: Emission timing & flags
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(0x64, 2), 10); // variance
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(0x66, 2), 29); // frames = 30
            payload[0x68] = 5; // particles per emission

            byte genFlags = 0;
            if (continuousSingleton) genFlags |= 0x04;
            if (autoRun) genFlags |= 0x10;
            payload[0x69] = genFlags;

            byte moreFlags = 0;
            if (batched) moreFlags |= 0x20;
            payload[0x6B] = moreFlags;

            // Offset +0x70: 4 section offsets (relative to section start, so +16 bytes from payload index)
            uint sec1Offset = 0x90 + 16;
            uint sec2Offset = 0xA0 + 16;
            uint sec3Offset = 0xD0 + 16;
            uint sec4Offset = 0;

            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(0x70, 4), sec1Offset);
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(0x74, 4), sec2Offset);
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(0x78, 4), sec3Offset);
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(0x7C, 4), sec4Offset);

            // Stream 1 (offset 0x90): Opcode 0x0A (GeneratorCullUpdater)
            // Config: Opcode 0x0A, size = 3 dwords (12 bytes)
            uint op0AConfig = 0x0A | (3u << 8);
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(0x90, 4), op0AConfig);
            BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(0x94, 4), 500f); // maxEmitDistance

            // Stream 2 (offset 0xA0): Opcode 0x01 (StandardParticleSetup)
            // Config: Opcode 0x01, size = 10 dwords (40 bytes)
            uint op01Config = 0x01 | (10u << 8);
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(0xA0, 4), op01Config);
            // Billboard flags (+4): FollowCamera (0x0004)
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(0xA4, 2), 0x0004);
            // RenderState flags (+6): FogEnabled (bit 0x0200 = 0 means enabled)
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(0xA6, 2), 0x0000);
            // LinkedDataId (+12)
            string padMesh = (linkedMeshId + "    ").Substring(0, 4);
            Encoding.ASCII.GetBytes(padMesh).CopyTo(payload.AsSpan(0xAC, 4));
            // BasePosition (+20)
            BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(0xB4, 4), basePos.X);
            BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(0xB8, 4), basePos.Y);
            BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(0xBC, 4), basePos.Z);
            // LinkedDataType (+33)
            payload[0xC1] = (byte)ParticleLinkedDataType.StaticMesh;
            // MaxLifeSpan (+34)
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(0xC2, 2), 300);

            // Stream 3 (offset 0xD0):
            // Opcode 0x27 (UVScroll X) size 2 dwords (8 bytes)
            uint op27Config = 0x27 | (2u << 8);
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(0xD0, 4), op27Config);
            BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(0xD4, 4), uvScroll.X);

            // Opcode 0x28 (UVScroll Y) size 2 dwords (8 bytes)
            uint op28Config = 0x28 | (2u << 8);
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(0xD8, 4), op28Config);
            BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(0xDC, 4), uvScroll.Y);

            return payload;
        }

        [Fact]
        public void DecodeGenerator_HeaderAndFlags_DecodesAccurately()
        {
            var payload = BuildSyntheticGeneratorPayload(
                attachType: ParticleAttachType.Sun,
                continuousSingleton: true,
                autoRun: true,
                batched: true
            );

            var gen = ParticleGeneratorDecoder.DecodeGenerator(payload, "GEN1");

            Assert.NotNull(gen);
            Assert.Equal("GEN1", gen.DatId);
            Assert.Equal(ParticleAttachType.Sun, gen.AttachType);
            Assert.Equal(4, gen.AttachedJoint0);
            Assert.Equal(8, gen.AttachedJoint1);
            Assert.True(gen.AttachSourceOriented);
            Assert.Equal(1.5f, gen.ScalePositionAmount);
            Assert.Equal(2.0f, gen.ScaleSizeAmount);
            Assert.Equal("ev01", gen.EnvironmentId);
            Assert.True(gen.ContinuousSingleton);
            Assert.True(gen.AutoRun);
            Assert.True(gen.Batched);
            Assert.True(gen.IsCelestial);
        }

        [Fact]
        public void DecodeGenerator_StandardSetupAndUVScroll_ExtractsCorrectly()
        {
            var payload = BuildSyntheticGeneratorPayload(
                attachType: ParticleAttachType.None,
                linkedMeshId: "clod",
                uvScroll: new Vector2(0.005f, -0.002f),
                basePos: new Vector3(10f, 20f, 30f)
            );

            var gen = ParticleGeneratorDecoder.DecodeGenerator(payload, "CLD1");

            Assert.NotNull(gen);
            Assert.NotNull(gen.Setup);
            Assert.Equal("clod", gen.Setup.LinkedDataId);
            Assert.Equal(ParticleLinkedDataType.StaticMesh, gen.Setup.LinkedDataType);
            Assert.True(gen.Setup.FollowCamera);
            Assert.True(gen.Setup.FogEnabled);
            Assert.Equal(new Vector3(10f, 20f, 30f), gen.Setup.BasePosition);

            // UV Scroll
            Assert.Equal(0.005f, gen.UVScrollVelocity.X, 0.0001f);
            Assert.Equal(-0.002f, gen.UVScrollVelocity.Y, 0.0001f);

            // Cull distance from Section 1
            Assert.Equal(500f, gen.MaxDrawDistance);
        }

        [Fact]
        public void DecodeGenerator_CelestialDetection_DetectsSunMoonAndMeshPrefixes()
        {
            // By AttachType
            var sunGen = ParticleGeneratorDecoder.DecodeGenerator(
                BuildSyntheticGeneratorPayload(attachType: ParticleAttachType.Sun), "SUN_");
            Assert.True(sunGen!.IsCelestial);

            var moonGen = ParticleGeneratorDecoder.DecodeGenerator(
                BuildSyntheticGeneratorPayload(attachType: ParticleAttachType.Moon), "MOON");
            Assert.True(moonGen!.IsCelestial);

            // By LinkedDataId
            var sphereGen = ParticleGeneratorDecoder.DecodeGenerator(
                BuildSyntheticGeneratorPayload(attachType: ParticleAttachType.None, linkedMeshId: "star"), "STAR");
            Assert.True(sphereGen!.IsCelestial);

            // Non-celestial cloud
            var cloudGen = ParticleGeneratorDecoder.DecodeGenerator(
                BuildSyntheticGeneratorPayload(attachType: ParticleAttachType.None, linkedMeshId: "clod"), "CLOD");
            Assert.False(cloudGen!.IsCelestial);
        }

        private static byte[] BuildGeneratorWithStreams(byte attachType, (byte Op, ushort Alloc, byte[] Args)[] initializers, (byte Op, ushort Alloc, byte[] Args)[] updaters)
        {
            var payload = new byte[0x200];
            payload[0] = attachType;
            int offset = 0x80;
            for (int stream = 0; stream < 2; stream++)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(0x74 + stream * 4, 4), (uint)(offset + 16));
                foreach (var (op, alloc, args) in stream == 0 ? initializers : updaters)
                {
                    int dwords = 1 + args.Length / 4;
                    BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(offset, 4), op | ((uint)dwords << 8) | ((uint)alloc << 13));
                    args.CopyTo(payload.AsSpan(offset + 4));
                    offset += dwords * 4;
                }
                offset += 4; // opcode 0x00 terminator
            }
            return payload;
        }

        private static byte[] Floats(params float[] values)
        {
            var bytes = new byte[values.Length * 4];
            for (int i = 0; i < values.Length; i++) BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(i * 4), values[i]);
            return bytes;
        }

        private static byte[] KeyFrameLinkArgs(string datId) => [0, 0, 0, 0, .. Encoding.ASCII.GetBytes(datId), 0, 0, 0, 0];

        [Fact]
        public void DecodeGenerator_CelestialOpcodes_DecodeRotationClockCurveAndTints()
        {
            byte[] dayColors = new byte[4 + 8 * 4];
            for (int i = 0; i < 8; i++) dayColors[4 + i * 4] = (byte)(0x10 * (i + 1));
            byte[] phaseColors = new byte[4 + 12 * 4];
            phaseColors[4 + 6 * 4 + 3] = 0x80; // full-moon alpha

            var payload = BuildGeneratorWithStreams(
                (byte)ParticleAttachType.Moon,
                initializers:
                [
                    (0x09, 0, Floats(0f, -0.785f, 0f)),
                    (0x60, 3, KeyFrameLinkArgs("kaaa")),
                    (0x63, 5, KeyFrameLinkArgs("ksta")),
                    (0x16, 0, [0x62, 0x62, 0x62, 0x80]),
                ],
                updaters:
                [
                    (0x3F, 5, []),
                    (0x45, 0, []),
                    (0x4E, 0, dayColors),
                    (0x4F, 0, phaseColors),
                ]);

            var gen = ParticleGeneratorDecoder.DecodeGenerator(payload, "moon");

            Assert.NotNull(gen);
            Assert.Equal(-0.785f, gen.Rotation.Y, 4);
            Assert.Equal("ksta", gen.ClockAlphaKeyFrameId);
            Assert.Equal(0x62 / 255f, gen.BaseColor.X, 4);
            Assert.Equal(0x80 / 255f, gen.BaseColor.W, 4);
            Assert.True(gen.SpriteIndexFromMoonPhase);
            Assert.Equal(8, gen.DayOfWeekColors!.Length);
            Assert.Equal(0x10 / 255f, gen.DayOfWeekColors[0].X, 4);
            Assert.Equal(0x80 / 255f, gen.DayOfWeekColors[7].X, 4);
            Assert.Equal(12, gen.MoonPhaseColors!.Length);
            Assert.Equal(0x80 / 255f, gen.MoonPhaseColors[6].W, 4);
            Assert.Equal(0f, gen.MoonPhaseColors[5].W);
        }

        [Theory]
        [InlineData(0x48, ParticleBlendFunc.SrcOneAdd)]
        [InlineData(0x44, ParticleBlendFunc.SrcInvSrcAdd)]
        [InlineData(0x42, ParticleBlendFunc.SrcOneRevSub)]
        [InlineData(0x46, ParticleBlendFunc.ZeroInvSrcAdd)]
        [InlineData(0x58, ParticleBlendFunc.OneZero)]
        public void DecodeGenerator_BlendFuncInitializer_MapsBlendFunctions(byte flags, ParticleBlendFunc expected)
        {
            var payload = BuildGeneratorWithStreams(0, initializers: [(0x1E, 0, [flags, 0, 0, 0])], updaters: []);

            Assert.Equal(expected, ParticleGeneratorDecoder.DecodeGenerator(payload, "cld1")!.BlendFunc);
        }

        [Fact]
        public void DecodeGenerator_NoBlendOpcode_DefaultsToAdditive()
        {
            var payload = BuildGeneratorWithStreams(0, initializers: [(0x09, 0, Floats(0f, 0f, 0f))], updaters: []);

            Assert.Equal(ParticleBlendFunc.SrcOneAdd, ParticleGeneratorDecoder.DecodeGenerator(payload, "kasa")!.BlendFunc);
        }

        [Fact]
        public void DecodeGenerator_CloudOpcodes_DecodePriorityRotationVelocityAndClockCurves()
        {
            var payload = BuildGeneratorWithStreams(
                0,
                initializers:
                [
                    (0x30, 0, Floats(40000f)),
                    (0x0B, 6, Floats(0f, 0.00017453f, 0f)),
                    (0x60, 4, KeyFrameLinkArgs("kcr1")),
                    (0x61, 2, KeyFrameLinkArgs("kcg1")),
                    (0x62, 0, KeyFrameLinkArgs("kcb1")),
                    (0x96, 8, KeyFrameLinkArgs("k007")),
                ],
                updaters:
                [
                    (0x05, 6, []),
                    (0x3C, 4, []),
                    (0x3D, 2, []),
                    (0x3E, 0, []),
                    (0x6C, 8, []),
                ]);

            var gen = ParticleGeneratorDecoder.DecodeGenerator(payload, "cld1");

            Assert.NotNull(gen);
            Assert.Equal(40000f, gen.ProjectionBias);
            Assert.Equal(0.00017453f, gen.RotationVelocity.Y, 6);
            Assert.Equal(new[] { "kcr1", "kcg1", "kcb1" }, gen.ClockColorKeyFrameIds);
            Assert.Equal(new[] { null, "k007", null }, gen.ClockPositionKeyFrameIds);
        }

        [Fact]
        public void DecodeGenerator_UndersizedPayload_ReturnsNull()
        {
            Assert.Null(ParticleGeneratorDecoder.DecodeGenerator(ReadOnlySpan<byte>.Empty, "NIL"));
            Assert.Null(ParticleGeneratorDecoder.DecodeGenerator(new byte[100], "SHORT"));
        }
    }
}
