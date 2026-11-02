// tests/Gordian.Engine.Tests/Ui/StockUiCompassTests.cs
using System;
using System.Numerics;
using Gordian.Core.World;
using Gordian.Engine.Ui;
using Xunit;

namespace Gordian.Engine.Tests.Ui
{
    /// <summary>The compass above the log (#485): its maths against the maintainer's retail captures of 2026-10-31.</summary>
    public class StockUiCompassTests
    {
        [Theory]
        [InlineData(0f, 1f, 0f)]
        [InlineData(1f, 0f, 90f)]
        [InlineData(0f, -1f, 180f)]
        [InlineData(-1f, 0f, 270f)]
        [InlineData(1f, 1f, 45f)]
        public void BearingOf_IsClockwiseFromNorth(float east, float north, float expected) =>
            Assert.Equal(expected, StockUiCompass.BearingOf(east, north), 3);

        [Fact]
        public void Letters_SitWhereTheTurnedCaptureShowsThem()
        {
            // image-2: the camera looks about 123 degrees (east-south-east); N is down and left, E up and left, S up and
            // right, W down and right, at about (-38, 11), (-25, -15), (36, -11), (23, 15) px from the centre.
            const float camera = 123.3f;
            AssertNear(new Vector2(-38, 11), StockUiCompass.Project(0, StockUiCompass.LetterRadius, camera));
            AssertNear(new Vector2(-25, -15.5f), StockUiCompass.Project(90, StockUiCompass.LetterRadius, camera));
            AssertNear(new Vector2(36, -11.5f), StockUiCompass.Project(180, StockUiCompass.LetterRadius, camera));
            AssertNear(new Vector2(23, 15.5f), StockUiCompass.Project(270, StockUiCompass.LetterRadius, camera));
        }

        [Fact]
        public void Letters_TurnWithTheCamera()
        {
            // image (the standard view): the camera looks about south-west; N is down and right, E down and left.
            const float camera = 221f;
            var n = StockUiCompass.Project(0, StockUiCompass.LetterRadius, camera);
            var e = StockUiCompass.Project(90, StockUiCompass.LetterRadius, camera);
            Assert.True(n.X > 20 && n.Y > 5, $"N {n}");
            Assert.True(e.X < -20 && e.Y > 5, $"E {e}");
            // What the camera looks at is the far side of the ring (up), what is behind it the near side (down).
            Assert.True(StockUiCompass.Project(camera, 10, camera).Y < -4);
            Assert.True(StockUiCompass.Project(camera + 180, 10, camera).Y > 4);
            Assert.True(StockUiCompass.Project(camera + 90, 10, camera).X > 9);
        }

        [Fact]
        public void Dots_FillTheRingOutToSixteenYalms()
        {
            Assert.Equal(Vector2.Zero, StockUiCompass.DotOffset(0, 0, 0));
            // Straight ahead at the range is the ring's far edge.
            var ahead = StockUiCompass.DotOffset(0, StockUiCompass.RangeYalms, 0)!.Value;
            Assert.Equal(0, ahead.X, 3);
            Assert.Equal(-StockUiCompass.RingRadius * StockUiCompass.Squash, ahead.Y, 3);
            var half = StockUiCompass.DotOffset(8, 0, 0)!.Value; // east, half way, with the camera looking north: right
            Assert.Equal(StockUiCompass.RingRadius / 2, half.X, 3);
            Assert.Null(StockUiCompass.DotOffset(12, 12, 0));
        }

        [Fact]
        public void Classify_ColoursNpcsMonstersAndPlayers()
        {
            Assert.Equal(StockUiCompass.DotKind.Npc, StockUiCompass.Classify(EntityType.Npc));
            Assert.Equal(StockUiCompass.DotKind.Npc, StockUiCompass.Classify(EntityType.Door));
            Assert.Equal(StockUiCompass.DotKind.Monster, StockUiCompass.Classify(EntityType.Monster));
            Assert.Equal(StockUiCompass.DotKind.Player, StockUiCompass.Classify(EntityType.Player));
            Assert.Equal(StockUiCompass.DotKind.Player, StockUiCompass.Classify(EntityType.Trust));
        }

        [Fact]
        public void TimeText_DropsTheHoursLeadingZero()
        {
            Assert.Equal("9:27", StockUiCompass.TimeText(9, 27));
            Assert.Equal("11:05", StockUiCompass.TimeText(11, 5));
            Assert.Equal("0:00", StockUiCompass.TimeText(0, 0));
            Assert.Equal("11:25 (J-5)", StockUiCompass.LineText(11, 25, "J-5"));
            Assert.Equal("11:25", StockUiCompass.LineText(11, 25, null));
        }

        [Fact]
        public void TimeOfDay_FollowsTheSessionClock()
        {
            var clock = new VanaClock();
            var now = new DateTime(2026, 10, 31, 12, 0, 0, DateTimeKind.Utc);
            long seconds = clock.GetVanadielSeconds(now) % VanaTime.SecondsPerVanadielDay;
            var (hour, minute) = StockUiCompass.TimeOfDay(clock, now);
            Assert.Equal((int)(seconds / 3600), hour);
            Assert.Equal((int)(seconds % 3600 / 60), minute);
            Assert.Equal(VanaTime.GetTimeOfDayHours(now, 0), hour + minute / 60f, 1);
        }

        [Fact]
        public void DayOrb_IsTheElementsBall()
        {
            Assert.Equal((48, 0), StockUiCompass.DayOrbCell(4));  // Iceday: light blue, as in the captures
            Assert.Equal((32, 0), StockUiCompass.DayOrbCell(0));  // Firesday: red
            Assert.Equal((32, 16), StockUiCompass.DayOrbCell(7)); // Darksday: dark
            var cells = new System.Collections.Generic.HashSet<(int, int)>();
            for (int day = 0; day < 8; day++) Assert.True(cells.Add(StockUiCompass.DayOrbCell(day)));
        }

        [Theory]
        [InlineData(16f, 16f, "A-1")]
        [InlineData(47.9f, 47.9f, "A-1")]
        [InlineData(48f, 16f, "B-1")]
        [InlineData(320f, 150f, "J-5")]
        [InlineData(495.9f, 495.9f, "O-15")]
        [InlineData(10f, 100f, null)]
        [InlineData(100f, 500f, null)]
        public void GridLabel_IsFifteenSquaresOf32(float u, float v, string? expected) =>
            Assert.Equal(expected, StockUiCompass.GridLabel(u, v));

        [Fact]
        public void Line_SitsAboveTheLog_OrTheScreensBottom()
        {
            Assert.Equal(1253f, StockUiCompass.LineBottom(1258f, 1440f, 1));
            Assert.Equal(1416f, StockUiCompass.LineBottom(null, 1440f, 1));
            Assert.Equal(1248f, StockUiCompass.LineBottom(1258f, 1440f, 2));
        }

        [Fact]
        public void AWindowInItsPlace_Overlaps()
        {
            var bounds = StockUiCompass.Bounds(1253f, 1);
            Assert.True(bounds.Y < 1253f - StockUiCompass.CenterRise - StockUiCompass.LetterRadius * StockUiCompass.Squash);
            // The target command menu just above the log.
            Assert.True(StockUiCompass.Overlaps(bounds, 16, 1200, 112, 58));
            // The main menu at the top right does not.
            Assert.False(StockUiCompass.Overlaps(bounds, 2000, 48, 112, 300));
        }

        [Fact]
        public void StockUiCommand_NamesTheCompass()
        {
            Assert.True(StockUiVisibilityState.TryParse("compass", out var element));
            Assert.Equal(StockUiElement.Compass, element);
            Assert.True((StockUiElement.All & StockUiElement.Compass) != 0);
        }

        private static void AssertNear(Vector2 expected, Vector2 actual, float tolerance = 3f) =>
            Assert.True(Vector2.Distance(expected, actual) <= tolerance, $"expected {expected}, got {actual}");
    }
}
