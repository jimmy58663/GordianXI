// src/Gordian.Core/Config/SessionConfig.cs
using System;

namespace Gordian.Core.Config
{
    /// <summary>
    /// Dictates the automation restrictions commanded directly by the connected server.
    /// </summary>
    public enum ServerAutomationPolicy : byte
    {
        AllowAll = 0,          // Full Gambit processing and automation allowed
        DisableCombat = 1,     // Allow auto-follow/positioning, but block automated action injection
        StrictVanilla = 2      // Completely kill and short-circuit the automation processing thread
    }

    /// <summary>
    /// Represents the isolated, instance-level configuration matrix for a single character session.
    /// REMOVED 'static' keyword to guarantee 6-box data isolation.
    /// </summary>
    public sealed class SessionProfile
    {
        /// <summary>
        /// Gets the server-enforced automation policy specific to this character instance.
        /// </summary>
        public ServerAutomationPolicy AutomationPolicy { get; internal set; } = ServerAutomationPolicy.AllowAll;

        public bool ShowNativePartyList { get; set; } = true;
        public bool ShowNativeAllianceList { get; set; } = true;
        public bool ShowNativeMenu { get; set; } = true;
    }
}
