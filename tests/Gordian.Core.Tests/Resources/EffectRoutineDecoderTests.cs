// tests/Gordian.Core.Tests/Resources/EffectRoutineDecoderTests.cs
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;
using Gordian.Core.Resources.Graphics;
using Xunit;

namespace Gordian.Core.Tests.Resources
{
    public class EffectRoutineDecoderTests
    {
        [Fact]
        public void Decode_SpawnsStartAtTheSumOfPriorDelays()
        {
            // Mirrors Bibiki Bay umi2/s000: kwa1, kwa2, kwa3 with trailing delays 985, 1042, 642 (total 2669).
            byte[] payload = EffectRoutineBuilder.BuildRoutinePayload(2669, ("kwa1", 985, 498), ("kwa2", 1042, 598), ("kwa3", 642, 266));

            var routine = EffectRoutineDecoder.Decode(payload, "s000");

            Assert.NotNull(routine);
            Assert.Equal(2669, routine.TotalFrames);
            Assert.Equal(new[]
            {
                new EffectRoutineSpawn("kwa1", 0, 498),
                new EffectRoutineSpawn("kwa2", 985, 598),
                new EffectRoutineSpawn("kwa3", 2027, 266)
            }, routine.Spawns);
        }

        [Fact]
        public void Decode_KeepsSpawnsPastTheFirst128Commands()
        {
            // Pso'Xja's barrier routines (d_ga/effe/bari/s000) run 266 commands; the spawns past 128 must not be dropped.
            var entries = new (string, ushort, ushort)[265];
            for (int i = 0; i < entries.Length; i++) entries[i] = ($"g{i:D3}", 2, 10);
            byte[] payload = EffectRoutineBuilder.BuildRoutinePayload(0, entries);

            var routine = EffectRoutineDecoder.Decode(payload, "s000");

            Assert.NotNull(routine);
            Assert.Equal(265, routine.Spawns.Count);
            Assert.Equal(new EffectRoutineSpawn("g264", 528, 10), routine.Spawns[^1]);
            Assert.Equal(530, routine.TotalFrames);
        }

        [Fact]
        public void Decode_ThirdListOp01_LoopsOnComplete()
        {
            // Bibiki Bay umi2/s000's third list is a single op 0x01 (0x100 at +4): the routine starts on zone load.
            byte[] payload = EffectRoutineBuilder.BuildRoutinePayload(2669, ("kwa1", 985, 498));
            Assert.False(EffectRoutineDecoder.Decode(payload, "s000")!.StartsOnZoneLoad);

            byte[] looping = EffectRoutineBuilder.WithLoopOnComplete(payload);
            var routine = EffectRoutineDecoder.Decode(looping, "s000");
            Assert.NotNull(routine);
            Assert.True(routine.LoopsOnComplete);
            Assert.True(routine.StartsOnZoneLoad);
            Assert.Single(routine.Spawns);
        }

        [Fact]
        public void Decode_ReadsRoutineStartsAndTimedReplay()
        {
            // Alzadaal's portal 1pa2: ... 03 s104 at 300, 73 s103 / s102 at 300 (no third-list loop: on demand).
            var commands = new List<byte[]>
            {
                EffectRoutineBuilder.Command(0x02, 100, 0, "g0a1"),
                EffectRoutineBuilder.Command(0x02, 200, 0, "g0b1"),
                EffectRoutineBuilder.Command(0x03, 0, 0, "s104"),
                EffectRoutineBuilder.Command(0x73, 0, 0, "s103"),
            };
            var routine = EffectRoutineDecoder.Decode(EffectRoutineBuilder.BuildPayload(300, commands), "1pa2");
            Assert.NotNull(routine);
            Assert.False(routine.StartsOnZoneLoad);
            Assert.Equal(new[] { new EffectRoutineStart("s104", 300, false), new EffectRoutineStart("s103", 300, true) }, routine.Starts);

            // A bird routine (Ronfaure's mode/hato/s001): op 0x52 replays it on the client's own timer.
            var bird = EffectRoutineDecoder.Decode(EffectRoutineBuilder.BuildPayload(8500, new List<byte[]> { EffectRoutineBuilder.Command(0x52, 0, 0, ""), EffectRoutineBuilder.Command(0x02, 8500, 8400, "hato") }), "s001");
            Assert.True(bird!.HasTimedReplay);
            Assert.True(bird.StartsOnZoneLoad);
        }

        [Fact]
        public void Decode_ReadsTheTimedReplayWindow()
        {
            // Ronfaure's pigeons hato/s002 (#81): 22:30-23:54, longest interval 201,600 ms (84 Vana'diel minutes), shortest 10,800.
            var payload = EffectRoutineBuilder.BuildPayload(8500, new List<byte[]> { EffectRoutineBuilder.TimedReplayCommand(3240000, 3441600, 201600, 10800), EffectRoutineBuilder.Command(0x02, 8500, 8400, "hato") });
            var routine = EffectRoutineDecoder.Decode(payload, "s002");

            var window = Assert.Single(routine!.TimedReplays);
            Assert.Equal(new TimedReplayWindow(3240000, 3441600, 201600, 10800), window);
            Assert.Equal("22:30-23:54", window.ToString());
            Assert.True(window.Contains(23f / 24f));
            Assert.False(window.Contains(12f / 24f));
            Assert.Equal(8500, routine.TotalFrames);
            Assert.Equal(new EffectRoutineSpawn("hato", 0, 8400), Assert.Single(routine.Spawns));
        }

        [Fact]
        public void Decode_RejectsTruncatedPayload()
        {
            Assert.Null(EffectRoutineDecoder.Decode(new byte[0x10], "s000"));
        }

    }
}
