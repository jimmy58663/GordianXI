// tests/Gordian.Core.Tests/Resources/SceneEffectZoneMeshTests.cs
using System.IO;
using System.Linq;
using Gordian.Core.Resources;
using Xunit;

namespace Gordian.Core.Tests.Resources
{
    /// <summary>
    /// Lower Delkfutt's Tower event 37 (#459): the sea, coral and Bahamut of scene file 51252 are generators drawing
    /// Section 0x2E meshes, stored encrypted like a zone's. Skipped without the game install.
    /// </summary>
    public class SceneEffectZoneMeshTests
    {
        private const string GameDirectory = @"G:\Program Files (x86)\PlayOnline\SquareEnix\FINAL FANTASY XI";

        [Fact]
        public void SceneFile_GeneratorsDrawingZoneMeshes_Load()
        {
            if (!Directory.Exists(GameDirectory)) return;
            var rm = new ResourceManager(GameDirectory);
            rm.InitializeFileTable();
            var set = rm.GetSceneEffects(51252);
            Assert.NotNull(set);
            // Before the key tables were passed only the 12 sprite-sheet layers loaded.
            Assert.True(set!.Layers.Count > 100, $"{set.Layers.Count} layers");
            foreach (string name in new[] { "sea1", "sea2", "sea3", "cor1", "bah4", "bah9" })
            {
                var layer = Assert.Single(set.Layers, l => l.Name == name);
                Assert.NotEmpty(layer.MeshGroups);
            }
            Assert.Contains("bahmoviesuimen", set.Textures.Keys);
        }

        /// <summary>Stage zone 542 places nothing, so it draws nothing of its own (no raw-mesh fallback at the origin).</summary>
        [Fact]
        public void BareStageZone_DrawsNoUnplacedMeshes()
        {
            if (!Directory.Exists(GameDirectory)) return;
            var rm = new ResourceManager(GameDirectory);
            rm.InitializeFileTable();
            Assert.True(rm.TryLoadZone(542, out var zone, out _));
            Assert.Empty(zone!.Placements);
            Assert.Empty(zone.MeshGroups);
        }
    }
}
