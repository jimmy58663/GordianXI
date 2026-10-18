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
    }
}
