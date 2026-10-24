// src/Gordian.Core/Graphics/ZoneFootEffects.cs
using System;
using System.Collections.Generic;
using System.Globalization;
using Gordian.Core.Resources.Graphics;

namespace Gordian.Core.Graphics
{
    /// <summary>
    /// A zone's foot effects (#40): the generators the client starts where a foot lands, by the terrain under it. Each
    /// zone DAT carries two sets beside its footstep sound pointers: <c>&lt;area&gt;/fses/fefs</c> for dry ground and
    /// <c>&lt;area&gt;/fser/fefr</c> for the rain (#342). A set's roots are named <c>0&lt;terrain hex&gt;00</c>
    /// (xi-tools <c>docs/sounds/footsteps.md</c> §2, after xim <c>TerrainType.toFootEffectId</c>); most are an invisible
    /// 5-frame carrier that spawns the visible particles as children (Bibiki Bay's sand <c>0300</c>: the dust puff
    /// <c>a301</c> at birth, the sparkle stream <c>a302</c>).
    /// </summary>
    /// <remarks>
    /// Census of zone DATs 0-299 (2026-10-20): <c>fefs</c> roots for sand <c>0300</c>, snow <c>0400</c> and deep water
    /// <c>0900</c> in 299 zones, shallow water <c>0800</c> in 292, path <c>0100</c> in 70, metal <c>0600</c> in 16, grass
    /// <c>0200</c> in 11; <c>fefr</c> has path in 288 and grass in 281. No zone has a root for Object, Stone, Wood or terrain
    /// 10. **Beyond xi-tools:** the <c>fefr</c> rain set and the census.
    /// </remarks>
    public sealed class ZoneFootEffects
    {
        private readonly Dictionary<int, WeatherSkyLayer> _dry;
        private readonly Dictionary<int, WeatherSkyLayer> _wet;

        public ZoneFootEffects(ActorEffectSet effects, Dictionary<int, WeatherSkyLayer> dry, Dictionary<int, WeatherSkyLayer> wet)
        {
            Effects = effects ?? throw new ArgumentNullException(nameof(effects));
            _dry = dry ?? throw new ArgumentNullException(nameof(dry));
            _wet = wet ?? throw new ArgumentNullException(nameof(wet));
        }

        /// <summary>Every foot-effect layer of the zone (roots and their children) with the textures they sample.</summary>
        public ActorEffectSet Effects { get; }

        /// <summary>The dry-ground roots by terrain index.</summary>
        public IReadOnlyDictionary<int, WeatherSkyLayer> Dry => _dry;

        /// <summary>The rain roots by terrain index.</summary>
        public IReadOnlyDictionary<int, WeatherSkyLayer> Wet => _wet;

        /// <summary>
        /// The root for a foot landing on <paramref name="terrain"/>, from the rain set when <paramref name="wet"/> (falling
        /// back to the dry set), else the dry set; null when the zone has none.
        /// </summary>
        public WeatherSkyLayer? RootFor(int terrain, bool wet)
        {
            if (wet && _wet.TryGetValue(terrain, out var rain)) return rain;
            return _dry.TryGetValue(terrain, out var dry) ? dry : null;
        }

        /// <summary>The terrain a root's name addresses (<c>0300</c> → 3), or -1 for any other generator name.</summary>
        public static int TerrainOfRoot(string generatorId)
        {
            if (generatorId is not { Length: 4 } || generatorId[0] != '0' || generatorId[2] != '0' || generatorId[3] != '0') return -1;
            return int.TryParse(generatorId.AsSpan(1, 1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int terrain) ? terrain : -1;
        }

        /// <summary>Whether a generator directory path is a dry (<c>…/fses/fefs</c>) or rain (<c>…/fser/fefr</c>) foot-effect set.</summary>
        public static bool IsFootEffectDirectory(string? path, out bool wet)
        {
            wet = false;
            if (string.IsNullOrEmpty(path)) return false;
            if (path.EndsWith("fses/fefs", StringComparison.OrdinalIgnoreCase)) return true;
            wet = path.EndsWith("fser/fefr", StringComparison.OrdinalIgnoreCase);
            return wet;
        }
    }
}
