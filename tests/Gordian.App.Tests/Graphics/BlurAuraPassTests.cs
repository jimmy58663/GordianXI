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
        public void ShadowGround_FitsTheSlopeAroundTheFeet_AndCapsLedges()
        {
            // #559: flat (or no collision samples) is level.
            Assert.Equal(Vector3.UnitY, EntityRenderer.ShadowGroundNormal(5f, 5f, 5f, 5f, 5f));
            Assert.Equal(Vector3.UnitY, EntityRenderer.ShadowGroundNormal(5f, null, null, null, null));
            // Ground rising 0.5 per yalm to the east tilts the normal west.
            float r = EntityRenderer.ShadowGroundSampleRadius;
            var east = EntityRenderer.ShadowGroundNormal(0f, 0.5f * r, -0.5f * r, 0f, 0f);
            Assert.Equal(Vector3.Normalize(new Vector3(-0.5f, 1f, 0f)).X, east.X, 4);
            Assert.Equal(0f, east.Z, 4);
            // A sample on a 3-yalm ledge to the north is capped at the steepest slope.
            var ledge = EntityRenderer.ShadowGroundNormal(0f, 0f, 0f, 3f, 0f);
            Assert.Equal(Vector3.Normalize(new Vector3(0f, 1f, -EntityRenderer.ShadowMaxGroundGradient)).Z, ledge.Z, 4);
            // The caster's own nudge is far smaller than the ground pull.
            Assert.True(EntityRenderer.ShadowCasterPull < EntityRenderer.ShadowEyePull / 10f);
        }

        [Theory]
        [InlineData(ActorShadowMode.High, true, true)]
        [InlineData(ActorShadowMode.High, false, true)]
        [InlineData(ActorShadowMode.Normal, true, true)]
        [InlineData(ActorShadowMode.Normal, false, false)]
        public void ShadowMode_PicksSilhouettesOrCircles(ActorShadowMode mode, bool isLocalPlayer, bool silhouette)
        {
            Assert.Equal(silhouette, EntityRenderer.CastsSilhouette(mode, isLocalPlayer));
            Assert.Null(EntityRenderer.CastsSilhouette(ActorShadowMode.Off, isLocalPlayer));
        }

        [Fact]
        public void PieceLightMultiplier_ScalesTheActorLightsAndTheirCap()
        {
            var uniform = new ZoneSceneUniform
            {
                SunColor = new Vector4(0.4f, 0.3f, 0.2f, 1f),
                MoonColor = new Vector4(0.1f, 0.1f, 0.2f, 1f),
                AmbientColor = new Vector4(0.2f, 0.2f, 0.2f, 1f),
                WeatherParams = new Vector4(0f, 0f, 0.75f, 0f)
            };
            var scaled = EntityRenderer.ScaledActorLight(uniform, 1.4f);
            Assert.Equal(0.56f, scaled.SunColor.X, 4);
            Assert.Equal(0.28f, scaled.AmbientColor.Y, 4);
            Assert.Equal(1.05f, scaled.WeatherParams.Z, 4);
            Assert.Equal(1f, scaled.AmbientColor.W);
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

        [Fact]
        public void EdgeBlur_TakesItsWidthFromTheWidestRing_AndItsOpacityFromTheMeanAlpha()
        {
            var (opacity, sigma) = EntityRenderer.AuraBlurParams(Garuda, 1440);
            Assert.Equal(11f * EntityRenderer.AuraBlurOpacityPerAlpha, opacity, 5);
            Assert.Equal(130f * EntityRenderer.AuraBlurSigmaPerRadius, sigma, 4);
            // The blur scales with the target's height.
            Assert.Equal(sigma / 2f, EntityRenderer.AuraBlurParams(Garuda, 720).SigmaPixels, 4);
        }

        [Fact]
        public void EdgeBlur_OpacityStopsAtTheMaximum()
        {
            var strong = new BlurAura(11, 7, 400, new Vector4(0.75f), new Vector4(0.5f), 50f,
                new[] { new BlurAuraRing(new Vector3(0.5f), 255, 110f) });
            Assert.Equal(EntityRenderer.AuraBlurMaxOpacity, EntityRenderer.AuraBlurParams(strong, 1080).Opacity);
        }
    }
}
