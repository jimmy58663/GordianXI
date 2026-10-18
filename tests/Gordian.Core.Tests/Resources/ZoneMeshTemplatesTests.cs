// tests/Gordian.Core.Tests/Resources/ZoneMeshTemplatesTests.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Gordian.Core.Resources.Graphics;
using Gordian.Core.Resources.Models;
using Xunit;

namespace Gordian.Core.Tests.Resources
{
    public class ZoneMeshTemplatesTests
    {
        private static List<MeshGroup> Mesh(string name, int vertices) =>
            new() { new MeshGroup { Name = name, Vertices = new MeshVertex[vertices] } };

        [Fact]
        public void RealName_WinsOverLargerSectionIdAlias_InEitherOrder()
        {
            // Port Jeuno's layout: section "saku" holds the mesh "saku2" (284 vertices), section "sak1" the mesh "saku" (264).
            var aliasFirst = new ZoneMeshTemplates();
            var saku2 = Mesh("saku2", 284);
            var saku = Mesh("saku", 264);
            aliasFirst.Register("saku2", saku2, ZoneMeshTemplates.NameRank);
            aliasFirst.Register("saku", saku2, ZoneMeshTemplates.SectionIdRank);
            aliasFirst.Register("saku", saku, ZoneMeshTemplates.NameRank);
            Assert.Same(saku, aliasFirst.Templates["saku"]);

            var nameFirst = new ZoneMeshTemplates();
            nameFirst.Register("saku", saku, ZoneMeshTemplates.NameRank);
            nameFirst.Register("saku", saku2, ZoneMeshTemplates.SectionIdRank);
            Assert.Same(saku, nameFirst.Templates["saku"]);
            Assert.Same(saku, ZoneDefDecoder.ResolveTemplate("saku", nameFirst.Templates, nameFirst.RealNames));
        }

        [Fact]
        public void SameRank_KeepsTheMostDetailedMesh()
        {
            var templates = new ZoneMeshTemplates();
            var small = Mesh("door", 10);
            var large = Mesh("door", 40);
            templates.Register("door", small, ZoneMeshTemplates.NameRank);
            templates.Register("door", large, ZoneMeshTemplates.NameRank);
            Assert.Same(large, templates.Templates["door"]);

            templates.Register("dr01", small, ZoneMeshTemplates.SectionIdRank);
            Assert.DoesNotContain("dr01", templates.RealNames);
            Assert.Contains("door", templates.RealNames);
        }

        [Fact]
        public void PortJeuno_AirshipDockBarriers_UseTheMeshNamedSaku()
        {
            string gameDir = @"G:\Program Files (x86)\PlayOnline\SquareEnix\FINAL FANTASY XI";
            if (!Directory.Exists(gameDir)) return;
            var rm = new Gordian.Core.Resources.ResourceManager(gameDir);
            rm.InitializeFileTable();
            if (!rm.TryLoadZone(246, out var zone, out _)) return;

            // Each barrier (posts and chain) runs along the mesh's local X, which the placement yaw turns onto the dock's
            // curved edge; the section-id alias "saku" (mesh saku2) runs along local Z, across the walkway (#256).
            Assert.Contains(zone.MeshGroups, g => g.Name == "saku_saku");
            Assert.DoesNotContain(zone.MeshGroups, g => g.Name == "saku2_saku");
        }

        [Fact]
        public void AddSection_PrefersTheSectionNamedLikeItsId()
        {
            var sections = new Dictionary<string, List<MeshGroup>>(StringComparer.OrdinalIgnoreCase);
            var cliff = Mesh("umigake01_m", 700);
            var foam = Mesh("umi1", 1873);
            ZoneMeshTemplates.AddSection(sections, "umi1", "umi1", cliff);
            ZoneMeshTemplates.AddSection(sections, "umi1", "umi1", foam);
            Assert.Same(foam, sections["umi1"]);

            // Once a section named like the id is in, later ones keep it; otherwise the first section stays.
            ZoneMeshTemplates.AddSection(sections, "umi1", "umi1", Mesh("umi1", 4));
            Assert.Same(foam, sections["umi1"]);
            var first = Mesh("kig_san1_m", 10);
            ZoneMeshTemplates.AddSection(sections, "kigx", "kigx", first);
            ZoneMeshTemplates.AddSection(sections, "kigx", "kigx", Mesh("other", 99));
            Assert.Same(first, sections["kigx"]);
        }

        [Fact]
        public void SouthGustaberg_WaveGenerators_DrawTheFoamMeshes_NotTheSeaCliffs()
        {
            string gameDir = @"G:\Program Files (x86)\PlayOnline\SquareEnix\FINAL FANTASY XI";
            if (!Directory.Exists(gameDir)) return;
            var rm = new Gordian.Core.Resources.ResourceManager(gameDir);
            rm.InitializeFileTable();
            if (!rm.TryLoadZone(107, out var zone, out _)) return;

            // nam1-nam5 link umi1-umi3: the flat wave-foam meshes (texture umna), not the umigake cliffs (gus_* terrain, #341).
            var waves = zone.EffectLayers.Where(l => l.Name.StartsWith("nam", StringComparison.Ordinal)).ToList();
            Assert.NotEmpty(waves);
            foreach (var layer in waves)
            {
                Assert.All(layer.MeshGroups, g => Assert.Contains("umna", g.TextureName, StringComparison.Ordinal));
            }
        }
    }
}
