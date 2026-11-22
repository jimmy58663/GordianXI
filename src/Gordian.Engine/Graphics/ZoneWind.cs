// src/Gordian.Engine/Graphics/ZoneWind.cs
using System;

namespace Gordian.Engine.Graphics
{
    /// <summary>
    /// The zone's wind factor (#606): how far the swaying zone meshes (trees, grass, webs) lean along their per-vertex wind
    /// offsets (<c>MeshGroup.WindDeltas</c>). A Section 0x2E mesh with config bit 1 stores a second vector per vertex and the
    /// client draws it at <c>position + factor * offset</c>; the factor is one value shared by every mesh, a triangle wave
    /// from 0 to 1 and back, two seconds each way (xim's WindFactor, stepped 1/60 per 30 fps game frame; ported in
    /// xi-model-viewer ui/js/renderer.js, https://github.com/vekien/xi-model-viewer, and described in xi-tools
    /// docs/zone/format.md, https://github.com/vekien/xi-tools). No reference ties it to the weather; see
    /// docs/rendering/viewport-and-terrain.md, "Wind sway".
    /// </summary>
    public static class ZoneWind
    {
        /// <summary>Seconds for one leg of the wave (0 to 1, or 1 back to 0).</summary>
        public const double LegSeconds = 2.0;

        /// <summary>The wind factor at a time on a running clock (seconds): 0 at 0, 1 at <see cref="LegSeconds"/>, 0 again at twice it.</summary>
        public static float Factor(double seconds)
        {
            if (!double.IsFinite(seconds)) return 0.0f;
            double phase = seconds / LegSeconds % 2.0;
            if (phase < 0.0) phase += 2.0;
            return (float)(phase <= 1.0 ? phase : 2.0 - phase);
        }
    }
}
