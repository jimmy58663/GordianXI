// src/Gordian.Core/Resources/Graphics/ZoneMeshTemplates.cs
using System;
using System.Collections.Generic;
using Gordian.Core.Resources.Models;

namespace Gordian.Core.Resources.Graphics
{
    /// <summary>
    /// The zone's 0x2E meshes by the keys ZoneDef placements look them up with (<see cref="ZoneDefDecoder.ResolveTemplate"/>):
    /// each mesh's own name, the last word of a spaced name, and its section id.
    /// <para>
    /// A placement's mesh id matches the 0x2E name field (ZoneDef record +0x00; xi-tools
    /// https://github.com/vekien/xi-tools, docs/zone/format.md), so a real name always wins over a section-id alias,
    /// whatever their vertex counts; only keys of the same kind fall back to the most detailed mesh. Port Jeuno (zone 246)
    /// has a mesh named <c>saku</c> (section <c>sak1</c>, posts and chain along local X) and a mesh <c>saku2</c> in the
    /// section <c>saku</c> (along local Z, 284 vertices against 264). Letting the larger alias replace the real name drew
    /// every airship-dock barrier with <c>saku2</c>, turned 90 degrees across the walkway (#256).
    /// </para>
    /// </summary>
    internal sealed class ZoneMeshTemplates
    {
        /// <summary>A mesh registered under its section id only.</summary>
        public const int SectionIdRank = 0;

        /// <summary>A mesh registered under the last word of its spaced 0x2E name.</summary>
        public const int NameTailRank = 1;

        /// <summary>A mesh registered under its own 0x2E name.</summary>
        public const int NameRank = 2;

        private readonly Dictionary<string, int> _ranks = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, List<MeshGroup>> Templates { get; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Keys that are some mesh's name (or name tail), not only a section id.</summary>
        public HashSet<string> RealNames { get; } = new(StringComparer.OrdinalIgnoreCase);

        public void Register(string name, List<MeshGroup> meshes, int rank)
        {
            if (string.IsNullOrWhiteSpace(name)) return;
            if (rank > SectionIdRank) RealNames.Add(name);

            if (Templates.TryGetValue(name, out var existing))
            {
                int existingRank = _ranks[name];
                if (rank < existingRank) return;
                if (rank == existingRank && VertexCount(meshes) <= VertexCount(existing)) return;
            }

            Templates[name] = meshes;
            _ranks[name] = rank;
        }

        /// <summary>
        /// Adds a 0x2E section to the by-section-id table that generators link their meshes through. The first section with
        /// an id is kept, except that a later section whose own name is that id replaces one named otherwise. South
        /// Gustaberg (zone 107) has the sea-cliff terrain meshes <c>umigake01_m</c>-<c>umigake03_m</c> in sections
        /// <c>umi1</c>-<c>umi3</c> and, later, the flat wave-foam meshes named <c>umi1</c>-<c>umi3</c> (texture
        /// <c>umna</c>) in sections of the same ids. The wave generators <c>nam1</c>-<c>nam5</c> link <c>umi1</c>-<c>umi3</c>;
        /// taking the first section drew the cliffs, stretched 16-17 times, as dark slabs over the bay (#341).
        /// </summary>
        public static void AddSection(Dictionary<string, List<MeshGroup>> sections, string key, string sectionId, List<MeshGroup> meshes)
        {
            if (meshes.Count == 0) return;
            if (!sections.TryGetValue(key, out var existing) ||
                (!IsNamed(existing, sectionId) && IsNamed(meshes, sectionId)))
            {
                sections[key] = meshes;
            }
        }

        /// <summary>The by-section-id key of a 0x2E section within one directory (e.g. <c>f_ro/mode/ligh/g_li</c>).</summary>
        public static string DirectoryKey(string directory, string sectionId) => $"dir:{directory}/{sectionId}";

        /// <summary>
        /// The 0x2E section a generator in <paramref name="directory"/> links as <paramref name="sectionId"/>: the one in its
        /// own directory, else in the nearest directory above it; null when no directory on that path holds one (the caller
        /// then falls back to the whole file). Section ids are four characters, so one id can name several meshes in a file.
        /// West Ronfaure (zone 100) holds two sections <c>san_</c>: the zone-line passage <c>san_zone01_m</c> in
        /// <c>f_ro/mode</c> and, later, the gate interior's light overlay <c>san_gate_inl</c> in <c>f_ro/mode/ligh/g_li</c>,
        /// next to the generator <c>gin2</c> that links it. Taking the first in the file drew the passage, unturned and
        /// additive, as translucent beams across the San d'Oria gate (#494); <c>gin1</c>'s <c>san2</c> likewise drew the
        /// emblem <c>san_emblem</c> instead of <c>san_gate1_inl</c>. The scope order is an *inference* from those data, as
        /// with the generators' point lists; no reference describes how the client resolves the link.
        /// </summary>
        public static List<MeshGroup>? FindInScope(Dictionary<string, List<MeshGroup>> sections, string? directory, string sectionId)
        {
            if (directory == null) return null;
            string dir = directory;
            while (true)
            {
                if (sections.TryGetValue(DirectoryKey(dir, sectionId), out var meshes)) return meshes;
                if (dir.Length == 0) return null;
                int slash = dir.LastIndexOf('/');
                dir = slash < 0 ? string.Empty : dir.Substring(0, slash);
            }
        }

        private static bool IsNamed(List<MeshGroup> meshes, string sectionId) =>
            meshes.Count > 0 && string.Equals(meshes[0].Name, sectionId, StringComparison.OrdinalIgnoreCase);

        private static int VertexCount(List<MeshGroup> meshes)
        {
            int count = 0;
            for (int i = 0; i < meshes.Count; i++) count += meshes[i].Vertices.Length;
            return count;
        }
    }
}
