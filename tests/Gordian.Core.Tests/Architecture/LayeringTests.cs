// tests/Gordian.Core.Tests/Architecture/LayeringTests.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Gordian.Core.Tests.Architecture
{
    /// <summary>
    /// Keeps the client's namespaces layered (issue #433): data parsing at the bottom, then
    /// protocol, session and world state, then the engine (playback, UI, input, audio) on
    /// top. A type may refer to types in its own layer or a lower one, never a higher one.
    /// References that break the rule today are listed in <see cref="KnownViolations"/>;
    /// fixing one means deleting its line, and the test fails on lines that no longer apply
    /// so the list only shrinks.
    /// </summary>
    public class LayeringTests
    {
        private enum Layer { Data = 0, Core = 1, Engine = 2 }

        /// <summary>The layer of each top-level area (the namespace segment after <c>Gordian.&lt;Assembly&gt;</c>).</summary>
        private static readonly Dictionary<string, Layer> Layers = new(StringComparer.Ordinal)
        {
            ["Resources"] = Layer.Data,

            ["(root)"] = Layer.Core,
            ["Config"] = Layer.Core,
            ["Diagnostics"] = Layer.Core,
            ["Network"] = Layer.Core,
            ["Profiles"] = Layer.Core,
            ["World"] = Layer.Core,

            ["Actions"] = Layer.Engine,
            ["Animation"] = Layer.Engine,
            ["Audio"] = Layer.Engine,
            ["Events"] = Layer.Engine,
            ["Graphics"] = Layer.Engine,
            ["Input"] = Layer.Engine,
            ["Macros"] = Layer.Engine,
            ["Ui"] = Layer.Engine,
        };

        /// <summary>
        /// Today's wrong-way references, as "<c>type -> area</c>" (type names without the
        /// <c>Gordian.Core.</c> prefix). Remove a line when its reference is gone.
        /// </summary>
        private static readonly HashSet<string> KnownViolations = new(StringComparer.Ordinal)
        {
            "Network.CharacterSession -> Actions",
            "Network.CharacterSession -> Animation",
            "Network.CharacterSession -> Events",
            "Network.CharacterSession -> Input",
            "Network.CharacterSession -> Macros",
            "Network.CharacterSession -> Ui",
            "Network.PacketParser -> Actions",
            "Network.PacketParser -> Animation",
            "Network.Packets.CombatLogFormatter -> Actions",
            "Network.Packets.CombatLogFormatter -> Events",
            "Network.Packets.StandardMessages -> Ui",
            "Network.SessionNetworkManager -> Actions",
            "Network.SessionNetworkManager -> Animation",
            "Resources.EntityModelLoader -> Audio",
            "Resources.EntityModelLoader -> Diagnostics",
            "Resources.Events.EventSceneResource -> Audio",
            "Resources.Graphics.SharedHitRoutines -> Network",
            "Resources.Graphics.TextureDecoder -> Diagnostics",
            "Resources.Graphics.WeatherSkyLayer -> Graphics",
            "Resources.Graphics.ZoneCollisionDecoder -> World",
            "Resources.Graphics.ZoneEnvironmentData -> World",
            "Resources.Models.ZoneGeometry -> Graphics",
            "Resources.Models.ZoneGeometry -> World",
            "Resources.Models.ZoneVessel -> World",
            "Resources.MountModels -> Graphics",
            "Resources.MountModels -> World",
            "Resources.ResourceManager -> Animation",
            "Resources.ResourceManager -> Diagnostics",
            "Resources.ResourceManager -> Graphics",
            "Resources.ResourceManager -> World",
            "Resources.Ui.UiResourceLibrary -> Diagnostics",
            "Resources.Vfs.DatOverlayPack -> Diagnostics",
            "Resources.Vfs.ModernAssetPack -> Diagnostics",
            "Resources.Vfs.VfsConfig -> Diagnostics",
            "Resources.Vfs.VfsHotReloadWatcher -> Diagnostics",
            "Resources.Vfs.VirtualFileSystem -> Diagnostics",
            "Resources.ZoneDataLoader -> Diagnostics",
            "Resources.ZoneDataLoader -> Graphics",
            "Resources.ZoneDataLoader -> World",
            "World.ActorEffectRequest -> Animation",
            "World.WorldEntity -> Animation",
        };

        private static Assembly[] ScannedAssemblies => new[] { typeof(Gordian.Core.Config.GordianStorage).Assembly };

        [Fact]
        public void EveryAreaHasALayer()
        {
            var unknown = ScannedAssemblies
                .SelectMany(a => a.GetTypes())
                .Select(AreaOf)
                .Where(a => a != null && !Layers.ContainsKey(a))
                .Distinct()
                .ToList();
            Assert.True(unknown.Count == 0, "Areas with no layer in LayeringTests.Layers: " + string.Join(", ", unknown));
        }

        [Fact]
        public void NoReferencePointsToAHigherLayer()
        {
            var found = ScannedAssemblies.SelectMany(FindViolations).ToHashSet(StringComparer.Ordinal);
            var added = found.Where(v => !KnownViolations.Contains(v)).OrderBy(v => v, StringComparer.Ordinal).ToList();
            var fixedOnes = KnownViolations.Where(v => !found.Contains(v)).OrderBy(v => v, StringComparer.Ordinal).ToList();

            Assert.True(added.Count == 0,
                "New references to a higher layer (move the type, or have the lower layer publish state the higher one reads):\n  "
                + string.Join("\n  ", added));
            Assert.True(fixedOnes.Count == 0,
                "These known violations are gone; delete them from LayeringTests.KnownViolations:\n  "
                + string.Join("\n  ", fixedOnes));
        }

        /// <summary>Every reference in <paramref name="assembly"/> from a type to an area in a higher layer, as "<c>type -> area</c>".</summary>
        internal static IEnumerable<string> FindViolations(Assembly assembly)
        {
            return TypeDependencyScanner.Scan(assembly)
                .Select(e => (e.From, FromArea: AreaOf(e.From), ToArea: AreaOf(e.To)))
                .Where(e => e.FromArea != null && e.ToArea != null
                    && Layers.TryGetValue(e.FromArea, out var from)
                    && Layers.TryGetValue(e.ToArea, out var to)
                    && to > from)
                .Select(e => $"{ShortName(e.From)} -> {e.ToArea}")
                .Distinct();
        }

        /// <summary>
        /// The area of a GordianXI type: the namespace segment after <c>Gordian.&lt;Assembly&gt;</c>
        /// (<c>Gordian.Core.Network.Packets</c> is <c>Network</c>), "(root)" for types directly in
        /// it, or null for types outside the scanned libraries.
        /// </summary>
        private static string? AreaOf(Type type)
        {
            string? ns = type.Namespace;
            if (ns == null || !IsScannedRoot(ns, out int rootLength)) return null;
            if (ns.Length == rootLength) return "(root)";
            string rest = ns.Substring(rootLength + 1);
            int dot = rest.IndexOf('.');
            return dot < 0 ? rest : rest.Substring(0, dot);
        }

        private static bool IsScannedRoot(string ns, out int rootLength)
        {
            foreach (var asm in ScannedAssemblies)
            {
                string root = asm.GetName().Name!;
                if (ns == root || ns.StartsWith(root + ".", StringComparison.Ordinal))
                {
                    rootLength = root.Length;
                    return true;
                }
            }
            rootLength = 0;
            return false;
        }

        private static string ShortName(Type type)
        {
            string name = type.FullName ?? type.Name;
            foreach (var asm in ScannedAssemblies)
            {
                string prefix = asm.GetName().Name + ".";
                if (name.StartsWith(prefix, StringComparison.Ordinal)) return name.Substring(prefix.Length);
            }
            return name;
        }
    }
}
