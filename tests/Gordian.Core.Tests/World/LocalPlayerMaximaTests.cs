// tests/Gordian.Core.Tests/World/LocalPlayerMaximaTests.cs
using System;
using System.Buffers.Binary;
using Gordian.Core.Network.Packets;
using Gordian.Core.World;
using Xunit;

namespace Gordian.Core.Tests.World
{
    /// <summary>
    /// Max HP / MP follow every packet that implies them (the maintainer's round 3, 2026-10-12: after !godmode the 9999
    /// maxima stayed until zoning). LandSandBoat sends S2C 0x061 with the maxima on job, gear and merit changes, but a
    /// status effect's change of the maxima only flags UPDATE_HP: S2C 0x0DF with the values and percentages.
    /// </summary>
    public class LocalPlayerMaximaTests
    {
        private static S2C_0x061_CliStatus CliStatus(int maxHp, int maxMp)
        {
            var payload = new byte[0x6C];
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(0, 4), maxHp);
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(4, 4), maxMp);
            payload[8] = (byte)JobId.Warrior;
            payload[9] = 99;
            return new S2C_0x061_CliStatus(payload);
        }

        private static byte[] GroupAttr(uint id, uint hp, uint mp, byte hpp, byte mpp)
        {
            var payload = new byte[36];
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(0, 4), id);
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(4, 4), hp);
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(8, 4), mp);
            payload[18] = hpp;
            payload[19] = mpp;
            return payload;
        }

        [Fact]
        public void GroupAttrWithLowerMaxima_LowersMaxHpAndMp()
        {
            var player = new LocalPlayerState { ServerId = 0x01020304 };
            player.UpdateFromCliStatus(CliStatus(9999, 9999)); // !godmode
            player.UpdateFromGroupAttr(new S2C_0x0DF_GroupAttr(GroupAttr(0x01020304, 9999, 9999, 100, 100)));
            Assert.Equal((9999, 9999), (player.MaxHp, player.MaxMp));

            // !godmode off: LandSandBoat clamps HP / MP to the new maxima and sends only 0x0DF (full: 100%).
            player.UpdateFromGroupAttr(new S2C_0x0DF_GroupAttr(GroupAttr(0x01020304, 1450, 620, 100, 100)));
            Assert.Equal((1450, 620), (player.MaxHp, player.MaxMp));

            // A later hit keeps the maxima (the percentage agrees), and a full 0x061 still sets them exactly.
            player.UpdateFromGroupAttr(new S2C_0x0DF_GroupAttr(GroupAttr(0x01020304, 725, 620, 50, 100)));
            Assert.Equal((1450, 620), (player.MaxHp, player.MaxMp));
            player.UpdateFromCliStatus(CliStatus(1400, 600));
            Assert.Equal((1400, 600), (player.MaxHp, player.MaxMp));
        }

        [Theory]
        [InlineData(9999, 1450, 100, 1450)]  // full after the drop: exact
        [InlineData(9999, 700, 50, 1400)]     // not full: the largest max that floors to 50% (LSB floors)
        [InlineData(1450, 725, 50, 1450)]     // agrees: kept
        [InlineData(1450, 1450, 100, 1450)]
        [InlineData(1450, 3, 1, 1450)]        // 1% also means less than 1%: kept
        [InlineData(1450, 0, 0, 1450)]        // dead or unknown: kept
        [InlineData(100, 300, 100, 300)]      // a raised maximum
        public void ReconcileMax_FollowsLandSandBoatsFlooredPercentage(int max, int current, byte percent, int expected)
        {
            Assert.Equal(expected, LocalPlayerState.ReconcileMax(max, current, percent));
            int result = LocalPlayerState.ReconcileMax(max, current, percent);
            if (current > 0 && percent > 1) Assert.Equal(percent, Math.Max(1, (int)Math.Floor(current * 100.0 / result)));
        }
    }
}
