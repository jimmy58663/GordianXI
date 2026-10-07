// tests/Gordian.Core.Tests/Network/LogoutSessionFlowTests.cs
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Gordian.Core.Network;
using Gordian.Core.Network.Packets;
using Xunit;

namespace Gordian.Core.Tests.Network
{
    /// <summary>
    /// Log Out / Shut Down session flow (#266, #270): the client's own C2S 0x0E7 request tells a shutdown from a logout
    /// (LandSandBoat answers both with S2C 0x00B state 1), and no C2S 0x015 goes out once a 0x00B takes the character off
    /// the map server.
    /// </summary>
    public sealed class LogoutSessionFlowTests
    {
        private sealed class Fixture
        {
            public List<byte[]> Sent { get; } = new();
            public SessionNetworkManager Net { get; }

            public Fixture()
            {
                Net = new SessionNetworkManager("127.0.0.1", 54230)
                {
                    OutboundChunkOverride = (data, _) =>
                    {
                        lock (Sent) Sent.Add(data.ToArray());
                        return Task.CompletedTask;
                    }
                };
            }

            public IEnumerable<ushort> SentOpcodes
            {
                get
                {
                    lock (Sent) return Sent.Select(p => (ushort)(BinaryPrimitives.ReadUInt16LittleEndian(p) & 0x1FF)).ToArray();
                }
            }

            public void Receive0x00B(LogoutState state)
            {
                byte[] payload = new byte[24];
                payload[0] = (byte)state;
                Net.Parser.Dispatcher.Dispatch(new PacketHeader(0x00B, 28, 1), payload);
            }

            public void Receive0x00A()
            {
                Net.Parser.Dispatcher.Dispatch(new PacketHeader(0x00A, 260, 1), new byte[256]);
            }

            public void ReceivePosPing()
            {
                Net.Parser.Dispatcher.Dispatch(new PacketHeader(0x015, 8, 1), new byte[4]);
            }

            public Task QueuePositionAsync()
                => Net.QueueChunkAsync(LifecycleOutboundPackets.BuildPos(sequenceId: 0, x: 1f, y: 2f, z: 3f, dir: 0, targetIndex: 0, moveFrame: 1));
        }

        // ---- C2S 0x0E7 request tracking ----

        [Theory]
        [InlineData(null, ReqLogoutMode.LogoutOn, ReqLogoutKind.Logout, ReqLogoutKind.Logout)]
        [InlineData(null, ReqLogoutMode.ShutdownOn, ReqLogoutKind.Shutdown, ReqLogoutKind.Shutdown)]
        [InlineData(null, ReqLogoutMode.Toggle, ReqLogoutKind.Shutdown, ReqLogoutKind.Shutdown)]
        [InlineData(ReqLogoutKind.Logout, ReqLogoutMode.Toggle, ReqLogoutKind.Logout, null)]
        [InlineData(ReqLogoutKind.Logout, ReqLogoutMode.Toggle, ReqLogoutKind.Shutdown, null)]
        [InlineData(ReqLogoutKind.Logout, ReqLogoutMode.ShutdownOn, ReqLogoutKind.Shutdown, ReqLogoutKind.Shutdown)]
        [InlineData(ReqLogoutKind.Shutdown, ReqLogoutMode.LogoutOn, ReqLogoutKind.Logout, ReqLogoutKind.Logout)]
        [InlineData(ReqLogoutKind.Shutdown, ReqLogoutMode.Off, ReqLogoutKind.Logout, null)]
        [InlineData(ReqLogoutKind.Shutdown, ReqLogoutMode.LogoutOn, ReqLogoutKind.Shutdown, ReqLogoutKind.Shutdown)]
        [InlineData(null, ReqLogoutMode.ShutdownOn, ReqLogoutKind.Logout, null)]
        public void ApplyLogoutRequest_MirrorsTheServer(ReqLogoutKind? pending, ReqLogoutMode mode, ReqLogoutKind kind, ReqLogoutKind? expected)
        {
            Assert.Equal(expected, LifecyclePacketModule.ApplyLogoutRequest(pending, mode, kind));
        }

        [Theory]
        [InlineData(LogoutState.Logout, null, true)]
        [InlineData(LogoutState.Logout, ReqLogoutKind.Logout, true)]
        [InlineData(LogoutState.Logout, ReqLogoutKind.Shutdown, false)]
        [InlineData(LogoutState.PolExit, null, false)]
        [InlineData(LogoutState.End, ReqLogoutKind.Logout, false)]
        public void SessionLogout_ReturnsToLobbyOnlyForALogOut(LogoutState state, ReqLogoutKind? requested, bool lobby)
        {
            var logout = new SessionLogout(state, requested);
            Assert.Equal(lobby, logout.ReturnsToLobby);
            Assert.Equal(requested == ReqLogoutKind.Shutdown, logout.IsShutdown);
        }

        [Fact]
        public async Task LoggedOut_CarriesTheShutdownRequest()
        {
            var f = new Fixture();
            SessionLogout? seen = null;
            f.Net.LoggedOut += l => seen = l;

            await f.Net.RequestLogoutAsync(shutdown: true);
            Assert.Equal(ReqLogoutKind.Shutdown, f.Net.PendingLogoutKind);
            f.Receive0x00B(LogoutState.Logout);

            Assert.NotNull(seen);
            Assert.Equal(LogoutState.Logout, seen!.Value.State);
            Assert.True(seen.Value.IsShutdown);
            Assert.False(seen.Value.ReturnsToLobby);
        }

        [Fact]
        public async Task LoggedOut_ReturnsToLobbyForALogOut()
        {
            var f = new Fixture();
            SessionLogout? seen = null;
            f.Net.LoggedOut += l => seen = l;

            await f.Net.RequestLogoutAsync(shutdown: false);
            f.Receive0x00B(LogoutState.Logout);

            Assert.NotNull(seen);
            Assert.True(seen!.Value.ReturnsToLobby);
        }

        [Fact]
        public async Task LoggedOut_LogOutAfterACancelledShutdownReturnsToLobby()
        {
            var f = new Fixture();
            SessionLogout? seen = null;
            f.Net.LoggedOut += l => seen = l;

            await f.Net.Parser.LifecycleModule.RequestLogoutAsync(ReqLogoutMode.ShutdownOn, ReqLogoutKind.Shutdown);
            await f.Net.Parser.LifecycleModule.RequestLogoutAsync(ReqLogoutMode.Off, ReqLogoutKind.Shutdown);
            await f.Net.Parser.LifecycleModule.RequestLogoutAsync(ReqLogoutMode.LogoutOn, ReqLogoutKind.Logout);
            f.Receive0x00B(LogoutState.Logout);

            Assert.True(seen!.Value.ReturnsToLobby);
        }

        [Fact]
        public void LoggedOut_NotRaisedForAZoneChangeOrCancel()
        {
            var f = new Fixture();
            int raised = 0;
            f.Net.LoggedOut += _ => raised++;

            f.Receive0x00B(LogoutState.Cancel);
            Assert.Equal(0, raised);
            Assert.False(f.Net.PositionUpdatesSuspended);
        }

        // ---- C2S 0x015 suppression (#266) ----

        [Fact]
        public async Task Logout0x00B_StopsEveryOutboundPacket()
        {
            var f = new Fixture();
            f.Receive0x00B(LogoutState.Logout);

            Assert.True(f.Net.SessionEnding);
            Assert.True(f.Net.PositionUpdatesSuspended);
            await f.QueuePositionAsync();
            await f.Net.QueueChunkAsync(LifecycleOutboundPackets.BuildReqLogout());
            f.ReceivePosPing();

            Assert.Empty(f.SentOpcodes);
        }

        [Fact]
        public async Task ZoneChange0x00B_SuspendsPositionUpdatesUntilTheNextLogin()
        {
            var f = new Fixture();
            await f.QueuePositionAsync();
            Assert.Equal(new ushort[] { 0x015 }, f.SentOpcodes);

            // The zone change starts (the transition itself fails here, without a socket; the suspension does not depend on it).
            f.Receive0x00B(LogoutState.ZoneChange);
            Assert.True(f.Net.PositionUpdatesSuspended);
            Assert.False(f.Net.SessionEnding);

            await f.QueuePositionAsync();
            f.ReceivePosPing();
            Assert.Equal(new ushort[] { 0x015 }, f.SentOpcodes);

            // Other packets still go to the new map server.
            await f.Net.QueueChunkAsync(LifecycleOutboundPackets.BuildReqLogout());
            Assert.Equal(new ushort[] { 0x015, 0x0E7 }, f.SentOpcodes);

            // The new map server's 0x00A login resumes them.
            f.Receive0x00A();
            Assert.False(f.Net.PositionUpdatesSuspended);
            await f.QueuePositionAsync();
            Assert.Equal(0x015, f.SentOpcodes.Last());
        }

        [Theory]
        [InlineData(LogoutState.MyRoom)]
        [InlineData(LogoutState.Timeout)]
        public void OtherLeavingStates_SuspendPositionUpdates(LogoutState state)
        {
            var f = new Fixture();
            f.Receive0x00B(state);
            Assert.True(f.Net.PositionUpdatesSuspended);
        }

        // ---- /logout and /shutdown ----

        [Theory]
        [InlineData("/logout", ChatCommandResultKind.Logout, RestMode.Toggle)]
        [InlineData("/logout on", ChatCommandResultKind.Logout, RestMode.On)]
        [InlineData("/shutdown", ChatCommandResultKind.Shutdown, RestMode.Toggle)]
        [InlineData("/SHUTDOWN off", ChatCommandResultKind.Shutdown, RestMode.Off)]
        public void Router_ParsesLogoutCommands(string input, ChatCommandResultKind kind, RestMode mode)
        {
            var result = ChatCommandRouter.Parse(input);
            Assert.Equal(kind, result.Kind);
            Assert.Equal(mode, result.Rest);
        }

        [Fact]
        public void Router_RejectsABadLogoutArgument()
        {
            Assert.Equal(ChatCommandResultKind.LocalNotice, ChatCommandRouter.Parse("/shutdown now").Kind);
        }

        [Theory]
        [InlineData("/logout", 0x00, 0x01)]
        [InlineData("/logout on", 0x01, 0x01)]
        [InlineData("/logout off", 0x02, 0x01)]
        [InlineData("/shutdown", 0x00, 0x03)]
        [InlineData("/shutdown on", 0x03, 0x03)]
        [InlineData("/shutdown off", 0x02, 0x03)]
        public async Task Commands_SendReqLogout(string input, int mode, int kind)
        {
            var f = new Fixture();
            var result = await f.Net.ActionService.ExecuteCommandAsync(input);

            Assert.True(result.Success);
            byte[] packet;
            lock (f.Sent) packet = Assert.Single(f.Sent);
            Assert.Equal(0x0E7, BinaryPrimitives.ReadUInt16LittleEndian(packet) & 0x1FF);
            Assert.Equal(mode, BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(4)));
            Assert.Equal(kind, BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(6)));
        }

        [Fact]
        public async Task ShutdownCommand_EndsTheSessionWithoutTheLobby()
        {
            var f = new Fixture();
            SessionLogout? seen = null;
            f.Net.LoggedOut += l => seen = l;

            await f.Net.ActionService.ExecuteCommandAsync("/shutdown");
            f.Receive0x00B(LogoutState.Logout);

            Assert.True(seen!.Value.IsShutdown);
            Assert.False(seen.Value.ReturnsToLobby);
        }
    }
}
