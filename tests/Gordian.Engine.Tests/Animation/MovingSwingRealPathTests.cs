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

        [Theory]
        [InlineData(CharacterRace.ElvaanMale, (ushort)484, (ushort)484, GordianKey.D, AnimationCategory.CombatMoveRight)]
        [InlineData(CharacterRace.ElvaanMale, (ushort)484, (ushort)484, GordianKey.A, AnimationCategory.CombatMoveLeft)]
        [InlineData(CharacterRace.ElvaanMale, (ushort)484, (ushort)484, GordianKey.S, AnimationCategory.CombatMoveBackward)]
        [InlineData(CharacterRace.ElvaanMale, (ushort)484, (ushort)484, GordianKey.W, AnimationCategory.CombatRun)]
        [InlineData(CharacterRace.HumeMale, (ushort)157, (ushort)157, GordianKey.D, AnimationCategory.CombatMoveRight)]
        [InlineData(CharacterRace.HumeMale, (ushort)157, (ushort)157, GordianKey.S, AnimationCategory.CombatMoveBackward)]
        public void ASwingWhileMoving_KeepsTheLegs_AndTheWeaponsInTheHands(CharacterRace race, ushort main, ushort sub, GordianKey key, AnimationCategory expected)
        {
            if (!Directory.Exists(GameDirectory)) return;
            var rm = new ResourceManager(GameDirectory);
            rm.InitializeFileTable();
            var grap = new ushort[9];
            grap[6] = main;
            grap[7] = sub;
            var model = EntityModelLoader.AssembleCharacter(race, 1, grap, rm.LoadDatBytes, rm.LoadDatBytesByFileId)!;
            var skeleton = model.Skeleton!;

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

            input.SetKeyDown(key);
            for (int i = 0; i < 100; i++) Frame();
            Assert.Equal(expected, me.Animation.Current);
            var locomotion = NpcStanceResolver.ResolveTargetClip(model, expected);
            Assert.Same(locomotion, me.Animation.CurrentClip);

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
                foreach (int j in legJoints)
                {
                    Assert.Equal(legs.Rotations[j], shown.Rotations[j]);
                    Assert.Equal(legs.Translations[j], shown.Translations[j]);
                }

                // The weapons: where the swing holds them, relative to the hands (outside the cross-fade into the swing).
                if (me.Animation.IsBlending) continue;
                var swing = SkeletonPoseEvaluator.EvaluatePose(skeleton, me.Animation.CurrentClip, me.Animation.ElapsedSeconds, me.Animation.LoopsCurrentClip, overrides);
                foreach (var v in weapons)
                {
                    Vector3 inSwing = Skin(v, swing), inShown = Skin(v, shown);
                    Assert.True(Vector3.Distance(inSwing - swing.Translations[leftHand], inShown - shown.Translations[leftHand]) < 0.001f
                        || Vector3.Distance(inSwing - swing.Translations[rightHand], inShown - shown.Translations[rightHand]) < 0.001f);
                }
            }
            Assert.True(swingFrames > 60, $"{swingFrames} swing frames");
        }
    }
}
