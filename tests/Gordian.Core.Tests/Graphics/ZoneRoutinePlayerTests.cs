// tests/Gordian.Core.Tests/Graphics/ZoneRoutinePlayerTests.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Gordian.Core.Graphics;
using Gordian.Core.Resources;
using Gordian.Core.Resources.Events;
using Gordian.Core.Resources.Graphics;
using Gordian.Core.Resources.Models;
using Gordian.Core.Tests.Resources;
using Xunit;

namespace Gordian.Core.Tests.Graphics
{
    /// <summary>
    /// On-demand zone routines (#210): which routines the client starts on zone load, and the map scheduler player that
    /// runs the rest (Alzadaal Undersea Ruins' Runic Portals). The retail checks are skipped without the game install.
    /// </summary>
    public class ZoneRoutinePlayerTests
    {
        private const string GameDirectory = @"G:\Program Files (x86)\PlayOnline\SquareEnix\FINAL FANTASY XI";
        private const string PortalDir = "d_at/effe/pba1/1pba";

        private static readonly ZoneParticleFrame Frame = new(Vector3.Zero, 0.5f, Vector3.One);

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
            SceneRoutine.Decode(EffectRoutineDecoderTests.BuildPayload(totalFrames, commands.ToList()), name)!;

        private static byte[] Cmd(byte op, ushort delay, string reference) => EffectRoutineDecoderTests.Command(op, delay, 0, reference);

        /// <summary>The portal's routines, shortened: 1pa2 spawns g0a1, then at 300 starts s104 once and s103 repeating; 1pak ends them.</summary>
        private static (ZoneRoutinePlayer Player, Dictionary<string, ZoneParticleEmitter> Emitters) Portal()
        {
            var library = new ZoneRoutineLibrary();
            library.AddRoutine(PortalDir, Routine("1pa2", 300, Cmd(0x02, 300, "g0a1"), Cmd(0x03, 0, "s104"), Cmd(0x73, 0, "s103")));
            library.AddRoutine(PortalDir, Routine("s104", 0, Cmd(0x02, 0, "tw21")));
            library.AddRoutine(PortalDir, Routine("s103", 140, Cmd(0x02, 140, "ll01")));
            library.AddRoutine(PortalDir, Routine("1pak", 0, Cmd(0x1E, 0, "g0a1"), Cmd(0x5F, 0, "s103"), Cmd(0x1E, 0, "ll01")));
            var emitters = new Dictionary<string, ZoneParticleEmitter>();
            foreach (var (id, life) in new[] { ("g0a1", (ushort)1000), ("tw21", (ushort)60), ("ll01", (ushort)20) })
            {
                var template = Generator(id, life);
                // Generators resolve from the routine's directory up through its parents.
                library.AddGenerator("d_at/effe/pba1", id, template);
                emitters[id] = new ZoneParticleEmitter(template);
            }
            return (new ZoneRoutinePlayer(library), emitters);
        }

        private static void Run(ZoneRoutinePlayer player, Dictionary<string, ZoneParticleEmitter> emitters, int frames, Action<int>? each = null)
        {
            var byTemplate = emitters.Values.ToDictionary(e => e.Template);
            for (int i = 0; i < frames; i++)
            {
                player.Update(1f, t => byTemplate.GetValueOrDefault(t));
                foreach (var emitter in emitters.Values) emitter.Update(1f, Frame);
                each?.Invoke(i);
            }
        }

        [Fact]
        public void Portal_PlaysOnlyWhenNamed_AndTheEndStopsIt()
        {
            var (player, emitters) = Portal();
            Run(player, emitters, 400);
            Assert.All(emitters.Values, e => Assert.Empty(e.Particles)); // nothing runs by itself

            Assert.False(player.Play("nope"));
            Assert.True(player.Play("1pa2"));
            Run(player, emitters, 5);
            Assert.Single(emitters["g0a1"].Particles);

            // s104 (0 frames) spawns its pillar once; s103 repeats its line every 140 frames.
            int pillars = 0, lines = 0, lastLines = 0;
            Run(player, emitters, 600, _ =>
            {
                if (emitters["tw21"].Particles.Count > pillars) pillars = emitters["tw21"].Particles.Count;
                int live = emitters["ll01"].Particles.Count;
                if (live > lastLines) lines++;
                lastLines = live;
            });
            Assert.Equal(1, pillars);
            Assert.Equal(3, lines); // at 300, 440 and 580
            Assert.Equal(1, player.RunningCount); // s103

            Assert.True(player.Play("1pak"));
            Run(player, emitters, 2);
            Assert.Equal(0, player.RunningCount);
            Assert.Empty(emitters["g0a1"].Particles);
            Run(player, emitters, 400);
            Assert.All(emitters.Values, e => Assert.Empty(e.Particles));
        }

        [Fact]
        public void ZeroFrameRoutine_StartedRepeating_RunsOnce()
        {
            var library = new ZoneRoutinePlayerTestsLibrary().Build();
            var player = new ZoneRoutinePlayer(library.Library);
            Assert.True(player.Play("loop"));
            Run(player, library.Emitters, 50);
            Assert.Equal(0, player.RunningCount);
            Assert.Single(library.Emitters["tw21"].Particles);
        }

        private sealed class ZoneRoutinePlayerTestsLibrary
        {
            public ZoneRoutineLibrary Library { get; } = new();
            public Dictionary<string, ZoneParticleEmitter> Emitters { get; } = new();

            public ZoneRoutinePlayerTestsLibrary Build()
            {
                Library.AddRoutine("z/effe/a", Routine("loop", 0, Cmd(0x73, 0, "s104")));
                Library.AddRoutine("z/effe/a", Routine("s104", 0, Cmd(0x02, 0, "tw21")));
                var template = Generator("tw21", 100);
                Library.AddGenerator("z/effe/a", "tw21", template);
                Emitters["tw21"] = new ZoneParticleEmitter(template);
                return this;
            }
        }

        // ── retail zone DATs ─────────────────────────────────────────────────────

        private static ZoneGeometry? LoadZone(int zoneId)
        {
            if (!Directory.Exists(GameDirectory)) return null;
            var rm = new ResourceManager(GameDirectory);
            rm.InitializeFileTable();
            return rm.TryLoadZone(zoneId, out var zone, out _) ? zone : null;
        }

        [Fact]
        public void Alzadaal_PortalRoutinesWaitForTheirMapScheduler()
        {
            if (LoadZone(72) is not { } zone) return;
            string[] portal = { "g0a1", "g0c2", "tw21", "tw22", "tw23", "tw24", "ll01", "ll05", "ll08" };
            var layers = zone.EffectLayers.Where(l => l.Emitter != null && portal.Contains(l.Name)).ToList();
            Assert.NotEmpty(layers);
            // None runs by itself any more (they looped and piled up); each waits idle for 1pa2 / 1pb2.
            Assert.All(layers, l => Assert.Null(l.Emitter!.Schedule));
            foreach (string name in new[] { "1pa1", "1pa2", "1pak", "1pb1", "1pb2", "1pbk", "2pb1", "2pb2", "2pbk" })
            {
                Assert.Single(zone.MapRoutines.Find(name));
            }
            // The zone's own ambient loops still run (third list op 0x01): saka's fish, gyo1's school.
            Assert.Contains(zone.EffectLayers, l => l.Name == "syd2" && l.Emitter?.Schedule != null && l.Emitter.ScheduleLoopFrames == 12654);

            // 1pa2 starts the portal: g0a1 at once, the pillars of s104 at 300.
            var player = new ZoneRoutinePlayer(zone.MapRoutines);
            var emitters = zone.EffectLayers.Where(l => l.Emitter != null).ToDictionary(l => l.Emitter!, l => new ZoneParticleEmitter(l.Emitter!));
            Assert.True(player.Play("1pa2"));
            int g0a1 = 0, pillars = 0;
            for (int frame = 0; frame < 320; frame++)
            {
                player.Update(1f, t => emitters.GetValueOrDefault(t));
                foreach (var (template, emitter) in emitters)
                {
                    // The camera stands at each generator, inside any emission cull distance.
                    emitter.Update(1f, new ZoneParticleFrame(template.RawBasePosition, 0.5f, Vector3.One));
                    if (template.Definition.DatId == "g0a1") g0a1 = Math.Max(g0a1, emitter.Particles.Count);
                    if (template.Definition.DatId == "tw21") pillars = Math.Max(pillars, emitter.Particles.Count);
                }
            }
            Assert.True(g0a1 > 0);
            Assert.Equal(1, pillars);
            Assert.True(player.RunningCount >= 2); // s102 / s103 repeat until 1pak
            Assert.True(player.Play("1pak"));
            player.Update(1f, t => emitters.GetValueOrDefault(t));
            Assert.Equal(0, player.RunningCount);
        }

        [Fact]
        public void Bibiki_ShorelineStillLoopsOnZoneLoad()
        {
            if (LoadZone(4) is not { } zone) return;
            // umi2/s000 (2669 frames, third list op 0x01) rolls kwa1..kwa3 in at 0, 985 and 2027.
            var starts = zone.EffectLayers
                .Where(l => l.Emitter?.Schedule != null && l.Emitter.ScheduleLoopFrames == 2669)
                .ToDictionary(l => l.Name, l => l.Emitter!.Schedule!.Single().StartFrame);
            Assert.Equal(0, starts["kwa1"]);
            Assert.Equal(985, starts["kwa2"]);
            Assert.Equal(2027, starts["kwa3"]);
        }

        [Fact]
        public void Manaclipper_AmbientRoutineRunsTheRoutinesItStarts_AtTheirFrames()
        {
            if (LoadZone(3) is not { } zone) return;
            // shio/alls (8000 frames, loops on completion) starts sio2 at 0, 500, 3000, 3400 and sio1 at 1200, 3800; each
            // spawns iws1 30 frames in. Before #210 sio1 / sio2 looped by themselves every 335 / 250 frames.
            var iws1 = zone.EffectLayers.First(l => l.Name == "iws1" && l.Emitter?.Schedule != null).Emitter!;
            Assert.Equal(8000, iws1.ScheduleLoopFrames);
            Assert.Equal(new[] { 30, 530, 1230, 3030, 3430, 3830 }, iws1.Schedule!.Select(s => s.StartFrame).OrderBy(f => f));
        }
    }
}
