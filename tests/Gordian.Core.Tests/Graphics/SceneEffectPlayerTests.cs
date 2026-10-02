// tests/Gordian.Core.Tests/Graphics/SceneEffectPlayerTests.cs
using System.IO;
using System.Linq;
using System.Numerics;
using Gordian.Core.Graphics;
using Gordian.Core.Resources;
using Gordian.Core.Resources.Events;
using Xunit;

namespace Gordian.Core.Tests.Graphics
{
    /// <summary>
    /// The effect routines of Port Jeuno event 324's scene files (#192), played from the installed game: the blink
    /// (51402), the sky flash (51327) and the director's <c>fall</c> (51328). Skipped without the game install.
    /// </summary>
    public class SceneEffectPlayerTests
    {
        private const string GameDirectory = @"G:\Program Files (x86)\PlayOnline\SquareEnix\FINAL FANTASY XI";

        private static readonly ZoneParticleFrame ActorFrame = new(new Vector3(0, -2, -10), 0.5f, Vector3.One, Vector3.UnitZ);
        private static readonly ZoneParticleFrame CameraFrame = new(Vector3.Zero, 0.5f, Vector3.One, Vector3.UnitZ);

        private static (SceneEffectPlayer Player, ActorEffectSet Effects)? Open(int fileId)
        {
            if (!Directory.Exists(GameDirectory)) return null;
            var rm = new ResourceManager(GameDirectory);
            rm.InitializeFileTable();
            var effects = rm.GetSceneEffects(fileId);
            Assert.NotNull(effects);
            var resource = EventSceneResource.Parse(rm.LoadDatBytesByFileId(fileId)!);
            return (new SceneEffectPlayer(resource, effects!), effects!);
        }

        private static string[] Live(SceneEffectPlayer player) =>
            player.Instance.Emitters.Where(e => e.Emitter.Particles.Count > 0).Select(e => e.Layer.Name).OrderBy(n => n).ToArray();

        private static void Run(SceneEffectPlayer player, int frames)
        {
            for (int i = 0; i < frames; i += 5) player.Update(5, ActorFrame, CameraFrame);
        }

        [Fact]
        public void Blink_BlackCardHoldsUntilOpenReplacesIt_ThenKillClearsTheScreen()
        {
            if (Open(51402) is not var (player, effects)) return;
            // The blink's cards sit in front of the camera (setup flag 0x04), kept for scene files.
            Assert.All(effects.Layers, l => Assert.True(SceneEffectPlayer.IsCameraSpace(l.Emitter!)));

            Assert.True(player.Start(1, "bl00"));
            Run(player, 600);
            Assert.Equal(new[] { "bk00" }, Live(player)); // bk00 never expires: the screen stays black

            // open: 0x3F replaces the black card with bk01 (150 frames), md00 starts with it.
            Assert.True(player.Start(2, "open"));
            Run(player, 20);
            Assert.Equal(new[] { "bk01", "md00" }, Live(player));
            Run(player, 200);
            Assert.Empty(Live(player));
            Assert.True(player.IsIdle);

            Assert.True(player.Start(3, "bl00"));
            Run(player, 10);
            Assert.True(player.Start(4, "kill"));
            Run(player, 5);
            Assert.Empty(Live(player));
        }

        [Fact]
        public void SkyFlash_RunsItsSubRoutines_AndStopEndsTheLoopingOnes()
        {
            if (Open(51327) is not var (player, _)) return;
            // mai2 starts cas1 and kie0 at once, stops loop at 370 and starts edxx; at 411 tama starts repeating.
            Assert.True(player.Start(1, "mai2"));
            Run(player, 20);
            Assert.Contains("dp02", Live(player)); // kie0's first generator
            Run(player, 400);
            Assert.True(player.RunningCount >= 1); // tama repeats
            Run(player, 300);
            Assert.Contains("bp06", Live(player)); // tama's generators keep coming

            // stop: 0x5F loop at once and 0x5F tama after 160 frames.
            Assert.True(player.Start(2, "stop"));
            Run(player, 200);
            Assert.Equal(0, player.RunningCount);
            Run(player, 400);
            Assert.True(player.IsIdle);
        }

        [Fact]
        public void TaskStop_EndsOnlyItsOwnRoutine()
        {
            if (Open(51327) is not var (player, _)) return;
            // mai1 starts strt at 242 and loop (repeating) at 322; stopping mai1 later leaves loop running.
            Assert.True(player.Start(7, "mai1"));
            Run(player, 330);
            player.Stop(7);
            Run(player, 200);
            Assert.Equal(1, player.RunningCount);
            Assert.Contains("un00", Live(player));
        }

        [Fact]
        public void Fall_SpawnsItsGeneratorsHighAboveTheActor()
        {
            if (Open(51328) is not var (player, _)) return;
            Assert.True(player.Start(1, "fall"));
            Run(player, 40);
            var live = Live(player);
            Assert.Contains("gl00", live);
            Assert.Contains("bl00", live); // the one camera-following card of the file
            // Raw DAT axes: -Y is up; the flash sits about 229 above the director.
            var glow = player.Instance.Emitters.First(e => e.Layer.Name == "gl05").Emitter;
            Assert.Equal(-229f, glow.Template.RawBasePosition.Y, 0);
            Assert.NotEmpty(glow.Particles);
            Assert.False(SceneEffectPlayer.IsCameraSpace(glow.Template));
        }
    }
}
