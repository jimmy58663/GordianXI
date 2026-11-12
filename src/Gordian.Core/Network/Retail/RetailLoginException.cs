// src/Gordian.Core/Network/Retail/RetailLoginException.cs
using System;

namespace Gordian.Core.Network.Retail
{
    /// <summary>A retail login stage failed: the server refused, answered something unexpected, or the spec leaves a gap.</summary>
    public sealed class RetailLoginException : Exception
    {
        /// <summary>The stage that failed ("version check", "core session", "account", "session key", "lobby").</summary>
        public string Stage { get; }

        /// <summary>The server's error code when it sent one (an IRC numeric, a <c>:51220</c> status), else 0.</summary>
        public int Code { get; }

        public RetailLoginException(string stage, string message, int code = 0, Exception? inner = null)
            : base($"Retail login ({stage}): {message}", inner)
        {
            Stage = stage;
            Code = code;
        }
    }
}
