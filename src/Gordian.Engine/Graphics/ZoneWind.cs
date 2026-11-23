// src/Gordian.Engine/Graphics/ZoneWind.cs
using System;

namespace Gordian.Engine.Graphics
{
    /// <summary>
    /// The zone's wind factor (#606): how far the swaying zone meshes (trees, grass, webs) lean along their per-vertex wind
    /// offsets (<c>MeshGroup.WindDeltas</c>). A Section 0x2E mesh with config bit 1 stores a second vector per vertex and the
    /// client draws it at <c>position + factor * offset</c>, the factor one value shared by every mesh (xi-tools
    /// docs/zone/format.md, https://github.com/vekien/xi-tools; xi-model-viewer ui/js/renderer.js,
    /// https://github.com/vekien/xi-model-viewer).
    /// <para>
    /// **Differs from xi-model-viewer:** its factor (after xim's WindFactor) is a 0-1-0 triangle wave, two seconds each way.
    /// Retail's is not (round 2, the maintainer's Valkurm Dunes video of the palms outside Selbina, 2026-11-23): one cycle
    /// takes about 5.15 s and is a gust, not a steady swing. The palm leans out to full over about 1.9 s and holds, relaxes
    /// half way, leans out again to nearly full and then settles back to rest. <see cref="Factor"/> is that cycle as four
    /// harmonics fitted to the crowns' tracked motion; docs/rendering/viewport-and-terrain.md "Wind sway" has the
    /// measurement. No reference ties it to the weather.
    /// </para>
    /// </summary>
    public static class ZoneWind
    {
        /// <summary>Seconds per gust cycle, from the retail video (autocorrelation and folding agree to 0.02 s).</summary>
        public const double PeriodSeconds = 5.15;

        /// <summary>Seconds into a cycle at which the meshes lean out the furthest (factor 1).</summary>
        public const double PeakSeconds = 0.375 * PeriodSeconds;

        // f(phase) = Mean + sum over k of Cos[k] cos(2 pi k phase) + Sin[k] sin(2 pi k phase): the fit to the video, scaled
        // so one cycle runs from 0 (the authored pose, at phase 0) to 1.
        private const double Mean = 0.5811;
        private static readonly double[] Cos = { -0.3292, -0.2476, 0.0147, -0.0191 };
        private static readonly double[] Sin = { 0.0804, -0.0490, 0.0719, -0.0495 };

        /// <summary>The wind factor at a time on a running clock (seconds): 0 at the start of each cycle, 1 at <see cref="PeakSeconds"/>.</summary>
        public static float Factor(double seconds)
        {
            if (!double.IsFinite(seconds)) return 0.0f;
            double phase = seconds / PeriodSeconds % 1.0;
            if (phase < 0.0) phase += 1.0;
            double f = Mean;
            for (int k = 0; k < Cos.Length; k++)
            {
                double angle = 2.0 * Math.PI * (k + 1) * phase;
                f += Cos[k] * Math.Cos(angle) + Sin[k] * Math.Sin(angle);
            }
            return (float)Math.Clamp(f, 0.0, 1.0);
        }
    }
}
