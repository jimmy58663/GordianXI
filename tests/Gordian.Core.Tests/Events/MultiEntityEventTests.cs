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
        private Run RunScene(ResourceManager rm, int zoneId, ushort eventId, Action<RecordingHost>? setup = null)
        {
            var script = ZoneEventScript.Parse(rm.LoadDatBytesByFileId(ZoneEventScript.GetFileId(zoneId))!)!;
            var dialog = ZoneDialogTable.Parse(rm.LoadDatBytesByFileId(ZoneDialogTable.GetFileId(zoneId))!)!;
            var host = new RecordingHost();
            setup?.Invoke(host);
            // The schedulers run for the lengths the retail scene and motion DATs give (#165).
            var scenes = new Dictionary<int, EventSceneResource?>();
            var banks = new Dictionary<int, Gordian.Core.Animation.EventMotionBank?>();
            host.RoutineSource = (fileId, routine) =>
            {
                if (!scenes.TryGetValue(fileId, out var resource))
                {
                    var bytes = rm.LoadDatBytesByFileId(fileId);
                    scenes[fileId] = resource = bytes == null ? null : EventSceneResource.Parse(bytes);
                }
                return resource != null && resource.TryGetRoutine(routine, out var r) ? r.TotalFrames : 0;
            };
            host.MotionSource = (source, resource, routine) =>
            {
                if (source != EventMotionSource.Bank) return 0;
                if (!banks.TryGetValue(resource, out var bank))
                {
                    var bytes = rm.LoadDatBytesByFileId(resource);
                    banks[resource] = bank = bytes == null ? null : Gordian.Core.Animation.EventMotionBank.Parse(bytes, resource);
                }
                return bank?.GetRoutineFrames(routine) ?? 0;
            };
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
            int found = host.SceneTasks.Count(t => host.RoutineSource!(t.FileId, t.Routine) > 0);
            int gestures = host.Motions.Count(m => host.MotionSource!(m.Source, m.Resource, m.Routine) > 0);
            _output.WriteLine($"  scene tasks {host.SceneTasks.Count} ({found} found, files {string.Join(",", host.SceneTasks.Select(t => t.FileId).Distinct())}), " +
                $"motions {host.Motions.Count} ({gestures} from banks), camera holds {string.Join(",", host.CameraHolds)}");
            Assert.True(scene.IsFinished);
            return run;
        }

        /// <summary>
        /// Port Jeuno (246), event 324 (Abyssea "A Journey Begins", LandSandBoat <c>A_Journey_Begins.lua</c>, on zoning in):
        /// the director 0x010F6090 holds the player's head on the look axis (0, 1024) with 0x79 sub 2 and clears it with 0x7B
        /// 121 frames later, the shot from behind the player before the pink flash in the maintainer's retail recording
        /// (2026-10-02, #188); the scene sets no head turn speed.
        /// </summary>
        [Fact]
        public void PortJeunoAbysseaIntro_HoldsThePlayersHeadOnALookAxis()
        {
            var rm = OpenGame();
            if (rm == null) return;
            var run = RunScene(rm, 246, 324);
            Assert.Equal((PlayerId, 0, 1024), Assert.Single(run.Host.LookAxes));
            Assert.Empty(run.Host.HeadTurnSpeeds);
            Assert.Contains(run.Host.Looks, l => l.Id == PlayerId && l.Target == uint.MaxValue);
        }

        /// <summary>
        /// Port Jeuno 324 (#209): the director ends the player's look at the flash marker 0x010F608F with 0x7B, and the
        /// player's own script then plays <c>atp0</c> from the race package skeleton slot · 10 + 70 (Tarutaru: race 5,
        /// slot 4, package 110), which is in the race sets table (file 61281), not the first two. It is the stand looking
        /// up that retail holds through the front shot under the flash (about 1:17-1:21 in the maintainer's recording,
        /// 2026-10-02).
        /// </summary>
        [Fact]
        public void PortJeunoAbysseaIntro_ThePlayerLooksUpFromItsRacePackage()
        {
            var rm = OpenGame();
            if (rm == null) return;
            var run = RunScene(rm, 246, 324, host => host.EntityValues[7] = 5);
            var looks = run.Host.Looks.Where(l => l.Id == PlayerId).Select(l => l.Target).ToList();
            int marker = looks.IndexOf(0x010F608F);
            Assert.True(marker >= 0);
            Assert.Equal(uint.MaxValue, looks[marker + 1]);
            var atp = Assert.Single(run.Host.Motions, m => m.Id == PlayerId && m.Routine == "atp0");
            Assert.Equal((EventMotionSource.Package, 110), (atp.Source, atp.Resource));
            Assert.Equal(new[] { 61281 }, Gordian.Core.Animation.EventMotionBank.PackageFiles(110));
        }

        /// <summary>
        /// Name plates (#191): Port Jeuno 324 hides the name of every NPC it places (each runs <c>92 01</c> on itself)
        /// and of Joachim (the director's <c>92 01</c>), never the player's, as the maintainer's retail recording shows
        /// only the player's plate; the Southern San d'Oria intro, whose retail recording shows every plate, hides none.
        /// </summary>
        [Fact]
        public void PortJeunoAbysseaIntro_HidesEveryNameButThePlayers()
        {
            var rm = OpenGame();
            if (rm == null) return;
            var run = RunScene(rm, 246, 324);
            var hidden = run.Host.HidesName.Where(h => h.Hide).Select(h => h.Id).ToHashSet();
            Assert.Contains(0x010F6093u, hidden);
            Assert.DoesNotContain(PlayerId, hidden);
            Assert.All(run.Scene.Actors.Where(a => a.CarriesEvent && a.EntityServerId is not PlayerId and not 0x010F6090 and not 0x010F608E and not 0x010F608F and not 0x010F6093),
                a => Assert.Contains(a.EntityServerId, hidden));
            Assert.DoesNotContain(run.Host.HidesName, h => !h.Hide);

            Assert.Empty(RunScene(rm, 230, 503).Host.HidesName);
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

        /// <summary>
        /// The Southern San d'Oria intro (230, event 503) as the maintainer's retail recording showed it (2026-09-30): it
        /// comes with the zone, its NPCs arrive seconds later (the start waits for them, and one arriving after the start
        /// still joins the event with the place its script gave it), the narration shows on the screen in the event message
        /// mode (0x67) instead of the log and closes by itself after its 0x7F 0x34 time (9 s).
        /// </summary>
        [Fact]
        public void SouthernSandoriaIntro_FromLogin_WaitsForItsNpcs_AndShowsTheNarrationOnScreen()
        {
            var rm = OpenGame();
            if (rm == null) return;
            var parser = new Gordian.Core.Network.PacketParser(new Gordian.Core.Config.SessionProfile(), (_, _) => System.Threading.Tasks.Task.CompletedTask);
            var controller = new EventDialogController(rm.LoadDatBytesByFileId);
            var chat = new Gordian.Core.Ui.StockUiChat();
            const uint Ceraule = 0x010E6001, Knight = 0x010E6068, TempleKnight = 0x010E60D7;
            controller.Attach(parser.Progression, parser.ProgressionModule, parser.World, parser.LocalPlayer, chat,
                new Gordian.Core.Ui.StockUiMenuController(), () => "Cybin");
            byte[] login = new byte[144];
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(login.AsSpan(0, 4), PlayerId);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(login.AsSpan(4, 2), 0x0400);
            foreach (int at in new[] { 44, 60, 94 }) System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(login.AsSpan(at, 2), 230);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(login.AsSpan(96, 2), 503);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(login.AsSpan(98, 2), 0x83);
            parser.Dispatcher.Dispatch(new Gordian.Core.Network.Packets.PacketHeader(0x00A, 1, (ushort)login.Length), login);

            // Nothing for 5 s: the zone-in start keeps waiting.
            for (int i = 0; i < 300; i++) controller.Tick(Frame);
            Assert.False(controller.IsActive);
            // Ceraule arrives (hidden by the server, as LandSandBoat sends cutscene NPCs); 1.5 s later the event runs.
            parser.World.UpsertEntity(new Gordian.Core.World.WorldEntity(Ceraule, 1, Gordian.Core.World.EntityType.Npc) { IsHidden = true });
            // A cutscene-only knight of the 1:50 scene: placed at the start, out of sight until the script shows it.
            parser.World.UpsertEntity(new Gordian.Core.World.WorldEntity(TempleKnight, 0xD7, Gordian.Core.World.EntityType.Npc) { IsHidden = true });
            int waited = 0;
            for (; waited < 600 && !controller.IsActive; waited++) controller.Tick(Frame);
            Assert.True(controller.IsActive);
            Assert.InRange(waited, 60 * EventDialogController.EntityWaitSeconds - 2, 60 * EventDialogController.EntityWaitSeconds + 2);

            // A knight arrives after the start: it joins the event and takes the place its script gave it.
            parser.World.UpsertEntity(new Gordian.Core.World.WorldEntity(Knight, 104, Gordian.Core.World.EntityType.Npc) { IsHidden = true });
            bool narrationShown = false, knightJoined = false, ceraulePosed = false;
            int narrationTicks = 0, logLinesInMode = 0, maxNarrationRun = 0, run = 0, narrationPages = 0, pagesBeforeKnight = -1;
            bool wasShowing = false;
            for (int i = 0; i < 200_000 && controller.IsActive; i++)
            {
                int before = chat.Log.Count(0);
                controller.Tick(Frame);
                parser.Progression.AcknowledgeEventUpdate();
                if (controller.IsCutsceneHud) logLinesInMode += chat.Log.Count(0) - before;
                var text = controller.EventText;
                if (text != null && !wasShowing) narrationPages++;
                wasShowing = text != null;
                parser.World.TryGetByServerId(TempleKnight, out var temple);
                if (temple!.IsDrawn && pagesBeforeKnight < 0) pagesBeforeKnight = narrationPages;
                if (text != null)
                {
                    narrationShown = true;
                    narrationTicks++;
                    run++;
                    maxNarrationRun = Math.Max(maxNarrationRun, run);
                    Assert.Equal((80, 340), (text.X, text.Y)); // the lines' own 0x02 position
                    Assert.DoesNotContain(text.Lines, l => l.Contains('<'));
                }
                else
                {
                    run = 0;
                    // Lines outside the narration (after 0x68) wait for Confirm, as in retail.
                    if (!controller.IsCutsceneHud) controller.Confirm();
                }
                parser.World.TryGetByServerId(Knight, out var knight);
                knightJoined |= knight!.IsInEvent && knight.EventPose != null && knight.IsDrawn;
                parser.World.TryGetByServerId(Ceraule, out var ceraule);
                ceraulePosed |= ceraule!.EventPose != null;
            }
            Assert.False(controller.IsActive);
            Assert.True(narrationShown);
            Assert.Equal(0, logLinesInMode);
            Assert.InRange(maxNarrationRun, 9 * 60 - 3, 9 * 60 + 3); // 0x7F 0x34 09
            Assert.True(knightJoined);
            Assert.True(pagesBeforeKnight >= 7, $"the 1:50 knight showed after {pagesBeforeKnight} narration pages"); // after all of them
            Assert.True(ceraulePosed);
            Assert.True(chat.Log.Count(0) > 5); // the talk after 0x68 goes to the log
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
        /// Every new-character intro (#86; zone and event from LandSandBoat's New_Character_Cutscenes.lua) runs to its
        /// end on all its entities, with its lines printed. Writes the opcodes still stepped over, by use count.
        /// </summary>
        [Theory]
        [InlineData(236, 1)]   // Port Bastok
        [InlineData(234, 1)]   // Bastok Mines
        [InlineData(235, 0)]   // Bastok Markets (then event 7)
        [InlineData(235, 7)]
        [InlineData(231, 535)] // Northern San d'Oria
        [InlineData(230, 503)] // Southern San d'Oria
        [InlineData(232, 500)] // Port San d'Oria
        [InlineData(238, 531)] // Windurst Waters
        [InlineData(241, 367)] // Windurst Woods
        [InlineData(240, 305)] // Port Windurst
        public void NewCharacterIntro_RunsToItsEnd(int zoneId, int eventId)
        {
            var rm = OpenGame();
            if (rm == null) return;
            var run = RunScene(rm, zoneId, (ushort)eventId);
            Assert.True(run.Scene.IsFinished);
            Assert.NotEmpty(run.Host.Printed);
            var skipped = run.Host.Skipped.GroupBy(o => o).OrderByDescending(g => g.Count()).Select(g => $"{g.Key:X2}x{g.Count()}");
            _output.WriteLine($"skipped opcodes: {string.Join(" ", skipped)}");
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

        /// <summary>
        /// The session path of the Windurst Woods intro (#86): S2C 0x00A with event 367 and LandSandBoat's flags
        /// RESET_CAMERA | NO_PCS | NO_NPCS | OPENING_MODE. Entities outside the event are hidden, the scripts hide the
        /// four NPCs their blocks flag (0x22 01) and walk others (0x1F), and everything is given back when the event ends.
        /// </summary>
        [Fact]
        public void WindurstWoodsIntro_FromLogin_StagesActorsAndRestoresThem()
        {
            var rm = OpenGame();
            if (rm == null) return;
            var parser = new Gordian.Core.Network.PacketParser(new Gordian.Core.Config.SessionProfile(), (_, _) => System.Threading.Tasks.Task.CompletedTask);
            var controller = new EventDialogController(rm.LoadDatBytesByFileId);
            const uint OtherPc = 0x00054321, Bystander = 0x010F1050, Hidden = 0x010F1081, Nanaa = 0x010F100F, Director = 0x010F100B;
            controller.Attach(parser.Progression, parser.ProgressionModule, parser.World, parser.LocalPlayer, new Gordian.Core.Ui.StockUiChat(),
                new Gordian.Core.Ui.StockUiMenuController(), () => "Cybin");
            byte[] login = new byte[144];
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(login.AsSpan(0, 4), PlayerId);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(login.AsSpan(4, 2), 0x0400);
            foreach (int at in new[] { 44, 60, 94 }) System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(login.AsSpan(at, 2), 241);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(login.AsSpan(96, 2), 367);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(login.AsSpan(98, 2),
                (ushort)(CutsceneFlags.ResetCamera | CutsceneFlags.NoPcs | CutsceneFlags.NoNpcs | CutsceneFlags.OpeningMode));
            parser.Dispatcher.Dispatch(new Gordian.Core.Network.Packets.PacketHeader(0x00A, 1, (ushort)login.Length), login);
            foreach (var (id, type) in new[] { (OtherPc, Gordian.Core.World.EntityType.Player), (Bystander, Gordian.Core.World.EntityType.Npc),
                (Hidden, Gordian.Core.World.EntityType.Npc), (Nanaa, Gordian.Core.World.EntityType.Npc), (Director, Gordian.Core.World.EntityType.Npc) })
            {
                parser.World.UpsertEntity(new Gordian.Core.World.WorldEntity(id, (ushort)(id & 0x3FF), type) { Position = new System.Numerics.Vector3(10, 0, 10) });
            }

            bool bystanderHidden = false, pcHidden = false, flaggedHidden = false, nanaaPosed = false, nanaaWalked = false, cutsceneHud = false, clockLocked = false;
            bool wallsShown = false, backHome = false;
            int loadTicks = 0, eventTicks = 0, wallsTicks = 0;
            parser.World.UpdateWeather(1);
            parser.World.DisplayedZoneId = 241; // a viewport draws the session's zone
            for (int i = 0; i < 200_000 && (i < 60 * (int)EventDialogController.ZoneInEntityWaitSeconds + 120 || controller.IsActive); i++)
            {
                // The viewport takes half a second to load a zone the event opens (0x34 / 0x35, #175).
                if (parser.World.DisplayedZoneId != parser.World.SceneZoneId && ++loadTicks >= 30)
                {
                    parser.World.DisplayedZoneId = parser.World.SceneZoneId;
                    loadTicks = 0;
                }
                controller.Tick(Frame);
                controller.Confirm();
                parser.Progression.AcknowledgeEventUpdate();
                if (!controller.IsActive) continue;
                parser.World.TryGetByServerId(Bystander, out var bystander);
                parser.World.TryGetByServerId(OtherPc, out var pc);
                parser.World.TryGetByServerId(Hidden, out var hidden);
                parser.World.TryGetByServerId(Nanaa, out var nanaa);
                bystanderHidden |= bystander!.IsEventHidden;
                pcHidden |= pc!.IsEventHidden;
                flaggedHidden |= hidden!.IsEventHidden;
                nanaaPosed |= nanaa!.EventPose != null;
                nanaaWalked |= nanaa.EventPose is { Speed: > 0 };
                cutsceneHud |= controller.IsCutsceneHud;
                clockLocked |= parser.World.IsTimeOfDayLocked;
                eventTicks++;
                if (parser.World.EventZoneId == 239) wallsTicks++;
                wallsShown |= parser.World.EventZoneId == 239;
                backHome |= wallsShown && parser.World.EventZoneId == 0;
            }
            Assert.False(controller.IsActive);
            Assert.True(bystanderHidden);
            Assert.True(pcHidden);
            Assert.True(flaggedHidden);
            Assert.True(nanaaPosed);
            Assert.True(nanaaWalked);
            Assert.True(cutsceneHud); // 0x67
            Assert.True(clockLocked); // 0x77
            Assert.False(controller.IsCutsceneHud);
            Assert.False(parser.World.IsTimeOfDayLocked);
            Assert.Equal(1, parser.World.WeatherNumber); // the zone's weather is back
            Assert.True(wallsShown); // 0x34: Windurst Walls for the opening scene
            Assert.True(backHome); // 0x35: Windurst Woods again
            Assert.InRange(wallsTicks, 60 * 60, 100 * 60); // seven narration lines that close themselves (0x7F 0x34)
            Assert.Equal(0, parser.World.EventZoneId);
            foreach (var entity in parser.World.GetAllEntities())
            {
                Assert.False(entity.IsEventHidden);
                Assert.Null(entity.EventPose);
                Assert.Null(entity.EventLook);
            }
        }

        /// <summary>
        /// The cutscene-only NPCs of an intro: LandSandBoat keeps them at status Disappear, so their updates carry the
        /// HideFlag. They are drawn while they take part in the event and hidden again once it ends (the maintainer's
        /// in-game test of #85, 2026-09-30: they stayed visible after the Windurst Woods intro). An NPC outside the event
        /// stays hidden throughout.
        /// </summary>
        [Fact]
        public void WindurstWoodsIntro_CutsceneNpcsAreDrawnOnlyDuringTheEvent()
        {
            var rm = OpenGame();
            if (rm == null) return;
            var parser = new Gordian.Core.Network.PacketParser(new Gordian.Core.Config.SessionProfile(), (_, _) => System.Threading.Tasks.Task.CompletedTask);
            var controller = new EventDialogController(rm.LoadDatBytesByFileId);
            const uint Nanaa = 0x010F100F, Outsider = 0x010F1050;
            controller.Attach(parser.Progression, parser.ProgressionModule, parser.World, parser.LocalPlayer, new Gordian.Core.Ui.StockUiChat(),
                new Gordian.Core.Ui.StockUiMenuController(), () => "Cybin");
            byte[] login = new byte[144];
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(login.AsSpan(0, 4), PlayerId);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(login.AsSpan(4, 2), 0x0400);
            foreach (int at in new[] { 44, 60, 94 }) System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(login.AsSpan(at, 2), 241);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(login.AsSpan(96, 2), 367);
            parser.Dispatcher.Dispatch(new Gordian.Core.Network.Packets.PacketHeader(0x00A, 1, (ushort)login.Length), login);
            foreach (uint id in new[] { Nanaa, Outsider })
            {
                parser.World.UpsertEntity(new Gordian.Core.World.WorldEntity(id, (ushort)(id & 0x3FF), Gordian.Core.World.EntityType.Npc) { IsHidden = true });
            }
            parser.World.TryGetByServerId(Nanaa, out var nanaa);
            parser.World.TryGetByServerId(Outsider, out var outsider);
            Assert.False(nanaa!.IsDrawn);

            bool drawnDuring = false, outsiderDrawn = false;
            for (int i = 0; i < 200_000 && (i < 60 * (int)EventDialogController.ZoneInEntityWaitSeconds + 120 || controller.IsActive); i++)
            {
                controller.Tick(Frame);
                controller.Confirm();
                parser.Progression.AcknowledgeEventUpdate();
                if (!controller.IsActive) continue;
                drawnDuring |= nanaa.IsDrawn;
                outsiderDrawn |= outsider!.IsDrawn;
            }
            Assert.False(controller.IsActive);
            Assert.True(drawnDuring);
            Assert.False(outsiderDrawn);
            Assert.False(nanaa.IsDrawn); // the server still hides her: gone once the event ends
            Assert.False(nanaa.IsInEvent);
        }
    }
}
