// tests/Gordian.Core.Tests/Events/EventSchedulerTests.cs
using System;
using System.Collections.Generic;
using System.Linq;
using Gordian.Core.Events;
using Gordian.Core.Resources.Events;
using Xunit;

namespace Gordian.Core.Tests.Events
{
    /// <summary>
    /// The scheduler opcodes of the event VM (#165): 0x45 / 0x52 / 0x55 run the main scheduler's scene tasks (camera
    /// shots and fades), 0x2C / 0x5B / 0x66 / 0x50 / 0x53 the entities' gestures, 0x46 holds the camera.
    /// </summary>
    public class EventSchedulerTests
    {
        private static readonly TimeSpan Frame = TimeSpan.FromSeconds(1.0 / 60);
        private const uint Npc = 0x010E6001;

        private static EventVm Make(byte[] code, RecordingHost host, uint[] references)
        {
            var block = new EventBlock(Npc, offsets: new ushort[] { 0 }, eventIds: new ushort[] { 100 }, references, code);
            return new EventVm(block, 100, new EventWorkZone(), host, Npc, 1);
        }

        private static IEnumerable<byte> U32(uint v) => BitConverter.GetBytes(v);
        private static IEnumerable<byte> Tag(string fourCc) => System.Text.Encoding.ASCII.GetBytes(fourCc);
        private static IEnumerable<byte> Ref(int index) => new[] { (byte)(0x8000 | index), (byte)((0x8000 | index) >> 8) };

        /// <summary>0x45 p actor actor routine value (17 bytes).</summary>
        private static IEnumerable<byte> Start(int refIndex, string routine) =>
            new byte[] { 0x45 }.Concat(Ref(refIndex)).Concat(U32(0x7FFFFFF8)).Concat(U32(0x7FFFFFF8)).Concat(Tag(routine)).Concat(new byte[] { 0, 0 });

        /// <summary>0x55 / 0x52 p actor actor routine (15 bytes).</summary>
        private static IEnumerable<byte> TaskOp(byte op, int refIndex, string routine) =>
            new[] { op }.Concat(Ref(refIndex)).Concat(U32(0x7FFFFFF8)).Concat(U32(0x7FFFFFF8)).Concat(Tag(routine));

        private static IEnumerable<byte> Print(int refIndex) => new byte[] { 0x48 }.Concat(Ref(refIndex));

        private static int TicksUntilPrinted(EventVm vm, RecordingHost host, int count)
        {
            int ticks = 0;
            while (host.Printed.Count < count && ticks < 10_000)
            {
                vm.Tick(Frame);
                ticks++;
            }
            return ticks;
        }

        [Fact]
        public void StartTask_LoadsTheSceneResourceOfItsNumber_AndTheWaitHoldsForTheRoutine()
        {
            // 45 s001 on p = ref0 ; 55 wait s001 ; 48 print ref1 ; 00
            var code = Start(0, "s001").Concat(TaskOp(0x55, 0, "s001")).Concat(Print(1)).Concat(new byte[] { 0x00 }).ToArray();
            var host = new RecordingHost();
            host.RoutineFrames["s001"] = 30;
            var vm = Make(code, host, new uint[] { 136, 9 });

            int ticks = TicksUntilPrinted(vm, host, 1);

            var task = Assert.Single(host.SceneTasks);
            Assert.Equal(30840, task.FileId); // 30704 + 136: the Port Bastok intro's shots
            Assert.Equal("s001", task.Routine);
            Assert.Equal(Npc, task.Caster);
            Assert.InRange(ticks, 30, 32); // the wait held for the routine's 30 frames
        }

        [Fact]
        public void MissingRoutine_EndsAtOnce_SoTheWaitDoesNotHold()
        {
            var code = Start(0, "zzzz").Concat(TaskOp(0x55, 0, "zzzz")).Concat(Print(1)).Concat(new byte[] { 0x00 }).ToArray();
            var host = new RecordingHost();
            var vm = Make(code, host, new uint[] { 136, 9 });
            vm.Tick(Frame);
            Assert.Single(host.Printed);
        }

        [Fact]
        public void EndTask_StopsTheRoutine_AndReleasesTheWait()
        {
            // 45 s002 ; 52 end s002 ; 55 wait s002 (over) ; 48 ; 00
            var code = Start(0, "s002").Concat(TaskOp(0x52, 0, "s002")).Concat(TaskOp(0x55, 0, "s002")).Concat(Print(1)).Concat(new byte[] { 0x00 }).ToArray();
            var host = new RecordingHost();
            host.RoutineFrames["s002"] = 600;
            var vm = Make(code, host, new uint[] { 136, 9 });
            vm.Tick(Frame);
            Assert.Equal(host.SceneTasks[0].Id, Assert.Single(host.StoppedTasks));
            Assert.Single(host.Printed);
        }

        [Fact]
        public void SameTaskStartedAgain_StopsTheOldOne()
        {
            var code = Start(0, "ovl1").Concat(Start(0, "ovl1")).Concat(new byte[] { 0x00 }).ToArray();
            var host = new RecordingHost();
            host.RoutineFrames["ovl1"] = 60;
            var vm = Make(code, host, new uint[] { 200 });
            vm.Tick(Frame);
            Assert.Equal(2, host.SceneTasks.Count);
            Assert.Equal(30904, host.SceneTasks[0].FileId);
            Assert.Equal(host.SceneTasks[0].Id, Assert.Single(host.StoppedTasks));
        }

        [Fact]
        public void BankGesture_LoadsTheMotionDat_YieldsAFrame_AndTheEntityWaitHoldsForIt()
        {
            // 5B ref0 self self tlk0 ; 53 self self tlk0 ; 48 ref1 ; 00
            var code = new byte[] { 0x5B }.Concat(Ref(0)).Concat(U32(0x7FFFFFF8)).Concat(U32(0x7FFFFFF8)).Concat(Tag("tlk0"))
                .Concat(new byte[] { 0x53 }).Concat(U32(0x7FFFFFF8)).Concat(U32(0x7FFFFFF8)).Concat(Tag("tlk0"))
                .Concat(Print(1)).Concat(new byte[] { 0x00 }).ToArray();
            var host = new RecordingHost();
            host.MotionFrames["tlk0"] = 20;
            var vm = Make(code, host, new uint[] { 70, 9 });

            vm.Tick(Frame);
            var motion = Assert.Single(host.Motions);
            Assert.Equal((Npc, EventMotionSource.Bank, 32104 + 70, "tlk0"), motion);
            Assert.Empty(host.Printed); // 0x5B yields a frame

            int ticks = TicksUntilPrinted(vm, host, 1);
            Assert.InRange(ticks, 19, 21);
        }

        /// <summary>0x6E actor emote (7 bytes).</summary>
        private static IEnumerable<byte> Emote(int refIndex) => new byte[] { 0x6E }.Concat(U32(0x7FFFFFF8)).Concat(Ref(refIndex));

        [Fact]
        public void Emote_PlaysTheIdAndVariant_AndTheNextEmoteWaitsForTheFirst()
        {
            // 6E self ref0 ; 6E self ref1 ; 48 ref2 ; 00 (#176: Rahal claps, emote 13, in the Southern San d'Oria intro)
            var code = Emote(0).Concat(Emote(1)).Concat(Print(2)).Concat(new byte[] { 0x00 }).ToArray();
            var host = new RecordingHost { EmoteFrames = 30 };
            var vm = Make(code, host, new uint[] { 13, 0x0102, 9 });

            vm.Tick(Frame);
            Assert.Equal((Npc, 13, 0), Assert.Single(host.Emotes));
            int ticks = TicksUntilPrinted(vm, host, 1);
            Assert.Equal((Npc, 2, 1), host.Emotes[1]);
            Assert.InRange(ticks, 29, 32);
        }

        [Fact]
        public void AnimationWait_YieldsOneFrame_WithoutWaitingForTheEnd()
        {
            // 6E self ref0 ; 99 self ; 48 ref1 ; 00: retail's 0x99 steps past itself before it yields.
            var code = Emote(0).Concat(new byte[] { 0x99 }).Concat(U32(0x7FFFFFF8)).Concat(Print(1)).Concat(new byte[] { 0x00 }).ToArray();
            var host = new RecordingHost { EmoteFrames = 100 };
            var vm = Make(code, host, new uint[] { 6, 9 });
            vm.Tick(Frame);
            Assert.Empty(host.Printed);
            vm.Tick(Frame);
            Assert.Single(host.Printed);
        }

        [Theory]
        [InlineData(70, 32174)]
        [InlineData(600, 49735)]
        [InlineData(1500, 57845)]
        [InlineData(2100, 61839)]
        [InlineData(3100, 69439)]
        public void MotionBankFileIds_FollowTheClientBands(int resource, int fileId) =>
            Assert.Equal(fileId, EventVm.MotionBankFileId(resource));

        [Fact]
        public void OwnMotion_And_Stop()
        {
            // 2C self self pas0 ; 50 self self pas0 ; 53 self self pas0 (over) ; 48 ; 00
            var code = new byte[] { 0x2C }.Concat(U32(0x7FFFFFF8)).Concat(U32(0x7FFFFFF8)).Concat(Tag("pas0"))
                .Concat(new byte[] { 0x50 }).Concat(U32(0x7FFFFFF8)).Concat(U32(0x7FFFFFF8)).Concat(Tag("pas0"))
                .Concat(new byte[] { 0x53 }).Concat(U32(0x7FFFFFF8)).Concat(U32(0x7FFFFFF8)).Concat(Tag("pas0"))
                .Concat(Print(0)).Concat(new byte[] { 0x00 }).ToArray();
            var host = new RecordingHost();
            host.MotionFrames["pas0"] = 100;
            var vm = Make(code, host, new uint[] { 9 });
            vm.Tick(Frame);
            Assert.Equal(EventMotionSource.Own, Assert.Single(host.Motions).Source);
            Assert.Equal((Npc, "pas0"), Assert.Single(host.StoppedMotions));
            Assert.Single(host.Printed);
        }

        [Fact]
        public void CameraOpcode_HoldsAndReleases_AndReportsWhoHasTheCamera()
        {
            // 46 01 ; 46 02 local0 ; 46 00 ; 46 02 local1 ; 00
            var code = new byte[] { 0x46, 0x01, 0x46, 0x02, 0x00, 0x00, 0x46, 0x00, 0x46, 0x02, 0x01, 0x00, 0x00 };
            var host = new RecordingHost();
            var vm = Make(code, host, Array.Empty<uint>());
            vm.Tick(Frame);
            Assert.Equal(new[] { true, false }, host.CameraHolds);
            Assert.Equal(0, vm.Locals[0]); // the event held the camera
            Assert.Equal(1, vm.Locals[1]); // the player has it back
        }

        [Fact]
        public void TaskOnAnActorOutsideTheZone_IsSteppedOver()
        {
            // 45 on actor 0x010E6099 (not in the zone) ; 00
            var code = new byte[] { 0x45 }.Concat(Ref(0)).Concat(U32(0x010E6099)).Concat(U32(0x010E6099)).Concat(Tag("s001")).Concat(new byte[] { 0, 0, 0x00 }).ToArray();
            var host = new RecordingHost();
            var vm = Make(code, host, new uint[] { 136 });
            vm.Tick(Frame);
            Assert.Empty(host.SceneTasks);
            Assert.True(vm.IsFinished);
        }
    }
}
