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
    /// #546 rounds 1-3: a shield block holds the shield up in front of the chest, upright, its face toward the attacker, as
    /// the maintainer's retail recording <c>Shield_Block_Retail.mp4</c> shows at about 51.4 s (2026-11-13). The retail model
    /// with a sword (look 5) and a shield (sub look 20), the block reaction played through <see cref="EntityAnimationState"/>
    /// over the battle stance and over the idle stance (a block while not engaged), and the pose the renderer draws
    /// (<see cref="EntityAnimationState.EvaluatePose"/>). The shield is measured from its own skinned mesh: its centre and
    /// the normal of its plate, turned to face away from the chest. The model faces +X with Y down.
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

        /// <summary>The points' centre and the normal of their best plane (the covariance's smallest axis).</summary>
        private static (Vector3 Centre, Vector3 Normal) Plate(Vector3[] points)
        {
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
            return (c, n);
        }

        /// <summary>The shield at the block's strongest frame, the chest's position then, and how far the shield moved.</summary>
        private static (Vector3 Centre, Vector3 Outward, Vector3 Chest, float Travel)? Block(
            CharacterRace race, AnimationCategory stance, ActionResolution resolution = ActionResolution.Block)
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
            var shield = model.AnimatedMeshGroups.Where(g => g.WeaponSlot == 1).SelectMany(g => g.Vertices).ToArray();
            Assert.NotEmpty(shield);
            int chest = model.Shield!.Chest;

            var state = new EntityAnimationState();
            for (int i = 0; i < 300; i++) state.Advance(Dt, stance, 0, model);
            var before = state.EvaluatePose(skeleton, overrides);
            var (stanceCentre, _) = Plate(shield.Select(v => Skin(v, before)).ToArray());

            state.EnqueueReaction(new HitReaction(resolution, 0f, 0, true, default, 0, 0, 0, Stopwatch.GetTimestamp()));
            float travel = -1f;
            (Vector3 Centre, Vector3 Outward, Vector3 Chest) best = default;
            for (int i = 0; i < 60; i++)
            {
                state.Advance(Dt, stance, 0, model);
                var pose = state.EvaluatePose(skeleton, overrides);
                var (centre, normal) = Plate(shield.Select(v => Skin(v, pose)).ToArray());
                var outward = Vector3.Dot(normal, centre - pose.Translations[chest]) >= 0f ? normal : -normal;
                float moved = Vector3.Distance(centre, stanceCentre);
                if (moved > travel)
                {
                    travel = moved;
                    best = (centre, outward, pose.Translations[chest]);
                }
            }
            return (best.Centre, best.Outward, best.Chest, travel);
        }

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
        /// At the block's height the shield faces the attacker (its outward normal within about 25 degrees of straight ahead,
        /// not tipped flat) and sits in front of the chest, both engaged and not. Measured 2026-11-13: the face within 15
        /// degrees of ahead on every race, tipped at most 12, the shield 0.25-0.8 yalm in front of the chest at chest height. Round 2's mirrored guard failed this: Hume
        /// male's shield face pointed about 50 degrees off ahead and tipped 25 degrees, and over the idle stance Galka's lay
        /// nearly flat, as the maintainer saw in game (round 3).
        /// </summary>
        [Theory]
        [MemberData(nameof(Cases))]
        public void ABlock_HoldsTheShieldUprightInFront_FacingTheAttacker(CharacterRace race, AnimationCategory stance)
        {
            if (Block(race, stance) is not { } block) return;
            Assert.True(block.Outward.X > 0.9f, $"shield face {block.Outward:F2}: not toward the attacker (+X)");
            Assert.True(MathF.Abs(block.Outward.Y) < 0.35f, $"shield face {block.Outward:F2}: tipped (Y is up and down)");
            Assert.True(block.Centre.X > block.Chest.X + 0.1f, $"shield at {block.Centre:F2}, chest at {block.Chest:F2}: not in front");
        }

        /// <summary>A guard (no shield involved) still plays the guard pose: it does not turn the shield to face ahead.</summary>
        [Theory]
        [InlineData(CharacterRace.HumeMale)]
        [InlineData(CharacterRace.Galka)]
        public void AGuard_DoesNotTurnTheShield(CharacterRace race)
        {
            if (Block(race, AnimationCategory.Combat, ActionResolution.Guard) is not { } guard) return;
            Assert.True(guard.Outward.X < 0.9f, $"shield face {guard.Outward:F2} in a guard");
        }
    }
}
