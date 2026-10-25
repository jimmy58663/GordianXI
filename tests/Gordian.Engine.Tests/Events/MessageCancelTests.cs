// tests/Gordian.Engine.Tests/Events/MessageCancelTests.cs
using System;
using System.IO;
using Gordian.Engine.Events;
using Gordian.Core.Resources;
using Gordian.Core.Resources.Events;
using Xunit;

namespace Gordian.Engine.Tests.Events
{
    /// <summary>
    /// Cancel on an event message (#413): where the event allows it (opcodes 0x2E / 0x42, on at the start) the line
    /// closes and the next 0x23 ends the event as cancelled, as a cancelled query does; where the event forbids it,
    /// Cancel does nothing.
    /// </summary>
    public class MessageCancelTests
    {
        private const string GameDirectory = @"G:\Program Files (x86)\PlayOnline\SquareEnix\FINAL FANTASY XI";
        private static readonly TimeSpan Frame = TimeSpan.FromSeconds(1.0 / 60);

        private static EventVm Make(byte[] code, RecordingHost host)
        {
            var block = new EventBlock(0x010E6001, offsets: new ushort[] { 0 }, eventIds: new ushort[] { 100 }, new uint[] { 500, 501 }, code);
            return new EventVm(block, 100, new EventWorkZone(), host, 0x010E6001, 1);
        }

        private static RecordingHost PromptHost()
        {
            var host = new RecordingHost();
            host.PromptMessages.Add(500);
            host.PromptMessages.Add(501);
            return host;
        }

        [Fact]
        public void Cancel_EndsTheEventAtTheMessageWait_WhenTheEventAllowsIt()
        {
            // 1D msg 500 ; 23 ; 1D msg 501 ; 23 ; 21
            var host = PromptHost();
            var vm = Make(new byte[] { 0x1D, 0x00, 0x80, 0x23, 0x1D, 0x01, 0x80, 0x23, 0x21 }, host);
            vm.Tick(Frame);
            Assert.True(vm.IsWaitingForConfirm);

            Assert.True(vm.Scene.CancelMessage());
            vm.Tick(Frame);

            Assert.True(vm.IsFinished);
            Assert.True(vm.IsCancelled);
            Assert.Equal(EventVm.CancelledEndParameter, vm.EndParameter);
            Assert.Single(host.Printed); // message 501 never shows
        }

        [Fact]
        public void Cancel_DoesNothing_AfterOpcode42()
        {
            // 42 ; 1D msg 500 ; 23 ; 21
            var host = PromptHost();
            var vm = Make(new byte[] { 0x42, 0x1D, 0x00, 0x80, 0x23, 0x21 }, host);
            vm.Tick(Frame);

            Assert.False(vm.Scene.CancelMessage());
            vm.Tick(Frame);
            Assert.True(vm.IsWaitingForConfirm);
            Assert.False(vm.IsFinished);

            vm.Confirm();
            vm.Tick(Frame);
            Assert.True(vm.IsFinished);
            Assert.False(vm.IsCancelled);
        }

        [Fact]
        public void Opcode2E_AllowsTheCancelAgain()
        {
            // 42 ; 2E ; 1D msg 500 ; 23 ; 21
            var host = PromptHost();
            var vm = Make(new byte[] { 0x42, 0x2E, 0x1D, 0x00, 0x80, 0x23, 0x21 }, host);
            vm.Tick(Frame);

            Assert.True(vm.Scene.CancelMessage());
            vm.Tick(Frame);
            Assert.True(vm.IsCancelled);
        }

        /// <summary>
        /// Southern San d'Oria: event 509 (an NPC's plain talk, no 0x42) can be cut short; event 23 (a cutscene on the
        /// player's block, 0x42 before its first line) cannot.
        /// </summary>
        [Theory]
        [InlineData(509, 0x010E6001u, true)]
        [InlineData(23, 0x7FFFFFF0u, false)]
        public void RetailEvents_AllowTheCancelOnlyWhereTheyDoNotClearIt(int eventId, uint actor, bool cancellable)
        {
            if (!Directory.Exists(GameDirectory)) return;
            var rm = new ResourceManager(GameDirectory);
            rm.InitializeFileTable();
            var script = ZoneEventScript.Parse(rm.LoadDatBytesByFileId(ZoneEventScript.GetFileId(230))!)!;
            var host = new RecordingHost { PromptAll = true };
            var scene = new EventScene(new EventWorkZone()) { PlayerServerId = 0x12345 };
            foreach (var block in script.Blocks)
            {
                if (block.ActorId != actor || block.IndexOf((ushort)eventId) < 0) continue;
                _ = new EventVm(block, (ushort)eventId, scene, host, block.ActorId == EventBlock.PlayerActor ? 0x12345u : block.ActorId, 1);
            }
            for (int t = 0; t < 20000 && !scene.IsWaitingForConfirm && !scene.IsFinished; t++) scene.Tick(Frame);
            Assert.True(scene.IsWaitingForConfirm);

            Assert.Equal(cancellable, scene.CancelMessage());
            for (int t = 0; t < 10; t++) scene.Tick(Frame);
            Assert.Equal(cancellable, scene.IsCancelled);
        }
    }
}
