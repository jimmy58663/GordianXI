// tests/Gordian.Core.Tests/Events/ZoneInCutsceneTests.cs
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Gordian.Core.Config;
using Gordian.Core.Events;
using Gordian.Core.Network;
using Gordian.Core.Network.Packets;
using Gordian.Core.Resources;
using Gordian.Core.Resources.Events;
using Gordian.Core.Resources.Tables;
using Gordian.Core.Ui;
using Xunit;
using Xunit.Abstractions;

namespace Gordian.Core.Tests.Events
{
    /// <summary>
    /// The zone-in cutscenes LandSandBoat sends in S2C 0x00A into Northern San d'Oria (issue #122), from the installed
    /// game's event DAT: Seekers of Adoulin 1-1 (event 878, the one the maintainer's character got on 2026-09-28) and
    /// Rhapsodies of Vana'diel 1-1 (event 30035). Neither is in the player's block: several NPC blocks carry the id,
    /// one of them directs the scene and the rest only toggle render flags, and retail runs them all at once.
    /// Skipped without the game install.
    /// </summary>
    public class ZoneInCutsceneTests
    {
        private const string GameDirectory = @"G:\Program Files (x86)\PlayOnline\SquareEnix\FINAL FANTASY XI";
        private const int NorthernSandoria = 231;
        private const uint Anilla = 0x010E7066;
        private static readonly TimeSpan Frame = TimeSpan.FromSeconds(1.0 / 60);
        private readonly ITestOutputHelper _output;

        public ZoneInCutsceneTests(ITestOutputHelper output) => _output = output;

        private static ResourceManager? OpenGame()
        {
            if (!Directory.Exists(GameDirectory)) return null;
            var rm = new ResourceManager(GameDirectory);
            rm.InitializeFileTable();
            return rm;
        }

        /// <summary>Runs every block carrying the event on one shared work zone, answering the first query with option 1.</summary>
        private (List<(uint Actor, RecordingHost Host)> Runs, uint End) RunAllBlocks(ResourceManager rm, ushort eventId)
        {
            var script = ZoneEventScript.Parse(rm.LoadDatBytesByFileId(ZoneEventScript.GetFileId(NorthernSandoria))!)!;
            var dialog = ZoneDialogTable.Parse(rm.LoadDatBytesByFileId(ZoneDialogTable.GetFileId(NorthernSandoria))!)!;
            var zone = new EventWorkZone();
            var runs = new List<(uint Actor, RecordingHost Host, EventVm Vm)>();
            foreach (var block in script.Blocks.Where(b => b.IndexOf(eventId) >= 0))
            {
                var host = new RecordingHost();
                runs.Add((block.ActorId, host, new EventVm(block, eventId, zone, host, block.ActorId, (ushort)(block.ActorId & 0x3FF))));
            }
            for (int tick = 0; tick < 100000 && runs.Any(r => !r.Vm.IsFinished); tick++)
            {
                foreach (var (_, host, vm) in runs)
                {
                    if (vm.IsFinished) continue;
                    vm.Tick(Frame);
                    if (vm.IsWaitingForConfirm) vm.Confirm();
                    host.ReceivePending = false;
                    if (host.Queries.Count > 0 && host.QueryResult == 0) host.QueryResult = 1;
                }
            }
            foreach (var (actor, host, vm) in runs)
            {
                _output.WriteLine($"0x{actor:X8}: {host.Printed.Count} messages, {host.Queries.Count} queries, updates {string.Join(",", host.Updates)}");
                foreach (var q in host.Queries) _output.WriteLine($"    query {q.Message}: {dialog.GetPlainText(q.Message)}");
                foreach (var p in host.Printed) _output.WriteLine($"    msg {p.Message}: {dialog.GetPlainText(p.Message)}");
            }
            Assert.All(runs, r => Assert.True(r.Vm.IsFinished));
            return (runs.Select(r => (r.Actor, r.Host)).ToList(), runs[0].Vm.EndParameter);
        }

        [Fact]
        public void SeekersZoneInCutscene_AnillaDirectsIt()
        {
            var rm = OpenGame();
            if (rm == null) return;
            var (runs, end) = RunAllBlocks(rm, 878);
            Assert.Equal(8, runs.Count);
            var director = Assert.Single(runs, r => r.Host.Printed.Count > 0 || r.Host.Queries.Count > 0);
            Assert.Equal(Anilla, director.Actor);
            Assert.Equal(19399, Assert.Single(director.Host.Queries).Message); // "Start Seekers of Adoulin?"
            Assert.Equal(13, director.Host.Printed.Count);
            Assert.Equal(1u, end);
        }

        [Fact]
        public void RhapsodiesZoneInCutscene_AsksToStartThenEnds()
        {
            var rm = OpenGame();
            if (rm == null) return;
            var (runs, end) = RunAllBlocks(rm, 30035);
            var director = Assert.Single(runs, r => r.Host.Printed.Count > 0 || r.Host.Queries.Count > 0);
            Assert.Equal(0x010E71B2u, director.Actor);
            Assert.Equal(19402, Assert.Single(director.Host.Queries).Message); // "Start Rhapsodies of Vana'diel?"
            Assert.Equal(new uint[] { 0 }, director.Host.Updates);
            Assert.Equal(1u, end);
        }

        /// <summary>
        /// The session path: S2C 0x00A with event 878 starts it, the dialog controller runs the carrying blocks (Anilla's
        /// query is cancelled here, as no UI library is loaded) and ends the event with C2S 0x05B.
        /// </summary>
        [Fact]
        public void ZoneInEvent_FromLoginPacket_RunsTheDirectorAndEndsTheEvent()
        {
            var rm = OpenGame();
            if (rm == null) return;
            var sent = new List<byte[]>();
            var parser = new PacketParser(new SessionProfile(), (chunk, _) =>
            {
                sent.Add(chunk.ToArray());
                return Task.CompletedTask;
            });
            var chat = new StockUiChat();
            var controller = new EventDialogController();
            var previousLoader = EventDialogController.DatLoader;
            EventDialogController.DatLoader = rm.LoadDatBytesByFileId;
            try
            {
                controller.Attach(parser.Progression, parser.ProgressionModule, parser.World, parser.LocalPlayer, chat,
                    new StockUiMenuController(), () => "Cybin");

                byte[] login = new byte[144];
                BinaryPrimitives.WriteUInt32LittleEndian(login.AsSpan(0, 4), 0x00012345);
                BinaryPrimitives.WriteUInt16LittleEndian(login.AsSpan(4, 2), 0x0400);
                BinaryPrimitives.WriteUInt16LittleEndian(login.AsSpan(44, 2), NorthernSandoria);
                BinaryPrimitives.WriteUInt16LittleEndian(login.AsSpan(60, 2), NorthernSandoria);
                BinaryPrimitives.WriteUInt16LittleEndian(login.AsSpan(94, 2), NorthernSandoria);
                BinaryPrimitives.WriteUInt16LittleEndian(login.AsSpan(96, 2), 878);
                parser.Dispatcher.Dispatch(new PacketHeader(0x00A, 1, (ushort)login.Length), login);

                // None of the eight NPCs is in this world: the start waits for them (C2S 0x016), then runs anyway.
                int waited = 0;
                for (; waited < 200 && !controller.IsActive; waited++) controller.Tick(Frame);
                Assert.True(controller.IsActive);
                Assert.InRange(waited, 60 * EventDialogController.EntityWaitSeconds - 2, 60 * EventDialogController.EntityWaitSeconds + 2);
                for (int i = 0; i < 100000 && controller.IsActive; i++)
                {
                    controller.Tick(Frame);
                    parser.Progression.AcknowledgeEventUpdate(); // the server answers each update with 0x052 mode 1
                }
                Assert.False(controller.IsActive);
            }
            finally
            {
                EventDialogController.DatLoader = previousLoader;
            }

            // The end: 0x05B mode 0 for the player's event 878 (header 4 bytes, UniqueNo +4, EndPara +8, EventPara +18).
            var end = Assert.Single(sent, p => (p[0] | (p[1] & 1) << 8) == 0x05B && BinaryPrimitives.ReadUInt16LittleEndian(p.AsSpan(14, 2)) == 0);
            Assert.Equal(0x00012345u, BinaryPrimitives.ReadUInt32LittleEndian(end.AsSpan(4, 4)));
            Assert.Equal(EventVm.CancelledEndParameter, BinaryPrimitives.ReadUInt32LittleEndian(end.AsSpan(8, 4)));
            Assert.Equal(878, BinaryPrimitives.ReadUInt16LittleEndian(end.AsSpan(18, 2)));
            Assert.Null(parser.Progression.ActiveEvent);
        }
    }
}
