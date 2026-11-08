// tests/Gordian.Core.Tests/Animation/MannequinPoseRealDataTests.cs
using System.IO;
using Gordian.Core.Resources;
using Gordian.Core.Resources.Tables;
using Xunit;

namespace Gordian.Core.Tests.Animation
{
    /// <summary>
    /// The Mog House mannequin on the retail DATs (#93): face 30 is its carved head, and the model holds the race's <c>mq0</c>
    /// pose instead of the breathing idle. Skipped without the game install.
    /// </summary>
    public class MannequinPoseRealDataTests
    {
        private const string GameDirectory = @"G:\Program Files (x86)\PlayOnline\SquareEnix\FINAL FANTASY XI";

        [Theory]
        [InlineData(CharacterRace.HumeMale, 7110)]
        [InlineData(CharacterRace.HumeFemale, 10286)]
        [InlineData(CharacterRace.ElvaanMale, 13462)]
        [InlineData(CharacterRace.ElvaanFemale, 16638)]
        [InlineData(CharacterRace.TaruMale, 19814)]
        [InlineData(CharacterRace.TaruFemale, 22982)]
        [InlineData(CharacterRace.Mithra, 23214)]
        [InlineData(CharacterRace.Galka, 26390)]
        public void TheMannequinHoldsItsPose(CharacterRace race, int faceFile)
        {
            Assert.True(CharacterEquipmentResolver.TryResolveGearFileId(race, CharacterSlot.Face, CharacterEquipmentResolver.MannequinFace, out int file));
            Assert.Equal(faceFile, file);
            if (!Directory.Exists(GameDirectory)) return;
            var rm = new ResourceManager(GameDirectory);
            rm.InitializeFileTable();
            var person = EntityModelLoader.AssembleCharacter(race, 1, new ushort[9], rm.LoadDatBytes, rm.LoadDatBytesByFileId)!;
            var mannequin = EntityModelLoader.AssembleCharacter(race, CharacterEquipmentResolver.MannequinFace, new ushort[9], rm.LoadDatBytes, rm.LoadDatBytesByFileId)!;
            var idle = mannequin.Animations["idl"];
            Assert.Equal("mq0", idle.Name);
            Assert.NotSame(person.Animations["idl"], idle);
            // Legs, upper body and waist: as many joints as the breathing idle moves.
            Assert.True(idle.Tracks.Count >= person.Animations["idl"].Tracks.Count - 4, $"{idle.Tracks.Count} joints vs {person.Animations["idl"].Tracks.Count}");
            Assert.False(mannequin.Animations.ContainsKey("idl0"));
        }
    }
}
