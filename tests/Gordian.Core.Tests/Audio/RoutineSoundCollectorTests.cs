// tests/Gordian.Core.Tests/Audio/RoutineSoundCollectorTests.cs
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Gordian.Core.Resources;
using Gordian.Core.Resources.Graphics;
using Gordian.Core.Resources.Models;
using Gordian.Core.Resources.Tables;
using Xunit;

namespace Gordian.Core.Tests.Audio
{
    /// <summary>The sounds motion routines carry (#41: combat and action sounds).</summary>
    public class RoutineSoundCollectorTests
    {
        private const string GameDirectory = @"G:\Program Files (x86)\PlayOnline\SquareEnix\FINAL FANTASY XI";
        private readonly ITestOutputHelper _output;

        public RoutineSoundCollectorTests(ITestOutputHelper output) => _output = output;

        /// <summary>A command: op, size in dwords, delay, a 4-char reference, zero padding.</summary>
        private static byte[] Cmd(byte op, int dwords, int delay = 0, string reference = "")
        {
            var c = new byte[dwords * 4];
            c[0] = op;
            BinaryPrimitives.WriteUInt16LittleEndian(c.AsSpan(1), (ushort)dwords);
            BinaryPrimitives.WriteUInt16LittleEndian(c.AsSpan(4), (ushort)delay);
            if (reference.Length > 0)
            {
                Encoding.ASCII.GetBytes(reference).CopyTo(c, 8);
            }

            return c;
        }

        private static RawMotionRoutine Routine(string name, params byte[][] commands)
        {
            var body = commands.SelectMany(c => c).Concat(new byte[8]).ToArray();
            var payload = new byte[0x20 + body.Length];
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(0x14), 0x20 + 16);
            body.CopyTo(payload, 0x20);
            return MotionRoutineDecoder.Decode(payload, name)!;
        }

        private static RawMotionRoutine Resolve(RawMotionRoutine routine, params (string Name, int Id)[] pointers) =>
            RoutineSoundCollector.ResolveSoundIds(routine, pointers.ToDictionary(p => p.Name, p => p.Id));

        [Fact]
        public void SoundCommand_ResolvesByNameAndKeepsItsTick()
        {
            var swing = Resolve(Routine("ati0", Cmd(0x01, 2), Cmd(0x20, 3, delay: 4), Cmd(0x0A, 8, reference: "skaz")), ("skaz", 6062));
            var cues = RoutineSoundCollector.Collect(new Dictionary<string, RawMotionRoutine> { ["ati0"] = swing }, "ati0");
            var cue = Assert.Single(cues);
            Assert.Equal(4, cue.Tick);
            Assert.Equal(new[] { 6062 }, cue.Choices);
            Assert.DoesNotContain(swing.Commands, c => c.Op == 0x0A); // playback commands are unchanged
        }

        [Fact]
        public void Links_AreFollowedAndBlockingLinksShiftTheRest()
        {
            var routines = new Dictionary<string, RawMotionRoutine>
            {
                ["atk0"] = Routine("atk0", Cmd(0x3B, 4, delay: 2, reference: "wind"), Cmd(0x0A, 8, reference: "post")),
                ["wind"] = Resolve(Routine("wind", Cmd(0x0A, 8, delay: 10, reference: "whsh")), ("whsh", 7)),
            };
            routines["atk0"] = Resolve(routines["atk0"], ("post", 8));
            var cues = RoutineSoundCollector.Collect(routines, "atk0");
            Assert.Equal(new[] { (0, 7), (12, 8) }, cues.Select(c => (c.Tick, c.Choices[0])).ToArray());
        }

        [Fact]
        public void RandomChoice_KeepsSilentMembers()
        {
            var voice = Resolve(Routine("vatk",
                Cmd(0x01, 2), Cmd(0x3D, 2, delay: 1), Cmd(0x50, 2), Cmd(0x50, 2),
                Cmd(0x0A, 8, reference: "atk1"), Cmd(0x0A, 8, delay: 1, reference: "atk2"), Cmd(0x3E, 2)),
                ("atk1", 101), ("atk2", 102));
            var cue = Assert.Single(RoutineSoundCollector.Collect(new Dictionary<string, RawMotionRoutine> { ["vatk"] = voice }, "vatk"));
            Assert.Equal(1, cue.Tick);
            Assert.Equal(new[] { 0, 0, 101, 102 }, cue.Choices);
        }

        [Fact]
        public void TargetLink_IsACueForTheOtherActor()
        {
            var damg = Routine("damg", Cmd(0x09, 4, reference: "chit"), Cmd(0x57, 4, reference: "sdam"));
            var cues = RoutineSoundCollector.Collect(new Dictionary<string, RawMotionRoutine> { ["damg"] = damg }, "damg");
            var cue = Assert.Single(cues);
            Assert.True(cue.IsTargetLink);
            Assert.Equal("chit", cue.TargetRoutine);
        }

        [Fact]
        public void UnknownRoutine_HasNoSounds() =>
            Assert.Empty(RoutineSoundCollector.Collect(new Dictionary<string, RawMotionRoutine>(), "ati0"));

        private static Dictionary<string, RawMotionRoutine> RoutinesOf(params byte[][] dats)
        {
            var routines = new Dictionary<string, RawMotionRoutine>(StringComparer.Ordinal);
            foreach (byte[] dat in dats)
            {
                foreach (RawMotionRoutine r in EntityModelLoader.ParseDatContainer(dat).Routines)
                {
                    routines[r.Name] = r;
                }
            }

            return routines;
        }

        [Fact]
        public void Retail_MonsterSwingCryHitAndDeath()
        {
            if (!Directory.Exists(GameDirectory))
            {
                return;
            }

            var rm = new ResourceManager(GameDirectory);
            rm.InitializeFileTable();
            // Monster model 10: swing whoosh skaz, cries vatk / vdam / vded, hit sound shit (through chit).
            var routines = RoutinesOf(rm.LoadDatBytesByFileId(CharacterEquipmentResolver.GetMonsterFileId(10))!);
            var ati0 = RoutineSoundCollector.Collect(routines, "ati0");
            Assert.Contains(ati0, c => c.Choices.SequenceEqual(new[] { 6062 }));
            var atk0 = RoutineSoundCollector.Collect(routines, "atk0");
            var cry = Assert.Single(atk0);
            Assert.Equal(10, cry.Choices.Count);
            Assert.Equal(6, cry.Choices.Count(id => id == 0));
            var damg = RoutineSoundCollector.Collect(routines, "damg");
            Assert.Contains(damg, c => c.IsTargetLink && c.TargetRoutine == "chit");
            Assert.Contains(RoutineSoundCollector.Collect(routines, "chit"), c => c.Choices.Contains(6063)); // shit
            Assert.NotEmpty(RoutineSoundCollector.Collect(routines, "dead"));
            _output.WriteLine($"ati0 {ati0.Count}, damg {damg.Count}");
        }

        [Fact]
        public void Retail_CharacterChitPlaysItsWeaponsHitSound_AndTheSwingItsWhoosh()
        {
            if (!Directory.Exists(GameDirectory))
            {
                return;
            }

            var rm = new ResourceManager(GameDirectory);
            rm.InitializeFileTable();
            Assert.True(CharacterEquipmentResolver.TryResolveGearFileId(CharacterRace.HumeMale, CharacterSlot.Main, 1, out int weapon));
            // Hume male base motion (chit -> se h), battle pack (ati0 -> skaz at 34), main weapon 1 (se h / skaz sounds).
            var routines = RoutinesOf(
                rm.LoadDatBytes(Path.Combine("ROM", "27", "82.DAT"))!,
                rm.LoadDatBytes(Path.Combine("ROM", "32", "13.DAT"))!,
                rm.LoadDatBytesByFileId(weapon)!);
            Assert.Contains(RoutineSoundCollector.Collect(routines, "chit"), c => c.Choices.Contains(6031));
            var swing = RoutineSoundCollector.Collect(routines, "ati0");
            Assert.Contains(swing, c => c.Tick == 34 && c.Choices.Contains(6030));
        }
    }
}
