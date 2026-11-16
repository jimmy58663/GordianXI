// tests/Gordian.Core.Tests/Animation/StateDrivenStanceTests.cs
using System.IO;
using Gordian.Core.Animation;
using Gordian.Core.Resources;
using Xunit;

namespace Gordian.Core.Tests.Animation
{
    /// <summary>
    /// State-driven models rest in their open stance (round 1 E7): the Synergy Furnace, sent with sub-animation 5, idles on
    /// <c>1dl</c> (cap raised) instead of the Hpemde rule's closed <c>2dl</c>. Skipped without the game install.
    /// </summary>
    public class StateDrivenStanceTests
    {
        private const string GameDirectory = @"G:\Program Files (x86)\PlayOnline\SquareEnix\FINAL FANTASY XI";

        [Fact]
        public void SynergyFurnace_RestsOpen()
        {
            if (!Directory.Exists(GameDirectory)) return;
            var rm = new ResourceManager(GameDirectory);
            rm.InitializeFileTable();
            var furnace = EntityModelLoader.LoadMonsterModel(2348, rm.LoadDatBytesByFileId)!;
            Assert.True(NpcStanceResolver.IsStateDriven(furnace));
            byte stance = NpcStanceResolver.ResolveEffectiveStance(furnace, 5);
            Assert.Equal(1, stance);
            Assert.Same(furnace.Animations["1dl0"], NpcStanceResolver.ResolveTargetClip(furnace, AnimationCategory.Idle, stance));
            Assert.Equal(2, NpcStanceResolver.ResolveEffectiveStance(furnace, 2));

            // A Hpemde-like family that is not state-driven keeps its rule.
            var homePoint = EntityModelLoader.LoadMonsterModel(51, rm.LoadDatBytesByFileId)!;
            Assert.False(NpcStanceResolver.IsStateDriven(homePoint));
        }

        /// <summary>
        /// #582: a dropped casket (blue 965, brown 966) spawns with sub-animation 4 and rests closed on <c>idl</c>; LandSandBoat's
        /// 1 once opened plays <c>sp00</c>'s opening clip <c>op0</c> into the open <c>1dl</c>; 5 (state 1 with the spawn bit, a
        /// casket already open when it comes into range) rests open without the opening.
        /// </summary>
        [Theory]
        [InlineData(965u)]
        [InlineData(966u)]
        public void Casket_SpawnsClosed_AndOpensThroughItsOpeningClip(uint modelId)
        {
            if (!Directory.Exists(GameDirectory)) return;
            var rm = new ResourceManager(GameDirectory);
            rm.InitializeFileTable();
            var casket = EntityModelLoader.LoadMonsterModel(modelId, rm.LoadDatBytesByFileId)!;
            Assert.True(NpcStanceResolver.IsStateDriven(casket));

            Assert.Equal(0, NpcStanceResolver.ResolveEffectiveStance(casket, 4));
            Assert.Equal(0, NpcStanceResolver.ResolveEffectiveStance(casket, 0));
            Assert.Same(casket.Animations["idl0"], NpcStanceResolver.ResolveTargetClip(casket, AnimationCategory.Idle, 0));
            Assert.Equal(1, NpcStanceResolver.ResolveEffectiveStance(casket, 1));
            Assert.Equal(1, NpcStanceResolver.ResolveEffectiveStance(casket, 5));
            Assert.Same(casket.Animations["1dl0"], NpcStanceResolver.ResolveTargetClip(casket, AnimationCategory.Idle, 1));

            var opening = NpcStanceResolver.ResolveTransitionClip(casket, 0, 1, 1);
            Assert.NotNull(opening);
            Assert.StartsWith("op0", opening!.Name);
            Assert.Null(NpcStanceResolver.ResolveTransitionClip(casket, 0, 1, 5));

            // Played through the animation state: closed at spawn, the opening clip on 1, then open.
            var state = new EntityAnimationState();
            state.Advance(1f / 60f, AnimationCategory.Idle, 4, casket);
            Assert.Equal(0, state.CurrentStance);
            state.Advance(1f / 60f, AnimationCategory.Idle, 1, casket);
            Assert.True(state.IsPlayingTransition);
            Assert.Same(opening, state.TransitionClip);
            for (int i = 0; i < 120; i++) state.Advance(1f / 60f, AnimationCategory.Idle, 1, casket);
            Assert.False(state.IsPlayingTransition);
            Assert.Equal(1, state.CurrentStance);
            Assert.Same(casket.Animations["1dl0"], state.CurrentClip);

            // Whatever the classifier makes of it (engaged, moving), an open casket stays open: its stance lists for those
            // categories fall back to idl, the closed lid (#582 round 1: "it closed and never opened again").
            foreach (var category in new[] { AnimationCategory.Combat, AnimationCategory.Walk, AnimationCategory.Run, AnimationCategory.Idle })
            {
                for (int i = 0; i < 30; i++) state.Advance(1f / 60f, category, 1, casket);
                Assert.Same(casket.Animations["1dl0"], state.CurrentClip);
                Assert.Equal(AnimationCategory.Idle, state.Current);
            }
        }

        /// <summary>
        /// #582 round 3: retail sends a casket 4 as it appears, 5 as it is opened and 6 once that event ends. The casket's
        /// <c>ids2</c> sets the open stance like <c>ids1</c>, so it stays open on 6 (the first rule shut the lid there).
        /// </summary>
        [Theory]
        [InlineData(965u)]
        [InlineData(966u)]
        public void Casket_StaysOpenThroughRetailsSequence(uint modelId)
        {
            if (!Directory.Exists(GameDirectory)) return;
            var rm = new ResourceManager(GameDirectory);
            rm.InitializeFileTable();
            var casket = EntityModelLoader.LoadMonsterModel(modelId, rm.LoadDatBytesByFileId)!;
            Assert.Equal(0, casket.RawMotionRoutines["ids0"].Stance);
            Assert.Equal(1, casket.RawMotionRoutines["ids1"].Stance);
            Assert.Equal(1, casket.RawMotionRoutines["ids2"].Stance);
            Assert.Equal(1, NpcStanceResolver.ResolveEffectiveStance(casket, 6));
            Assert.Equal(1, NpcStanceResolver.ResolveEffectiveStance(casket, 2));

            var state = new EntityAnimationState();
            foreach (byte sub in new byte[] { 4, 5, 6 })
            {
                for (int i = 0; i < 120; i++) state.Advance(1f / 60f, AnimationCategory.Idle, sub, casket);
            }
            Assert.Equal(1, state.CurrentStance);
            Assert.Same(casket.Animations["1dl0"], state.CurrentClip);
        }

        /// <summary>The furnace names its transitions by previous and new state: 0 to 1 is <c>sp01</c>, its appearance clip <c>pop</c>.</summary>
        [Fact]
        public void SynergyFurnace_TransitionIsNamedByBothStates()
        {
            if (!Directory.Exists(GameDirectory)) return;
            var rm = new ResourceManager(GameDirectory);
            rm.InitializeFileTable();
            var furnace = EntityModelLoader.LoadMonsterModel(2348, rm.LoadDatBytesByFileId)!;
            Assert.StartsWith("pop", NpcStanceResolver.ResolveTransitionClip(furnace, 0, 1, 1)!.Name);
            Assert.Null(NpcStanceResolver.ResolveTransitionClip(furnace, 0, 1, 5));
        }
    }
}
