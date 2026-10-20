// tests/Gordian.Core.Tests/Animation/SynthesisEffectRealDataTests.cs
using System.IO;
using System.Linq;
using Gordian.Core.Animation;
using Gordian.Core.Resources;
using Gordian.Core.Resources.Events;
using Gordian.Core.Resources.Models;
using Gordian.Core.Resources.Tables;
using Xunit;

namespace Gordian.Core.Tests.Animation
{
    /// <summary>
    /// The synthesis crystal effect (#315) on the retail DATs: file 4912 + S2C 0x030's EffectNum, its <c>frst</c>, the endings
    /// and the body's <c>ri0?</c>. Skipped without the game install.
    /// </summary>
    public class SynthesisEffectRealDataTests
    {
        private const string GameDirectory = @"G:\Program Files (x86)\PlayOnline\SquareEnix\FINAL FANTASY XI";

        private static ResourceManager? Retail()
        {
            if (!Directory.Exists(GameDirectory)) return null;
            var rm = new ResourceManager(GameDirectory);
            rm.InitializeFileTable();
            return rm;
        }

        /// <summary>
        /// Each element's file holds the crystal and the timeline (texture cris_aq ... cris_dk in LandSandBoat's SynthesisEffect
        /// order), and the built main runs frst, the result's ending when frst ends (630 ticks), then stcr.
        /// </summary>
        [Fact]
        public void EveryElement_ResolvesToItsCrystalFile_WithTheEndingForTheResult()
        {
            var rm = Retail();
            if (rm == null) return;
            var library = new ActionMotionLibrary(rm.LoadDatBytesByFileId);
            string[] textures = ["cris_aq", "cris_ar", "cris_ig", "cris_tr", "cris_th", "cris_ic", "cris_lt", "cris_dk"];
            for (int effect = 0x10; effect <= 0x17; effect++)
            {
                byte[] bytes = rm.LoadDatBytesByFileId(4912 + effect)!;
                Assert.Contains(textures[effect - 0x10], System.Text.Encoding.ASCII.GetString(bytes));
                foreach (var (type, ending) in new[] { (0, "ednn"), (1, "edmm"), (2, "edxx"), (4, "edxx") })
                {
                    var asset = library.ResolveSynthesis(effect, type)!;
                    Assert.Equal(ActionAssetKind.Synthesis, asset.Kind);
                    Assert.True(asset.HoldsBodyToEnd);
                    Assert.True(asset.Effects!.TryGetRoutine(ActionAsset.MainRoutine, out var main));
                    var starts = main.Commands.Where(c => c.Kind == SceneCommandKind.StartRoutine).Select(c => (c.StartFrame, c.Reference)).ToArray();
                    Assert.Equal((0, "frst"), starts[0]);
                    Assert.Equal((630, ending), starts[1]);
                    Assert.Equal("stcr", starts[2].Reference);
                    Assert.True(asset.Effects.TryGetRoutine("strt", out var strt));
                    Assert.Contains(strt.Commands, c => c.Kind == SceneCommandKind.SpawnGenerator && c.Reference == "cri0");
                }
            }
            Assert.Null(library.ResolveSynthesis(0x18, 1));
            // The renderer draws the file's generators by file id: they decode, the crystal and its textures with them.
            var effects = rm.GetSceneEffects(4912 + 0x14)!;
            Assert.True(effects.Layers.Count >= 20, $"{effects.Layers.Count} layers");
            Assert.Contains(effects.Textures.Keys, k => k.Contains("cris_th", System.StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// On a Tarutaru the built main plays ri0? (the hand up with the crystal) from frst's start and the body holds it to
        /// the end of the ending; the sounds of frst's strt / loop and of the ending follow it.
        /// </summary>
        [Fact]
        public void OnTheCrafter_TheBodyHoldsTheCrystalUp_AndTheEndingSoundsPlay()
        {
            var rm = Retail();
            if (rm == null) return;
            var model = EntityModelLoader.AssembleCharacter(CharacterRace.TaruMale, 1, new ushort[9], rm.LoadDatBytes, rm.LoadDatBytesByFileId)!;
            var library = new ActionMotionLibrary(rm.LoadDatBytesByFileId);
            var asset = library.ResolveSynthesis(0x11, 1)!; // wind, success
            var routine = asset.Motion!.BuildOnModel(model, ActionAsset.MainRoutine)!;
            Assert.Equal("ri0", routine.Segments[0].ClipName.TrimEnd('?'));
            Assert.Equal(0, routine.Segments[0].Loops); // held until replaced
            Assert.True(routine.TotalTicks > 630);
            var sounds = asset.Motion.GetSoundsOnModel(model, ActionAsset.MainRoutine);
            Assert.Contains(sounds, c => c.Tick >= 630);
            Assert.Contains(sounds, c => c.Tick < 630);
        }
    }
}
