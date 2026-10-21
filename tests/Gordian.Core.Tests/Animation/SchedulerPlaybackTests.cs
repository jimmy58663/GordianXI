// tests/Gordian.Core.Tests/Animation/SchedulerPlaybackTests.cs
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Gordian.Core.Animation;
using Gordian.Core.Network.Packets;
using Gordian.Core.Resources;
using Gordian.Core.Resources.Events;
using Gordian.Core.Tests.Resources;
using Gordian.Core.World;
using Xunit;

namespace Gordian.Core.Tests.Animation
{
    /// <summary>
    /// S2C 0x038 / 0x03A playback (#311) and the actor colour commands 0x29 / 0x2E (#206). The retail checks are skipped
    /// without the game install.
    /// </summary>
    public class SchedulerPlaybackTests
    {
        private const string GameDirectory = @"G:\Program Files (x86)\PlayOnline\SquareEnix\FINAL FANTASY XI";
        private const uint Caster = 0x01000010, Target = 0x01000020;

        private static byte[] ColorCommand(byte op, ushort delay, ushort duration, uint bgra)
        {
            var cmd = new byte[16];
            cmd[0] = op;
            cmd[1] = 0x04;
            BinaryPrimitives.WriteUInt16LittleEndian(cmd.AsSpan(4), delay);
            BinaryPrimitives.WriteUInt16LittleEndian(cmd.AsSpan(6), duration);
            BinaryPrimitives.WriteUInt32LittleEndian(cmd.AsSpan(8), bgra);
            return cmd;
        }

        private static byte[] Hold(ushort delay, ushort duration)
        {
            var cmd = new byte[8];
            cmd[0] = 0x2E;
            cmd[1] = 0x02;
            BinaryPrimitives.WriteUInt16LittleEndian(cmd.AsSpan(4), delay);
            BinaryPrimitives.WriteUInt16LittleEndian(cmd.AsSpan(6), duration);
            return cmd;
        }

        private static SceneRoutine Routine(int total, params byte[][] commands) =>
            SceneRoutine.Decode(EffectRoutineDecoderTests.BuildPayload(total, commands.ToList()), "test")!;

        /// <summary>wp00 as in file 5013: white over 0-60, a 150-frame hold, then alpha 0 over 90-150.</summary>
        private static SceneRoutine Wp00() => Routine(150,
            ColorCommand(0x29, 0, 60, 0x80FFFFFF), Hold(0, 150), EffectRoutineDecoderTests.Command(0x02, 90, 0, "g006"), ColorCommand(0x29, 0, 60, 0x00808080));

        [Fact]
        public void Decode_ReadsColourHoldAndActorLinks()
        {
            var routine = Routine(90, ColorCommand(0x29, 90, 90, 0x00808080), Hold(0, 120), EffectRoutineDecoderTests.Command(0x57, 0, 0, "eflg"));
            // The payload opens with the 0x01 every routine starts with.
            Assert.Equal(0x01, routine.Commands[0].Opcode);
            routine = new SceneRoutine { Name = routine.Name, TotalFrames = routine.TotalFrames, Commands = routine.Commands.Skip(1).ToList() };
            Assert.Equal(SceneCommandKind.ActorColor, routine.Commands[0].Kind);
            Assert.Equal(0x00808080u, routine.Commands[0].Color);
            Assert.Equal(90, routine.Commands[0].Duration);
            Assert.Equal(SceneCommandKind.ActorHold, routine.Commands[1].Kind);
            Assert.Equal(120, routine.Commands[1].Duration);
            Assert.Equal(SceneCommandKind.LinkActorRoutine, routine.Commands[2].Kind);
            Assert.Equal("eflg", routine.Commands[2].Reference);
            Assert.False(routine.HasEffects); // none of them runs a generator
        }

        [Fact]
        public void Kesu_FadesTheActorOutOverNinetyFrames_AndStaysOut()
        {
            var track = new ActorColorTrack();
            Assert.True(track.Schedule(Routine(90, ColorCommand(0x29, 90, 90, 0x00808080)), 10.0));
            Assert.Equal(1f, track.Sample(10.0).W, 3);
            Assert.Equal(0.5f, track.Sample(10.75).W, 3);
            Assert.Equal(0f, track.Sample(11.5).W, 3);
            Assert.Equal(0f, track.Sample(60.0).W, 3); // no hold: a despawning entity stays out
            Assert.Equal(1f, track.Sample(60.0).X, 3); // its colour is unchanged
        }

        [Fact]
        public void Wp00_WhitensThenVanishes_AndTheHoldEndsAt150()
        {
            var track = new ActorColorTrack();
            track.Schedule(Wp00(), 0.0);
            Assert.Equal(1.5f, track.Sample(0.5).X, 2);      // halfway to FF: 1.5 times as bright
            Assert.Equal(1f, track.Sample(1.0).W, 3);       // opaque until 90
            Assert.Equal(0.5f, track.Sample(2.0).W, 3);     // halfway through the fade out over 90-150
            Assert.Equal(0f, track.Sample(2.49).W, 1);
            Assert.Equal(ActorColorTrack.AsDrawn, track.Sample(2.6)); // the hold is over: drawn as is
            Assert.False(track.IsActive);
        }

        [Fact]
        public void Wp01_StartsHidden_AndReappearsWhite()
        {
            // 5014: 80808000 at once, FFFFFF80 over 20-60, 80808080 over 60-120, held 120.
            var track = new ActorColorTrack();
            track.Schedule(Routine(170, ColorCommand(0x29, 0, 0, 0x00808080), Hold(20, 120), ColorCommand(0x29, 40, 40, 0x80FFFFFF),
                ColorCommand(0x29, 110, 60, 0x80808080)), 0.0);
            Assert.Equal(0f, track.Sample(0.1).W, 3);
            Assert.Equal(1f, track.Sample(1.0).W, 3);
            Assert.Equal(255f / 128f, track.Sample(1.0).X, 3);
            Assert.Equal(1f, track.Sample(1.99).X, 1);
        }

        [Fact]
        public void CategoryOf_MapsTheFourKnownTypes()
        {
            Assert.Equal(ActionCategory.MagicFinish, SchedulerPlayback.CategoryOf(MagicSchedulerType.CastSpell));
            Assert.Equal(ActionCategory.AbilityFinish, SchedulerPlayback.CategoryOf((MagicSchedulerType)2));
            Assert.Equal(ActionCategory.SkillFinish, SchedulerPlayback.CategoryOf((MagicSchedulerType)6));
            Assert.Equal(ActionCategory.MobSkillFinish, SchedulerPlayback.CategoryOf((MagicSchedulerType)9));
            Assert.Null(SchedulerPlayback.CategoryOf(MagicSchedulerType.UseItem));
            Assert.Null(SchedulerPlayback.CategoryOf((MagicSchedulerType)4));
        }

        [Fact]
        public void ActorScheduler_PostsTheRoutinesEffectsOnTheCasterTowardTheTarget()
        {
            var world = new WorldState { CurrentZoneId = 100 };
            world.UpsertEntity(new WorldEntity(Caster, 1, EntityType.Monster));
            world.UpsertEntity(new WorldEntity(Target, 2, EntityType.Player));
            var scheduler = new SchedulerState();
            _ = new SchedulerPlayback(scheduler, world, library: null);

            scheduler.PostActor(new ActorSchedulerRequest(100, "kesu", 0x7573656B, Caster, Target, 1, 2));
            scheduler.PostActor(new ActorSchedulerRequest(100, "hitl", 0, 0x0BADBEEF, 0, 0, 0)); // unknown caster

            var taken = new List<ActorEffectRequest>();
            world.TakeActorEffects(100, taken);
            var effect = Assert.Single(taken);
            Assert.Equal(new ActorEffectRequest(Caster, Target, "kesu") { ZoneId = 100 }, effect);
        }

        [Fact]
        public void MagicScheduler_OfAnUnknownType_PlaysNothing()
        {
            var world = new WorldState { CurrentZoneId = 100 };
            world.UpsertEntity(new WorldEntity(Caster, 1, EntityType.Player));
            var scheduler = new SchedulerState();
            _ = new SchedulerPlayback(scheduler, world, new ActionMotionLibrary(_ => null));
            scheduler.PostMagic(new MagicSchedulerRequest(100, 251, 4, Caster, Caster, 1, 1)); // the hearts icon
            scheduler.PostMagic(new MagicSchedulerRequest(100, 261, 0, Caster, Caster, 1, 1)); // no file loads
            var taken = new List<ActorEffectRequest>();
            world.TakeActorEffects(100, taken);
            Assert.Empty(taken);
        }

        // ── retail DATs ─────────────────────────────────────────────────────

        private static ResourceManager? Retail()
        {
            if (!Directory.Exists(GameDirectory)) return null;
            var rm = new ResourceManager(GameDirectory);
            rm.InitializeFileTable();
            return rm;
        }

        [Fact]
        public void SharedKesuAndHitl_AreAColourFadeAndTwoActorLinks()
        {
            var rm = Retail();
            if (rm?.LoadDatBytesByFileId(0) is not { } bytes) return;
            var shared = EventSceneResource.Parse(bytes);
            Assert.True(shared.TryGetRoutine("kesu", out var kesu));
            var fade = Assert.Single(kesu.Commands, c => c.Kind == SceneCommandKind.ActorColor);
            Assert.Equal(0x00808080u, fade.Color);
            Assert.Equal(90, fade.Duration);
            Assert.True(shared.TryGetRoutine("hitl", out var hitl));
            Assert.Equal(new[] { "eflg", "selg" }, hitl.Commands.Where(c => c.Kind == SceneCommandKind.LinkActorRoutine).Select(c => c.Reference));
        }

        [Fact]
        public void BattlefieldWarps_CarryTheirColourCommands()
        {
            var rm = Retail();
            if (rm?.LoadDatBytesByFileId(5013) is not { } wp00Bytes || rm.LoadDatBytesByFileId(5014) is not { } wp01Bytes) return;
            Assert.True(EventSceneResource.Parse(wp00Bytes).TryGetRoutine("main", out var wp00));
            Assert.Equal(new[] { (0, 60, 0x80FFFFFFu), (90, 60, 0x00808080u) },
                wp00.Commands.Where(c => c.Kind == SceneCommandKind.ActorColor).Select(c => (c.StartFrame, c.Duration, c.Color)));
            Assert.Equal(150, Assert.Single(wp00.Commands, c => c.Kind == SceneCommandKind.ActorHold).Duration);
            Assert.True(EventSceneResource.Parse(wp01Bytes).TryGetRoutine("main", out var wp01));
            Assert.Equal(new[] { (0, 0, 0x00808080u), (20, 40, 0x80FFFFFFu), (60, 60, 0x80808080u) },
                wp01.Commands.Where(c => c.Kind == SceneCommandKind.ActorColor).Select(c => (c.StartFrame, c.Duration, c.Color)));
            Assert.Equal(120, Assert.Single(wp01.Commands, c => c.Kind == SceneCommandKind.ActorHold).Duration);
        }
    }
}
