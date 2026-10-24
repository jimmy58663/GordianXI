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
        [Fact]
        public void Decode_ReadsClipBlendLoopsAndTiming()
        {
            // The PC swing ati0 shape (ROM/32/13): a lock, the clip at0? held for the routine, the hit link at 40.
            var payload = MotionRoutineBuilder.Routine(98,
                MotionRoutineBuilder.Command(0x01, 2, 0, 0),
                MotionRoutineBuilder.PlayClip("at0?", 34, 98, 12, 20, 1),
                MotionRoutineBuilder.Link(0x57, "skaz", 6),
                MotionRoutineBuilder.Link(0x03, "dada", 58));

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
                ["ati0"] = MotionRoutineDecoder.Decode(MotionRoutineBuilder.Routine(98, MotionRoutineBuilder.PlayClip("at0?", 34, 98, 12, 20, 1), MotionRoutineBuilder.Link(0x03, "dada", 58)), "ati0")!
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
                ["shwh"] = MotionRoutineDecoder.Decode(MotionRoutineBuilder.Routine(1, MotionRoutineBuilder.Link(0x03, "stwh", 0), MotionRoutineBuilder.Link(0x03, "shot", 1), MotionRoutineBuilder.Link(0x3B, "wash", 0)), "shwh")!,
                ["shot"] = MotionRoutineDecoder.Decode(MotionRoutineBuilder.Routine(180, MotionRoutineBuilder.PlayClip("ma1?", 0, 28, 20, 20, 1), MotionRoutineBuilder.Link(0x03, "mloc", 28), MotionRoutineBuilder.PlayClip("ma2?", 152, 48, 10, 20, 4)), "shot")!
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
                ["cabk"] = MotionRoutineDecoder.Decode(MotionRoutineBuilder.Routine(33, MotionRoutineBuilder.PlayClip("mb0?", 0, 33, 16, 10, 63), MotionRoutineBuilder.Link(0x03, "ner1", 33)), "cabk")!
            };

            var built = MotionRoutineDecoder.Build(routines, "cabk", _ => true);

            Assert.True(built!.IsSustained);
        }

        [Fact]
        public void Build_ReadsFlinchAndPoseFlash()
        {
            var routines = new Dictionary<string, RawMotionRoutine>
            {
                ["damg"] = MotionRoutineDecoder.Decode(MotionRoutineBuilder.Routine(2, MotionRoutineBuilder.Link(0x57, "sdam", 0), MotionRoutineBuilder.Flinch(24)), "damg")!,
                ["pary"] = MotionRoutineDecoder.Decode(MotionRoutineBuilder.Routine(0, MotionRoutineBuilder.PoseFlash(1, 16)), "pary")!
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
                ["init"] = MotionRoutineDecoder.Decode(MotionRoutineBuilder.Routine(0, MotionRoutineBuilder.Link(0x03, "hwpc", 0)), "init")!,
                ["ati0"] = MotionRoutineDecoder.Decode(MotionRoutineBuilder.Routine(98, MotionRoutineBuilder.PlayClip("at0?", 0, 98, 12, 20, 1)), "ati0")!
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
