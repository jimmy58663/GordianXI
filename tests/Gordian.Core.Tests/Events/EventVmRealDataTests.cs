// tests/Gordian.Core.Tests/Events/EventVmRealDataTests.cs
using System;
using System.IO;
using System.Linq;
using Gordian.Core.Events;
using Gordian.Core.Resources;
using Gordian.Core.Resources.Events;
using Gordian.Core.Resources.Tables;
using Xunit;
using Xunit.Abstractions;

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
