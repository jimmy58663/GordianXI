// tests/Gordian.Core.Tests/Animation/ControlLockTests.cs
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Gordian.Core.Animation;
using Gordian.Core.Config;
using Gordian.Core.Resources;
using Gordian.Core.Resources.Graphics;
using Gordian.Core.Resources.Models;
using Gordian.Core.Resources.Tables;
using Gordian.Tests.Shared;
using Xunit;

namespace Gordian.Core.Tests.Animation
{
    /// <summary>
    /// The action freezes (#511): routine op 0x2E (control lock) read from the routines, followed through links and the shared
    /// <c>wash</c> / <c>waso</c>, and held on the actor's animation state as timers from the routine's start.
    /// </summary>
    public class ControlLockTests
    {
        private const string GameDirectory = @"G:\Program Files (x86)\PlayOnline\SquareEnix\FINAL FANTASY XI";
        private const float Tick = 1f / 60f;

        private static byte[] ControlLock(int delay, int ticks) => MotionRoutineBuilder.Command(0x2E, 2, delay, ticks);

        private static RawMotionRoutine Decode(string name, int total, params byte[][] commands) =>
            MotionRoutineDecoder.Decode(MotionRoutineBuilder.Routine(total, commands), name)!;

        [Fact]
        public void Decode_ReadsTheControlLockDurationAndStart()
        {
            // Fast Blade's main (ROM/76/30): the lock carries a delay of 1, so the clip after it starts at 1.
            var raw = Decode("main", 152, ControlLock(1, 90), MotionRoutineBuilder.PlayClip("b00?", 34, 35, 4, 4, 1));
            var command = Assert.Single(raw.Commands, c => c.Op == 0x2E);
            Assert.Equal(0, command.StartTick);
            Assert.Equal(90, command.Duration);

            var built = MotionRoutineDecoder.Build(new Dictionary<string, RawMotionRoutine> { ["main"] = raw }, "main", _ => true)!;
            Assert.Equal(new[] { new ControlLock(0, 90) }, built.ControlLocks);
        }

        [Fact]
        public void Build_FollowsLinksAndTheSharedReleaseLocks()
        {
            // A spell's main links the caster's release (0x3C), which waits on the shared wash (0x3B) of ROM/0/0.
            var routines = new Dictionary<string, RawMotionRoutine>
            {
                ["main"] = Decode("main", 200, MotionRoutineBuilder.Link(0x3C, "shbk", 10), MotionRoutineBuilder.Link(0x03, "mdam", 40)),
                ["shbk"] = Decode("shbk", 1, MotionRoutineBuilder.Link(0x03, "hwmg", 1), MotionRoutineBuilder.Link(0x3B, "wash", 0)),
            };

            var built = MotionRoutineDecoder.Build(routines, "main", _ => true)!;

            Assert.Equal(new[] { new ControlLock(1, 60) }, built.ControlLocks); // a delay applies after its command: wash runs at 1
        }

        [Fact]
        public void Build_ARoutineWithOnlyALockIsKept()
        {
            var routines = new Dictionary<string, RawMotionRoutine> { ["lock"] = Decode("lock", 30, ControlLock(0, 30)) };
            Assert.NotNull(MotionRoutineDecoder.Build(routines, "lock", _ => false));
        }

        private static EntityModel Model(params (string Name, byte[] Payload)[] routines)
        {
            var model = new EntityModel { Name = "Test" };
            foreach (var name in new[] { "idl", "btl", "wlk", "run", "cm0", "ind", "otd" })
            {
                model.Animations[name] = new AnimationClip { Name = name, KeyFrameDuration = 1f, NumFrames = 31 };
            }
            foreach (var (name, payload) in routines) model.RawMotionRoutines[name] = MotionRoutineDecoder.Decode(payload, name)!;
            model.RebuildMotionRoutines();
            return model;
        }

        private static long After(long start, double seconds) => start + (long)(seconds * Stopwatch.Frequency);

        [Fact]
        public void AnActionsLockHoldsItsActorUntilItEnds()
        {
            var model = Model(("cast", MotionRoutineBuilder.Routine(120, ControlLock(0, 60), MotionRoutineBuilder.PlayClip("cm0?", 0, 120, 4, 4, 1))));
            var state = new EntityAnimationState();
            state.Advance(0f, AnimationCategory.Idle, 0, model);

            long before = Stopwatch.GetTimestamp();
            state.EnqueueAction(new ActionRequest { Motion = ActionMotion.Routine, Routine = "cast", ReceivedTimestamp = before });
            state.Advance(Tick, AnimationCategory.Idle, 0, model);

            Assert.Equal(ActionFreezeSource.Actions, state.ControlLocksAt(After(before, 0.5)));
            Assert.Equal(ActionFreezeSource.None, state.ControlLocksAt(After(before, 1.5)));
        }

        [Fact]
        public void OverlappingLocksHoldUntilTheLastEnds_AndALockOutlivesItsReplacedRoutine()
        {
            var model = Model(
                ("long", MotionRoutineBuilder.Routine(120, ControlLock(0, 90), MotionRoutineBuilder.PlayClip("cm0?", 0, 120, 4, 4, 1))),
                ("shrt", MotionRoutineBuilder.Routine(30, ControlLock(0, 30), MotionRoutineBuilder.PlayClip("cm0?", 0, 30, 4, 4, 1))));
            var state = new EntityAnimationState();
            state.Advance(0f, AnimationCategory.Idle, 0, model);

            long start = Stopwatch.GetTimestamp();
            state.EnqueueAction(new ActionRequest { Motion = ActionMotion.Routine, Routine = "long", ReceivedTimestamp = start });
            state.Advance(Tick, AnimationCategory.Idle, 0, model);
            state.EnqueueAction(new ActionRequest { Motion = ActionMotion.Routine, Routine = "shrt", ReceivedTimestamp = start });
            for (int i = 0; i < 5; i++) state.Advance(Tick, AnimationCategory.Idle, 0, model);

            // The short lock has ended (0.5 s) but the long one (1.5 s) still holds.
            Assert.Equal(ActionFreezeSource.Actions, state.ControlLocksAt(After(start, 1.0)));
            Assert.Equal(ActionFreezeSource.None, state.ControlLocksAt(After(start, 2.0)));

            state.EnqueueAction(new ActionRequest { Motion = ActionMotion.Routine, Routine = "long", ReceivedTimestamp = Stopwatch.GetTimestamp() });
            state.Advance(Tick, AnimationCategory.Idle, 0, model);
            state.ClearControlLocks();
            Assert.Equal(ActionFreezeSource.None, state.ControlLocksAt(Stopwatch.GetTimestamp()));
        }

        [Fact]
        public void TheWeaponDrawLockIsAnEngageLock()
        {
            var model = Model(("in 0", MotionRoutineBuilder.Routine(72, ControlLock(0, 72), MotionRoutineBuilder.PlayClip("ind?", 0, 72, 4, 4, 1))));
            var state = new EntityAnimationState();
            state.Advance(0f, AnimationCategory.Idle, 0, model);

            long start = Stopwatch.GetTimestamp();
            state.Advance(Tick, AnimationCategory.Combat, 0, model);

            Assert.Equal(ActionFreezeSource.Engage, state.ControlLocksAt(After(start, 0.5)));
        }

        private static void Run(EntityAnimationState state, EntityModel model, AnimationCategory category, int ticks)
        {
            for (int i = 0; i < ticks; i++) state.Advance(Tick, category, 0, model);
        }

        /// <summary>
        /// Round 1 of the in-game test (#511): an action used while moving plays over the run, the character sliding, as retail
        /// shows it with the freezes off; a draw started while running plays too. A chant still gives way to the run.
        /// </summary>
        [Theory]
        [InlineData("shit", (byte)Gordian.Core.Network.Packets.ActionCategory.ItemFinish, true)]
        [InlineData("cabk", (byte)Gordian.Core.Network.Packets.ActionCategory.MagicStart, false)]
        public void FinishRoutinesKeepTheBodyWhileMoving_StartRoutinesGiveWay(string name, byte category, bool slides)
        {
            var model = Model((name, MotionRoutineBuilder.Routine(60, MotionRoutineBuilder.PlayClip("cm0?", 0, 60, 4, 4, 1))));
            var state = new EntityAnimationState();
            state.Advance(0f, AnimationCategory.Idle, 0, model);

            state.EnqueueAction(new ActionRequest
            {
                Motion = ActionMotion.Routine, Routine = name, Category = (Gordian.Core.Network.Packets.ActionCategory)category,
                ReceivedTimestamp = Stopwatch.GetTimestamp()
            });
            Run(state, model, AnimationCategory.Run, 10);

            if (slides) Assert.Equal(name, state.ActiveRoutine?.Name);
            else Assert.Null(state.ActiveRoutine);
        }

        [Fact]
        public void EngagingWhileRunning_PlaysTheDraw()
        {
            var model = Model(("in 0", MotionRoutineBuilder.Routine(72, ControlLock(0, 72), MotionRoutineBuilder.PlayClip("ind?", 0, 72, 4, 4, 1))));
            var state = new EntityAnimationState();
            Run(state, model, AnimationCategory.Run, 2);

            Run(state, model, AnimationCategory.CombatRun, 10);

            Assert.Equal("in 0", state.ActiveRoutine?.Name);
            Assert.Equal("ind", state.CurrentClip?.Name);
            Run(state, model, AnimationCategory.CombatRun, 70);
            Assert.Null(state.ActiveRoutine); // then the run takes the body back
        }

        [Fact]
        public void Settings_IgnoreOnlyWhatTheServerAllows()
        {
            var settings = new ActionFreezeSettings { IgnoreRequested = ActionFreezeSource.Engage };
            var profile = new SessionProfile();
            Assert.Equal(ActionFreezeSource.Engage, settings.GetIgnored(profile));
            Assert.Equal(ActionFreezeSource.Engage, settings.GetIgnored(null));

            profile.Restrict(FeatureRestrictions.ActionFreezeOverride);
            Assert.True(ActionFreezeSettings.IsLocked(profile));
            Assert.Equal(ActionFreezeSource.None, settings.GetIgnored(profile));
        }

        private static ResourceManager? Retail()
        {
            if (!Directory.Exists(GameDirectory)) return null;
            var rm = new ResourceManager(GameDirectory);
            rm.InitializeFileTable();
            return rm;
        }

        /// <summary>The shared release locks GordianXI keeps in code are the ones the retail <c>ROM/0/0.DAT</c> carries.</summary>
        [Fact]
        public void SharedControlLocks_MatchTheRetailDat()
        {
            var rm = Retail();
            if (rm == null) return;
            var raw = EntityModelLoader.ParseDatContainer(rm.LoadDatBytes(Path.Combine("ROM", "0", "0.DAT"))!, "shared").Routines!;
            foreach (var (name, ticks) in SharedControlLocks.ByName)
            {
                var routine = raw.Single(r => r.Name == name);
                var built = MotionRoutineDecoder.Build(new Dictionary<string, RawMotionRoutine> { [name] = routine }, name, _ => false)!;
                Assert.Equal(new[] { new ControlLock(0, ticks) }, built.ControlLocks);
            }
        }

        /// <summary>
        /// Retail's locks (2026-11-12): the Hume battle pack's draw / sheathe lock 72 ticks and its swings none; Fast Blade's
        /// main locks 90 from the start; a job ability's 100.
        /// </summary>
        [Fact]
        public void RetailRoutines_LockWhereRetailFreezes()
        {
            var rm = Retail();
            if (rm == null) return;
            var pack = EntityModelLoader.ParseDatContainer(rm.LoadDatBytes(Path.Combine("ROM", "32", "13.DAT"))!, "pack").Routines!
                .ToDictionary(r => r.Name);
            ControlLock[] Locks(IReadOnlyDictionary<string, RawMotionRoutine> set, string name) =>
                MotionRoutineDecoder.Build(set, name, _ => true)?.ControlLocks.ToArray() ?? [];

            Assert.Equal(new[] { new ControlLock(0, 72) }, Locks(pack, "in 0"));
            Assert.Equal(new[] { new ControlLock(0, 72) }, Locks(pack, "out0"));
            Assert.Empty(Locks(pack, "ati0"));

            Assert.True(rm.FileTable.TryResolve(ActionMotionLibrary.WeaponSkillFileId(CharacterRace.HumeMale, 1), out var fastBlade));
            var ws = EntityModelLoader.ParseDatContainer(rm.LoadDatBytes(fastBlade)!, "ws").Routines!.ToDictionary(r => r.Name);
            Assert.Equal(new[] { new ControlLock(0, 90) }, Locks(ws, "main"));

            Assert.True(rm.FileTable.TryResolve(ActionMotionLibrary.JobAbilityFileId(0), out var ability));
            var ja = EntityModelLoader.ParseDatContainer(rm.LoadDatBytes(ability)!, "ja").Routines!.ToDictionary(r => r.Name);
            Assert.Equal(new[] { new ControlLock(0, 100) }, Locks(ja, "main"));
        }
    }
}
