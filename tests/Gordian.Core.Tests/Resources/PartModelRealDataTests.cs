// tests/Gordian.Core.Tests/Resources/PartModelRealDataTests.cs
using System.IO;
using Gordian.Core.Resources;
using Gordian.Core.Resources.Tables;
using Xunit;

namespace Gordian.Core.Tests.Resources
{
    /// <summary>
    /// Part models (#608): the S2C 0x00E SubKind 6 Mamool Ja and automaton DATs carry no skeleton, and their <c>init</c>
    /// routine names the model they are built on (routine op 0x98). Skipped without the game install.
    /// </summary>
    public class PartModelRealDataTests
    {
        private const string GameDirectory = @"G:\Program Files (x86)\PlayOnline\SquareEnix\FINAL FANTASY XI";

        [Theory]
        [InlineData(1626u, 1625u)] // Molaal Ja: the mage pack
        [InlineData(1603u, 1600u)] // Golaal Ja: the sword pack
        [InlineData(1615u, 1601u)] // Bakool Ja: the spear pack
        [InlineData(1977u, 1976u)] // automaton, Harlequin frame and head: the automaton base
        public void PartModel_NamesItsBase_AndIsBuiltOnIt(uint modelId, uint baseId)
        {
            if (!Directory.Exists(GameDirectory)) return;
            var rm = new ResourceManager(GameDirectory);
            rm.InitializeFileTable();
            var dat = rm.LoadDatBytesByFileId(CharacterEquipmentResolver.GetMonsterFileId(modelId));
            if (dat == null) return;

            Assert.Equal(baseId, EntityModelLoader.FindBaseModel(dat));
            var model = EntityModelLoader.LoadMonsterModel(modelId, rm.LoadDatBytesByFileId)!;
            Assert.NotNull(model.Skeleton);
            Assert.False(model.HasNoBody);
            Assert.NotEmpty(model.Animations);
        }

        [Fact]
        public void CompleteModel_NamesNoBase()
        {
            if (!Directory.Exists(GameDirectory)) return;
            var rm = new ResourceManager(GameDirectory);
            rm.InitializeFileTable();
            // Gaheel Ja (1584, ROM4/2/56) is a whole Mamool Ja: skeleton riz and its own meshes.
            var dat = rm.LoadDatBytesByFileId(CharacterEquipmentResolver.GetMonsterFileId(1584));
            if (dat == null) return;
            Assert.Null(EntityModelLoader.FindBaseModel(dat));
        }
    }
}
