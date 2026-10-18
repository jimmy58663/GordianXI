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

        private static int VertexCount(List<MeshGroup> meshes)
        {
            int count = 0;
            for (int i = 0; i < meshes.Count; i++) count += meshes[i].Vertices.Length;
            return count;
        }
    }
}
