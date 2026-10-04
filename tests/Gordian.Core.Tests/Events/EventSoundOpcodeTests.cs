// tests/Gordian.Core.Tests/Events/EventSoundOpcodeTests.cs
using System;
using System.Linq;
using Gordian.Core.Events;
using Gordian.Core.Resources.Events;
using Gordian.Core.World;
using Xunit;

namespace Gordian.Core.Tests.Events
{
    /// <summary>Event music and sound volume opcodes 0x5C / 0x5D / 0x69 / 0x6A / 0x9A (#167).</summary>
    public class EventSoundOpcodeTests
    {
        private static readonly TimeSpan Frame = TimeSpan.FromSeconds(1.0 / 60);

        /// <summary>A work reference to immediate data <paramref name="index"/>.</summary>
        private static byte[] Ref(int index) => new[] { (byte)(0x8000 | index), (byte)((0x8000 | index) >> 8) };

        private static EventVm Make(byte[] code, RecordingHost host, params uint[] references)
        {
            var block = new EventBlock(0x010E6001, offsets: new ushort[] { 0 }, eventIds: new ushort[] { 100 }, references, code);
            return new EventVm(block, 100, new EventWorkZone(), host, 0x010E6001, 1);
        }

        [Fact]
        public void MusicSlotsAndVolumes_ReachTheHost()
        {
            // 5C 00 [track] ; 5C 83 [track] [vol] ; 5C A0 [vol] [time] ; 5D [vol] [time] ; 69 01 [mask] ; 6A [v] [t] [mask] ; 21
            byte[] code = new byte[] { 0x5C, 0x00 }.Concat(Ref(0))
                .Concat(new byte[] { 0x5C, 0x83 }).Concat(Ref(1)).Concat(Ref(2))
                .Concat(new byte[] { 0x5C, 0xA0 }).Concat(Ref(2)).Concat(Ref(3))
                .Concat(new byte[] { 0x5D }).Concat(Ref(4)).Concat(Ref(3))
                .Concat(new byte[] { 0x69, 0x01 }).Concat(Ref(5))
                .Concat(new byte[] { 0x6A }).Concat(Ref(6)).Concat(Ref(3)).Concat(Ref(5))
                .Concat(new byte[] { 0x21 }).ToArray();
            var host = new RecordingHost();
            EventVm vm = Make(code, host, 107, 220, 64, 30, 127, 0x05, 500);
            for (int i = 0; i < 10 && !vm.IsFinished; i++)
            {
                vm.Tick(Frame);
            }

            Assert.Equal(new[]
            {
                "slot 0=107", "slot 3=220", "music 64/30", "music 127/30", "sound 5=0/0", "sound 5=0.5/30",
            }, host.Sound);
            Assert.Empty(host.Skipped);
        }

        [Fact]
        public void Op9A_WaitsForTheMusic()
        {
            byte[] code = { 0x9A, 0x21 };
            var host = new RecordingHost { MusicReady = false };
            EventVm vm = Make(code, host);
            for (int i = 0; i < 5; i++)
            {
                vm.Tick(Frame);
            }

            Assert.False(vm.IsFinished);
            host.MusicReady = true;
            for (int i = 0; i < 3 && !vm.IsFinished; i++)
            {
                vm.Tick(Frame);
            }

            Assert.True(vm.IsFinished);
        }

        [Fact]
        public void MusicState_EventLayerIsDroppedWhenTheEventEnds()
        {
            var music = new ZoneMusicState();
            music.SetZoneTable(new ushort[] { 107, 108, 101, 103, 212 });
            music.SetEventSlot(0, 220);
            music.SetEventVolume(30, 40);
            Assert.Equal(220, music.Get(MusicSlot.ZoneDay));
            Assert.Equal(40, music.Volume);
            Assert.True(music.HasEventMusic);
            music.EndEvent();
            Assert.Equal(107, music.Get(MusicSlot.ZoneDay));
            Assert.Equal(ZoneMusicState.MaxVolume, music.Volume);
            Assert.False(music.HasEventMusic);
        }

        [Fact]
        public void EventSoundVolumes_SetByMaskAndReset()
        {
            var volumes = new EventSoundVolumes();
            volumes.Set(EventSoundCategory.Effect | EventSoundCategory.Zone, 0.25f, 60);
            Assert.Equal(0.25f, volumes.Get(EventSoundCategory.Effect));
            Assert.Equal(1f, volumes.Get(EventSoundCategory.System));
            Assert.Equal(0.25f, volumes.Get(EventSoundCategory.Zone));
            volumes.Reset();
            Assert.Equal(1f, volumes.Get(EventSoundCategory.Zone));
        }
    }
}
