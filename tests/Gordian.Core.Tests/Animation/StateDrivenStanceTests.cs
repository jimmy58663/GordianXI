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
    }
}
