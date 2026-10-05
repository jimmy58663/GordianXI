// src/Gordian.Core/World/PlayerSizeScale.cs
namespace Gordian.Core.World
{
    /// <summary>
    /// The model scale of a player character's size (small / medium / large): the lobby's size choice
    /// (<c>TC_OPERATION_MAKE.size</c>, LandSandBoat <c>char_look.size</c>) and the entity update's <c>GraphSize</c>
    /// (XiPackets world/server/0x000D: 0 small, 1 medium, 2 large). The client scales the whole model uniformly.
    /// <para>
    /// <b>Guessed:</b> the values are not in the reference sources or a DAT found so far; they are a few percent either way
    /// of medium so the three sizes read apart. Settle with a retail capture of one race at the three sizes (head height
    /// against the same wall). See docs/reference/calibrations.md.
    /// </para>
    /// </summary>
    public static class PlayerSizeScale
    {
        public const float Small = 0.94f, Medium = 1.0f, Large = 1.06f;

        /// <summary>The scale of a size class; anything other than 0 or 2 is medium.</summary>
        public static float For(byte size) => size switch
        {
            0 => Small,
            2 => Large,
            _ => Medium,
        };
    }
}
