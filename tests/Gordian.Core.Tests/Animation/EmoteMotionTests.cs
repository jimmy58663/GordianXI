// tests/Gordian.Core.Tests/Animation/EmoteMotionTests.cs
using System.IO;
using System.Linq;
using Gordian.Core.Animation;
using Gordian.Core.Resources;
using Gordian.Core.Resources.Tables;
using Xunit;

namespace Gordian.Core.Tests.Animation
{
    /// <summary>
    /// The race emote motions an event plays with 0x6E (#176). The data tests are skipped without the game install.
    /// </summary>
    public class EmoteMotionTests
    {
        private const string GameDirectory = @"G:\Program Files (x86)\PlayOnline\SquareEnix\FINAL FANTASY XI";

        [Theory]
        [InlineData(0, 0, 1)]   // point
        [InlineData(1, 0, 0)]   // bow
        [InlineData(2, 2, 4)]   // salute, third variant
        [InlineData(3, 0, 5)]   // kneel
        [InlineData(13, 0, 15)] // clap
        [InlineData(200, 0, -1)]
        public void Slot_FollowsTheClipsOfTheEmoteFiles(int emote, int variant, int slot) =>
            Assert.Equal(slot, EmoteMotion.Slot(emote, variant));

        /// <summary>
        /// Rahal (Elvaan male, look 1) claps in the Southern San d'Oria intro: emote 13 is slot 15, routine em07 of
        /// ROM/41/115 playing <c>clp?</c> for 180 frames, its waist part from ROM/41/121.
        /// </summary>
        [Fact]
        public void ElvaanClap_JoinsTheThreeBodyParts()
        {
            if (!Directory.Exists(GameDirectory)) return;
            var rm = new ResourceManager(GameDirectory);
            rm.InitializeFileTable();
            var bank = EmoteMotion.LoadBank(CharacterRace.ElvaanMale, 13, 0, rm.LoadDatBytes);
            Assert.NotNull(bank);
            var routine = Assert.Single(bank!.Routines).Value;
            Assert.Equal(EmoteMotion.RoutineName(13), routine.Name);
            Assert.Equal("clp", Assert.Single(routine.Segments).ClipName);
            Assert.True(bank.GetRoutineFrames(routine.Name) >= 150);
            Assert.True(bank.Clips.ContainsKey("clp2")); // the waist part from the +6 file
            Assert.True(bank.Clips["clp"].Tracks.Count > bank.Clips["clp1"].Tracks.Count);
        }

        [Fact]
        public void FaceOnlyEmote_HasNoBank()
        {
            if (!Directory.Exists(GameDirectory)) return;
            var rm = new ResourceManager(GameDirectory);
            rm.InitializeFileTable();
            Assert.Null(EmoteMotion.LoadBank(CharacterRace.HumeMale, 15, 0, rm.LoadDatBytes)); // smile
        }
    }
}
