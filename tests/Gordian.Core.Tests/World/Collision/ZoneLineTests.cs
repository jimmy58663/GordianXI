using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Gordian.Core.Resources;
using Gordian.Core.Resources.Graphics;
using Gordian.Core.World.Collision;
using Xunit;

namespace Gordian.Core.Tests.World.Collision
{
    public class ZoneLineTests
    {
        private const string GameDirectory = @"G:\Program Files (x86)\PlayOnline\SquareEnix\FINAL FANTASY XI";
        private readonly ITestOutputHelper _output;

        public ZoneLineTests(ITestOutputHelper output) => _output = output;

        private static ZoneInteraction Box(string id, Vector3 center, Vector3 size, float rotation = 0.0f) => new(id, center, rotation, size);

        [Fact]
        public void IsZoneLine_OnlyFourCharacterZIds()
        {
            Assert.True(Box("z6e0", Vector3.Zero, Vector3.One).IsZoneLine);
            Assert.True(Box("zmrq", Vector3.Zero, Vector3.One).IsZoneLine);
            Assert.False(Box("_6e0", Vector3.Zero, Vector3.One).IsZoneLine);
            Assert.False(Box("@6l0", Vector3.Zero, Vector3.One).IsZoneLine);
            Assert.False(Box("z6", Vector3.Zero, Vector3.One).IsZoneLine);
        }

        [Theory]
        [InlineData("z6e0", true, true)]
        [InlineData("z6e1", false, false)] // the landing box beside z6e0
        [InlineData("z2sa", true, true)]
        [InlineData("z2sf", false, false)]
        [InlineData("z2sy", true, true)]
        [InlineData("zmr0", true, false)] // Mog House entrance: not until the room is drawn
        [InlineData("zms2", true, false)]
        [InlineData("zmrq", true, true)] // Mog House exit
        [InlineData("_6e0", false, false)]
        public void IsCrossing_EvenLastCharacterAndNoMogHouseEntrance(string id, bool crossing, bool requestable)
        {
            Assert.Equal(crossing, ZoneLines.IsCrossing(id));
            Assert.Equal(requestable, ZoneLines.IsRequestable(id));
        }

        [Fact]
        public void Monitor_IgnoresLandingBoxes()
        {
            var lines = new[]
            {
                Box("z6e0", new Vector3(0, 0, 0), new Vector3(4, 4, 4)),
                Box("z6e1", new Vector3(10, 0, 0), new Vector3(4, 4, 4)),
            };
            var monitor = new ZoneLineMonitor();
            // Arriving in the landing box arms (it is not a line), walking into it sends nothing, the line itself does.
            Assert.Null(monitor.Update(230, lines, new Vector3(10, 0, 0), true, 0));
            Assert.Null(monitor.Update(230, lines, new Vector3(9, 0, 0), true, 0.1));
            Assert.Equal("z6e0", monitor.Update(230, lines, new Vector3(1, 0, 0), true, 0.2)?.Id);
        }

        [Fact]
        public void Contains_UsesTheTurnedBox()
        {
            // 10 wide (local X), 4 high, 2 deep (local Z).
            var straight = Box("z000", new Vector3(100, -5, 50), new Vector3(10, 4, 2));
            Assert.True(straight.Contains(new Vector3(104, -5, 50)));
            Assert.False(straight.Contains(new Vector3(100, -5, 52)));
            Assert.False(straight.Contains(new Vector3(100, -8, 50))); // above the box (internal -Y up)

            // A quarter turn swaps the footprint's axes.
            var turned = straight with { RotationY = MathF.PI / 2 };
            Assert.False(turned.Contains(new Vector3(104, -5, 50)));
            Assert.True(turned.Contains(new Vector3(100, -5, 54)));
            Assert.True(turned.Contains(new Vector3(100.9f, -5, 50)));
            Assert.True(turned.Contains(new Vector3(101.5f, -5, 50), margin: 1.0f));
        }

        [Fact]
        public void Select_KeepsZoneLinesOncePerId()
        {
            var lines = ZoneLines.Select(new[]
            {
                Box("_door", Vector3.Zero, Vector3.One),
                Box("z001", Vector3.Zero, Vector3.One),
                Box("z001", new Vector3(50, 0, 0), Vector3.One),
                Box("z002", Vector3.Zero, new Vector3(1, 0, 1)), // flat: no volume
                Box("zmrq", Vector3.Zero, Vector3.One),
            });
            Assert.Equal(new[] { "z001", "zmrq" }, lines.Select(l => l.Id));
            Assert.Equal(Vector3.Zero, lines[0].Center);
        }

        private static readonly IReadOnlyList<ZoneInteraction> TwoLines = new[]
        {
            Box("z6e0", new Vector3(0, 0, 0), new Vector3(4, 4, 4)),
            Box("z6e2", new Vector3(20, 0, 0), new Vector3(4, 4, 4)),
        };

        [Fact]
        public void Monitor_SendsOnceOnEntering()
        {
            var monitor = new ZoneLineMonitor();
            Assert.Null(monitor.Update(230, TwoLines, new Vector3(10, 0, 0), true, 0));
            var line = monitor.Update(230, TwoLines, new Vector3(1, 0, 0), true, 0.1);
            Assert.Equal("z6e0", line?.Id);
            Assert.Equal("z6e0", monitor.PendingId);
            // Standing in the line, or walking deeper into it, sends nothing more.
            Assert.Null(monitor.Update(230, TwoLines, new Vector3(0.5f, 0, 0), true, 0.2));
            Assert.Null(monitor.Update(230, TwoLines, new Vector3(0, 0, 0), true, 1.0));
        }

        [Fact]
        public void Monitor_ArrivingInsideALineDoesNotSend()
        {
            var monitor = new ZoneLineMonitor();
            // The first position of the zone is inside a line: wait for the player to step out first.
            Assert.Null(monitor.Update(100, TwoLines, new Vector3(0, 0, 0), true, 0));
            Assert.Null(monitor.Update(100, TwoLines, new Vector3(1, 0, 0), true, 0.5));
            Assert.Null(monitor.Update(100, TwoLines, new Vector3(10, 0, 0), true, 1.0));
            Assert.Equal("z6e2", monitor.Update(100, TwoLines, new Vector3(20, 0, 0), true, 1.5)?.Id);
        }

        [Fact]
        public void Monitor_RefusedLineNeedsThePlayerToLeaveFirst()
        {
            var monitor = new ZoneLineMonitor();
            monitor.Update(230, TwoLines, new Vector3(10, 0, 0), true, 0);
            Assert.NotNull(monitor.Update(230, TwoLines, new Vector3(0, 0, 0), true, 0.1));
            // No answer (or a refusal that leaves the player in the line): the pending request expires, still no resend.
            Assert.Null(monitor.Update(230, TwoLines, new Vector3(0, 0, 0), true, 0.1 + ZoneLineMonitor.PendingTimeoutSeconds));
            Assert.Null(monitor.PendingId);
            // Out and back in: a new request.
            Assert.Null(monitor.Update(230, TwoLines, new Vector3(10, 0, 0), true, 6.0));
            Assert.Equal("z6e0", monitor.Update(230, TwoLines, new Vector3(0, 0, 0), true, 6.1)?.Id);
        }

        [Fact]
        public void Monitor_PendingBlocksOtherLines()
        {
            var monitor = new ZoneLineMonitor();
            monitor.Update(230, TwoLines, new Vector3(10, 0, 0), true, 0);
            Assert.NotNull(monitor.Update(230, TwoLines, new Vector3(0, 0, 0), true, 0.1));
            Assert.Null(monitor.Update(230, TwoLines, new Vector3(10, 0, 0), true, 0.2));
            Assert.Null(monitor.Update(230, TwoLines, new Vector3(20, 0, 0), true, 0.3));
        }

        [Fact]
        public void Monitor_NothingWhileTheSessionCannotSend()
        {
            var monitor = new ZoneLineMonitor();
            // Outside while blocked (zoning, an event) does not arm, and inside sends nothing.
            Assert.Null(monitor.Update(230, TwoLines, new Vector3(10, 0, 0), false, 0));
            Assert.Null(monitor.Update(230, TwoLines, new Vector3(0, 0, 0), false, 0.1));
            Assert.Null(monitor.Update(230, TwoLines, new Vector3(0, 0, 0), true, 0.2));
            Assert.Null(monitor.PendingId);
        }

        [Fact]
        public void Monitor_ZoneChangeClearsPendingAndDisarms()
        {
            var monitor = new ZoneLineMonitor();
            monitor.Update(230, TwoLines, new Vector3(10, 0, 0), true, 0);
            Assert.NotNull(monitor.Update(230, TwoLines, new Vector3(0, 0, 0), true, 0.1));
            Assert.Null(monitor.Update(100, TwoLines, new Vector3(0, 0, 0), true, 0.5));
            Assert.Null(monitor.PendingId);
        }

        /// <summary>
        /// The zone DATs' <c>z</c> boxes against LandSandBoat's zone lines (data/zones/*/zone.yaml, the <c>from</c>
        /// positions): every LSB line of Southern San d'Oria and West Ronfaure has a box of the same id, and its origin
        /// is the box's centre (LSB took its lines from these records).
        /// </summary>
        [Theory]
        [InlineData(230, "z6e0", 113.458f, -4.079f, -57.351f)]
        [InlineData(230, "zmr0", 164.933f, -5.547f, 164.792f)]
        [InlineData(230, "z6e2", -113.372f, -4.075f, -57.418f)]
        [InlineData(230, "z6e4", 0.013f, -8.469f, 57.965f)]
        [InlineData(100, "z2s0", -119.065f, -65.707f, 280.921f)]
        [InlineData(100, "z2s4", -11.697f, -53.643f, 140.000f)]
        [InlineData(100, "z2s6", 94.760f, -29.531f, -200.153f)]
        public void RetailZoneLines_MatchLandSandBoat(int zoneId, string id, float x, float y, float z)
        {
            if (!Directory.Exists(GameDirectory)) return;
            var resources = new ResourceManager(GameDirectory);
            resources.InitializeFileTable();
            var collision = resources.TryLoadZoneCollision(zoneId);
            Assert.NotNull(collision);
            foreach (var line in collision!.ZoneLineVolumes)
            {
                _output.WriteLine($"{line.Id} centre=({line.Center.X:F2}, {line.Center.Y:F2}, {line.Center.Z:F2}) size=({line.Size.X:F2}, {line.Size.Y:F2}, {line.Size.Z:F2}) rot={line.RotationY:F3}");
            }
            var box = collision.ZoneLineVolumes.Single(l => l.Id == id);
            var origin = new Vector3(x, y, z);
            float distance = Vector2.Distance(new Vector2(box.Center.X, box.Center.Z), new Vector2(x, z));
            _output.WriteLine($"{id}: LSB origin {(box.Contains(origin) ? "inside" : "outside")}, {distance:F2} yalms from the centre");
            Assert.True(box.Contains(origin));
        }
    }
}
