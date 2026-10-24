// tests/Gordian.Engine.Tests/Graphics/ZoneFootEffectsTests.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Gordian.Engine.Graphics;
using Gordian.Core.Resources;
using Gordian.Core.Resources.Graphics;
using Xunit;

namespace Gordian.Engine.Tests.Graphics
{
    /// <summary>A zone's foot effects and the shared footprint (#40).</summary>
    public class ZoneFootEffectsTests
    {
        private const string GameDirectory = @"G:\Program Files (x86)\PlayOnline\SquareEnix\FINAL FANTASY XI";

        [Theory]
        [InlineData("0300", 3)]
        [InlineData("0400", 4)]
        [InlineData("0a00", 10)]
        [InlineData("a301", -1)]
        [InlineData("0301", -1)]
        [InlineData("w300", -1)]
        [InlineData("030", -1)]
        public void TerrainOfRoot_ReadsTheHexDigit(string name, int terrain) =>
            Assert.Equal(terrain, ZoneFootEffects.TerrainOfRoot(name));

        [Fact]
        public void FootEffectDirectories_AreTheDryAndRainSets()
        {
            Assert.True(ZoneFootEffects.IsFootEffectDirectory("f_pa/fses/fefs", out bool wet));
            Assert.False(wet);
            Assert.True(ZoneFootEffects.IsFootEffectDirectory("f_pa/fser/fefr", out wet));
            Assert.True(wet);
            Assert.False(ZoneFootEffects.IsFootEffectDirectory("f_pa/effe/fefs2", out _));
            Assert.False(ZoneFootEffects.IsFootEffectDirectory(null, out _));
        }

        [Fact]
        public void RootFor_TakesTheRainSetWhenWet_ElseTheDrySet()
        {
            var sandDry = new WeatherSkyLayer { Name = "0300" };
            var pathDry = new WeatherSkyLayer { Name = "0100" };
            var pathWet = new WeatherSkyLayer { Name = "0100" };
            var effects = new ActorEffectSet(new[] { sandDry, pathDry, pathWet }, Array.Empty<WeatherRoutineVariant>(), new Dictionary<string, DecodedTexture>());
            var feet = new ZoneFootEffects(effects, new Dictionary<int, WeatherSkyLayer> { [3] = sandDry, [1] = pathDry }, new Dictionary<int, WeatherSkyLayer> { [1] = pathWet });
            Assert.Same(pathWet, feet.RootFor(1, wet: true));
            Assert.Same(pathDry, feet.RootFor(1, wet: false));
            Assert.Same(sandDry, feet.RootFor(3, wet: true)); // no rain root: the dry one
            Assert.Null(feet.RootFor(5, wet: false));
        }

        /// <summary>The footprint of ROM/0/0 and its ops: FootMark 0x8E, ground colour 0x8F, the default Z-first rotation.</summary>
        [Fact]
        public void Retail_FootprintGenerator()
        {
            if (!Directory.Exists(GameDirectory)) return;
            var rm = new ResourceManager(GameDirectory);
            rm.InitializeFileTable();
            byte[] dat = rm.LoadDatBytes(Path.Combine("ROM", "0", "0.DAT"))!;
            var root = Gordian.Core.Resources.Containers.DatDirectoryTree.Build(dat);
            var entry = root.CollectByTypeRecursive(Gordian.Core.Resources.Containers.DatSectionType.ParticleGenerator).Single(e => e.DatId == "fmrk");
            Assert.Equal("fbib", entry.Parent.DatId);
            var fmrk = ParticleGeneratorDecoder.DecodeGenerator(entry.Payload.Span, "fmrk")!;
            Assert.True(fmrk.FootMark);
            Assert.True(fmrk.GroundColored);
            Assert.False(fmrk.Setup!.RotationOrderZyx);
            Assert.Equal(ParticleLinkedDataType.SpriteSheet, fmrk.Setup.LinkedDataType);
            Assert.Equal("fmk1", fmrk.Setup.LinkedDataId);
            Assert.Equal(180, fmrk.Setup.MaxLifeSpan);
            Assert.Equal(ParticleBlendFunc.SrcInvSrcAdd, fmrk.BlendFunc);
            Assert.Equal(0.15f, fmrk.Scale.X, 2);
            Assert.Equal(0.19f, fmrk.Scale.Y, 2);
            Assert.Equal(-MathF.PI / 2, fmrk.Rotation.X, 3);
            Assert.Equal(MathF.PI / 2, fmrk.Rotation.Z, 3);

            var shared = rm.GetSharedRoutineEffects()!.Value.Effects;
            var layer = shared.Layers.Single(l => l.Name == "fmrk");
            Assert.True(layer.GroundColored);
            Assert.NotNull(layer.Emitter);
        }

        /// <summary>
        /// Bibiki Bay's foot effects: every dry and rain root resolves (the static meshes hit7 / asi1 / asi2 from ROM/0/0),
        /// the sand carrier spawns its puff and sparkles, and the puffs take the ground's colour.
        /// </summary>
        [Fact]
        public void Retail_BibikiBayFootEffects()
        {
            if (!Directory.Exists(GameDirectory)) return;
            var rm = new ResourceManager(GameDirectory);
            rm.InitializeFileTable();
            Assert.True(rm.TryLoadZone(4, out var zone, out _));
            var feet = zone.FootEffects!;
            Assert.Equal(new[] { 1, 3, 4, 8, 9 }, feet.Dry.Keys.OrderBy(k => k).ToArray());
            Assert.Equal(new[] { 1, 2, 3, 4, 8, 9 }, feet.Wet.Keys.OrderBy(k => k).ToArray());

            var sand = feet.Dry[3];
            Assert.Equal("hit7", sand.DatId);
            Assert.Equal(new[] { "a301", "a302" }, sand.Emitter!.Children.Keys.OrderBy(k => k).ToArray());
            var puff = feet.Effects.Layers.Single(l => ReferenceEquals(l.Emitter, sand.Emitter.Children["a301"]));
            Assert.True(puff.GroundColored);
            Assert.True(puff.Emitter!.ChildOnly);
            // The rain path is a flat ripple, the dry one a dust puff.
            Assert.Equal("asi2", feet.Wet[1].DatId);
            Assert.Equal("hi11", feet.Dry[1].DatId);
            // The foot effects are not zone effect layers.
            Assert.DoesNotContain(zone.EffectLayers, l => l.Name is "0300" or "a301");
            Assert.All(feet.Effects.Layers, l => Assert.True(l.MeshGroups.Count > 0, l.Name));
        }
    }
}
