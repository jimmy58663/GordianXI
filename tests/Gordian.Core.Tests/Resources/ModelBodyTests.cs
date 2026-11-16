// tests/Gordian.Core.Tests/Resources/ModelBodyTests.cs
using System.IO;
using Gordian.Core.Resources;
using Xunit;

namespace Gordian.Core.Tests.Resources
{
    /// <summary>
    /// Models with no body cast no shadow (#577 round 2): the Home Point crystal and the invisible event NPCs are a 3-vertex
    /// placeholder mesh drawn by their effects, if at all. Skipped without the game install.
    /// </summary>
    public class ModelBodyTests
    {
        private const string GameDirectory = @"G:\Program Files (x86)\PlayOnline\SquareEnix\FINAL FANTASY XI";

        [Theory]
        [InlineData(51u, true)]    // Home Point crystal
        [InlineData(50u, true)]    // invisible event NPC
        [InlineData(52u, true)]    // invisible "???" NPC
        [InlineData(2411u, true)]  // Yahse campfire, drawn by its effects
        [InlineData(965u, false)]  // blue treasure casket
        [InlineData(2348u, false)] // Synergy Furnace
        [InlineData(300u, false)]  // a monster
        public void HasNoBody_OnlyForThePlaceholderMesh(uint modelId, bool noBody)
        {
            if (!Directory.Exists(GameDirectory)) return;
            var rm = new ResourceManager(GameDirectory);
            rm.InitializeFileTable();
            var model = EntityModelLoader.LoadMonsterModel(modelId, rm.LoadDatBytesByFileId)!;
            Assert.Equal(noBody, model.HasNoBody);
        }
    }
}
