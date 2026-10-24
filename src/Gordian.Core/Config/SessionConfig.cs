// src/Gordian.Core/Config/SessionConfig.cs
using System;
using System.Threading;
using Gordian.Core.Network.Packets;

namespace Gordian.Core.Config
{
    /// <summary>
    /// Bitflag set of client capabilities the connected server has restricted for this session.
    /// A set bit blocks the corresponding capability; unset bits remain permissive (None = fully allowed).
    /// Sourced exclusively from the server-authoritative S2C 0x0EE packet — see
    /// <see cref="Gordian.Core.Network.Packets.S2C_0x0EE_FeatureRestrictions"/>.
    /// </summary>
    [Flags]
    public enum FeatureRestrictions : ulong
    {
        /// <summary>No restrictions. Every capability below is permitted.</summary>
        None = 0,

        /// <summary>Blocks automated combat action injection (Gambit rotations, GearSwap-style reactions).</summary>
        Combat = 1UL << 0,

        /// <summary>Blocks synthetic movement/pathing (e.g. /moveto).</summary>
        Movement = 1UL << 1,

        /// <summary>Blocks client-side run speed tampering (SpeedOverride / SpeedMultiplier).</summary>
        SpeedOverride = 1UL << 2,

        /// <summary>Blocks the low-level raw outgoing packet injection escape hatch.</summary>
        RawPacketInjection = 1UL << 3,

        /// <summary>Blocks exporting live session/world state to addons or external processes.</summary>
        ReadGameState = 1UL << 4,

        /// <summary>Blocks the addon scripting runtime from loading or executing any script.</summary>
        Addons = 1UL << 5,

        /// <summary>Blocks turning off ground and wall collision (walking through walls).</summary>
        WallCollisionOverride = 1UL << 6,

        /// <summary>Blocks turning off collision with NPCs and monsters.</summary>
        EntityCollisionOverride = 1UL << 7,

        /// <summary>Blocks ignoring knockback (the Anchor option): the server's knockback always moves the character.</summary>
        KnockbackOverride = 1UL << 8
    }

    /// <summary>
    /// Represents the isolated, instance-level configuration matrix for a single character session.
    /// REMOVED 'static' keyword to guarantee 6-box data isolation.
    /// </summary>
    public sealed class SessionProfile
    {
        private ulong _featureRestrictions;

        /// <summary>
        /// Gets the server-enforced feature restrictions specific to this character instance. Write-once per bit: only
        /// <see cref="ApplyServerRestrictions"/> changes it, and only by adding restrictions, so nothing can lift a
        /// restriction the server set for the life of the session (AGENTS.md kill-switch policy).
        /// </summary>
        public FeatureRestrictions FeatureRestrictions => (FeatureRestrictions)Volatile.Read(ref _featureRestrictions);

        /// <summary>
        /// Adds the restrictions a decoded S2C 0x0EE carries. Restrictions already set stay set: a later packet can tighten
        /// the session but never loosen it, so a server lifting a restriction takes effect at the next login. The only
        /// production caller is the 0x0EE handler in <see cref="Gordian.Core.Network.Packets.LifecyclePacketModule"/>, and
        /// <c>LayeringTests.FeatureRestrictionsAreWrittenOnlyByTheServerPacket</c> fails if another is added. Taking the
        /// decoded packet rather than a flag value keeps it from being a general-purpose setter.
        /// </summary>
        internal void ApplyServerRestrictions(in S2C_0x0EE_FeatureRestrictions packet)
        {
            if (!packet.IsValid) return;
            Interlocked.Or(ref _featureRestrictions, (ulong)packet.Value);
        }

        /// <summary>
        /// Returns whether the given capability is currently blocked by the server's feature restrictions.
        /// </summary>
        public bool IsRestricted(FeatureRestrictions flag) => (FeatureRestrictions & flag) != 0;

        public bool ShowNativePartyList { get; set; } = true;
        public bool ShowNativeAllianceList { get; set; } = true;
        public bool ShowNativeMenu { get; set; } = true;
    }
}
