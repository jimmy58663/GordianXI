// tests/Gordian.Core.Tests/Events/MultiEntityEventTests.cs
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
    /// Events that several entities play (#85): the new-character intro cutscenes (#86) and NPC talks where a second
    /// NPC answers, run from the installed game's event DATs the way the dialog
    /// controller runs an event: one VM per entity whose block carries the event id (or the catch-all), all in one
    /// <see cref="EventScene"/>. Event ids from LandSandBoat <c>scripts/quests/hiddenQuests/New_Character_Cutscenes.lua</c>.
    /// Skipped without the game install.
    /// </summary>
    public class MultiEntityEventTests
    {
        private const string GameDirectory = @"G:\Program Files (x86)\PlayOnline\SquareEnix\FINAL FANTASY XI";
        private const uint PlayerId = 0x00012345;
        private static readonly TimeSpan Frame = TimeSpan.FromSeconds(1.0 / 60);
        private readonly ITestOutputHelper _output;

        public MultiEntityEventTests(ITestOutputHelper output) => _output = output;

        private static ResourceManager? OpenGame()
        {
            if (!Directory.Exists(GameDirectory)) return null;
            var rm = new ResourceManager(GameDirectory);
            rm.InitializeFileTable();
            return rm;
        }

        private sealed class Run
        {
            public required EventScene Scene { get; init; }
            public required RecordingHost Host { get; init; }
            public required ZoneDialogTable Dialog { get; init; }
            public Dictionary<uint, Dictionary<byte, int>> Opcodes { get; } = new();
            public int Ticks { get; set; }
        }

        /// <summary>Runs an event to its end on every carrying entity, confirming each line and answering queries with option 1.</summary>
        private Run RunScene(ResourceManager rm, int zoneId, ushort eventId)
        {
            var script = ZoneEventScript.Parse(rm.LoadDatBytesByFileId(ZoneEventScript.GetFileId(zoneId))!)!;
            var dialog = ZoneDialogTable.Parse(rm.LoadDatBytesByFileId(ZoneDialogTable.GetFileId(zoneId))!)!;
            var host = new RecordingHost();
            var scene = new EventScene(new EventWorkZone()) { PlayerServerId = PlayerId };
            var run = new Run { Scene = scene, Host = host, Dialog = dialog };
            foreach (var block in script.Blocks)
            {
                if (block.ActorId == EventBlock.ZoneActor) continue;
                if (block.IndexOf(eventId) < 0 && block.IndexOf(EventBlock.AnyEventId) < 0) continue;
                uint id = block.ActorId == EventBlock.PlayerActor ? PlayerId : block.ActorId;
                var vm = new EventVm(block, eventId, scene, host, id, (ushort)(id & 0x3FF));
                var counts = run.Opcodes[id] = new Dictionary<byte, int>();
                vm.Trace = (_, op) => counts[op] = counts.GetValueOrDefault(op) + 1;
            }
            for (; run.Ticks < 200_000 && !scene.IsFinished; run.Ticks++)
            {
                scene.Tick(Frame);
                if (scene.IsWaitingForConfirm) scene.Confirm();
                host.ReceivePending = false;
                if (host.Queries.Count > 0 && host.QueryResult == 0) host.QueryResult = 1;
            }
            _output.WriteLine($"zone {zoneId} event {eventId}: {scene.Actors.Count} entities, {run.Ticks} ticks ({run.Ticks / 60.0:F1} s), ended={scene.IsEnded} end=0x{scene.EndParameter:X}, updates {string.Join(",", host.Updates)}");
            foreach (var vm in scene.Actors)
            {
                var ops = run.Opcodes[vm.EntityServerId];
                int requests = new byte[] { 0x27, 0x28, 0x29, 0x2A }.Sum(o => ops.GetValueOrDefault(o));
                int lines = host.Printed.Count(p => p.Id == vm.EntityServerId);
                _output.WriteLine($"  0x{vm.EntityServerId:X8}{(vm.CarriesEvent ? "" : " (catch-all)")}: {ops.Values.Sum()} ops, {requests} request ops, finished={vm.IsFinished}");
            }
            foreach (var p in host.Printed) _output.WriteLine($"    0x{p.Id:X8} msg {p.Message}: {dialog.GetPlainText(p.Message)}");
            Assert.True(scene.IsFinished);
            return run;
        }

        private int RequestOps(Run run, uint actor) =>
            new byte[] { 0x27, 0x28, 0x29, 0x2A }.Sum(o => run.Opcodes[actor].GetValueOrDefault(o));

        /// <summary>
        /// Bastok Mines (234), event 1: the player's block holds 8 bytes; the scene is NPC 0x010EA001's 1,837-byte part.
        /// Running only the player's part (before #85) ended the event at once.
        /// </summary>
        [Fact]
        public void BastokMinesIntro_RunsTheNpcScene()
        {
            var rm = OpenGame();
            if (rm == null) return;
            var run = RunScene(rm, 234, 1);
            Assert.True(run.Scene.Actors.Count > 20);
            // The director narrates (no speaker) and has Deidogg (0x010EA003) and Gumbah (0x010EA004) speak through requests.
            Assert.True(RequestOps(run, 0x010EA001) > 0);
            Assert.Contains(run.Host.Printed, p => p.Message == 10480 && p.Speaker == EventSpeaker.None);
            Assert.Contains(run.Host.Printed, p => p.Id == 0x010EA003);
            Assert.Contains(run.Host.Printed, p => p.Id == 0x010EA004);
            Assert.True(run.Scene.IsEnded); // by 0x21
            Assert.True(run.Ticks > 60); // the scene's waits take time
        }

        /// <summary>Windurst Woods (241), event 367: the player's block has a bare end; NPC 0x010F100B's ~2 KB part is the scene.</summary>
        [Fact]
        public void WindurstWoodsIntro_RunsTheNpcScene()
        {
            var rm = OpenGame();
            if (rm == null) return;
            var run = RunScene(rm, 241, 367);
            // The director has Nanaa Mihgo (0x010F100F) and the Star Onion Brigade (0x010F100C, 0x010F100D) speak.
            Assert.True(RequestOps(run, 0x010F100B) > 0);
            Assert.Contains(run.Host.Printed, p => p.Id == 0x010F100F);
            Assert.Contains(run.Host.Printed, p => p.Id == 0x010F100C);
            Assert.Contains(run.Host.Printed, p => p.Id == 0x010F100D);
            Assert.True(run.Scene.IsEnded);
        }

        /// <summary>Bastok Markets (235), event 0: a 12-byte player part, the rest in NPC 0x010EB001's block.</summary>
        [Fact]
        public void BastokMarketsIntro_RunsTheNpcScene()
        {
            var rm = OpenGame();
            if (rm == null) return;
            var run = RunScene(rm, 235, 0);
            Assert.True(run.Opcodes[0x010EB001].Values.Sum() > 50);
        }

        /// <summary>Port Bastok (236), event 1: the scene is the player's block; it directs NPCs through companion requests.</summary>
        [Fact]
        public void PortBastokIntro_DirectsNpcsThroughRequests()
        {
            var rm = OpenGame();
            if (rm == null) return;
            var run = RunScene(rm, 236, 1);
            Assert.True(RequestOps(run, PlayerId) > 0);
            Assert.NotEmpty(run.Host.Printed);
        }

        /// <summary>
        /// Southern San d'Oria (230), event 663 (Femitte's goldsmithing task): Femitte calls "Rouva?" and her attendant
        /// Rouva (0x010E60C4) answers, each line from the speaker's own block, run by companion requests from the player's block, which directs the scene.
        /// Before #85 only Femitte's block ran, so Rouva stayed silent.
        /// </summary>
        [Fact]
        public void TwoNpcTalk_SecondNpcAnswersThroughARequest()
        {
            var rm = OpenGame();
            if (rm == null) return;
            var run = RunScene(rm, 230, 663);
            Assert.Equal(new uint[] { PlayerId, 0x010E60C3, 0x010E60C4 }, run.Scene.Actors.Select(a => a.EntityServerId).OrderBy(i => i).ToArray());
            Assert.True(RequestOps(run, PlayerId) > 0); // the player's block directs: it has both NPCs speak
            int call = run.Host.Printed.FindIndex(p => p.Message == 8886 && p.Id == 0x010E60C3); // "Rouva?"
            int answer = run.Host.Printed.FindIndex(p => p.Message == 8887 && p.Id == 0x010E60C4); // "Right away, my lady."
            Assert.True(call >= 0 && answer == call + 1);
            Assert.True(run.Scene.IsEnded);
        }
    }
}
