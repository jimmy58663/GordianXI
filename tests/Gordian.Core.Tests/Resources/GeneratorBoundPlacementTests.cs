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

        /// <summary>
        /// Lower Jeuno's generators under <c>t_ju/mode/entr</c> (<c>ent1</c>-<c>ent3</c>) are entrance overlays, which the
        /// renderer draws only across an environment boundary (#414 round 5); its other lit effects are not.
        /// </summary>
        [Fact]
        public void LowerJeunoEntranceOverlays_AreFlagged()
        {
            if (!Directory.Exists(GameDirectory)) return;
            var rm = new ResourceManager(GameDirectory);
            rm.InitializeFileTable();
            if (!rm.TryLoadZone(245, out var zone, out _) || zone == null) return;

            var entrances = zone.EffectLayers.Where(l => l.IsEntranceOverlay).Select(l => l.Name).OrderBy(n => n).ToArray();
            Assert.Equal(new[] { "ent1", "ent2", "ent3" }, entrances);
            Assert.DoesNotContain(zone.EffectLayers, l => l.Name == "tama" && l.IsEntranceOverlay);
        }
    }
}
