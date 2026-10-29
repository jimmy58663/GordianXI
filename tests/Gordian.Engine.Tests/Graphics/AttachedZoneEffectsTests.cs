// tests/Gordian.Engine.Tests/Graphics/AttachedZoneEffectsTests.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Gordian.Engine.Graphics;
using Gordian.Core.Resources;
using Gordian.Core.Resources.Events;
using Gordian.Core.Resources.Graphics;
using Gordian.Core.Resources.Models;
using Gordian.Engine.Tests.Resources;
using Gordian.Core.World;
using Xunit;

namespace Gordian.Engine.Tests.Graphics
{
    /// <summary>
    /// Zone generators attached to an actor (#232), the zone-placed TargetActorSourceFacing weather birds (#121) and
    /// doors' own effect routines (#227). The retail checks are skipped without the game install.
    /// </summary>
    public class AttachedZoneEffectsTests
    {
        private const string GameDirectory = @"G:\Program Files (x86)\PlayOnline\SquareEnix\FINAL FANTASY XI";

        private static ZoneEmitterTemplate Generator(string id, ushort life)
        {
            var def = new ParticleGeneratorDefinition
            {
                DatId = id,
                FramesPerEmission = 1000,
                Setup = new StandardParticleSetup { MaxLifeSpan = life, LinkedDataType = ParticleLinkedDataType.StaticMesh }
            };
            def.Initializers.Add(new ParticleOpcode(0x01, 0, Array.Empty<uint>()));
            return new ZoneEmitterTemplate(def, new Dictionary<ushort, KeyFrameCurve>());
        }

        private static SceneRoutine Routine(string name, int totalFrames, params byte[][] commands) =>
            SceneRoutine.Decode(EffectRoutineBuilder.BuildPayload(totalFrames, commands.ToList()), name)!;

        private static byte[] Cmd(byte op, ushort delay, string reference) => EffectRoutineBuilder.Command(op, delay, 0, reference);

        [Theory]
        [InlineData(ParticleAttachType.None, 0u)]
        [InlineData(ParticleAttachType.TargetActorSourceFacing, 0u)]
        [InlineData(ParticleAttachType.Sun, 0u)]
        [InlineData(ParticleAttachType.TargetActor, 2u)]
        [InlineData(ParticleAttachType.TargetToSourceBasis, 2u)]
        [InlineData(ParticleAttachType.SourceActor, 1u)]
        [InlineData(ParticleAttachType.SourceActorTargetFacing, 1u)]
        [InlineData(ParticleAttachType.SourceActorWeapon, 1u)]
        [InlineData(ParticleAttachType.ZoneActor0xA, 1u)]
        [InlineData(ParticleAttachType.ZoneActor0xC, 1u)]
        public void ActorFor_PicksTheSideOfTheAttachType(ParticleAttachType type, uint expected)
        {
            Assert.Equal(expected, new ZoneRoutineActors(1, 2).ActorFor(type));
        }

        [Fact]
        public void ActorFor_FallsBackToTheOtherActor()
        {
            Assert.Equal(1u, new ZoneRoutineActors(1, 0).ActorFor(ParticleAttachType.TargetActor));
            Assert.Equal(2u, new ZoneRoutineActors(0, 2).ActorFor(ParticleAttachType.SourceActor));
            Assert.Equal(0u, ZoneRoutineActors.None.ActorFor(ParticleAttachType.ZoneActor0xA));
        }

        [Fact]
        public void Player_HandsEveryStartedEmitterItsRoutinesActors_ThroughStartedRoutines()
        {
            var library = new ZoneRoutineLibrary();
            library.AddRoutine("z/effe/a", Routine("root", 10, Cmd(0x02, 0, "g001"), Cmd(0x03, 0, "kid")));
            library.AddRoutine("z/effe/a", Routine("kid", 10, Cmd(0x02, 0, "g002")));
            var emitters = new Dictionary<string, ZoneParticleEmitter>();
            foreach (string id in new[] { "g001", "g002" })
            {
                var template = Generator(id, 100);
                library.AddGenerator("z/effe/a", id, template);
                emitters[id] = new ZoneParticleEmitter(template);
            }
            var byTemplate = emitters.Values.ToDictionary(e => e.Template);
            var player = new ZoneRoutinePlayer(library);
            var bound = new List<(string, ZoneRoutineActors)>();

            Assert.True(player.Play("root", new ZoneRoutineActors(0x1001, 0x1002)));
            for (int i = 0; i < 3; i++) player.Update(1f, t => byTemplate.GetValueOrDefault(t), (e, a) => bound.Add((e.Template.Definition.DatId, a)));

            Assert.Equal(new[] { ("g001", new ZoneRoutineActors(0x1001, 0x1002)), ("g002", new ZoneRoutineActors(0x1001, 0x1002)) }, bound);
        }

        [Fact]
        public void DoorRoutines_AreReachedByPathOnly_AndFindTheirDoorResources()
        {
            // Alzadaal's door _20t, shortened: open (in the door's directory) starts stat, which lives in its door/ folder.
            var library = new ZoneRoutineLibrary();
            library.AddRoutine("d_at/door/_20t", Routine("open", 2, Cmd(0x03, 0, "stat")), byName: false);
            library.AddRoutine("d_at/door/_20t", Routine("intc", 2, Cmd(0x03, 0, "map1")), byName: false);
            library.AddRoutine("d_at/door/_20t/door", Routine("stat", 200, Cmd(0x02, 0, "s001"), Cmd(0x1E, 60, "g000")), byName: false);
            library.AddRoutine("d_at/door/_20t/door", Routine("map1", 0, Cmd(0x02, 0, "g000")), byName: false);
            var g000 = Generator("g000", 0);
            var s001 = Generator("s001", 30);
            library.AddGenerator("d_at/door/_20t/door", "g000", g000);
            library.AddGenerator("d_at/door/_20t/door", "s001", s001);
            var emitters = new[] { g000, s001 }.ToDictionary(t => t, t => new ZoneParticleEmitter(t));
            var player = new ZoneRoutinePlayer(library);
            var bound = new List<(string Id, uint Door)>();
            void Run(int frames)
            {
                for (int i = 0; i < frames; i++)
                {
                    player.Update(1f, t => emitters.GetValueOrDefault(t), (e, a) => bound.Add((e.Template.Definition.DatId, a.CasterServerId)));
                    foreach (var e in emitters.Values) e.Update(1f, new ZoneParticleFrame(Vector3.Zero, 0.5f, Vector3.One));
                }
            }

            // A map scheduler's name never starts a door's routine.
            Assert.False(player.Play("open"));
            Assert.Equal(200, player.GetFrames("d_at/door/_20t", "open"));

            // The door is first seen closed: intc lights its idle meshes; opening plays stat on the door.
            var trigger = new DoorEffectTrigger(new Dictionary<string, string> { ["_20t"] = "d_at/door/_20t" });
            trigger.Observe("_20t", 0x0101, open: false, player);
            Run(3);
            Assert.NotEmpty(emitters[g000].Particles);
            trigger.Observe("_20t", 0x0101, open: false, player); // no change: nothing replays
            trigger.Observe("_20t", 0x0101, open: true, player);
            Run(3);
            Assert.NotEmpty(emitters[s001].Particles);
            Run(70);
            Assert.Empty(emitters[g000].Particles); // stat's 0x1E at 60 kills the idle meshes
            Assert.All(bound, b => Assert.Equal(0x0101u, b.Door));
            Assert.Contains(bound, b => b.Id == "s001");
        }

        [Fact]
        public void StreetLamp_LightsWhenItsDoorOpens_ThroughItsEvstRoutine()
        {
            // Lower Jeuno's lamp _l00, shortened: state routines one level down under other names, lton in a sibling.
            var library = new ZoneRoutineLibrary();
            library.AddRoutine("t_ju/door/_l00/ligh", Routine("inte", 2, Cmd(0x5F, 0, "lton")), byName: false);
            library.AddRoutine("t_ju/door/_l00/ligh", Routine("evst", 2, Cmd(0x73, 0, "lton")), byName: false);
            library.AddRoutine("t_ju/door/_l00/ligh", Routine("eved", 2, Cmd(0x5F, 0, "lton")), byName: false);
            library.AddRoutine("t_ju/door/_l00/lt08", Routine("lton", 60, Cmd(0x02, 0, "RR09")), byName: false);
            var lamp = Generator("RR09", 0);
            library.AddGenerator("t_ju/door/_l00/lt08", "RR09", lamp);
            var emitter = new ZoneParticleEmitter(lamp);
            var player = new ZoneRoutinePlayer(library);
            void Run(int frames)
            {
                for (int i = 0; i < frames; i++)
                {
                    player.Update(1f, t => t == lamp ? emitter : null);
                    emitter.Update(1f, new ZoneParticleFrame(Vector3.Zero, 0.5f, Vector3.One));
                }
            }

            var trigger = new DoorEffectTrigger(new Dictionary<string, string> { ["_l00"] = "t_ju/door/_l00/ligh" });
            trigger.Observe("_l00", 0x0202, open: false, player); // intc is missing: inte plays
            Run(3);
            Assert.Empty(emitter.Particles);
            trigger.Observe("_l00", 0x0202, open: true, player); // open is missing: evst loops lton
            Run(3);
            Assert.NotEmpty(emitter.Particles);
            Assert.True(player.IsPlaying("lton"));
        }

        [Fact]
        public void StopGenerator_EndsTheEmission_AndLeavesTheParticles()
        {
            var library = new ZoneRoutineLibrary();
            library.AddRoutine("z/a", Routine("go", 0, EffectRoutineBuilder.Command(0x02, 0, 200, "g001")));
            library.AddRoutine("z/a", Routine("halt", 0, Cmd(0x2D, 0, "g001")));
            var template = Generator("g001", 500);
            template.Definition.FramesPerEmission = 5;
            library.AddGenerator("z/a", "g001", template);
            var emitter = new ZoneParticleEmitter(template);
            var player = new ZoneRoutinePlayer(library);
            void Run(int frames)
            {
                for (int i = 0; i < frames; i++)
                {
                    player.Update(1f, _ => emitter);
                    emitter.Update(1f, new ZoneParticleFrame(Vector3.Zero, 0.5f, Vector3.One));
                }
            }
            player.Play("go");
            Run(20);
            int live = emitter.Particles.Count;
            Assert.True(live > 1);
            player.Play("halt");
            Run(20);
            Assert.Equal(live, emitter.Particles.Count);
        }

        /// <summary>
        /// An event's 0x2D naming a door's open (Mamook's doors, #233) takes its length from the door, not from a zone
        /// routine of that name, so 0x54 waits while the door moves.
        /// </summary>
        [Fact]
        public void ZoneScheduler_OnADoor_TakesTheDoorsLength()
        {
            var parser = new Gordian.Core.Network.PacketParser(new Gordian.Core.Config.SessionProfile(), (_, _) => System.Threading.Tasks.Task.CompletedTask);
            var controller = new Gordian.Engine.Events.EventDialogController();
            controller.Attach(parser.Progression, parser.ProgressionModule, parser.World, parser.LocalPlayer, new Gordian.Engine.Ui.StockUiChat(),
                new Gordian.Engine.Ui.StockUiMenuController(), () => "Cybin");
            parser.World.UpsertEntity(new WorldEntity(0x01118123, 0x123, EntityType.Door) { TransportId = "_ij0" });
            parser.World.UpsertEntity(new WorldEntity(0x01118124, 0x124, EntityType.Npc));
            var zoneFrames = Gordian.Engine.Events.EventDialogController.ZoneRoutineFrames;
            var doorFrames = Gordian.Engine.Events.EventDialogController.DoorRoutineFrames;
            try
            {
                Gordian.Engine.Events.EventDialogController.ZoneRoutineFrames = (_, _) => 7;
                Gordian.Engine.Events.EventDialogController.DoorRoutineFrames = (_, door, routine) => door == "_ij0" && routine == "open" ? 45 : 0;
                var host = (Gordian.Engine.Events.IEventVmHost)controller;
                Assert.Equal(45, host.StartZoneScheduler("open", 0x01118123, 0x01118123));
                Assert.Equal(7, host.StartZoneScheduler("open", 0x01118124, 0x01118124)); // not a door
                Assert.Equal(7, host.StartZoneScheduler("sc00", 0x01118123, 0x01118123)); // not a door routine
            }
            finally
            {
                Gordian.Engine.Events.EventDialogController.ZoneRoutineFrames = zoneFrames;
                Gordian.Engine.Events.EventDialogController.DoorRoutineFrames = doorFrames;
            }
        }

        /// <summary>An event's zone scheduler plays in the zone the scene shows (an event zone), a server's in the current zone.</summary>
        [Fact]
        public void EventZoneScheduler_GoesToTheSceneZone()
        {
            var world = new WorldState { CurrentZoneId = 14 };
            world.EventZoneId = 241;
            world.PostMapScheduler("kaks", 1, 1, stop: false, sceneZone: true);
            world.PostMapScheduler("1pa1", 0, 0);
            var scene = new List<MapSchedulerRequest>();
            world.TakeMapSchedulers(241, scene);
            Assert.Equal("kaks", Assert.Single(scene).Routine);
            var current = new List<MapSchedulerRequest>();
            world.TakeMapSchedulers(14, current);
            Assert.Equal("1pa1", Assert.Single(current).Routine);
        }

        // ── retail zone DATs ─────────────────────────────────────────────────────

        private static ZoneGeometry? LoadZone(int zoneId)
        {
            if (!Directory.Exists(GameDirectory)) return null;
            var rm = new ResourceManager(GameDirectory);
            rm.InitializeFileTable();
            return rm.TryLoadZone(zoneId, out var zone, out _) ? zone : null;
        }

        /// <summary>
        /// Phanauet Channel (zone 1): the cloudy-weather birds' spawn points wbi0-wbi6 (TargetActorSourceFacing, zone
        /// positions 45 yalms over the river) are zone emitters under their weather, with the weighted-mesh bird tbi1 as a
        /// child (#121).
        /// </summary>
        [Fact]
        public void Phanauet_WeatherBirds_AreZonePlacedWeatherEmitters()
        {
            if (LoadZone(1) is not { } zone) return;
            var birds = zone.EffectLayers.Where(l => l.Emitter != null && l.Name.StartsWith("wbi", StringComparison.Ordinal) && l.WeatherIds.Contains("clod")).ToList();
            Assert.Equal(7, birds.Count);
            Assert.All(birds, b => Assert.False(b.IsActorAttached));
            Assert.Contains(birds, b => Vector3.Distance(b.Emitter!.RawBasePosition, new Vector3(-176.7f, -45f, -256.4f)) < 0.1f);
            // The fireflies hota/htr* too.
            Assert.Contains(zone.EffectLayers, l => l.Name == "htr1" && l.Emitter != null && !l.IsActorAttached);
            // The divers dw01-dw03 follow the dive path tbi1 (a 12-yalm circle) with the height curve psy2.
            var diver = Assert.Single(zone.EffectLayers, l => l.Name == "dw01" && l.WeatherIds.Contains("clod") && l.Emitter != null);
            Assert.Equal(7, diver.Emitter!.PointLists.Values.Single().Length);
        }

        /// <summary>
        /// The zone an event opens with 0x34 holds the routines its 0x2D names that the event's own zone lacks (#233): the
        /// cutscene stage zone 541 (file 641, <c>e_cr</c>) that zone 14's event 50 and Pso'Xja's event 2 open loads as a zone and
        /// has them; so does 540 (file 640), which Lower Delkfutt's Tower's event 36 opens.
        /// </summary>
        [Fact]
        public void EventZones_HoldTheRoutinesTheirEventsName()
        {
            if (LoadZone(541) is not { } stage) return;
            // sc21-sc41 / kc21-kc41, which the event names too, are in no stage zone 540-546 (nor, per the file scan, anywhere).
            foreach (string routine in new[] { "sc11", "kaks", "kaki", "kc11", "4aks", "lop6" }) Assert.NotEmpty(stage.MapRoutines.Find(routine));
            if (LoadZone(14) is { } hall) Assert.Empty(hall.MapRoutines.Find("kaks"));
            // Windurst Woods 588 opens 192 for its skai.
            if (LoadZone(192) is { } ruins) Assert.NotEmpty(ruins.MapRoutines.Find("skai"));
            // Lower Delkfutt's Tower 36 (the Crystal Line scene) opens 540 for its main / gass / del1 / del2.
            if (LoadZone(540) is { } line) foreach (string routine in new[] { "main", "gass", "del1", "del2" }) Assert.NotEmpty(line.MapRoutines.Find(routine));
        }

        /// <summary>
        /// Alzadaal Undersea Ruins (zone 72): the door _20t's effect routines are reachable by its directory, its idle
        /// meshes g000-g009 ride the door (ZoneActor0xA), and a map scheduler's name does not start them (#227, #232).
        /// </summary>
        [Fact]
        public void Alzadaal_DoorEffects_RideTheDoor()
        {
            if (LoadZone(72) is not { } zone) return;
            Assert.Equal("d_at/door/_20t", zone.DoorDirectories["_20t"]);
            Assert.DoesNotContain(zone.MapRoutines.Find("stat"), found => found.Directory.Contains("/door/", StringComparison.Ordinal));
            Assert.True(zone.MapRoutines.TryResolveRoutine("d_at/door/_20t", "stat", out string statDir, out var stat));
            Assert.Equal("d_at/door/_20t/door", statDir);
            Assert.Equal(200, stat.TotalFrames);
            var g000 = zone.EffectLayers.Where(l => l.Name == "g000" && l.OwnerDoorId == "_20t").ToList();
            Assert.Single(g000);
            Assert.True(g000[0].IsActorAttached);
            Assert.Equal(ParticleAttachType.ZoneActor0xA, g000[0].AttachType);
            Assert.True(zone.MapRoutines.TryResolveGenerator("d_at/door/_20t/door", "g000", out var template));
            Assert.Same(g000[0].Emitter, template);
            // The portals still answer to their names.
            Assert.Single(zone.MapRoutines.Find("1pa1"));
        }
            /// <summary>
        /// The birds' spawners follow their point list (0x54 / 0x34, round 1 E1): Phanauet Channel's and Carpenters'
        /// Landing's <c>wbi0</c> circle about 40 yalms around their base instead of hovering, and face along the circle.
        /// </summary>
        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        public void WeatherBirds_CircleAlongTheirPath(int zoneId)
        {
            if (LoadZone(zoneId) is not { } zone) return;
            var layer = zone.EffectLayers.First(l => l.Name == "wbi0" && l.WeatherIds.Contains("clod") && l.Emitter != null);
            Assert.True(layer.Emitter!.PointLists.Values.Single().Length == 7);
            // The path alone: the clock curve (op 0x17) would make the first bird a matter of chance.
            layer.Emitter!.EmissionClockCurve = null;
            var emitter = new ZoneParticleEmitter(layer.Emitter!);
            var basePos = layer.Emitter!.RawBasePosition;
            var frame = new ZoneParticleFrame(basePos, 0.5f, Vector3.One);
            emitter.Update(1f, frame);
            var bird = Assert.Single(emitter.Particles);
            emitter.Update(1f, frame);
            var min = bird.WorldPosition;
            var max = min;
            float travelled = 0f;
            var last = min;
            for (int i = 0; i < 3000; i++)
            {
                emitter.Update(1f, frame);
                travelled += Vector3.Distance(last, bird.WorldPosition);
                last = bird.WorldPosition;
                min = Vector3.Min(min, last);
                max = Vector3.Max(max, last);
            }
            // Across the ~40-yalm circle in the horizontal plane (X and Z), level in height.
            Assert.InRange(max.X - min.X, 70f, 90f);
            Assert.InRange(max.Z - min.Z, 70f, 90f);
            Assert.True(max.Y - min.Y < 1f, $"height spread {max.Y - min.Y:F2}");
            Assert.True(travelled > 150f, $"travelled {travelled:F1}");
            Assert.True(bird.LastMovement.LengthSquared() > 0f);
        }
            /// <summary>
        /// Section 1 op 0x17 scales each emission by a curve over the day (round 2 E1): none while it is 0, every emission at
        /// 1, about half at 0.5.
        /// </summary>
        [Fact]
        public void EmissionClockCurve_ScalesTheEmissionsByTheTimeOfDay()
        {
            var template = Generator("bird", 2000);
            template.Definition.AutoRun = true;
            template.Definition.FramesPerEmission = 1;
            template.EmissionClockCurve = new KeyFrameCurve("wabi", new[] { new KeyFrameEntry(0f, 0f), new KeyFrameEntry(0.5f, 0f), new KeyFrameEntry(0.5001f, 0.5f), new KeyFrameEntry(1f, 0.5f) });
            int Emitted(float dayFraction)
            {
                var emitter = new ZoneParticleEmitter(template, seed: 7);
                int born = 0;
                for (int i = 0; i < 400; i++)
                {
                    int before = emitter.Particles.Count;
                    emitter.Update(1f, new ZoneParticleFrame(Vector3.Zero, dayFraction, Vector3.One));
                    born += Math.Max(0, emitter.Particles.Count - before);
                }
                return born;
            }
            Assert.Equal(0, Emitted(0.25f));
            Assert.InRange(Emitted(0.75f), 120, 280); // about one emission in two
        }

        /// <summary>A retired weather emitter emits no more and fades its particles out, then drops them (round 2 E1).</summary>
        [Fact]
        public void Retire_FadesTheParticlesOut()
        {
            var template = Generator("rain", 1000);
            template.Definition.AutoRun = true;
            template.Definition.FramesPerEmission = 5;
            var emitter = new ZoneParticleEmitter(template);
            var frame = new ZoneParticleFrame(Vector3.Zero, 0.5f, Vector3.One);
            for (int i = 0; i < 30; i++) emitter.Update(1f, frame);
            int live = emitter.Particles.Count;
            Assert.True(live > 3);
            emitter.Retire(60f);
            emitter.Update(30f, frame);
            Assert.Equal(live, emitter.Particles.Count);
            Assert.All(emitter.Particles, p => Assert.InRange(p.ColorMultiplier.W, 0.3f, 0.7f));
            emitter.Update(31f, frame);
            Assert.Empty(emitter.Particles);
            emitter.Resume();
            for (int i = 0; i < 10; i++) emitter.Update(1f, frame);
            Assert.NotEmpty(emitter.Particles);
        }

        /// <summary>Phanauet Channel's birds take their clock curve wabi: no bird at 03:00, some at noon.</summary>
        [Fact]
        public void PhanauetBirds_FlyOnlyByDay()
        {
            if (LoadZone(1) is not { } zone) return;
            var layer = zone.EffectLayers.First(l => l.Name == "wbi0" && l.WeatherIds.Contains("clod") && l.Emitter != null);
            Assert.Equal("wabi", layer.Emitter!.EmissionClockCurve!.DatId);
            int Born(float dayFraction)
            {
                var emitter = new ZoneParticleEmitter(layer.Emitter!, seed: 3);
                var frame = new ZoneParticleFrame(layer.Emitter!.RawBasePosition, dayFraction, Vector3.One);
                int born = 0;
                for (int i = 0; i < 60 * 600; i++)
                {
                    int before = emitter.Particles.Count;
                    emitter.Update(1f, frame);
                    born += Math.Max(0, emitter.Particles.Count - before);
                }
                return born;
            }
            Assert.Equal(0, Born(3f / 24f));
            // 0.05 per emission, one emission every 334 frames: about 5 birds in ten minutes at noon.
            Assert.InRange(Born(12f / 24f), 1, 14);
        }
    }
}
