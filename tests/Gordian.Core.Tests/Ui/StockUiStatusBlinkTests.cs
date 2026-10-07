// tests/Gordian.Core.Tests/Ui/StockUiStatusBlinkTests.cs
using System;
using System.Buffers.Binary;
using Gordian.Core.Network.Packets;
using Gordian.Core.Ui;
using Gordian.Core.World;
using Xunit;

namespace Gordian.Core.Tests.Ui
{
    /// <summary>Expiring status icons blink (#17): the 0x063 timers, matching them to the shown icons, and the fade.</summary>
    public class StockUiStatusBlinkTests
    {
        /// <summary>An S2C 0x063 type 0x09 payload as LandSandBoat sends it: icons from 4, end times from 68 (Earth s since the epoch x 60).</summary>
        private static LocalPlayerState PlayerWith(params (ushort Id, uint End)[] icons)
        {
            var p = new byte[4 + 192];
            BinaryPrimitives.WriteUInt16LittleEndian(p, S2C_0x063_MiscData.TypeStatusIcons);
            BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(2), 192);
            for (int i = 0; i < 32; i++)
            {
                BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(4 + i * 2), i < icons.Length ? icons[i].Id : (ushort)0xFF);
                BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(68 + i * 4), i < icons.Length ? icons[i].End : 0x7FFFFFFF);
            }
            var player = new LocalPlayerState();
            var misc = new S2C_0x063_MiscData(p);
            player.UpdateStatusIcons(in misc);
            return player;
        }

        private static uint EndAt(double earthSeconds) => unchecked((uint)(long)(earthSeconds * 60));

        [Fact]
        public void Timers_ReadEarthSecondsLeftAcrossTheOverflow()
        {
            // LandSandBoat: (seconds left + vanadiel_timestamp()) * 60, which overflows a u32 today (~7.8e8 s since 2002).
            double now = 780_000_000.25;
            var player = PlayerWith((40, EndAt(now + 25.25)), (33, 0x7FFFFFFF), (41, EndAt(now - 2)));
            var timers = player.GetStatusIconTimers(now);
            Assert.Equal(3, timers.Count);
            Assert.Equal((ushort)40, timers[0].Id);
            Assert.Equal(25.25, timers[0].RemainingSeconds!.Value, 2);
            Assert.Null(timers[1].RemainingSeconds);
            Assert.True(timers[2].RemainingSeconds < 0);
            Assert.Equal(25.25, player.GetStatusIconRemainingSeconds(0, now)!.Value, 2);
        }

        [Fact]
        public void MatchRemaining_PairsByOrderThenById()
        {
            var timers = new (ushort, double?)[] { (40, 100), (43, 10), (40, 5), (33, null) };
            // Same order: one to one.
            Assert.Equal(new double?[] { 100, 10, 5, null }, StockUiStatusBlink.MatchRemaining(new ushort[] { 40, 43, 40, 33 }, timers));
            // 0x037 arrived with Protect (43) gone: the second 40 still takes the second 40's timer.
            Assert.Equal(new double?[] { 100, 5, null }, StockUiStatusBlink.MatchRemaining(new ushort[] { 40, 40, 33 }, timers));
            // An icon 0x063 does not list yet has no timer.
            Assert.Equal(new double?[] { null, 100 }, StockUiStatusBlink.MatchRemaining(new ushort[] { 99, 40 }, timers));
        }

        [Fact]
        public void Opacity_FadesOnlyBelowTheThreshold()
        {
            Assert.Equal(1f, StockUiStatusBlink.Opacity(null, 0.5));
            Assert.Equal(1f, StockUiStatusBlink.Opacity(StockUiStatusBlink.BlinkThresholdSeconds + 1, 0.5));
            Assert.False(StockUiStatusBlink.IsBlinking(StockUiStatusBlink.BlinkThresholdSeconds));
            Assert.True(StockUiStatusBlink.IsBlinking(StockUiStatusBlink.BlinkThresholdSeconds - 0.1));
            double period = StockUiStatusBlink.BlinkPeriodSeconds;
            Assert.Equal(1f, StockUiStatusBlink.Opacity(5, 0), 3);
            Assert.Equal(StockUiStatusBlink.MinimumOpacity, StockUiStatusBlink.Opacity(5, period / 2), 3);
            Assert.Equal(1f, StockUiStatusBlink.Opacity(5, period), 3);
            float quarter = StockUiStatusBlink.Opacity(5, period / 4);
            Assert.InRange(quarter, StockUiStatusBlink.MinimumOpacity + 0.1f, 0.9f);
        }
    }
}
