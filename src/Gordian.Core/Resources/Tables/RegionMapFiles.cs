// src/Gordian.Core/Resources/Tables/RegionMapFiles.cs
using System.IO;
using Gordian.Core.Resources.Graphics;

namespace Gordian.Core.Resources.Tables
{
    /// <summary>The world maps behind Region Info's pages.</summary>
    public enum RegionMap
    {
        /// <summary>The Middle Lands (Conquest).</summary>
        Conquest,

        /// <summary>Aht Urhgan (Besieged).</summary>
        Besieged,

        /// <summary>The Middle Lands of the Crystal War (Campaign).</summary>
        Campaign,

        /// <summary>Ulbuka (Colonization).</summary>
        Colonization,
    }

    /// <summary>
    /// The region maps Region Info draws behind its pages (#488): one DAT each, laid out like a zone map
    /// (<see cref="ZoneMapFiles"/>: a 512 x 512 texture, Section 0x20, and a one-part 0x31 group placing it at (0, 0)). Found
    /// by reading every 150 KB-6 MB DAT of the retail install for <c>menumap</c> textures not named after a zone
    /// (2026-11-01): <c>menumap conquest</c> (ROM/17/23, the Middle Lands), <c>m_ex3_rg</c> (ROM/161/34, Aht Urhgan's
    /// islands), <c>m_ex4_rg</c> (ROM/195/94, the Middle Lands again, drawn for the Crystal War) and <c>m_ex5_rg</c>
    /// (ROM/292/107, Ulbuka). **Beyond xi-tools**, which lists ROM/17 as zone maps only. Which page shows which file is our
    /// reading of the names and the maintainer's retail screenshots (Besieged and Colonization, round 1 of #488); the
    /// world-to-map placement of the regions on them is not in these files.
    /// </summary>
    public static class RegionMapFiles
    {
        /// <summary>The DAT path of a region map (<c>ROM/17/23.DAT</c> for Conquest).</summary>
        public static string PathOf(RegionMap map)
        {
            var (folder, file) = map switch
            {
                RegionMap.Conquest => (17, 23),
                RegionMap.Besieged => (161, 34),
                RegionMap.Campaign => (195, 94),
                _ => (292, 107),
            };
            return Path.Combine("ROM", folder.ToString(System.Globalization.CultureInfo.InvariantCulture),
                file.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".DAT");
        }

        /// <summary>Decodes a region map's texture, or null when its DAT is missing.</summary>
        public static DecodedTexture? Load(ResourceManager resources, RegionMap map)
        {
            string path = PathOf(map);
            return resources.LoadDatBytes(path) is { } dat ? ZoneMapFiles.Decode(dat, path) : null;
        }
    }
}
