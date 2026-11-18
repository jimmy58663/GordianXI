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
    /// <summary>Dismount and chocobo dig (#437): the riding routines coff / chor and the mount left behind.</summary>
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
        public void MountingPlaysNothing_DiggingPlaysChorOnAChocoboOnly()
        {
            var player = new PlayerEntity(1, 1);
            MountTransitions.Observe(player, moving: false, 1000);
            player.Mount = Chocobo;
            MountTransitions.Observe(player, moving: false, 1000);
            Assert.False(player.Animation.HasQueuedActions);
            Assert.False(player.MountAnimation.HasQueuedActions);

            MountTransitions.PlayDig(player);
            Assert.True(player.Animation.HasQueuedActions);
            Assert.True(player.MountAnimation.HasQueuedActions);

            var raptorRider = new PlayerEntity(2, 2) { Mount = Raptor };
            MountTransitions.PlayDig(raptorRider);
            Assert.False(raptorRider.Animation.HasQueuedActions);
            Assert.False(raptorRider.MountAnimation.HasQueuedActions);
        }

        [Fact]
        public void LocalPlayerLosingItsMountUnasked_IsKnockedOff_CdamEightYalmsThenFade()
        {
            var player = new PlayerEntity(3, 3) { Mount = Chocobo };
            long t0 = Stopwatch.GetTimestamp();
            MountTransitions.Observe(player, moving: false, t0, isLocalPlayer: true);
            player.Mount = MountAppearance.None;

            var start = MountTransitions.Observe(player, moving: false, t0, isLocalPlayer: true);
            Assert.True(start.Departing);
            Assert.True(start.RiderSeated); // the rider plays its pack's cdam (45 ticks)
            Assert.Equal(MountTransitions.KnockedOffRoutine, player.MountAnimation.PeekQueuedRoutine());
            Assert.Equal(MountTransitions.KnockedOffRoutine, player.Animation.PeekQueuedRoutine());

            // Tick 64: halfway through the 120-tick run (4-124), still opaque (the fade is 94-124).
            var running = MountTransitions.Observe(player, moving: false, t0 + Ticks(64), isLocalPlayer: true);
            Assert.Equal(4f, running.ForwardYalms, 2);
            Assert.Equal(1f, running.Alpha);
            Assert.False(running.RiderSeated);
            Assert.Equal(0.5f, MountTransitions.Observe(player, moving: false, t0 + Ticks(109), isLocalPlayer: true).Alpha, 2);
            Assert.Equal(MountFrame.None, MountTransitions.Observe(player, moving: false, t0 + Ticks(125), isLocalPlayer: true));
        }

        [Fact]
        public void AskedDismountAndOtherPlayers_PlayCoff()
        {
            var local = new PlayerEntity(4, 4) { Mount = Raptor };
            long t0 = Stopwatch.GetTimestamp();
            MountTransitions.Observe(local, moving: false, t0, isLocalPlayer: true);
            MountTransitions.NoteDismountRequest(local.ServerId);
            local.Mount = MountAppearance.None;
            MountTransitions.Observe(local, moving: false, Stopwatch.GetTimestamp(), isLocalPlayer: true);
            Assert.Equal(MountTransitions.DismountRoutine, local.MountAnimation.PeekQueuedRoutine());

            var other = new PlayerEntity(5, 5) { Mount = Raptor };
            MountTransitions.Observe(other, moving: false, t0);
            other.Mount = MountAppearance.None;
            MountTransitions.Observe(other, moving: false, t0);
            Assert.Equal(MountTransitions.DismountRoutine, other.MountAnimation.PeekQueuedRoutine());
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
        /// chor and coff, and the raptor's coff; a dig starts chor on the rider and the chocobo.
        /// </summary>
        [Fact]
        public void Retail_RidersAndMountsCarryTheirRoutines_AndDiggingPlaysChor()
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

            var player = new PlayerEntity(1, 1) { Mount = Chocobo };
            MountTransitions.PlayDig(player);
            player.Animation.Advance(1f / 60f, AnimationCategory.Idle, 0, rider);
            Assert.Equal("chor", player.Animation.ActiveRoutine?.Name);
            player.MountAnimation.Advance(1f / 60f, AnimationCategory.Idle, 0, chocobo);
            Assert.Equal("chor", player.MountAnimation.ActiveRoutine?.Name);
        }

        /// <summary>
        /// The dig's effects and sound (#437): the chocobo's <c>chor</c> spawns <c>chov</c> / <c>cho0</c> / <c>cho1</c> (shared
        /// <c>ROM/0/0</c> meshes) and plays sound pointer <c>8077</c> at ticks 64, 78, 102 and 110; the mount tracker reports
        /// the dig once, not a routine already playing when first seen.
        /// </summary>
        [Fact]
        public void Retail_ChocoboDigKicksUpDirtWithItsSound()
        {
            if (!Directory.Exists(GameDirectory)) return;
            var rm = new ResourceManager(GameDirectory);
            rm.InitializeFileTable();
            var effects = rm.GetMountEffects(Chocobo);
            Assert.NotNull(effects);
            Assert.True(effects!.Routines.ContainsKey(MountTransitions.DigRoutine));
            var generators = System.Linq.Enumerable.ToHashSet(System.Linq.Enumerable.Select(effects.Layers, l => l.Emitter!.Definition.DatId));
            Assert.Superset(new System.Collections.Generic.HashSet<string> { "chov", "cho0", "cho1" }, generators);
            Assert.Same(effects, rm.GetMountEffects(Chocobo with { ChocoboIndex = 2 }));

            Assert.True(rm.TryLoadMountModel(Chocobo, out var chocobo));
            var sounds = chocobo!.GetRoutineSounds(MountTransitions.DigRoutine);
            Assert.Equal(new[] { 64, 78, 102, 110 }, System.Linq.Enumerable.Select(sounds, c => c.Tick));
            Assert.All(sounds, c => Assert.Equal(new[] { 8077 }, c.Choices));

            var tracker = new Gordian.Engine.Graphics.MountEffectTracker();
            var player = new PlayerEntity(1, 1) { Mount = Chocobo };
            Assert.Null(tracker.Step(player));
            MountTransitions.PlayDig(player);
            player.MountAnimation.Advance(1f / 60f, AnimationCategory.Idle, 0, chocobo);
            Assert.Equal(MountTransitions.DigRoutine, tracker.Step(player));
            player.MountAnimation.Advance(1f / 60f, AnimationCategory.Idle, 0, chocobo);
            Assert.Null(tracker.Step(player));
            Assert.Null(tracker.Step(new PlayerEntity(2, 2)));
        }
    }
}
