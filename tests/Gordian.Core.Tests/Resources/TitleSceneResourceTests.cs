// tests/Gordian.Core.Tests/Resources/TitleSceneResourceTests.cs
using System.IO;
using System.Linq;
using Gordian.Core.Resources;
using Gordian.Core.Resources.Events;
using Gordian.Tests.Shared;
using Xunit;

namespace Gordian.Core.Tests.Resources
{
    /// <summary>The title fly-through file (<c>ROM/0/23</c>, #552): its segments, the <c>loop</c> choices and the title ops.</summary>
    public class TitleSceneResourceTests
    {
        private const string GameDirectory = @"G:\Program Files (x86)\PlayOnline\SquareEnix\FINAL FANTASY XI";

        [Fact]
        public void Parse_ReadsSegmentsInFileOrder_TheLoopChoices_AndTheTitleOps()
        {
            var bytes = new TitleSceneBuilder()
                .Route("ca01", 0f, 10f)
                .Route("cb01", 0f, 10f)
                .Segment("mov1", 102, "ca01", 1000, "suny", 2592000,
                    (300, TitleSceneBuilder.Weather("rain")), (300, TitleSceneBuilder.Clock(19 * 216000)), (400, TitleSceneBuilder.UseEnvironment("s101")))
                .Segment("ex1a", 130, "cb01", 800)
                .Segment("ex2e", 24, "cb01", 800)
                .Loop((0, new[] { "mov1" }), (1, new[] { "ex1a", "nope" }))
                .Environment("s101")
                .Build();

            var scene = TitleSceneResource.Parse(bytes);

            Assert.NotNull(scene);
            Assert.Equal(new[] { (1, "mov1", 102), (2, "ex1a", 130), (3, "ex2e", 24) }, scene!.Segments.Select(s => (s.Number, s.Name, s.ZoneId)));
            Assert.Equal(2, scene.Groups.Count);
            Assert.Equal(0, scene.Groups[0].Expansion);
            Assert.Equal("mov1", Assert.Single(scene.Groups[0].Segments).Name);
            Assert.Equal(1, scene.Groups[1].Expansion);
            Assert.Equal("ex1a", Assert.Single(scene.Groups[1].Segments).Name);
            Assert.True(scene.TryGetEnvironment("s101", out _));
            Assert.Equal(2, scene.Scene.Routes.Count);

            var commands = scene.Segments[0].Routine.Commands;
            Assert.Contains(commands, c => c.Kind == SceneCommandKind.TitleZone && c.Number == 102 && c.StartFrame == 0);
            Assert.Contains(commands, c => c.Kind == SceneCommandKind.TitleWeather && c.Reference == "rain" && c.StartFrame == 300);
            Assert.Contains(commands, c => c.Kind == SceneCommandKind.TitleClock && c.Number == 19 * 216000 && c.StartFrame == 300);
            Assert.Contains(commands, c => c.Kind == SceneCommandKind.TitleEnvironment && c.Reference == "s101" && c.StartFrame == 400);
            Assert.Contains(commands, c => c.Kind == SceneCommandKind.Camera && c.Reference == "ca01" && c.Duration == 1000);
            Assert.Equal(1000, scene.Segments[0].Routine.TotalFrames);
        }

        [Theory]
        [InlineData(2592000u, 12f)]
        [InlineData(900000u, 4f + 10f / 60f)]
        [InlineData(4968000u, 23f)]
        [InlineData(0u, 0f)]
        public void HourOf_IsTheClockIn216000thsOfAnHour(uint clock, float hour) =>
            Assert.Equal(hour, TitleSceneResource.HourOf(clock), 3);

        [Fact]
        public void Parse_WithoutZones_IsNull() =>
            Assert.Null(TitleSceneResource.Parse(new TitleSceneBuilder().Route("ca01", 0f, 1f).Build()));

        /// <summary>The retail file: 22 segments, segment 12 North Gustaberg, the four choices of loop (2026-11-13, #552).</summary>
        [Fact]
        public void RetailFile_HasTwentyTwoSegments_AndFourChoices()
        {
            if (!Directory.Exists(GameDirectory)) return;
            var rm = new ResourceManager(GameDirectory);
            rm.InitializeFileTable();
            var scene = TitleSceneResource.Parse(rm.LoadDatBytesByFileId(TitleSceneResource.FileId));

            Assert.NotNull(scene);
            Assert.Equal(22, scene!.Segments.Count);
            Assert.Equal(317, scene.Scene.Routes.Count);
            var opening = scene.Opening!;
            Assert.Equal(("mov5", 106), (opening.Name, opening.ZoneId));
            Assert.Equal(new[] { 68, 51, 61, 54, 50, 130, 123, 121, 141, 126, 111, 106, 104, 102, 103, 109, 2, 29, 33, 4, 24, 25 },
                scene.Segments.Select(s => s.ZoneId));
            Assert.Equal(new[] { 0, 1, 2, 3 }, scene.Groups.Select(g => g.Expansion));
            Assert.Equal(new[] { "mov1", "mov2", "mov3", "mov5", "mov4", "mov6", "mov7", "mov8" }, scene.Groups[0].Segments.Select(s => s.Name));
            Assert.Equal(new[] { "ex1a", "ex1b", "ex1c" }, scene.Groups[1].Segments.Select(s => s.Name));
            Assert.Equal(new[] { "ex2a", "ex2d", "ex2b", "ex2c" }, scene.Groups[2].Segments.Select(s => s.Name));
            Assert.Equal(new[] { "ex3a", "ex3d", "ex3e", "ex3c" }, scene.Groups[3].Segments.Select(s => s.Name));
            // Segments 4, 21 and 22 are in no choice.
            var chosen = scene.Groups.SelectMany(g => g.Segments).Select(s => s.Number).ToHashSet();
            Assert.Equal(new[] { 4, 21, 22 }, scene.Segments.Select(s => s.Number).Where(n => !chosen.Contains(n)));
            // mov1 (La Theine) lights its scene with the file's own s101; the other two environments are not named.
            Assert.Contains(scene.Segments.Single(s => s.Name == "mov1").Routine.Commands, c => c.Kind == SceneCommandKind.TitleEnvironment && c.Reference == "s101");
            Assert.True(scene.TryGetEnvironment("s101", out _));
            // Every shot names a route of the file.
            foreach (var segment in scene.Segments)
            {
                foreach (var command in segment.Routine.Commands.Where(c => c.Kind == SceneCommandKind.Camera))
                {
                    Assert.True(scene.Scene.TryGetRoute(command.Reference, out _), $"{segment.Name}: {command.Reference}");
                }
            }
        }
    }
}
