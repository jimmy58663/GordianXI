// tests/Gordian.Core.Tests/Events/EventVmRealDataTests.cs
using System;
using System.IO;
using System.Linq;
using Gordian.Core.Events;
using Gordian.Core.Resources;
using Gordian.Core.Resources.Events;
using Gordian.Core.Resources.Tables;
using Xunit;

namespace Gordian.Core.Tests.Events
{
    /// <summary>
    /// Runs retail Southern San d'Oria events through the VM when the install is present: a plain talk event and
    /// the home point menu (LandSandBoat: Ailevia.lua startEvent(615), HomePoint#1.lua event 8700).
    /// </summary>
    public class EventVmRealDataTests
    {
        private const string GameDirectory = @"G:\Program Files (x86)\PlayOnline\SquareEnix\FINAL FANTASY XI";
        private const int SouthernSandoria = 230;
        private const int NorthernSandoria = 231;
        private static readonly TimeSpan Frame = TimeSpan.FromSeconds(1.0 / 60);
        private readonly ITestOutputHelper _output;

        public EventVmRealDataTests(ITestOutputHelper output) => _output = output;

        private static (ZoneEventScript Script, ZoneDialogTable Dialog)? Load()
        {
            if (!Directory.Exists(GameDirectory)) return null;
            var rm = new ResourceManager(GameDirectory);
            rm.InitializeFileTable();
            var script = ZoneEventScript.Parse(rm.LoadDatBytesByFileId(ZoneEventScript.GetFileId(SouthernSandoria))!);
            var dialog = ZoneDialogTable.Parse(rm.LoadDatBytesByFileId(ZoneDialogTable.GetFileId(SouthernSandoria))!);
            return (script!, dialog!);
        }

        /// <summary>
        /// Plays an event the way the client would: confirms every prompt, answers every query with
        /// <paramref name="choose"/> (1-based option for the n-th query), and acknowledges every update.
        /// </summary>
        private static void Drive(EventVm vm, RecordingHost host, Func<int, int> choose, int maxTicks = 5000)
        {
            int answered = 0;
            for (int i = 0; i < maxTicks && !vm.IsFinished; i++)
            {
                vm.Tick(Frame);
                if (vm.IsWaitingForConfirm) vm.Confirm();
                if (host.ReceivePending) host.ReceivePending = false;
                if (host.Queries.Count > answered && host.QueryResult == 0)
                {
                    host.QueryResult = choose(answered);
                    answered++;
                }
            }
        }

        [Fact]
        public void Ailevia_TalkEvent_PrintsHerLine()
        {
            var data = Load();
            if (data == null) return;
            var (script, dialog) = data.Value;
            var block = script.FindEvent(615)!;
            var host = new RecordingHost();
            var zone = new EventWorkZone();
            var vm = new EventVm(block, 615, zone, host, block.ActorId, (ushort)(block.ActorId & 0x3FF));

            Drive(vm, host, _ => 1);
            var lines = host.Printed.Select(p => dialog.GetPlainText(p.Message)).ToList();
            foreach (var line in lines) _output.WriteLine(line);
            _output.WriteLine($"skipped opcodes: {string.Join(" ", host.Skipped.Distinct().Select(b => b.ToString("X2")))} pc={vm.ProgramCounter} op={block.Code[Math.Min(vm.ProgramCounter, block.Code.Length - 1)]:X2} waiting={vm.IsWaitingForConfirm} pending={host.ReceivePending}");
            Assert.True(vm.IsFinished);
            Assert.NotEmpty(lines);
            Assert.Contains(lines, l => l.Contains("street", StringComparison.OrdinalIgnoreCase));
            Assert.All(host.Printed, p => Assert.Equal(EventSpeaker.Entity, p.Speaker));
        }

        /// <summary>
        /// Northern San d'Oria actor 0x010E7017, event 4865, is one call into code past the next event's offset; the
        /// VM must follow it rather than end the event at that offset (#72).
        /// </summary>
        [Fact]
        public void NorthernSandoria_Event4865_FollowsItsCallPastTheNextOffset()
        {
            if (!Directory.Exists(GameDirectory)) return;
            var rm = new ResourceManager(GameDirectory);
            rm.InitializeFileTable();
            var script = ZoneEventScript.Parse(rm.LoadDatBytesByFileId(ZoneEventScript.GetFileId(NorthernSandoria))!)!;
            var dialog = ZoneDialogTable.Parse(rm.LoadDatBytesByFileId(ZoneDialogTable.GetFileId(NorthernSandoria))!)!;
            Assert.True(script.TryGetBlock(0x010E7017, out var block));
            var host = new RecordingHost();
            var vm = new EventVm(block, 4865, new EventWorkZone(), host, block.ActorId, (ushort)(block.ActorId & 0x3FF));
            Drive(vm, host, _ => 1);
            foreach (var p in host.Printed) _output.WriteLine($"msg {p.Message}: {dialog.GetPlainText(p.Message)}");
            Assert.True(vm.IsFinished);
            Assert.Equal(new[] { 17734, 17735, 17736, 17737 }, host.Printed.Select(p => p.Message));
        }

        /// <summary>
        /// Runs every requested NPC event of Northern San d'Oria: every event that ends must end on an end opcode (0x00,
        /// 0x21, a return with an empty stack, 0x26, a cancelled query), not on an unknown opcode or by running off
        /// the code (#72, #73). Some events are still waiting when the tick budget runs out (menus the driver keeps
        /// re-answering, long waits); those are counted but not failed.
        /// </summary>
        [Fact]
        public void NorthernSandoria_NpcEvents_EndOnTheirEndOpcodes()
        {
            if (!Directory.Exists(GameDirectory)) return;
            var rm = new ResourceManager(GameDirectory);
            rm.InitializeFileTable();
            var script = ZoneEventScript.Parse(rm.LoadDatBytesByFileId(ZoneEventScript.GetFileId(NorthernSandoria))!)!;
            int events = 0, clean = 0, running = 0, wrong = 0;
            foreach (var block in script.Blocks)
            {
                if (block.ActorId == EventBlock.ZoneActor || block.ActorId == EventBlock.PlayerActor) continue;
                foreach (ushort eventId in block.EventIds.Distinct())
                {
                    if (eventId == 0xFFFF || eventId == EventBlock.AnyEventId) continue;
                    var host = new RecordingHost();
                    var vm = new EventVm(block, eventId, new EventWorkZone(), host, block.ActorId, (ushort)(block.ActorId & 0x3FF));
                    byte last = 0;
                    vm.Trace = (_, op) => last = op;
                    Drive(vm, host, _ => 1);
                    events++;
                    if (!vm.IsFinished) running++;
                    else if (last is 0x00 or 0x21 or 0x1B or 0x26 or 0x25) clean++;
                    else
                    {
                        wrong++;
                        _output.WriteLine($"actor {block.ActorId:X8} event {eventId}: ended on op {last:X2} at pc {vm.ProgramCounter}");
                    }
                }
            }
            _output.WriteLine($"{events} NPC events: {clean} end cleanly, {running} still running, {wrong} end wrongly");
            Assert.Equal(0, wrong);
            Assert.True(clean >= events * 0.95);
        }

        [Fact]
        public void HomePoint_Event_OpensTheMenuAndReportsTheChoice()
        {
            var data = Load();
            if (data == null) return;
            var (script, dialog) = data.Value;
            var block = script.FindEvent(8700)!;
            var host = new RecordingHost();
            var zone = new EventWorkZone();
            // LandSandBoat: startEvent(8700, 1, g1, g2, g3, g4, gil, 4095, params) for a character with no home points set;
            // params bit 16 marks the crystal as a fresh home point (so the script offers the menu straight away).
            zone.SetParameters(new[] { 1, 0, 0, 0, 0, 1000, 4095, 1 << 16 });
            var vm = new EventVm(block, 8700, zone, host, block.ActorId, (ushort)(block.ActorId & 0x3FF));

            // First query: "What will you do?" -> option 2, "Set this as your home point." (LandSandBoat SET_HOMEPOINT).
            Drive(vm, host, n => n == 0 ? 2 : 1);
            foreach (var p in host.Printed) _output.WriteLine($"msg {p.Message}: {dialog.GetPlainText(p.Message)}");
            foreach (var q in host.Queries) _output.WriteLine($"query {q.Message} default {q.Default} hidden {q.Hidden:X}: {dialog.GetPlainText(q.Message)}");
            _output.WriteLine($"skipped opcodes: {string.Join(" ", host.Skipped.Distinct().Select(b => b.ToString("X2")))} pc={vm.ProgramCounter}");
            var query = host.Queries[0];
            Assert.True(dialog.GetMessage(query.Message)!.HasChoices);
            Assert.Equal("What will you do? / Travel to another home point. Set this as your home point. Other settings. On second thought, never mind.", dialog.GetPlainText(query.Message));
            _output.WriteLine($"updates: {string.Join(",", host.Updates)} end={vm.EndParameter:X} cancelled={vm.IsCancelled} pc={vm.ProgramCounter}");
            Assert.True(vm.IsFinished);
            Assert.NotEmpty(host.Updates);
        }
    }
}
