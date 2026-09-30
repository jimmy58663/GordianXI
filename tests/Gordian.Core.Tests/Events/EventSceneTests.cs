// tests/Gordian.Core.Tests/Events/EventSceneTests.cs
using System;
using System.Collections.Generic;
using System.Linq;
using Gordian.Core.Events;
using Gordian.Core.Resources.Events;
using Xunit;

namespace Gordian.Core.Tests.Events
{
    /// <summary>
    /// Multi-entity events (#85): one VM per entity in an <see cref="EventScene"/>, request stacks and the companion
    /// opcodes 0x27-0x2A (XiEvents OpCodes 0x0027-0x002A, XiEvent::ReqSet / GetReqStatus / GetReqLevel).
    /// </summary>
    public class EventSceneTests
    {
        private const uint Director = 0x010E6001;
        private const uint Actor = 0x010E6002;
        private const ushort EventId = 100;
        private static readonly TimeSpan Frame = TimeSpan.FromSeconds(1.0 / 60);

        private static byte[] Ref(int index) => BitConverter.GetBytes((ushort)(0x8000 | index));

        private static byte[] Id(uint id) => BitConverter.GetBytes(id);

        private static byte[] Code(params object[] parts)
        {
            var bytes = new List<byte>();
            foreach (var part in parts)
            {
                switch (part)
                {
                    case byte b: bytes.Add(b); break;
                    case int i: bytes.Add((byte)i); break;
                    case byte[] a: bytes.AddRange(a); break;
                    default: throw new ArgumentException(part.ToString());
                }
            }
            return bytes.ToArray();
        }

        /// <summary>A block whose events are laid out back to back: slot n = event id 100 + n.</summary>
        private static EventBlock Block(uint actor, uint[] references, params byte[][] events)
        {
            var offsets = new ushort[events.Length];
            var ids = new ushort[events.Length];
            var code = new List<byte>();
            for (int i = 0; i < events.Length; i++)
            {
                offsets[i] = (ushort)code.Count;
                ids[i] = (ushort)(EventId + i);
                code.AddRange(events[i]);
            }
            return new EventBlock(actor, offsets, ids, references, code.ToArray());
        }

        private static (EventScene Scene, RecordingHost Host) Scene(params EventBlock[] blocks)
        {
            var host = new RecordingHost();
            var scene = new EventScene(new EventWorkZone());
            foreach (var block in blocks) _ = new EventVm(block, EventId, scene, host, block.ActorId, (ushort)(block.ActorId & 0x3FF));
            return (scene, host);
        }

        private static List<int> Messages(RecordingHost host) => host.Printed.Select(p => p.Message).ToList();

        [Fact]
        public void Request29_RunsTheSlotOnTheOtherEntityAndWaitsForItsEnd()
        {
            // Director: 29 prio=8 Actor slot=1 ; 48 msg 900 ; 21
            var director = Block(Director, new uint[] { 900 },
                Code(0x29, 8, Id(Actor), 1, 0x48, Ref(0), 0x21));
            // Actor: slot 0 (the event itself) ends at once; slot 1 prints 800, waits 3 frames, prints 801, ends.
            var actor = Block(Actor, new uint[] { 800, 801, 3 },
                Code(0x00),
                Code(0x1D, Ref(0), 0x1C, Ref(2), 0x1D, Ref(1), 0x00));
            var (scene, host) = Scene(director, actor);

            for (int i = 0; i < 20 && !scene.IsFinished; i++) scene.Tick(Frame);

            Assert.True(scene.IsFinished);
            Assert.True(scene.IsEnded);
            Assert.Equal(new[] { 800, 801, 900 }, Messages(host)); // the director went on only after the actor's part ended
            Assert.Equal(Actor, host.Printed[0].Id);
        }

        [Fact]
        public void Request27_DoesNotWait()
        {
            var director = Block(Director, new uint[] { 900 },
                Code(0x27, 8, Id(Actor), 1, 0x48, Ref(0), 0x00));
            var actor = Block(Actor, new uint[] { 800, 5 },
                Code(0x00),
                Code(0x1C, Ref(1), 0x1D, Ref(0), 0x00));
            var (scene, host) = Scene(director, actor);

            scene.Tick(Frame);
            Assert.Equal(new[] { 900 }, Messages(host));
            for (int i = 0; i < 20 && !scene.IsFinished; i++) scene.Tick(Frame);
            Assert.Equal(new[] { 900, 800 }, Messages(host));
            Assert.False(scene.IsEnded); // every part ended with 0x00, none with 0x21
            Assert.True(scene.IsFinished);
        }

        [Fact]
        public void Request28_WaitsOnlyUntilTheEntityHasStartedIt()
        {
            // The actor is busy with its own event (a 10-frame wait at the same priority 16) before it takes the request.
            var director = Block(Director, new uint[] { 900 },
                Code(0x28, 16, Id(Actor), 1, 0x48, Ref(0), 0x00));
            var actor = Block(Actor, new uint[] { 800, 801, 10 },
                Code(0x1C, Ref(2), 0x00),
                Code(0x1D, Ref(0), 0x1C, Ref(2), 0x1D, Ref(1), 0x00));
            var (scene, host) = Scene(director, actor);

            int ticks = 0;
            while (!host.Printed.Any(p => p.Message == 900) && ticks++ < 100) scene.Tick(Frame);
            // 900 comes after the actor started slot 1 (800) and before it finished it (801).
            Assert.Equal(new[] { 800, 900 }, Messages(host));
            for (int i = 0; i < 100 && !scene.IsFinished; i++) scene.Tick(Frame);
            Assert.Equal(new[] { 800, 900, 801 }, Messages(host));
        }

        [Fact]
        public void MoreUrgentRequest_PreemptsTheRunningOne_WhichResumesAfterwards()
        {
            // The actor's own event: print 700, wait 5 frames, print 701. The director asks slot 1 at priority 8.
            var director = Block(Director, Array.Empty<uint>(), Code(0x29, 8, Id(Actor), 1, 0x00));
            var actor = Block(Actor, new uint[] { 700, 701, 800, 5 },
                Code(0x1D, Ref(0), 0x1C, Ref(3), 0x1D, Ref(1), 0x00),
                Code(0x1D, Ref(2), 0x00));
            var (scene, host) = Scene(actor, director);

            for (int i = 0; i < 30 && !scene.IsFinished; i++) scene.Tick(Frame);
            Assert.Equal(new[] { 700, 800, 701 }, Messages(host));
        }

        [Fact]
        public void Request2A_WaitsUntilTheEntityHasNothingAsUrgent()
        {
            // The actor waits 5 frames in its own event (priority 16); 0x2A at 16 waits for it, then the director prints.
            var director = Block(Director, new uint[] { 900 }, Code(0x2A, 16, Id(Actor), 0x48, Ref(0), 0x00));
            var actor = Block(Actor, new uint[] { 800, 5 }, Code(0x1C, Ref(1), 0x1D, Ref(0), 0x00));
            var (scene, host) = Scene(actor, director);

            for (int i = 0; i < 30 && !scene.IsFinished; i++) scene.Tick(Frame);
            Assert.Equal(new[] { 800, 900 }, Messages(host));
        }

        [Fact]
        public void RequestToAnEntityOutsideTheEvent_IsSkipped()
        {
            var director = Block(Director, new uint[] { 900 }, Code(0x29, 8, Id(0x010E6099), 1, 0x48, Ref(0), 0x21));
            var (scene, host) = Scene(director);
            scene.Tick(Frame);
            Assert.True(scene.IsFinished);
            Assert.Equal(new[] { 900 }, Messages(host));
        }

        [Fact]
        public void EndOpcode21_EndsEveryEntity_While00EndsOnlyTheRunningRequest()
        {
            var first = Block(Director, Array.Empty<uint>(), Code(0x00));
            var second = Block(Actor, new uint[] { 800, 60 }, Code(0x1C, Ref(1), 0x1D, Ref(0), 0x00));
            var (scene, host) = Scene(first, second);
            scene.Tick(Frame);
            Assert.False(scene.IsFinished); // the second entity still waits

            var ender = Block(Director, Array.Empty<uint>(), Code(0x21));
            var (scene2, host2) = Scene(ender, Block(Actor, new uint[] { 800, 60 }, Code(0x1C, Ref(1), 0x1D, Ref(0), 0x00)));
            scene2.Tick(Frame);
            Assert.True(scene2.IsFinished);
            Assert.True(scene2.IsEnded);
            for (int i = 0; i < 100; i++) scene2.Tick(Frame);
            Assert.Empty(host2.Printed);
        }

        [Fact]
        public void CatchAllOnlyEntity_DoesNotHoldTheEventOpen()
        {
            var carrier = Block(Director, Array.Empty<uint>(), Code(0x00));
            // Event id 0xFFFE only: a long wait.
            var wildcard = new EventBlock(Actor, new ushort[] { 0 }, new ushort[] { EventBlock.AnyEventId }, new uint[] { 600 }, Code(0x1C, Ref(0), 0x00));
            var host = new RecordingHost();
            var scene = new EventScene(new EventWorkZone());
            _ = new EventVm(carrier, EventId, scene, host, Director, 1);
            var vm = new EventVm(wildcard, EventId, scene, host, Actor, 2);
            Assert.False(vm.CarriesEvent);
            scene.Tick(Frame);
            Assert.True(scene.IsFinished);
        }

        [Fact]
        public void MessageWindow_IsSharedByTheEntities()
        {
            // The director prints a prompt line; the actor's 0x23 waits on it too until the player confirms.
            var director = Block(Director, new uint[] { 500 }, Code(0x1D, Ref(0), 0x23, 0x00));
            var actor = Block(Actor, new uint[] { 800, 2 }, Code(0x1C, Ref(1), 0x23, 0x1D, Ref(0), 0x00));
            var (scene, host) = Scene(director, actor);
            host.PromptMessages.Add(500);

            for (int i = 0; i < 10; i++) scene.Tick(Frame);
            Assert.Equal(new[] { 500 }, Messages(host));
            Assert.True(scene.IsWaitingForConfirm);
            scene.Confirm();
            scene.Tick(Frame);
            Assert.Equal(new[] { 500, 800 }, Messages(host));
        }

        [Fact]
        public void OpcodeBE_StoresWhoAskedForTheRunningRequest()
        {
            // The actor's slot 1 stores the requester (0xBE -> local 0) and ends; its own event stores itself.
            var director = Block(Director, Array.Empty<uint>(), Code(0x29, 8, Id(Actor), 1, 0x00));
            var actor = Block(Actor, Array.Empty<uint>(), Code(0xBE, 0x01, 0x00, 0x00), Code(0xBE, 0x00, 0x00, 0x00));
            var (scene, _) = Scene(director, actor);
            for (int i = 0; i < 10 && !scene.IsFinished; i++) scene.Tick(Frame);
            var vm = scene.FindActor(Actor)!;
            Assert.Equal(unchecked((int)Director), vm.Locals[0]);
            Assert.Equal(unchecked((int)Actor), vm.Locals[1]);
        }

        [Theory]
        [InlineData(0x7FFFFFC1u, 0, 1)]
        [InlineData(0x7FFFFFC5u, 0, 5)]
        [InlineData(0x7FFFFFC6u, 1, 0)]
        [InlineData(0x7FFFFFCBu, 1, 5)]
        [InlineData(0x7FFFFFCCu, 2, 0)]
        [InlineData(0x7FFFFFD1u, 2, 5)]
        [InlineData(0x7FFFFFF1u, 0, 1)]
        [InlineData(0x7FFFFFF5u, 0, 5)]
        public void PartyActorCodes_NameAPartySlot(uint code, int party, int slot)
        {
            Assert.True(EventVm.TryGetPartySlot(code, out int p, out int s));
            Assert.Equal((party, slot), (p, s));
        }

        [Theory]
        [InlineData(0x7FFFFFC0u)]
        [InlineData(0x7FFFFFF0u)]
        [InlineData(0x7FFFFFF8u)]
        [InlineData(0x010E6001u)]
        public void OtherActorCodes_AreNotPartySlots(uint code) => Assert.False(EventVm.TryGetPartySlot(code, out _, out _));
    }
}
