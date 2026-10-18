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
