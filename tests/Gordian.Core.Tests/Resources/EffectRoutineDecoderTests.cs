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
            byte[] payload = BuildRoutinePayload(2669, ("kwa1", 985, 498), ("kwa2", 1042, 598), ("kwa3", 642, 266));

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
            byte[] payload = BuildRoutinePayload(0, entries);

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
            byte[] payload = BuildRoutinePayload(2669, ("kwa1", 985, 498));
            Assert.False(EffectRoutineDecoder.Decode(payload, "s000")!.StartsOnZoneLoad);

            byte[] looping = WithLoopOnComplete(payload);
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
                Command(0x02, 100, 0, "g0a1"),
                Command(0x02, 200, 0, "g0b1"),
                Command(0x03, 0, 0, "s104"),
                Command(0x73, 0, 0, "s103"),
            };
            var routine = EffectRoutineDecoder.Decode(BuildPayload(300, commands), "1pa2");
            Assert.NotNull(routine);
            Assert.False(routine.StartsOnZoneLoad);
            Assert.Equal(new[] { new EffectRoutineStart("s104", 300, false), new EffectRoutineStart("s103", 300, true) }, routine.Starts);

            // A bird routine (Ronfaure's mode/hato/s001): op 0x52 replays it on the client's own timer.
            var bird = EffectRoutineDecoder.Decode(BuildPayload(8500, new List<byte[]> { Command(0x52, 0, 0, ""), Command(0x02, 8500, 8400, "hato") }), "s001");
            Assert.True(bird!.HasTimedReplay);
            Assert.True(bird.StartsOnZoneLoad);
        }

        [Fact]
        public void Decode_RejectsTruncatedPayload()
        {
            Assert.Null(EffectRoutineDecoder.Decode(new byte[0x10], "s000"));
        }

        /// <summary>
        /// Builds a routine payload: header with the command list at +0x40 (section-relative 0x50) and the total at +0x1C,
        /// a leading 0x01 command, one 0x02 spawn per entry, then 0x00 end.
        /// </summary>
        internal static byte[] BuildRoutinePayload(int totalFrames, params (string Id, ushort Delay, ushort Duration)[] spawns)
        {
            var bytes = new List<byte>(new byte[0x40]);
            var header = new byte[0x40];
            BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(0x10), 0x40);
            BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(0x14), 0x50);
            BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(0x1C), totalFrames);
            bytes = new List<byte>(header);

            bytes.AddRange(new byte[] { 0x01, 0x02, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 });
            foreach (var (id, delay, duration) in spawns)
            {
                var cmd = new byte[16];
                cmd[0] = 0x02;
                cmd[1] = 0x04;
                BinaryPrimitives.WriteUInt16LittleEndian(cmd.AsSpan(4), delay);
                BinaryPrimitives.WriteUInt16LittleEndian(cmd.AsSpan(6), duration);
                Encoding.ASCII.GetBytes(id).CopyTo(cmd, 8);
                bytes.AddRange(cmd);
            }
            bytes.AddRange(new byte[] { 0x00, 0x02, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 });
            return bytes.ToArray();
        }

        /// <summary>Appends a third command list holding <c>01</c> (0x100 at +4) and points header +0x18 at it.</summary>
        internal static byte[] WithLoopOnComplete(byte[] payload)
        {
            var bytes = new List<byte>(payload);
            int offset = bytes.Count + 16;
            bytes.AddRange(new byte[] { 0x01, 0x02, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00 });
            var result = bytes.ToArray();
            BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(0x18), offset);
            return result;
        }

        /// <summary>A three-dword command: op, size 3, delay, duration, FourCC.</summary>
        internal static byte[] Command(byte op, ushort delay, ushort duration, string reference)
        {
            var cmd = new byte[12];
            cmd[0] = op;
            cmd[1] = 0x03;
            BinaryPrimitives.WriteUInt16LittleEndian(cmd.AsSpan(4), delay);
            BinaryPrimitives.WriteUInt16LittleEndian(cmd.AsSpan(6), duration);
            Encoding.ASCII.GetBytes(reference).CopyTo(cmd, 8);
            return cmd;
        }

        /// <summary>A routine payload with the command list at section +0x50 (after a leading 0x01) and no third list.</summary>
        internal static byte[] BuildPayload(int totalFrames, List<byte[]> commands)
        {
            var header = new byte[0x40];
            BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(0x10), 0x40);
            BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(0x14), 0x50);
            BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(0x1C), totalFrames);
            var bytes = new List<byte>(header);
            bytes.AddRange(new byte[] { 0x01, 0x02, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 });
            foreach (var cmd in commands) bytes.AddRange(cmd);
            bytes.AddRange(new byte[] { 0x00, 0x02, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 });
            return bytes.ToArray();
        }
    }
}
