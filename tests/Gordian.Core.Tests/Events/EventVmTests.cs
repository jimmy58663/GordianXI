// tests/Gordian.Core.Tests/Events/EventVmTests.cs
using System;
using System.Collections.Generic;
using Gordian.Core.Events;
using Gordian.Core.Resources.Events;
using Xunit;

namespace Gordian.Core.Tests.Events
{
    /// <summary>A scripted host that records what the VM asked for.</summary>
    internal sealed class RecordingHost : IEventVmHost
    {
        public List<(int Message, EventSpeaker Speaker, uint Id)> Printed { get; } = new();
        public List<(int Message, int Default, uint Hidden)> Queries { get; } = new();
        public List<uint> Updates { get; } = new();
        public List<(uint Parameter, float X, float Y, float Z)> PositionUpdates { get; } = new();
        public List<bool> Locks { get; } = new();
        public List<byte> Skipped { get; } = new();
        public HashSet<int> PromptMessages { get; } = new();
        public Dictionary<int, int> EntityValues { get; } = new();
        public int QueryResult { get; set; }
        public bool ReceivePending { get; set; }
        public int GameTime { get; set; } = 1234;
        public int Closed { get; private set; }

        /// <summary>How long a prompt message stays open in these tests (long enough that only Confirm closes it).</summary>
        public double PromptSeconds { get; set; } = 60;

        public double PrintMessage(int messageId, EventSpeaker speaker, uint speakerServerId, ushort speakerIndex)
        {
            Printed.Add((messageId, speaker, speakerServerId));
            return PromptMessages.Contains(messageId) ? PromptSeconds : 0;
        }

        public void OpenQuery(int messageId, int defaultIndex, uint hiddenMask) => Queries.Add((messageId, defaultIndex, hiddenMask));
        public void CloseQuery()
        {
            Closed++;
            QueryResult = 0;
        }
        public void SendEventUpdate(uint endParameter)
        {
            Updates.Add(endParameter);
            ReceivePending = true;
        }
        public void SendEventUpdateXzy(uint endParameter, float x, float y, float z, float heading)
        {
            PositionUpdates.Add((endParameter, x, y, z));
            ReceivePending = true;
        }
        public void SetControlLock(bool locked) => Locks.Add(locked);
        public bool EntityExists(uint serverId) => serverId == 0x010E6001 || Entities.ContainsKey(serverId);
        public int GetEntityValue(uint serverId, int key) => EntityValues.TryGetValue(key, out int v) ? v : 0;
        public void OnSkippedOpcode(byte opcode, int pc) => Skipped.Add(opcode);

        /// <summary>Where the entities stand before the event (server id → position, heading, speed).</summary>
        public Dictionary<uint, (System.Numerics.Vector3 Position, float Heading, float Speed)> Entities { get; } = new();
        public List<(uint Id, System.Numerics.Vector3 Position, float Heading, float Speed)> Poses { get; } = new();
        public Dictionary<uint, bool> Hidden { get; } = new();

        public bool TryGetEntityPose(uint serverId, out System.Numerics.Vector3 position, out float heading, out float speed)
        {
            bool known = Entities.TryGetValue(serverId, out var e);
            (position, heading, speed) = e;
            return known;
        }

        public void SetEntityPose(uint serverId, System.Numerics.Vector3 position, float heading, float speed) =>
            Poses.Add((serverId, position, heading, speed));

        public void SetEntityHidden(uint serverId, bool hidden) => Hidden[serverId] = hidden;

        /// <summary>How many frames each scene routine (by name) runs; routines not listed are missing (0 frames).</summary>
        public Dictionary<string, int> RoutineFrames { get; } = new();
        public List<(int Id, int FileId, string Routine, uint Caster, uint Target)> SceneTasks { get; } = new();
        public List<int> StoppedTasks { get; } = new();
        public List<bool> CameraHolds { get; } = new();

        /// <summary>When set, how many frames a scene routine (file id, name) runs, instead of <see cref="RoutineFrames"/>.</summary>
        public Func<int, string, int>? RoutineSource { get; set; }

        /// <summary>When set, how many frames an entity motion (source, resource, name) plays, instead of <see cref="MotionFrames"/>.</summary>
        public Func<EventMotionSource, int, string, int>? MotionSource { get; set; }

        /// <summary>How many frames each entity motion (by routine name) plays.</summary>
        public Dictionary<string, int> MotionFrames { get; } = new();
        public List<(uint Id, EventMotionSource Source, int Resource, string Routine)> Motions { get; } = new();
        public List<(uint Id, string Routine)> StoppedMotions { get; } = new();

        public int StartSceneTask(int taskId, int fileId, string routine, uint casterServerId, uint targetServerId)
        {
            SceneTasks.Add((taskId, fileId, routine, casterServerId, targetServerId));
            if (RoutineSource != null) return RoutineSource(fileId, routine);
            return RoutineFrames.TryGetValue(routine, out int frames) ? frames : 0;
        }

        public void StopSceneTask(int taskId) => StoppedTasks.Add(taskId);
        public void SetEventCamera(bool held) => CameraHolds.Add(held);

        public int PlayEntityMotion(uint serverId, EventMotionSource source, int resource, string routine, uint targetServerId)
        {
            Motions.Add((serverId, source, resource, routine));
            if (MotionSource != null) return MotionSource(source, resource, routine);
            return MotionFrames.TryGetValue(routine, out int frames) ? frames : 0;
        }

        public void StopEntityMotion(uint serverId, string routine) => StoppedMotions.Add((serverId, routine));

        public List<(uint Id, int Emote, int Variant)> Emotes { get; } = new();
        public int EmoteFrames { get; set; }

        public int PlayEntityEmote(uint serverId, int emote, int variant)
        {
            Emotes.Add((serverId, emote, variant));
            return EmoteFrames;
        }

        public List<int> OpenedZones { get; } = new();
        public bool IsEventZoneLoading { get; set; }
        public void OpenEventZone(int zoneId) => OpenedZones.Add(zoneId);

        public List<(uint Id, uint Target, int SpeechFrame)> Looks { get; } = new();
        public void SetEntityLook(uint serverId, uint targetServerId, int speechFrame) => Looks.Add((serverId, targetServerId, speechFrame));

        public List<(uint Id, int AxisX, int AxisY)> LookAxes { get; } = new();
        public void SetEntityLookAxis(uint serverId, int axisX, int axisY) => LookAxes.Add((serverId, axisX, axisY));

        public List<(uint Id, bool Keep)> KeepsHeight { get; } = new();
        public void SetEntityKeepsHeight(uint serverId, bool keep) => KeepsHeight.Add((serverId, keep));
        public List<(uint Id, int Speed)> HeadTurnSpeeds { get; } = new();
        public void SetEntityHeadTurnSpeed(uint serverId, int speed) => HeadTurnSpeeds.Add((serverId, speed));
    }

    public class EventVmTests
    {
        private static readonly TimeSpan Frame = TimeSpan.FromSeconds(1.0 / 60);

        private static EventVm Make(byte[] code, RecordingHost host, EventWorkZone? zone = null, uint[]? references = null)
        {
            var block = new EventBlock(0x010E6001, offsets: new ushort[] { 0 }, eventIds: new ushort[] { 100 }, references ?? Array.Empty<uint>(), code);
            return new EventVm(block, 100, zone ?? new EventWorkZone(), host, 0x010E6001, 1);
        }

        private static byte[] Ref(int index) => new[] { (byte)(0x8000 | index), (byte)((0x8000 | index) >> 8) };

        [Fact]
        public void TalkEvent_PrintsMessagesAndWaitsForConfirm()
        {
            // 1D msg(ref0) ; 23 ; 1D msg(ref1) ; 23 ; 21
            var code = new byte[] { 0x1D, 0x00, 0x80, 0x23, 0x1D, 0x01, 0x80, 0x23, 0x21 };
            var host = new RecordingHost();
            host.PromptMessages.Add(500);
            var vm = Make(code, host, references: new uint[] { 500, 501 });

            vm.Tick(Frame);
            Assert.Single(host.Printed);
            Assert.Equal((500, EventSpeaker.Entity, 0x010E6001u), host.Printed[0]);
            Assert.True(vm.IsWaitingForConfirm);
            Assert.False(vm.IsFinished);

            vm.Tick(Frame); // still waiting
            Assert.Single(host.Printed);

            vm.Confirm();
            vm.Tick(Frame);
            Assert.Equal(2, host.Printed.Count);
            Assert.Equal(501, host.Printed[1].Message);
            Assert.True(vm.IsFinished); // message 501 has no prompt: 23 passes, 21 ends
            Assert.False(vm.IsCancelled);
            Assert.Equal(0u, vm.EndParameter);
        }

        [Fact]
        public void PromptMessage_ClosesOnItsOwnAfterItsTime()
        {
            var code = new byte[] { 0x1D, 0x00, 0x80, 0x23, 0x21 };
            var host = new RecordingHost { PromptSeconds = 1.0 };
            host.PromptMessages.Add(500);
            var vm = Make(code, host, references: new uint[] { 500 });
            vm.Tick(Frame);
            Assert.True(vm.IsWaitingForConfirm);
            vm.Tick(TimeSpan.FromSeconds(0.5));
            Assert.True(vm.IsWaitingForConfirm);
            Assert.False(vm.IsFinished);
            vm.Tick(TimeSpan.FromSeconds(0.6));
            Assert.False(vm.IsWaitingForConfirm);
            Assert.True(vm.IsFinished);
        }

        [Fact]
        public void Query_StoresSelectionAndSendsUpdate()
        {
            // 24 msg(ref0) default(ref1) hidden(ref2) ; 25 ; 03 zone[1] = zone[0] ; 43 00 ; 43 01 ; 00
            var code = new byte[]
            {
                0x24, 0x00, 0x80, 0x01, 0x80, 0x02, 0x80,
                0x25,
                0x03, 0x01, 0x10, 0x00, 0x10,
                0x43, 0x00,
                0x43, 0x01,
                0x00,
            };
            var host = new RecordingHost();
            var zone = new EventWorkZone();
            var vm = Make(code, host, zone, new uint[] { 26, 1, 0 });

            vm.Tick(Frame);
            Assert.Equal((26, 1, 0u), Assert.Single(host.Queries));
            Assert.False(vm.IsFinished);

            host.QueryResult = 3; // third option
            vm.Tick(Frame);
            Assert.Equal(1, host.Closed);
            Assert.Equal(2, zone.Selection);
            vm.Tick(Frame);
            Assert.Equal(2u, Assert.Single(host.Updates)); // zone[1] copied from zone[0]
            Assert.False(vm.IsFinished); // waiting for the server

            host.ReceivePending = false;
            vm.Tick(Frame);
            vm.Tick(Frame);
            Assert.True(vm.IsFinished);
            Assert.Equal(2u, vm.EndParameter);
        }

        [Fact]
        public void Query_CancelEndsTheEventAsCancelled()
        {
            var code = new byte[] { 0x24, 0x00, 0x80, 0x01, 0x80, 0x02, 0x80, 0x25, 0x00 };
            var host = new RecordingHost();
            var zone = new EventWorkZone();
            var vm = Make(code, host, zone, new uint[] { 26, 0, 0 });
            vm.Tick(Frame);
            host.QueryResult = 255;
            vm.Tick(Frame);
            Assert.True(vm.IsFinished);
            Assert.True(vm.IsCancelled);
            Assert.Equal(254, zone.Selection);
            Assert.Equal(EventVm.CancelledEndParameter, vm.EndParameter);
        }

        [Fact]
        public void If_JumpsPastTheElseBranch()
        {
            // 03 local0 = ref0(5) ; 02 local0 == ref1(5) kind 1 -> jump to 17 ; 48 msg ref2 ; 00 ; [16] 48 msg ref3 ; 00
            var code = new byte[]
            {
                0x03, 0x00, 0x00, 0x00, 0x80,
                0x02, 0x00, 0x00, 0x01, 0x80, 0x01, 0x11, 0x00,
                0x48, 0x02, 0x80,
                0x00,
                0x48, 0x03, 0x80,
                0x00,
            };
            var host = new RecordingHost();
            var vm = Make(code, host, references: new uint[] { 5, 5, 700, 701 });
            vm.Tick(Frame);
            Assert.Equal(701, Assert.Single(host.Printed).Message);
            Assert.True(vm.IsFinished);
        }

        [Fact]
        public void JumpAndReturn_UseTheReturnStack()
        {
            // 1A -> 7 ; 48 ref0 ; 00 ; [7] 48 ref1 ; 1B
            var code = new byte[] { 0x1A, 0x07, 0x00, 0x48, 0x00, 0x80, 0x00, 0x48, 0x01, 0x80, 0x1B };
            var host = new RecordingHost();
            var vm = Make(code, host, references: new uint[] { 10, 11 });
            vm.Tick(Frame);
            Assert.Equal(new[] { 11, 10 }, host.Printed.ConvertAll(p => p.Message));
            Assert.True(vm.IsFinished);
        }

        [Fact]
        public void BitWork_SetsAndReadsBitFields()
        {
            // 40 from(ref0=0) to(ref1=15) target(zone[1]) value(ref2=8) ; 40 from(ref3=16) to(ref4=31) zone[1] value(ref0=0)
            // 41 from 0 to 15 of zone[1] -> local 4 ; 00
            var code = new byte[]
            {
                0x40, 0x00, 0x80, 0x01, 0x80, 0x01, 0x10, 0x02, 0x80,
                0x40, 0x03, 0x80, 0x04, 0x80, 0x01, 0x10, 0x00, 0x80,
                0x41, 0x00, 0x80, 0x01, 0x80, 0x01, 0x10, 0x04, 0x00,
                0x03, 0x05, 0x00, 0x04, 0x00,
                0x00,
            };
            var host = new RecordingHost();
            var zone = new EventWorkZone();
            var vm = Make(code, host, zone, new uint[] { 0, 15, 8, 16, 31 });
            vm.Tick(Frame);
            Assert.Equal(8, zone.EndParameter);
            Assert.True(vm.IsFinished);
        }

        [Fact]
        public void ServerParametersAndLocalsFlowThroughTheZone()
        {
            // 03 local0 = zone[3] ; 0B local0++ ; 03 zone[1] = local0 ; 00
            var code = new byte[] { 0x03, 0x00, 0x00, 0x03, 0x10, 0x0B, 0x00, 0x00, 0x03, 0x01, 0x10, 0x00, 0x00, 0x00 };
            var host = new RecordingHost();
            var zone = new EventWorkZone();
            zone.SetParameters(new[] { 1, 41 }); // num[0] -> zone[2], num[1] -> zone[3]
            var vm = Make(code, host, zone);
            vm.Tick(Frame);
            Assert.Equal(42, zone.EndParameter);
        }

        [Fact]
        public void Waits_CountFrames()
        {
            // 1C wait(ref0 = 30 frames) ; 48 ref1 ; 00
            var code = new byte[] { 0x1C, 0x00, 0x80, 0x48, 0x01, 0x80, 0x00 };
            var host = new RecordingHost();
            var vm = Make(code, host, references: new uint[] { 30, 5 });
            vm.Tick(TimeSpan.FromSeconds(0.25));
            Assert.Empty(host.Printed);
            vm.Tick(TimeSpan.FromSeconds(0.3)); // the wait runs out and yields (as retail's 0x1C does)
            vm.Tick(Frame);
            Assert.Single(host.Printed);
            Assert.True(vm.IsFinished);
        }

        [Fact]
        public void UnknownOpcodes_AreSteppedOverByLength_AndUnknownLengthsEndTheEvent()
        {
            // 1E look-at (5 bytes, skipped) ; 20 01 lock ; 7D ... (3 bytes, skipped) ; 48 ref0 ; E7 (unknown length) ; 48 ref1
            var code = new byte[]
            {
                0x1E, 0xF0, 0xFF, 0xFF, 0x7F,
                0x20, 0x01,
                0x7D, 0x01, 0x02,
                0x48, 0x00, 0x80,
                0xE7, 0x01, 0x11, 0x22,
                0x48, 0x01, 0x80,
            };
            var host = new RecordingHost();
            var vm = Make(code, host, references: new uint[] { 9, 10 });
            vm.Tick(Frame);
            Assert.Equal(new[] { true }, host.Locks);
            Assert.Equal(9, Assert.Single(host.Printed).Message);
            Assert.Contains((byte)0x7D, host.Skipped);
            Assert.Contains((byte)0xE7, host.Skipped);
            Assert.True(vm.IsFinished);
        }

        [Fact]
        public void EntityFacts_ComeFromTheHost()
        {
            // 03 local0 = 0x7F86 (player job) ; 44 exists(ref0) else -> end ; 03 zone[1] = local0 ; 00
            var code = new byte[]
            {
                0x03, 0x00, 0x00, 0x86, 0x7F,
                0x44, 0x00, 0x80, 0x0E, 0x00,
                0x03, 0x01, 0x10, 0x00, 0x00,
                0x00,
            };
            var host = new RecordingHost();
            host.EntityValues[6] = 7; // job 7 = PLD
            var zone = new EventWorkZone();
            var vm = Make(code, host, zone, new uint[] { 0x010E6001 });
            vm.Tick(Frame);
            Assert.Equal(7, zone.EndParameter);
            Assert.True(vm.IsFinished);
        }

        [Fact]
        public void MissingEvent_IsFinishedAtOnce()
        {
            var block = new EventBlock(1, offsets: new ushort[] { 0 }, eventIds: new ushort[] { 5 }, Array.Empty<uint>(), new byte[] { 0x00 });
            var vm = new EventVm(block, 6, new EventWorkZone(), new RecordingHost(), 1, 1);
            Assert.True(vm.IsFinished);
        }

        [Fact]
        public void OpcodeTable_KnowsSubCaseLengths()
        {
            Assert.Equal(8, EventOpcodeTable.GetLength(new byte[] { 0x1F, 0x00 }, 0));
            Assert.Equal(2, EventOpcodeTable.GetLength(new byte[] { 0x1F, 0x01 }, 0));
            Assert.Equal(4, EventOpcodeTable.GetLength(new byte[] { 0x46, 0x02 }, 0));
            Assert.Equal(15, EventOpcodeTable.GetLength(new byte[] { 0x66 }, 0));
            Assert.Equal(0, EventOpcodeTable.GetLength(new byte[] { 0xE7 }, 0));
        }

        /// <summary>Lengths corrected by the retail corpus walk (#73): total bytes, opcode included.</summary>
        [Theory]
        [InlineData(new byte[] { 0x5F, 0x00 }, 2)]
        [InlineData(new byte[] { 0x5F, 0x02 }, 6)]
        [InlineData(new byte[] { 0x5F, 0x03 }, 16)]
        [InlineData(new byte[] { 0x5F, 0x05 }, 18)]
        [InlineData(new byte[] { 0x5F, 0x07 }, 14)]
        [InlineData(new byte[] { 0x7E, 0x01 }, 6)]
        [InlineData(new byte[] { 0x7E, 0x03 }, 16)]
        [InlineData(new byte[] { 0x7E, 0x06 }, 18)]
        [InlineData(new byte[] { 0x7E, 0x07 }, 8)]
        [InlineData(new byte[] { 0x7E, 0x09 }, 0)]
        [InlineData(new byte[] { 0xB4, 0x14 }, 12)]
        [InlineData(new byte[] { 0xB4, 0x15 }, 2)]
        [InlineData(new byte[] { 0xB6, 0x14 }, 6)]
        [InlineData(new byte[] { 0xC4 }, 12)]
        [InlineData(new byte[] { 0x75, 0x02 }, 2)]
        [InlineData(new byte[] { 0x60, 0x01 }, 4)]
        [InlineData(new byte[] { 0x60, 0x02 }, 6)]
        [InlineData(new byte[] { 0x60, 0x09 }, 2)]
        [InlineData(new byte[] { 0xBF, 0x20 }, 10)]
        [InlineData(new byte[] { 0x72, 0x00 }, 4)]
        [InlineData(new byte[] { 0x72, 0x01 }, 6)]
        [InlineData(new byte[] { 0xA6, 0x01 }, 2)]
        [InlineData(new byte[] { 0xA6, 0x02 }, 4)]
        [InlineData(new byte[] { 0x9D, 0x07 }, 6)]
        [InlineData(new byte[] { 0x9D, 0x0C }, 8)]
        [InlineData(new byte[] { 0x9D, 0x0D }, 10)]
        [InlineData(new byte[] { 0xD4, 0x00 }, 8)]
        public void OpcodeTable_CorpusCorrectedLengths(byte[] code, int length) =>
            Assert.Equal(length, EventOpcodeTable.GetLength(code, 0));

        [Fact]
        public void Event_RunsCodePastTheNextEventsOffset()
        {
            // Modelled on Northern San d'Oria actor 0x010E7017, event 4865: the event is a call into code past the
            // next event's offset, then an end. Event 100: 1A -> 8 ; 21 | event 101 at 4: 48 ref1 ; 00 | [8] 48 ref0 ; 1B
            var code = new byte[] { 0x1A, 0x08, 0x00, 0x21, 0x48, 0x01, 0x80, 0x00, 0x48, 0x00, 0x80, 0x1B };
            var block = new EventBlock(0x010E6001, offsets: new ushort[] { 0, 4 }, eventIds: new ushort[] { 100, 101 }, new uint[] { 17734, 99 }, code);
            var host = new RecordingHost();
            var vm = new EventVm(block, 100, new EventWorkZone(), host, 0x010E6001, 1);
            vm.Tick(Frame);
            Assert.Equal(17734, Assert.Single(host.Printed).Message);
            Assert.True(vm.IsFinished);
            Assert.Equal(3, vm.ProgramCounter); // ended on its own 0x21
        }

        [Fact]
        public void FrameDelay_Opcode57_AddsTheTicksFrames()
        {
            // 57 local0 += frame delay ; 03 zone[1] = local0 ; 00
            var code = new byte[] { 0x57, 0x00, 0x00, 0x03, 0x01, 0x10, 0x00, 0x00, 0x00 };
            var zone = new EventWorkZone();
            var vm = Make(code, new RecordingHost(), zone);
            vm.Tick(TimeSpan.FromSeconds(2.0 / 60));
            Assert.Equal(2, zone.EndParameter);
        }

        [Fact]
        public void Sleep_Opcode6F_CountsTheTicksFramesEvenAfterOtherOpcodes()
        {
            // 48 ref0 ; 6F (16-frame sleep) ; 48 ref1 ; 00
            var code = new byte[] { 0x48, 0x00, 0x80, 0x6F, 0x48, 0x01, 0x80, 0x00 };
            var host = new RecordingHost();
            var vm = Make(code, host, references: new uint[] { 1, 2 });
            var tenFrames = TimeSpan.FromSeconds(10.0 / 60);
            vm.Tick(tenFrames); // prints, then the sleep takes this tick's 10 frames: 6 left
            Assert.Single(host.Printed);
            vm.Tick(tenFrames); // runs out and yields
            Assert.Single(host.Printed);
            vm.Tick(Frame);
            Assert.Equal(2, host.Printed.Count);
            Assert.True(vm.IsFinished);
        }
    }
}
