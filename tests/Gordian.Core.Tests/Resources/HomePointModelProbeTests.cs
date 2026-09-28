// tests/Gordian.Core.Tests/Resources/HomePointModelProbeTests.cs
using System.IO;
using Gordian.Core.Resources;
using Gordian.Core.Resources.Containers;
using Gordian.Core.Resources.Tables;
using Xunit;
using Xunit.Abstractions;

namespace Gordian.Core.Tests.Resources
{
    /// <summary>
    /// The home point crystal's NPC model (look 0, model id 51 from LandSandBoat, file id 1351 = ROM/3/25.DAT) is
    /// drawn by its effect: the DAT holds particle generators, two effect routines ("bind", "aper"), particle meshes
    /// and textures around a skeleton with a 224-byte placeholder mesh and an idle clip (2026-09-28). Playing that
    /// routine as the entity's body is the model-embedded effect routine work of issue #9; until then the crystal is
    /// invisible in GordianXI.
    /// </summary>
    public class HomePointModelProbeTests
    {
        private const string GameDirectory = @"G:\Program Files (x86)\PlayOnline\SquareEnix\FINAL FANTASY XI";
        private readonly ITestOutputHelper _output;

        public HomePointModelProbeTests(ITestOutputHelper output) => _output = output;

        [Fact]
        public void HomePointCrystal_Model51_IsAnEffectOnlyModel()
        {
            if (!Directory.Exists(GameDirectory)) return;
            var rm = new ResourceManager(GameDirectory);
            rm.InitializeFileTable();
            int fileId = CharacterEquipmentResolver.GetMonsterFileId(51);
            var bytes = rm.LoadDatBytesByFileId(fileId);
            _output.WriteLine($"file id {fileId}: {bytes?.Length} bytes");
            Assert.NotNull(bytes);
            bool anyRoutine = false;
            int meshBytes = 0;
            foreach (var header in DatSectionWalker.ReadHeaders(bytes!))
            {
                _output.WriteLine($"  section type=0x{header.RawTypeCode:X2} ({header.TypeCode}) id='{header.DatId}' size={header.SizeBytes}");
                if (header.TypeCode == DatSectionType.EffectRoutine) anyRoutine = true;
                if (header.TypeCode == DatSectionType.SkeletonMesh) meshBytes += header.SizeBytes;
            }
            var model = EntityModelLoader.LoadMonsterModel(51, rm.LoadDatBytesByFileId);
            _output.WriteLine($"model: {(model == null ? "null" : $"{model.AnimatedMeshGroups.Count} mesh groups, {model.Textures.Count} textures, {model.Animations.Count} animations, skeleton={(model.Skeleton == null ? "none" : "yes")}")}");
            Assert.True(anyRoutine);
            Assert.True(meshBytes < 1024, $"placeholder mesh only: {meshBytes} bytes");
        }
    }
}
