// tests/Gordian.App.Tests/Graphics/BlurAuraPassTests.cs
using System;
using System.Numerics;
using Gordian.App.Graphics;
using Gordian.Core.Resources.Models;
using Xunit;

namespace Gordian.App.Tests.Graphics
{
    public class BlurAuraPassTests
    {
        private static readonly BlurAura Garuda = new(11, 7, 400, new Vector4(0.75f), new Vector4(0.5f), 50f,
            new[] { new BlurAuraRing(new Vector3(0.5f), 12, 60f), new BlurAuraRing(new Vector3(0.5f), 10, 130f) });

        [Fact]
        public void RingParams_WidthFollowsTheRadius_AndTheRimFollowsTheFalloff()
        {
            var inner = EntityRenderer.AuraRingParams(Garuda, 0, 0f);
            var outer = EntityRenderer.AuraRingParams(Garuda, 1, 0f);

            Assert.Equal(60f * EntityRenderer.AuraWidthPerRadius, inner.Z, 6);
            Assert.Equal(130f * EntityRenderer.AuraWidthPerRadius, outer.Z, 6);
            Assert.Equal(400f * EntityRenderer.AuraRimPerFalloff, inner.W, 5);
        }

        [Fact]
        public void ShadowLight_UsesTheSunWhileUp_TheMoonWhileDown_AndNeverDropsBelowTheMinimumElevation()
        {
            var noon = EntityRenderer.ShadowLightDirection(new Vector3(0f, 1f, 0f));
            Assert.Equal(Vector3.UnitY, noon);

            // 16:26: the sun at elevation 0.40 toward +X is raised to the minimum, keeping its bearing.
            var late = EntityRenderer.ShadowLightDirection(Vector3.Normalize(new Vector3(0.917f, 0.399f, 0f)));
            Assert.Equal(EntityRenderer.ShadowMinElevation, late.Y, 4);
            Assert.True(late.X > 0f);
            Assert.Equal(1f, late.Length(), 4);

            // Night: the sun is below the horizon, so the shadow follows the moon opposite it.
            var night = EntityRenderer.ShadowLightDirection(Vector3.Normalize(new Vector3(0.5f, -0.8f, 0f)));
            Assert.True(night.Y >= EntityRenderer.ShadowMinElevation);
            Assert.True(night.X < 0f);
        }

        [Fact]
        public void RingParams_DriftStaysWithinTheGhostOffset_AndMovesOverTime()
        {
            var a = EntityRenderer.AuraRingParams(Garuda, 0, 0f);
            var b = EntityRenderer.AuraRingParams(Garuda, 0, 0.5f);

            foreach (var p in new[] { a, b })
            {
                Assert.InRange(MathF.Abs(p.X), 0f, EntityRenderer.AuraGhostOffset + 1e-6f);
                Assert.InRange(MathF.Abs(p.Y), 0f, EntityRenderer.AuraGhostOffset + 1e-6f);
            }
            Assert.NotEqual(new Vector2(a.X, a.Y), new Vector2(b.X, b.Y));
        }
    }
}
