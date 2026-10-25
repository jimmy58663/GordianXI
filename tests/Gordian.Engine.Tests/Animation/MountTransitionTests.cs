// tests/Gordian.Engine.Tests/Animation/MountTransitionTests.cs
using System.Diagnostics;
using System.IO;
using Gordian.Core.Animation;
using Gordian.Core.Resources;
using Gordian.Core.Resources.Tables;
using Gordian.Core.World;
using Gordian.Engine.Animation;
using Xunit;

namespace Gordian.Engine.Tests.Animation
{
    /// <summary>Mount-up and dismount (#437): the riding routines chor / coff and the mount left behind.</summary>
    public class MountTransitionTests
    {
        private const string GameDirectory = @"G:\Program Files (x86)\PlayOnline\SquareEnix\FINAL FANTASY XI";

        private static readonly MountAppearance Chocobo = MountAppearance.FromWire(MountAppearance.StatusChocobo, 0, 1);
        private static readonly MountAppearance Raptor = MountAppearance.FromWire(MountAppearance.StatusMount, 2, 2);

        private static long Ticks(float ticks) => (long)(ticks / EntityAnimationState.RoutineTicksPerSecond * Stopwatch.Frequency);

        [Fact]
        public void FirstSighting_DrawsTheMountWithoutAFlourish()
        {
            var player = new PlayerEntity(1, 1) { Mount = Chocobo };
            var frame = MountTransitions.Observe(player, moving: false, 1000);
            Assert.Equal(Chocobo, frame.Mount);
            Assert.True(frame.RiderSeated);
            Assert.False(frame.Departing);
            Assert.Equal(MountFrame.None, MountTransitions.Observe(new PlayerEntity(2, 2), moving: false, 1000));
        }

        [Fact]
        public void ChocoboDismount_RiderStaysSeatedThroughCoff_ThenTheChocoboRunsOffAndFades()
        {
            var player = new PlayerEntity(1, 1) { Mount = Chocobo };
            long t0 = 1000;
            MountTransitions.Observe(player, moving: false, t0);
            player.Mount = MountAppearance.None;

            var start = MountTransitions.Observe(player, moving: false, t0);
            Assert.True(start.Departing);
            Assert.True(start.RiderSeated);
            Assert.Equal(Chocobo, start.Mount);
            Assert.Equal(0f, start.ForwardYalms);
            Assert.Equal(1f, start.Alpha);

            // Tick 138: halfway through the 72-tick run (102-174) and still opaque (the fade is 150-162).
            var running = MountTransitions.Observe(player, moving: false, t0 + Ticks(138));
            Assert.Equal(1.5f, running.ForwardYalms, 2);
            Assert.Equal(1f, running.Alpha);
            var fading = MountTransitions.Observe(player, moving: false, t0 + Ticks(156));
            Assert.Equal(0.5f, fading.Alpha, 2);

            // Past the chocobo's 174 ticks the rider still finishes ch2 (180), with nothing left to draw of the chocobo.
            var landing = MountTransitions.Observe(player, moving: false, t0 + Ticks(177));
            Assert.True(landing.RiderSeated);
            Assert.Equal(0f, landing.Alpha);
            Assert.Equal(MountFrame.None, MountTransitions.Observe(player, moving: false, t0 + Ticks(181)));
        }

        [Fact]
        public void ChocoboDismount_WalkingOffEndsTheRidersPose()
        {
            var player = new PlayerEntity(1, 1) { Mount = Chocobo };
            MountTransitions.Observe(player, moving: false, 1000);
            player.Mount = MountAppearance.None;
            MountTransitions.Observe(player, moving: false, 1000);

            var walking = MountTransitions.Observe(player, moving: true, 1000 + Ticks(20));
            Assert.False(walking.RiderSeated);
            Assert.True(walking.Departing); // the chocobo still leaves
            Assert.False(MountTransitions.Observe(player, moving: false, 1000 + Ticks(30)).RiderSeated);
        }

        [Fact]
        public void OtherMountDismount_RiderOnFootAtOnce_TheMountRunsTenYalms()
        {
            var player = new PlayerEntity(1, 1) { Mount = Raptor };
            MountTransitions.Observe(player, moving: false, 1000);
            player.Mount = MountAppearance.None;

            var start = MountTransitions.Observe(player, moving: false, 1000);
            Assert.False(start.RiderSeated);
            Assert.True(start.Departing);
            Assert.Equal(10f * 71f / 72f, MountTransitions.Observe(player, moving: false, 1000 + Ticks(73)).ForwardYalms, 2);
            Assert.Equal(MountFrame.None, MountTransitions.Observe(player, moving: false, 1000 + Ticks(75)));
        }

        [Fact]
        public void MountingAgainEndsTheDeparture()
        {
            var player = new PlayerEntity(1, 1) { Mount = Raptor };
            MountTransitions.Observe(player, moving: false, 1000);
            player.Mount = MountAppearance.None;
            MountTransitions.Observe(player, moving: false, 1000);
            player.Mount = Chocobo;
            var frame = MountTransitions.Observe(player, moving: false, 1000 + Ticks(10));
            Assert.Equal(Chocobo, frame.Mount);
            Assert.False(frame.Departing);
        }

        /// <summary>
        /// The retail data the profiles are read from: the chocobo rider pack's chor (168) and coff (180), the chocobo's own
        /// chor and coff, and the raptor's coff; mounting up starts chor on the rider.
        /// </summary>
        [Fact]
        public void Retail_RidersAndMountsCarryTheirRoutines_AndMountingPlaysChor()
        {
            if (!Directory.Exists(GameDirectory)) return;
            var rm = new ResourceManager(GameDirectory);
            rm.InitializeFileTable();
            var character = EntityModelLoader.AssembleCharacter(CharacterRace.HumeMale, 1, new ushort[9], rm.LoadDatBytes, rm.LoadDatBytesByFileId)!;
            Assert.True(rm.TryGetRiderModel(character, CharacterRace.HumeMale, out var rider));
            Assert.Equal(168, EntityAnimationState.OnePassTicks(rider!.MotionRoutines["chor"]));
            Assert.Equal(MountTransitions.ChocoboRiderDismountTicks, EntityAnimationState.OnePassTicks(rider.MotionRoutines["coff"]));
            Assert.True(rm.TryLoadMountModel(Chocobo, out var chocobo));
            Assert.Contains("chor", chocobo!.MotionRoutines.Keys);
            Assert.Contains("coff", chocobo.MotionRoutines.Keys);
            Assert.True(rm.TryLoadMountModel(Raptor, out var raptor));
            Assert.Contains("coff", raptor!.MotionRoutines.Keys);
            Assert.DoesNotContain("chor", raptor.MotionRoutines.Keys);

            var player = new PlayerEntity(1, 1);
            MountTransitions.Observe(player, moving: false);
            player.Mount = Chocobo;
            MountTransitions.Observe(player, moving: false);
            player.Animation.Advance(1f / 60f, AnimationCategory.Idle, 0, rider);
            Assert.Equal("chor", player.Animation.ActiveRoutine?.Name);
            player.MountAnimation.Advance(1f / 60f, AnimationCategory.Idle, 0, chocobo);
            Assert.Equal("chor", player.MountAnimation.ActiveRoutine?.Name);
        }
    }
}
