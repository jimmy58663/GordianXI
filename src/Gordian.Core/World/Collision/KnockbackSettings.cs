// src/Gordian.Core/World/Collision/KnockbackSettings.cs
using System;
using Gordian.Core.Config;

namespace Gordian.Core.World.Collision
{
    /// <summary>
    /// One knockback level's slide: the push per 60 Hz tick (yalms), the fraction of speed lost each tick, and how many ticks
    /// it lasts.
    /// </summary>
    public readonly record struct KnockbackProfile(float PushPerTick, float Damper, float Ticks)
    {
        /// <summary>The distance the full slide covers on open ground (yalms).</summary>
        public float Distance
        {
            get
            {
                float keep = 1f - Damper;
                return keep >= 1f ? PushPerTick * Ticks : PushPerTick * (1f - MathF.Pow(keep, Ticks)) / Damper;
            }
        }
    }

    /// <summary>
    /// The session's knockback option: the Anchor toggle (<c>/anchor</c>, the built-in form of the Windower addon of that
    /// name) ignores the knockback in S2C 0x028 results, unless the server's <see cref="FeatureRestrictions.KnockbackOverride"/>
    /// bit forces knockback on. Off by default for legacy parity.
    /// <para>
    /// Knockback levels 1-7 come from the upper three bits of a result's <c>scale</c>; their push / damper / timer values are
    /// referenced from LandSandBoat (https://github.com/LandSandBoat/server, src/map/enums/action/knockback.h), matching the
    /// client table in XiPackets (world/server/0x0028, <c>scale</c>). How the client integrates them is not documented:
    /// GordianXI moves the push each 60 Hz tick, losing the damper fraction of it per tick, for the timer's ticks (level 1
    /// about 0.4 yalm, level 4 about 1 yalm, level 7 about 3 yalms). To be calibrated in game.
    /// </para>
    /// </summary>
    public sealed class KnockbackSettings
    {
        private static readonly KnockbackProfile[] Levels =
        [
            new(0f, 0f, 0f),
            new(0.083333336f, 0.075f, 5f),
            new(0.16666667f, 0.15f, 5f),
            new(0.16666667f, 0.15f, 10f),
            new(0.16666667f, 0.15f, 18f),
            new(0.16666667f, 0.125f, 30f),
            new(0.16666667f, 0.1f, 35f),
            new(0.16666667f, 0.05f, 45f),
        ];

        /// <summary>Whether the player asked to ignore knockback.</summary>
        public bool AnchorRequested { get; set; }

        /// <summary>Whether the server forbids ignoring knockback.</summary>
        public static bool IsAnchorLocked(SessionProfile? profile) => profile?.IsRestricted(FeatureRestrictions.KnockbackOverride) ?? false;

        /// <summary>Whether knockback is ignored now: asked for and not forbidden by the server.</summary>
        public bool IsAnchored(SessionProfile? profile) => AnchorRequested && !IsAnchorLocked(profile);

        /// <summary>The slide for a knockback level (0 or out of range: none).</summary>
        public static KnockbackProfile ProfileOf(int level) => level > 0 && level < Levels.Length ? Levels[level] : Levels[0];
    }
}
