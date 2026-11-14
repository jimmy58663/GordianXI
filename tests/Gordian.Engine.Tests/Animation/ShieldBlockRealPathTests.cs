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
    /// #546 rounds 1-5: a shield block is three poses at once (round 5, the layering the maintainer set from
    /// <c>Shield_Block_Retail.mp4</c> at about 51.4 s): the hit flinch on the spine, chest and head, the guard stance's legs
    /// (the foot step), and the guard pose <c>gdm</c> on the shield arm, the weapon arm in none of them. The retail model with
    /// a sword (look 5) and a shield (sub look 20), the block reaction played through <see cref="EntityAnimationState"/> over
    /// the battle stance and over the idle stance (a block while not engaged), and the pose the renderer draws
    /// (<see cref="EntityAnimationState.EvaluatePose"/>), sampled in the middle of the block's hold beside the same state
    /// without the block. Model space faces +X with Y down.
    /// </summary>
    public class ShieldBlockRealPathTests
    {
        private const string GameDirectory = @"G:\Program Files (x86)\PlayOnline\SquareEnix\FINAL FANTASY XI";
        private const float Dt = 1f / 144f;

        /// <summary>A shield's sub look: looks 19-63 are the flat shields (about 0.5 by 0.5 yalm, 0.07 thick, on the left arm).</summary>
        private const ushort ShieldLook = 20;

        private sealed record Run(EntityModel Model, ShieldBlockPose.ShieldMount Mount, int Layers,
            SkeletonPoseEvaluator.EvaluatedPose Stance, SkeletonPoseEvaluator.EvaluatedPose Held, SkeletonPoseEvaluator.EvaluatedPose Unblocked);

        /// <summary>The pose before the reaction, in the middle of its hold (8 of the flash's 16 ticks), and the pose then without it.</summary>
        private static Run? Play(CharacterRace race, AnimationCategory stance, ActionResolution resolution = ActionResolution.Block, float distortion = 0.5f)
        {
            if (!Directory.Exists(GameDirectory)) return null;
            var rm = new ResourceManager(GameDirectory);
            rm.InitializeFileTable();
            var grap = new ushort[9];
            grap[6] = 5;
            grap[7] = ShieldLook;
            var model = EntityModelLoader.AssembleCharacter(race, 1, grap, rm.LoadDatBytes, rm.LoadDatBytesByFileId)!;
            Assert.NotNull(model.Shield);
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

            state.EnqueueReaction(new HitReaction(resolution, distortion, 0, true, default, 0, 0, 0, Stopwatch.GetTimestamp()));
            for (int i = 0; i < 19; i++) // 8 ticks of 60 per second at 144 frames per second
            {
                state.Advance(Dt, stance, 0, model);
                plain.Advance(Dt, stance, 0, model);
            }
            return new Run(model, model.Shield!, state.OverlayLayers.Length, before,
                state.EvaluatePose(skeleton, overrides), plain.EvaluatePose(skeleton, overrides));
        }

        private static float Angle(Quaternion a, Quaternion b) =>
            2f * MathF.Acos(Math.Min(1f, MathF.Abs(Quaternion.Dot(Quaternion.Normalize(a), Quaternion.Normalize(b))))) * 180f / MathF.PI;

        private static Quaternion Under(in SkeletonPoseEvaluator.EvaluatedPose pose, int parent, int joint) =>
            Quaternion.Normalize(Quaternion.Conjugate(pose.Rotations[parent]) * pose.Rotations[joint]);

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
        /// The shield is held as <c>gdm</c> played alone holds it relative to the body (the shield joint's model-space rotation
        /// within 6 degrees of it), raised and in front of the chest.
        /// </summary>
        [Theory]
        [MemberData(nameof(Cases))]
        public void ABlock_PlaysTheGuardPoseOnTheShieldArm(CharacterRace race, AnimationCategory stance)
        {
            if (Play(race, stance) is not { } run) return;
            Assert.Equal(3, run.Layers);
            var guard = SkeletonPoseEvaluator.EvaluatePose(run.Model.Skeleton!, run.Model.Animations["gdm"], 0f, false, run.Model.ParentOverrides);
            float off = Angle(run.Held.Rotations[run.Mount.Joint], guard.Rotations[run.Mount.Joint]);
            Assert.True(off < 6f, $"shield {off:F1} degrees from where gdm holds it");

            var (before, _) = ShieldBlockPose.ShieldIn(run.Stance, run.Mount);
            var (held, _) = ShieldBlockPose.ShieldIn(run.Held, run.Mount);
            var chest = run.Held.Translations[run.Mount.Chest];
            Assert.True(held.X > chest.X + 0.1f, $"shield at {held:F2}, chest at {chest:F2}: not in front");
            Assert.True(held.Y < before.Y - 0.05f, $"shield {before:F2} -> {held:F2}: not raised (Y is down)");
        }

        /// <summary>
        /// The weapon arm is in no layer: every joint of it keeps its rotation relative to its parent (it rides on the
        /// flinching chest, a swing goes on), within 0.5 degrees of the unblocked pose.
        /// </summary>
        [Theory]
        [MemberData(nameof(Cases))]
        public void ABlock_LeavesTheWeaponArmAlone(CharacterRace race, AnimationCategory stance)
        {
            if (Play(race, stance) is not { } run) return;
            var skeleton = run.Model.Skeleton!;
            var arm = ShieldBlockPose.WeaponArmMask(skeleton, run.Model.ParentOverrides);
            Assert.Contains(true, arm);
            for (int j = 0; j < arm.Length; j++)
            {
                if (!arm[j]) continue;
                // Engaged, the weapon grip hangs from the hand; at rest the sheathed weapon keeps its skeletal parent (the hip).
                int parent = stance == AnimationCategory.Combat && run.Model.ParentOverrides != null && run.Model.ParentOverrides.TryGetValue(j, out int over)
                    ? over : skeleton.Joints[j].Parent;
                if (parent < 0 || parent == j) continue;
                float off = Angle(Under(run.Held, parent, j), Under(run.Unblocked, parent, j));
                Assert.True(off < 0.5f, $"weapon arm joint {j} turned {off:F1} degrees under its parent in a block");
            }
        }

        /// <summary>
        /// The body reacts: the feet step to the guard stance's (some leg joint 0.04 yalm or more from the unblocked pose;
        /// measured 2026-11-13 0.05-0.4) and the hit flinch turns the chest against the pelvis (more than 5 degrees; measured
        /// 10-23 at distortion 0.5). A block that does no damage (distortion 0) has no flinch.
        /// </summary>
        [Theory]
        [MemberData(nameof(Cases))]
        public void ABlock_StepsAndFlinches(CharacterRace race, AnimationCategory stance)
        {
            if (Play(race, stance) is not { } run) return;
            var skeleton = run.Model.Skeleton!;
            var overrides = run.Model.ParentOverrides;
            float step = Enumerable.Range(0, skeleton.Count).Where(j => !LocomotionLayering.IsUpperBody(skeleton, j, overrides))
                .Max(j => Vector3.Distance(run.Held.Translations[j], run.Unblocked.Translations[j]));
            Assert.True(step >= 0.04f, $"legs moved {step:F3} yalm in a block");

            int pelvis = LocomotionLayering.Pelvis(skeleton), chest = LocomotionLayering.Chest(skeleton);
            float flinch = Angle(Under(run.Held, pelvis, chest), Under(run.Unblocked, pelvis, chest));
            Assert.True(flinch > 5f, $"chest turned {flinch:F1} degrees against the pelvis in a block at distortion 0.5");

            if (Play(race, stance, distortion: 0f) is not { } still) return;
            Assert.Equal(2, still.Layers);
            float none = Angle(Under(still.Held, pelvis, chest), Under(still.Unblocked, pelvis, chest));
            Assert.True(none < 0.5f, $"chest turned {none:F1} degrees in a block without damage");
        }

        /// <summary>A plain guard still plays <c>gdm</c> over the whole body, one pose: the weapon hand comes up.</summary>
        [Theory]
        [InlineData(CharacterRace.HumeMale)]
        [InlineData(CharacterRace.Galka)]
        public void AGuard_StillMovesTheWeaponArm(CharacterRace race)
        {
            if (Play(race, AnimationCategory.Combat, ActionResolution.Guard) is not { } run) return;
            Assert.Equal(1, run.Layers);
            int hand = run.Model.Skeleton!.References[127].Index;
            float moved = Vector3.Distance(run.Held.Translations[hand], run.Unblocked.Translations[hand]);
            Assert.True(moved > 0.1f, $"weapon hand moved {moved:F3} yalm in a guard");
        }
    }
}
