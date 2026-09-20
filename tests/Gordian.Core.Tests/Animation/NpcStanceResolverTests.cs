// tests/Gordian.Core.Tests/Animation/NpcStanceResolverTests.cs
using System.Collections.Generic;
using Gordian.Core.Animation;
using Gordian.Core.Resources.Models;
using Xunit;

namespace Gordian.Core.Tests.Animation
{
    public class NpcStanceResolverTests
    {
        private static EntityModel CreateModelWithClips(params string[] clipNames)
        {
            var model = new EntityModel { Name = "TestMob" };
            foreach (var name in clipNames)
            {
                var clip = new AnimationClip
                {
                    Name = name,
                    NumFrames = 30,
                    KeyFrameDuration = 1.0f
                };
                model.Animations[name] = clip;
            }
            return model;
        }

        [Fact]
        public void ResolveEffectiveStance_Uragnite_TogglesShellAtSub5()
        {
            var uragnite = CreateModelWithClips("shel", "idl0", "btl0", "wlk0", "run0", "1tl0", "gud0", "atm5");

            // AnimationSub 0 or 4 is out of shell
            Assert.Equal(0, NpcStanceResolver.ResolveEffectiveStance(uragnite, 0));
            Assert.Equal(0, NpcStanceResolver.ResolveEffectiveStance(uragnite, 4));

            // AnimationSub 5 is in shell
            Assert.Equal(1, NpcStanceResolver.ResolveEffectiveStance(uragnite, 5));

            // Target clips
            var openClip = NpcStanceResolver.ResolveTargetClip(uragnite, AnimationCategory.Combat, 0);
            Assert.NotNull(openClip);
            Assert.Equal("btl0", openClip.Name);

            var shellClip = NpcStanceResolver.ResolveTargetClip(uragnite, AnimationCategory.Combat, 1);
            Assert.NotNull(shellClip);
            Assert.Equal("1tl0", shellClip.Name);
        }

        [Fact]
        public void ResolveEffectiveStance_Omega_TogglesBipedAtSub1_AndHasTransitions()
        {
            var omega = CreateModelWithClips(
                "omeg", "idl0", "btl0", "wlk0", "run0",
                "1dl0", "1tl0", "1lk0", "1un0",
                "sp00", "sp10", "ded0", "de10"
            );

            // Stance 0 (Quadruped)
            Assert.Equal(0, NpcStanceResolver.ResolveEffectiveStance(omega, 0));
            Assert.Equal("idl0", NpcStanceResolver.ResolveTargetClip(omega, AnimationCategory.Idle, 0)?.Name);
            Assert.Equal("btl0", NpcStanceResolver.ResolveTargetClip(omega, AnimationCategory.Combat, 0)?.Name);
            Assert.Equal("wlk0", NpcStanceResolver.ResolveTargetClip(omega, AnimationCategory.Walk, 0)?.Name);
            Assert.Equal("run0", NpcStanceResolver.ResolveTargetClip(omega, AnimationCategory.Run, 0)?.Name);

            // Stance 1 (Bipedal)
            Assert.Equal(1, NpcStanceResolver.ResolveEffectiveStance(omega, 1));
            Assert.Equal("1dl0", NpcStanceResolver.ResolveTargetClip(omega, AnimationCategory.Idle, 1)?.Name);
            Assert.Equal("1tl0", NpcStanceResolver.ResolveTargetClip(omega, AnimationCategory.Combat, 1)?.Name);
            Assert.Equal("1lk0", NpcStanceResolver.ResolveTargetClip(omega, AnimationCategory.Walk, 1)?.Name);
            Assert.Equal("1un0", NpcStanceResolver.ResolveTargetClip(omega, AnimationCategory.Run, 1)?.Name);

            // Transitions: 0 -> 1 is sp00 (stand-up), 1 -> 0 is sp10 (drop-down)
            var transStand = NpcStanceResolver.ResolveTransitionClip(omega, 0, 1);
            Assert.NotNull(transStand);
            Assert.Equal("sp00", transStand.Name);

            var transDrop = NpcStanceResolver.ResolveTransitionClip(omega, 1, 0);
            Assert.NotNull(transDrop);
            Assert.Equal("sp10", transDrop.Name);
        }

        [Fact]
        public void ResolveEffectiveStance_Hpemde_TogglesOpenMouthAndDiving()
        {
            var hpemde = CreateModelWithClips(
                "hebi", "idl0", "btl0", "wlk0", "run0",
                "1dl0", "1tl0", "1lk0", "1un0",
                "2dl0", "2tl0", "2lk0", "2un0",
                "sp00", "sp10", "sp20", "sp30", "dd00", "dd10"
            );

            // Sub 0 or 6 is normal
            Assert.Equal(0, NpcStanceResolver.ResolveEffectiveStance(hpemde, 0));
            Assert.Equal(0, NpcStanceResolver.ResolveEffectiveStance(hpemde, 6));

            // Sub 3 is open mouth
            Assert.Equal(1, NpcStanceResolver.ResolveEffectiveStance(hpemde, 3));
            Assert.Equal("1dl0", NpcStanceResolver.ResolveTargetClip(hpemde, AnimationCategory.Idle, 1)?.Name);

            // Sub 5 is diving / submerged
            Assert.Equal(2, NpcStanceResolver.ResolveEffectiveStance(hpemde, 5));
            Assert.Equal("2dl0", NpcStanceResolver.ResolveTargetClip(hpemde, AnimationCategory.Idle, 2)?.Name);
            Assert.Equal("2lk0", NpcStanceResolver.ResolveTargetClip(hpemde, AnimationCategory.Walk, 2)?.Name);

            // Transitions
            Assert.Equal("sp00", NpcStanceResolver.ResolveTransitionClip(hpemde, 0, 1)?.Name);
            Assert.Equal("sp10", NpcStanceResolver.ResolveTransitionClip(hpemde, 1, 0)?.Name);
            Assert.Equal("sp20", NpcStanceResolver.ResolveTransitionClip(hpemde, 0, 2)?.Name);
            Assert.Equal("sp30", NpcStanceResolver.ResolveTransitionClip(hpemde, 2, 0)?.Name);
        }

        [Fact]
        public void ResolveEffectiveStance_Adamantoise_GuardsAtSub1()
        {
            var turtle = CreateModelWithClips("adam", "idl0", "btl0", "wlk0", "run0", "1tl0", "gid0", "gud0", "sp00", "sp10");

            Assert.Equal(0, NpcStanceResolver.ResolveEffectiveStance(turtle, 0));
            Assert.Equal(0, NpcStanceResolver.ResolveEffectiveStance(turtle, 2)); // Out of shell
            Assert.Equal(1, NpcStanceResolver.ResolveEffectiveStance(turtle, 1)); // Into shell

            // When in guard stance, combat resolves to 1tl0 (or gid0)
            Assert.Equal("1tl0", NpcStanceResolver.ResolveTargetClip(turtle, AnimationCategory.Combat, 1)?.Name);

            // Transitions
            Assert.Equal("sp00", NpcStanceResolver.ResolveTransitionClip(turtle, 0, 1)?.Name);
            Assert.Equal("sp10", NpcStanceResolver.ResolveTransitionClip(turtle, 1, 0)?.Name);
        }

        [Fact]
        public void ResolveEffectiveStance_ConventionFallback_DetectsMode1Clips()
        {
            var customMob = CreateModelWithClips("idl0", "1dl0", "btl0", "1tl0");

            Assert.Equal(0, NpcStanceResolver.ResolveEffectiveStance(customMob, 0));
            Assert.Equal(1, NpcStanceResolver.ResolveEffectiveStance(customMob, 1));
            Assert.Equal(0, NpcStanceResolver.ResolveEffectiveStance(customMob, 99)); // Unknown sub defaults to 0
        }
    }
}
