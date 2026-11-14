namespace Gordian.Core.Diagnostics
{
    /// <summary>
    /// Severity of a <see cref="GordianLog"/> record, lowest first. A category's minimum level (see
    /// <see cref="LoggingSettings"/>) drops every record below it before its message is formatted.
    /// </summary>
    public enum LogLevel
    {
        /// <summary>Very high-frequency detail (per frame, per packet byte). Off unless a category asks for it.</summary>
        Trace = 0,

        /// <summary>Developer detail. On by default in Debug builds or with <c>GORDIAN_DEBUG=1</c>.</summary>
        Debug = 1,

        /// <summary>Normal milestones: login steps, zone changes, resources loaded.</summary>
        Info = 2,

        /// <summary>Something unexpected that the client recovered from.</summary>
        Warn = 3,

        /// <summary>A failure; carries the full exception when there is one.</summary>
        Error = 4,

        /// <summary>
        /// A message shown to the user (the Profiles and Launch status box). Never filtered, and written only to the
        /// per-launch status file, not the system log.
        /// </summary>
        Status = 5,

        /// <summary>As a category's minimum level: drop everything from that category except status messages.</summary>
        Off = 6,
    }
}
