// tests/Gordian.Core.Tests/Events/SceneSoundTests.cs
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Gordian.Core.Events;
using Gordian.Core.Resources;
using Gordian.Core.Resources.Events;
using Xunit;
using Xunit.Abstractions;

namespace Gordian.Core.Tests.Events
{
    /// <summary>Cutscene routine sounds: sound commands and sound generators of scene DATs (#167, in-game round 2).</summary>
    public class SceneSoundTests
    {
        private const string GameDirectory = @"G:\Program Files (x86)\PlayOnline\SquareEnix\FINAL FANTASY XI";
        private readonly ITestOutputHelper _output;

        public SceneSoundTests(ITestOutputHelper output) => _output = output;

        /// <summary>The scene tasks Port Jeuno 324 starts, in order (file, routine), from the scripted run.</summary>
        private static readonly (int File, string Routine)[] PortJeuno324Tasks =
        {
            (51402, "kill"), (30904, "fdo1"), (57129, "aop1"), (30904, "fdi1"), (57129, "a110"), (70443, "s002"), (57129, "se00"),
            (57129, "a111"), (57129, "a112"), (51327, "mai1"), (57129, "a113"), (57129, "0pro"), (57129, "0dkn"), (51327, "mai2"),
            (57129, "a120"), (57129, "kpro"), (57129, "a116"), (51328, "fall"), (57129, "0rak"), (57129, "a117"), (51327, "stop"),
            (30905, "who1"), (57129, "krak"), (57129, "ke00"), (70443, "kil2"), (30905, "whi1"), (51402, "bl00"), (57129, "a119"),
            (51402, "open"), (51402, "clos"), (57129, "a121"), (57129, "a122"), (57129, "a123"), (57129, "a127"), (57129, "a124"),
            (57129, "a125"), (57129, "a126"), (57129, "a128"), (57129, "a129"), (57129, "a130"), (57129, "a131"), (57129, "a132"),
            (57129, "a133"), (57129, "a134"), (57129, "0blf"),
        };

        [Fact]
        public void Retail_PortJeuno324_SchedulesItsRoutineSounds()
        {
            if (!Directory.Exists(GameDirectory))
            {
                return;
            }

            var rm = new ResourceManager(GameDirectory);
            rm.InitializeFileTable();
            double clock = 0;
            var presentation = new EventPresentation { Clock = () => clock };
            var files = new Dictionary<int, EventSceneResource>();
            int task = 0;
            foreach (var (file, name) in PortJeuno324Tasks)
            {
                if (!files.TryGetValue(file, out var resource))
                {
                    files[file] = resource = EventSceneResource.Parse(rm.LoadDatBytesByFileId(file)!);
                }

                if (resource.TryGetRoutine(name, out var routine))
                {
                    presentation.Play(++task, resource, routine, Vector3.Zero, file, 1, 1, 0f);
                }
            }

            clock = 1e6;
            var sounds = new List<EventPresentation.SceneSound>();
            presentation.TakeDueSounds(sounds);
            _output.WriteLine($"{sounds.Count} sounds: " + string.Join(", ", sounds.Select(s => $"{s.SoundId}(op {s.Opcode:X2})")));
            Assert.Contains(sounds, s => s.SoundId == 41060); // se00
            Assert.Contains(sounds, s => s.SoundId == 8158);  // who1
            Assert.Contains(sounds, s => s.SoundId == 34125); // 0pro
            // The lightning (0rak): 6041 and the looping 2088, authored with a 3000-yalm range; krak kills 2088.
            Assert.Contains(sounds, s => s.SoundId == 36041 && s.Generator == "6041" && s.Far == 3000f);
            Assert.Contains(sounds, s => s.SoundId == 2088 && s.Far == 3000f);
            Assert.Contains(sounds, s => s.Opcode == 0x1E && s.Generator == "2088");
            presentation.TakeDueSounds(sounds = new List<EventPresentation.SceneSound>());
            Assert.Empty(sounds); // handed out once
        }
    }
}
