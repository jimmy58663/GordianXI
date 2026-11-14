// tests/Gordian.Engine.Tests/Animation/ShieldBlockRealPathTests.cs
using System;
using System.Diagnostics;
using System.IO;
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
    /// #546 rounds 1-4: a shield block plays the guard pose <c>gdm</c> on the shield arm only, the other arm and the body
    /// staying on whatever is playing (the maintainer's round 4 call, from XI Model Viewer, where <c>gdm</c> brings both hands
    /// up to the middle of the chest; retail <c>Shield_Block_Retail.mp4</c> at about 51.4 s pulls the shield across the
    /// chest toward the attacker). The retail model with a sword (look 5) and a shield (sub look 20), the block reaction
    /// played through <see cref="EntityAnimationState"/> over the battle stance and over the idle stance (a block while not
    /// engaged), and the pose the renderer draws (<see cref="EntityAnimationState.EvaluatePose"/>). Model space faces +X
    /// with Y down.
    /// </summary>
    public class ShieldBlockRealPathTests
    {
        private const string GameDirectory = @"G:\Program Files (x86)\PlayOnline\SquareEnix\FINAL FANTASY XI";
        private const float Dt = 1f / 144f;

        /// <summary>A shield's sub look: looks 19-63 are the flat shields (about 0.5 by 0.5 yalm, 0.07 thick, on the left arm).</summary>
        private const ushort ShieldLook = 20;

        private sealed record Run(EntityModel Model, ShieldBlockPose.ShieldMount Mount,
            SkeletonPoseEvaluator.EvaluatedPose Stance, SkeletonPoseEvaluator.EvaluatedPose Peak, SkeletonPoseEvaluator.EvaluatedPose Unblocked);

        /// <summary>
        /// The pose before the reaction, at its strongest frame (the shield arm furthest from the stance), and the pose at that
        /// same moment without the reaction.
        /// </summary>
        private static Run? Play(CharacterRace race, AnimationCategory stance, ActionResolution resolution = ActionResolution.Block)
        {
            if (!Directory.Exists(GameDirectory)) return null;
            var rm = new ResourceManager(GameDirectory);
            rm.InitializeFileTable();
            var grap = new ushort[9];
            grap[6] = 5;
            grap[7] = ShieldLook;
            var model = EntityModelLoader.AssembleCharacter(race, 1, grap, rm.LoadDatBytes, rm.LoadDatBytesByFileId)!;
            Assert.NotNull(model.Shield);
            var mount = model.Shield!;
            var skeleton = model.Skeleton!;
            var overrides = model.ParentOverrides;

            var state = new EntityAnimationState();
            var plain = new EntityAnimationState();
            for (int i = 0; i < 300; i++)
            {
                state.Advance(Dt, stance, 0, model);
                plain.Advance(Dt, stance, 0, model);
            }
            var before = state.EvaluatePose(skeleton, overrides);

            state.EnqueueReaction(new HitReaction(resolution, 0f, 0, true, default, 0, 0, 0, Stopwatch.GetTimestamp()));
            float most = -1f;
            SkeletonPoseEvaluator.EvaluatedPose peak = before, unblocked = before;
            for (int i = 0; i < 60; i++)
            {
                state.Advance(Dt, stance, 0, model);
                plain.Advance(Dt, stance, 0, model);
                var pose = state.EvaluatePose(skeleton, overrides);
                float moved = Angle(Relative(pose, mount), Relative(before, mount)) + Angle(RightArm(pose, model), RightArm(before, model));
                if (moved > most)
                {
                    most = moved;
                    peak = pose;
                    unblocked = plain.EvaluatePose(skeleton, overrides);
                }
            }
            return new Run(model, mount, before, peak, unblocked);
        }

        /// <summary>The shield joint's rotation relative to the chest (the parent of the shield arm).</summary>
        private static Quaternion Relative(in SkeletonPoseEvaluator.EvaluatedPose pose, ShieldBlockPose.ShieldMount mount) =>
            Quaternion.Normalize(Quaternion.Conjugate(pose.Rotations[mount.Chest]) * pose.Rotations[mount.Joint]);

        private static Quaternion RightArm(in SkeletonPoseEvaluator.EvaluatedPose pose, EntityModel model) =>
            pose.Rotations[model.Skeleton!.References[127].Index];

        private static float Angle(Quaternion a, Quaternion b) =>
            2f * MathF.Acos(Math.Min(1f, MathF.Abs(Quaternion.Dot(Quaternion.Normalize(a), Quaternion.Normalize(b))))) * 180f / MathF.PI;

        public static TheoryData<CharacterRace, AnimationCategory> Cases()
        {
            var data = new TheoryData<CharacterRace, AnimationCategory>();
            foreach (var race in new[] { CharacterRace.HumeMale, CharacterRace.HumeFemale, CharacterRace.ElvaanMale, CharacterRace.ElvaanFemale,
                         CharacterRace.TaruMale, CharacterRace.TaruFemale, CharacterRace.Mithra, CharacterRace.Galka })
            {
                data.Add(race, AnimationCategory.Combat);
                data.Add(race, AnimationCategory.Idle);
            }
            return data;
        }

        /// <summary>
        /// At the block's height the shield is held as <c>gdm</c> played alone holds it relative to the body (the shield
        /// joint's model-space rotation within 6 degrees of it), and it comes up and in front of the chest.
        /// </summary>
        [Theory]
        [MemberData(nameof(Cases))]
        public void ABlock_PlaysTheGuardPoseOnTheShieldArm(CharacterRace race, AnimationCategory stance)
        {
            if (Play(race, stance) is not { } run) return;
            var guard = SkeletonPoseEvaluator.EvaluatePose(run.Model.Skeleton!, run.Model.Animations["gdm"], 0f, false, run.Model.ParentOverrides);
            float off = Angle(run.Peak.Rotations[run.Mount.Joint], guard.Rotations[run.Mount.Joint]);
            Assert.True(off < 6f, $"shield {off:F1} degrees from where gdm holds it");

            var (before, _) = ShieldBlockPose.ShieldIn(run.Stance, run.Mount);
            var (peak, _) = ShieldBlockPose.ShieldIn(run.Peak, run.Mount);
            var chest = run.Peak.Translations[run.Mount.Chest];
            Assert.True(peak.X > chest.X + 0.1f, $"shield at {peak:F2}, chest at {chest:F2}: not in front");
            Assert.True(peak.Y < before.Y - 0.05f, $"shield {before:F2} -> {peak:F2}: not raised (Y is down)");
        }

        /// <summary>
        /// The block moves the shield arm alone: the weapon hand and the head are where they would be without it.
        /// </summary>
        [Theory]
        [MemberData(nameof(Cases))]
        public void ABlock_LeavesTheOtherArmAndTheBodyAlone(CharacterRace race, AnimationCategory stance)
        {
            if (Play(race, stance) is not { } run) return;
            var skeleton = run.Model.Skeleton!;
            foreach (int reference in new[] { 127, 5 })
            {
                int joint = skeleton.References[reference].Index;
                float moved = Vector3.Distance(run.Peak.Translations[joint], run.Unblocked.Translations[joint]);
                Assert.True(moved < 0.01f, $"reference {reference} (joint {joint}) moved {moved:F3} yalm in a block");
            }
        }

        /// <summary>A plain guard still plays <c>gdm</c> over the whole body: the weapon hand comes up.</summary>
        [Theory]
        [InlineData(CharacterRace.HumeMale)]
        [InlineData(CharacterRace.Galka)]
        public void AGuard_StillMovesTheWeaponArm(CharacterRace race)
        {
            if (Play(race, AnimationCategory.Combat, ActionResolution.Guard) is not { } run) return;
            int hand = run.Model.Skeleton!.References[127].Index;
            float moved = Vector3.Distance(run.Peak.Translations[hand], run.Unblocked.Translations[hand]);
            Assert.True(moved > 0.1f, $"weapon hand moved {moved:F3} yalm in a guard");
        }
    }
}
