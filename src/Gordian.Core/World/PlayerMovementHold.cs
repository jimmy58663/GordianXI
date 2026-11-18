// src/Gordian.Core/World/PlayerMovementHold.cs
using System;
using System.Collections.Generic;

namespace Gordian.Core.World
{
    /// <summary>
    /// How a crowd-control status effect holds the local player (#540). The server does not enforce any of these:
    /// LandSandBoat accepts every C2S 0x015 move (<c>c2s/0x015_pos.cpp</c> checks only Disappear and Shutdown), so the
    /// client holds the player itself. No option (<c>/actionfreeze</c>, a speed override) and no <c>FeatureRestrictions</c>
    /// bit lifts a hold: these are server effects, not control locks.
    /// </summary>
    public enum PlayerMovementHold : byte
    {
        /// <summary>The player moves and turns freely.</summary>
        None = 0,

        /// <summary>
        /// Bind (and the player's own chocobo dig): the player cannot move but can still turn in place. LandSandBoat's <c>scripts/effects/bind.lua</c> sets the base
        /// speed to 0, so S2C 0x037 <c>Speed</c> is 0 (<c>CBattleEntity::UpdateSpeed</c>).
        /// </summary>
        Rooted = 1,

        /// <summary>
        /// Stun, sleep, petrification and terror: no movement and no turning while the effect is on.
        /// </summary>
        Immobile = 2,
    }

    /// <summary>
    /// The status effects that hold the local player and how (#540). Effect ids are from LandSandBoat
    /// (https://github.com/LandSandBoat/server) <c>scripts/enum/effect.codegen.lua</c>.
    /// </summary>
    public static class StatusEffectHolds
    {
        /// <summary><c>xi.effect.SLEEP</c>.</summary>
        public const ushort Sleep = 2;

        /// <summary><c>xi.effect.PETRIFICATION</c>.</summary>
        public const ushort Petrification = 7;

        /// <summary><c>xi.effect.STUN</c>.</summary>
        public const ushort Stun = 10;

        /// <summary><c>xi.effect.BIND</c>.</summary>
        public const ushort Bind = 11;

        /// <summary><c>xi.effect.SLEEP_II</c>.</summary>
        public const ushort SleepII = 19;

        /// <summary><c>xi.effect.TERROR</c>.</summary>
        public const ushort Terror = 28;

        /// <summary><c>xi.effect.LULLABY</c>.</summary>
        public const ushort Lullaby = 193;

        /// <summary>
        /// The chocobo dig routine <c>chor</c>'s length in 60 Hz ticks: rider and chocobo both play <c>chh</c> for 168 ticks
        /// (<c>ROM/90/14</c> <c>hm_c/chor</c>, <c>ROM/169/11</c> <c>cyl_/chor</c>; the engine's <c>MountTransitions.PlayDig</c>).
        /// The player is rooted that long after S2C 0x02F digs with its own chocobo.
        /// </summary>
        public const int DigTicks = 168;

        /// <summary>The hold one status effect puts on the player.</summary>
        public static PlayerMovementHold For(ushort effectId) => effectId switch
        {
            Sleep or SleepII or Lullaby or Petrification or Stun or Terror => PlayerMovementHold.Immobile,
            Bind => PlayerMovementHold.Rooted,
            _ => PlayerMovementHold.None,
        };

        /// <summary>The strongest hold among <paramref name="effectIds"/>.</summary>
        public static PlayerMovementHold Strongest(IEnumerable<ushort> effectIds)
        {
            var hold = PlayerMovementHold.None;
            foreach (ushort id in effectIds)
            {
                var h = For(id);
                if (h > hold) hold = h;
            }
            return hold;
        }

        /// <summary>The stronger of two holds.</summary>
        public static PlayerMovementHold Max(PlayerMovementHold a, PlayerMovementHold b) => (PlayerMovementHold)Math.Max((byte)a, (byte)b);
    }
}
