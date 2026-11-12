// src/Gordian.Core/Animation/ActionFreezeSettings.cs
using System;
using Gordian.Core.Config;

namespace Gordian.Core.Animation
{
    /// <summary>
    /// What started a control lock (routine op 0x2E) on the local player, so the player can ignore one kind and keep the
    /// other. Every control lock comes from a routine the player's own character plays as the caster: the 0x2E command locks
    /// the caster only, so another actor's action never freezes the player through it.
    /// </summary>
    [Flags]
    public enum ActionFreezeSource : byte
    {
        None = 0,

        /// <summary>
        /// The player's own actions: a job ability, weapon skill or spell presentation's <c>main</c>, a spell, item or ranged
        /// release (the shared <c>wash</c> / <c>waso</c>), an item use's start (<c>cait</c>), and any other routine played on
        /// the character (an actor scheduler, S2C 0x038).
        /// </summary>
        Actions = 1 << 0,

        /// <summary>Drawing or sheathing the weapons on engage and disengage (<c>in 0</c> / <c>out0</c>, 72 ticks).</summary>
        Engage = 1 << 1,

        All = Actions | Engage
    }

    /// <summary>
    /// The session's action freeze option (#511): retail holds the player in place while one of its own routines carries a
    /// control lock, and <c>/actionfreeze off [actions|engage]</c> ignores them, as JaZero-style plugins did. On by default for
    /// legacy parity. The server's <see cref="FeatureRestrictions.ActionFreezeOverride"/> bit keeps every freeze on whatever
    /// the player asks. A server freeze (S2C 0x037 FreezeFlag) is not a control lock and no option lifts it.
    /// </summary>
    public sealed class ActionFreezeSettings
    {
        /// <summary>The freeze sources the player asked to ignore.</summary>
        public ActionFreezeSource IgnoreRequested { get; set; }

        /// <summary>Whether the server forbids ignoring action freezes.</summary>
        public static bool IsLocked(SessionProfile? profile) => profile?.IsRestricted(FeatureRestrictions.ActionFreezeOverride) ?? false;

        /// <summary>The sources ignored now: those asked for, or none when the server forbids it.</summary>
        public ActionFreezeSource GetIgnored(SessionProfile? profile) => IsLocked(profile) ? ActionFreezeSource.None : IgnoreRequested;
    }
}
