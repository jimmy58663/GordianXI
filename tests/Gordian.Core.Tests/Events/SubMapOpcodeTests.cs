// tests/Gordian.Core.Tests/Events/SubMapOpcodeTests.cs
using System;
using System.Collections.Generic;
using Gordian.Core.Events;
using Gordian.Core.Resources.Events;
using Gordian.Core.World;
using Xunit;

namespace Gordian.Core.Tests.Events
{
    /// <summary>Event opcodes 0x75 (load room, C2S 0x0F2) and 0xA6 (sub-map number, C2S 0x0EB / S2C 0x10E).</summary>
    public class SubMapOpcodeTests
    {
        private sealed class SubMapHost : IEventVmHost
        {
            public List<int> Rooms { get; } = new();
            public List<int> SubMapChanges { get; } = new();
            public List<byte> Skipped { get; } = new();
            public int Requests { get; private set; }
            public bool Pending { get; set; }
            public int Number { get; set; }
            public bool Reading { get; set; }
            public bool QueueFull { get; set; }

            public double PrintMessage(int messageId, EventSpeaker speaker, uint speakerServerId, ushort speakerIndex) => 0;
            public void OpenQuery(int messageId, int defaultIndex, uint hiddenMask) { }
            public int QueryResult => 0;
            public void CloseQuery() { }
            public void SendEventUpdate(uint endParameter) { }
            public void SendEventUpdateXzy(uint endParameter, float x, float y, float z, float heading) { }
            public bool ReceivePending => false;
            public void SetControlLock(bool locked) { }
            public bool EntityExists(uint serverId) => false;
            public int GetEntityValue(uint serverId, int key) => 0;
            public int GameTime => 0;
            public void OnSkippedOpcode(byte opcode, int pc) => Skipped.Add(opcode);

            public bool IsReadingRoomData => Reading;
            public void OpenIndoorRoom(int room) => Rooms.Add(room);
            public bool SendSubMapChange(int subMapNumber)
            {
                if (QueueFull) return false;
                SubMapChanges.Add(subMapNumber);
                return true;
            }
            public bool RequestSubMapNumber()
            {
                if (QueueFull) return false;
                Requests++;
                Pending = true;
                return true;
            }
            public bool SubMapNumberPending => Pending;
            public int SubMapNumber => Number;
        }

        private static readonly TimeSpan Frame = TimeSpan.FromSeconds(1.0 / 60);

        private static EventVm Make(byte[] code, SubMapHost host, EventWorkZone zone, uint[]? references = null)
        {
            var block = new EventBlock(0x010E6001, offsets: new ushort[] { 0 }, eventIds: new ushort[] { 100 }, references ?? Array.Empty<uint>(), code);
            return new EventVm(block, 100, zone, host, 0x010E6001, 1);
        }

        [Fact]
        public void A6_RequestsWaitsForTheAnswerAndStoresTheSubMap()
        {
            // A6 00 ; A6 01 ; A6 02 zone[0] ; 00
            var code = new byte[] { 0xA6, 0x00, 0xA6, 0x01, 0xA6, 0x02, 0x00, 0x10, 0x00 };
            var host = new SubMapHost();
            var zone = new EventWorkZone();
            var vm = Make(code, host, zone);

            vm.Tick(Frame); // sub 0 sends 0x0EB and yields
            Assert.Equal(1, host.Requests);
            vm.Tick(Frame); // sub 1 waits for 0x10E
            vm.Tick(Frame);
            Assert.False(vm.IsFinished);
            Assert.Equal(1, host.Requests);

            host.Number = 7;
            host.Pending = false; // S2C 0x10E arrived
            for (int i = 0; i < 4 && !vm.IsFinished; i++) vm.Tick(Frame);
            Assert.True(vm.IsFinished);
            Assert.Equal(7, zone.Zone[0]);
            Assert.Empty(host.Skipped);
        }

        [Fact]
        public void A6_RetriesTheRequestWhileItCannotBeQueued()
        {
            var code = new byte[] { 0xA6, 0x00, 0x00 };
            var host = new SubMapHost { QueueFull = true };
            var vm = Make(code, host, new EventWorkZone());
            vm.Tick(Frame);
            vm.Tick(Frame);
            Assert.Equal(0, host.Requests);
            Assert.False(vm.IsFinished);

            host.QueueFull = false;
            vm.Tick(Frame);
            Assert.Equal(1, host.Requests);
            vm.Tick(Frame);
            Assert.True(vm.IsFinished);
        }

        [Fact]
        public void LoadRoom_OpensTheRoomThenSendsItAsTheSubMap()
        {
            // 75 00 ref0(5) ; 75 01 ; 75 02 ; 00 — the retail pattern: sub 2 reuses the room operand of the 75 00.
            var code = new byte[] { 0x75, 0x00, 0x00, 0x80, 0x75, 0x01, 0x75, 0x02, 0x00 };
            var host = new SubMapHost();
            var vm = Make(code, host, new EventWorkZone(), new uint[] { 5 });

            for (int i = 0; i < 4 && !vm.IsFinished; i++) vm.Tick(Frame);
            Assert.True(vm.IsFinished);
            Assert.Equal(5, Assert.Single(host.Rooms));
            Assert.Equal(5, Assert.Single(host.SubMapChanges));
        }

        [Fact]
        public void LoadRoom_WaitsWhileRoomDataIsRead_AndRetriesTheSend()
        {
            var code = new byte[] { 0x75, 0x00, 0x00, 0x80, 0x75, 0x01, 0x75, 0x02, 0x00 };
            var host = new SubMapHost { Reading = true };
            var vm = Make(code, host, new EventWorkZone(), new uint[] { 3 });
            vm.Tick(Frame);
            vm.Tick(Frame);
            Assert.Empty(host.Rooms);

            host.Reading = false;
            host.QueueFull = true;
            for (int i = 0; i < 4; i++) vm.Tick(Frame);
            Assert.Equal(3, Assert.Single(host.Rooms));
            Assert.Empty(host.SubMapChanges);
            Assert.False(vm.IsFinished);

            host.QueueFull = false;
            for (int i = 0; i < 3 && !vm.IsFinished; i++) vm.Tick(Frame);
            Assert.True(vm.IsFinished);
            Assert.Equal(3, Assert.Single(host.SubMapChanges));
        }

        [Fact]
        public void SubMapState_TracksZoneInAnswersAndRooms()
        {
            var state = new SubMapState();
            var changes = new List<int>();
            var rooms = new List<int>();
            state.SubMapChanged += changes.Add;
            state.IndoorRoomChanged += rooms.Add;

            state.OnZoneLogin(4);
            Assert.Equal(4, state.SubMapNumber);
            Assert.Equal(-1, state.IndoorRoom);

            state.MarkRequested();
            Assert.True(state.RequestPending);
            state.ReceiveSubMapNumber(0);
            Assert.False(state.RequestPending);
            Assert.Equal(0, state.SubMapNumber);

            state.OpenIndoorRoom(2);
            Assert.Equal(2, state.IndoorRoom);
            state.OnZoneLogin(0);
            Assert.Equal(-1, state.IndoorRoom);

            Assert.Equal(new[] { 4, 0 }, changes);
            Assert.Equal(new[] { 2, -1 }, rooms);
        }
    }
}
