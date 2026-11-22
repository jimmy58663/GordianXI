// tests/Gordian.App.Tests/Graphics/EffectDrawOrderTests.cs
using System.Collections.Generic;
using Gordian.App.Graphics;
using Gordian.Core.Resources.Graphics;
using Xunit;

namespace Gordian.App.Tests.Graphics
{
    /// <summary>
    /// The zone effect pass's order (#458): depth writers, then static surfaces, then particles, each back to front.
    /// </summary>
    public class EffectDrawOrderTests
    {
        private static ZoneTerrainRenderer.EffectDraw Draw(string name, float distance, bool surface = false, bool depthWrite = false) =>
            new(new ZoneTerrainRenderer.GpuWeatherSkySubmesh { Name = name, Layer = new WeatherSkyLayer { Name = name, DepthWrite = depthWrite } },
                distance, null, null, Surface: surface);

        [Fact]
        public void SurfacesDrawBeforeParticles_WhateverTheirDistance()
        {
            // Phanauet Channel: the river's base is the barge's centre, nearer the camera than a spirit hovering past it.
            var draws = new List<ZoneTerrainRenderer.EffectDraw>
            {
                Draw("spirit far", 9f),
                Draw("river", 3f, surface: true),
                Draw("spirit near", 2f),
                Draw("sea", 1f, depthWrite: true),
                Draw("cave fog", 40f, surface: true),
            };
            draws.Sort(ZoneTerrainRenderer.EffectDrawOrder);
            Assert.Equal(new[] { "sea", "cave fog", "river", "spirit far", "spirit near" }, draws.ConvertAll(d => d.Mesh.Name));
        }

        [Fact]
        public void StaticOverlayWithNegativeZOffset_IsPulledAFixedDistance_NotDecalBiased()
        {
            // #592: East Ronfaure's gin1 copies the gatehouse walls, writes depth and carries z offset -1.5. It is depth-tested
            // 0.05 yalms nearer the eye; the decal bias grew with distance and showed it through the ceiling beams.
            var overlay = new WeatherSkyLayer { IsWorldEffect = true, DepthWrite = true, DepthOffset = -1.5f };
            Assert.Equal(-ZoneTerrainRenderer.StaticOverlayPullDistance, ZoneTerrainRenderer.StaticOverlayPull(overlay, staticSurface: true));
            Assert.False(ZoneTerrainRenderer.TakesDecalBias(overlay));
            Assert.Equal(0.0f, ZoneTerrainRenderer.StaticOverlayPull(overlay, staticSurface: false));
            Assert.Equal(0.0f, ZoneTerrainRenderer.StaticOverlayPull(new WeatherSkyLayer { IsWorldEffect = true, DepthWrite = true }, staticSurface: true));
            Assert.True(ZoneTerrainRenderer.TakesDecalBias(new WeatherSkyLayer { IsWorldEffect = true }));
            Assert.False(ZoneTerrainRenderer.TakesDecalBias(new WeatherSkyLayer { DepthOffset = -1.5f }));
        }
    }
}
