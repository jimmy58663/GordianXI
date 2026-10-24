// tests/Gordian.Core.Tests/Architecture/LayeringTests.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Gordian.Core.Tests.Architecture
{
    /// <summary>
    /// Keeps the client layered (issue #433): <c>Gordian.Core</c> holds protocol, session, world and entity state and the
    /// DAT resources under them; <c>Gordian.Engine</c> (playback, UI, input, audio, the session composition root) sits on
    /// top. Since the split the compiler stops Core from naming engine types; these tests also scan the IL of both
    /// assemblies (so a future layer inside one assembly can be checked the same way), make every new Core area pick its
    /// layer, and check the assembly boundaries and the kill-switch setter. References that break the rule would be listed
    /// in <see cref="KnownViolations"/>; fixing one means deleting its line, and the test fails on lines that no longer
    /// apply so the list only shrinks. Resources is not a layer of its own: its loaders build world and collision state and
    /// log through Diagnostics, so a separate data layer would need those moved first.
    /// </summary>
    public class LayeringTests
    {
        private enum Layer { Core = 0, Engine = 1 }

        /// <summary>The root namespace of the engine layer: everything under it is <see cref="Layer.Engine"/>.</summary>
        private const string EngineRoot = "Gordian.Engine";

        /// <summary>
        /// The layer of each top-level area of <c>Gordian.Core</c> (the namespace segment after it). Animation holds the
        /// per-entity animation state and motion data that <c>WorldEntity</c> carries and the renderer reads; the playback
        /// that drives it is in <c>Gordian.Engine.Animation</c>.
        /// </summary>
        private static readonly Dictionary<string, Layer> Layers = new(StringComparer.Ordinal)
        {
            ["(root)"] = Layer.Core,
            ["Config"] = Layer.Core,
            ["Diagnostics"] = Layer.Core,
            ["Network"] = Layer.Core,
            ["Profiles"] = Layer.Core,
            ["Resources"] = Layer.Core,
            ["World"] = Layer.Core,
            ["Animation"] = Layer.Core,
        };

        /// <summary>
        /// Today's wrong-way references, as "<c>type -> area</c>" (type names without the
        /// <c>Gordian.Core.</c> prefix). Remove a line when its reference is gone.
        /// </summary>
        private static readonly HashSet<string> KnownViolations = new(StringComparer.Ordinal)
        {
        };

        internal static Assembly[] ScannedAssemblies => new[]
        {
            typeof(Gordian.Core.Config.GordianStorage).Assembly,
            typeof(Gordian.Engine.Sessions.CharacterSession).Assembly,
        };

        [Fact]
        public void CoreDoesNotReferenceTheEngine()
        {
            var core = typeof(Gordian.Core.Config.GordianStorage).Assembly;
            Assert.DoesNotContain(core.GetReferencedAssemblies(), a => a.Name == "Gordian.Engine");
            Assert.All(core.GetTypes(), t => Assert.False(IsUnder(t.Namespace, EngineRoot), $"{t.FullName} is in Gordian.Core"));
        }

        [Fact]
        public void EngineTypesLiveInTheEngineAssembly()
        {
            var engine = typeof(Gordian.Engine.Sessions.CharacterSession).Assembly;
            Assert.All(engine.GetTypes().Where(t => t.Namespace != null && !t.Namespace.StartsWith("<", StringComparison.Ordinal)),
                t => Assert.True(IsUnder(t.Namespace, EngineRoot), $"{t.FullName} is in Gordian.Engine"));
        }

        /// <summary>
        /// The server's feature restrictions are set by Core's packet handling only: the setter is internal, and Core shows its
        /// internals to the engine and the tests, not to the App or the addon runtime (AGENTS.md kill-switch policy).
        /// </summary>
        [Fact]
        public void FeatureRestrictionsCannotBeSetOutsideCore()
        {
            var setter = typeof(Gordian.Core.Config.SessionProfile).GetProperty(nameof(Gordian.Core.Config.SessionProfile.FeatureRestrictions))!.SetMethod!;
            Assert.True(setter.IsAssembly);
            var friends = typeof(Gordian.Core.Config.SessionProfile).Assembly
                .GetCustomAttributes<System.Runtime.CompilerServices.InternalsVisibleToAttribute>()
                .Select(a => a.AssemblyName)
                .ToList();
            Assert.DoesNotContain("Gordian.App", friends);
            Assert.DoesNotContain("Gordian.Addons", friends);
            Assert.DoesNotContain("Gordian.Automation", friends);
        }

        [Fact]
        public void EveryAreaHasALayer()
        {
            var unknown = ScannedAssemblies
                .SelectMany(a => a.GetTypes())
                .Where(t => !IsUnder(t.Namespace, EngineRoot))
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
                .Select(e => (e.From, e.To, FromLayer: LayerOf(e.From), ToLayer: LayerOf(e.To)))
                .Where(e => e.FromLayer != null && e.ToLayer != null && e.ToLayer > e.FromLayer)
                .Select(e => $"{ShortName(e.From)} -> {QualifiedArea(e.To)}")
                .Distinct();
        }

        private static Layer? LayerOf(Type type)
        {
            if (IsUnder(type.Namespace, EngineRoot)) return Layer.Engine;
            if (!IsUnder(type.Namespace, CoreRoot)) return null;
            return Layers.TryGetValue(AreaOf(type)!, out var layer) ? layer : null;
        }

        private const string CoreRoot = "Gordian.Core";

        private static bool IsUnder(string? ns, string root) =>
            ns != null && (ns == root || ns.StartsWith(root + ".", StringComparison.Ordinal));

        /// <summary>
        /// The area of a GordianXI type, prefixed with "Engine." under <see cref="EngineRoot"/> so the two halves of a
        /// split area read apart: the namespace segment after the root (<c>Gordian.Core.Network.Packets</c> is
        /// <c>Network</c>), "(root)" for types directly in it, or null for types outside both roots.
        /// </summary>
        private static string? QualifiedArea(Type type)
        {
            if (IsUnder(type.Namespace, EngineRoot)) return "Engine." + AreaUnder(type.Namespace!, EngineRoot);
            return AreaOf(type);
        }

        private static string? AreaOf(Type type) =>
            IsUnder(type.Namespace, CoreRoot) ? AreaUnder(type.Namespace!, CoreRoot)
            : IsUnder(type.Namespace, EngineRoot) ? AreaUnder(type.Namespace!, EngineRoot)
            : null;

        private static string AreaUnder(string ns, string root)
        {
            if (ns.Length == root.Length) return "(root)";
            string rest = ns.Substring(root.Length + 1);
            int dot = rest.IndexOf('.');
            return dot < 0 ? rest : rest.Substring(0, dot);
        }

        /// <summary>The type's full name without its root, prefixed with "Engine." under <see cref="EngineRoot"/>.</summary>
        private static string ShortName(Type type)
        {
            string name = type.FullName ?? type.Name;
            if (name.StartsWith(EngineRoot + ".", StringComparison.Ordinal)) return "Engine." + name.Substring(EngineRoot.Length + 1);
            if (name.StartsWith(CoreRoot + ".", StringComparison.Ordinal)) return name.Substring(CoreRoot.Length + 1);
            return name;
        }
    }
}
