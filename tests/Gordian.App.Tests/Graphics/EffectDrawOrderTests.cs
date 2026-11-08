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
    }
}
