namespace Gordian.Core.Diagnostics
{
    /// <summary>
    /// A named context, normally one character, that tags every <see cref="GordianLog"/> line written while it is active
    /// (<c>[Charname]</c>), so a multi-box log can be read per character. The owner keeps one instance and updates
    /// <see cref="Name"/> when it learns the name; lines take the name at the moment they are logged.
    /// </summary>
    /// <remarks>
    /// Activate it for synchronous work with <see cref="GordianLog.BeginScope"/>, or for a whole async loop with
    /// <see cref="GordianLog.SetFlowScope"/>.
    /// </remarks>
    public sealed class LogScope
    {
        private volatile string? _name;

        /// <summary>Creates a scope with an optional initial name.</summary>
        public LogScope(string? name = null) => Name = name;

        /// <summary>The tag written on each line; null or blank writes no tag.</summary>
        public string? Name
        {
            get => _name;
            set => _name = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }
    }
}
