// tests/Gordian.Core.Tests/Events/HomePointCaptureTests.cs
using System;
using System.Collections.Generic;
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
    /// Replays the maintainer's retail packet capture of the Southern San d'Oria home points (2026-09-28) through the
    /// VM and checks the client's answers match the capture:
    /// <list type="bullet">
    /// <item>Talk to Home Point #2 (event 8701, params [1, -1, -1, -1, 0x3FFFFFF, 10, 4095, 1]): update 8 (0x05B mode 1),
    /// then "Set this as your home point." ends with 1.</item>
    /// <item>Talk again, "Travel to another home point." then the current region and Home Point #1 of the zone:
    /// updates 8 and 2, then a position update (0x05C mode 1) with parameter 3 to (-84.47, 1.00, -65.45), then the end with 3.</item>
    /// <item>Talk to Home Point #1 (event 8700, last param 0) and travel to Northern San d'Oria's Home Point #1:
    /// updates 8 and 0x30002 (teleport, home point 3), then the end with 0x30002 and a zone change.</item>
    /// </list>
    /// </summary>
    public class HomePointCaptureTests
    {
        private const string GameDirectory = @"G:\Program Files (x86)\PlayOnline\SquareEnix\FINAL FANTASY XI";
        private const int SouthernSandoria = 230;
        private static readonly TimeSpan Frame = TimeSpan.FromSeconds(1.0 / 60);
        private readonly ITestOutputHelper _output;

        public HomePointCaptureTests(ITestOutputHelper output) => _output = output;

        private static (ZoneEventScript Script, ZoneDialogTable Dialog)? Load()
        {
            if (!Directory.Exists(GameDirectory)) return null;
            var rm = new ResourceManager(GameDirectory);
            rm.InitializeFileTable();
            var script = ZoneEventScript.Parse(rm.LoadDatBytesByFileId(ZoneEventScript.GetFileId(SouthernSandoria))!);
            var dialog = ZoneDialogTable.Parse(rm.LoadDatBytesByFileId(ZoneDialogTable.GetFileId(SouthernSandoria))!);
            return (script!, dialog!);
        }

        /// <summary>Plays an event, answering each query by the text of the option to pick (a substring), in order.</summary>
        private void Drive(EventVm vm, RecordingHost host, ZoneDialogTable dialog, EventWorkZone zone, Queue<string> picks, int maxTicks = 20000)
        {
            int answered = 0;
            var trace = new Queue<(int Pc, byte Op)>();
            vm.Trace = (pc, op) =>
            {
                trace.Enqueue((pc, op));
                if (trace.Count > 60) trace.Dequeue();
            };
            for (int i = 0; i < maxTicks && !vm.IsFinished; i++)
            {
                vm.Tick(Frame);
                if (vm.IsWaitingForConfirm) vm.Confirm();
                if (host.ReceivePending) host.ReceivePending = false;
                if (host.Queries.Count > answered && host.QueryResult == 0)
                {
                    var query = host.Queries[answered];
                    var message = dialog.GetMessage(query.Message)!;
                    var context = new SimpleMessageContext(Enumerable.Range(0, 40).Select(zone.GetMessageParameter).ToArray());
                    var (comments, choices) = EventMessageFormatter.FormatQuery(message, context);
                    _output.WriteLine($"query {query.Message} default {query.Default} hidden 0x{query.Hidden:X}: {string.Join(" | ", comments)}");
                    _output.WriteLine($"   zone[0..40]: {string.Join(",", zone.Zone.Take(41))}");
                    _output.WriteLine($"   locals[0..48]: {string.Join(",", vm.Locals.Take(49))}");
                    for (int c = 0; c < choices.Count; c++) _output.WriteLine($"   {c + 1}: {choices[c]}{(((query.Hidden >> c) & 1) != 0 ? " (hidden)" : "")}");
                    string pick = picks.Count > 0 ? picks.Dequeue() : "";
                    int number = choices.FindIndex(c => c.Contains(pick, StringComparison.OrdinalIgnoreCase)) + 1;
                    if (number == 0) number = 255;
                    _output.WriteLine($"   -> pick '{pick}' = {number}");
                    host.QueryResult = number;
                    answered++;
                }
            }
            foreach (var p in host.Printed) _output.WriteLine($"msg {p.Message}: {dialog.GetPlainText(p.Message)}");
            if (host.Skipped.Contains(0xFF)) _output.WriteLine($"trace: {string.Join(" ", trace.Select(t => $"{t.Pc}:{t.Op:X2}"))}");
            _output.WriteLine($"updates: {string.Join(",", host.Updates.Select(u => $"0x{u:X}"))} positions: {string.Join(",", host.PositionUpdates.Select(p => $"0x{p.Parameter:X}@({p.X:F2},{p.Y:F2},{p.Z:F2})"))} end=0x{vm.EndParameter:X} finished={vm.IsFinished} cancelled={vm.IsCancelled} pc={vm.ProgramCounter} skipped={string.Join(" ", host.Skipped.Distinct().Select(b => b.ToString("X2")))}");
        }

        private static int[] CaptureParameters(int last) => new[] { 1, -1, -1, -1, 0x3FFFFFF, 10, 4095, last };

        [Fact]
        public void SetHomePoint_EndsWithOne()
        {
            var data = Load();
            if (data == null) return;
            var (script, dialog) = data.Value;
            var block = script.FindEvent(8701)!;
            var host = new RecordingHost();
            var zone = new EventWorkZone();
            zone.SetParameters(CaptureParameters(1));
            var vm = new EventVm(block, 8701, zone, host, block.ActorId, (ushort)(block.ActorId & 0x3FF));
            Drive(vm, host, dialog, zone, new Queue<string>(new[] { "Set this as your home point" }));
            Assert.True(vm.IsFinished);
            Assert.Equal(new uint[] { 8 }, host.Updates);
            Assert.Equal(1u, vm.EndParameter);
        }

        [Fact]
        public void TravelWithinTheZone_SendsThePositionUpdate()
        {
            var data = Load();
            if (data == null) return;
            var (script, dialog) = data.Value;
            var block = script.FindEvent(8701)!;
            var host = new RecordingHost();
            var zone = new EventWorkZone();
            zone.SetParameters(CaptureParameters(1));
            var vm = new EventVm(block, 8701, zone, host, block.ActorId, (ushort)(block.ActorId & 0x3FF));
            // The zone list names zones by id until a zone name table exists: 230 = Southern San d'Oria.
            Drive(vm, host, dialog, zone, new Queue<string>(new[] { "Travel to another home point", "Current region", "<8230>", "Home Point #1", "Yes, please" }));
            Assert.True(vm.IsFinished);
            Assert.Equal(new uint[] { 8, 2 }, host.Updates);
            var position = Assert.Single(host.PositionUpdates);
            Assert.Equal(3u, position.Parameter);
            Assert.Equal(-84.47, position.X, 2);
            Assert.Equal(1.0, position.Y, 2);
            Assert.Equal(-65.45, position.Z, 2);
            Assert.Equal(3u, vm.EndParameter);
        }

        [Fact]
        public void TravelToAnotherZone_EndsWithTheTeleportChoice()
        {
            var data = Load();
            if (data == null) return;
            var (script, dialog) = data.Value;
            var block = script.FindEvent(8700)!;
            var host = new RecordingHost();
            var zone = new EventWorkZone();
            zone.SetParameters(CaptureParameters(0));
            var vm = new EventVm(block, 8700, zone, host, block.ActorId, (ushort)(block.ActorId & 0x3FF));
            // 231 = Northern San d'Oria.
            Drive(vm, host, dialog, zone, new Queue<string>(new[] { "Travel to another home point", "San d'Oria", "<8231>", "Home Point #1", "Yes, please" }));
            Assert.True(vm.IsFinished);
            Assert.Equal(new uint[] { 8, 0x30002 }, host.Updates);
            Assert.Equal(0x30002u, vm.EndParameter);
        }
    }
}
