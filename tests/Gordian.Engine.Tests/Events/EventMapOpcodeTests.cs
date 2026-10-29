// tests/Gordian.Engine.Tests/Events/EventMapOpcodeTests.cs
using System;
using Gordian.Core.Resources.Events;
using Gordian.Engine.Events;
using Xunit;

namespace Gordian.Engine.Tests.Events
{
    /// <summary>
    /// The event map opcodes (#168): 0x89 / 0xC8 open a zone's map, 0x8B marks it, 0x8A closes it, and 0xD4 gives a query's
    /// options their places (XiEvents OpCodes/0x0089, 0x008A, 0x008B, 0x00C8, 0x00D4), and what the map then shows.
    /// </summary>
    public class EventMapOpcodeTests
    {
        private static readonly TimeSpan Frame = TimeSpan.FromSeconds(1.0 / 60);

        private static EventVm Make(byte[] code, RecordingHost host, uint[] references)
        {
            var block = new EventBlock(0x010E6001, offsets: new ushort[] { 0 }, eventIds: new ushort[] { 100 }, references, code);
            return new EventVm(block, 100, new EventWorkZone(), host, 0x010E6001, 1);
        }

        private static void Run(EventVm vm)
        {
            for (int i = 0; i < 20 && !vm.IsFinished; i++) vm.Tick(Frame);
        }

        [Fact]
        public void IntroMapTutorial_OpensMarksAndClosesTheMap()
        {
            // As Southern San d'Oria's intro (event 503): C8 230 0 1 ; 8B 230 0 -10264 -363 "Guide_NPC" ; 8A ; 00
            var code = new byte[]
            {
                0xC8, 0x00, 0x80, 0x01, 0x80, 0x02, 0x80,
                0x8B, 0x00, 0x80, 0x01, 0x80, 0x03, 0x80, 0x04, 0x80,
                (byte)'G', (byte)'u', (byte)'i', (byte)'d', (byte)'e', (byte)'_', (byte)'N', (byte)'P', (byte)'C', 0, 0, 0, 0, 0, 0, 0,
                0x8A,
                0x89, 0x00, 0x80,
                0x00,
            };
            var host = new RecordingHost();
            var vm = Make(code, host, new uint[] { 230, 0, 1, unchecked((uint)-10264), unchecked((uint)-363) });
            Run(vm);
            Assert.True(vm.IsFinished);
            Assert.Equal(new[] { "open 230 0", "mark 230 0 -10.264 -0.363 Guide NPC", "close", "open 230 -1" }, host.MapCalls);
            Assert.Empty(host.Skipped);
        }

        [Fact]
        public void D4_GivesQueryOptionsTheirPlaces_AndSubZeroOpensTheMapBehindTheQuery()
        {
            // D4 01 opt kind number ; D4 03 opt v ; D4 04 opt v x y z ; D4 05 ... ; D4 00 msg default hidden (the query)
            var code = new byte[]
            {
                0xD4, 0x01, 0x01, 0x80, 0x00, 0x80, 0x02, 0x80,
                0xD4, 0x03, 0x04, 0x80, 0x03, 0x80,
                0xD4, 0x04, 0x04, 0x80, 0x03, 0x80, 0x05, 0x80, 0x06, 0x80, 0x07, 0x80,
                0xD4, 0x05, 0x01, 0x80, 0x03, 0x80, 0x05, 0x80, 0x06, 0x80, 0x07, 0x80,
                0xD4, 0x00, 0x08, 0x80, 0x00, 0x80, 0x00, 0x80,
                0x00,
            };
            var host = new RecordingHost();
            var vm = Make(code, host, new uint[] { 0, 1, 98, 231, 2, 1000, 2000, 3000, 30 });
            vm.Tick(Frame);
            Assert.Equal(new[]
            {
                "entry 1 sub 1 0 98 0 0 0",
                "entry 2 sub 3 231 0 0 0 0",
                "entry 2 sub 4 231 0 1000 2000 3000",
                "entry 1 sub 5 231 0 1000 2000 3000",
                "query",
            }, host.MapCalls);
            Assert.Equal(30, Assert.Single(host.Queries).Message);
            Assert.Empty(host.Skipped);
        }

        [Fact]
        public void TheQueryMap_ShowsOnlyOnAHomePointOption_AndGoesWithTheQuery()
        {
            var state = EventMapState.Closed
                .WithQueryEntry(1, new EventQueryMapEntry(1, EventQueryMapEntry.HomePointKind, 98, 0, 0, 0))
                .WithQueryMap();
            Assert.Null(state.Focus(null));
            Assert.Null(state.Focus(0)); // "Nowhere."
            var focus = state.Focus(1);
            Assert.NotNull(focus);
            Assert.Equal(231, focus!.Zone); // Northern San d'Oria #4
            Assert.Equal(-133f, focus.X!.Value, 3);
            Assert.Equal(195f, focus.Z!.Value, 3);
            Assert.Equal(EventMapPin.HomePoint, focus.Pin);
            Assert.Equal(EventMapState.HomePointLabel, Assert.Single(focus.Markers).Name);
            Assert.Null(state.WithoutQuery().Focus(1));
            // The confirm prompt after the list has no map behind it.
            Assert.Null(state.WithoutQuery().WithQueryEntry(1, new EventQueryMapEntry(1, 0, 98, 0, 0, 0)).Focus(1));
        }

        [Fact]
        public void TheEventMap_CentresOnTheLastMarker_UntilItCloses()
        {
            var state = EventMapState.Closed.Open(230, 0);
            var focus = state.Focus(null);
            Assert.NotNull(focus);
            Assert.Null(focus!.X);
            state = state.Mark(new EventMapMarker(0, -10.264f, -0.363f, "Ailevia")).Mark(new EventMapMarker(1, 5, 6, "Other"));
            focus = state.Focus(null)!;
            Assert.Equal((5f, 6f), (focus.X!.Value, focus.Z!.Value));
            Assert.Equal(2, focus.Markers.Count);
            Assert.Equal(EventMapPin.Event, focus.Pin);
            // A marker with an index already placed moves it.
            Assert.Equal(2, state.Mark(new EventMapMarker(0, 1, 1, "Ailevia")).Markers.Count);
            Assert.Null(state.Close().Focus(null));
        }
    }
}
