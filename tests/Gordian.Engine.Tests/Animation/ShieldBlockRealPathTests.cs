// tests/Gordian.Engine.Tests/Animation/ShieldBlockRealPathTests.cs
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using Gordian.Core.Animation;
using Gordian.Core.Network.Packets;
using Gordian.Core.Resources;
using Gordian.Core.Resources.Graphics;
using Gordian.Core.Resources.Models;
using Gordian.Core.Resources.Tables;
using Xunit;

namespace Gordian.Engine.Tests.Animation
{
    /// <summary>
    /// #546 round 1: a shield block comes from the shield arm. The retail model with a sword (look 5) and a shield (sub
    /// look 20) in the battle stance, the block reaction played through <see cref="EntityAnimationState"/> and the pose the
    /// renderer draws (<see cref="EntityAnimationState.EvaluatePose"/>). Retail reference: the maintainer's
    /// <c>Shield_Block_Retail.mp4</c> (2026-11-13), where the shield arm rises toward the attacker at about 46.2 s and
    /// 51.4 s.
    /// </summary>
    public class ShieldBlockRealPathTests
    {
        private const string GameDirectory = @"G:\Program Files (x86)\PlayOnline\SquareEnix\FINAL FANTASY XI";
        private const float Dt = 1f / 144f;

        /// <summary>A shield's sub look: looks 19-63 are the flat shields (about 0.5 by 0.5 yalm, 0.07 thick, on the left arm).</summary>
        private const ushort ShieldLook = 20;

        private static Vector3 Skin(AnimatedMeshVertex v, SkeletonPoseEvaluator.EvaluatedPose pose)
        {
            var p0 = Vector3.Transform(v.Position0, pose.Rotations[v.Joint0]);
            if (v.Joint1 < 0) return pose.Translations[v.Joint0] + p0;
            var p1 = Vector3.Transform(v.Position1, pose.Rotations[v.Joint1]);
            return p0 + v.Weight0 * pose.Translations[v.Joint0] + p1 + v.Weight1 * pose.Translations[v.Joint1];
        }

        /// <summary>
        /// How far each hand and the shield move at most from the stance while the reaction plays: (left hand, right hand,
        /// shield centre), in yalms.
        /// </summary>
        private static (float Left, float Right, float Shield)? Reaction(CharacterRace race, ActionResolution resolution)
        {
            if (!Directory.Exists(GameDirectory)) return null;
            var rm = new ResourceManager(GameDirectory);
            rm.InitializeFileTable();
            var grap = new ushort[9];
            grap[6] = 5;
            grap[7] = ShieldLook;
            var model = EntityModelLoader.AssembleCharacter(race, 1, grap, rm.LoadDatBytes, rm.LoadDatBytesByFileId)!;
            var skeleton = model.Skeleton!;
            var overrides = model.ParentOverrides;
            var shield = model.AnimatedMeshGroups.Where(g => g.WeaponSlot == 1).SelectMany(g => g.Vertices).ToArray();
            Assert.NotEmpty(shield);

            var state = new EntityAnimationState();
            for (int i = 0; i < 300; i++) state.Advance(Dt, AnimationCategory.Combat, 0, model);
            var stance = state.EvaluatePose(skeleton, overrides);
            Vector3 Hand(SkeletonPoseEvaluator.EvaluatedPose p, int reference) =>
                Skeleton.ResolveJointReference(skeleton, p.Translations, p.Rotations, p.Scales, reference);
            Vector3 Shield(SkeletonPoseEvaluator.EvaluatedPose p) => shield.Aggregate(Vector3.Zero, (s, v) => s + Skin(v, p)) / shield.Length;

            // The stance holds still enough over the reaction's 16 ticks that the stance pose stands for it.
            state.EnqueueReaction(new HitReaction(resolution, 0f, 0, true, default, 0, 0, 0, Stopwatch.GetTimestamp()));
            float left = 0, right = 0, moved = 0;
            for (int i = 0; i < 60; i++)
            {
                state.Advance(Dt, AnimationCategory.Combat, 0, model);
                var pose = state.EvaluatePose(skeleton, overrides);
                left = Math.Max(left, Vector3.Distance(Hand(pose, 126), Hand(stance, 126)));
                right = Math.Max(right, Vector3.Distance(Hand(pose, 127), Hand(stance, 127)));
                moved = Math.Max(moved, Vector3.Distance(Shield(pose), Shield(stance)));
            }
            return (left, right, moved);
        }

        /// <summary>
        /// A block moves the shield hand at least as far as the weapon hand, and carries the shield at least as far as a
        /// guard does. Where the guard is the weapon arm's (every race but Tarutaru and Elvaan female), the block is that guard
        /// mirrored: the shield goes 1.8-5 times as far as in the guard (Hume male 0.87 against 0.17 yalm, Galka 1.16 against
        /// 0.55). Measured 2026-11-13; before, a block was the guard itself, the weapon hand up (Hume male 1.00 yalm, the
        /// shield hand 0.12).
        /// </summary>
        [Theory]
        [InlineData(CharacterRace.HumeMale, true)]
        [InlineData(CharacterRace.HumeFemale, true)]
        [InlineData(CharacterRace.ElvaanMale, true)]
        [InlineData(CharacterRace.ElvaanFemale, false)]
        [InlineData(CharacterRace.TaruMale, false)]
        [InlineData(CharacterRace.TaruFemale, false)]
        [InlineData(CharacterRace.Mithra, true)]
        [InlineData(CharacterRace.Galka, true)]
        public void ABlock_ComesFromTheShieldArm(CharacterRace race, bool mirrored)
        {
            if (Reaction(race, ActionResolution.Block) is not { } block) return;
            var guard = Reaction(race, ActionResolution.Guard)!.Value;
            Assert.True(block.Left >= block.Right - 0.02f, $"block: shield hand {block.Left:F2}, weapon hand {block.Right:F2}");
            Assert.True(block.Shield >= guard.Shield - 0.01f, $"the shield moved {block.Shield:F2} in a block, {guard.Shield:F2} in a guard");
            if (mirrored) Assert.True(block.Shield > 1.5f * guard.Shield, $"the shield moved {block.Shield:F2} in a block, {guard.Shield:F2} in a guard");
        }

        /// <summary>A guard (no shield involved) still comes from the weapon arm, as its pose is authored.</summary>
        [Theory]
        [InlineData(CharacterRace.HumeMale)]
        [InlineData(CharacterRace.Galka)]
        public void AGuard_StillRaisesTheWeaponArm(CharacterRace race)
        {
            if (Reaction(race, ActionResolution.Guard) is not var (left, right, _)) return;
            Assert.True(right > 2f * left, $"weapon hand {right:F2} against shield hand {left:F2}");
        }
    }
}
