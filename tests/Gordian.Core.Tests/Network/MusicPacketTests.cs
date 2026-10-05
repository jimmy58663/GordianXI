// tests/Gordian.Core.Tests/Network/MusicPacketTests.cs
using System;
using System.Buffers.Binary;
using System.Threading.Tasks;
using Gordian.Core.Config;
using Gordian.Core.Network.Packets;
using Gordian.Core.World;
using Xunit;

namespace Gordian.Core.Tests.Network
{
    /// <summary>S2C 0x05F / 0x060 and the 0x00A <c>MusicNum[5]</c> table (#114).</summary>
    public class MusicPacketTests
    {
        private static byte[] Payload(ushort a, ushort b)
        {
            var p = new byte[4];
            BinaryPrimitives.WriteUInt16LittleEndian(p, a);
            BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(2), b);
            return p;
        }

        [Fact]
        public void Music_DecodesSlotAndTrack()
        {
            var packet = new S2C_0x05F_Music(Payload(2, 101));
            Assert.True(packet.IsValid);
            Assert.Equal(2, packet.Slot);
            Assert.Equal(101, packet.MusicNum);
            Assert.False(new S2C_0x05F_Music(new byte[3]).IsValid);
        }

        [Fact]
        public void MusicVolume_DecodesTimeAndVolume()
        {
            var packet = new S2C_0x060_MusicVolume(Payload(60, 64));
            Assert.True(packet.IsValid);
            Assert.Equal(60, packet.Time);
            Assert.Equal(64, packet.Volume);
        }

        [Fact]
        public void Module_WritesTheMusicState()
        {
            var music = new ZoneMusicState();
            var dispatcher = new PacketDispatcher();
            new MusicPacketModule(music).Register(dispatcher);
            int changes = 0;
            music.Changed += () => changes++;

            dispatcher.Dispatch(new PacketHeader(0x05F, 8, 0), Payload(3, 219));
            dispatcher.Dispatch(new PacketHeader(0x060, 8, 0), Payload(30, 200));

            Assert.Equal(219, music.Get(MusicSlot.BattleParty));
            Assert.Equal(ZoneMusicState.MaxVolume, music.Volume); // clamped
            Assert.Equal(30, music.VolumeFadeTime);
            Assert.Equal(2, changes);
        }

        [Fact]
        public void LoginAck_MusicTableFillsSlotsZeroToFour()
        {
            byte[] payload = new byte[128];
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(44), 230); // zone
            ushort[] table = { 107, 108, 101, 103, 212 };
            for (int i = 0; i < 5; i++)
            {
                BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(0x52 + i * 2), table[i]);
            }

            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(92), 0xFFFF); // SubMapNumber must not leak in

            var module = new LifecyclePacketModule(new SessionProfile(), (m, h) => Task.CompletedTask) { LogOutboundOnRoute = false };
            var dispatcher = new PacketDispatcher();
            module.Register(dispatcher);
            var music = new ZoneMusicState();
            music.SetSlot((int)MusicSlot.Fishing, 55);
            ZoneLoginInfo? received = null;
            module.ZoneLoginInfoReceived += info =>
            {
                received = info;
                music.SetZoneTable(stackalloc ushort[] { info.MusicDay, info.MusicNight, info.MusicBattleSolo, info.MusicBattleParty, info.MusicMount });
            };
            dispatcher.Dispatch(new PacketHeader(0x00A, 132, 0), payload);

            Assert.NotNull(received);
            Assert.Equal(table, new[] { received.Value.MusicDay, received.Value.MusicNight, received.Value.MusicBattleSolo, received.Value.MusicBattleParty, received.Value.MusicMount });
            Assert.Equal(107, music.Get(MusicSlot.ZoneDay));
            Assert.Equal(108, music.Get(MusicSlot.ZoneNight));
            Assert.Equal(212, music.Get(MusicSlot.Mount));
            Assert.Equal(55, music.Get(MusicSlot.Fishing));
        }
    }
}
