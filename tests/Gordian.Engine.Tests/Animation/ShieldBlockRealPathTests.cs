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
    /// #546 rounds 1-6: a block and a parry draw the weapon's whole-body reaction pose (<c>gdi6</c> / <c>pyi6</c> from the
    /// battle waist pack), as the two stills of the maintainer's
    /// retail recording <c>Shield_Block_Retail.mp4</c> show: the block a braced lunge with the shield held up in front, the
    /// parry the sword raised across the body with the shield dropped. The retail model with a sword (look 5) and a shield
    /// (sub look 20), the reaction played through <see cref="EntityAnimationState"/> over the battle stance and over the idle
    /// stance, and the pose the renderer draws (<see cref="EntityAnimationState.EvaluatePose"/>), sampled in the hold.
    /// Model space faces +X (the attacker) with Y down.
    /// </summary>
    public class ShieldBlockRealPathTests
    {
        private const string GameDirectory = @"G:\Program Files (x86)\PlayOnline\SquareEnix\FINAL FANTASY XI";
        private const float Dt = 1f / 144f;
        private const ushort ShieldLook = 20;

        private static EntityModel? Load(CharacterRace race)
        {
            if (!Directory.Exists(GameDirectory)) return null;
            var rm = new ResourceManager(GameDirectory);
            rm.InitializeFileTable();
            var grap = new ushort[9];
            grap[6] = 5;
            grap[7] = ShieldLook;
            return EntityModelLoader.AssembleCharacter(race, 1, grap, rm.LoadDatBytes, rm.LoadDatBytesByFileId);
        }

        /// <summary>The state after <paramref name="frames"/> frames of the reaction, from the stance.</summary>
        private static EntityAnimationState Play(EntityModel model, AnimationCategory stance, ActionResolution resolution, int frames)
        {
            var state = new EntityAnimationState();
            for (int i = 0; i < 300; i++) state.Advance(Dt, stance, 0, model);
            state.EnqueueReaction(new HitReaction(resolution, 0.5f, 0, true, default, 0, 0, 0, Stopwatch.GetTimestamp()));
            for (int i = 0; i < frames; i++) state.Advance(Dt, stance, 0, model);
            return state;
        }

        private static Vector3 Skin(AnimatedMeshVertex v, SkeletonPoseEvaluator.EvaluatedPose pose)
        {
            var p0 = Vector3.Transform(v.Position0, pose.Rotations[v.Joint0]);
            if (v.Joint1 < 0) return pose.Translations[v.Joint0] + p0;
            var p1 = Vector3.Transform(v.Position1, pose.Rotations[v.Joint1]);
            return p0 + v.Weight0 * pose.Translations[v.Joint0] + p1 + v.Weight1 * pose.Translations[v.Joint1];
        }

        private static float Angle(Quaternion a, Quaternion b) =>
            2f * MathF.Acos(Math.Min(1f, MathF.Abs(Quaternion.Dot(Quaternion.Normalize(a), Quaternion.Normalize(b))))) * 180f / MathF.PI;

        /// <summary>The shield's centre and the normal of its plate (the covariance's smallest axis), turned away from <paramref name="from"/>.</summary>
        private static (Vector3 Centre, Vector3 Outward) Shield(EntityModel model, SkeletonPoseEvaluator.EvaluatedPose pose, Vector3 from)
        {
            var points = model.AnimatedMeshGroups.Where(g => g.WeaponSlot == 1).SelectMany(g => g.Vertices).Select(v => Skin(v, pose)).ToArray();
            Assert.NotEmpty(points);
            var c = points.Aggregate(Vector3.Zero, (a, b) => a + b) / points.Length;
            float xx = 0, xy = 0, xz = 0, yy = 0, yz = 0, zz = 0;
            foreach (var p in points)
            {
                var d = p - c;
                xx += d.X * d.X; xy += d.X * d.Y; xz += d.X * d.Z; yy += d.Y * d.Y; yz += d.Y * d.Z; zz += d.Z * d.Z;
            }
            Assert.True(Matrix4x4.Invert(new Matrix4x4(xx + 1e-6f, xy, xz, 0, xy, yy + 1e-6f, yz, 0, xz, yz, zz + 1e-6f, 0, 0, 0, 0, 1), out var inverse));
            var n = Vector3.Normalize(new Vector3(1f, 0.3f, 0.2f));
            for (int i = 0; i < 60; i++) n = Vector3.Normalize(Vector3.TransformNormal(n, inverse));
            return (c, Vector3.Dot(n, c - from) >= 0f ? n : -n);
        }

        /// <summary>Whether the joint is a weapon grip the loader re-parents into a hand, or hangs below one.</summary>
        private static bool HangsFromAGrip(Skeleton skeleton, System.Collections.Generic.IReadOnlyDictionary<int, int> overrides, int joint)
        {
            for (int guard = 0; joint >= 0 && joint < skeleton.Count && guard <= skeleton.Count; guard++)
            {
                if (overrides.ContainsKey(joint)) return true;
                int parent = skeleton.Joints[joint].Parent;
                joint = parent == joint ? -1 : parent;
            }
            return false;
        }

        public static TheoryData<CharacterRace, AnimationCategory, ActionResolution> Cases()
        {
            var data = new TheoryData<CharacterRace, AnimationCategory, ActionResolution>();
            foreach (var race in new[] { CharacterRace.HumeMale, CharacterRace.HumeFemale, CharacterRace.ElvaanMale, CharacterRace.ElvaanFemale,
                         CharacterRace.TaruMale, CharacterRace.TaruFemale, CharacterRace.Mithra, CharacterRace.Galka })
            {
                foreach (var stance in new[] { AnimationCategory.Combat, AnimationCategory.Idle })
                {
                    data.Add(race, stance, ActionResolution.Block);
                    data.Add(race, stance, ActionResolution.Parry);
                }
            }
            return data;
        }

        /// <summary>
        /// In the hold the whole body is the weapon's reaction pose (every joint within 1 degree of the whole-body pose
        /// evaluated on its own; at rest the sheathed weapon stays on the hip), whatever stance it
        /// came from, and the feet stand on the
        /// ground (the lowest point of the body within 0.03 yalm of it: the lunge's crouch comes from the pose's joint
        /// translations).
        /// </summary>
        [Theory]
        [MemberData(nameof(Cases))]
        public void TheHold_IsTheWeaponsWholeBodyPose(CharacterRace race, AnimationCategory stance, ActionResolution resolution)
        {
            if (Load(race) is not { } model) return;
            var skeleton = model.Skeleton!;
            var overrides = model.ParentOverrides;
            var state = Play(model, stance, resolution, 30); // 12.5 ticks: inside the 16 held
            var layers = state.OverlayLayers.ToArray();
            Assert.Single(layers);
            Assert.Equal(resolution == ActionResolution.Parry ? "pyi6" : "gdi6", layers[0].Clip.Name);
            var held = state.EvaluatePose(skeleton, overrides);
            var expected = SkeletonPoseEvaluator.EvaluatePose(skeleton, layers[0].Clip, 0f, false, overrides);
            for (int j = 0; j < skeleton.Count; j++)
            {
                // At rest the sheathed weapon's grip keeps its skeletal parent (the hip), not the hand.
                if (stance == AnimationCategory.Idle && overrides != null && HangsFromAGrip(skeleton, overrides, j)) continue;
                float off = Angle(held.Rotations[j], expected.Rotations[j]);
                Assert.True(off < 1f, $"joint {j} {off:F1} degrees from the reaction pose");
            }
            var body = model.AnimatedMeshGroups.Where(g => g.WeaponSlot < 0).SelectMany(g => g.Vertices).ToArray();
            float lowest = body.Max(v => Skin(v, held).Y);
            Assert.True(MathF.Abs(lowest) < 0.03f, $"feet {lowest:F3} yalm off the ground (Y down)");
        }

        /// <summary>
        /// The block holds the shield up in front of the chest, its face toward the attacker, as retail's still does.
        /// Measured 2026-11-13 (sword pack, engaged): the shield 0.24-1.0 yalm in front of the chest (Tarutaru 0.24, Galka
        /// 1.0), level with it to 0.27 above, its face 0.42-0.96 toward the attacker (Galka's tipped up about 30 degrees).
        /// </summary>
        [Theory]
        [MemberData(nameof(Cases))]
        public void ABlock_HoldsTheShieldUpInFront(CharacterRace race, AnimationCategory stance, ActionResolution resolution)
        {
            if (resolution != ActionResolution.Block || Load(race) is not { } model) return;
            var pose = Play(model, stance, resolution, 30).EvaluatePose(model.Skeleton!, model.ParentOverrides);
            var chest = pose.Translations[LocomotionLayering.Chest(model.Skeleton!)];
            var (centre, outward) = Shield(model, pose, chest);
            Assert.True(centre.X > chest.X + 0.15f, $"shield at {centre - chest:F2} from the chest: not in front");
            Assert.True(centre.Y < chest.Y + 0.1f, $"shield at {centre - chest:F2} from the chest: below it (Y down)");
            Assert.True(outward.X > 0.4f, $"shield face {outward:F2}: not toward the attacker (+X)");
        }

        /// <summary>
        /// The parry raises the weapon hand out toward the attacker, at the chest or above, and drops the shield to the side
        /// or behind, as retail's still does. Measured 2026-11-13 (engaged): the weapon hand 0.18-0.98 yalm in front
        /// of the chest and 0.06-0.46 above it (Galka's reaches furthest and lowest), the shield from 0.41 behind the chest to 0.09 in front (Tarutaru, at its side).
        /// </summary>
        [Theory]
        [MemberData(nameof(Cases))]
        public void AParry_RaisesTheSwordAndDropsTheShield(CharacterRace race, AnimationCategory stance, ActionResolution resolution)
        {
            if (resolution != ActionResolution.Parry || Load(race) is not { } model) return;
            var skeleton = model.Skeleton!;
            var pose = Play(model, stance, resolution, 30).EvaluatePose(skeleton, model.ParentOverrides);
            var chest = pose.Translations[LocomotionLayering.Chest(skeleton)];
            var hand = pose.Translations[skeleton.References[127].Index];
            var (centre, _) = Shield(model, pose, chest);
            Assert.True(hand.Y < chest.Y + 0.05f && hand.X > chest.X + 0.1f, $"weapon hand at {hand - chest:F2} from the chest: not raised toward the attacker (Y down)");
            Assert.True(centre.X < chest.X + 0.15f, $"shield at {centre - chest:F2} from the chest: held out in front");
            Assert.True(centre.Y > chest.Y - 0.05f, $"shield at {centre - chest:F2} from the chest: not dropped");
        }

        /// <summary>
        /// The pose comes in over 2 ticks, holds the routine's 16 and goes out over 4: full weight from 3 to 17 ticks, nothing
        /// left after 23 (60 ticks a second).
        /// </summary>
        [Theory]
        [InlineData(CharacterRace.HumeMale)]
        [InlineData(CharacterRace.Galka)]
        public void ThePose_IsHeldForTheRoutinesTicks(CharacterRace race)
        {
            if (Load(race) is not { } model) return;
            foreach (int ticks in new[] { 3, 10, 17 })
            {
                var layers = Play(model, AnimationCategory.Combat, ActionResolution.Block, (int)MathF.Round(ticks * 144f / 60f)).OverlayLayers.ToArray();
                Assert.Single(layers);
                Assert.True(layers[0].Weight > 0.99f, $"weight {layers[0].Weight:F2} at tick {ticks}");
            }
            Assert.Empty(Play(model, AnimationCategory.Combat, ActionResolution.Block, (int)MathF.Round(23 * 144f / 60f)).OverlayLayers.ToArray());
        }
    }
}
