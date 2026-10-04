// src/Gordian.App/Graphics/ZoneSubmeshPass.cs
namespace Gordian.App.Graphics
{
    /// <summary>
    /// Where and how <see cref="ZoneTerrainRenderer"/> draws a placed zone submesh.
    /// </summary>
    public enum ZoneSubmeshPass
    {
        /// <summary>Pass 1, authored order: solid, texture alpha ignored, depth written.</summary>
        Opaque,

        /// <summary>Pass 1, authored order: alpha-tested (mesh name starting <c>_</c>), depth written.</summary>
        Cutout,

        /// <summary>Pass 1, authored order: alpha-blended overlay (0x8000), depth tested, not written.</summary>
        BlendDecal,

        /// <summary>Pass 3, after entities: an alpha-blended (0x8000) submesh of an alpha-tested (<c>_</c>) mesh.</summary>
        DeferredBlend,

        /// <summary>Pass 3, after entities: an alpha-blended (0x8000) water surface, depth tested, not written.</summary>
        Water
    }

    public static class ZoneSubmeshPasses
    {
        /// <summary>
        /// Classifies a zone submesh by the client's render state. Only the 0x8000 blend flag makes a submesh
        /// translucent; without it the submesh is solid and writes depth (alpha-tested when its mesh name starts with
        /// <c>_</c>), whatever its name says, so a name-based water hint applies to blended submeshes only (#251).
        /// Render state referenced from xi-tools (docs/zone/format.md, docs/zone/export.md) and xi-model-viewer
        /// (ui/js/zoneModel.js, after xim GLDrawer.drawXim).
        /// </summary>
        public static ZoneSubmeshPass Classify(bool isBlend, bool isFoliage, bool isWater)
        {
            if (!isBlend) return isFoliage ? ZoneSubmeshPass.Cutout : ZoneSubmeshPass.Opaque;
            if (isWater) return ZoneSubmeshPass.Water;
            return isFoliage ? ZoneSubmeshPass.DeferredBlend : ZoneSubmeshPass.BlendDecal;
        }

        /// <summary>True for the passes drawn after the entities (Pass 3).</summary>
        public static bool IsDeferred(this ZoneSubmeshPass pass) =>
            pass is ZoneSubmeshPass.Water or ZoneSubmeshPass.DeferredBlend;
    }
}
