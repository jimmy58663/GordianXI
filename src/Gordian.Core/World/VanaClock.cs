// src/Gordian.Core/World/VanaClock.cs
using System;
using System.Threading;

namespace Gordian.Core.World
{
    /// <summary>
    /// One session's Vana'diel clock: the local clock plus the offset to that session's server clock, set from the
    /// game time in its S2C 0x00A login ack. Each <see cref="WorldState"/> owns one (<see cref="WorldState.Clock"/>), so
    /// sessions connected to different servers in one process keep their own time of day, day of week, moon and
    /// transport timebase. The calendar maths are the pure helpers on <see cref="VanaTime"/>.
    /// Protocol specification referenced from LandSandBoat (https://github.com/LandSandBoat/server)
    /// and XiPackets (https://github.com/atom0s/XiPackets).
    /// </summary>
    /// <remarks>
    /// Written from the network thread and read from the render, audio and input threads: the offset is a single
    /// 64-bit value read and written atomically. Reads allocate nothing.
    /// </remarks>
    public sealed class VanaClock
    {
        private long _serverClockOffsetSeconds;

        /// <summary>The offset from the local clock to this session's server clock, in Earth seconds (0 until synchronized).</summary>
        public long ServerClockOffsetSeconds => Volatile.Read(ref _serverClockOffsetSeconds);

        /// <summary>
        /// Synchronizes this clock with the server's game time from S2C 0x00A (Earth seconds since the Vana'diel
        /// epoch), received now. A zero game time is ignored.
        /// </summary>
        public void SynchronizeServerTime(uint serverGameTime) => SynchronizeServerTime(serverGameTime, DateTime.UtcNow);

        /// <summary>As <see cref="SynchronizeServerTime(uint)"/>, received at <paramref name="utcNow"/>.</summary>
        public void SynchronizeServerTime(uint serverGameTime, DateTime utcNow)
        {
            if (serverGameTime == 0) return;
            Volatile.Write(ref _serverClockOffsetSeconds, VanaTime.ComputeServerClockOffset(serverGameTime, utcNow));
        }

        /// <summary>Sets the server clock offset directly (Earth seconds).</summary>
        public void SetServerClockOffset(long offsetSeconds) => Volatile.Write(ref _serverClockOffsetSeconds, offsetSeconds);

        /// <summary>Resets the offset to zero (the local clock).</summary>
        public void Reset() => Volatile.Write(ref _serverClockOffsetSeconds, 0);

        /// <summary>Total Vana'diel seconds since the epoch on this session's server clock.</summary>
        public long GetVanadielSeconds(DateTime utcTime) => VanaTime.GetVanadielSeconds(utcTime, ServerClockOffsetSeconds);

        /// <summary>
        /// Earth seconds (fractional) since the Vana'diel epoch on this session's server clock: the timebase of
        /// transport legs (elevators, ships) in entity updates.
        /// </summary>
        public double GetEarthSecondsSinceEpoch(DateTime utcTime) => VanaTime.GetEarthSecondsSinceEpoch(utcTime, ServerClockOffsetSeconds);

        /// <summary>The Vana'diel time of day in hours [0, 24) on this session's server clock.</summary>
        public float GetTimeOfDayHours(DateTime utcTime) => VanaTime.GetTimeOfDayHours(utcTime, ServerClockOffsetSeconds);

        /// <summary>The moon phase percentage (0 to 100), see <see cref="VanaTime.GetMoonPhase(DateTime)"/>.</summary>
        public int GetMoonPhase(DateTime utcTime) => VanaTime.GetMoonPhase(utcTime, ServerClockOffsetSeconds);

        /// <summary>The moon direction (0 neither, 1 waning, 2 waxing), see <see cref="VanaTime.GetMoonDirection(DateTime)"/>.</summary>
        public int GetMoonDirection(DateTime utcTime) => VanaTime.GetMoonDirection(utcTime, ServerClockOffsetSeconds);

        /// <summary>The day of the week (0 Firesday .. 7 Darksday), see <see cref="VanaTime.GetDayOfWeekIndex(DateTime)"/>.</summary>
        public int GetDayOfWeekIndex(DateTime utcTime) => VanaTime.GetDayOfWeekIndex(utcTime, ServerClockOffsetSeconds);

        /// <summary>The 12-step moon phase index (0 new .. 6 full), see <see cref="VanaTime.GetMoonPhaseIndex(DateTime)"/>.</summary>
        public int GetMoonPhaseIndex(DateTime utcTime) => VanaTime.GetMoonPhaseIndex(utcTime, ServerClockOffsetSeconds);
    }
}
