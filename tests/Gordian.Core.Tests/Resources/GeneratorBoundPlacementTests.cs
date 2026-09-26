// tests/Gordian.Core.Tests/Resources/GeneratorBoundPlacementTests.cs
using System.IO;
using System.Linq;
using Gordian.Core.Resources;
using Gordian.Core.Resources.Models;
using Xunit;

namespace Gordian.Core.Tests.Resources
{
    public class GeneratorBoundPlacementTests
    {
        private const string GameDirectory = @"G:\Program Files (x86)\PlayOnline\SquareEnix\FINAL FANTASY XI";

        [Theory]
        [InlineData("ent1", true)]
        [InlineData("f001", true)]
        [InlineData("_720", false)] // door part
        [InlineData("@6l1", false)] // elevator part
        [InlineData("", false)]
        public void IsGeneratorBound_FollowsTheBlockIdPrefix(string blockId, bool expected)
        {
            var placement = new ZonePlacement("mesh", default, default, default, 100f, BlockId: blockId);
            Assert.Equal(expected, placement.IsGeneratorBound);
        }

        /// <summary>
        /// Bibiki Bay's cave mouth (`yama_3c_ent`, BlockID `ent1`) is drawn only by its generator's alpha-blended effect
        /// layer; a static copy z-fought with the tunnel mesh it overlays.
        /// </summary>
        [Fact]
        public void BibikiCaveMouth_IsDrawnOnlyByItsGenerator()
        {
            if (!Directory.Exists(GameDirectory)) return;
            var rm = new ResourceManager(GameDirectory);
            rm.InitializeFileTable();
            if (!rm.TryLoadZone(4, out var zone, out _) || zone == null) return;

            Assert.Contains(zone.EffectLayers, l => l.GeneratorId == "ent1");
            Assert.DoesNotContain(zone.MeshGroups, g => g.Name.Contains("yama_3c_ent"));
            Assert.Contains(zone.MeshGroups, g => g.Name.Contains("yama_3c_m")); // the tunnel itself stays
        }
    }
}
