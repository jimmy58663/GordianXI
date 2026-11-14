// tests/Gordian.Engine.Tests/Ui/Lobby/TitleFlythroughTests.cs
using System;
using System.Collections.Generic;
using System.Linq;
using Gordian.Core.Resources.Events;
using Gordian.Engine.Ui.Lobby;
using Gordian.Tests.Shared;
using Xunit;

namespace Gordian.Engine.Tests.Ui.Lobby
{
    /// <summary>The title fly-through player (#552): segment order, waiting for zones, fades, weather and clock.</summary>
    public class TitleFlythroughTests
    {
        private const double Frame = 1 / 60.0;

        /// <summary>
        /// Twelve base segments (zones 101-112, the twelfth the opening one), two expansion-1 segments (zones 201, 202) and one
        /// in no choice (zone 300); <c>loop</c> chooses among all twelve base ones, then the expansion's two.
        /// </summary>
        private static TitleSceneResource Scene()
        {
            var builder = new TitleSceneBuilder().Route("cam0", 0f, 10f);
            var bases = new List<string>();
            for (int i = 1; i <= 12; i++)
            {
                string name = $"mv{i:00}";
                bases.Add(name);
                if (i == 12)
                {
                    builder.Segment(name, 100 + i, "cam0", 600, "suny", 12 * 216000,
                        (200, TitleSceneBuilder.Weather("mist")), (200, TitleSceneBuilder.Clock(19 * 216000)));
                }
                else
                {
                    builder.Segment(name, 100 + i, "cam0", 600);
                }
            }
            builder.Segment("ex1a", 201, "cam0", 600).Segment("ex1b", 202, "cam0", 600).Segment("ex2e", 300, "cam0", 600);
            builder.Loop((0, bases.ToArray()), (1, new[] { "ex1a", "ex1b" }));
            return TitleSceneResource.Parse(builder.Build())!;
        }

        /// <summary>Runs the fly-through with its zone always shown until <paramref name="count"/> segments have started.</summary>
        private static List<int> PlayedZones(TitleFlythrough title, int count)
        {
            var zones = new List<int>();
            for (int guard = 0; zones.Count < count && guard < 1_000_000; guard++)
            {
                bool waiting = title.IsWaitingForZone;
                title.Update(Frame, zoneShown: true);
                if (waiting && !title.IsWaitingForZone) zones.Add(title.ZoneId);
            }
            return zones;
        }

        [Fact]
        public void OpensWithSegmentTwelve_ThenOneOfEachChoiceInTurn()
        {
            var title = new TitleFlythrough(Scene(), random: new Random(7));
            Assert.Equal(TitleSceneResource.OpeningSegmentNumber, title.Segment!.Number);
            Assert.Equal(112, title.ZoneId);

            var zones = PlayedZones(title, 7);

            Assert.Equal(112, zones[0]);
            // The opening segment stood for the base choice: next the expansion's, then base, expansion, ...
            for (int i = 1; i < zones.Count; i++)
            {
                if (i % 2 == 1) Assert.InRange(zones[i], 201, 202);
                else Assert.InRange(zones[i], 101, 112);
            }
            Assert.DoesNotContain(300, zones);
        }

        [Fact]
        public void AChoiceWithoutInstalledZones_IsSkipped()
        {
            var title = new TitleFlythrough(Scene(), zone => zone < 200, new Random(3));
            var zones = PlayedZones(title, 5);
            Assert.All(zones, z => Assert.InRange(z, 101, 112));
        }

        [Fact]
        public void WithoutTheOpeningZone_AnotherSegmentOpens()
        {
            var title = new TitleFlythrough(Scene(), zone => zone != 112, new Random(3));
            Assert.NotNull(title.Segment);
            Assert.NotEqual(112, title.ZoneId);
        }

        [Fact]
        public void StaysBlackAndStill_UntilItsZoneIsShown_ThenFadesIn()
        {
            var title = new TitleFlythrough(Scene(), random: new Random(1));
            for (int i = 0; i < 120; i++) title.Update(Frame, zoneShown: false);
            Assert.True(title.IsWaitingForZone);
            Assert.Equal(0, title.Frame);
            Assert.True(title.Presentation.SceneColor.X < 0.01f);

            title.Update(Frame, zoneShown: true);
            Assert.False(title.IsWaitingForZone);
            Assert.True(title.Presentation.TryGetCamera(out _));
            for (int i = 0; i < 50; i++) title.Update(Frame, zoneShown: true);
            Assert.InRange(title.Presentation.SceneColor.X, 0.4f, 0.6f);
            for (int i = 0; i < 60; i++) title.Update(Frame, zoneShown: true);
            Assert.Equal(1f, title.Presentation.SceneColor.X, 3);

            // The zone gone (the view moved on): the clock stands still.
            double frame = title.Frame;
            for (int i = 0; i < 30; i++) title.Update(Frame, zoneShown: false);
            Assert.Equal(frame, title.Frame);
        }

        [Fact]
        public void FollowsTheSegmentsWeatherAndClock_AndEndsBlack()
        {
            var title = new TitleFlythrough(Scene(), random: new Random(1));
            title.Update(Frame, zoneShown: true);
            Assert.Equal(("suny", 12f), (title.Weather, title.Hour));
            for (int i = 0; i < 210; i++) title.Update(Frame, zoneShown: true);
            Assert.Equal("mist", title.Weather);
            Assert.Equal(19f, title.Hour, 3);
            Assert.Equal(TitleSceneResource.OpeningSegmentNumber, title.Segment!.Number);

            // At its end the screen is black and the next segment waits for its zone.
            int nextZone = title.NextZoneId;
            Assert.NotEqual(0, nextZone);
            while (!title.IsWaitingForZone) title.Update(Frame, zoneShown: true);
            Assert.Equal(nextZone, title.ZoneId);
            Assert.True(title.Presentation.SceneColor.X < 0.01f);
        }

        [Fact]
        public void Skip_GoesToTheNextSegmentFromBlack()
        {
            var title = new TitleFlythrough(Scene(), random: new Random(1));
            for (int i = 0; i < 300; i++) title.Update(Frame, zoneShown: true);
            int next = title.NextZoneId;

            title.Skip();

            Assert.True(title.IsWaitingForZone);
            Assert.Equal(next, title.ZoneId);
            Assert.True(title.Presentation.SceneColor.X < 0.01f);
            title.Update(Frame, zoneShown: true);
            for (int i = 0; i < 110; i++) title.Update(Frame, zoneShown: true);
            Assert.Equal(1f, title.Presentation.SceneColor.X, 3);
        }

        [Fact]
        public void WithoutChoices_PlaysInFileOrder()
        {
            var builder = new TitleSceneBuilder().Route("cam0", 0f, 10f);
            for (int i = 1; i <= 13; i++) builder.Segment($"s{i:000}", 100 + i, "cam0", 300);
            var title = new TitleFlythrough(TitleSceneResource.Parse(builder.Build())!);
            Assert.Equal(new[] { 112, 113, 101, 102 }, PlayedZones(title, 4));
        }
    }
}
