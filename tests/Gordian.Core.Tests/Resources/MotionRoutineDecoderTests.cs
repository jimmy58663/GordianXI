// tests/Gordian.Core.Tests/Resources/MotionRoutineDecoderTests.cs
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;
using Gordian.Core.Resources.Graphics;
using Gordian.Core.Resources.Models;
using Xunit;

namespace Gordian.Core.Tests.Resources
{
    public class MotionRoutineDecoderTests
    {
        /// <summary>Builds a Section 0x07 payload (after the 16-byte section header) from encoded commands.</summary>
        internal static byte[] Routine(int totalTicks, params byte[][] commands)
        {
            var bytes = new List<byte>(new byte[0x20]);
            foreach (var command in commands) bytes.AddRange(command);
            bytes.AddRange(new byte[8]); // op 0x00 ends the list
            var payload = bytes.ToArray();
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(0x14), 0x20 + 16);
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(0x1C), totalTicks);
            return payload;
        }

        private static byte[] Command(byte op, int dwords, int delay, int duration, string reference = "")
        {
            var command = new byte[dwords * 4];
            command[0] = op;
            BinaryPrimitives.WriteUInt16LittleEndian(command.AsSpan(1), (ushort)dwords);
            BinaryPrimitives.WriteUInt16LittleEndian(command.AsSpan(4), (ushort)delay);
            BinaryPrimitives.WriteUInt16LittleEndian(command.AsSpan(6), (ushort)duration);
            if (reference.Length > 0 && dwords >= 3) Encoding.ASCII.GetBytes(reference).CopyTo(command, 8);
            return command;
        }

        internal static byte[] PlayClip(string reference, int delay, int duration, int blendIn, int blendOut, int loops, float speed = 1f)
        {
            var command = Command(0x05, 8, delay, duration, reference);
            BinaryPrimitives.WriteSingleLittleEndian(command.AsSpan(0x10), speed);
            BinaryPrimitives.WriteSingleLittleEndian(command.AsSpan(0x14), 1f);
            BinaryPrimitives.WriteUInt16LittleEndian(command.AsSpan(0x18), (ushort)blendIn);
            BinaryPrimitives.WriteUInt16LittleEndian(command.AsSpan(0x1C), (ushort)blendOut);
            BinaryPrimitives.WriteUInt16LittleEndian(command.AsSpan(0x1E), (ushort)loops);
            return command;
        }

        internal static byte[] Link(byte op, string reference, int delay) => Command(op, 4, delay, 0, reference);

        internal static byte[] Flinch(int ticks)
        {
            var command = Command(0x21, 9, 2, 0);
            BinaryPrimitives.WriteSingleLittleEndian(command.AsSpan(0x18), ticks);
            return command;
        }

        internal static byte[] PoseFlash(int pose, int ticks)
        {
            var command = Command(0x5A, 7, 0, 0);
            command[0x0C] = 4;
            BinaryPrimitives.WriteUInt16LittleEndian(command.AsSpan(0x0E), (ushort)pose);
            BinaryPrimitives.WriteSingleLittleEndian(command.AsSpan(0x10), 1f);
            BinaryPrimitives.WriteSingleLittleEndian(command.AsSpan(0x14), ticks);
            return command;
        }

        [Fact]
        public void Decode_ReadsClipBlendLoopsAndTiming()
        {
            // The PC swing ati0 shape (ROM/32/13): a lock, the clip at0? held for the routine, the hit link at 40.
            var payload = Routine(98,
                Command(0x01, 2, 0, 0),
                PlayClip("at0?", 34, 98, 12, 20, 1),
                Link(0x57, "skaz", 6),
                Link(0x03, "dada", 58));

            var raw = MotionRoutineDecoder.Decode(payload, "ati0");

            Assert.NotNull(raw);
            Assert.Equal(98, raw!.TotalTicks);
            var clip = raw.Commands[0];
            Assert.Equal(0x05, clip.Op);
            Assert.Equal("at0?", clip.Reference);
            Assert.Equal(0, clip.StartTick);
            Assert.Equal(98, clip.Duration);
            Assert.Equal(12, clip.BlendInTicks);
            Assert.Equal(20, clip.BlendOutTicks);
            Assert.Equal(1, clip.Loops);
            Assert.Equal("dada", raw.Commands[2].Reference);
            Assert.Equal(40, raw.Commands[2].StartTick);
        }

        [Fact]
        public void Build_StripsWildcardAndFindsHitTick()
        {
            var routines = new Dictionary<string, RawMotionRoutine>
            {
                ["ati0"] = MotionRoutineDecoder.Decode(Routine(98, PlayClip("at0?", 34, 98, 12, 20, 1), Link(0x03, "dada", 58)), "ati0")!
            };

            var built = MotionRoutineDecoder.Build(routines, "ati0", clip => clip == "at0");

            Assert.NotNull(built);
            var segment = Assert.Single(built!.Segments);
            Assert.Equal("at0", segment.ClipName);
            Assert.Equal(new[] { 34 }, built.HitTicks);
            Assert.False(built.IsSustained);
        }

        [Fact]
        public void Build_FollowsLinksAndOffsetsTheChildsClips()
        {
            // The monster cast release shwh links shot, which plays ma1? then ma2? (ROM model 1600).
            var routines = new Dictionary<string, RawMotionRoutine>
            {
                ["shwh"] = MotionRoutineDecoder.Decode(Routine(1, Link(0x03, "stwh", 0), Link(0x03, "shot", 1), Link(0x3B, "wash", 0)), "shwh")!,
                ["shot"] = MotionRoutineDecoder.Decode(Routine(180, PlayClip("ma1?", 0, 28, 20, 20, 1), Link(0x03, "mloc", 28), PlayClip("ma2?", 152, 48, 10, 20, 4)), "shot")!
            };

            var built = MotionRoutineDecoder.Build(routines, "shwh", clip => clip is "ma1" or "ma2");

            Assert.NotNull(built);
            Assert.Collection(built!.Segments,
                s => { Assert.Equal("ma1", s.ClipName); Assert.Equal(0, s.StartTick); },
                s => { Assert.Equal("ma2", s.ClipName); Assert.Equal(28, s.StartTick); Assert.Equal(4, s.Loops); });
            Assert.Equal(180, built.TotalTicks);
        }

        [Fact]
        public void Build_LongLoopIsSustained()
        {
            // The PC black magic chant cabk: mb0? looping 63 times.
            var routines = new Dictionary<string, RawMotionRoutine>
            {
                ["cabk"] = MotionRoutineDecoder.Decode(Routine(33, PlayClip("mb0?", 0, 33, 16, 10, 63), Link(0x03, "ner1", 33)), "cabk")!
            };

            var built = MotionRoutineDecoder.Build(routines, "cabk", _ => true);

            Assert.True(built!.IsSustained);
        }

        [Fact]
        public void Build_ReadsFlinchAndPoseFlash()
        {
            var routines = new Dictionary<string, RawMotionRoutine>
            {
                ["damg"] = MotionRoutineDecoder.Decode(Routine(2, Link(0x57, "sdam", 0), Flinch(24)), "damg")!,
                ["pary"] = MotionRoutineDecoder.Decode(Routine(0, PoseFlash(1, 16)), "pary")!
            };

            var all = MotionRoutineDecoder.BuildAll(routines, _ => true);

            Assert.Equal(24, all["damg"].FlinchTicks);
            Assert.Equal(1, all["pary"].PoseFlashIndex);
            Assert.Equal(16, all["pary"].PoseFlashTicks);
        }

        [Fact]
        public void Build_SkipsClipsTheModelLacksAndRoutinesWithNothingToPlay()
        {
            var routines = new Dictionary<string, RawMotionRoutine>
            {
                ["init"] = MotionRoutineDecoder.Decode(Routine(0, Link(0x03, "hwpc", 0)), "init")!,
                ["ati0"] = MotionRoutineDecoder.Decode(Routine(98, PlayClip("at0?", 0, 98, 12, 20, 1)), "ati0")!
            };

            var all = MotionRoutineDecoder.BuildAll(routines, _ => false);

            Assert.Empty(all);
        }

        [Fact]
        public void Decode_RejectsShortPayloadAndBadOffset()
        {
            Assert.Null(MotionRoutineDecoder.Decode(new byte[8], "x"));
            var payload = new byte[0x20];
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(0x14), 0x4000);
            Assert.Null(MotionRoutineDecoder.Decode(payload, "x"));
        }
    }
}
