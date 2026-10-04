// tests/Gordian.App.Tests/Graphics/ZoneSubmeshPassTests.cs
using System;
using System.IO;
using System.Linq;
using Gordian.App.Graphics;
using Xunit;

namespace Gordian.App.Tests.Graphics
{
    /// <summary>
    /// Zone submesh pass classification (#251): only the 0x8000 blend flag makes a zone submesh translucent.
    /// </summary>
    public class ZoneSubmeshPassTests
    {
        private const string GameDirectory = @"G:\Program Files (x86)\PlayOnline\SquareEnix\FINAL FANTASY XI";

        [Theory]
        [InlineData(false, false, false, ZoneSubmeshPass.Opaque)]
        [InlineData(false, true, false, ZoneSubmeshPass.Cutout)]
        [InlineData(true, false, false, ZoneSubmeshPass.BlendDecal)]
        [InlineData(true, true, false, ZoneSubmeshPass.DeferredBlend)]
        [InlineData(true, false, true, ZoneSubmeshPass.Water)]
        [InlineData(true, true, true, ZoneSubmeshPass.Water)]
        // A water hint on a submesh without the blend flag changes nothing: it is solid and writes depth.
        [InlineData(false, false, true, ZoneSubmeshPass.Opaque)]
        [InlineData(false, true, true, ZoneSubmeshPass.Cutout)]
        public void Classify_FollowsTheBlendFlagAndNamePrefix(bool isBlend, bool isFoliage, bool isWater, ZoneSubmeshPass expected)
        {
            Assert.Equal(expected, ZoneSubmeshPasses.Classify(isBlend, isFoliage, isWater));
        }

        [Theory]
        [InlineData(ZoneSubmeshPass.Opaque, false)]
        [InlineData(ZoneSubmeshPass.Cutout, false)]
        [InlineData(ZoneSubmeshPass.BlendDecal, false)]
        [InlineData(ZoneSubmeshPass.DeferredBlend, true)]
        [InlineData(ZoneSubmeshPass.Water, true)]
        public void IsDeferred_OnlyForTheTranslucentPass(ZoneSubmeshPass pass, bool expected)
        {
            Assert.Equal(expected, pass.IsDeferred());
        }

        /// <summary>
        /// Fort Ghelsba (zone 141): the palisade logs (textures kawa / kawa_hos, no blend flag) draw solid in Pass 1 and
        /// write depth, so the river and waterfall effects behind them stay hidden. Skipped without the retail install.
        /// </summary>
        [Fact]
        public void FortGhelsba_PalisadeLogsDrawSolid()
        {
            if (!Directory.Exists(GameDirectory)) return;
            var rm = new Gordian.Core.Resources.ResourceManager(GameDirectory);
            rm.InitializeFileTable();
            if (!rm.TryLoadZone(141, out var zone, out _)) return;

            var logs = zone.MeshGroups.Where(g => g.TextureName.Contains("kawa", StringComparison.OrdinalIgnoreCase)).ToList();
            // Measured 2026-10-04: 248 placed kawa / kawa_hos submeshes, 2 of them blended (_uge_ie03b).
            Assert.True(logs.Count > 200, $"expected the palisade logs, found {logs.Count}");
            foreach (var g in logs.Where(g => !g.IsBlend))
            {
                var pass = ZoneSubmeshPasses.Classify(g.IsBlend, g.IsFoliage, g.IsWater);
                Assert.False(g.IsWater, $"{g.Name} ({g.TextureName}) has no blend flag but is classed as water");
                Assert.False(pass.IsDeferred(), $"{g.Name} ({g.TextureName}) is deferred to the translucent pass");
            }
            // No submesh without the blend flag is water anywhere in the zone.
            Assert.DoesNotContain(zone.MeshGroups, g => g.IsWater && !g.IsBlend);
        }
    }
}
