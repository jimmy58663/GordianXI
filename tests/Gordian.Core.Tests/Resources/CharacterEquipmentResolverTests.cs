// tests/Gordian.Core.Tests/Resources/CharacterEquipmentResolverTests.cs
using System.IO;
using Gordian.Core.Resources.Tables;
using Xunit;

namespace Gordian.Core.Tests.Resources
{
    public class CharacterEquipmentResolverTests
    {
        [Theory]
        [InlineData(CharacterRace.HumeMale, CharacterSlot.Face, 0, 7080)]
        [InlineData(CharacterRace.HumeMale, CharacterSlot.Head, 0, 7112)]
        [InlineData(CharacterRace.HumeMale, CharacterSlot.Body, 0, 7368)]
        [InlineData(CharacterRace.HumeMale, CharacterSlot.Hands, 0, 7624)]
        [InlineData(CharacterRace.HumeMale, CharacterSlot.Legs, 0, 7880)]
        [InlineData(CharacterRace.HumeMale, CharacterSlot.Feet, 0, 8136)]
        [InlineData(CharacterRace.HumeMale, CharacterSlot.Main, 0, 8392)]
        [InlineData(CharacterRace.HumeMale, CharacterSlot.Sub, 0, 41199)]
        [InlineData(CharacterRace.HumeMale, CharacterSlot.Ranged, 0, 9416)]
        [InlineData(CharacterRace.HumeFemale, CharacterSlot.Face, 0, 10256)]
        [InlineData(CharacterRace.HumeFemale, CharacterSlot.Body, 0, 10544)]
        [InlineData(CharacterRace.ElvaanMale, CharacterSlot.Face, 0, 13432)]
        [InlineData(CharacterRace.ElvaanMale, CharacterSlot.Body, 0, 13720)]
        [InlineData(CharacterRace.ElvaanFemale, CharacterSlot.Face, 0, 16608)]
        [InlineData(CharacterRace.ElvaanFemale, CharacterSlot.Body, 0, 16896)]
        [InlineData(CharacterRace.TaruMale, CharacterSlot.Face, 0, 19784)]
        [InlineData(CharacterRace.TaruMale, CharacterSlot.Body, 0, 20072)]
        [InlineData(CharacterRace.TaruFemale, CharacterSlot.Face, 0, 22952)]
        [InlineData(CharacterRace.TaruFemale, CharacterSlot.Body, 0, 20072)]
        [InlineData(CharacterRace.Mithra, CharacterSlot.Face, 0, 23184)]
        [InlineData(CharacterRace.Mithra, CharacterSlot.Body, 0, 23472)]
        [InlineData(CharacterRace.Galka, CharacterSlot.Face, 0, 26360)]
        [InlineData(CharacterRace.Galka, CharacterSlot.Body, 0, 26648)]
        public void CharacterEquipmentResolver_ResolvesBaseFileIds(
            CharacterRace race,
            CharacterSlot slot,
            ushort modelId,
            int expectedFileId)
        {
            bool success = CharacterEquipmentResolver.TryResolveGearFileId(race, slot, modelId, out int fileId);
            Assert.True(success);
            Assert.Equal(expectedFileId, fileId);
        }

        [Fact]
        public void CharacterEquipmentResolver_ResolvesChainedGearGroups()
        {
            // HumeMale Head:
            // Group 0: [7112, 256] -> models 0..255 map to 7112..7367
            // Group 1: [63323, 48] -> model 256 maps to 63323
            bool hitGroup0End = CharacterEquipmentResolver.TryResolveGearFileId(CharacterRace.HumeMale, CharacterSlot.Head, 255, out int fid255);
            Assert.True(hitGroup0End);
            Assert.Equal(7112 + 255, fid255);

            bool hitGroup1Start = CharacterEquipmentResolver.TryResolveGearFileId(CharacterRace.HumeMale, CharacterSlot.Head, 256, out int fid256);
            Assert.True(hitGroup1Start);
            Assert.Equal(63323, fid256);
        }

        [Theory]
        [InlineData(1u, 1301)]       // Vanilla (Rabbit)
        [InlineData(300u, 1600)]     // Vanilla (Mandragora)
        [InlineData(356u, 1656)]     // Vanilla (Crab)
        [InlineData(400u, 1700)]     // Vanilla (Adamantoise)
        [InlineData(500u, 1800)]     // Vanilla
        [InlineData(1720u, 52015)]   // Expansion / ToAU (Colibri)
        [InlineData(1778u, 52073)]   // Expansion / ToAU (Acrolith)
        [InlineData(2247u, 52542)]   // Expansion / WotG (Lycopodium)
        [InlineData(3000u, 99907)]   // Late Expansion
        [InlineData(3128u, 100035)]  // Late Expansion
        [InlineData(3193u, 101739)]  // Adoulin / RoV
        public void CharacterEquipmentResolver_ResolvesMonsterFileIds(uint modelId, int expectedFileId)
        {
            int fileId = CharacterEquipmentResolver.GetMonsterFileId(modelId);
            Assert.Equal(expectedFileId, fileId);
        }

        [Fact]
        public void CharacterEquipmentResolver_ReturnsBaseSkeletonPaths()
        {
            Assert.Equal(Path.Combine("ROM", "27", "82.DAT"), CharacterEquipmentResolver.GetBaseSkeletonPath(CharacterRace.HumeMale));
            Assert.Equal(Path.Combine("ROM", "32", "58.DAT"), CharacterEquipmentResolver.GetBaseSkeletonPath(CharacterRace.HumeFemale));
            Assert.Equal(Path.Combine("ROM", "37", "31.DAT"), CharacterEquipmentResolver.GetBaseSkeletonPath(CharacterRace.ElvaanMale));
            Assert.Equal(Path.Combine("ROM", "42", "4.DAT"), CharacterEquipmentResolver.GetBaseSkeletonPath(CharacterRace.ElvaanFemale));
            Assert.Equal(Path.Combine("ROM", "46", "93.DAT"), CharacterEquipmentResolver.GetBaseSkeletonPath(CharacterRace.TaruMale));
            Assert.Equal(Path.Combine("ROM", "51", "89.DAT"), CharacterEquipmentResolver.GetBaseSkeletonPath(CharacterRace.Mithra));
            Assert.Equal(Path.Combine("ROM", "56", "59.DAT"), CharacterEquipmentResolver.GetBaseSkeletonPath(CharacterRace.Galka));
        }
    }
}
