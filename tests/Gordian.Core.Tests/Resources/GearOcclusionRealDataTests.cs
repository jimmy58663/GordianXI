// tests/Gordian.Core.Tests/Resources/GearOcclusionRealDataTests.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Gordian.Core.Resources;
using Gordian.Core.Resources.Models;
using Gordian.Core.Resources.Tables;
using Xunit;
using Xunit.Abstractions;

namespace Gordian.Core.Tests.Resources
{
    /// <summary>
    /// Gear occlusion (#88) on retail Hume Male gear: a full helm (occludeType 0x05) and a sleeved body (0x12) hide the
    /// face DAT's hair and face pieces and the hands' wrist pieces. Skipped without the game install.
    /// </summary>
    public class GearOcclusionRealDataTests
    {
        private const string GameDirectory = @"G:\Program Files (x86)\PlayOnline\SquareEnix\FINAL FANTASY XI";
        private readonly ITestOutputHelper _output;

        public GearOcclusionRealDataTests(ITestOutputHelper output) => _output = output;

        private static List<SkeletonMeshGroup> Meshes(ResourceManager rm, CharacterSlot slot, ushort modelId)
        {
            if (!CharacterEquipmentResolver.TryResolveGearFileId(CharacterRace.HumeMale, slot, modelId, out int fileId)) return new();
            var bytes = rm.LoadDatBytesByFileId(fileId);
            return bytes == null ? new() : EntityModelLoader.ParseDatContainer(bytes, $"{slot}_{modelId}").Meshes;
        }

        /// <summary>The first model id of a slot whose meshes declare <paramref name="occludeType"/>.</summary>
        private static ushort FindModel(ResourceManager rm, CharacterSlot slot, byte occludeType)
        {
            for (ushort id = 1; id < 400; id++)
            {
                if (Meshes(rm, slot, id).Any(m => m.OccludeType == occludeType)) return id;
            }
            return 0;
        }

        private static int Triangles(IEnumerable<SkeletonMeshGroup> meshes) => meshes.Sum(m => m.Pieces.Sum(p =>
            p.Topology == MeshTopology.TriangleStrip ? p.Corners.Length - 2 : p.Corners.Length / 3));

        [Fact]
        public void HumeMale_FullHelmAndSleeves_HideHairFaceAndWrists()
        {
            if (!Directory.Exists(GameDirectory)) return;
            var rm = new ResourceManager(GameDirectory);
            rm.InitializeFileTable();

            ushort helm = FindModel(rm, CharacterSlot.Head, 0x05);
            ushort sleeved = FindModel(rm, CharacterSlot.Body, 0x12);
            _output.WriteLine($"full helm: head model {helm}; sleeved body: body model {sleeved}");
            Assert.NotEqual(0, helm);
            Assert.NotEqual(0, sleeved);

            var worn = new List<(string Slot, List<SkeletonMeshGroup> Meshes)>
            {
                ("face", Meshes(rm, CharacterSlot.Face, 1)),
                ("head", Meshes(rm, CharacterSlot.Head, helm)),
                ("body", Meshes(rm, CharacterSlot.Body, sleeved)),
                ("hands", Meshes(rm, CharacterSlot.Hands, 0)),
                ("legs", Meshes(rm, CharacterSlot.Legs, 0)),
                ("feet", Meshes(rm, CharacterSlot.Feet, 0)),
            };
            int before = Triangles(worn.SelectMany(w => w.Meshes));
            var occlusion = GearOcclusion.From(worn.SelectMany(w => w.Meshes));
            var hiddenBySlot = new Dictionary<string, int>();
            foreach (var (slot, meshes) in worn)
            {
                var hiddenTypes = meshes.SelectMany(m => m.Pieces).Where(p => occlusion.Hides(p.DisplayType)).Select(p => p.DisplayType).Distinct().OrderBy(t => t).ToList();
                int removed = meshes.Sum(occlusion.RemoveHiddenPieces);
                hiddenBySlot[slot] = removed;
                _output.WriteLine($"  {slot}: occludeTypes [{string.Join(", ", meshes.Select(m => $"0x{m.OccludeType:X2}").Distinct())}], hid {removed} piece(s) (displayTypes [{string.Join(", ", hiddenTypes)}])");
            }
            int after = Triangles(worn.SelectMany(w => w.Meshes));
            _output.WriteLine($"triangles: {before} -> {after} ({100.0 * (before - after) / before:F1}% fewer)");

            Assert.True(hiddenBySlot["face"] > 0, "the full helm hides the face DAT's hair and face");
            Assert.True(hiddenBySlot["hands"] > 0, "the sleeves hide the wrists");
            Assert.True(after < before);
        }

        [Fact]
        public void FixedNpcModel_HidesItsOwnHairUnderItsHat()
        {
            // Apururu (fixed model 164, Windurst Waters intro, #181): one DAT with a hat (occludeType 0x04) and a face
            // mesh whose hair pieces are displayTypes 1 and 3. The hat hides the hair; the face (4) stays.
            if (!Directory.Exists(GameDirectory)) return;
            var rm = new ResourceManager(GameDirectory);
            rm.InitializeFileTable();

            int fileId = CharacterEquipmentResolver.GetMonsterFileId(164);
            var dat = rm.LoadDatBytesByFileId(fileId);
            Assert.NotNull(dat);
            var meshes = EntityModelLoader.ParseDatContainer(dat!, "Apururu").Meshes;
            Assert.Contains(meshes, m => m.OccludeType == 0x04);
            Assert.Contains(meshes.SelectMany(m => m.Pieces), p => p.DisplayType is 1 or 3);

            var unoccluded = EntityModelLoader.AssembleModel(dat!, name: "unoccluded");
            var model = EntityModelLoader.LoadMonsterModel(164, rm.LoadDatBytesByFileId);
            Assert.NotNull(model);

            int Count(EntityModel m) => m.AnimatedMeshGroups.Sum(g => g.Indices.Length) / 3;
            _output.WriteLine($"model 164: {Count(unoccluded)} triangles without occlusion, {Count(model!)} with");
            Assert.True(Count(model!) < Count(unoccluded));
        }

        [Fact]
        public void HumeMale_StowedRangedWeapon_AddsNoGeometry()
        {
            if (!Directory.Exists(GameDirectory)) return;
            var rm = new ResourceManager(GameDirectory);
            rm.InitializeFileTable();

            ushort ranged = 0;
            for (ushort id = 1; id < 400 && ranged == 0; id++)
            {
                if (Meshes(rm, CharacterSlot.Ranged, id).Count > 0) ranged = id;
            }
            Assert.NotEqual(0, ranged);

            // grap table: race/face, head, body, hands, legs, feet, main, sub, ranged (slot ids carry the slot in bits 12-15).
            ushort[] bare = { 0x0101, 0x1000, 0x2000, 0x3000, 0x4000, 0x5000, 0x6000, 0x7000, 0x8000 };
            ushort[] withRanged = (ushort[])bare.Clone();
            withRanged[8] = (ushort)(0x8000 | ranged);

            var without = EntityModelLoader.AssembleCharacter(CharacterRace.HumeMale, 0x0101, bare, rm.LoadDatBytes, rm.LoadDatBytesByFileId);
            var with = EntityModelLoader.AssembleCharacter(CharacterRace.HumeMale, 0x0101, withRanged, rm.LoadDatBytes, rm.LoadDatBytesByFileId);
            Assert.NotNull(without);
            Assert.NotNull(with);

            int Count(EntityModel m) => m.AnimatedMeshGroups.Sum(g => g.Indices.Length) / 3;
            _output.WriteLine($"ranged model {ranged}: {Count(without!)} triangles without, {Count(with!)} with");
            Assert.Equal(Count(without!), Count(with!));
        }
    }
}
