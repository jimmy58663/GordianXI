// tests/Gordian.Core.Tests/Graphics/AttachedZoneEffectsTests.cs
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
using Gordian.Core.World;
using Xunit;

namespace Gordian.Core.Tests.Graphics
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
            SceneRoutine.Decode(EffectRoutineDecoderTests.BuildPayload(totalFrames, commands.ToList()), name)!;

        private static byte[] Cmd(byte op, ushort delay, string reference) => EffectRoutineDecoderTests.Command(op, delay, 0, reference);

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
        public void StopGenerator_EndsTheEmission_AndLeavesTheParticles()
        {
            var library = new ZoneRoutineLibrary();
            library.AddRoutine("z/a", Routine("go", 0, EffectRoutineDecoderTests.Command(0x02, 0, 200, "g001")));
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
    }
}
