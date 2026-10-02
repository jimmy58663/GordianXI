// tests/Gordian.Core.Tests/Animation/EventMotionPackageTests.cs
using System.IO;
using Gordian.Core.Animation;
using Gordian.Core.Resources;
using Xunit;

namespace Gordian.Core.Tests.Animation
{
    /// <summary>The 0x66 motion packages (#176). Skipped without the game install.</summary>
    public class EventMotionPackageTests
    {
        private const string GameDirectory = @"G:\Program Files (x86)\PlayOnline\SquareEnix\FINAL FANTASY XI";

        private static EventMotionBank? Load(ResourceManager rm, int package)
        {
            var (withWaist, withoutWaist) = EventMotionBank.PackageFileIds(package);
            return (rm.LoadDatBytesByFileId(withWaist) is { } a ? EventMotionBank.Parse(a, withWaist) : null)
                ?? (rm.LoadDatBytesByFileId(withoutWaist) is { } b ? EventMotionBank.Parse(b, withoutWaist) : null);
        }

        /// <summary>
        /// The Southern San d'Oria intro's gestures: Ceraule's talk and thought (package 20), the gate guards' and
        /// Ceraule's salute (21) and the Royal Knights' and Rahal's talk (29, only in the second table).
        /// </summary>
        [Theory]
        [InlineData(20, 32400, "tlk0")]
        [InlineData(20, 32400, "thk1")]
        [InlineData(21, 32402, "sl00")]
        [InlineData(29, 32741, "tlk0")]
        [InlineData(29, 32741, "thk2")]
        [InlineData(12, 32384, "kka0")] // Cornelia's, per xi-tools
        public void Package_HasTheGesture(int package, int fileId, string routine)
        {
            if (!Directory.Exists(GameDirectory)) return;
            var rm = new ResourceManager(GameDirectory);
            rm.InitializeFileTable();
            var bank = Load(rm, package);
            Assert.NotNull(bank);
            Assert.Equal(fileId, bank!.FileId);
            Assert.True(bank.GetRoutineFrames(routine) > 0);
        }

        [Fact]
        public void SaluteOfTheFirstTable_JoinsTheWaistPart()
        {
            if (!Directory.Exists(GameDirectory)) return;
            var rm = new ResourceManager(GameDirectory);
            rm.InitializeFileTable();
            var bank = Load(rm, 21)!;
            Assert.True(bank.Clips.ContainsKey("sl12"));
            Assert.True(bank.Clips["sl1"].Tracks.Count > bank.Clips["sl11"].Tracks.Count);
        }
    }
}
