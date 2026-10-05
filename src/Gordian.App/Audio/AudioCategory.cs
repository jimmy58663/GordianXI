// src/Gordian.App/Audio/AudioCategory.cs
namespace Gordian.App.Audio
{
    /// <summary>
    /// The mixer buses. They follow the retail sound categories: the event VM's volume opcodes 0x69 / 0x6A address
    /// effects (0x01), system (0x02), zone (0x04) and master (0x08) by mask (XiEvents <c>OpCodes/0x0069</c>), and the
    /// config page has separate music and sound effect volumes.
    /// </summary>
    public enum AudioCategory
    {
        /// <summary>Background music (zone, battle, event tracks).</summary>
        Music = 0,

        /// <summary>Sound effects of actors: footsteps, combat, spells, abilities, cries.</summary>
        Effects = 1,

        /// <summary>System sounds: menu cursor, confirm, cancel, targeting, chat cues.</summary>
        System = 2,

        /// <summary>Zone sounds: ambient loops and the zone's effect generators (wind, water, fires).</summary>
        Zone = 3,
    }
}
