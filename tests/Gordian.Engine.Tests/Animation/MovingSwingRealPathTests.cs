// tests/Gordian.Engine.Tests/Animation/MovingSwingRealPathTests.cs
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Gordian.Core.Animation;
using Gordian.Core.Config;
using Gordian.Core.Network.Packets;
using Gordian.Core.Resources;
using Gordian.Core.Resources.Graphics;
using Gordian.Core.Resources.Models;
using Gordian.Core.Resources.Tables;
using Gordian.Core.World;
using Gordian.Engine.Actions;
using Gordian.Engine.Animation;
using Gordian.Engine.Input;
using Xunit;

namespace Gordian.Engine.Tests.Animation
{
    /// <summary>
    /// Melee swings while moving on the real path (#511 round 5): the retail model, the locomotion controller locked on and
    /// driven by keys, the classifier, the animation playback and <see cref="EntityAnimationState.EvaluatePose"/>, the pose
    /// the renderer draws. The maintainer's monk is an Elvaan male with hand-to-hand look 484 in both hands (log
    /// 2026-11-13, 0x051 face/race 0x0302); the dual-wield case is a Hume male with two daggers (look 157).
    /// </summary>
    public class MovingSwingRealPathTests
    {
        private const string GameDirectory = @"G:\Program Files (x86)\PlayOnline\SquareEnix\FINAL FANTASY XI";
        private const float Dt = 1f / 144f;

        private static Vector3 Skin(AnimatedMeshVertex v, SkeletonPoseEvaluator.EvaluatedPose pose)
        {
            var p0 = Vector3.Transform(v.Position0, pose.Rotations[v.Joint0]);
            if (v.Joint1 < 0) return pose.Translations[v.Joint0] + p0;
            var p1 = Vector3.Transform(v.Position1, pose.Rotations[v.Joint1]);
            return p0 + v.Weight0 * pose.Translations[v.Joint0] + p1 + v.Weight1 * pose.Translations[v.Joint1];
        }

        /// <summary>
        /// The retail model on the real path: the local player engaged, locked on and moving with <paramref name="key"/> held,
        /// a frame stepping the controller, the classifier and the playback; null without the game install.
        /// </summary>
        private static (EntityModel Model, PlayerEntity Me, Func<AnimationCategory> Frame, AnimationClip? Locomotion)? Setup(
            CharacterRace race, ushort main, ushort sub, GordianKey key, AnimationCategory expected, bool walking)
        {
            if (!Directory.Exists(GameDirectory)) return null;
            var rm = new ResourceManager(GameDirectory);
            rm.InitializeFileTable();
            var grap = new ushort[9];
            grap[6] = main;
            grap[7] = sub;
            var model = EntityModelLoader.AssembleCharacter(race, 1, grap, rm.LoadDatBytes, rm.LoadDatBytesByFileId)!;

            var world = new WorldState();
            var player = new LocalPlayerState { ServerId = 0x12345678 };
            var me = new PlayerEntity(player.ServerId, 1) { Name = "Me", IsSpawned = true, Hpp = 100 };
            world.UpsertEntity(me);
            var status = new byte[0x60];
            status[38] = 100;
            status[44] = 1;
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(status.AsSpan(40), 50u | (50u << 17));
            player.UpdateFromCharStatus(new S2C_0x037_CharStatus(status));
            var input = new InputState();
            Task Send(ReadOnlyMemory<byte> m, bool u) => Task.CompletedTask;
            var service = new PlayerActionService(new SessionProfile(), world, player, new CombatPacketModule(new CombatState(), player, Send),
                new ChatPacketModule(Send), new PartyPacketModule(new PartyState(), Send), new EntityPacketModule(world, player, Send),
                new LifecyclePacketModule(new SessionProfile(), Send), Send);
            var controller = new PlayerLocomotionController(input, InputProfile.CreateCompact(), world, player, service);
            var mob = new WorldEntity(0x9999, 2, EntityType.Monster) { Position = new Vector3(0f, 0f, 10f), IsSpawned = true };
            world.UpsertEntity(mob);
            service.SetTarget(mob);
            service.UiSettings.SetValue(Gordian.Engine.Ui.StockUiSettingKey.AutoLockOnEngage, 0);
            service.Combat!.Engage(mob.ServerId, mob.TargetIndex);
            service.SetLockOn(true);
            controller.CameraYaw = 270f;

            AnimationCategory Frame()
            {
                controller.Update(TimeSpan.FromSeconds(Dt));
                var category = AnimationStateClassifier.Classify(me, service.Combat!.IsEngaged, isLocalPlayer: true);
                me.Animation.Advance(Dt, category, me.AnimationSub, model);
                return category;
            }

            input.IsWalking = walking;
            input.SetKeyDown(key);
            for (int i = 0; i < 100; i++) Frame();
            Assert.Equal(expected, me.Animation.Current);
            Assert.Equal(walking, me.Speed < 40); // the walk moves at half the run's speed
            var locomotion = NpcStanceResolver.ResolveTargetClip(model, expected);
            Assert.Same(locomotion, me.Animation.CurrentClip);
            return (model, me, Frame, locomotion);
        }

        /// <summary>
        /// Round 7: a hand-to-hand round while moving alternates the hands. The hand-to-hand packs have moving swings for the
        /// main hand only (Elvaan male: atf0 / atb0 / atl0 / atr0), so an off-hand hit plays the standing off-hand punch
        /// (bti0 / bti1: at3 / at4) on the upper body over the strafe, and the left hand punches.
        /// </summary>
        [Theory]
        [InlineData(GordianKey.D, AnimationCategory.CombatMoveRight, false)]
        [InlineData(GordianKey.S, AnimationCategory.CombatMoveBackward, false)]
        [InlineData(GordianKey.A, AnimationCategory.CombatMoveLeft, true)]
        public void AMovingHandToHandRound_PunchesWithBothHands(GordianKey key, AnimationCategory expected, bool walking)
        {
            if (Setup(CharacterRace.ElvaanMale, 484, 484, key, expected, walking) is not var (model, me, Frame, locomotion)) return;
            var skeleton = model.Skeleton!;
            int leftHand = skeleton.References[126].Index, rightHand = skeleton.References[127].Index, chest = LocomotionLayering.Chest(skeleton);
            for (int h = 0; h < 4; h++)
            {
                me.Animation.EnqueueAction(new ActionRequest
                {
                    ActorId = me.ServerId, Motion = ActionMotion.Swing, Category = ActionCategory.BasicAttack, SubKind = (ushort)(h % 2),
                    ReceivedTimestamp = Stopwatch.GetTimestamp()
                });
            }

            var routines = new HashSet<string>();
            float leftReachOffHand = 0f, leftReachMainHand = 0f, rightReachOffHand = 0f;
            var overrides = model.ParentOverrides;
            for (int i = 0; i < 144 * 4; i++)
            {
                Frame();
                if (me.Animation.ActiveRoutine is not { } routine) continue;
                routines.Add(routine.Name);
                Assert.Same(locomotion, me.Animation.LowerBodyClip); // the legs keep moving under either hand's punch
                var pose = me.Animation.EvaluatePose(skeleton, overrides);
                float left = Vector3.Distance(pose.Translations[leftHand], pose.Translations[chest]);
                float right = Vector3.Distance(pose.Translations[rightHand], pose.Translations[chest]);
                if (routine.Name.StartsWith("bti", StringComparison.Ordinal))
                {
                    leftReachOffHand = Math.Max(leftReachOffHand, left);
                    rightReachOffHand = Math.Max(rightReachOffHand, right);
                }
                else
                {
                    leftReachMainHand = Math.Max(leftReachMainHand, left);
                }
            }

            string moving = expected switch
            {
                AnimationCategory.CombatMoveRight => "atr0",
                AnimationCategory.CombatMoveLeft => "atl0",
                _ => "atb0"
            };
            Assert.Contains(moving, routines); // the main hand's moving swing
            Assert.Contains(routines, r => r.StartsWith("bti", StringComparison.Ordinal)); // and the off hand's punch
            Assert.True(leftReachOffHand > leftReachMainHand + 0.1f, $"left hand reach: off-hand punch {leftReachOffHand:F2}, main-hand swing {leftReachMainHand:F2}");
            Assert.True(leftReachOffHand > rightReachOffHand, $"off-hand punch: left {leftReachOffHand:F2}, right {rightReachOffHand:F2}");
        }

        private static ActionRequest Swing(PlayerEntity me, ushort subKind) => new()
        {
            ActorId = me.ServerId, Motion = ActionMotion.Swing, Category = ActionCategory.BasicAttack, SubKind = subKind,
            ReceivedTimestamp = Stopwatch.GetTimestamp()
        };

        /// <summary>
        /// The horizontal direction (degrees, model space) the left hand reaches furthest from the spine root during the next
        /// off-hand punch, played on <paramref name="frame"/> after an off-hand hit is queued.
        /// </summary>
        private static float OffHandPunchAim(EntityModel model, PlayerEntity me, Func<AnimationCategory> frame)
        {
            var skeleton = model.Skeleton!;
            int leftHand = skeleton.References[126].Index, spine = LocomotionLayering.UpperBodyRoot(skeleton);
            me.Animation.EnqueueAction(Swing(me, 1));
            float best = -1f, aim = 0f;
            for (int i = 0; i < 144 * 2; i++)
            {
                frame();
                if (me.Animation.ActiveRoutine?.Name.StartsWith("bti", StringComparison.Ordinal) != true) continue;
                var pose = me.Animation.EvaluatePose(skeleton, model.ParentOverrides);
                var reach = pose.Translations[leftHand] - pose.Translations[spine];
                float distance = new Vector2(reach.X, reach.Z).Length();
                if (distance > best)
                {
                    best = distance;
                    aim = MathF.Atan2(reach.Z, reach.X) * 180f / MathF.PI;
                }
            }
            Assert.True(best > 0f, "no off-hand punch played");
            return aim;
        }

        /// <summary>
        /// Round 8: the off-hand punch played while strafing (the standing clip over the strafe) aims where it does standing,
        /// at the target, not off to the side along the strafe's twisted chest.
        /// </summary>
        [Theory]
        [InlineData(GordianKey.D, AnimationCategory.CombatMoveRight)]
        [InlineData(GordianKey.A, AnimationCategory.CombatMoveLeft)]
        public void TheOffHandPunchWhileStrafing_AimsAsItDoesStanding(GordianKey key, AnimationCategory strafe)
        {
            if (Setup(CharacterRace.ElvaanMale, 484, 484, key, strafe, false) is not var (model, me, Frame, _)) return;
            float strafing = OffHandPunchAim(model, me, Frame);

            if (Setup(CharacterRace.ElvaanMale, 484, 484, GordianKey.W, AnimationCategory.CombatRun, false) is not var (standingModel, standingMe, _, _)) return;
            AnimationCategory Standing()
            {
                standingMe.Animation.Advance(Dt, AnimationCategory.Combat, 0, standingModel);
                return AnimationCategory.Combat;
            }
            for (int i = 0; i < 100; i++) Standing();
            float standing = OffHandPunchAim(standingModel, standingMe, Standing);

            float difference = MathF.Abs(((strafing - standing) % 360f + 540f) % 360f - 180f);
            Assert.True(difference < 15f, $"off-hand punch aims {strafing:F0} degrees strafing, {standing:F0} standing");
        }

        /// <summary>
        /// Round 8: the main-hand weapon (its grip re-parented into the right hand) stays rigidly in the hand through swings and
        /// hit reactions, standing and strafing. The off-hand claw of a hand-to-hand weapon is carried by its mount joint, not
        /// re-parented, and drifts up to about 0.6 yalm in hit reactions on main as well: tracked separately, not part of #511.
        /// </summary>
        [Theory]
        [InlineData(GordianKey.D, AnimationCategory.CombatMoveRight, true)]
        [InlineData(GordianKey.D, AnimationCategory.CombatMoveRight, false)]
        public void TheMainHandWeapon_StaysInTheHand_ThroughSwingsAndHitReactions(GordianKey key, AnimationCategory strafe, bool moving)
        {
            if (Setup(CharacterRace.ElvaanMale, 484, 484, key, strafe, false) is not var (model, me, Frame, _)) return;
            var skeleton = model.Skeleton!;
            var overrides = model.ParentOverrides!;
            int rightHand = skeleton.References[127].Index;
            var claw = model.AnimatedMeshGroups.Where(g => g.WeaponSlot == 0).SelectMany(g => g.Vertices).Take(40).ToArray();
            Func<AnimationCategory> step = moving ? Frame : () =>
            {
                me.Animation.Advance(Dt, AnimationCategory.Combat, 0, model);
                return AnimationCategory.Combat;
            };
            for (int i = 0; i < 60; i++) step();
            Vector3 InHand(SkeletonPoseEvaluator.EvaluatedPose pose, AnimatedMeshVertex v) =>
                Vector3.Transform(Skin(v, pose) - pose.Translations[rightHand], Quaternion.Inverse(pose.Rotations[rightHand]));
            var first = me.Animation.EvaluatePose(skeleton, overrides);
            var held = claw.Select(v => InHand(first, v)).ToArray();
            float drift = 0f;
            for (int f = 0; f < 144 * 5; f++)
            {
                if (f % 144 == 10) me.Animation.EnqueueAction(Swing(me, (ushort)(f / 144 % 2)));
                if (f % 144 == 100) me.Animation.EnqueueReaction(new HitReaction(default, 1f, 0, f % 288 == 100, default, 0, 0, 0, Stopwatch.GetTimestamp(), default, default));
                step();
                var pose = me.Animation.EvaluatePose(skeleton, overrides);
                for (int k = 0; k < claw.Length; k++) drift = Math.Max(drift, Vector3.Distance(InHand(pose, claw[k]), held[k]));
            }
            Assert.True(drift < 0.002f, $"main-hand claw drifted {drift:F3} yalm from the hand");
        }

        [Theory]
        [InlineData(CharacterRace.ElvaanMale, (ushort)484, (ushort)484, GordianKey.D, AnimationCategory.CombatMoveRight)]
        [InlineData(CharacterRace.ElvaanMale, (ushort)484, (ushort)484, GordianKey.A, AnimationCategory.CombatMoveLeft)]
        [InlineData(CharacterRace.ElvaanMale, (ushort)484, (ushort)484, GordianKey.S, AnimationCategory.CombatMoveBackward)]
        [InlineData(CharacterRace.ElvaanMale, (ushort)484, (ushort)484, GordianKey.W, AnimationCategory.CombatRun)]
        [InlineData(CharacterRace.HumeMale, (ushort)157, (ushort)157, GordianKey.D, AnimationCategory.CombatMoveRight)]
        [InlineData(CharacterRace.HumeMale, (ushort)157, (ushort)157, GordianKey.S, AnimationCategory.CombatMoveBackward)]
        // Walking (round 5 correction): the same, at the walk's pace.
        [InlineData(CharacterRace.ElvaanMale, (ushort)484, (ushort)484, GordianKey.W, AnimationCategory.CombatWalk, true)]
        [InlineData(CharacterRace.ElvaanMale, (ushort)484, (ushort)484, GordianKey.D, AnimationCategory.CombatMoveRight, true)]
        [InlineData(CharacterRace.ElvaanMale, (ushort)484, (ushort)484, GordianKey.A, AnimationCategory.CombatMoveLeft, true)]
        [InlineData(CharacterRace.ElvaanMale, (ushort)484, (ushort)484, GordianKey.S, AnimationCategory.CombatMoveBackward, true)]
        [InlineData(CharacterRace.HumeMale, (ushort)157, (ushort)157, GordianKey.W, AnimationCategory.CombatWalk, true)]
        public void ASwingWhileMoving_KeepsTheLegs_AndTheWeaponsInTheHands(CharacterRace race, ushort main, ushort sub, GordianKey key, AnimationCategory expected,
            bool walking = false)
        {
            if (Setup(race, main, sub, key, expected, walking) is not var (model, me, Frame, locomotion)) return;
            var skeleton = model.Skeleton!;

            // An auto-attack round: three hits, main and off hand.
            for (int h = 0; h < 3; h++)
            {
                me.Animation.EnqueueAction(new ActionRequest
                {
                    ActorId = me.ServerId, Motion = ActionMotion.Swing, Category = ActionCategory.BasicAttack, SubKind = (ushort)(h % 2),
                    ReceivedTimestamp = Stopwatch.GetTimestamp()
                });
            }

            var overrides = model.ParentOverrides;
            var weapons = model.AnimatedMeshGroups.Where(g => g.WeaponSlot is 0 or 1).SelectMany(g => g.Vertices).ToArray();
            Assert.NotEmpty(weapons);
            int leftHand = skeleton.References[126].Index, rightHand = skeleton.References[127].Index;
            var legJoints = Enumerable.Range(0, skeleton.Count).Where(j => !LocomotionLayering.IsUpperBody(skeleton, j, overrides)).ToArray();
            Assert.Contains(LocomotionLayering.Pelvis(skeleton), legJoints);
            int swingFrames = 0;
            for (int i = 0; i < 144 * 3; i++)
            {
                Assert.Equal(expected, Frame());
                if (me.Animation.ActiveRoutine is not { } routine || !routine.Name.StartsWith("at", StringComparison.Ordinal) && !routine.Name.StartsWith("bt", StringComparison.Ordinal)) continue;
                swingFrames++;

                // The legs: exactly the walk, run, strafe or back step, on its own clock.
                Assert.Same(locomotion, me.Animation.LowerBodyClip);
                var shown = me.Animation.EvaluatePose(skeleton, overrides);
                var legs = SkeletonPoseEvaluator.EvaluatePose(skeleton, locomotion, me.Animation.LowerBodyElapsedSeconds, true, overrides);
                // The torso too: the spine up to the chest keeps the locomotion's twist (round 6: it turned square to the mob),
                // except under the standing off-hand punch, which keeps its own square torso to aim at the target (round 8).
                bool square = me.Animation.LowerBodyUnderStandingSwing;
                Assert.Equal(routine.Name[2] == 'i', square);
                foreach (int j in legJoints.Concat(Enumerable.Range(0, skeleton.Count).Where(j => !square && LocomotionLayering.IsSpine(skeleton, j))))
                {
                    Assert.Equal(legs.Rotations[j], shown.Rotations[j]);
                    Assert.Equal(legs.Translations[j], shown.Translations[j]);
                }

                // The weapons: where the swing holds them, relative to the hands (outside the cross-fade into the swing).
                if (me.Animation.IsBlending) continue;
                var swing = SkeletonPoseEvaluator.EvaluatePose(skeleton, me.Animation.CurrentClip, me.Animation.ElapsedSeconds, me.Animation.LoopsCurrentClip, overrides);
                int chest = LocomotionLayering.Chest(skeleton);
                var turn = square ? Quaternion.Identity : Quaternion.Normalize(shown.Rotations[chest] * Quaternion.Inverse(swing.Rotations[chest]));
                foreach (var v in weapons)
                {
                    Vector3 inSwing = Skin(v, swing), inShown = Skin(v, shown);
                    Assert.True(Vector3.Distance(Vector3.Transform(inSwing - swing.Translations[leftHand], turn), inShown - shown.Translations[leftHand]) < 0.002f
                        || Vector3.Distance(Vector3.Transform(inSwing - swing.Translations[rightHand], turn), inShown - shown.Translations[rightHand]) < 0.002f);
                }
            }
            Assert.True(swingFrames > 60, $"{swingFrames} swing frames");
        }
    }
}
