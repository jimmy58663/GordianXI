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

        private static void AssertNear(System.Numerics.Vector3 expected, System.Numerics.Vector3 actual) =>
            Assert.True(System.Numerics.Vector3.Distance(expected, actual) < 1e-4f, $"expected {expected}, got {actual}");

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

        [Fact]
        public void Opcode37_PlacesTheEntity_OperandsXYHeightAndHeading()
        {
            // 37 x=ref0 y=ref1 height=ref2 heading=ref3 ; 00  (1024 of 4096 steps = a quarter turn = South)
            var block = Block(Director, new uint[] { 1000, 2000, 3000, 1024 }, Code(0x37, Ref(0), Ref(1), Ref(2), Ref(3), 0x00));
            var (scene, host) = Scene(block);
            scene.Tick(Frame);
            var pose = Assert.Single(host.Poses);
            Assert.Equal(Director, pose.Id);
            AssertNear(new System.Numerics.Vector3(1, 3, 2), pose.Position); // internal Y is the height
            Assert.Equal(MathF.PI / 2, pose.Heading, 3);
            Assert.Equal(0f, pose.Speed);
        }

        [Fact]
        public void Opcode1F_WalksAtTheScriptSpeedFacingItsWay_ThenStands()
        {
            // 32 speed=ref0 (30 tenths = 3 yalms/s) ; 1F 00 goal x=ref1 y=ref2 height=ref2 ; 1F 01 ; 48 msg ; 00
            var block = Block(Director, new uint[] { 30, 3000, 0, 900 },
                Code(0x32, Ref(0), 0x1F, 0x00, Ref(1), Ref(2), Ref(2), 0x1F, 0x01, 0x48, Ref(3), 0x00));
            var host = new RecordingHost();
            host.Entities[Director] = (System.Numerics.Vector3.Zero, MathF.PI, 5f);
            var scene = new EventScene(new EventWorkZone());
            _ = new EventVm(block, EventId, scene, host, Director, 1);

            int frames = 0;
            while (host.Printed.Count == 0 && frames++ < 200) scene.Tick(Frame);
            // 3 yalms at 3 yalms/s: about a second of frames, walking East (heading 0) at the script's speed.
            Assert.InRange(frames, 59, 62);
            Assert.All(host.Poses.Take(host.Poses.Count - 1), p => Assert.Equal(3f, p.Speed));
            Assert.All(host.Poses, p => Assert.Equal(0f, p.Heading, 3));
            var last = host.Poses[^1];
            AssertNear(new System.Numerics.Vector3(3, 0, 0), last.Position);
            Assert.Equal(0f, last.Speed); // standing at the goal
        }

        [Fact]
        public void Opcode1F_FromAnUnknownPlace_IsAtTheGoalAtOnce()
        {
            // The entity never arrived and nothing placed it: walking from the origin would take minutes.
            var block = Block(Director, new uint[] { 90000, 0, 900 },
                Code(0x1F, 0x00, Ref(0), Ref(1), Ref(1), 0x1F, 0x01, 0x48, Ref(2), 0x00));
            var (scene, host) = Scene(block);
            scene.Tick(Frame);
            scene.Tick(Frame);
            Assert.Single(host.Printed);
            AssertNear(new System.Numerics.Vector3(90, 0, 0), host.Poses[^1].Position);
        }

        [Fact]
        public void Opcode4A_TurnsTheFirstActorToFaceTheSecond()
        {
            // The director turns the actor toward the player, who stands due North (+Z) of it.
            var director = Block(Director, Array.Empty<uint>(), Code(0x4A, Id(Actor), Id(0x7FFFFFF0), 0x00));
            var actor = Block(Actor, Array.Empty<uint>(), Code(0x00));
            var host = new RecordingHost();
            host.Entities[Actor] = (new System.Numerics.Vector3(5, 0, 5), 0f, 0f);
            host.Entities[0x00012345] = (new System.Numerics.Vector3(5, 0, 15), 0f, 0f);
            var scene = new EventScene(new EventWorkZone()) { PlayerServerId = 0x00012345 };
            _ = new EventVm(director, EventId, scene, host, Director, 1);
            _ = new EventVm(actor, EventId, scene, host, Actor, 2);
            scene.Tick(Frame);
            var pose = Assert.Single(host.Poses);
            Assert.Equal(Actor, pose.Id);
            Assert.Equal(3 * MathF.PI / 2, pose.Heading, 3); // North is wire 192 = 3π/2
            AssertNear(new System.Numerics.Vector3(5, 0, 5), pose.Position);
        }

        [Fact]
        public void Opcode76_WaitsWhileTheActorTurns()
        {
            // 39 heading=ref0 (a half turn from 0) ; 76 actor ; 48 msg ; 00
            var block = Block(Director, new uint[] { 2048, 900 }, Code(0x39, Ref(0), 0x76, Id(Director), 0x48, Ref(1), 0x00));
            var host = new RecordingHost();
            host.Entities[Director] = (System.Numerics.Vector3.Zero, 0f, 0f);
            var scene = new EventScene(new EventWorkZone());
            _ = new EventVm(block, EventId, scene, host, Director, 1);

            int frames = 0;
            while (host.Printed.Count == 0 && frames++ < 200) scene.Tick(Frame);
            // ln(π / 0.05) / 8 = 0.52 s of turning.
            Assert.InRange(frames, 30, 34);
        }

        /// <summary>
        /// 0x59 sub 0 (#197): with a body turn speed of 512 steps a frame the half turn takes 4 frames, not the ease's 31,
        /// and the speed reaches the host for the drawing.
        /// </summary>
        [Fact]
        public void Opcode59Sub0_TurnsAtTheSetSpeed()
        {
            var block = Block(Director, new uint[] { 512, 2048, 900 }, Code(0x59, 0x00, Ref(0), 0x39, Ref(1), 0x76, Id(Director), 0x48, Ref(2), 0x00));
            var host = new RecordingHost();
            host.Entities[Director] = (System.Numerics.Vector3.Zero, 0f, 0f);
            var scene = new EventScene(new EventWorkZone());
            _ = new EventVm(block, EventId, scene, host, Director, 1);

            int frames = 0;
            while (host.Printed.Count == 0 && frames++ < 200) scene.Tick(Frame);
            Assert.InRange(frames, 4, 6);
            Assert.Equal((Director, 512), Assert.Single(host.TurnSpeeds));
        }

        /// <summary>0x59 sub 1 (#197): the turn speed of another actor of the event times that actor's own turns.</summary>
        [Fact]
        public void Opcode59Sub1_SetsAnotherActorsTurnSpeed()
        {
            var director = Block(Director, new uint[] { 1024 }, Code(0x59, 0x01, Id(Actor), Ref(0), 0x00));
            var actor = Block(Actor, Array.Empty<uint>(), Code(0x00));
            var (scene, host) = Scene(director, actor);
            scene.Tick(Frame);
            Assert.Equal((Actor, 1024), Assert.Single(host.TurnSpeeds));
        }

        /// <summary>
        /// 0x59 sub 4 (#197): the walk speed of the VM's own walks, in tenths of a yalm per second, whichever actor the
        /// opcode names (here another actor of the event).
        /// </summary>
        [Fact]
        public void Opcode59Sub4_SetsTheWalkSpeedOfTheVmsOwnWalk()
        {
            // 59 04 actor speed=ref0 (2.0 yalms/s) ; 1F 00 x=ref1 y=ref2 h=ref2 ; 1F 01 ; 00
            var director = Block(Director, new uint[] { 20, 10_000, 0 }, Code(0x59, 0x04, Id(Actor), Ref(0), 0x1F, 0x00, Ref(1), Ref(2), Ref(2), 0x1F, 0x01, 0x00));
            var actor = Block(Actor, Array.Empty<uint>(), Code(0x00));
            var host = new RecordingHost();
            host.Entities[Director] = (System.Numerics.Vector3.Zero, 0f, 4f);
            host.Entities[Actor] = (new System.Numerics.Vector3(5, 0, 5), 0f, 4f);
            var scene = new EventScene(new EventWorkZone());
            _ = new EventVm(director, EventId, scene, host, Director, 1);
            _ = new EventVm(actor, EventId, scene, host, Actor, 2);
            scene.Tick(Frame);
            var walk = host.Poses.Last(p => p.Id == Director);
            Assert.Equal(2f, walk.Speed, 3);
            Assert.Equal(2f / 60f, walk.Position.X, 3);
            Assert.DoesNotContain(host.Poses, p => p.Id == Actor && p.Speed > 0);
        }

        /// <summary>0x59 sub 6 (#197): waits while the actor plays an emote, then goes on.</summary>
        [Fact]
        public void Opcode59Sub6_WaitsForTheActorsEmote()
        {
            // 6E actor emote=ref0 ; 59 06 actor ; 48 msg ; 00
            var block = Block(Director, new uint[] { 13, 900 }, Code(0x6E, Id(Director), Ref(0), 0x59, 0x06, Id(Director), 0x48, Ref(1), 0x00));
            var (scene, host) = Scene(block);
            host.EmoteFrames = 10;
            int frames = 0;
            while (host.Printed.Count == 0 && frames++ < 100) scene.Tick(Frame);
            Assert.InRange(frames, 10, 12);
        }

        /// <summary>
        /// 0x6C (#197): fades the actor's alpha from opaque (0x80) to 0 over 4 frames, stepping it every frame and holding the
        /// script until the time is out: the first call starts the fade and steps once in the same frame.
        /// </summary>
        [Fact]
        public void Opcode6C_FadesTheActorAndWaits()
        {
            var block = Block(Director, new uint[] { 0, 4, 900 }, Code(0x6C, Id(Director), Ref(0), Ref(1), 0x48, Ref(2), 0x00));
            var (scene, host) = Scene(block);
            for (int i = 0; i < 4; i++) scene.Tick(Frame);
            Assert.Empty(host.Printed);
            Assert.Equal(new[] { 96, 64, 32, 0 }, host.AlphaChanges.Select(c => c.Alpha));
            scene.Tick(Frame);
            Assert.Single(host.Printed);
            Assert.Equal(0, host.Alphas[Director]);
        }

        /// <summary>0x6C on an actor that is not in the zone goes on at once without a fade.</summary>
        [Fact]
        public void Opcode6C_PassesForAMissingActor()
        {
            var block = Block(Director, new uint[] { 0, 60, 900 }, Code(0x6C, Id(0x010E6099), Ref(0), Ref(1), 0x48, Ref(2), 0x00));
            var (scene, host) = Scene(block);
            scene.Tick(Frame);
            Assert.Single(host.Printed);
            Assert.Empty(host.AlphaChanges);
        }

        /// <summary>
        /// 0x16 / 0x17 / 0x18 (#197): -r sin, r cos of a 4096-step angle, and atan2(-a, b) at 4096 / π per radian, cut toward
        /// zero. Retail's angle unit is 6.283 / 4096, so a half turn's cosine falls just short of -1 and r = 1500 gives -1499.
        /// </summary>
        [Fact]
        public void Opcodes16To18_StoreTheTrigonometry()
        {
            // 16 L0 = -1500 sin(0) ; 17 L1 = 1500 cos(0) ; 17 L2 = 1500 cos(2048) ; 18 L3 = atan2(-0, -1000) ; 18 L4 = atan2(-1000, 0) ; 00
            var block = Block(Director, new uint[] { 0, 1500, 2048, 1000, unchecked((uint)-1000) }, Code(
                0x16, 0x00, 0x00, Ref(0), Ref(1),
                0x17, 0x01, 0x00, Ref(0), Ref(1),
                0x17, 0x02, 0x00, Ref(2), Ref(1),
                0x18, 0x03, 0x00, Ref(0), Ref(4),
                0x18, 0x04, 0x00, Ref(3), Ref(0),
                0x00));
            var host = new RecordingHost();
            var scene = new EventScene(new EventWorkZone());
            var vm = new EventVm(block, EventId, scene, host, Director, 1);
            scene.Tick(Frame);
            Assert.Equal(new[] { 0, 1500, -1499, 4096, -2048 }, vm.Locals.Take(5));
        }

        [Fact]
        public void Opcode76_PassesWhenTheActorIsNotTurning()
        {
            var block = Block(Director, new uint[] { 900 }, Code(0x76, Id(Director), 0x48, Ref(0), 0x00));
            var (scene, host) = Scene(block);
            scene.Tick(Frame);
            Assert.Single(host.Printed);
        }

        [Fact]
        public void Opcodes5EAnd6B_ReturnTheActorToIdle()
        {
            // 66 gesture on the director, then 5E resets the event entity and 6B the actor
            var block = Block(Director, new uint[] { 900 }, Code(0x5E, Id(0x306C6469), 0x6B, Id(0x306C6469), Id(Actor), 0x48, Ref(0), 0x00));
            var actor = Block(Actor, Array.Empty<uint>(), Code(0x00));
            var (scene, host) = Scene(block, actor);
            scene.Tick(Frame);
            Assert.Equal(new[] { (Director, string.Empty), (Actor, string.Empty) }, host.StoppedMotions);
            Assert.Single(host.Printed);
        }

        [Fact]
        public void OpcodeBA_PlacesAnotherActorOfTheEvent()
        {
            // BA actor x=ref0 y=ref1 height=ref2 heading=ref3
            var director = Block(Director, new uint[] { 7000, 8000, 500, 2048 }, Code(0xBA, Id(Actor), Ref(0), Ref(1), Ref(2), Ref(3), 0x00));
            var actor = Block(Actor, Array.Empty<uint>(), Code(0x00));
            var (scene, host) = Scene(director, actor);
            scene.Tick(Frame);
            var pose = Assert.Single(host.Poses);
            Assert.Equal(Actor, pose.Id);
            AssertNear(new System.Numerics.Vector3(7, 0.5f, 8), pose.Position);
            Assert.Equal(MathF.PI, pose.Heading, 3);
        }

        [Fact]
        public void Opcodes22And4E_SetTheEventHideFlag()
        {
            // 22 01 hides the entity itself; 4E 00 actor shows another; 4E 01 player hides the player.
            var director = Block(Director, Array.Empty<uint>(), Code(0x22, 0x01, 0x4E, 0x00, Id(Actor), 0x4E, 0x01, Id(0x7FFFFFF0), 0x00));
            var host = new RecordingHost();
            var scene = new EventScene(new EventWorkZone()) { PlayerServerId = 0x00012345 };
            _ = new EventVm(director, EventId, scene, host, Director, 1);
            scene.Tick(Frame);
            Assert.True(host.Hidden[Director]);
            Assert.False(host.Hidden[Actor]);
            Assert.True(host.Hidden[0x00012345]);
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
