// tests/Gordian.Core.Tests/Resources/ParticleKeyFrameDecoderTests.cs
using System;
using System.Buffers.Binary;
using Gordian.Core.Resources.Graphics;
using Xunit;

namespace Gordian.Core.Tests.Resources
{
    public class ParticleKeyFrameDecoderTests
    {
        private static byte[] BuildSyntheticKeyFramePayload(params (float Time, float Value)[] points)
        {
            byte[] payload = new byte[points.Length * 8];
            for (int i = 0; i < points.Length; i++)
            {
                BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(i * 8, 4), points[i].Time);
                BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(i * 8 + 4, 4), points[i].Value);
            }
            return payload;
        }

        [Fact]
        public void DecodeKeyFrame_ValidPoints_ReturnsCurveWithCorrectEntries()
        {
            var payload = BuildSyntheticKeyFramePayload(
                (0.0f, 10.0f),
                (0.5f, 50.0f),
                (1.0f, 100.0f)
            );

            var curve = ParticleKeyFrameDecoder.DecodeKeyFrame(payload, "KF01");

            Assert.NotNull(curve);
            Assert.Equal("KF01", curve.DatId);
            Assert.Equal(3, curve.Entries.Count);
            Assert.Equal(0.0f, curve.Entries[0].Time);
            Assert.Equal(10.0f, curve.Entries[0].Value);
            Assert.Equal(0.5f, curve.Entries[1].Time);
            Assert.Equal(50.0f, curve.Entries[1].Value);
            Assert.Equal(1.0f, curve.Entries[2].Time);
            Assert.Equal(100.0f, curve.Entries[2].Value);
        }

        [Fact]
        public void Evaluate_PiecewiseLinearInterpolation_ComputesAccurateValues()
        {
            var payload = BuildSyntheticKeyFramePayload(
                (0.0f, 0.0f),
                (0.5f, 100.0f),
                (1.0f, 200.0f)
            );

            var curve = ParticleKeyFrameDecoder.DecodeKeyFrame(payload, "TEST")!;

            Assert.Equal(0.0f, curve.Evaluate(0.0f), 0.001f);
            Assert.Equal(50.0f, curve.Evaluate(0.25f), 0.001f);
            Assert.Equal(100.0f, curve.Evaluate(0.5f), 0.001f);
            Assert.Equal(150.0f, curve.Evaluate(0.75f), 0.001f);
            Assert.Equal(200.0f, curve.Evaluate(1.0f), 0.001f);
            Assert.Equal(200.0f, curve.Evaluate(1.5f), 0.001f); // Clamps to end
        }

        [Fact]
        public void Evaluate_WithInitialValueOverride_UsesOverrideForInitialKeyframe()
        {
            var payload = BuildSyntheticKeyFramePayload(
                (0.0f, 0.0f),
                (1.0f, 100.0f)
            );

            var curve = ParticleKeyFrameDecoder.DecodeKeyFrame(payload, "OVER")!;

            // Without override: (0.0, 0.0) -> (1.0, 100.0), at 0.5 = 50.0
            Assert.Equal(50.0f, curve.Evaluate(0.5f), 0.001f);

            // With override of 20.0 for initial value: (0.0, 20.0) -> (1.0, 100.0), at 0.5 = 60.0
            Assert.Equal(60.0f, curve.Evaluate(0.5f, initialValueOverride: 20.0f), 0.001f);
            Assert.Equal(20.0f, curve.Evaluate(0.0f, initialValueOverride: 20.0f), 0.001f);
        }

        [Fact]
        public void DecodeKeyFrame_TerminatesAtTime1_IgnoresExtraData()
        {
            var payload = BuildSyntheticKeyFramePayload(
                (0.0f, 5.0f),
                (1.0f, 25.0f),
                (1.5f, 999.0f) // Should be ignored because time >= 1.0 terminated reading
            );

            var curve = ParticleKeyFrameDecoder.DecodeKeyFrame(payload, "TERM");

            Assert.NotNull(curve);
            Assert.Equal(2, curve.Entries.Count);
            Assert.Equal(1.0f, curve.Entries[^1].Time);
            Assert.Equal(25.0f, curve.Entries[^1].Value);
        }

        [Fact]
        public void DecodeKeyFrame_EmptyOrUndersized_ReturnsNull()
        {
            Assert.Null(ParticleKeyFrameDecoder.DecodeKeyFrame(ReadOnlySpan<byte>.Empty, "NOPE"));
            Assert.Null(ParticleKeyFrameDecoder.DecodeKeyFrame(new byte[7], "SHORT"));
        }
    }
}
