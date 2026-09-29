// tests/Gordian.Core.Tests/Events/ZoneInCutsceneTests.cs
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
    /// Runs the Rhapsodies of Vana'diel 1-1 zone-in cutscene (event 30035, Northern San d'Oria) that LandSandBoat sends in
    /// S2C 0x00A (issue #122) through the VM from the installed game's event DAT, answering "Yes (View cutscene).". Skipped without the game install.
    /// </summary>
    public class ZoneInCutsceneTests
    {
        private const string GameDirectory = @"G:\Program Files (x86)\PlayOnline\SquareEnix\FINAL FANTASY XI";
        private const int NorthernSandoria = 231;
        private static readonly TimeSpan Frame = TimeSpan.FromSeconds(1.0 / 60);
        private readonly ITestOutputHelper _output;

        public ZoneInCutsceneTests(ITestOutputHelper output) => _output = output;

        [Fact]
        public void RhapsodiesZoneInCutscene_AsksToStartThenEnds()
        {
            if (!Directory.Exists(GameDirectory)) return;
            var rm = new ResourceManager(GameDirectory);
            rm.InitializeFileTable();
            var script = ZoneEventScript.Parse(rm.LoadDatBytesByFileId(ZoneEventScript.GetFileId(NorthernSandoria))!)!;
            var dialog = ZoneDialogTable.Parse(rm.LoadDatBytesByFileId(ZoneDialogTable.GetFileId(NorthernSandoria))!)!;

            var block = script.FindEvent(30035);
            Assert.NotNull(block);
            _output.WriteLine($"block 0x{block!.ActorId:X8}");
            var host = new RecordingHost();
            var zone = new EventWorkZone();
            const uint player = 0x00012345;
            var vm = new EventVm(block, 30035, zone, host, player, 0x0400);
            int answered = 0;
            int ticks = 0;
            for (; ticks < 200000 && !vm.IsFinished; ticks++)
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
                    _output.WriteLine($"query {query.Message}: {string.Join(" | ", comments)} -> {string.Join(" | ", choices)}");
                    host.QueryResult = 1;
                    answered++;
                }
            }
            foreach (var p in host.Printed) _output.WriteLine($"msg {p.Message}: {dialog.GetPlainText(p.Message)}");
            _output.WriteLine($"ticks={ticks} updates={string.Join(",", host.Updates.Select(u => $"0x{u:X}"))} end=0x{vm.EndParameter:X} finished={vm.IsFinished} skipped={string.Join(" ", host.Skipped.Distinct().Select(b => b.ToString("X2")))}");
            Assert.True(vm.IsFinished);
            // The script's own query ("Start Rhapsodies of Vana'diel?"), one update to the server, then the end
            // (LandSandBoat completes the mission on any end parameter).
            Assert.Equal(19402, Assert.Single(host.Queries).Message);
            Assert.Equal(new uint[] { 0 }, host.Updates);
            Assert.Equal(1u, vm.EndParameter);
        }
    }
}
