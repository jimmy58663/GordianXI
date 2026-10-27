// tests/Gordian.Engine.Tests/Events/MotionDispatchOpcodeTests.cs
using System;
using System.Linq;
using Gordian.Engine.Events;
using Gordian.Core.Resources.Events;
using Xunit;

namespace Gordian.Engine.Tests.Events
{
    /// <summary>
    /// Opcode 0x5F, the non-yielding forms of the motion opcodes (#237, XiEvents OpCodes/0x005F): sub 3 / 4 start a
    /// motion as 0x5B / 0x66 do, sub 7 waits for one as 0x53 does, sub 2 ends the actor's action as 0xC1 does.
    /// </summary>
    public class MotionDispatchOpcodeTests
    {
        private const uint Self = 0x010E6001;
        private static readonly TimeSpan Frame = TimeSpan.FromSeconds(1.0 / 60);

        private static EventVm Make(byte[] code, RecordingHost host, uint[] references)
        {
            var block = new EventBlock(Self, offsets: new ushort[] { 0 }, eventIds: new ushort[] { 100 }, references, code);
            return new EventVm(block, 100, new EventWorkZone(), host, Self, 1);
        }

        // Reference 0 is the motion resource (5); the actors are the event's own entity (0x7FFFFFF8), the routines literal FourCCs.
        private static readonly uint[] References = { 5 };

        [Fact]
        public void Sub3_StartsABankMotion_AndSub7WaitsForIt_WithoutYielding()
        {
            // 5F 03 res actor target routine ; 5F 07 actor target routine ; 21
            var code = new byte[]
            {
                0x5F, 0x03, 0x00, 0x80, 0xF8, 0xFF, 0xFF, 0x7F, 0xF8, 0xFF, 0xFF, 0x7F, 0x68, 0x69, 0x7A, 0x30,
                0x5F, 0x07, 0xF8, 0xFF, 0xFF, 0x7F, 0xF8, 0xFF, 0xFF, 0x7F, 0x68, 0x69, 0x7A, 0x30,
                0x21,
            };
            var host = new RecordingHost();
            host.MotionFrames["hiz0"] = 10;
            var vm = Make(code, host, References);

            vm.Tick(Frame);
            var motion = Assert.Single(host.Motions);
            Assert.Equal((Self, EventMotionSource.Bank, "hiz0"), (motion.Id, motion.Source, motion.Routine));
            Assert.Equal(Gordian.Core.Animation.EventMotionBank.MotionBankFileId(5), motion.Resource);
            Assert.False(vm.IsFinished); // sub 7 holds while hiz0 plays
            for (int i = 0; i < 20 && !vm.IsFinished; i++) vm.Tick(Frame);
            Assert.True(vm.IsFinished);
            Assert.Empty(host.Skipped);
        }

        [Fact]
        public void Sub4_StartsAPackageMotion_AndSub5CarriesAnExtraOperand()
        {
            var code = new byte[]
            {
                0x5F, 0x04, 0x00, 0x80, 0xF8, 0xFF, 0xFF, 0x7F, 0xF8, 0xFF, 0xFF, 0x7F, 0x68, 0x69, 0x7A, 0x30,
                0x5F, 0x05, 0x00, 0x80, 0xF8, 0xFF, 0xFF, 0x7F, 0xF8, 0xFF, 0xFF, 0x7F, 0x6B, 0x72, 0x75, 0x30, 0x00, 0x80,
                0x21,
            };
            var host = new RecordingHost();
            var vm = Make(code, host, References);
            vm.Tick(Frame); // neither yields: both start in the same frame and the event ends

            Assert.True(vm.IsFinished);
            Assert.Equal(new[] { (EventMotionSource.Package, 5, "hiz0"), (EventMotionSource.Bank, Gordian.Core.Animation.EventMotionBank.MotionBankFileId(5), "kru0") },
                host.Motions.Select(m => (m.Source, m.Resource, m.Routine)));
        }

        [Fact]
        public void Sub2_EndsTheActorsAction()
        {
            var code = new byte[] { 0x5F, 0x02, 0xF8, 0xFF, 0xFF, 0x7F, 0x21 };
            var host = new RecordingHost();
            var vm = Make(code, host, References);
            vm.Tick(Frame);

            Assert.True(vm.IsFinished);
            Assert.Equal((Self, string.Empty), Assert.Single(host.StoppedMotions));
        }
    }
}
