// tests/Gordian.Core.Tests/World/StatusPagesStateTests.cs
using System.Buffers.Binary;
using Gordian.Core.Network.Packets;
using Gordian.Core.World;
using Xunit;

namespace Gordian.Core.Tests.World
{
    /// <summary>The state the Status window's Master Levels and Play Time read (#487).</summary>
    public class StatusPagesStateTests
    {
        [Fact]
        public void JobInfo_StoresMasterLevelsAndMasteryFlags()
        {
            var p = new byte[128];
            BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(100), 1u << 18);
            p[104 + 18] = 12;
            var player = new LocalPlayerState();
            player.UpdateFromJobInfo(new S2C_0x01B_JobInfo(p));
            Assert.Equal(12, player.MasterLevels[18]);
            Assert.Equal(0, player.MasterLevels[1]);
            Assert.Equal(1u << 18, player.JobMasteryFlags);
        }

        [Fact]
        public void PlayTime_IsTheZoneInTotalPlusTheTimeSince()
        {
            var world = new WorldState();
            Assert.Null(world.PlayTimeSecondsNow);
            world.UpdateZoneLoginInfo(default(ZoneLoginInfo) with { PlayTime = 1000 });
            long? now = world.PlayTimeSecondsNow;
            Assert.NotNull(now);
            Assert.InRange(now!.Value, 1000, 1005);
        }
    }
}
