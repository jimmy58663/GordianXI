// tests/Gordian.Core.Tests/Network/ServerSilenceWatchdogTests.cs
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using Gordian.Core.Network;
using Gordian.Core.Network.Compression;
using Gordian.Core.Network.Packets;
using Xunit;

namespace Gordian.Core.Tests.Network
{
    /// <summary>
    /// A dropped map session (#235): the connection-lost indicator after 10 s without a server datagram, and the session
    /// ending itself (LogoutState Timeout) after 60 s, each session on its own clock.
    /// </summary>
    public sealed class ServerSilenceWatchdogTests
    {
        /// <summary>A clock the test moves by hand.</summary>
        private sealed class ManualTimeProvider : TimeProvider
        {
            private long _ticks = 1_000_000;
            public override long TimestampFrequency => TimeSpan.TicksPerSecond;
            public override long GetTimestamp() => _ticks;
            public override DateTimeOffset GetUtcNow() => new DateTimeOffset(_ticks, TimeSpan.Zero);
            public void Advance(TimeSpan by) => _ticks += by.Ticks;
        }

        private sealed class Fixture
        {
            public ManualTimeProvider Clock { get; }
            public SessionNetworkManager Net { get; }
            public List<SessionLogout> Logouts { get; } = new();
            public List<bool> LostChanges { get; } = new();
            public List<ushort> Sent { get; } = new();
            private ushort _serverSeq;

            public Fixture(ManualTimeProvider? clock = null)
            {
                Clock = clock ?? new ManualTimeProvider();
                Net = new SessionNetworkManager("127.0.0.1", 54230, timeProvider: Clock)
                {
                    OutboundChunkOverride = (data, _) =>
                    {
                        lock (Sent) Sent.Add((ushort)(BinaryPrimitives.ReadUInt16LittleEndian(data.Span) & 0x1FF));
                        return Task.CompletedTask;
                    }
                };
                Net.LoggedOut += l => Logouts.Add(l);
                Net.ConnectionLostChanged += lost => LostChanges.Add(lost);
                Net.CurrentState = SessionState.ActiveInWorld;
            }

            public void Wait(double seconds)
            {
                Clock.Advance(TimeSpan.FromSeconds(seconds));
                Net.CheckServerSilence();
            }

            /// <summary>
            /// Feeds a datagram the parser accepts: the session has no key yet, so it is read unencrypted (28-byte header,
            /// compressed sub-packets, 16-byte trailer). It carries one 4-byte sub-packet of an unused id.
            /// </summary>
            public bool ReceiveServerDatagram()
            {
                byte[] sub = new byte[4];
                BinaryPrimitives.WriteUInt16LittleEndian(sub, (ushort)(0x1F0 | (1 << 9)));
                byte[] compressed = new byte[256];
                int length = FfxiCodec.Default.Compress(sub, compressed);
                byte[] datagram = new byte[28 + length + 16];
                BinaryPrimitives.WriteUInt16LittleEndian(datagram, ++_serverSeq);
                compressed.AsSpan(0, length).CopyTo(datagram.AsSpan(28));
                return Net.ProcessInboundDatagram(datagram);
            }
        }

        [Fact]
        public void Thresholds_MatchTheServerSideLinkDeadAndSessionTimeout()
        {
            Assert.Equal(TimeSpan.FromSeconds(10), SessionNetworkManager.DefaultConnectionLostAfter);
            Assert.Equal(TimeSpan.FromSeconds(60), SessionNetworkManager.DefaultServerSilenceTimeout);
        }

        [Fact]
        public void TimeSinceLastServerPacket_CountsFromTheSessionComingUp()
        {
            var f = new Fixture();
            f.Clock.Advance(TimeSpan.FromSeconds(3));
            Assert.Equal(TimeSpan.FromSeconds(3), f.Net.TimeSinceLastServerPacket);
        }

        [Fact]
        public void ConnectionLost_TurnsOnAfterTenSecondsOfSilence()
        {
            var f = new Fixture();
            f.Wait(9.9);
            Assert.False(f.Net.IsConnectionLost);
            Assert.Empty(f.LostChanges);

            f.Wait(0.1);
            Assert.True(f.Net.IsConnectionLost);
            Assert.Equal(new[] { true }, f.LostChanges);

            f.Wait(1);
            Assert.Equal(new[] { true }, f.LostChanges); // raised once
            Assert.Empty(f.Logouts);
        }

        [Fact]
        public void ServerDatagram_ClearsTheIndicatorAndRestartsTheClock()
        {
            var f = new Fixture();
            f.Wait(15);
            Assert.True(f.Net.IsConnectionLost);

            Assert.True(f.ReceiveServerDatagram());
            Assert.False(f.Net.IsConnectionLost);
            Assert.Equal(new[] { true, false }, f.LostChanges);
            Assert.Equal(TimeSpan.Zero, f.Net.TimeSinceLastServerPacket);

            f.Wait(50);
            Assert.Empty(f.Logouts); // 65 s since the session came up, 50 s since the server last answered
        }

        [Fact]
        public void DatagramThatDoesNotParse_DoesNotCountAsServerTraffic()
        {
            var f = new Fixture();
            f.Wait(11);
            Assert.False(f.Net.ProcessInboundDatagram(new byte[20]));
            Assert.True(f.Net.IsConnectionLost);
            Assert.Equal(TimeSpan.FromSeconds(11), f.Net.TimeSinceLastServerPacket);
        }

        [Fact]
        public async Task SixtySecondsOfSilence_EndsTheSessionAsATimeout()
        {
            var f = new Fixture();
            f.Wait(59.9);
            Assert.Empty(f.Logouts);

            f.Wait(0.1);
            var logout = Assert.Single(f.Logouts);
            Assert.Equal(LogoutState.Timeout, logout.State);
            Assert.True(logout.ReturnsToLobby); // retail tries to reconnect to the lobby
            Assert.False(logout.IsShutdown);
            Assert.True(f.Net.SessionEnding);
            Assert.False(f.Net.IsConnectionLost);
            Assert.Equal(SessionState.Disconnected, f.Net.CurrentState);

            // Nothing more is sent, and the session ends once.
            await f.Net.QueueChunkAsync(LifecycleOutboundPackets.BuildPos(sequenceId: 0, x: 1f, y: 2f, z: 3f, dir: 0, targetIndex: 0, moveFrame: 1));
            Assert.Empty(f.Sent);
            f.Wait(60);
            Assert.Single(f.Logouts);
        }

        [Fact]
        public async Task PendingLogoutKind_IsCarriedOnTheTimeout()
        {
            var f = new Fixture();
            await f.Net.RequestLogoutAsync(shutdown: false);
            f.Wait(60);
            var logout = Assert.Single(f.Logouts);
            Assert.Equal(ReqLogoutKind.Logout, logout.RequestedKind);
            Assert.True(logout.ReturnsToLobby);
        }

        [Fact]
        public void LogoutPacket_EndsTheSessionOnce_AndTheWatchdogStaysQuiet()
        {
            var f = new Fixture();
            byte[] payload = new byte[24];
            payload[0] = (byte)LogoutState.Logout;
            f.Net.Parser.Dispatcher.Dispatch(new PacketHeader(0x00B, 28, 1), payload);
            f.Net.Parser.Dispatcher.Dispatch(new PacketHeader(0x00B, 28, 1), payload);

            var logout = Assert.Single(f.Logouts);
            Assert.Equal(LogoutState.Logout, logout.State);
            f.Wait(120);
            Assert.Single(f.Logouts);
            Assert.Empty(f.LostChanges);
        }

        [Fact]
        public async Task ZoneTransition_HidesTheIndicatorButStillTimesOut()
        {
            var f = new Fixture();
            await f.Net.PerformZoneTransitionAsync(IPAddress.Loopback, 54231, TestContext.Current.CancellationToken);
            Assert.True(f.Net.ZoneTransitionPending);

            f.Wait(30);
            Assert.False(f.Net.IsConnectionLost);
            f.Wait(30);
            Assert.Equal(LogoutState.Timeout, Assert.Single(f.Logouts).State);
        }

        [Fact]
        public void DisconnectedSession_IsNeverFlagged()
        {
            var f = new Fixture();
            f.Net.CurrentState = SessionState.Disconnected;
            f.Wait(120);
            Assert.False(f.Net.IsConnectionLost);
            Assert.Empty(f.Logouts);
            Assert.Equal(TimeSpan.Zero, f.Net.TimeSinceLastServerPacket);
        }

        [Fact]
        public void EachSession_HasItsOwnClock()
        {
            var clock = new ManualTimeProvider();
            var silent = new Fixture(clock);
            var answered = new Fixture(clock);

            for (int i = 0; i < 13; i++)
            {
                clock.Advance(TimeSpan.FromSeconds(5));
                answered.ReceiveServerDatagram();
                silent.Net.CheckServerSilence();
                answered.Net.CheckServerSilence();
            }

            Assert.Equal(LogoutState.Timeout, Assert.Single(silent.Logouts).State);
            Assert.Empty(answered.Logouts);
            Assert.False(answered.Net.IsConnectionLost);
            Assert.Equal(SessionState.ActiveInWorld, answered.Net.CurrentState);
        }

        [Fact]
        public void CustomThresholds_AreHonoured()
        {
            var f = new Fixture();
            f.Net.ConnectionLostAfter = TimeSpan.FromSeconds(2);
            f.Net.ServerSilenceTimeout = TimeSpan.FromSeconds(10);
            f.Wait(2);
            Assert.True(f.Net.IsConnectionLost);
            f.Wait(8);
            Assert.Single(f.Logouts);
        }
    }
}
