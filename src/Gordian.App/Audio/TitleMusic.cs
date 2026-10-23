// src/Gordian.App/Audio/TitleMusic.cs
using System;

namespace Gordian.App.Audio
{
    /// <summary>The lobby Config window's Title Music choices (ROM/97/36 entries 251-257).</summary>
    public enum TitleMusicOption
    {
        /// <summary>"The title song for the most recent version of the game installed" (251).</summary>
        Latest,
        /// <summary>"Vana'diel March" from the base game and Rise of the Zilart (252).</summary>
        VanadielMarch,
        /// <summary>"Unity" from Chains of Promathia (253).</summary>
        Unity,
        /// <summary>"Vana'diel March #4" from Treasures of Aht Urhgan (254).</summary>
        VanadielMarch4,
        /// <summary>"Wings of the Goddess" (255).</summary>
        WingsOfTheGoddess,
        /// <summary>"A New Direction" from Seekers of Adoulin (256).</summary>
        ANewDirection,
        /// <summary>"At random from the various title songs for each version of the game installed" (257).</summary>
        Random,
    }

    /// <summary>
    /// The title music the character lobby plays (#378). The client picks it itself: no title scene or UI DAT names a
    /// sound (xi-tools <c>docs/dats/ROM_0_23.md</c>), and its lobby Config window offers the choices of
    /// <see cref="TitleMusicOption"/> (labels read from the retail ROM/97/36, 2026-10-20).
    /// </summary>
    /// <remarks>
    /// Track ids referenced from LandSandBoat (https://github.com/LandSandBoat/server, <c>docs/MusicIDs.txt</c>: 108 "Title
    /// Screen 1 (Original+Zilart)", 242 "Title Screen 2 (CoP)", 179 "Title Screen 3 (ToAU)", 140 "Title Screen 4 (WotG)", 58
    /// "Title Screen 5 (Seekers)"). Provisional, for a retail check: that the default is <see cref="TitleMusicOption.Latest"/>
    /// and that with every expansion installed it plays "A New Direction". Option 270, "We Are Vana'diel", is not offered:
    /// no reference gives its track id.
    /// </remarks>
    public static class TitleMusic
    {
        public const int VanadielMarch = 108;
        public const int Unity = 242;
        public const int VanadielMarch4 = 179;
        public const int WingsOfTheGoddess = 140;
        public const int ANewDirection = 58;

        /// <summary>The expansions' title songs, oldest first.</summary>
        public static readonly int[] Tracks = [VanadielMarch, Unity, VanadielMarch4, WingsOfTheGoddess, ANewDirection];

        /// <summary>The track an option plays (<paramref name="random"/> picks for <see cref="TitleMusicOption.Random"/>).</summary>
        public static int Track(TitleMusicOption option, Random random)
        {
            ArgumentNullException.ThrowIfNull(random);
            return option switch
            {
                TitleMusicOption.VanadielMarch => VanadielMarch,
                TitleMusicOption.Unity => Unity,
                TitleMusicOption.VanadielMarch4 => VanadielMarch4,
                TitleMusicOption.WingsOfTheGoddess => WingsOfTheGoddess,
                TitleMusicOption.ANewDirection => ANewDirection,
                TitleMusicOption.Random => Tracks[random.Next(Tracks.Length)],
                _ => Tracks[^1],
            };
        }
    }
}
