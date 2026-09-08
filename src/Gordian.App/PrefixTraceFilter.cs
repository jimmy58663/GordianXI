// src/Gordian.App/PrefixTraceFilter.cs
using System;
using System.Diagnostics;

namespace Gordian.App
{
    /// <summary>
    /// Filters process-wide trace data based on string token prefixes to achieve clean log isolation.
    /// </summary>
    public sealed class PrefixTraceFilter : TraceFilter
    {
        private readonly string _requiredPrefix;
        private readonly bool _rejectIfMatch;

        /// <summary>
        /// Initializes a new prefix monitoring filter ruleset.
        /// </summary>
        /// <param name="requiredPrefix">The token string token to look for (e.g., "[NET_TRACE]").</param>
        /// <param name="rejectIfMatch">If true, matching lines are hidden (ideal for the system log).</param>
        public PrefixTraceFilter(string requiredPrefix, bool rejectIfMatch = false)
        {
            _requiredPrefix = requiredPrefix ?? throw new ArgumentNullException(nameof(requiredPrefix));
            _rejectIfMatch = rejectIfMatch;
        }

        public override bool ShouldTrace(TraceEventCache? cache, string source, TraceEventType eventType, 
            int id, string? formatOrMessage, object?[]? args, object? data1, object?[]? data2)
        {
            if (string.IsNullOrEmpty(formatOrMessage)) return true;

            bool hasPrefix = formatOrMessage.StartsWith(_requiredPrefix, StringComparison.OrdinalIgnoreCase);

            if (_rejectIfMatch)
            {
                // For the system log: hide it if it has the network prefix
                return !hasPrefix;
            }

            // For the network log: only allow it if it has the network prefix
            return hasPrefix;
        }
    }
}
