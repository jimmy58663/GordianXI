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
    }
}
